using System.Collections.Generic;
using BigProject.Core.Grid;
using UnityEngine;

namespace BigProject.Client
{
    public enum HighlightLayer
    {
        Reachable,
        CastRange,
        AllyMove,
        AllyCast,
        PlannedMove,
        PlannedCast,
        Path,
        Area,
        Flash
    }

    /// <summary>Surbrillances de cases par couche (portée, chemin prévu, zone d'effet...).</summary>
    public sealed class TileHighlighter : MonoBehaviour
    {
        private readonly Dictionary<HighlightLayer, List<SpriteRenderer>> pools =
            new Dictionary<HighlightLayer, List<SpriteRenderer>>();

        private BoardView board;

        public void Init(BoardView boardView)
        {
            board = boardView;
        }

        public void Set(HighlightLayer layer, IEnumerable<GridPosition> cells)
        {
            if (!pools.TryGetValue(layer, out List<SpriteRenderer> pool))
            {
                pool = new List<SpriteRenderer>();
                pools.Add(layer, pool);
            }

            int used = 0;
            if (cells != null)
            {
                foreach (GridPosition cell in cells)
                {
                    if (used == pool.Count)
                    {
                        SpriteRenderer renderer = SpriteFactory.CreateRenderer(
                            "Highlight " + layer, transform, ColorOf(layer), OrderOf(layer));
                        renderer.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
                        pool.Add(renderer);
                    }

                    pool[used].transform.position = board.CellToWorld(cell);
                    pool[used].gameObject.SetActive(true);
                    used++;
                }
            }

            for (int i = used; i < pool.Count; i++)
            {
                pool[i].gameObject.SetActive(false);
            }
        }

        public void Clear(HighlightLayer layer)
        {
            Set(layer, null);
        }

        public void ClearAll()
        {
            foreach (HighlightLayer layer in pools.Keys)
            {
                Clear(layer);
            }
        }

        private static Color ColorOf(HighlightLayer layer)
        {
            switch (layer)
            {
                case HighlightLayer.Reachable: return new Color(0.2f, 0.6f, 1f, 0.35f);
                case HighlightLayer.CastRange: return new Color(1f, 0.45f, 0.2f, 0.30f);
                case HighlightLayer.AllyMove: return new Color(0.35f, 0.6f, 1f, 0.28f);
                case HighlightLayer.AllyCast: return new Color(0.7f, 0.45f, 1f, 0.45f);
                case HighlightLayer.PlannedMove: return new Color(0.3f, 0.9f, 0.4f, 0.45f);
                case HighlightLayer.PlannedCast: return new Color(1f, 0.55f, 0.1f, 0.55f);
                case HighlightLayer.Path: return new Color(1f, 1f, 1f, 0.45f);
                case HighlightLayer.Area: return new Color(1f, 0.2f, 0.2f, 0.55f);
                default: return new Color(1f, 0.85f, 0.2f, 0.75f);
            }
        }

        private static int OrderOf(HighlightLayer layer)
        {
            return 1 + (int)layer;
        }
    }
}
