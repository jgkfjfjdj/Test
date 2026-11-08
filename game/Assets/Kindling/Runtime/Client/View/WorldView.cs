using System.Collections.Generic;
using Kindling.Content;
using Kindling.Core.World;
using Kindling.Simulation.Collision;
using Kindling.Simulation.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kindling.Client.View
{
    /// <summary>
    /// Draws the static map the server sent: greybox geometry, ponds and resource nodes (grouped per chunk
    /// and shown only near the camera). Depleted trees become stumps and rocks rubble (their collision stays).
    /// Also runs the sun and sky from the server's time of day.
    /// </summary>
    public sealed class WorldView
    {
        const float NodeViewDistance = 170f;

        GameObject m_Root;
        StaticMap m_Map;
        Light m_Sun;
        readonly Dictionary<ChunkCoord, GameObject> m_NodeChunks = new Dictionary<ChunkCoord, GameObject>();
        NodeView[] m_Nodes;
        float m_NextCull;

        struct NodeView
        {
            public Transform Main;
            public GameObject Canopy;
            public Vector3 FullScale;
            public bool Alive;
            public bool Pickable;
            public bool Tall;
        }

        public void Build(StaticMap map, GreyboxMaterials materials)
        {
            if (m_Root != null && m_Map == map)
            {
                m_Root.SetActive(true);
                return;
            }

            Destroy();
            m_Map = map;
            m_Root = new GameObject($"World (seed {map.Seed})");
            Object.DontDestroyOnLoad(m_Root);

            GameObject ground = MakeBox("Ground", m_Root.transform, new Vector3(0f, -0.5f, 0f), new Vector3(map.MapSize, 1f, map.MapSize), materials.Ground);
            ground.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var geometry = new GameObject("Geometry").transform;
            geometry.SetParent(m_Root.transform, false);
            foreach (GreyboxPiece piece in map.Pieces)
            {
                Vector3 min = piece.Box.Min.ToUnity();
                Vector3 max = piece.Box.Max.ToUnity();
                MakeBox(piece.Kind.ToString(), geometry, (min + max) * 0.5f, max - min, materials.For(piece.Kind));
            }

            foreach (Aabb pond in map.Ponds)
            {
                Vector3 min = pond.Min.ToUnity();
                Vector3 max = pond.Max.ToUnity();
                GameObject water = MakeBox("Pond", geometry, new Vector3((min.x + max.x) / 2, 0.01f, (min.z + max.z) / 2), new Vector3(max.x - min.x, 0.04f, max.z - min.z), materials.Solid(Palette.Water));
                water.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            BuildNodes(map, materials);

            var sun = new GameObject("Sun");
            sun.transform.SetParent(m_Root.transform, false);
            m_Sun = sun.AddComponent<Light>();
            m_Sun.type = LightType.Directional;
            m_Sun.color = new Color(1f, 0.96f, 0.88f);
            m_Sun.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = AmbientMode.Flat;

            // No fog: its shader variants are stripped unless a scene in the build uses fog.
        }

        void BuildNodes(StaticMap map, GreyboxMaterials materials)
        {
            m_Nodes = new NodeView[map.Nodes.Count];
            Material leaves = materials.Solid(Palette.Leaves);
            for (int i = 0; i < map.Nodes.Count; i++)
            {
                NodePlacement n = map.Nodes[i];
                ChunkCoord chunk = ChunkCoord.FromWorld(n.Position.X, n.Position.Z, WorldGrid.DefaultChunkSize);
                if (!m_NodeChunks.TryGetValue(chunk, out GameObject chunkRoot))
                {
                    chunkRoot = new GameObject($"Nodes {chunk.X},{chunk.Z}");
                    chunkRoot.transform.SetParent(m_Root.transform, false);
                    m_NodeChunks[chunk] = chunkRoot;
                }

                Vector3 size = n.Def.Size.ToUnity();
                Vector3 feet = n.Position.ToUnity();
                bool tall = n.Def.Kind == GatherKind.Wood;
                PrimitiveType shape = tall ? PrimitiveType.Cylinder : n.Def.Pickable ? PrimitiveType.Sphere : PrimitiveType.Cube;
                GameObject main = GameObject.CreatePrimitive(shape);
                Object.Destroy(main.GetComponent<Collider>());
                main.name = n.Def.Name;
                main.transform.SetParent(chunkRoot.transform, false);
                main.transform.localRotation = Quaternion.Euler(0f, n.Yaw, 0f);
                main.GetComponent<MeshRenderer>().sharedMaterial = materials.Solid(Palette.ForNode(n.Def));
                Vector3 scale = tall ? new Vector3(size.x, size.y * 0.5f, size.z) : size; // cylinder mesh is 2 units tall
                main.transform.localScale = scale;
                main.transform.localPosition = feet + new Vector3(0f, size.y * 0.5f, 0f);

                GameObject canopy = null;
                if (tall)
                {
                    canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.Destroy(canopy.GetComponent<Collider>());
                    canopy.name = "Canopy";
                    canopy.transform.SetParent(chunkRoot.transform, false);
                    canopy.transform.localPosition = feet + new Vector3(0f, size.y * 0.85f, 0f);
                    canopy.transform.localScale = new Vector3(3.2f, 3.6f, 3.2f);
                    canopy.GetComponent<MeshRenderer>().sharedMaterial = leaves;
                }

                m_Nodes[i] = new NodeView { Main = main.transform, Canopy = canopy, FullScale = scale, Alive = true, Pickable = n.Def.Pickable, Tall = tall };
            }
        }

        /// <summary>Shows node depletion and culls node chunks far from the camera.</summary>
        public void Sync(ResourceField nodes, Vector3 cameraPosition)
        {
            if (m_Nodes == null || nodes == null)
            {
                return;
            }

            for (int i = 0; i < m_Nodes.Length; i++)
            {
                bool alive = nodes.IsAlive(i);
                if (alive == m_Nodes[i].Alive)
                {
                    continue;
                }

                NodeView v = m_Nodes[i];
                v.Alive = alive;
                if (v.Pickable)
                {
                    v.Main.gameObject.SetActive(alive);
                }
                else
                {
                    // Stump or rubble: the collision box stays, so something stays visible there.
                    Vector3 scale = alive ? v.FullScale : new Vector3(v.FullScale.x, v.Tall ? 0.3f : v.FullScale.y * 0.3f, v.FullScale.z);
                    float height = v.Tall ? scale.y * 2f : scale.y;
                    Vector3 p = v.Main.localPosition;
                    v.Main.localScale = scale;
                    v.Main.localPosition = new Vector3(p.x, height * 0.5f, p.z);
                    if (v.Canopy != null)
                    {
                        v.Canopy.SetActive(alive);
                    }
                }

                m_Nodes[i] = v;
            }

            if (Time.unscaledTime >= m_NextCull)
            {
                m_NextCull = Time.unscaledTime + 0.5f;
                foreach (var pair in m_NodeChunks)
                {
                    float cx = (pair.Key.X + 0.5f) * WorldGrid.DefaultChunkSize - cameraPosition.x;
                    float cz = (pair.Key.Z + 0.5f) * WorldGrid.DefaultChunkSize - cameraPosition.z;
                    bool near = cx * cx + cz * cz <= (NodeViewDistance + 46f) * (NodeViewDistance + 46f);
                    if (pair.Value.activeSelf != near)
                    {
                        pair.Value.SetActive(near);
                    }
                }
            }
        }

        /// <summary>Sun angle, light and sky color for a time of day (0 = sunrise; night at the end of the day).</summary>
        public void UpdateSky(Camera camera, double timeOfDay, float nightFraction)
        {
            if (m_Sun == null)
            {
                return;
            }

            double dayEnd = 1.0 - nightFraction;
            float daylight;
            float sunAngle;
            if (timeOfDay < dayEnd)
            {
                float t = (float)(timeOfDay / dayEnd);
                sunAngle = Mathf.Lerp(-5f, 185f, t);
                daylight = Mathf.Clamp01(Mathf.Min(t, 1f - t) * 8f);
            }
            else
            {
                sunAngle = Mathf.Lerp(185f, 355f, (float)((timeOfDay - dayEnd) / nightFraction));
                daylight = 0f;
            }

            m_Sun.transform.rotation = Quaternion.Euler(sunAngle, -30f, 0f);
            m_Sun.intensity = 0.08f + 1.05f * daylight;
            Color night = new Color(0.07f, 0.09f, 0.16f);
            RenderSettings.ambientLight = Color.Lerp(night, new Color(0.45f, 0.48f, 0.55f), daylight);
            if (camera != null)
            {
                camera.backgroundColor = Color.Lerp(new Color(0.02f, 0.03f, 0.07f), SkyColor, daylight);
            }
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

            m_NodeChunks.Clear();
            m_Nodes = null;
            m_Map = null;
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
