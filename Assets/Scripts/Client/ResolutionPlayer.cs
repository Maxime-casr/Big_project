using System;
using System.Collections;
using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using BigProject.Core.Resolution;
using BigProject.Core.Session;
using BigProject.Core.Units;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Rejoue un RoundResult. Chaque action dure <see cref="PlaybackTiming.ActionSeconds"/> (0,8 s), avec
    /// <see cref="PlaybackTiming.PauseSeconds"/> (0,2 s) de pause entre deux actions. Les beats d'une même action
    /// (déplacement case par case) se partagent ses 0,8 s ; les événements d'un même beat s'animent en parallèle.
    /// Ne décide jamais du résultat, il le met seulement en scène.
    /// </summary>
    public sealed class ResolutionPlayer : MonoBehaviour
    {
        // Les impacts (dégâts, soins, mort) arrivent à cette fraction du beat : le lancé est avant, l'effet après.
        private const float ImpactFraction = 0.6f;
        private const float FlashSeconds = 0.25f;
        private const float DeathShrinkSeconds = 0.3f;
        private const float MaxFinishWaitSeconds = 1.5f;

        private UnitViewRegistry units;
        private BoardView board;
        private TileHighlighter highlighter;
        private int runningAnimations;
        private int currentStep = -1;
        private readonly List<GridPosition> zoneCells = new List<GridPosition>();

        public bool IsPlaying { get; private set; }

        public event Action PlaybackFinished;

        public void Init(UnitViewRegistry unitViews, BoardView boardView, TileHighlighter tileHighlighter)
        {
            units = unitViews;
            board = boardView;
            highlighter = tileHighlighter;
        }

        public void Play(RoundResult result, MatchState finalState)
        {
            if (IsPlaying)
            {
                return;
            }

            IsPlaying = true;
            StartCoroutine(PlayRoutine(result, finalState));
        }

        private IEnumerator PlayRoutine(RoundResult result, MatchState finalState)
        {
            currentStep = -1;
            zoneCells.Clear();

            // Les unités invoquées pendant le tour existent déjà dans l'état final : leurs vues sont créées
            // tout de suite (cachées) pour que les événements suivants puissent les retrouver.
            foreach (ResolutionBeat beat in result.Beats)
            {
                foreach (GameEvent gameEvent in beat.Events)
                {
                    if (gameEvent is UnitSummonedEvent summoned)
                    {
                        UnitState unit = finalState.GetUnit(summoned.UnitId);
                        if (unit != null)
                        {
                            units.EnsureView(unit).gameObject.SetActive(false);
                        }
                    }
                }
            }

            int index = 0;
            while (index < result.Beats.Count)
            {
                // Tous les beats de l'action en cours.
                int step = result.Beats[index].StepIndex;
                int end = index;
                while (end < result.Beats.Count && result.Beats[end].StepIndex == step)
                {
                    end++;
                }

                float beatSeconds = PlaybackTiming.ActionSeconds / (end - index);
                for (int i = index; i < end; i++)
                {
                    yield return PlayBeat(result.Beats[i], beatSeconds);
                }

                // Fin de l'action : les animations ponctuelles encore en cours sont coupées.
                units.InterruptAnimations();
                highlighter.Clear(HighlightLayer.Flash);

                index = end;
                if (index < result.Beats.Count)
                {
                    yield return new WaitForSeconds(PlaybackTiming.PauseSeconds);
                }
            }

            // Les mouvements de fin d'action ont pris leur temps : on laisse finir ce qui dépasse un peu.
            float waited = 0f;
            while (runningAnimations > 0 && waited < MaxFinishWaitSeconds)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            units.SyncAll(finalState);
            IsPlaying = false;
            PlaybackFinished?.Invoke();
        }

        private IEnumerator PlayBeat(ResolutionBeat beat, float seconds)
        {
            // La zone d'un sort reste affichée pendant toute l'action : lancé au début, il touche à la fin.
            if (beat.StepIndex != currentStep)
            {
                currentStep = beat.StepIndex;
                zoneCells.Clear();
            }

            foreach (GameEvent gameEvent in beat.Events)
            {
                if (gameEvent is SpellCastEvent cast)
                {
                    zoneCells.AddRange(cast.AffectedCells);
                }
                else if (gameEvent is SpellImpactEvent impact)
                {
                    zoneCells.AddRange(impact.Cells);
                }
            }

            highlighter.Set(HighlightLayer.Flash, zoneCells);

            foreach (GameEvent gameEvent in beat.Events)
            {
                IEnumerator animation = Animate(gameEvent, seconds);
                if (animation != null)
                {
                    StartCoroutine(Track(animation));
                }
            }

            // Durée fixe : le beat finit à l'heure, même si une animation est plus longue.
            for (float elapsed = 0f; elapsed < seconds; elapsed += Time.deltaTime)
            {
                yield return null;
            }
        }

        private IEnumerator Track(IEnumerator animation)
        {
            runningAnimations++;
            yield return animation;
            runningAnimations--;
        }

        private IEnumerator Animate(GameEvent gameEvent, float seconds)
        {
            switch (gameEvent)
            {
                case UnitMovedEvent moved:
                    return units.Get(moved.UnitId).MoveTo(board.CellToWorld(moved.To), seconds);

                case UnitTeleportedEvent teleported:
                    return units.Get(teleported.UnitId).Teleport(board.CellToWorld(teleported.To), seconds);

                case UnitSummonedEvent summoned:
                    return units.Get(summoned.UnitId).Appear(seconds * ImpactFraction);

                case MoveBlockedEvent blocked:
                    return units.Get(blocked.UnitId).Bump(board.CellToWorld(blocked.Attempted), seconds);

                case SpellCastEvent cast:
                    units.Get(cast.CasterId).FaceToward(board.CellToWorld(cast.Target));
                    return Cast(units.Get(cast.CasterId), cast.SpellId, seconds);

                case SpellFizzledEvent fizzled:
                    return Fizzle(units.Get(fizzled.CasterId), seconds);

                case UnitDamagedEvent damaged:
                    return Impact(units.Get(damaged.UnitId), damaged.HealthAfter, new Color(1f, 0.15f, 0.15f), true, seconds);

                case UnitHealedEvent healed:
                    return Impact(units.Get(healed.UnitId), healed.HealthAfter, new Color(0.3f, 1f, 0.4f), false, seconds);

                case UnitDiedEvent died:
                    return Death(units.Get(died.UnitId), seconds);

                default:
                    return null;
            }
        }

        private static IEnumerator Cast(UnitView caster, string spellId, float seconds)
        {
            caster.PlayCast(GameContent.GetCastAnimation(spellId));

            // Sans animation sur le prefab, une pulsation sert de secours.
            if (caster.HasAnimation)
            {
                yield break;
            }

            yield return caster.Pulse(seconds * ImpactFraction);
        }

        // Un lancer qui échoue coupe l'animation de lancer déjà commencée.
        private static IEnumerator Fizzle(UnitView caster, float seconds)
        {
            caster.StopAnimation();
            yield return caster.Flash(new Color(0.5f, 0.5f, 0.5f), Mathf.Min(FlashSeconds, seconds));
        }

        private static IEnumerator Impact(UnitView view, int healthAfter, Color flash, bool isHit, float seconds)
        {
            float delay = seconds * ImpactFraction;
            yield return new WaitForSeconds(delay);

            view.SetHealth(healthAfter);
            if (isHit)
            {
                view.PlayHit();
            }

            yield return view.Flash(flash, Mathf.Min(FlashSeconds, seconds - delay));
        }

        private static IEnumerator Death(UnitView view, float seconds)
        {
            float delay = seconds * ImpactFraction;
            yield return new WaitForSeconds(delay);
            yield return view.Die(Mathf.Min(DeathShrinkSeconds, seconds - delay));
        }
    }
}
