using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using BigProject.Core.Spells;
using BigProject.Core.Units;

namespace BigProject.Core.Orders
{
    /// <summary>Résultat de la simulation d'un plan : coûts consommés et position finale.</summary>
    public sealed class PlanProjection
    {
        internal PlanProjection(GridPosition start)
        {
            FinalPosition = start;
        }

        public GridPosition FinalPosition { get; internal set; }
        public int MovementUsed { get; internal set; }
        public int ManaUsed { get; internal set; }
        public string Error { get; internal set; }
        public bool IsValid => Error == null;
    }

    /// <summary>
    /// Valide un plan en le simulant ordre par ordre. Utilisé par le serveur pour accepter
    /// ou refuser un plan, et par le client pour la prévisualisation et les budgets restants.
    /// </summary>
    public static class PlanValidator
    {
        public static PlanProjection Project(
            MatchState state,
            SpellCatalog spells,
            UnitState unit,
            IReadOnlyList<UnitOrder> orders)
        {
            var projection = new PlanProjection(unit.Position);

            if (!unit.IsAlive)
            {
                projection.Error = "L'unité est vaincue.";
                return projection;
            }

            if (orders.Count > state.Config.MaxOrdersPerUnit)
            {
                projection.Error = "Trop d'actions (maximum " + state.Config.MaxOrdersPerUnit + ").";
                return projection;
            }

            GridPosition position = unit.Position;
            foreach (UnitOrder order in orders)
            {
                string error;
                if (order is MoveOrder move)
                {
                    error = ApplyMove(state.Map, move, ref position, projection, unit.MovementPoints);
                }
                else if (order is CastOrder cast)
                {
                    error = ApplyCast(state.Map, spells, unit, cast, position, projection);
                }
                else
                {
                    error = "Type d'ordre inconnu.";
                }

                if (error != null)
                {
                    projection.Error = error;
                    return projection;
                }
            }

            projection.FinalPosition = position;
            return projection;
        }

        private static string ApplyMove(
            GridMap map,
            MoveOrder move,
            ref GridPosition position,
            PlanProjection projection,
            int movementPoints)
        {
            if (move.Path.Count == 0)
            {
                return "Déplacement vide.";
            }

            var visited = new HashSet<GridPosition> { position };
            foreach (GridPosition cell in move.Path)
            {
                if (!position.IsAdjacentTo(cell))
                {
                    return "Le chemin doit avancer case par case.";
                }

                if (!map.IsWalkable(cell))
                {
                    return "Le chemin traverse une case infranchissable.";
                }

                if (!visited.Add(cell))
                {
                    return "Le chemin repasse par la même case.";
                }

                position = cell;
                projection.MovementUsed++;
            }

            return projection.MovementUsed > movementPoints ? "Pas assez de PM." : null;
        }

        private static string ApplyCast(
            GridMap map,
            SpellCatalog spells,
            UnitState unit,
            CastOrder cast,
            GridPosition position,
            PlanProjection projection)
        {
            SpellDefinition spell = spells.Get(cast.SpellId);
            if (spell == null || !unit.KnowsSpell(cast.SpellId))
            {
                return "Sort inconnu ou non maîtrisé.";
            }

            projection.ManaUsed += spell.ManaCost;
            if (projection.ManaUsed > unit.Mana)
            {
                return "Pas assez de mana.";
            }

            return SpellTargeting.CanTarget(map, spell, position, cast.Target, out string error) ? null : error;
        }
    }
}
