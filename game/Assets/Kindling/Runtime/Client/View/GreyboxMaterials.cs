using System.Collections.Generic;
using Kindling.Simulation.World;
using UnityEngine;

namespace Kindling.Client.View
{
    /// <summary>
    /// Materials for greybox geometry, made at runtime from the render pipeline's default material
    /// (which a primitive receives), so no material assets or Shader.Find are needed in builds.
    /// </summary>
    public sealed class GreyboxMaterials
    {
        // Engine code stripping keeps only components referenced from code; CreatePrimitive needs these.
        static readonly System.Type[] s_KeepComponents = { typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider), typeof(CapsuleCollider), typeof(SphereCollider) };

        readonly Dictionary<GreyboxKind, Material> m_ByKind = new Dictionary<GreyboxKind, Material>();
        readonly Material m_Template;
        Texture2D m_Checker;

        public GreyboxMaterials()
        {
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_Template = probe.GetComponent<MeshRenderer>().sharedMaterial;
            Object.Destroy(probe);
            Ground = Make(new Color(0.42f, 0.45f, 0.40f), tiling: 256f);
            Remote = Make(new Color(0.95f, 0.55f, 0.20f), tiling: 0f);
            RemoteAway = Make(new Color(0.55f, 0.55f, 0.58f), tiling: 0f);
            Visor = Make(new Color(0.1f, 0.1f, 0.12f), tiling: 0f);
        }

        public static int KeptComponentCount => s_KeepComponents.Length;

        public Material Ground { get; }

        public Material Remote { get; }

        public Material RemoteAway { get; }

        public Material Visor { get; }

        public Material For(GreyboxKind kind)
        {
            if (!m_ByKind.TryGetValue(kind, out Material material))
            {
                material = Make(ColorFor(kind), tiling: 0f);
                m_ByKind[kind] = material;
            }

            return material;
        }

        static Color ColorFor(GreyboxKind kind)
        {
            switch (kind)
            {
                case GreyboxKind.Wall: return new Color(0.55f, 0.53f, 0.50f);
                case GreyboxKind.Crate: return new Color(0.60f, 0.47f, 0.32f);
                case GreyboxKind.Step: return new Color(0.70f, 0.70f, 0.72f);
                case GreyboxKind.Platform: return new Color(0.62f, 0.64f, 0.68f);
                case GreyboxKind.TunnelRoof:
                case GreyboxKind.TunnelWall: return new Color(0.40f, 0.48f, 0.58f);
                case GreyboxKind.Tower: return new Color(0.50f, 0.44f, 0.55f);
                default: return Color.gray;
            }
        }

        Material Make(Color color, float tiling)
        {
            var material = new Material(m_Template) { color = color };
            if (tiling > 0f)
            {
                material.mainTexture = Checker();
                material.mainTextureScale = new Vector2(tiling, tiling);
            }

            return material;
        }

        Texture2D Checker()
        {
            if (m_Checker != null)
            {
                return m_Checker;
            }

            const int size = 64;
            m_Checker = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4, name = "GreyboxChecker" };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool dark = (x < size / 2) ^ (y < size / 2);
                    bool line = x == 0 || y == 0;
                    byte v = line ? (byte)150 : dark ? (byte)205 : (byte)230;
                    pixels[y * size + x] = new Color32(v, v, v, 255);
                }
            }

            m_Checker.SetPixels32(pixels);
            m_Checker.Apply(true, true);
            return m_Checker;
        }
    }
}
