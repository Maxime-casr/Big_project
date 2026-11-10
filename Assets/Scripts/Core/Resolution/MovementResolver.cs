using System;
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

        /// <summary>Le déplacement a été interrompu : l'unité n'ira pas plus loin.</summary>
        public bool Stopped { get; set; }
    }

    /// <summary>
    /// Résout les déplacements d'une action : tous les marcheurs avancent d'une case en même temps.
    /// Règles de collision :
    /// - une unité déjà posée sur une case (ou bloquée) la garde : celle qui voulait y entrer s'arrête ;
    /// - plusieurs unités qui visent la même case en même temps : l'une d'elles, tirée au hasard, passe,
    ///   les autres s'arrêtent ;
    /// - un échange de places est impossible : les deux unités s'arrêtent ;
    /// - suivre une unité qui libère sa case au même instant est autorisé, sauf si elle est elle-même bloquée.
    /// Une unité arrêtée perd le reste de son chemin ; l'appelant annule aussi ses actions suivantes.
    /// </summary>
    public static class MovementResolver
    {
        public static void Resolve(MatchState state, List<PendingMove> moves, List<ResolutionBeat> beats, Random random)
        {
            int longest = 0;
            foreach (PendingMove move in moves)
            {
                longest = Math.Max(longest, move.Path.Count);
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

                HashSet<int> blocked = FindBlocked(state, proposals, stepIndex, random);
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

        private static HashSet<int> FindBlocked(
            MatchState state,
            List<PendingMove> proposals,
            int stepIndex,
            Random random)
        {
            var target = new Dictionary<int, GridPosition>();
            foreach (PendingMove move in proposals)
            {
                target[move.UnitId] = move.Path[stepIndex];
            }

            var blocked = new HashSet<int>();
            var winners = new Dictionary<GridPosition, int>();

            while (true)
            {
                // Chaque passe est évaluée sur un instantané : l'ordre de parcours n'influence pas le résultat.
                var newlyBlocked = new HashSet<int>();
                AddContestLosers(proposals, target, blocked, winners, random, newlyBlocked);

                foreach (PendingMove move in proposals)
                {
                    if (!blocked.Contains(move.UnitId) && !newlyBlocked.Contains(move.UnitId) &&
                        IsBlockedByOccupant(state, target, blocked, state.GetUnit(move.UnitId)))
                    {
                        newlyBlocked.Add(move.UnitId);
                    }
                }

                if (newlyBlocked.Count == 0)
                {
                    return blocked;
                }

                blocked.UnionWith(newlyBlocked);
            }
        }

        /// <summary>Tire au sort, une seule fois par case, l'unité qui passe parmi celles qui la visent.</summary>
        private static void AddContestLosers(
            List<PendingMove> proposals,
            Dictionary<int, GridPosition> target,
            HashSet<int> blocked,
            Dictionary<GridPosition, int> winners,
            Random random,
            HashSet<int> newlyBlocked)
        {
            var contenders = new Dictionary<GridPosition, List<int>>();
            var cells = new List<GridPosition>();
            foreach (PendingMove move in proposals)
            {
                if (blocked.Contains(move.UnitId))
                {
                    continue;
                }

                GridPosition cell = target[move.UnitId];
                if (!contenders.TryGetValue(cell, out List<int> list))
                {
                    list = new List<int>();
                    contenders.Add(cell, list);
                    cells.Add(cell);
                }

                list.Add(move.UnitId);
            }

            // Ordre fixe des cases : le tirage au sort ne dépend pas de l'ordre des dictionnaires.
            cells.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));

            foreach (GridPosition cell in cells)
            {
                List<int> list = contenders[cell];
                if (list.Count < 2)
                {
                    continue;
                }

                if (!winners.TryGetValue(cell, out int winner))
                {
                    winner = list[random.Next(list.Count)];
                    winners.Add(cell, winner);
                }

                foreach (int unitId in list)
                {
                    if (unitId != winner)
                    {
                        newlyBlocked.Add(unitId);
                    }
                }
            }
        }

        private static bool IsBlockedByOccupant(
            MatchState state,
            Dictionary<int, GridPosition> target,
            HashSet<int> blocked,
            UnitState unit)
        {
            GridPosition cell = target[unit.Id];
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

            // Échange de places : impossible.
            return target[occupant.Id] == unit.Position;
        }
    }
}
