using System;
using System.Collections.Generic;

namespace BigProject.Core.Grid
{
    public static class LineOfSight
    {
        /// <summary>Les cases intermédiaires (hors départ et arrivée) ne doivent pas bloquer la vue.</summary>
        public static bool HasLineOfSight(GridMap map, GridPosition from, GridPosition to)
        {
            List<GridPosition> line = Line(from, to);
            for (int i = 1; i < line.Count - 1; i++)
            {
                if (map.BlocksSight(line[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Ligne de Bresenham, extrémités incluses.</summary>
        public static List<GridPosition> Line(GridPosition from, GridPosition to)
        {
            var cells = new List<GridPosition>();
            int x = from.X;
            int y = from.Y;
            int dx = Math.Abs(to.X - from.X);
            int dy = -Math.Abs(to.Y - from.Y);
            int stepX = from.X < to.X ? 1 : -1;
            int stepY = from.Y < to.Y ? 1 : -1;
            int error = dx + dy;

            while (true)
            {
                cells.Add(new GridPosition(x, y));
                if (x == to.X && y == to.Y)
                {
                    break;
                }

                int doubled = 2 * error;
                if (doubled >= dy)
                {
                    error += dy;
                    x += stepX;
                }

                if (doubled <= dx)
                {
                    error += dx;
                    y += stepY;
                }
            }

            return cells;
        }
    }
}
