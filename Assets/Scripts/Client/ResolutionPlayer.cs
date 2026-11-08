using System;
using System.Collections;
using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using BigProject.Core.Resolution;
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
            foreach (ResolutionBeat beat in result.Beats)
            {
                yield return PlayBeat(beat);
            }

            units.SyncAll(finalState);
            IsPlaying = false;
            PlaybackFinished?.Invoke();
        }

        private IEnumerator PlayBeat(ResolutionBeat beat)
        {
            var flashed = new List<GridPosition>();
            foreach (GameEvent gameEvent in beat.Events)
            {
                if (gameEvent is SpellCastEvent cast)
                {
                    flashed.AddRange(cast.AffectedCells);
                }
            }

            highlighter.Set(HighlightLayer.Flash, flashed);

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

            highlighter.Clear(HighlightLayer.Flash);
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
