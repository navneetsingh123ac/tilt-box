using System;
using System.Collections.Generic;
using TiltBox.Core;
using UnityEngine;

namespace TiltBox.Game
{
    /// <summary>
    /// Week-1 placeholder art: every sprite is baked at runtime from a signed-distance function, so there are
    /// no image assets to manage yet. Week 5 swaps this class for real sprites behind the same members.
    /// </summary>
    public static class PlaceholderArt
    {
        private const int Size = 128;
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        // Toy-room palette.
        public static readonly Color Background = Hex(0xF3EBDD);
        public static readonly Color BoardPanel = Hex(0xCDB894);
        public static readonly Color FloorTile = Hex(0xE8DAC0);
        public static readonly Color Wall = Hex(0x8A765F);
        public static readonly Color Ink = Hex(0x3B3025);

        private static readonly Color[] BlockColours =
        {
            Hex(0xE5484D), // red
            Hex(0x3E7BFA), // blue
            Hex(0xF2B705), // yellow
            Hex(0x2FA66A), // green
            Hex(0x9152D0), // purple
            Hex(0x4A4F5A), // black
        };

        public static readonly Color KeyGold = Hex(0xE8B23A);
        public static readonly Color StickyPink = Hex(0xF29BC1);
        public static readonly Color[] PortalColours = { Hex(0x2BC4C4), Hex(0xF08A24) };

        public static Color ColourOf(BlockColor c) => c == BlockColor.Key ? KeyGold : BlockColours[(int)c];

        public static Sprite RoundedSquare => Get("rsq", p => RoundBox(p, 0.92f, 0.30f));
        public static Sprite Ring => Get("ring", p => Mathf.Abs(RoundBox(p, 0.86f, 0.28f)) - 0.07f);
        public static Sprite Circle => Get("circle", p => p.magnitude - 0.9f);
        public static Sprite Star => Get("star", p => Polygon(p, StarPoints(0.95f, 0.42f, 5)));
        public static Sprite Highlight => Get("hilite", p => RoundBox(p - new Vector2(0f, 0.42f), 0.7f, 0.3f, 0.22f));

        /// <summary>The colour-blind symbol for a block colour: ● ▲ ■ ★ ♥ ◆ (GDD §2).</summary>
        public static Sprite SymbolOf(BlockColor c)
        {
            switch (c)
            {
                case BlockColor.Red: return Get("sym_circle", p => p.magnitude - 0.8f);
                case BlockColor.Blue: return Get("sym_tri", p => Polygon(p, RegularPoints(3, 0.95f, 90f, -0.15f)));
                case BlockColor.Yellow: return Get("sym_square", p => RoundBox(p, 0.68f, 0.08f));
                case BlockColor.Green: return Star;
                case BlockColor.Purple: return Get("sym_heart", Heart);
                case BlockColor.Black: return Get("sym_diamond", p => Polygon(p, RegularPoints(4, 0.95f, 90f, 0f)));
                case BlockColor.Key: return KeySymbol;
                default: throw new ArgumentOutOfRangeException(nameof(c));
            }
        }

        /// <summary>Chevron pointing up (rotate for other directions); drawn on one-way gates.</summary>
        public static Sprite Chevron => Get("chevron", p => Polygon(p, new[]
        {
            new Vector2(0f, 0.75f), new Vector2(0.8f, -0.05f), new Vector2(0.8f, -0.5f),
            new Vector2(0f, 0.3f), new Vector2(-0.8f, -0.5f), new Vector2(-0.8f, -0.05f),
        }));

        /// <summary>Key symbol for key blocks: a ring bow, a shaft and two teeth.</summary>
        public static Sprite KeySymbol => Get("key", p =>
        {
            float bow = Mathf.Abs((p - new Vector2(-0.45f, 0f)).magnitude - 0.3f) - 0.12f;
            float shaft = RoundBox(p - new Vector2(0.25f, 0f), 0.5f, 0.1f, 0.03f);
            float tooth1 = RoundBox(p - new Vector2(0.55f, -0.18f), 0.08f, 0.16f, 0.02f);
            float tooth2 = RoundBox(p - new Vector2(0.3f, -0.15f), 0.07f, 0.12f, 0.02f);
            return Mathf.Min(Mathf.Min(bow, shaft), Mathf.Min(tooth1, tooth2));
        });

        /// <summary>Keyhole: a circle over a tapered slot.</summary>
        public static Sprite KeyholeShape => Get("keyhole", p =>
        {
            float circle = (p - new Vector2(0f, 0.22f)).magnitude - 0.32f;
            float slot = Polygon(p, new[] { new Vector2(-0.13f, 0.1f), new Vector2(0.13f, 0.1f), new Vector2(0.26f, -0.6f), new Vector2(-0.26f, -0.6f) });
            return Mathf.Min(circle, slot);
        });

