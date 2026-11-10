using BigProject.Core.Grid;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace BigProject.Client
{
    /// <summary>
    /// Affiche le terrain et convertit entre cases de la grille et positions du monde.
    /// Avec une Tilemap (celle de la scène), la conversion passe par elle : origine quelconque, coordonnées
    /// négatives et centres de cases en .5 sont gérés. Sans Tilemap, des carrés sont générés, centrés sur (0,0).
    /// Tout le reste du client (unités, caméra, minimap, surbrillances) passe par cette classe pour convertir.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        private static readonly Color FloorA = new Color(0.25f, 0.31f, 0.34f);
        private static readonly Color FloorB = new Color(0.30f, 0.36f, 0.39f);
        private static readonly Color ObstacleColor = new Color(0.10f, 0.11f, 0.13f);
        private static readonly Color PitColor = new Color(0.04f, 0.07f, 0.16f);

        private Tilemap tilemap;

        public GridMap Map { get; private set; }
        public bool UsesTilemap => tilemap != null;

        /// <summary>Rectangle du monde couvert par la carte, bords compris.</summary>
        public Rect WorldBounds { get; private set; }

        /// <param name="tilemap">Tilemap de la scène à utiliser pour l'affichage ; null pour générer le terrain.</param>
        public void Build(GridMap map, Tilemap tilemap = null)
        {
            Map = map;
            this.tilemap = tilemap;

            if (tilemap == null)
            {
                GenerateTiles();
            }

            ComputeBounds();
        }

        public Vector3 CellToWorld(GridPosition cell)
        {
            if (tilemap != null)
            {
                Vector3 center = tilemap.GetCellCenterWorld(new Vector3Int(Map.OriginX + cell.X, Map.OriginY + cell.Y, 0));
                center.z = 0f;
                return center;
            }

            return new Vector3(
                cell.X - (Map.Width - 1) / 2f,
                cell.Y - (Map.Height - 1) / 2f,
                0f);
        }

        public GridPosition WorldToCell(Vector3 world)
        {
            if (tilemap != null)
            {
                Vector3Int cell = tilemap.WorldToCell(world);
                return new GridPosition(cell.x - Map.OriginX, cell.y - Map.OriginY);
            }

            return new GridPosition(
                Mathf.FloorToInt(world.x + Map.Width / 2f),
                Mathf.FloorToInt(world.y + Map.Height / 2f));
        }

        public bool IsInside(GridPosition cell)
        {
            return Map.IsInside(cell);
        }

        /// <summary>Position du monde → position relative dans la carte, de (0,0) en bas à gauche à (1,1) en haut à droite.</summary>
        public Vector2 WorldToNormalized(Vector3 world)
        {
            Rect bounds = WorldBounds;
            return new Vector2((world.x - bounds.xMin) / bounds.width, (world.y - bounds.yMin) / bounds.height);
        }

        public Vector3 NormalizedToWorld(Vector2 normalized)
        {
            Rect bounds = WorldBounds;
            return new Vector3(bounds.xMin + normalized.x * bounds.width, bounds.yMin + normalized.y * bounds.height, 0f);
        }

        private void ComputeBounds()
        {
            Vector3 first = CellToWorld(new GridPosition(0, 0));
            Vector3 last = CellToWorld(new GridPosition(Map.Width - 1, Map.Height - 1));
            Vector3 stepX = CellToWorld(new GridPosition(1, 0)) - first;
            Vector3 stepY = CellToWorld(new GridPosition(0, 1)) - first;
            float halfWidth = Mathf.Abs(stepX.x) / 2f;
            float halfHeight = Mathf.Abs(stepY.y) / 2f;

            float xMin = Mathf.Min(first.x, last.x) - halfWidth;
            float yMin = Mathf.Min(first.y, last.y) - halfHeight;
            float xMax = Mathf.Max(first.x, last.x) + halfWidth;
            float yMax = Mathf.Max(first.y, last.y) + halfHeight;
            WorldBounds = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        private void GenerateTiles()
        {
            for (int y = 0; y < Map.Height; y++)
            {
                for (int x = 0; x < Map.Width; x++)
                {
                    var position = new GridPosition(x, y);
                    SpriteRenderer tile = SpriteFactory.CreateRenderer(
                        "Tile " + x + "," + y, transform, TileColor(Map.GetTile(position), x, y), 0);
                    tile.transform.position = CellToWorld(position);
                    tile.transform.localScale = new Vector3(0.96f, 0.96f, 1f);
                }
            }
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
