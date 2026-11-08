using System.Collections.Generic;
using System.Numerics;
using Kindling.Content;
using Kindling.Core.Serialization;
using Kindling.Simulation;
using Kindling.Simulation.Game;
using Kindling.Simulation.Items;

namespace Kindling.Net.Protocol
{
    /// <summary>Server → client (reliable, 1 Hz): world clock.</summary>
    public struct WorldStateMessage
    {
        public double WorldSeconds;
        public float DayLengthSeconds;
        public float NightFraction;

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)MessageType.WorldState);
            w.WriteUInt((uint)System.BitConverter.DoubleToInt64Bits(WorldSeconds));
            w.WriteUInt((uint)(System.BitConverter.DoubleToInt64Bits(WorldSeconds) >> 32));
            w.WriteFloat(DayLengthSeconds);
            w.WriteFloat(NightFraction);
        }

        public static WorldStateMessage Read(ref PacketReader r)
        {
            ulong lo = r.ReadUInt();
            ulong hi = r.ReadUInt();
            return new WorldStateMessage
            {
                WorldSeconds = System.BitConverter.Int64BitsToDouble((long)(lo | (hi << 32))),
                DayLengthSeconds = r.ReadFloat(),
                NightFraction = r.ReadFloat(),
            };
        }
    }

    public struct CraftJobInfo
    {
        public int Id;
        public ushort RecipeId;
        public int Remaining;
        public float Progress;
    }

    public struct RespawnPointInfo
    {
        public uint EntityId;
        public float CooldownLeft;
    }

    /// <summary>Server → owner (reliable, on change, ≤ 4 Hz): the player's own survival state.</summary>
    public sealed class OwnerStateMessage
    {
        public LifeState Life;
        public float Health;
        public float Hunger;
        public float Thirst;
        public float Warmth;
        public StatusEffect Effects;
        public float DownedLeft;
        public byte LastDamage;
        public sbyte ActiveSlot;
        public uint OpenContainer;
        public readonly List<CraftJobInfo> Crafting = new List<CraftJobInfo>();
        public readonly List<RespawnPointInfo> RespawnPoints = new List<RespawnPointInfo>();

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)MessageType.OwnerState);
            w.WriteByte((byte)Life);
            w.WriteFloat(Health);
            w.WriteFloat(Hunger);
            w.WriteFloat(Thirst);
            w.WriteFloat(Warmth);
            w.WriteUShort((ushort)Effects);
            w.WriteFloat(DownedLeft);
            w.WriteByte(LastDamage);
            w.WriteSByte(ActiveSlot);
            w.WriteVarUInt(OpenContainer);
            w.WriteByte((byte)Crafting.Count);
            foreach (CraftJobInfo j in Crafting)
            {
                w.WriteVarUInt((uint)j.Id);
                w.WriteUShort(j.RecipeId);
                w.WriteVarUInt((uint)j.Remaining);
                w.WriteFloat(j.Progress);
            }

            w.WriteByte((byte)RespawnPoints.Count);
            foreach (RespawnPointInfo p in RespawnPoints)
            {
                w.WriteVarUInt(p.EntityId);
                w.WriteFloat(p.CooldownLeft);
            }
        }

        public void Read(ref PacketReader r)
        {
            Life = (LifeState)r.ReadByte();
            Health = r.ReadFloat();
            Hunger = r.ReadFloat();
            Thirst = r.ReadFloat();
            Warmth = r.ReadFloat();
            Effects = (StatusEffect)r.ReadUShort();
            DownedLeft = r.ReadFloat();
            LastDamage = r.ReadByte();
            ActiveSlot = r.ReadSByte();
            OpenContainer = r.ReadVarUInt();
            Crafting.Clear();
            int jobs = r.ReadByte();
            for (int i = 0; i < jobs; i++)
            {
                Crafting.Add(new CraftJobInfo { Id = (int)r.ReadVarUInt(), RecipeId = r.ReadUShort(), Remaining = (int)r.ReadVarUInt(), Progress = r.ReadFloat() });
            }

            RespawnPoints.Clear();
            int points = r.ReadByte();
            for (int i = 0; i < points; i++)
            {
                RespawnPoints.Add(new RespawnPointInfo { EntityId = r.ReadVarUInt(), CooldownLeft = r.ReadFloat() });
            }
        }
    }

    /// <summary>
    /// Server → viewer (reliable): the full contents of one container (own main/hotbar/wear, or the open
    /// world container), sent whenever its version changes. Containers are small, so no slot deltas.
    /// </summary>
    public static class ContainerMessage
    {
        public static void Write(PacketWriter w, ContainerKind kind, uint entityId, ItemContainer c)
        {
            w.WriteByte((byte)MessageType.Container);
            w.WriteByte((byte)kind);
            w.WriteVarUInt(entityId);
            w.WriteUInt(c.Version);
            SaveLikeCodec.Write(w, c);
        }

        public static void Read(ref PacketReader r, ContentDatabase content, out ContainerKind kind, out uint entityId, out uint version, ItemContainer into)
        {
            kind = (ContainerKind)r.ReadByte();
            entityId = r.ReadVarUInt();
            version = r.ReadUInt();
            SaveLikeCodec.Read(ref r, content, into);
        }

        /// <summary>Reads just the capacity so the receiver can allocate the right container.</summary>
        public static int PeekCapacity(PacketReader r)
        {
            r.ReadByte();
            r.ReadVarUInt();
            r.ReadUInt();
            return r.ReadByte();
        }

        static class SaveLikeCodec
        {
            public static void Write(PacketWriter w, ItemContainer c)
            {
                w.WriteByte((byte)c.Capacity);
                int count = 0;
                for (int i = 0; i < c.Capacity; i++)
                {
                    count += c[i].IsEmpty ? 0 : 1;
                }

                w.WriteByte((byte)count);
                for (int i = 0; i < c.Capacity; i++)
                {
                    ItemStack s = c[i];
                    if (s.IsEmpty)
                    {
                        continue;
                    }

                    w.WriteByte((byte)i);
                    w.WriteUShort(s.Def.NetId);
                    w.WriteVarUInt((uint)s.Amount);
                    w.WriteByte(s.Def.HasCondition ? (byte)System.Math.Round(255f * System.Math.Max(0f, System.Math.Min(1f, s.Condition / s.Def.MaxCondition))) : (byte)255);
                }
            }

            public static void Read(ref PacketReader r, ContentDatabase content, ItemContainer into)
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
                    ItemDef def = content.ByNetId<ItemDef>(id) ?? throw new ProtocolException($"Unknown item {id}.");
                    if (!into.IsValidSlot(slot))
                    {
                        throw new ProtocolException("Invalid slot.");
                    }

                    into.Set(slot, new ItemStack(def, amount, def.MaxCondition * condition / 255f));
                }
            }
        }
    }

    /// <summary>Server → owner (reliable): game events (gather hits, items gained, deaths…).</summary>
    public static class EventsMessage
    {
        public static void Write(PacketWriter w, List<GameEvent> events)
        {
            w.WriteByte((byte)MessageType.Events);
            int count = System.Math.Min(events.Count, 255);
            w.WriteByte((byte)count);
            for (int i = 0; i < count; i++)
            {
                GameEvent e = events[i];
                w.WriteByte((byte)e.Kind);
                w.WriteVarUInt((uint)e.Code);
                w.WriteUShort(e.ItemNetId);
                w.WriteVarUInt((uint)System.Math.Max(0, e.Amount));
                w.WriteVarUInt((uint)System.Math.Max(0, e.Target));
                w.WriteBool(e.Flag);
                MapDataMessage.WriteVector(w, e.Position);
            }
        }

        public static void Read(ref PacketReader r, List<GameEvent> into)
        {
            int count = r.ReadByte();
            for (int i = 0; i < count; i++)
            {
                into.Add(new GameEvent
                {
                    Kind = (GameEventKind)r.ReadByte(),
                    Code = (int)r.ReadVarUInt(),
                    ItemNetId = r.ReadUShort(),
                    Amount = (int)r.ReadVarUInt(),
                    Target = (int)r.ReadVarUInt(),
                    Flag = r.ReadBool(),
                    Position = MapDataMessage.ReadVector(ref r),
                });
            }
        }
    }

    public enum ActionKind : byte
    {
        MoveItem = 1,
        Drop = 2,
        Use = 3,
        SelectHotbar = 4,
        Craft = 5,
        CancelCraft = 6,
        Deploy = 7,
        Interact = 8,
        CloseContainer = 9,
        ToggleProcessor = 10,
        Respawn = 11,
        GiveUp = 12,
        Revive = 13,
    }

    /// <summary>Client → server (reliable): a player action. The server answers with an ActionResult carrying the same request id.</summary>
    public struct ActionRequest
    {
        public ushort RequestId;
        public ActionKind Kind;
        public ContainerRef From;
        public int FromSlot;
        public ContainerRef To;

        /// <summary>Target slot; -1 = quick move.</summary>
        public int ToSlot;

        public int Amount;

        /// <summary>Recipe net id, job id, hotbar slot, entity id, node index or player id depending on the kind.</summary>
        public uint Target;

        public InteractTarget InteractTarget;
        public Vector3 Position;
        public float Yaw;

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)MessageType.Action);
            w.WriteUShort(RequestId);
            w.WriteByte((byte)Kind);
            switch (Kind)
            {
                case ActionKind.MoveItem:
                    WriteRef(w, From);
                    w.WriteByte((byte)FromSlot);
                    WriteRef(w, To);
                    w.WriteSByte((sbyte)ToSlot);
                    w.WriteVarUInt((uint)System.Math.Max(0, Amount));
                    break;
                case ActionKind.Drop:
                    WriteRef(w, From);
                    w.WriteByte((byte)FromSlot);
                    w.WriteVarUInt((uint)System.Math.Max(0, Amount));
                    break;
                case ActionKind.Use:
                    WriteRef(w, From);
                    w.WriteByte((byte)FromSlot);
                    break;
                case ActionKind.Deploy:
                    w.WriteByte((byte)FromSlot);
                    w.WriteFloat(Position.X);
                    w.WriteFloat(Position.Z);
                    w.WriteFloat(Yaw);
                    break;
                case ActionKind.Interact:
                    w.WriteByte((byte)InteractTarget);
                    w.WriteVarUInt(Target);
                    break;
                case ActionKind.Craft:
                    w.WriteVarUInt(Target);
                    w.WriteVarUInt((uint)System.Math.Max(0, Amount));
                    break;
                case ActionKind.SelectHotbar:
                    w.WriteSByte((sbyte)FromSlot);
                    break;
                case ActionKind.CancelCraft:
                case ActionKind.ToggleProcessor:
                case ActionKind.Respawn:
                case ActionKind.Revive:
                    w.WriteVarUInt(Target);
                    break;
            }
        }

        public static ActionRequest Read(ref PacketReader r)
        {
            var a = new ActionRequest { RequestId = r.ReadUShort(), Kind = (ActionKind)r.ReadByte() };
            switch (a.Kind)
            {
                case ActionKind.MoveItem:
                    a.From = ReadRef(ref r);
                    a.FromSlot = r.ReadByte();
                    a.To = ReadRef(ref r);
                    a.ToSlot = r.ReadSByte();
                    a.Amount = (int)System.Math.Min(r.ReadVarUInt(), int.MaxValue);
                    break;
                case ActionKind.Drop:
                    a.From = ReadRef(ref r);
                    a.FromSlot = r.ReadByte();
                    a.Amount = (int)System.Math.Min(r.ReadVarUInt(), int.MaxValue);
                    break;
                case ActionKind.Use:
                    a.From = ReadRef(ref r);
                    a.FromSlot = r.ReadByte();
                    break;
                case ActionKind.Deploy:
                    a.FromSlot = r.ReadByte();
                    a.Position = new Vector3(r.ReadFloat(), 0f, r.ReadFloat());
                    a.Yaw = r.ReadFloat();
                    break;
                case ActionKind.Interact:
                    a.InteractTarget = (InteractTarget)r.ReadByte();
                    a.Target = r.ReadVarUInt();
                    break;
                case ActionKind.Craft:
                    a.Target = r.ReadVarUInt();
                    a.Amount = (int)System.Math.Min(r.ReadVarUInt(), 1000);
                    break;
                case ActionKind.SelectHotbar:
                    a.FromSlot = r.ReadSByte();
                    break;
                case ActionKind.CancelCraft:
                case ActionKind.ToggleProcessor:
                case ActionKind.Respawn:
                case ActionKind.Revive:
                    a.Target = r.ReadVarUInt();
                    break;
                case ActionKind.CloseContainer:
                case ActionKind.GiveUp:
                    break;
                default:
                    throw new ProtocolException($"Unknown action {a.Kind}.");
            }

            return a;
        }

        static void WriteRef(PacketWriter w, ContainerRef r)
        {
            w.WriteByte((byte)r.Kind);
            if (r.Kind == ContainerKind.Entity)
            {
                w.WriteVarUInt(r.EntityId);
            }
        }

        static ContainerRef ReadRef(ref PacketReader r)
        {
            var kind = (ContainerKind)r.ReadByte();
            if (kind > ContainerKind.Entity)
            {
                throw new ProtocolException("Invalid container kind.");
            }

            return new ContainerRef { Kind = kind, EntityId = kind == ContainerKind.Entity ? r.ReadVarUInt() : 0 };
        }
    }
}
