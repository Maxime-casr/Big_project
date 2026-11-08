using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using BigProject.Core.Units;

namespace BigProject.Core.Resolution
{
    public sealed class PendingMove
    {
        public PendingMove(int unitId, IReadOnlyList<GridPosition> path)
        {
            UnitId = unitId;
            Path = path;
        }

        public int UnitId { get; }
        public IReadOnlyList<GridPosition> Path { get; }
        public bool Stopped { get; set; }
    }

    /// <summary>
    /// Résout les déplacements d'une étape : tous les marcheurs avancent d'une case en même temps.
    /// Règles de conflit, symétriques (aucune priorité arbitraire) :
    /// - deux unités visant la même case sont toutes deux arrêtées ;
    /// - un échange de places est refusé aux deux unités ;
    /// - une case occupée par une unité qui ne bouge pas arrête le marcheur ;
    /// - suivre une unité qui libère sa case au même instant est autorisé.
    /// Une unité arrêtée abandonne le reste de son chemin.
    /// </summary>
    public static class MovementResolver
    {
        public static void Resolve(MatchState state, List<PendingMove> moves, List<ResolutionBeat> beats)
        {
            int longest = 0;
            foreach (PendingMove move in moves)
            {
                longest = System.Math.Max(longest, move.Path.Count);
            }

            for (int stepIndex = 0; stepIndex < longest; stepIndex++)
            {
                var proposals = new List<PendingMove>();
                foreach (PendingMove move in moves)
                {
                    if (!move.Stopped && stepIndex < move.Path.Count)
                    {
                        proposals.Add(move);
                    }
                }

                if (proposals.Count == 0)
                {
                    break;
                }

                HashSet<int> blocked = FindBlocked(state, proposals, stepIndex);
                var beat = new ResolutionBeat();

                foreach (PendingMove move in proposals)
                {
                    if (blocked.Contains(move.UnitId))
                    {
                        UnitState unit = state.GetUnit(move.UnitId);
                        move.Stopped = true;
                        beat.Events.Add(new MoveBlockedEvent(unit.Id, unit.Position, move.Path[stepIndex]));
                    }
                }

                foreach (PendingMove move in proposals)
                {
                    if (!blocked.Contains(move.UnitId))
                    {
                        UnitState unit = state.GetUnit(move.UnitId);
                        GridPosition from = unit.Position;
                        unit.MoveTo(move.Path[stepIndex]);
                        beat.Events.Add(new UnitMovedEvent(unit.Id, from, unit.Position));
                    }
                }

                beats.Add(beat);
            }
        }

        private static HashSet<int> FindBlocked(MatchState state, List<PendingMove> proposals, int stepIndex)
        {
            var target = new Dictionary<int, GridPosition>();
            foreach (PendingMove move in proposals)
            {
                target[move.UnitId] = move.Path[stepIndex];
            }

            var blocked = new HashSet<int>();
            while (true)
            {
                // Chaque passe est évaluée sur un instantané : l'ordre de parcours n'influence pas le résultat.
                var newlyBlocked = new List<int>();
                foreach (PendingMove move in proposals)
                {
                    if (!blocked.Contains(move.UnitId) &&
                        IsBlocked(state, proposals, target, blocked, state.GetUnit(move.UnitId)))
                    {
                        newlyBlocked.Add(move.UnitId);
                    }
                }

                if (newlyBlocked.Count == 0)
                {
                    return blocked;
                }

                foreach (int unitId in newlyBlocked)
                {
                    blocked.Add(unitId);
                }
            }
        }

        private static bool IsBlocked(
            MatchState state,
            List<PendingMove> proposals,
            Dictionary<int, GridPosition> target,
            HashSet<int> blocked,
            UnitState unit)
        {
            GridPosition cell = target[unit.Id];

            foreach (PendingMove other in proposals)
            {
                if (other.UnitId != unit.Id && !blocked.Contains(other.UnitId) && target[other.UnitId] == cell)
                {
                    return true;
                }
            }

            UnitState occupant = state.GetUnitAt(cell);
            if (occupant == null || occupant.Id == unit.Id)
            {
                return false;
            }

            bool occupantLeaves = target.ContainsKey(occupant.Id) && !blocked.Contains(occupant.Id);
            if (!occupantLeaves)
            {
                return true;
            }

            return target[occupant.Id] == unit.Position;
        }
    }
}
