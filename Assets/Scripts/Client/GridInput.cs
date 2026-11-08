using System;
using BigProject.Core.Grid;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BigProject.Client
{
    /// <summary>Traduit la souris en événements sur les cases de la grille.</summary>
    public sealed class GridInput : MonoBehaviour
    {
        private Camera targetCamera;
        private BoardView board;
        private GridPosition? hovered;

        public event Action<GridPosition> CellClicked;
        public event Action<GridPosition> CellRightClicked;
        public event Action<GridPosition?> HoverChanged;

        /// <summary>Permet à l'interface d'intercepter la souris (panneau latéral...).</summary>
        public Func<Vector2, bool> IsPointerBlocked { get; set; }

        public void Init(Camera camera, BoardView boardView)
        {
            targetCamera = camera;
            board = boardView;
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || targetCamera == null)
            {
                return;
            }

            Vector2 screenPosition = mouse.position.ReadValue();
            GridPosition? cell = null;

            bool blocked = IsPointerBlocked != null && IsPointerBlocked(screenPosition);
            if (!blocked && targetCamera.pixelRect.Contains(screenPosition))
            {
                GridPosition candidate = board.WorldToCell(targetCamera.ScreenToWorldPoint(screenPosition));
                if (board.IsInside(candidate))
                {
                    cell = candidate;
                }
            }

            if (cell != hovered)
            {
                hovered = cell;
                HoverChanged?.Invoke(hovered);
            }

            if (!cell.HasValue)
            {
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                CellClicked?.Invoke(cell.Value);
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                CellRightClicked?.Invoke(cell.Value);
            }
        }
    }
}
