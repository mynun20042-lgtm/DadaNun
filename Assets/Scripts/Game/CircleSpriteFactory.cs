using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Generates a simple filled-circle sprite procedurally (no image assets).
    /// The base sprite is white so it can be tinted to any color via Image.color / SpriteRenderer.color.
    /// </summary>
    public static class CircleSpriteFactory
    {
        private static Sprite _cached;

        /// <summary>A shared white circle sprite (128px). Tint via the renderer's color.</summary>
        public static Sprite WhiteCircle
        {
            get
            {
                if (_cached == null)
                    _cached = CreateCircle(128);
                return _cached;
            }
        }

        public static Sprite CreateCircle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            float r = size * 0.5f;
            float rInner = r - 1.5f; // for a soft 1.5px anti-aliased edge
            Vector2 c = new Vector2(r, r);
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                    float a;
                    if (d <= rInner) a = 1f;
                    else if (d >= r) a = 0f;
                    else a = 1f - (d - rInner) / (r - rInner);

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
