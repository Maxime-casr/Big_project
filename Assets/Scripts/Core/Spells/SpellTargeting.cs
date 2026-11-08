using System;
using System.Collections.Generic;
using BigProject.Core.Grid;

namespace BigProject.Core.Spells
{
    /// <summary>Règles de portée et de ligne de vue d'un sort.</summary>
    public static class SpellTargeting
    {
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

            int distance = from.DistanceTo(target);
            if (distance < spell.MinRange || distance > spell.MaxRange)
            {
                error = "Case hors de portée (" + spell.MinRange + " à " + spell.MaxRange + ").";
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
    }
}
