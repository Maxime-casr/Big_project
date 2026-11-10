using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BigProject.Client
{
    /// <summary>
    /// Caméra orthographique de combat. Par défaut elle suit le personnage contrôlé (ou son fantôme pendant
    /// la planification). Le déplacer depuis la minimap la détache ; Espace, un nouveau pas ou un changement
    /// de joueur la recolle. La molette règle le zoom.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        private const float DefaultSize = 3.8f;
        private const float MinSize = 2.5f;
        private const float MaxSize = 7f;
        private const float Margin = 0.5f;
        private const float SmoothTime = 0.15f;

        private Camera cam;
        private BoardView board;
        private Func<Vector3?> followTarget;
        private Func<bool> isPointerOverUi;
        private Vector3 focus;
        private Vector3 velocity;
        private bool following = true;

        public bool IsFollowing => following;

        /// <summary>Zone du monde actuellement visible (pour le cadre de la minimap).</summary>
        public Rect VisibleWorldRect
        {
            get
            {
                float halfHeight = cam.orthographicSize;
                float halfWidth = halfHeight * cam.aspect;
                Vector3 position = transform.position;
                return new Rect(position.x - halfWidth, position.y - halfHeight, halfWidth * 2f, halfHeight * 2f);
            }
        }

        public void Init(BoardView boardView, Func<Vector3?> followTargetProvider, Func<bool> pointerOverUi)
        {
            cam = GetComponent<Camera>();
            board = boardView;
            followTarget = followTargetProvider;
            isPointerOverUi = pointerOverUi;

            cam.orthographic = true;
            cam.backgroundColor = new Color(0.07f, 0.08f, 0.11f);
            cam.orthographicSize = DefaultSize;

            Vector3? target = followTarget();
            focus = target ?? board.NormalizedToWorld(new Vector2(0.5f, 0.5f));
            transform.position = ClampToBoard(new Vector3(focus.x, focus.y, -10f));
            following = true;
        }

        /// <summary>Recolle la caméra sur le personnage contrôlé.</summary>
        public void Recenter()
        {
            following = true;
        }

        /// <summary>Déplace la caméra librement (minimap) : elle cesse de suivre le personnage.</summary>
        public void FocusOn(Vector3 world)
        {
            following = false;
            focus = world;
        }

        private void LateUpdate()
        {
            if (board == null)
            {
                return;
            }

            HandleZoom();

            if (following)
            {
                Vector3? target = followTarget();
                if (target.HasValue)
                {
                    focus = target.Value;
                }
            }

            Vector3 desired = ClampToBoard(new Vector3(focus.x, focus.y, -10f));
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, SmoothTime);
        }

        private void HandleZoom()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || (isPointerOverUi != null && isPointerOverUi()))
            {
                return;
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                cam.orthographicSize = Mathf.Clamp(cam.orthographicSize - Mathf.Sign(scroll) * 0.4f, MinSize, MaxSize);
            }
        }

        private Vector3 ClampToBoard(Vector3 position)
        {
            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * cam.aspect;
            Rect bounds = board.WorldBounds;
            bounds.xMin -= Margin;
            bounds.yMin -= Margin;
            bounds.xMax += Margin;
            bounds.yMax += Margin;

            // Si la vue dépasse la carte, on la centre ; sinon on l'empêche de sortir.
            position.x = halfWidth * 2f >= bounds.width
                ? bounds.center.x
                : Mathf.Clamp(position.x, bounds.xMin + halfWidth, bounds.xMax - halfWidth);
            position.y = halfHeight * 2f >= bounds.height
                ? bounds.center.y
                : Mathf.Clamp(position.y, bounds.yMin + halfHeight, bounds.yMax - halfHeight);
            return position;
        }
    }
}
