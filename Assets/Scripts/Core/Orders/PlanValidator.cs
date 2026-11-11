using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using BigProject.Core.Spells;
using BigProject.Core.Units;

namespace BigProject.Core.Orders
{
    /// <summary>Résultat de la simulation d'un plan : coûts consommés, position finale et invocations prévues.</summary>
    public sealed class PlanProjection
    {
        internal PlanProjection(GridPosition start)
        {
            FinalPosition = start;
        }

        /// <summary>Où sera l'unité à la fin du plan (déplacements et téléportations compris).</summary>
        public GridPosition FinalPosition { get; internal set; }

        public int MovementUsed { get; internal set; }
        public int ManaUsed { get; internal set; }

        /// <summary>Les cases où le plan invoque une unité, dans l'ordre (pour les fantômes transparents).</summary>
        public List<GridPosition> SummonCells { get; } = new List<GridPosition>();

        /// <summary>Le personnage invoqué à chacune de ces cases (même ordre que SummonCells).</summary>
        public List<string> SummonCharacterIds { get; } = new List<string>();

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
                    error = ApplyCast(state, spells, unit, cast, ref position, projection);
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
            MatchState state,
            SpellCatalog spells,
            UnitState unit,
            CastOrder cast,
            ref GridPosition position,
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

            string error;
            switch (spell.Targeting)
            {
                case TargetKind.Self:
                    return null;

                case TargetKind.Unit:
                    // À la planification, la cible est jugée sur sa position actuelle : c'est la meilleure
                    // information disponible. La vraie vérification a lieu au lancement.
                    UnitState target = state.GetUnit(cast.TargetUnitId);
                    return SpellTargeting.CanTargetUnit(state.Map, spell, position, unit, target, out error) ? null : error;

                default:
                    if (!SpellTargeting.CanTarget(state.Map, spell, position, cast.Target, out error))
                    {
                        return error;
                    }

                    if (spell.HasEffect(SpellEffectKind.Teleport) || spell.HasEffect(SpellEffectKind.Summon))
                    {
                        if (!state.Map.IsWalkable(cast.Target))
                        {
                            return "On ne peut pas se placer sur cette case.";
                        }
                    }

                    if (spell.HasEffect(SpellEffectKind.Summon))
                    {
                        int planned = state.CountLivingSummons(unit.PlayerId) + projection.SummonCells.Count;
                        if (planned >= state.Config.MaxSummonsPerPlayer)
                        {
                            return "Trop d'invocations (maximum " + state.Config.MaxSummonsPerPlayer + ").";
                        }

                        projection.SummonCells.Add(cast.Target);
                        projection.SummonCharacterIds.Add(spell.SummonedCharacterId);
                    }

                    if (spell.HasEffect(SpellEffectKind.Teleport))
                    {
                        position = cast.Target;
                    }

                    return null;
            }
        }
    }
}
