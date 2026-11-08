using System.Collections.Generic;
using System.Numerics;
using Kindling.Core.Serialization;
using Kindling.Core.World;
using Kindling.Simulation.Items;
using Kindling.Simulation.World;

namespace Kindling.Net.Protocol
{
    /// <summary>A world entity as clients see it.</summary>
    public struct ChunkEntity
    {
        public uint Id;
        public ushort TypeId;
        public Vector3 Position;
        public float Yaw;
        public WorldEntityFlags Flags;

        /// <summary>0–255 fraction of max health (255 for entities without health).</summary>
        public byte Health;

        /// <summary>Name on a body or sleeper.</summary>
        public string Label;

        /// <summary>What a dropped item is (for its look and the pick-up prompt).</summary>
        public ushort ItemId;

        public int ItemAmount;

        public static ChunkEntity From(WorldEntity e)
        {
            var c = new ChunkEntity
            {
                Id = e.Id,
                TypeId = e.Def.NetId,
                Position = e.Position,
                Yaw = e.Yaw,
                Flags = e.Flags,
                Health = e.Def.Health > 0 ? (byte)System.Math.Round(255f * System.Math.Max(0f, System.Math.Min(1f, e.Health / e.Def.Health))) : (byte)255,
                Label = e.Label,
            };
            if (e.Def.Kind == Content.EntityKind.DroppedItem && e.Container != null && !e.Container[0].IsEmpty)
            {
                ItemStack s = e.Container[0];
                c.ItemId = s.Def.NetId;
                c.ItemAmount = s.Amount;
            }

            return c;
        }

        public void Write(PacketWriter w)
        {
            w.WriteVarUInt(Id);
            w.WriteUShort(TypeId);
            MapDataMessage.WriteVector(w, Position);
            w.WriteUShort(InputCodec.EncodeYaw(Yaw));
            w.WriteByte((byte)Flags);
            w.WriteByte(Health);
            byte mask = (byte)((string.IsNullOrEmpty(Label) ? 0 : 1) | (ItemId != 0 ? 2 : 0));
            w.WriteByte(mask);
            if ((mask & 1) != 0)
            {
                w.WriteString(Label, 64);
            }

            if ((mask & 2) != 0)
            {
                w.WriteUShort(ItemId);
                w.WriteVarUInt((uint)ItemAmount);
            }
        }

        public static ChunkEntity Read(ref PacketReader r)
        {
            var e = new ChunkEntity
            {
                Id = r.ReadVarUInt(),
                TypeId = r.ReadUShort(),
                Position = MapDataMessage.ReadVector(ref r),
                Yaw = InputCodec.DecodeYaw(r.ReadUShort()),
                Flags = (WorldEntityFlags)r.ReadByte(),
                Health = r.ReadByte(),
            };
            byte mask = r.ReadByte();
            if ((mask & 1) != 0)
            {
                e.Label = r.ReadString(64);
            }

            if ((mask & 2) != 0)
            {
                e.ItemId = r.ReadUShort();
                e.ItemAmount = (int)r.ReadVarUInt();
            }

            return e;
        }
    }

    public enum ChunkOp : byte
    {
        UpsertEntity = 1,
        RemoveEntity = 2,
        NodeState = 3,
    }

    /// <summary>
    /// Chunk replication (reliable, ordered): a snapshot when a client subscribes to a chunk, then deltas,
    /// and an unload when it leaves the chunk's range. Node alive bits are in the chunk's node order
    /// (nodes of the static map whose position falls in the chunk, by index).
    /// </summary>
    public static class ChunkMessages
    {
        public static void WriteSnapshot(PacketWriter w, ChunkCoord chunk, IReadOnlyList<int> nodes, ResourceField resources, IReadOnlyList<WorldEntity> entities)
        {
            w.WriteByte((byte)MessageType.ChunkSnapshot);
            WriteCoord(w, chunk);
            w.WriteVarUInt((uint)nodes.Count);
            byte bits = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (resources.IsAlive(nodes[i]))
                {
                    bits |= (byte)(1 << (i & 7));
                }

                if ((i & 7) == 7 || i == nodes.Count - 1)
                {
                    w.WriteByte(bits);
                    bits = 0;
                }
            }

            w.WriteVarUInt((uint)entities.Count);
            foreach (WorldEntity e in entities)
            {
                ChunkEntity.From(e).Write(w);
            }
        }

        public static void ReadSnapshot(ref PacketReader r, out ChunkCoord chunk, List<bool> alive, List<ChunkEntity> entities)
        {
            chunk = ReadCoord(ref r);
            alive.Clear();
            entities.Clear();
            uint nodes = r.ReadVarUInt();
            if (nodes > 100000)
            {
                throw new ProtocolException("Too many nodes.");
            }

            byte bits = 0;
            for (int i = 0; i < nodes; i++)
            {
                if ((i & 7) == 0)
                {
                    bits = r.ReadByte();
                }

                alive.Add((bits & (1 << (i & 7))) != 0);
            }

            uint count = r.ReadVarUInt();
            for (uint i = 0; i < count; i++)
            {
                entities.Add(ChunkEntity.Read(ref r));
            }
        }

        public static void BeginDelta(PacketWriter w, ChunkCoord chunk, out int countOffset)
        {
            w.WriteByte((byte)MessageType.ChunkDelta);
            WriteCoord(w, chunk);
            countOffset = w.Length;
            w.WriteUShort(0);
        }

        public static void SetDeltaCount(PacketWriter w, int countOffset, int count)
        {
            w.Buffer[countOffset] = (byte)count;
            w.Buffer[countOffset + 1] = (byte)(count >> 8);
        }

        public static void WriteUpsert(PacketWriter w, WorldEntity e)
        {
            w.WriteByte((byte)ChunkOp.UpsertEntity);
            ChunkEntity.From(e).Write(w);
        }

        public static void WriteRemove(PacketWriter w, uint id)
        {
            w.WriteByte((byte)ChunkOp.RemoveEntity);
            w.WriteVarUInt(id);
        }

        public static void WriteNode(PacketWriter w, int nodeIndex, bool alive)
        {
            w.WriteByte((byte)ChunkOp.NodeState);
            w.WriteVarUInt((uint)nodeIndex);
            w.WriteBool(alive);
        }

        public static void WriteUnload(PacketWriter w, ChunkCoord chunk)
        {
            w.WriteByte((byte)MessageType.ChunkUnload);
            WriteCoord(w, chunk);
        }

        public static void WriteCoord(PacketWriter w, ChunkCoord c)
        {
            w.WriteShort((short)c.X);
            w.WriteShort((short)c.Z);
        }

        public static ChunkCoord ReadCoord(ref PacketReader r) => new ChunkCoord(r.ReadShort(), r.ReadShort());
    }

    public static class ActionResultMessage
    {
        public static void Write(PacketWriter w, ushort requestId, Kindling.Simulation.ActionError error)
        {
            w.WriteByte((byte)MessageType.ActionResult);
            w.WriteUShort(requestId);
            w.WriteByte((byte)error);
        }
    }
}
