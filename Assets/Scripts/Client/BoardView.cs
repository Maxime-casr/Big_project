using BigProject.Core.Grid;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>Affiche le terrain et convertit entre cases de la grille et positions du monde.</summary>
    public sealed class BoardView : MonoBehaviour
    {
        private static readonly Color FloorA = new Color(0.25f, 0.31f, 0.34f);
        private static readonly Color FloorB = new Color(0.30f, 0.36f, 0.39f);
        private static readonly Color ObstacleColor = new Color(0.10f, 0.11f, 0.13f);
        private static readonly Color PitColor = new Color(0.04f, 0.07f, 0.16f);

        public GridMap Map { get; private set; }

        public void Build(GridMap map)
        {
            Map = map;
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var position = new GridPosition(x, y);
                    SpriteRenderer tile = SpriteFactory.CreateRenderer(
                        "Tile " + x + "," + y, transform, TileColor(map.GetTile(position), x, y), 0);
                    tile.transform.position = CellToWorld(position);
                    tile.transform.localScale = new Vector3(0.96f, 0.96f, 1f);
                }
            }
        }

        public Vector3 CellToWorld(GridPosition cell)
        {
            return new Vector3(
                cell.X - (Map.Width - 1) / 2f,
                cell.Y - (Map.Height - 1) / 2f,
                0f);
        }

        public GridPosition WorldToCell(Vector3 world)
        {
            return new GridPosition(
                Mathf.FloorToInt(world.x + Map.Width / 2f),
                Mathf.FloorToInt(world.y + Map.Height / 2f));
        }

        public bool IsInside(GridPosition cell)
        {
            return Map.IsInside(cell);
        }

        private static Color TileColor(TileType type, int x, int y)
        {
            switch (type)
            {
                case TileType.Obstacle:
                    return ObstacleColor;
                case TileType.Pit:
                    return PitColor;
                default:
                    return (x + y) % 2 == 0 ? FloorA : FloorB;
            }
        }
    }
}
