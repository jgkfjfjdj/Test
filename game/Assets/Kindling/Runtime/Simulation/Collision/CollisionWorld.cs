using System;
using System.Collections.Generic;
using System.Numerics;

namespace Kindling.Simulation.Collision
{
    /// <summary>
    /// Static collision geometry for movement: an infinite ground plane plus axis-aligned boxes in a
    /// uniform XZ grid. Movement is resolved one axis at a time (clip Y, then X, then Z), which is
    /// simple, robust and gives the same result on every platform for the same inputs.
    /// Immutable after construction, so it can be shared between threads; queries take a caller-owned
    /// scratch list to stay allocation-free.
    /// </summary>
    public sealed class CollisionWorld
    {
        /// <summary>Gap kept between colliders after clipping, so boxes never end up touching exactly.</summary>
        public const float Skin = 0.001f;

        readonly Aabb[] m_Boxes;
        readonly Dictionary<long, List<int>> m_Cells = new Dictionary<long, List<int>>();
        readonly float m_CellSize;

        public float GroundHeight { get; }
        public int BoxCount => m_Boxes.Length;
        public IReadOnlyList<Aabb> Boxes => m_Boxes;

        public CollisionWorld(IEnumerable<Aabb> boxes, float groundHeight = 0f, float cellSize = 8f)
        {
            if (!(cellSize > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            }

            m_Boxes = new List<Aabb>(boxes ?? Array.Empty<Aabb>()).ToArray();
            m_CellSize = cellSize;
            GroundHeight = groundHeight;
            for (int i = 0; i < m_Boxes.Length; i++)
            {
                ForEachCell(m_Boxes[i], key =>
                {
                    if (!m_Cells.TryGetValue(key, out var list))
                    {
                        m_Cells[key] = list = new List<int>(4);
                    }

                    list.Add(i);
                });
            }
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        void ForEachCell(in Aabb box, Action<long> action)
        {
            int x0 = (int)Math.Floor(box.Min.X / m_CellSize);
            int x1 = (int)Math.Floor(box.Max.X / m_CellSize);
            int z0 = (int)Math.Floor(box.Min.Z / m_CellSize);
            int z1 = (int)Math.Floor(box.Max.Z / m_CellSize);
            for (int x = x0; x <= x1; x++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    action(Key(x, z));
                }
            }
        }

        /// <summary>
        /// Collects indices of boxes whose cells overlap <paramref name="region"/> into <paramref name="results"/>
        /// (cleared first). Indices may repeat; the clip/overlap functions don't mind.
        /// </summary>
        public void Gather(in Aabb region, List<int> results)
        {
            results.Clear();
            int x0 = (int)Math.Floor(region.Min.X / m_CellSize);
            int x1 = (int)Math.Floor(region.Max.X / m_CellSize);
            int z0 = (int)Math.Floor(region.Min.Z / m_CellSize);
            int z1 = (int)Math.Floor(region.Max.Z / m_CellSize);
            for (int x = x0; x <= x1; x++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    if (m_Cells.TryGetValue(Key(x, z), out var list))
                    {
                        results.AddRange(list);
                    }
                }
            }
        }

        /// <summary>How far <paramref name="box"/> can move along Y by <paramref name="dy"/> before hitting something.</summary>
        public float ClipY(in Aabb box, float dy, List<int> candidates)
        {
            if (dy < 0 && box.Min.Y >= GroundHeight - Skin)
            {
                dy = Math.Max(dy, GroundHeight - box.Min.Y + Skin * 0.5f);
                if (dy > 0)
                {
                    dy = 0;
                }
            }

            foreach (int i in candidates)
            {
                ref readonly Aabb b = ref m_Boxes[i];
                if (box.Max.X <= b.Min.X || box.Min.X >= b.Max.X || box.Max.Z <= b.Min.Z || box.Min.Z >= b.Max.Z)
                {
                    continue;
                }

                if (dy > 0 && box.Max.Y <= b.Min.Y + Skin)
                {
                    dy = Math.Min(dy, Math.Max(0f, b.Min.Y - box.Max.Y - Skin * 0.5f));
                }
                else if (dy < 0 && box.Min.Y >= b.Max.Y - Skin)
                {
                    dy = Math.Max(dy, Math.Min(0f, b.Max.Y - box.Min.Y + Skin * 0.5f));
                }
            }

            return dy;
        }

        public float ClipX(in Aabb box, float dx, List<int> candidates)
        {
            foreach (int i in candidates)
            {
                ref readonly Aabb b = ref m_Boxes[i];
                if (box.Max.Y <= b.Min.Y || box.Min.Y >= b.Max.Y || box.Max.Z <= b.Min.Z || box.Min.Z >= b.Max.Z)
                {
                    continue;
                }

                if (dx > 0 && box.Max.X <= b.Min.X + Skin)
                {
                    dx = Math.Min(dx, Math.Max(0f, b.Min.X - box.Max.X - Skin * 0.5f));
                }
                else if (dx < 0 && box.Min.X >= b.Max.X - Skin)
                {
                    dx = Math.Max(dx, Math.Min(0f, b.Max.X - box.Min.X + Skin * 0.5f));
                }
            }

            return dx;
        }

        public float ClipZ(in Aabb box, float dz, List<int> candidates)
        {
            foreach (int i in candidates)
            {
                ref readonly Aabb b = ref m_Boxes[i];
                if (box.Max.Y <= b.Min.Y || box.Min.Y >= b.Max.Y || box.Max.X <= b.Min.X || box.Min.X >= b.Max.X)
                {
                    continue;
                }

                if (dz > 0 && box.Max.Z <= b.Min.Z + Skin)
                {
                    dz = Math.Min(dz, Math.Max(0f, b.Min.Z - box.Max.Z - Skin * 0.5f));
                }
                else if (dz < 0 && box.Min.Z >= b.Max.Z - Skin)
                {
                    dz = Math.Max(dz, Math.Min(0f, b.Max.Z - box.Min.Z + Skin * 0.5f));
                }
            }

            return dz;
        }

        /// <summary>True if <paramref name="box"/> overlaps any box or sinks below the ground plane.</summary>
        public bool Overlaps(in Aabb box, List<int> scratch)
        {
            if (box.Min.Y < GroundHeight - Skin)
            {
                return true;
            }

            Gather(box, scratch);
            foreach (int i in scratch)
            {
                if (box.Intersects(m_Boxes[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
