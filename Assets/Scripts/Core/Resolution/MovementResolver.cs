using System;
using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using BigProject.Core.Units;

namespace BigProject.Core.Resolution
{
    public enum PlacementKind
    {
        /// <summary>Marche case par case.</summary>
        Walk,

        /// <summary>Le lanceur réapparaît sur la case visée, sans traverser ce qui est entre les deux.</summary>
        Teleport,

        /// <summary>Une nouvelle unité apparaît sur la case visée.</summary>
        Summon
    }

    /// <summary>
    /// Une unité qui veut occuper des cases pendant l'action : en marchant, en se téléportant ou en apparaissant.
    /// Tout le monde est soumis aux mêmes règles de collision.
    /// </summary>
    public sealed class PendingMove
    {
        public PendingMove(int unitId, IReadOnlyList<GridPosition> path)
        {
            UnitId = unitId;
            CasterId = unitId;
            Path = path;
            Kind = PlacementKind.Walk;
        }

        private PendingMove(int unitId, int casterId, PlacementKind kind, GridPosition destination, string spellId)
        {
            UnitId = unitId;
            CasterId = casterId;
            Kind = kind;
            Path = new[] { destination };
            SpellId = spellId;
        }

        public static PendingMove Teleport(int casterId, GridPosition destination, string spellId)
        {
            return new PendingMove(casterId, casterId, PlacementKind.Teleport, destination, spellId);
        }

        /// <param name="summoned">L'unité à créer, déjà construite mais pas encore ajoutée au match.</param>
        public static PendingMove Summon(int casterId, UnitState summoned, string spellId)
        {
            return new PendingMove(summoned.Id, casterId, PlacementKind.Summon, summoned.Position, spellId)
            {
                Summoned = summoned
            };
        }

        /// <summary>L'unité qui bouge, se téléporte, ou (invocation) l'identifiant de la future unité.</summary>
        public int UnitId { get; }

        /// <summary>Le personnage à l'origine de l'action : c'est lui qui est interrompu si elle échoue.</summary>
        public int CasterId { get; }

        public PlacementKind Kind { get; }
        public IReadOnlyList<GridPosition> Path { get; }
        public string SpellId { get; }
        public UnitState Summoned { get; private set; }

        /// <summary>L'action a échoué : l'unité n'ira pas plus loin.</summary>
        public bool Stopped { get; set; }
    }

    /// <summary>
    /// Résout les déplacements d'une action : tous les marcheurs avancent d'une case en même temps, et les
    /// téléportations et invocations arrivent au tout premier instant.
    /// Règles de collision :
    /// - une unité déjà posée sur une case (ou bloquée) la garde : celle qui voulait y entrer s'arrête ;
    /// - plusieurs unités qui visent la même case au même instant : l'une d'elles, tirée au hasard (l'initiative),
    ///   passe, les autres s'arrêtent ;
    /// - un échange de places est impossible : les deux unités s'arrêtent ;
    /// - suivre une unité qui libère sa case au même instant est autorisé, sauf si elle est elle-même bloquée.
    /// Une unité arrêtée perd le reste de son chemin ; l'appelant annule aussi les actions suivantes de son lanceur.
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
                    if (!blocked.Contains(move.UnitId))
                    {
                        continue;
                    }

                    move.Stopped = true;
                    if (move.Kind == PlacementKind.Walk)
                    {
                        UnitState unit = state.GetUnit(move.UnitId);
                        beat.Events.Add(new MoveBlockedEvent(unit.Id, unit.Position, move.Path[stepIndex]));
                    }
                }

                // Les marcheurs et téléportés bougent d'abord, puis les invocations apparaissent : la case qu'une
                // unité quitte à cet instant est alors libre.
                foreach (PendingMove move in proposals)
                {
                    if (blocked.Contains(move.UnitId) || move.Kind == PlacementKind.Summon)
                    {
                        continue;
                    }

                    UnitState unit = state.GetUnit(move.UnitId);
                    GridPosition from = unit.Position;
                    unit.MoveTo(move.Path[stepIndex]);
                    beat.Events.Add(move.Kind == PlacementKind.Teleport
                        ? (GameEvent)new UnitTeleportedEvent(unit.Id, from, unit.Position)
                        : new UnitMovedEvent(unit.Id, from, unit.Position));
                }

                foreach (PendingMove move in proposals)
                {
                    if (!blocked.Contains(move.UnitId) && move.Kind == PlacementKind.Summon)
                    {
                        state.AddUnit(move.Summoned);
                        beat.Events.Add(new UnitSummonedEvent(move.Summoned.Id, move.CasterId, move.Summoned.Position));
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
            var byId = new Dictionary<int, PendingMove>();
            foreach (PendingMove move in proposals)
            {
                target[move.UnitId] = move.Path[stepIndex];
                byId[move.UnitId] = move;
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
                        IsBlockedByOccupant(state, target, blocked, move))
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

                list.Sort();
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
            PendingMove move)
        {
            GridPosition cell = target[move.UnitId];
            UnitState occupant = state.GetUnitAt(cell);
            if (occupant == null || occupant.Id == move.UnitId)
            {
                return false;
            }

            bool occupantLeaves = target.ContainsKey(occupant.Id) && !blocked.Contains(occupant.Id);
            if (!occupantLeaves)
            {
                return true;
            }

            // Échange de places : impossible. Une invocation n'a pas de case de départ, donc rien à échanger.
            UnitState mover = move.Kind == PlacementKind.Summon ? null : state.GetUnit(move.UnitId);
            return mover != null && target[occupant.Id] == mover.Position;
        }
    }
}
