using System;
using System.Collections.Generic;
using System.Numerics;
using Kindling.Content;
using Kindling.Core.World;

namespace Kindling.Simulation.World
{
    public enum EntityChange : byte
    {
        Created,
        Updated,
        Removed,
    }

    public struct EntityChangeRecord
    {
        public ChunkCoord Chunk;
        public uint EntityId;
        public EntityChange Kind;
    }

    /// <summary>
    /// World entities grouped by 64 m chunk. Every create/update/remove bumps the chunk's version and is
    /// logged, which drives chunk replication and saving.
    /// </summary>
    public sealed class EntityStore
    {
        readonly WorldGrid m_Grid;
        readonly Dictionary<uint, WorldEntity> m_Entities = new Dictionary<uint, WorldEntity>();
        readonly Dictionary<ChunkCoord, List<WorldEntity>> m_ByChunk = new Dictionary<ChunkCoord, List<WorldEntity>>();
        readonly Dictionary<ChunkCoord, uint> m_ChunkVersions = new Dictionary<ChunkCoord, uint>();
        readonly List<EntityChangeRecord> m_Changes = new List<EntityChangeRecord>();
        readonly HashSet<uint> m_Dirty = new HashSet<uint>();
        readonly HashSet<uint> m_Removed = new HashSet<uint>();
        static readonly List<WorldEntity> s_Empty = new List<WorldEntity>();

        public EntityStore(WorldGrid grid)
        {
            m_Grid = grid;
        }

        public uint NextId { get; set; } = 1;

        public int Count => m_Entities.Count;

        public IEnumerable<WorldEntity> All => m_Entities.Values;

        public WorldEntity Get(uint id) => m_Entities.TryGetValue(id, out WorldEntity e) ? e : null;

        public ChunkCoord ChunkOf(Vector3 p) => ChunkCoord.FromWorld(p.X, p.Z, m_Grid.ChunkSize);

        public IReadOnlyList<WorldEntity> InChunk(ChunkCoord chunk) => m_ByChunk.TryGetValue(chunk, out List<WorldEntity> list) ? list : s_Empty;

        public uint ChunkVersion(ChunkCoord chunk) => m_ChunkVersions.TryGetValue(chunk, out uint v) ? v : 0;

        public WorldEntity Spawn(EntityDef def, Vector3 position, float yaw) => Add(NextId++, def, position, yaw);

        /// <summary>Adds an entity with a known id (loading a save).</summary>
        public WorldEntity Add(uint id, EntityDef def, Vector3 position, float yaw)
        {
            if (def == null)
            {
                throw new ArgumentNullException(nameof(def));
            }

            if (m_Entities.ContainsKey(id))
            {
                throw new InvalidOperationException($"Entity {id} already exists.");
            }

            var e = new WorldEntity(id, def, position, yaw, ChunkOf(position));
            m_Entities[id] = e;
            ChunkList(e.Chunk).Add(e);
            NextId = Math.Max(NextId, id + 1);
            m_Removed.Remove(id);
            Log(e, EntityChange.Created);
            return e;
        }

        /// <summary>Call after changing anything replicated or saved about an entity.</summary>
        public void MarkChanged(WorldEntity e)
        {
            if (m_Entities.ContainsKey(e.Id))
            {
                Log(e, EntityChange.Updated);
            }
        }

        public void Move(WorldEntity e, Vector3 position)
        {
            ChunkCoord chunk = ChunkOf(position);
            if (chunk.Equals(e.Chunk))
            {
                e.Position = position;
                MarkChanged(e);
                return;
            }

            Log(e, EntityChange.Removed); // leaves the old chunk
            ChunkList(e.Chunk).Remove(e);
            e.Position = position;
            e.Chunk = chunk;
            ChunkList(chunk).Add(e);
            Log(e, EntityChange.Created);
        }

        public bool Remove(uint id)
        {
            if (!m_Entities.TryGetValue(id, out WorldEntity e))
            {
                return false;
            }

            m_Entities.Remove(id);
            ChunkList(e.Chunk).Remove(e);
            Log(e, EntityChange.Removed);
            m_Dirty.Remove(id);
            m_Removed.Add(id);
            return true;
        }

        /// <summary>Entities within <paramref name="radius"/> (horizontal distance) of a point.</summary>
        public void Query(Vector3 center, float radius, List<WorldEntity> results, Func<WorldEntity, bool> filter = null)
        {
            results.Clear();
            int reach = (int)Math.Ceiling(radius / m_Grid.ChunkSize);
            ChunkCoord c = ChunkOf(center);
            float r2 = radius * radius;
            for (int dx = -reach; dx <= reach; dx++)
            {
                for (int dz = -reach; dz <= reach; dz++)
                {
                    foreach (WorldEntity e in InChunk(new ChunkCoord(c.X + dx, c.Z + dz)))
                    {
                        float ex = e.Position.X - center.X;
                        float ez = e.Position.Z - center.Z;
                        if (ex * ex + ez * ez <= r2 && (filter == null || filter(e)))
                        {
                            results.Add(e);
                        }
                    }
                }
            }
        }

        public void ConsumeChanges(List<EntityChangeRecord> changes)
        {
            changes.Clear();
            changes.AddRange(m_Changes);
            m_Changes.Clear();
        }

        /// <summary>Entities changed or created since the last call (saving), and ids removed since then.</summary>
        public void ConsumeDirty(List<WorldEntity> dirty, List<uint> removed)
        {
            dirty.Clear();
            removed.Clear();
            foreach (uint id in m_Dirty)
            {
                if (m_Entities.TryGetValue(id, out WorldEntity e))
                {
                    dirty.Add(e);
                }
            }

            removed.AddRange(m_Removed);
            m_Dirty.Clear();
            m_Removed.Clear();
        }

        List<WorldEntity> ChunkList(ChunkCoord chunk)
        {
            if (!m_ByChunk.TryGetValue(chunk, out List<WorldEntity> list))
            {
                m_ByChunk[chunk] = list = new List<WorldEntity>();
            }

            return list;
        }

        void Log(WorldEntity e, EntityChange kind)
        {
            e.Version++;
            m_ChunkVersions[e.Chunk] = ChunkVersion(e.Chunk) + 1;
            m_Changes.Add(new EntityChangeRecord { Chunk = e.Chunk, EntityId = e.Id, Kind = kind });
            if (kind != EntityChange.Removed)
            {
                m_Dirty.Add(e.Id);
            }
        }
    }
}
