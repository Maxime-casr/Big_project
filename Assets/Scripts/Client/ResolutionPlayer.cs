using System;
using System.Collections;
using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using BigProject.Core.Resolution;
using BigProject.Core.Units;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Rejoue un RoundResult : les événements d'un même beat s'animent en parallèle,
    /// les beats s'enchaînent. Ne décide jamais du résultat, il le met seulement en scène.
    /// </summary>
    public sealed class ResolutionPlayer : MonoBehaviour
    {
        private const float MoveDuration = 0.22f;
        private const float CastDuration = 0.35f;
        private const float ImpactDelay = 0.2f;
        private const float MinimumBeatDuration = 0.25f;

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

            foreach (ResolutionBeat beat in result.Beats)
            {
                yield return PlayBeat(beat);
            }

            highlighter.Clear(HighlightLayer.Flash);
            units.SyncAll(finalState);
            IsPlaying = false;
            PlaybackFinished?.Invoke();
        }

        private IEnumerator PlayBeat(ResolutionBeat beat)
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

            float elapsed = 0f;
            runningAnimations = 0;
            foreach (GameEvent gameEvent in beat.Events)
            {
                IEnumerator animation = Animate(gameEvent);
                if (animation != null)
                {
                    StartCoroutine(Track(animation));
                }
            }

            while (runningAnimations > 0 || elapsed < MinimumBeatDuration)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private IEnumerator Track(IEnumerator animation)
        {
            runningAnimations++;
            yield return animation;
            runningAnimations--;
        }

        private IEnumerator Animate(GameEvent gameEvent)
        {
            switch (gameEvent)
            {
                case UnitMovedEvent moved:
                    return units.Get(moved.UnitId).MoveTo(board.CellToWorld(moved.To), MoveDuration);

                case UnitTeleportedEvent teleported:
                    return units.Get(teleported.UnitId).Teleport(board.CellToWorld(teleported.To), MoveDuration * 1.5f);

                case UnitSummonedEvent summoned:
                    return units.Get(summoned.UnitId).Appear(CastDuration);

                case MoveBlockedEvent blocked:
                    return units.Get(blocked.UnitId).Bump(board.CellToWorld(blocked.Attempted), MoveDuration);

                case SpellCastEvent cast:
                    return units.Get(cast.CasterId).Pulse(CastDuration);

                case SpellFizzledEvent fizzled:
                    return units.Get(fizzled.CasterId).Flash(new Color(0.5f, 0.5f, 0.5f), CastDuration);

                case UnitDamagedEvent damaged:
                    return AfterDelay(units.Get(damaged.UnitId), damaged.HealthAfter, new Color(1f, 0.15f, 0.15f));

                case UnitHealedEvent healed:
                    return AfterDelay(units.Get(healed.UnitId), healed.HealthAfter, new Color(0.3f, 1f, 0.4f));

                case UnitDiedEvent died:
                    return Death(units.Get(died.UnitId));

                default:
                    return null;
            }
        }

        private static IEnumerator AfterDelay(UnitView view, int healthAfter, Color flash)
        {
            yield return new WaitForSeconds(ImpactDelay);
            view.SetHealth(healthAfter);
            yield return view.Flash(flash, 0.25f);
        }

        private static IEnumerator Death(UnitView view)
        {
            yield return new WaitForSeconds(ImpactDelay + 0.25f);
            yield return view.Die(0.3f);
        }
    }
}
