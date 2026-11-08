using Kindling.Controls;
using UnityEngine;

namespace Kindling.Client.View
{
    /// <summary>Generated textures, scaled styles and point↔pixel helpers for IMGUI drawing.</summary>
    public sealed class GuiKit
    {
        Texture2D m_Disc;
        Texture2D m_Ring;
        Texture2D m_White;
        float m_StyleScale = -1f;

        public GUIStyle Label { get; private set; }

        public GUIStyle SmallLabel { get; private set; }

        public GUIStyle CenteredLabel { get; private set; }

        public GUIStyle Title { get; private set; }

        public GUIStyle Button { get; private set; }

        public GUIStyle TextField { get; private set; }

        public GUIStyle Toggle { get; private set; }

        /// <summary>Pixels per layout point (iOS: about the Retina scale factor).</summary>
        public static float PixelsPerPoint
        {
            get
            {
                if (Application.isMobilePlatform && Screen.dpi > 0f)
                {
                    return Mathf.Max(1f, Screen.dpi / 163f);
                }

                return Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 600f);
            }
        }

        public Texture2D Disc => m_Disc != null ? m_Disc : m_Disc = MakeCircle(false);

        public Texture2D Ring => m_Ring != null ? m_Ring : m_Ring = MakeCircle(true);

        public Texture2D White => m_White != null ? m_White : m_White = MakeWhite();

        /// <summary>Call at the start of OnGUI (GUI.skin is only available there).</summary>
        public void EnsureStyles()
        {
            float scale = PixelsPerPoint;
            if (Mathf.Approximately(scale, m_StyleScale) && Label != null)
            {
                return;
            }

            m_StyleScale = scale;
            Label = new GUIStyle(GUI.skin.label) { fontSize = Px(15), wordWrap = true };
            Label.normal.textColor = new Color(0.95f, 0.93f, 0.9f);
            SmallLabel = new GUIStyle(Label) { fontSize = Px(11), wordWrap = false };
            CenteredLabel = new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = Px(13), wordWrap = false };
            Title = new GUIStyle(Label) { fontSize = Px(30), fontStyle = FontStyle.Bold };
            Title.normal.textColor = new Color(0.98f, 0.55f, 0.2f);
            Button = new GUIStyle(GUI.skin.button) { fontSize = Px(16), fixedHeight = Px(44) };
            TextField = new GUIStyle(GUI.skin.textField) { fontSize = Px(16), fixedHeight = Px(40), alignment = TextAnchor.MiddleLeft };
            Toggle = new GUIStyle(GUI.skin.toggle) { fontSize = Px(15) };
        }

        public int Px(float points) => Mathf.RoundToInt(points * PixelsPerPoint);

        /// <summary>Layout rectangle (points, origin bottom-left) to an IMGUI rectangle (pixels, origin top-left).</summary>
        public static Rect ToGui(ScreenRect r)
        {
            float s = PixelsPerPoint;
            return new Rect(r.X * s, Screen.height - r.YMax * s, r.Width * s, r.Height * s);
        }

        public static Vector2 ToGui(System.Numerics.Vector2 points)
        {
            float s = PixelsPerPoint;
            return new Vector2(points.X * s, Screen.height - points.Y * s);
        }

        public void DrawCircle(Vector2 center, float radiusPixels, Color color, bool ring = false)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - radiusPixels, center.y - radiusPixels, radiusPixels * 2, radiusPixels * 2), ring ? Ring : Disc, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        public void DrawRect(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, White, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        static Texture2D MakeCircle(bool ring)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = ring ? "GuiRing" : "GuiDisc" };
            var pixels = new Color32[size * size];
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                    float alpha = Mathf.Clamp01(r - d); // 1-pixel soft edge
                    if (ring)
                    {
                        alpha *= Mathf.Clamp01(d - (r - 6f));
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        static Texture2D MakeWhite()
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "GuiWhite" };
            tex.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) });
            tex.Apply(false, true);
            return tex;
        }
    }
}
