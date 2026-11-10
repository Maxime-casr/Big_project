using System;

namespace BigProject.Core.Grid
{
    public enum TileType
    {
        Floor = 0,
        /// <summary>Bloque le passage et la ligne de vue.</summary>
        Obstacle = 1,
        /// <summary>Bloque le passage mais pas la ligne de vue.</summary>
        Pit = 2
    }

    /// <summary>Terrain statique du combat. Ne contient aucune unité.</summary>
    public sealed class GridMap
    {
        private readonly TileType[] tiles;

        public GridMap(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "La carte doit avoir une taille positive.");
            }

            Width = width;
            Height = height;
            tiles = new TileType[width * height];
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>
        /// Coordonnées, dans la source de la carte (la Tilemap de la scène), de la case (0,0) de la grille.
        /// Purement informatif : les règles n'en dépendent jamais, seul l'affichage s'en sert.
        /// </summary>
        public int OriginX { get; set; }
        public int OriginY { get; set; }

        public bool IsInside(GridPosition position)
        {
            return position.X >= 0 && position.X < Width && position.Y >= 0 && position.Y < Height;
        }

        public TileType GetTile(GridPosition position)
        {
            return tiles[Index(position)];
        }

        public void SetTile(GridPosition position, TileType type)
        {
            tiles[Index(position)] = type;
        }

        public bool IsWalkable(GridPosition position)
        {
            return IsInside(position) && tiles[Index(position)] == TileType.Floor;
        }

        public bool BlocksSight(GridPosition position)
        {
            return IsInside(position) && tiles[Index(position)] == TileType.Obstacle;
        }

        private int Index(GridPosition position)
        {
            if (!IsInside(position))
            {
                throw new ArgumentOutOfRangeException(nameof(position), position + " est hors de la carte.");
            }

            return position.Y * Width + position.X;
        }
    }
}
