using BigProject.Core.Grid;
using BigProject.Core.Match;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace BigProject.Client
{
    /// <summary>
    /// Transforme la Tilemap de la scène en carte de jeu, à poser sur l'objet Grid.
    /// - Chaque case qui a une tuile dans "Sol" est jouable ; une case vide à l'intérieur du sol est un trou.
    /// - Une tuile de "Sol" listée dans "Tuiles obstacles" (ou présente sur la Tilemap "Obstacles") bloque
    ///   le passage et la vue ; dans "Tuiles trous" (ou Tilemap "Trous"), elle bloque le passage seulement.
    /// - Les coordonnées de la Tilemap peuvent être négatives et centrées en .5 : la conversion passe toujours
    ///   par la Tilemap (GetCellCenterWorld / WorldToCell), jamais par un calcul manuel.
    /// </summary>
    public sealed class MapSource : MonoBehaviour
    {
        [Header("Sol : toute case avec une tuile est jouable")]
        [SerializeField] private Tilemap ground;

        [Header("Obstacles et trous (optionnel)")]
        [SerializeField] private Tilemap obstacles;
        [SerializeField] private Tilemap pits;
        [Tooltip("Tuiles du Sol qui bloquent le passage et la vue (murs, rochers...).")]
        [SerializeField] private TileBase[] obstacleTiles;
        [Tooltip("Tuiles du Sol qui bloquent le passage mais pas la vue (eau, vide...).")]
        [SerializeField] private TileBase[] pitTiles;

        [Header("Cases de départ (optionnel)")]
        [Tooltip("Objets placés sur la carte : un par joueur de l'équipe bleue. Vide = placement automatique.")]
        [SerializeField] private Transform[] blueSpawns;
        [SerializeField] private Transform[] redSpawns;

        public Tilemap Ground
        {
            get
            {
                if (ground == null)
                {
                    ground = GetComponentInChildren<Tilemap>();
                }

                return ground;
            }
        }

        /// <summary>Taille de la zone de sol (plus petit rectangle contenant toutes les tuiles).</summary>
        public bool TryGetGroundBounds(out BoundsInt bounds)
        {
            bounds = default;
            Tilemap tilemap = Ground;
            if (tilemap == null)
            {
                return false;
            }

            bool any = false;
            Vector3Int min = Vector3Int.zero;
            Vector3Int max = Vector3Int.zero;
            foreach (Vector3Int cell in tilemap.cellBounds.allPositionsWithin)
            {
                if (!tilemap.HasTile(cell))
                {
                    continue;
                }

                if (!any)
                {
                    min = max = cell;
                    any = true;
                }
                else
                {
                    min = Vector3Int.Min(min, cell);
                    max = Vector3Int.Max(max, cell);
                }
            }

            if (any)
            {
                bounds = new BoundsInt(min.x, min.y, 0, max.x - min.x + 1, max.y - min.y + 1, 1);
            }

            return any;
        }

        /// <summary>La Tilemap de la scène a-t-elle les dimensions de cette carte ?</summary>
        public bool Matches(GridMap map)
        {
            return TryGetGroundBounds(out BoundsInt bounds) && bounds.size.x == map.Width && bounds.size.y == map.Height;
        }

        /// <summary>Lit la Tilemap. Retourne null s'il n'y a aucune tuile de sol.</summary>
        public MapDefinition Read()
        {
            if (!TryGetGroundBounds(out BoundsInt bounds))
            {
                return null;
            }

            var grid = new GridMap(bounds.size.x, bounds.size.y) { OriginX = bounds.xMin, OriginY = bounds.yMin };
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var cell = new Vector3Int(bounds.xMin + x, bounds.yMin + y, 0);
                    grid.SetTile(new GridPosition(x, y), ReadCell(cell));
                }
            }

            var definition = new MapDefinition(grid);
            AddSpawns(definition.BlueSpawns, blueSpawns, bounds);
            AddSpawns(definition.RedSpawns, redSpawns, bounds);
            return definition;
        }

        private TileType ReadCell(Vector3Int cell)
        {
            if (obstacles != null && obstacles.HasTile(cell))
            {
                return TileType.Obstacle;
            }

            if (pits != null && pits.HasTile(cell))
            {
                return TileType.Pit;
            }

            TileBase tile = Ground.GetTile(cell);
            if (tile == null)
            {
                return TileType.Pit;
            }

            if (Contains(obstacleTiles, tile))
            {
                return TileType.Obstacle;
            }

            return Contains(pitTiles, tile) ? TileType.Pit : TileType.Floor;
        }

        private void AddSpawns(System.Collections.Generic.List<GridPosition> target, Transform[] spawns, BoundsInt bounds)
        {
            if (spawns == null)
            {
                return;
            }

            foreach (Transform spawn in spawns)
            {
                if (spawn == null)
                {
                    continue;
                }

                Vector3Int cell = Ground.WorldToCell(spawn.position);
                target.Add(new GridPosition(cell.x - bounds.xMin, cell.y - bounds.yMin));
            }
        }

        private static bool Contains(TileBase[] list, TileBase tile)
        {
            if (list == null)
            {
                return false;
            }

            foreach (TileBase candidate in list)
            {
                if (candidate == tile)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
