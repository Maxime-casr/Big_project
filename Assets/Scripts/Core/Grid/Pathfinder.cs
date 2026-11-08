using System;
using System.Collections.Generic;

namespace BigProject.Core.Grid
{
    /// <summary>Recherche de chemin en 4 directions (parcours en largeur, déterministe).</summary>
    public static class Pathfinder
    {
        /// <summary>
        /// Plus court chemin sans la case de départ. Retourne null si la case est
        /// inaccessible dans la limite donnée ou si départ et arrivée sont identiques.
        /// </summary>
        public static List<GridPosition> FindPath(
            GridMap map,
            GridPosition start,
            GridPosition goal,
            int maxLength,
            Func<GridPosition, bool> isBlocked = null)
        {
            if (!map.IsInside(start) || !map.IsInside(goal) || start == goal || maxLength <= 0)
            {
                return null;
            }

            if (!IsPassable(map, goal, isBlocked))
            {
                return null;
            }

            var distance = new int[map.Width * map.Height];
            var previous = new GridPosition[map.Width * map.Height];
            for (int i = 0; i < distance.Length; i++)
            {
                distance[i] = -1;
            }

            var queue = new Queue<GridPosition>();
            distance[Index(map, start)] = 0;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                GridPosition current = queue.Dequeue();
                int currentDistance = distance[Index(map, current)];
                if (current == goal)
                {
                    break;
                }

                if (currentDistance >= maxLength)
                {
                    continue;
                }

                foreach (GridPosition direction in GridPosition.Directions)
                {
                    GridPosition next = current + direction;
                    if (!IsPassable(map, next, isBlocked) || distance[Index(map, next)] != -1)
                    {
                        continue;
                    }

                    distance[Index(map, next)] = currentDistance + 1;
                    previous[Index(map, next)] = current;
                    queue.Enqueue(next);
                }
            }

            if (distance[Index(map, goal)] == -1)
            {
                return null;
            }

            var path = new List<GridPosition>();
            GridPosition step = goal;
            while (step != start)
            {
                path.Add(step);
                step = previous[Index(map, step)];
            }

            path.Reverse();
            return path;
        }

        /// <summary>Toutes les cases atteignables en au plus maxCost pas (départ exclu).</summary>
        public static List<GridPosition> GetReachable(
            GridMap map,
            GridPosition start,
            int maxCost,
            Func<GridPosition, bool> isBlocked = null)
        {
            var result = new List<GridPosition>();
            if (!map.IsInside(start) || maxCost <= 0)
            {
                return result;
            }

            var distance = new int[map.Width * map.Height];
            for (int i = 0; i < distance.Length; i++)
            {
                distance[i] = -1;
            }

            var queue = new Queue<GridPosition>();
            distance[Index(map, start)] = 0;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                GridPosition current = queue.Dequeue();
                int currentDistance = distance[Index(map, current)];
                if (currentDistance >= maxCost)
                {
                    continue;
                }

                foreach (GridPosition direction in GridPosition.Directions)
                {
                    GridPosition next = current + direction;
                    if (!IsPassable(map, next, isBlocked) || distance[Index(map, next)] != -1)
                    {
                        continue;
                    }

                    distance[Index(map, next)] = currentDistance + 1;
                    result.Add(next);
                    queue.Enqueue(next);
                }
            }

            return result;
        }

        private static bool IsPassable(GridMap map, GridPosition position, Func<GridPosition, bool> isBlocked)
        {
            return map.IsWalkable(position) && (isBlocked == null || !isBlocked(position));
        }

        private static int Index(GridMap map, GridPosition position)
        {
            return position.Y * map.Width + position.X;
        }
    }
}
