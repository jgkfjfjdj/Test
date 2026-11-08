using System.Collections.Generic;
using Kindling.Content;
using Kindling.Net.Client;
using Kindling.Net.Protocol;
using Kindling.Simulation.World;
using UnityEngine;

namespace Kindling.Client.View
{
    /// <summary>Greybox visuals for the chunk-replicated world entities (deployables, loot, items, bodies, sleepers).</summary>
    public sealed class EntityViews
    {
        readonly GreyboxMaterials m_Materials;
        readonly Dictionary<uint, View> m_Views = new Dictionary<uint, View>();
        readonly List<uint> m_Stale = new List<uint>();
        readonly List<Label> m_Labels = new List<Label>();
        Transform m_Root;
        uint m_SyncedVersion = uint.MaxValue;

        sealed class View
        {
            public GameObject Root;
            public Light Fire;
            public GameObject Flame;
            public ushort TypeId;
        }

        public struct Label
        {
            public Vector3 Position;
            public string Text;
        }

        public EntityViews(GreyboxMaterials materials)
        {
            m_Materials = materials;
        }

        /// <summary>Names to draw over bodies and sleepers.</summary>
        public IReadOnlyList<Label> Labels => m_Labels;

        public void Sync(ClientWorld world)
        {
            if (world.EntityVersion == m_SyncedVersion)
            {
                return;
            }

            m_SyncedVersion = world.EntityVersion;
            if (m_Root == null)
            {
                var go = new GameObject("World entities");
                Object.DontDestroyOnLoad(go);
                m_Root = go.transform;
            }

            m_Stale.Clear();
            m_Stale.AddRange(m_Views.Keys);
            m_Labels.Clear();
            foreach (ChunkEntity e in world.Entities.Values)
            {
                EntityDef def = world.DefOf(e);
                if (def == null)
                {
                    continue;
                }

                m_Stale.Remove(e.Id);
                if (!m_Views.TryGetValue(e.Id, out View view) || view.TypeId != e.TypeId)
                {
                    if (view != null)
                    {
                        Object.Destroy(view.Root);
                    }

                    view = Create(def, e);
                    m_Views[e.Id] = view;
                }

                view.Root.transform.SetPositionAndRotation(e.Position.ToUnity(), Quaternion.Euler(0f, e.Yaw, 0f));
                bool lit = (e.Flags & WorldEntityFlags.Lit) != 0;
                if (view.Fire != null && view.Fire.enabled != lit)
                {
                    view.Fire.enabled = lit;
                    view.Flame.SetActive(lit);
                }

                if (!string.IsNullOrEmpty(e.Label))
                {
                    m_Labels.Add(new Label { Position = e.Position.ToUnity() + Vector3.up * 0.9f, Text = def.Kind == EntityKind.Sleeper ? e.Label + " (sleeping)" : e.Label });
                }
            }

            foreach (uint id in m_Stale)
            {
                Object.Destroy(m_Views[id].Root);
                m_Views.Remove(id);
            }
        }

        public void Clear()
        {
            foreach (View v in m_Views.Values)
            {
                Object.Destroy(v.Root);
            }

            m_Views.Clear();
            m_Labels.Clear();
            m_SyncedVersion = uint.MaxValue;
        }

        View Create(EntityDef def, in ChunkEntity e)
        {
            var view = new View { Root = new GameObject($"{def.Name} #{e.Id}"), TypeId = e.TypeId };
            view.Root.transform.SetParent(m_Root, false);
            Vector3 size = def.Size.ToUnity();
            Material material = m_Materials.Solid(Palette.ForEntity(def));
            switch (def.Kind)
            {
                case EntityKind.Body:
                case EntityKind.Sleeper:
                    // A person lying on the ground.
                    Part(view.Root.transform, PrimitiveType.Capsule, new Vector3(0f, 0.3f, 0f), new Vector3(0.6f, 0.9f, 0.6f), Quaternion.Euler(90f, 0f, 0f), material);
                    break;
                case EntityKind.Breakable:
                    Part(view.Root.transform, PrimitiveType.Cylinder, new Vector3(0f, size.y / 2, 0f), new Vector3(size.x, size.y / 2, size.z), Quaternion.identity, material);
                    break;
                case EntityKind.DroppedItem:
                    Part(view.Root.transform, PrimitiveType.Cube, new Vector3(0f, size.y / 2, 0f), size, Quaternion.Euler(0f, 30f, 0f), material);
                    break;
                default:
                    Part(view.Root.transform, PrimitiveType.Cube, new Vector3(0f, size.y / 2, 0f), size, Quaternion.identity, material);
                    if (def.Processor != null)
                    {
                        view.Flame = Part(view.Root.transform, PrimitiveType.Sphere, new Vector3(0f, size.y + 0.15f, 0f), new Vector3(0.35f, 0.45f, 0.35f), Quaternion.identity, m_Materials.Solid(Palette.Ember));
                        view.Flame.SetActive(false);
                        var lightGo = new GameObject("Fire light");
                        lightGo.transform.SetParent(view.Root.transform, false);
                        lightGo.transform.localPosition = new Vector3(0f, size.y + 0.6f, 0f);
                        view.Fire = lightGo.AddComponent<Light>();
                        view.Fire.type = LightType.Point;
                        view.Fire.color = new Color(1f, 0.6f, 0.25f);
                        view.Fire.range = Mathf.Max(6f, def.HeatRadius * 2.5f);
                        view.Fire.intensity = 2f;
                        view.Fire.enabled = false;
                    }

                    break;
            }

            return view;
        }

        static GameObject Part(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Quaternion rotation, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }
    }
}
