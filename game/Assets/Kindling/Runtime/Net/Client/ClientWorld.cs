using System;
using System.Collections.Generic;
using System.Numerics;
using Kindling.Content;
using Kindling.Core.Serialization;
using Kindling.Core.World;
using Kindling.Net.Protocol;
using Kindling.Simulation.Collision;
using Kindling.Simulation.Game;
using Kindling.Simulation.Items;
using Kindling.Simulation.World;

namespace Kindling.Net.Client
{
    /// <summary>
    /// The client's copy of the survival world: the static map, node states and world entities of
    /// subscribed chunks, the player's own inventory, open container, survival state, events and clock.
    /// Read-only for game code: only server messages change it.
    /// </summary>
    public sealed class ClientWorld
    {
        readonly Dictionary<uint, ChunkEntity> m_Entities = new Dictionary<uint, ChunkEntity>();
        readonly Dictionary<ChunkCoord, HashSet<uint>> m_ChunkEntities = new Dictionary<ChunkCoord, HashSet<uint>>();
        readonly List<bool> m_AliveScratch = new List<bool>();
        readonly List<ChunkEntity> m_EntityScratch = new List<ChunkEntity>();
        WorldGrid m_Grid;
        double m_WorldSecondsAtSync;
        double m_LocalAtSync;
        double m_LocalTime;

        public ClientWorld(ContentDatabase content)
        {
            Content = content;
        }

        public ContentDatabase Content { get; }

        public StaticMap Map { get; private set; }

        public CollisionWorld Collision { get; private set; }

        /// <summary>Node alive states (indices into <see cref="StaticMap.Nodes"/>), for subscribed chunks.</summary>
        public ResourceField Nodes { get; private set; }

        public IReadOnlyDictionary<uint, ChunkEntity> Entities => m_Entities;

        public CharacterInventory Inventory { get; } = new CharacterInventory();

        public uint OpenContainerId { get; private set; }

        /// <summary>Contents of the open world container (chest, campfire, body…), or null.</summary>
        public ItemContainer OpenContainer { get; private set; }

        public OwnerStateMessage Owner { get; } = new OwnerStateMessage();

        public bool HasOwnerState { get; private set; }

        public LifeState Life => HasOwnerState ? Owner.Life : LifeState.Alive;

        /// <summary>Events not yet shown by the UI. Drain with <see cref="DrainEvents"/>.</summary>
        public List<GameEvent> PendingEvents { get; } = new List<GameEvent>();

        public float DayLengthSeconds { get; private set; } = 3600f;

        public float NightFraction { get; private set; } = 0.25f;

        /// <summary>Server world time, extrapolated between 1 Hz updates.</summary>
        public double WorldSeconds => m_WorldSecondsAtSync + (m_LocalTime - m_LocalAtSync);

        public double TimeOfDay => (WorldSeconds / Math.Max(1.0, DayLengthSeconds) + WorldClock.StartTimeOfDay) % 1.0;

        public bool IsNight => TimeOfDay >= 1.0 - NightFraction;

        /// <summary>Bumped on any change (views can skip work when it's unchanged).</summary>
        public uint Version { get; private set; }

        public uint EntityVersion { get; private set; }

        public uint InventoryVersion { get; private set; }

        public int LoadedChunkCount => m_ChunkEntities.Count;

        public bool IsLoaded(ChunkCoord chunk) => m_ChunkEntities.ContainsKey(chunk);

        public EntityDef DefOf(in ChunkEntity e) => Content.ByNetId<EntityDef>(e.TypeId);

        public void DrainEvents(List<GameEvent> into)
        {
            into.AddRange(PendingEvents);
            PendingEvents.Clear();
        }

        internal void Advance(double dt) => m_LocalTime += dt;

        internal void SetMap(StaticMap map)
        {
            Map = map;
            Collision = map.BuildCollisionWorld();
            m_Grid = new WorldGrid(map.MapSize);
            Nodes = new ResourceField(map, m_Grid);
            m_Entities.Clear();
            m_ChunkEntities.Clear();
            Version++;
            EntityVersion++;
        }

