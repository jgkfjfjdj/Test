using Kindling.Simulation.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kindling.Client.View
{
    /// <summary>Draws the greybox map from the same layout the simulation collides with, plus sun and sky.</summary>
    public sealed class WorldView
    {
        GameObject m_Root;
        int m_Seed = int.MinValue;

        public void Build(GreyboxLayout layout, GreyboxMaterials materials)
        {
            if (m_Root != null && m_Seed == layout.Seed)
            {
                m_Root.SetActive(true);
                return;
            }

            Destroy();
            m_Seed = layout.Seed;
            m_Root = new GameObject($"Greybox (seed {layout.Seed})");
            Object.DontDestroyOnLoad(m_Root);

            GameObject ground = MakeBox("Ground", m_Root.transform, new Vector3(0f, -0.5f, 0f), new Vector3(GreyboxLayout.MapSize, 1f, GreyboxLayout.MapSize), materials.Ground);
            ground.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            foreach (GreyboxPiece piece in layout.Pieces)
            {
                Vector3 min = new Vector3(piece.Box.Min.X, piece.Box.Min.Y, piece.Box.Min.Z);
                Vector3 max = new Vector3(piece.Box.Max.X, piece.Box.Max.Y, piece.Box.Max.Z);
                MakeBox(piece.Kind.ToString(), m_Root.transform, (min + max) * 0.5f, max - min, materials.For(piece.Kind));
            }

            var sun = new GameObject("Sun");
            sun.transform.SetParent(m_Root.transform, false);
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.48f, 0.55f);

            // No fog: its shader variants are stripped unless a scene in the build uses fog.
        }

        public static Color SkyColor => new Color(0.62f, 0.72f, 0.82f);

        public void Hide()
        {
            if (m_Root != null)
            {
                m_Root.SetActive(false);
            }
        }

        public void Destroy()
        {
            if (m_Root != null)
            {
                Object.Destroy(m_Root);
                m_Root = null;
            }
        }

        static GameObject MakeBox(string name, Transform parent, Vector3 center, Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>()); // collision is simulated, not PhysX
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }
    }
}
