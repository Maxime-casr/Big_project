using System;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Copie de la carte de la scène, enregistrée dans un asset pour que le serveur (qui ne charge pas la
    /// scène de combat) joue sur la même carte. Générée depuis la Tilemap par le menu
    /// "Big Project > Générer la carte du serveur" et à chaque enregistrement de la scène de combat.
    /// Se trouve dans Assets/Resources/Maps/CombatMap.asset.
    /// </summary>
    public sealed class MapData : ScriptableObject
    {
        public const string ResourcePath = "Maps/CombatMap";

        [SerializeField] private int width;
        [SerializeField] private int height;
        [SerializeField] private int originX;
        [SerializeField] private int originY;
        [Tooltip("Une case par caractère, ligne par ligne en partant du bas : 0 = sol, 1 = obstacle, 2 = trou.")]
        [SerializeField] private string tiles;
        [SerializeField] private Vector2Int[] blueSpawns = Array.Empty<Vector2Int>();
        [SerializeField] private Vector2Int[] redSpawns = Array.Empty<Vector2Int>();

        public static MapData Load()
        {
            return Resources.Load<MapData>(ResourcePath);
        }

        public void Fill(MapDefinition definition)
        {
            GridMap grid = definition.Grid;
            width = grid.Width;
            height = grid.Height;
            originX = grid.OriginX;
            originY = grid.OriginY;

            var chars = new char[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    chars[y * width + x] = (char)('0' + (int)grid.GetTile(new GridPosition(x, y)));
                }
            }

            tiles = new string(chars);
            blueSpawns = ToArray(definition.BlueSpawns);
            redSpawns = ToArray(definition.RedSpawns);
        }

        /// <summary>Retourne null si l'asset est incohérent (dimensions, caractères inconnus).</summary>
        public MapDefinition ToDefinition()
        {
            if (width <= 0 || height <= 0 || tiles == null || tiles.Length != width * height)
            {
                Debug.LogError("MapData : l'asset de carte est vide ou incohérent. Régénère-le depuis la Tilemap.");
                return null;
            }

            var grid = new GridMap(width, height) { OriginX = originX, OriginY = originY };
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int value = tiles[y * width + x] - '0';
                    if (!Enum.IsDefined(typeof(TileType), value))
                    {
                        Debug.LogError("MapData : type de case inconnu '" + tiles[y * width + x] + "'.");
                        return null;
                    }

                    grid.SetTile(new GridPosition(x, y), (TileType)value);
                }
            }

            var definition = new MapDefinition(grid);
            foreach (Vector2Int spawn in blueSpawns)
            {
                definition.BlueSpawns.Add(new GridPosition(spawn.x, spawn.y));
            }

            foreach (Vector2Int spawn in redSpawns)
            {
                definition.RedSpawns.Add(new GridPosition(spawn.x, spawn.y));
            }

            return definition;
        }

        private static Vector2Int[] ToArray(System.Collections.Generic.List<GridPosition> positions)
        {
            var result = new Vector2Int[positions.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new Vector2Int(positions[i].X, positions[i].Y);
            }

            return result;
        }
    }
}
