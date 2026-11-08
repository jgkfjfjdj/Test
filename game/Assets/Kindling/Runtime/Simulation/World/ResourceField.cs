using System;
using System.Collections.Generic;
using System.Numerics;
using Kindling.Core.World;
using Kindling.Simulation.Collision;

namespace Kindling.Simulation.World
{
    /// <summary>
    /// Live state of the map's resource nodes: alive/depleted (replicated per chunk as a bitset), remaining
    /// yield (server only) and respawn times. Node index = position in <see cref="StaticMap.Nodes"/>.
    /// </summary>
    public sealed class ResourceField
    {
        readonly StaticMap m_Map;
        readonly WorldGrid m_Grid;
        readonly bool[] m_Alive;
        readonly float[] m_Remaining;
        readonly double[] m_RespawnAt;
        readonly byte[] m_Hits;
        readonly Dictionary<ChunkCoord, List<int>> m_ByChunk = new Dictionary<ChunkCoord, List<int>>();
        readonly Dictionary<ChunkCoord, uint> m_ChunkVersions = new Dictionary<ChunkCoord, uint>();
        readonly List<int> m_Changed = new List<int>();
        readonly HashSet<int> m_SaveDirty = new HashSet<int>();
        static readonly List<int> s_Empty = new List<int>();

        public ResourceField(StaticMap map, WorldGrid grid)
        {
            m_Map = map;
            m_Grid = grid;
            int n = map.Nodes.Count;
            m_Alive = new bool[n];
            m_Remaining = new float[n];
            m_RespawnAt = new double[n];
            m_Hits = new byte[n];
            for (int i = 0; i < n; i++)
            {
                m_Alive[i] = true;
                m_Remaining[i] = map.Nodes[i].Def.TotalYield;
                ChunkCoord c = ChunkOf(map.Nodes[i].Position);
                if (!m_ByChunk.TryGetValue(c, out List<int> list))
                {
                    m_ByChunk[c] = list = new List<int>();
                }

                list.Add(i);
            }
        }

        public int Count => m_Alive.Length;

        public NodePlacement Placement(int index) => m_Map.Nodes[index];

        public bool IsAlive(int index) => m_Alive[index];

        public float Remaining(int index) => m_Remaining[index];

        public double RespawnAt(int index) => m_RespawnAt[index];

        public ChunkCoord ChunkOf(Vector3 p) => ChunkCoord.FromWorld(p.X, p.Z, m_Grid.ChunkSize);

        public IReadOnlyList<int> NodesInChunk(ChunkCoord chunk) => m_ByChunk.TryGetValue(chunk, out List<int> list) ? list : s_Empty;

        public uint ChunkVersion(ChunkCoord chunk) => m_ChunkVersions.TryGetValue(chunk, out uint v) ? v : 0;

        /// <summary>
        /// Removes up to <paramref name="amount"/> yield. Returns what was actually removed; the node is
        /// depleted (and scheduled to respawn) when nothing is left.
        /// </summary>
        public float Harvest(int index, float amount, double now)
        {
            if (!m_Alive[index] || amount <= 0)
            {
                return 0f;
            }

            float taken = Math.Min(amount, m_Remaining[index]);
            m_Remaining[index] -= taken;
            m_Hits[index]++;
            m_SaveDirty.Add(index);
            if (m_Remaining[index] <= 0.0001f)
            {
                m_Alive[index] = false;
                m_Remaining[index] = 0f;
                m_RespawnAt[index] = now + m_Map.Nodes[index].Def.RespawnSeconds;
                Changed(index);
            }

            return taken;
        }

        /// <summary>Respawns nodes whose timer ran out.</summary>
        public void Update(double now)
        {
            for (int i = 0; i < m_Alive.Length; i++)
            {
                if (!m_Alive[i] && now >= m_RespawnAt[i])
                {
                    m_Alive[i] = true;
                    m_Remaining[i] = m_Map.Nodes[i].Def.TotalYield;
                    m_RespawnAt[i] = 0;
                    m_Hits[i] = 0;
                    Changed(i);
                }
            }
        }

        /// <summary>
        /// The node's current "sweet spot": a point on its surface that gives bonus yield when hit. It
        /// moves after every hit (deterministic from the node index and hit count).
        /// </summary>
        public Vector3 SweetSpot(int index)
        {
            NodePlacement n = m_Map.Nodes[index];
            uint h = (uint)(index * 2654435761u) ^ (uint)(m_Hits[index] * 40503u);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            float angle = (h % 360) * (float)(Math.PI / 180.0);
            float heightFraction = n.Def.Size.Y > 3f ? 0.15f + (h >> 9) % 100 / 100f * 0.12f : 0.5f + (h >> 9) % 100 / 100f * 0.3f;
            float y = n.Position.Y + Math.Min(n.Def.Size.Y * heightFraction, 1.7f);
            return new Vector3(n.Position.X + (float)Math.Sin(angle) * n.Def.Size.X * 0.5f, y, n.Position.Z + (float)Math.Cos(angle) * n.Def.Size.Z * 0.5f);
        }