        /// <summary>Padlock icon for lock walls.</summary>
        public static Sprite Padlock => Get("padlock", p =>
        {
            float body = RoundBox(p - new Vector2(0f, -0.25f), 0.55f, 0.42f, 0.1f);
            float shackle = Mathf.Max(Mathf.Abs((p - new Vector2(0f, 0.2f)).magnitude - 0.36f) - 0.1f, -(p.y - 0.15f));
            return Mathf.Min(body, shackle);
        });

        /// <summary>Portal: a thick ring with a gap-free swirl (rotated in the view).</summary>
        public static Sprite PortalRing => Get("portal", p =>
        {
            float ring = Mathf.Abs(p.magnitude - 0.7f) - 0.14f;
            float swirl = Mathf.Abs(p.magnitude - 0.35f) - 0.07f;
            float cut = Mathf.Max(p.x, -p.y) ; // remove a quarter of the inner swirl so rotation is visible
            return Mathf.Min(ring, Mathf.Max(swirl, -cut));
        });

        /// <summary>Sticky pad: a soft square covered in dots.</summary>
        public static Sprite StickyDots => Get("sticky", p =>
        {
            float best = float.MaxValue;
            for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
                best = Mathf.Min(best, (p - new Vector2(i * 0.55f, j * 0.55f)).magnitude - 0.15f);
            return best;
        });

        /// <summary>Frost overlay for iced blocks: a rounded square with a few diagonal streaks cut out.</summary>
        public static Sprite Frost => Get("frost", p =>
        {
            float box = RoundBox(p, 0.92f, 0.3f);
            float streak = Mathf.Abs(Mathf.Repeat(p.x + p.y + 0.3f, 0.75f) - 0.375f) - 0.05f;
            return Mathf.Max(box, -streak);
        });

        // Light enough that the block's colour still shows through (colour-blind symbol stays visible too).
        public static readonly Color Ice = new Color(0.82f, 0.95f, 1f, 0.6f);

        /// <summary>Rounded square with 9-slice borders, for panels and buttons of any aspect ratio.</summary>
        public static Sprite Panel => Get("panel", p => RoundBox(p, 0.94f, 0.36f), 30);

        private static Sprite Get(string key, Func<Vector2, float> sdf, int border = 0)
        {
            if (Cache.TryGetValue(key, out Sprite s) && s != null) return s;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "placeholder_" + key,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[Size * Size];
            float pixel = 2f / Size; // one pixel in normalised units
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var p = new Vector2((x + 0.5f) * pixel - 1f, (y + 0.5f) * pixel - 1f);
                float a = Mathf.Clamp01(0.5f - sdf(p) / pixel);
                px[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size, 0, SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            s.name = key;
            Cache[key] = s;
            return s;
        }

        // --- signed distance helpers (negative inside, units: half-sprite = 1) ---

        private static float RoundBox(Vector2 p, float half, float radius) => RoundBox(p, half, half, radius);

        private static float RoundBox(Vector2 p, float halfW, float halfH, float radius)
        {
            float qx = Mathf.Abs(p.x) - halfW + radius;
            float qy = Mathf.Abs(p.y) - halfH + radius;
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        private static float Polygon(Vector2 p, Vector2[] v)
        {
            float d = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
            {
                Vector2 e = v[i] - v[j];
                Vector2 w = p - v[j];
                Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, b.magnitude);
                if ((v[i].y > p.y) != (v[j].y > p.y) && p.x < (v[j].x - v[i].x) * (p.y - v[i].y) / (v[j].y - v[i].y) + v[i].x)
                    inside = !inside;
            }
            return inside ? -d : d;
        }

        private static Vector2[] RegularPoints(int n, float r, float startDeg, float yOffset)
        {
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float a = (startDeg + i * 360f / n) * Mathf.Deg2Rad;
                pts[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r + yOffset);
            }
            return pts;
        }

        private static Vector2[] StarPoints(float outer, float inner, int points)
        {
            var pts = new Vector2[points * 2];
            for (int i = 0; i < pts.Length; i++)
            {
                float r = i % 2 == 0 ? outer : inner;
                float a = (90f + i * 180f / points) * Mathf.Deg2Rad;
                pts[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r - 0.05f);
            }
            return pts;
        }

        // Inigo Quilez's exact heart SDF. In heart space the tip is at (0,0) and the shape spans
        // x ±0.6, y 0..1.1; scale by 0.72 and shift so it sits centred in the sprite.
        private static float Heart(Vector2 p)
        {
            const float s = 0.72f;
            Vector2 h = new Vector2(Mathf.Abs(p.x) * s, p.y * s + 0.552f);
            float d;
            if (h.y + h.x > 1f)
                d = (h - new Vector2(0.25f, 0.75f)).magnitude - Mathf.Sqrt(2f) / 4f;
            else
            {
                float a = (h - new Vector2(0f, 1f)).sqrMagnitude;
                float m = 0.5f * Mathf.Max(h.x + h.y, 0f);
                float b = (h - new Vector2(m, m)).sqrMagnitude;
                d = Mathf.Sqrt(Mathf.Min(a, b)) * Mathf.Sign(h.x - h.y);
            }
            return d / s;
        }

        private static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
    }
}
