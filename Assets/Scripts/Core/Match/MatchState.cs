using System;
using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Units;

namespace BigProject.Core.Match
{
    public enum MatchPhase
    {
        /// <summary>Les joueurs choisissent leurs actions.</summary>
        Planning,
        /// <summary>Le serveur résout le tour. Aucun plan accepté.</summary>
        Resolving,
        Finished
    }

    /// <summary>
    /// Source de vérité du match : carte, unités, tour courant. Seul le résolveur la modifie.
    /// Les objets de la scène ne font que l'afficher.
    /// </summary>
    public sealed class MatchState
    {
        public const int NoTeam = -1;

        private readonly List<UnitState> units = new List<UnitState>();

        public MatchState(GridMap map, MatchConfig config)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Config = config ?? throw new ArgumentNullException(nameof(config));
            RoundNumber = 1;
            Phase = MatchPhase.Planning;
            WinnerTeamId = NoTeam;
        }

        public GridMap Map { get; }
        public MatchConfig Config { get; }
        public IReadOnlyList<UnitState> Units => units;

        public int RoundNumber { get; set; }
        public MatchPhase Phase { get; set; }
        /// <summary>Équipe gagnante une fois le match terminé, NoTeam en cas d'égalité.</summary>
        public int WinnerTeamId { get; set; }

        public void AddUnit(UnitState unit)
        {
            if (GetUnit(unit.Id) != null)
            {
                throw new InvalidOperationException("Identifiant d'unité déjà utilisé : " + unit.Id);
            }

            if (!Map.IsWalkable(unit.Position) || GetUnitAt(unit.Position) != null)
            {
                throw new InvalidOperationException("Case de départ invalide pour l'unité " + unit.Id + ".");
            }

            units.Add(unit);
        }

        public UnitState GetUnit(int id)
        {
            foreach (UnitState unit in units)
            {
                if (unit.Id == id)
                {
                    return unit;
                }
            }

            return null;
        }

        /// <summary>Unité vivante présente sur la case, sinon null.</summary>
        public UnitState GetUnitAt(GridPosition position)
        {
            foreach (UnitState unit in units)
            {
                if (unit.IsAlive && unit.Position == position)
                {
                    return unit;
                }
            }

            return null;
        }

        /// <summary>Joueurs ayant encore au moins une unité en vie, triés par identifiant.</summary>
        public List<int> GetActivePlayerIds()
        {
            var players = new SortedSet<int>();
            foreach (UnitState unit in units)
            {
                if (unit.IsAlive)
                {
                    players.Add(unit.PlayerId);
                }
            }

            return new List<int>(players);
        }

        public MatchState Clone()
        {
            var copy = new MatchState(Map, Config)
            {
                RoundNumber = RoundNumber,
                Phase = Phase,
                WinnerTeamId = WinnerTeamId
            };

            foreach (UnitState unit in units)
            {
                copy.units.Add(unit.Clone());
            }

            return copy;
        }
    }
}
