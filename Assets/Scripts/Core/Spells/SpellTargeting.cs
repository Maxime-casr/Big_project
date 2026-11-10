using System;
using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Units;

namespace BigProject.Core.Spells
{
    /// <summary>Règles de portée, de ligne de vue et de cible d'un sort.</summary>
    public static class SpellTargeting
    {
        /// <summary>Le lanceur et la cible sont-ils dans la relation voulue (soi-même, allié, ennemi) ?</summary>
        public static bool Matches(TargetFilter filter, UnitState caster, UnitState victim)
        {
            if (victim.Id == caster.Id)
            {
                return (filter & TargetFilter.Self) != 0;
            }

            return victim.TeamId == caster.TeamId
                ? (filter & TargetFilter.Allies) != 0
                : (filter & TargetFilter.Enemies) != 0;
        }

        public static bool CanTarget(
            GridMap map,
            SpellDefinition spell,
            GridPosition from,
            GridPosition target,
            out string error)
        {
            if (!map.IsInside(target))
            {
                error = "La case visée est hors de la carte.";
                return false;
            }

            return CheckGeometry(map, spell, from, target, out error);
        }

        /// <summary>
        /// Une unité peut-elle être visée depuis cette position ? Utilisé deux fois : à la planification
        /// (avec la position actuelle de la cible) et au lancement (avec sa position au début de l'action).
        /// </summary>
        public static bool CanTargetUnit(
            GridMap map,
            SpellDefinition spell,
            GridPosition from,
            UnitState caster,
            UnitState target,
            out string error)
        {
            if (target == null)
            {
                error = "Cible introuvable.";
                return false;
            }

            if (!target.IsAlive)
            {
                error = "La cible est déjà vaincue.";
                return false;
            }

            if (!Matches(spell.UnitTargets, caster, target))
            {
                error = "Cette cible n'est pas valide pour ce sort.";
                return false;
            }

            return CheckGeometry(map, spell, from, target.Position, out error);
        }

        private static bool CheckGeometry(
            GridMap map,
            SpellDefinition spell,
            GridPosition from,
            GridPosition target,
            out string error)
        {
            int distance = from.DistanceTo(target);
            if (distance < spell.MinRange || distance > spell.MaxRange)
            {
                error = "Hors de portée (" + spell.MinRange + " à " + spell.MaxRange + ").";
                return false;
            }

            if (spell.StraightLineOnly && from.X != target.X && from.Y != target.Y)
            {
                error = "Ce sort ne se lance qu'en ligne droite.";
                return false;
            }

            if (spell.RequiresLineOfSight && !LineOfSight.HasLineOfSight(map, from, target))
            {
                error = "La ligne de vue est bloquée.";
                return false;
            }

            error = null;
            return true;
        }

        public static List<GridPosition> GetCastableCells(GridMap map, SpellDefinition spell, GridPosition from)
        {
            var cells = new List<GridPosition>();
            for (int dy = -spell.MaxRange; dy <= spell.MaxRange; dy++)
            {
                for (int dx = -spell.MaxRange; dx <= spell.MaxRange; dx++)
                {
                    var candidate = new GridPosition(from.X + dx, from.Y + dy);
                    if (map.IsInside(candidate) && CanTarget(map, spell, from, candidate, out _))
                    {
                        cells.Add(candidate);
                    }
                }
            }

            return cells;
        }

        /// <summary>Les unités vivantes que l'on peut viser depuis cette position.</summary>
        public static List<UnitState> GetTargetableUnits(
            GridMap map,
            SpellDefinition spell,
            GridPosition from,
            UnitState caster,
            IEnumerable<UnitState> units)
        {
            var result = new List<UnitState>();
            foreach (UnitState unit in units)
            {
                if (CanTargetUnit(map, spell, from, caster, unit, out _))
                {
                    result.Add(unit);
                }
            }

            return result;
        }
    }
}
