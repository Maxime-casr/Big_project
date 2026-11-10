using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace BigProject.Client
{
    /// <summary>
    /// Transforme les Tilemaps de la scène en carte de jeu, à poser sur l'objet Grid. Tout se dessine à la
    /// palette de tuiles :
    /// - "Sol" : chaque case qui a une tuile est jouable ; une case vide à l'intérieur du sol est un trou ;
    /// - "Obstacles" : une tuile bloque le passage et la vue ;
    /// - "Départ bleu" / "Départ rouge" : chaque tuile est une case de départ pour un joueur de l'équipe
    ///   (lues de gauche à droite). S'il en manque, les équipes sont placées automatiquement.
    /// Les coordonnées des Tilemaps peuvent être négatives et centrées en .5 : la conversion passe toujours
    /// par la Tilemap (GetCellCenterWorld / WorldToCell), jamais par un calcul manuel.
    /// </summary>
    public sealed class MapSource : MonoBehaviour
    {
        [Header("Sol : toute case avec une tuile est jouable")]
        [SerializeField] private Tilemap ground;

        [Header("Obstacles (optionnel) : bloquent le passage et la vue")]
        [SerializeField] private Tilemap obstacles;

        [Header("Cases de départ (optionnel) : une tuile = un joueur")]
        [SerializeField] private Tilemap blueSpawns;
        [SerializeField] private Tilemap redSpawns;

        [Tooltip("Cache les tuiles de départ pendant le combat : ce ne sont que des repères pour l'éditeur.")]
        [SerializeField] private bool hideSpawnMarkersInGame = true;

        public Tilemap Ground
        {
            get
            {
                if (ground == null)
                {
                    foreach (Tilemap candidate in GetComponentsInChildren<Tilemap>(true))
                    {
                        if (candidate != obstacles && candidate != blueSpawns && candidate != redSpawns)
                        {
                            ground = candidate;
                            break;
                        }
                    }
                }

                return ground;
            }
        }

        /// <summary>Plus petit rectangle contenant toutes les tuiles du sol. False s'il n'y en a aucune.</summary>
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

        /// <summary>Le sol de la scène a-t-il les dimensions de cette carte ?</summary>
        public bool Matches(GridMap map)
        {
            return TryGetGroundBounds(out BoundsInt bounds) && bounds.size.x == map.Width && bounds.size.y == map.Height;
        }

        /// <summary>Cache les repères de départ pendant le combat, si l'option est active.</summary>
        public void HideSpawnMarkers()
        {
            if (!hideSpawnMarkersInGame)
            {
                return;
            }

            SetRendererEnabled(blueSpawns, false);
            SetRendererEnabled(redSpawns, false);
        }

        /// <summary>Lit les Tilemaps. Retourne null s'il n'y a aucune tuile de sol.</summary>
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
            ReadSpawns(blueSpawns, bounds, definition.BlueSpawns);
            ReadSpawns(redSpawns, bounds, definition.RedSpawns);
            return definition;
        }

        private TileType ReadCell(Vector3Int cell)
        {
            if (!Ground.HasTile(cell))
            {
                return TileType.Pit;
            }

            return obstacles != null && obstacles.HasTile(cell) ? TileType.Obstacle : TileType.Floor;
        }

        /// <summary>Les tuiles de départ, de gauche à droite puis de bas en haut : l'ordre est donc toujours le même.</summary>
        private static void ReadSpawns(Tilemap source, BoundsInt bounds, List<GridPosition> target)
        {
            if (source == null)
            {
                return;
            }

            var cells = new List<Vector3Int>();
            foreach (Vector3Int cell in source.cellBounds.allPositionsWithin)
            {
                if (source.HasTile(cell) && cell.x >= bounds.xMin && cell.x < bounds.xMax &&
                    cell.y >= bounds.yMin && cell.y < bounds.yMax)
                {
                    cells.Add(cell);
                }
            }

            cells.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            foreach (Vector3Int cell in cells)
            {
                target.Add(new GridPosition(cell.x - bounds.xMin, cell.y - bounds.yMin));
            }
        }

        private static void SetRendererEnabled(Tilemap tilemap, bool enabled)
        {
            if (tilemap == null)
            {
                return;
            }

            var renderer = tilemap.GetComponent<TilemapRenderer>();
            if (renderer != null)
            {
                renderer.enabled = enabled;
            }
        }
    }
}