        /// <summary>Nearest alive node hit by a ray within <paramref name="maxDistance"/>.</summary>
        public bool RayCast(Vector3 origin, Vector3 direction, float maxDistance, out int index, out Vector3 hitPoint)
        {
            index = -1;
            hitPoint = default;
            float best = maxDistance;
            ChunkCoord center = ChunkOf(origin);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    foreach (int i in NodesInChunk(new ChunkCoord(center.X + dx, center.Z + dz)))
                    {
                        if (!m_Alive[i])
                        {
                            continue;
                        }

                        if (RayAabb(origin, direction, m_Map.Nodes[i].Bounds, out float t) && t < best)
                        {
                            best = t;
                            index = i;
                        }
                    }
                }
            }

            if (index >= 0)
            {
                hitPoint = origin + direction * best;
                return true;
            }

            return false;
        }

        /// <summary>Nearest alive node matching <paramref name="filter"/> within <paramref name="radius"/> (horizontal).</summary>
        public int FindNearest(Vector3 position, float radius, Func<NodePlacement, bool> filter)
        {
            int found = -1;
            float best = radius * radius;
            int reach = (int)Math.Ceiling(radius / m_Grid.ChunkSize);
            ChunkCoord center = ChunkOf(position);
            for (int dx = -reach; dx <= reach; dx++)
            {
                for (int dz = -reach; dz <= reach; dz++)
                {
                    foreach (int i in NodesInChunk(new ChunkCoord(center.X + dx, center.Z + dz)))
                    {
                        NodePlacement n = m_Map.Nodes[i];
                        if (!m_Alive[i] || (filter != null && !filter(n)))
                        {
                            continue;
                        }

                        float ddx = n.Position.X - position.X;
                        float ddz = n.Position.Z - position.Z;
                        float d2 = ddx * ddx + ddz * ddz;
                        if (d2 < best)
                        {
                            best = d2;
                            found = i;
                        }
                    }
                }
            }

            return found;
        }

        /// <summary>Restores saved state (alive flags, remaining yield, respawn times).</summary>
        public void Restore(int index, bool alive, float remaining, double respawnAt)
        {
            if (index < 0 || index >= m_Alive.Length)
            {
                return;
            }

            m_Alive[index] = alive;
            m_Remaining[index] = alive ? Math.Max(0f, Math.Min(remaining, m_Map.Nodes[index].Def.TotalYield)) : 0f;
            m_RespawnAt[index] = alive ? 0 : respawnAt;
            Changed(index);
        }

        public void ConsumeChanges(List<int> changed)
        {
            changed.Clear();
            changed.AddRange(m_Changed);
            m_Changed.Clear();
        }

        /// <summary>Alive bits for the nodes of a chunk, in <see cref="NodesInChunk"/> order.</summary>
        public void WriteAliveBits(ChunkCoord chunk, List<bool> bits)
        {
            bits.Clear();
            foreach (int i in NodesInChunk(chunk))
            {
                bits.Add(m_Alive[i]);
            }
        }

        /// <summary>Nodes whose saved state changed since the last call (any harvest, depletion or respawn).</summary>
        public void ConsumeSaveDirty(List<int> nodes)
        {
            nodes.Clear();
            nodes.AddRange(m_SaveDirty);
            m_SaveDirty.Clear();
        }

        void Changed(int index)
        {
            m_SaveDirty.Add(index);
            ChunkCoord c = ChunkOf(m_Map.Nodes[index].Position);
            m_ChunkVersions[c] = ChunkVersion(c) + 1;
            m_Changed.Add(index);
        }

        public static bool RayAabb(Vector3 origin, Vector3 dir, in Aabb box, out float t)
        {
            float tMin = 0f;
            float tMax = float.MaxValue;
            for (int axis = 0; axis < 3; axis++)
            {
                float o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
                float d = axis == 0 ? dir.X : axis == 1 ? dir.Y : dir.Z;
                float min = axis == 0 ? box.Min.X : axis == 1 ? box.Min.Y : box.Min.Z;
                float max = axis == 0 ? box.Max.X : axis == 1 ? box.Max.Y : box.Max.Z;
                if (Math.Abs(d) < 1e-8f)
                {
                    if (o < min || o > max)
                    {
                        t = 0;
                        return false;
                    }

                    continue;
                }

                float inv = 1f / d;
                float t1 = (min - o) * inv;
                float t2 = (max - o) * inv;
                if (t1 > t2)
                {
                    (t1, t2) = (t2, t1);
                }

                tMin = Math.Max(tMin, t1);
                tMax = Math.Min(tMax, t2);
                if (tMin > tMax)
                {
                    t = 0;
                    return false;
                }
            }

            t = tMin;
            return true;
        }
    }
}
