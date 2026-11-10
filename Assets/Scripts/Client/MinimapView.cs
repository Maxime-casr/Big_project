using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using BigProject.Core.Units;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BigProject.Client
{
    /// <summary>
    /// Minimap : la carte, un point par unité (vert = unité contrôlée, bleu = allié, rouge = ennemi) et un
    /// cadre qui montre ce que voit la caméra. Cliquer ou glisser sur la minimap déplace la caméra.
    /// À poser sur une RawImage de l'interface ; le cadre est un enfant (Image) à assigner.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class MinimapView : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        private static readonly Color FloorColor = new Color(0.32f, 0.38f, 0.41f);
        private static readonly Color ObstacleColor = new Color(0.08f, 0.09f, 0.1f);
        private static readonly Color PitColor = new Color(0.03f, 0.06f, 0.18f);

        [SerializeField] private RectTransform frame;
        [Tooltip("Ajuste la hauteur de la minimap pour respecter les proportions de la carte.")]
        [SerializeField] private bool matchMapAspect = true;

        private readonly Dictionary<int, Image> dots = new Dictionary<int, Image>();

        private RawImage mapImage;
        private RectTransform mapRect;
        private MatchState state;
        private CameraRig cameraRig;
        private PlanningController planning;
        private Texture2D texture;

        public RectTransform Frame
        {
            get => frame;
            set => frame = value;
        }

        public void Init(MatchState matchState, CameraRig rig, PlanningController planningController)
        {
            state = matchState;
            cameraRig = rig;
            planning = planningController;

            mapImage = GetComponent<RawImage>();
            mapRect = (RectTransform)transform;

            if (matchMapAspect)
            {
                mapRect.sizeDelta = new Vector2(mapRect.sizeDelta.x, mapRect.sizeDelta.x * state.Map.Height / state.Map.Width);
            }

            BuildTexture();

            foreach (UnitState unit in state.Units)
            {
                var go = new GameObject("Point " + unit.Name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(mapRect, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(0.5f, 0.5f);
                var image = go.GetComponent<Image>();
                image.raycastTarget = false;
                dots.Add(unit.Id, image);
            }

            if (frame != null)
            {
                frame.SetAsLastSibling();
                frame.GetComponent<Graphic>().raycastTarget = false;
            }
        }

        private void LateUpdate()
        {
            if (state == null)
            {
                return;
            }

            UnitState controlled = planning.SelectedUnit;
            float cellWidth = mapRect.rect.width / state.Map.Width;
            float cellHeight = mapRect.rect.height / state.Map.Height;
            float dotSize = Mathf.Max(4f, Mathf.Min(cellWidth, cellHeight) * 0.8f);

            foreach (UnitState unit in state.Units)
            {
                Image dot = dots[unit.Id];
                dot.gameObject.SetActive(unit.IsAlive);
                if (!unit.IsAlive)
                {
                    continue;
                }

                var rect = (RectTransform)dot.transform;
                rect.anchoredPosition = new Vector2((unit.Position.X + 0.5f) * cellWidth, (unit.Position.Y + 0.5f) * cellHeight);
                rect.sizeDelta = new Vector2(dotSize, dotSize);
                dot.color = UnitViewRegistry.ColorFor(unit, controlled);
                if (controlled != null && unit.Id == controlled.Id)
                {
                    rect.SetAsLastSibling();
                }
            }

            UpdateFrame();
        }

        private void UpdateFrame()
        {
            if (frame == null)
            {
                return;
            }

            // Le cadre reste au-dessus des points.
            frame.SetAsLastSibling();

            Rect view = cameraRig.VisibleWorldRect;
            float width = mapRect.rect.width;
            float height = mapRect.rect.height;
            float uMin = Mathf.Clamp01((view.xMin + state.Map.Width / 2f) / state.Map.Width);
            float uMax = Mathf.Clamp01((view.xMax + state.Map.Width / 2f) / state.Map.Width);
            float vMin = Mathf.Clamp01((view.yMin + state.Map.Height / 2f) / state.Map.Height);
            float vMax = Mathf.Clamp01((view.yMax + state.Map.Height / 2f) / state.Map.Height);

            frame.anchorMin = frame.anchorMax = Vector2.zero;
            frame.pivot = Vector2.zero;
            frame.anchoredPosition = new Vector2(uMin * width, vMin * height);
            frame.sizeDelta = new Vector2((uMax - uMin) * width, (vMax - vMin) * height);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            MoveCameraTo(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            MoveCameraTo(eventData);
        }

        private void MoveCameraTo(PointerEventData eventData)
        {
            if (state == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    mapRect, eventData.position, eventData.pressEventCamera, out Vector2 local))
            {
                return;
            }

            Rect rect = mapRect.rect;
            float u = Mathf.Clamp01((local.x - rect.xMin) / rect.width);
            float v = Mathf.Clamp01((local.y - rect.yMin) / rect.height);
            cameraRig.FocusOn(new Vector3((u - 0.5f) * state.Map.Width, (v - 0.5f) * state.Map.Height, 0f));
        }

        private void BuildTexture()
        {
            GridMap map = state.Map;
            texture = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color[map.Width * map.Height];
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    switch (map.GetTile(new GridPosition(x, y)))
                    {
                        case TileType.Obstacle:
                            pixels[y * map.Width + x] = ObstacleColor;
                            break;
                        case TileType.Pit:
                            pixels[y * map.Width + x] = PitColor;
                            break;
                        default:
                            pixels[y * map.Width + x] = FloorColor;
                            break;
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            mapImage.texture = texture;
            mapImage.color = Color.white;
        }

        private void OnDestroy()
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }
    }
}