        internal void Reset()
        {
            m_Entities.Clear();
            m_ChunkEntities.Clear();
            HasOwnerState = false;
            OpenContainerId = 0;
            OpenContainer = null;
            Inventory.Clear();
            PendingEvents.Clear();
            Version++;
            EntityVersion++;
            InventoryVersion++;
        }

        internal void ApplyWorldState(in WorldStateMessage m)
        {
            m_WorldSecondsAtSync = m.WorldSeconds;
            m_LocalAtSync = m_LocalTime;
            DayLengthSeconds = m.DayLengthSeconds;
            NightFraction = m.NightFraction;
        }

        internal void ApplySnapshot(ref PacketReader r)
        {
            ChunkMessages.ReadSnapshot(ref r, out ChunkCoord chunk, m_AliveScratch, m_EntityScratch);
            IReadOnlyList<int> nodes = Nodes.NodesInChunk(chunk);
            if (nodes.Count != m_AliveScratch.Count)
            {
                throw new ProtocolException($"Chunk {chunk} node count mismatch ({nodes.Count} vs {m_AliveScratch.Count}).");
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                SetNode(nodes[i], m_AliveScratch[i]);
            }

            Unload(chunk);
            var ids = new HashSet<uint>();
            m_ChunkEntities[chunk] = ids;
            foreach (ChunkEntity e in m_EntityScratch)
            {
                m_Entities[e.Id] = e;
                ids.Add(e.Id);
            }

            Version++;
            EntityVersion++;
        }

        internal void ApplyDelta(ref PacketReader r)
        {
            ChunkCoord chunk = ChunkMessages.ReadCoord(ref r);
            if (!m_ChunkEntities.TryGetValue(chunk, out HashSet<uint> ids))
            {
                ids = new HashSet<uint>();
                m_ChunkEntities[chunk] = ids;
            }

            int ops = r.ReadUShort();
            for (int i = 0; i < ops; i++)
            {
                var op = (ChunkOp)r.ReadByte();
                switch (op)
                {
                    case ChunkOp.UpsertEntity:
                        ChunkEntity e = ChunkEntity.Read(ref r);
                        RemoveFromOtherChunk(e.Id, chunk);
                        m_Entities[e.Id] = e;
                        ids.Add(e.Id);
                        break;
                    case ChunkOp.RemoveEntity:
                        uint id = r.ReadVarUInt();
                        if (ids.Remove(id))
                        {
                            m_Entities.Remove(id);
                        }

                        break;
                    case ChunkOp.NodeState:
                        int node = (int)r.ReadVarUInt();
                        bool alive = r.ReadBool();
                        if (node >= 0 && node < Nodes.Count)
                        {
                            SetNode(node, alive);
                        }

                        break;
                    default:
                        throw new ProtocolException($"Unknown chunk op {op}.");
                }
            }

            Version++;
            EntityVersion++;
        }

        void RemoveFromOtherChunk(uint id, ChunkCoord keep)
        {
            foreach (var pair in m_ChunkEntities)
            {
                if (!pair.Key.Equals(keep) && pair.Value.Remove(id))
                {
                    return;
                }
            }
        }

        internal void Unload(ChunkCoord chunk)
        {
            if (m_ChunkEntities.TryGetValue(chunk, out HashSet<uint> ids))
            {
                foreach (uint id in ids)
                {
                    m_Entities.Remove(id);
                }

                m_ChunkEntities.Remove(chunk);
                Version++;
                EntityVersion++;
            }
        }

        void SetNode(int index, bool alive)
        {
            if (Nodes.IsAlive(index) != alive)
            {
                Nodes.Restore(index, alive, Nodes.Placement(index).Def.TotalYield, 0);
            }
        }

        internal void ApplyContainer(ref PacketReader r)
        {
            int capacity = ContainerMessage.PeekCapacity(r);
            var kind = (ContainerKind)r.ReadByte();
            uint entityId = r.ReadVarUInt();
            r.ReadUInt();
            ItemContainer target;
            switch (kind)
            {
                case ContainerKind.Main:
                    target = Inventory.Main;
                    break;
                case ContainerKind.Hotbar:
                    target = Inventory.Hotbar;
                    break;
                case ContainerKind.Wear:
                    target = Inventory.Wear;
                    break;
                default:
                    if (OpenContainer == null || OpenContainerId != entityId || OpenContainer.Capacity != capacity)
                    {
                        OpenContainer = new ItemContainer(ContainerRole.Storage, capacity);
                    }

                    OpenContainerId = entityId;
                    target = OpenContainer;
                    break;
            }

            ReadSlots(ref r, target);
            InventoryVersion++;
            Version++;
        }

