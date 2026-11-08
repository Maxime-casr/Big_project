using UnityEngine;

namespace BigProject.Client
{
    /// <summary>Sprites générés par code, en attendant de vrais assets graphiques.</summary>
    public static class SpriteFactory
    {
        private static Sprite square;

        public static Sprite Square
        {
            get
            {
                if (square == null)
                {
                    var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Point,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    texture.SetPixel(0, 0, Color.white);
                    texture.Apply();

                    square = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                    square.hideFlags = HideFlags.HideAndDontSave;
                }

                return square;
            }
        }

        public static SpriteRenderer CreateRenderer(string name, Transform parent, Color color, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = Square;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }
    }
}
