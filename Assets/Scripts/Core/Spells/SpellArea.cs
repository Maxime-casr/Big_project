using System;
using System.Collections.Generic;
using BigProject.Core.Grid;

namespace BigProject.Core.Spells
{
    /// <summary>Calcule les cases touchées par la zone d'effet d'un sort.</summary>
    public static class SpellArea
    {
        public static List<GridPosition> GetCells(GridMap map, AreaShape shape, int radius, GridPosition center)
        {
            var cells = new List<GridPosition>();
            switch (shape)
            {
                case AreaShape.Single:
                    AddIfInside(map, cells, center);
                    break;

                case AreaShape.Cross:
                    AddIfInside(map, cells, center);
                    foreach (GridPosition direction in GridPosition.Directions)
                    {
                        for (int step = 1; step <= radius; step++)
                        {
                            AddIfInside(map, cells, new GridPosition(
                                center.X + direction.X * step,
                                center.Y + direction.Y * step));
                        }
                    }

                    break;

                case AreaShape.Diamond:
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            if (Math.Abs(dx) + Math.Abs(dy) <= radius)
                            {
                                AddIfInside(map, cells, new GridPosition(center.X + dx, center.Y + dy));
                            }
                        }
                    }

                    break;
            }

            return cells;
        }

        private static void AddIfInside(GridMap map, List<GridPosition> cells, GridPosition position)
        {
            if (map.IsInside(position))
            {
                cells.Add(position);
            }
        }
    }
}