        void ReadSlots(ref PacketReader r, ItemContainer into)
        {
            int capacity = r.ReadByte();
            if (capacity != into.Capacity)
            {
                throw new ProtocolException("Container capacity mismatch.");
            }

            into.Clear();
            int count = r.ReadByte();
            for (int i = 0; i < count; i++)
            {
                int slot = r.ReadByte();
                ushort id = r.ReadUShort();
                int amount = (int)r.ReadVarUInt();
                byte condition = r.ReadByte();
                ItemDef def = Content.ByNetId<ItemDef>(id) ?? throw new ProtocolException($"Unknown item {id}.");
                if (!into.IsValidSlot(slot))
                {
                    throw new ProtocolException("Invalid slot.");
                }

                into.Set(slot, new ItemStack(def, amount, def.MaxCondition * condition / 255f));
            }
        }

        internal void ApplyOwner(ref PacketReader r)
        {
            Owner.Read(ref r);
            HasOwnerState = true;
            Inventory.SelectSlot(Owner.ActiveSlot);
            if (Owner.OpenContainer == 0 || Owner.OpenContainer != OpenContainerId)
            {
                if (Owner.OpenContainer == 0)
                {
                    OpenContainerId = 0;
                    OpenContainer = null;
                }
            }

            Version++;
        }

        internal void ApplyEvents(ref PacketReader r) => EventsMessage.Read(ref r, PendingEvents);

        /// <summary>
        /// What the player is looking at, for the interact prompt: a world entity (container, dropped
        /// item…), a pickable plant, or the pond they stand in.
        /// </summary>
        public bool FindInteractTarget(Vector3 eye, Vector3 dir, float reach, out InteractTarget kind, out uint id)
        {
            kind = InteractTarget.None;
            id = 0;
            float best = reach;
            foreach (ChunkEntity e in m_Entities.Values)
            {
                EntityDef def = DefOf(e);
                if (def == null || def.Kind == EntityKind.Sleeper || def.Kind == EntityKind.Breakable || (def.ContainerSlots == 0 && def.Processor == null))
                {
                    continue;
                }

                float dx = e.Position.X - eye.X;
                float dz = e.Position.Z - eye.Z;
                if (dx * dx + dz * dz > (reach + 2) * (reach + 2))
                {
                    continue;
                }

                if (ResourceField.RayAabb(eye, dir, Bounds(e, def).Inflate(0.15f), out float t) && t < best)
                {
                    best = t;
                    kind = InteractTarget.Entity;
                    id = e.Id;
                }
            }

            if (Nodes != null)
            {
                for (int i = 0; i < Nodes.Count; i++)
                {
                    NodePlacement n = Nodes.Placement(i);
                    if (!n.Def.Pickable || !Nodes.IsAlive(i))
                    {
                        continue;
                    }

                    float dx = n.Position.X - eye.X;
                    float dz = n.Position.Z - eye.Z;
                    if (dx * dx + dz * dz > (reach + 1) * (reach + 1))
                    {
                        continue;
                    }

                    if (ResourceField.RayAabb(eye, dir, n.Bounds.Inflate(0.25f), out float t) && t < best)
                    {
                        best = t;
                        kind = InteractTarget.Node;
                        id = (uint)i;
                    }
                }
            }

            if (kind == InteractTarget.None && Map != null && Map.IsInPond(eye, 1.5f))
            {
                kind = InteractTarget.Pond;
            }

            return kind != InteractTarget.None;
        }

        public static Aabb Bounds(in ChunkEntity e, EntityDef def)
        {
            bool sideways = ((int)Math.Round(e.Yaw / 90f) & 1) == 1;
            float sx = sideways ? def.Size.Z : def.Size.X;
            float sz = sideways ? def.Size.X : def.Size.Z;
            return new Aabb(e.Position - new Vector3(sx / 2, 0f, sz / 2), e.Position + new Vector3(sx / 2, def.Size.Y, sz / 2));
        }
    }
}
