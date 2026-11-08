using System;
using BigProject.Core.Grid;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>Cadre la caméra orthographique sur la grille, à gauche du panneau d'interface.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        private const float Margin = 0.75f;

        private Camera cam;
        private GridMap map;
        private Func<float> panelWidthProvider;
        private int lastWidth;
        private int lastHeight;
        private float lastPanel;

        public void Init(GridMap gridMap, Func<float> panelWidthInPixels)
        {
            cam = GetComponent<Camera>();
            map = gridMap;
            panelWidthProvider = panelWidthInPixels;
            cam.orthographic = true;
            cam.backgroundColor = new Color(0.07f, 0.08f, 0.11f);
            Apply();
        }

        private void LateUpdate()
        {
            if (map == null)
            {
                return;
            }

            if (Screen.width != lastWidth || Screen.height != lastHeight ||
                !Mathf.Approximately(panelWidthProvider(), lastPanel))
            {
                Apply();
            }
        }

        private void Apply()
        {
            lastWidth = Screen.width;
            lastHeight = Screen.height;
            lastPanel = panelWidthProvider();

            float viewWidth = Mathf.Max(1f, lastWidth - lastPanel);
            float aspect = viewWidth / Mathf.Max(1f, lastHeight);
            float halfHeight = map.Height / 2f + Margin;
            float halfWidth = (map.Width / 2f + Margin) / aspect;
            cam.orthographicSize = Mathf.Max(halfHeight, halfWidth);

            // Décale la caméra pour centrer la grille dans la zone laissée libre par le panneau.
            float worldPerPixel = cam.orthographicSize * 2f / lastHeight;
            transform.position = new Vector3(lastPanel * 0.5f * worldPerPixel, 0f, -10f);
        }
    }
}
