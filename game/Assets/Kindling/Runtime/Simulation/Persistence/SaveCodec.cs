using System.Collections.Generic;
using System.Numerics;
using Kindling.Content;
using Kindling.Core.Serialization;
using Kindling.Simulation.Game;
using Kindling.Simulation.Items;
using Kindling.Simulation.Movement;
using Kindling.Simulation.World;

namespace Kindling.Simulation.Persistence
{
    /// <summary>
    /// Versioned binary formats for the parts of a save that are blobs: character data, entity data and
    /// containers. Every blob starts with a format version; readers accept every version they know.
    /// </summary>
    public static class SaveCodec
    {
        public const byte CharacterVersion = 1;
        public const byte EntityVersion = 1;

        // ---------------------------------------------------------------- containers

        public static void WriteContainer(PacketWriter w, ItemContainer c)
        {
            w.WriteByte((byte)c.Capacity);
            int count = 0;
            for (int i = 0; i < c.Capacity; i++)
            {
                if (!c[i].IsEmpty)
                {
                    count++;
                }
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
                w.WriteFloat(s.Condition);
            }
        }

        /// <summary>Reads into an existing container. Unknown (retired) items are skipped with a warning.</summary>
        public static void ReadContainer(ref PacketReader r, ItemContainer c, ContentDatabase content, List<string> warnings)
        {
            r.ReadByte(); // capacity when saved; the content's current capacity wins
            int count = r.ReadByte();
            c.Clear();
            for (int n = 0; n < count; n++)
            {
                int slot = r.ReadByte();
                ushort id = r.ReadUShort();
                int amount = (int)r.ReadVarUInt();
                float condition = r.ReadFloat();
                ItemDef def = content.ByNetId<ItemDef>(id);
                if (def == null)
                {
                    warnings?.Add($"Unknown item id {id} (×{amount}) dropped from a container.");
                    continue;
                }

                if (!c.IsValidSlot(slot))
                {
                    if (c.Add(def, amount, condition) > 0)
                    {
                        warnings?.Add($"{def.Key} ×{amount} didn't fit after a capacity change.");
                    }

                    continue;
                }

                c.Set(slot, new ItemStack(def, System.Math.Min(amount, def.StackSize), condition));
            }
        }

        // ---------------------------------------------------------------- characters

        public static byte[] EncodeCharacter(Character c)
        {
            var w = new PacketWriter(512);
            w.WriteByte(CharacterVersion);
            PlayerState m = c.Motion;
            WriteVector(w, m.Position);
            WriteVector(w, m.Velocity);
            w.WriteFloat(m.Yaw);
            w.WriteFloat(m.Pitch);
            w.WriteByte((byte)((m.Grounded ? 1 : 0) | (m.Crouched ? 2 : 0) | (m.Sprinting ? 4 : 0) | (m.JumpHeld ? 8 : 0) | (m.Exhausted ? 16 : 0)));
            w.WriteFloat(m.Stamina);
            w.WriteFloat(m.StaminaRegenDelay);

            SurvivalStats s = c.Stats;
            w.WriteFloat(s.Health);
            w.WriteFloat(s.Hunger);
            w.WriteFloat(s.Thirst);
            w.WriteFloat(s.Warmth);
            w.WriteUShort((ushort)s.Effects);
            w.WriteFloat(s.PoisonLeft);
            w.WriteFloat(s.HealLeft);
            w.WriteFloat(s.HealRate);

            w.WriteByte((byte)c.Life);
            w.WriteFloat(c.DownedLeft);
            w.WriteByte((byte)c.LastDamage);
            w.WriteFloat((float)0); // reserved

            WriteContainer(w, c.Inventory.Main);
            WriteContainer(w, c.Inventory.Hotbar);
            WriteContainer(w, c.Inventory.Wear);
            w.WriteSByte((sbyte)c.Inventory.ActiveSlot);

            w.WriteVarUInt((uint)c.NextCraftJobId);
            w.WriteByte((byte)c.Crafting.Count);
            foreach (CraftJob job in c.Crafting)
            {
                w.WriteVarUInt((uint)job.Id);
                w.WriteUShort(job.Recipe.NetId);
                w.WriteVarUInt((uint)job.Remaining);
                w.WriteFloat(job.Progress);
            }

            w.WriteVarUInt((uint)c.Knowledge.Count);
            foreach (ushort id in c.Knowledge)
            {
                w.WriteUShort(id);
            }

            WriteDouble(w, c.OfflineSince);
            return w.ToArray();
        }

        public static void DecodeCharacter(byte[] data, Character c, ContentDatabase content, List<string> warnings)
        {
            var r = new PacketReader(data);
            byte version = r.ReadByte();
            if (version != CharacterVersion)
            {
                throw new ProtocolException($"Unknown character data version {version}.");
            }

            var m = new PlayerState
            {
                Position = ReadVector(ref r),
                Velocity = ReadVector(ref r),
                Yaw = r.ReadFloat(),
                Pitch = r.ReadFloat(),
            };
            byte bits = r.ReadByte();
            m.Grounded = (bits & 1) != 0;
            m.Crouched = (bits & 2) != 0;
            m.Sprinting = (bits & 4) != 0;
            m.JumpHeld = (bits & 8) != 0;
            m.Exhausted = (bits & 16) != 0;
            m.Stamina = r.ReadFloat();
            m.StaminaRegenDelay = r.ReadFloat();
            c.Motion = m;

            c.Stats = new SurvivalStats
            {
                Health = r.ReadFloat(),
                Hunger = r.ReadFloat(),
                Thirst = r.ReadFloat(),
                Warmth = r.ReadFloat(),
                Effects = (StatusEffect)r.ReadUShort(),
                PoisonLeft = r.ReadFloat(),
                HealLeft = r.ReadFloat(),
                HealRate = r.ReadFloat(),
            };
            c.Life = (LifeState)r.ReadByte();
            c.DownedLeft = r.ReadFloat();
            c.LastDamage = (DamageCause)r.ReadByte();
            r.ReadFloat();

            ReadContainer(ref r, c.Inventory.Main, content, warnings);
            ReadContainer(ref r, c.Inventory.Hotbar, content, warnings);
            ReadContainer(ref r, c.Inventory.Wear, content, warnings);
            c.Inventory.SelectSlot(r.ReadSByte());

            c.NextCraftJobId = (int)r.ReadVarUInt();
            c.Crafting.Clear();
            int jobs = r.ReadByte();
            for (int i = 0; i < jobs; i++)
            {
                var job = new CraftJob { Id = (int)r.ReadVarUInt() };
                ushort recipeId = r.ReadUShort();
                job.Remaining = (int)r.ReadVarUInt();
                job.Progress = r.ReadFloat();
                job.Recipe = content.ByNetId<RecipeDef>(recipeId);
                if (job.Recipe == null)
                {
                    warnings?.Add($"{c.AccountId}: unknown recipe id {recipeId} dropped from the crafting queue.");
                    continue;
                }

                c.Crafting.Add(job);
            }

            c.Knowledge.Clear();
            uint known = r.ReadVarUInt();
            for (uint i = 0; i < known; i++)
            {
                c.Knowledge.Add(r.ReadUShort());
            }

            c.OfflineSince = ReadDouble(ref r);
        }

        // ---------------------------------------------------------------- entities

        public static byte[] EncodeEntity(WorldEntity e)
        {
            var w = new PacketWriter(128);
            w.WriteByte(EntityVersion);
            w.WriteBool(e.Container != null);
            if (e.Container != null)
            {
                WriteContainer(w, e.Container);
            }

            w.WriteString(e.Label ?? string.Empty, 64);
            WriteDouble(w, e.DecayAt);
            w.WriteInt(e.SpawnerIndex);
            w.WriteFloat(e.FuelLeft);
            w.WriteFloat(e.Progress);
            WriteDouble(w, e.CooldownUntil);
            return w.ToArray();
        }

        public static void DecodeEntity(byte[] data, WorldEntity e, ContentDatabase content, List<string> warnings)
        {
            var r = new PacketReader(data);
            byte version = r.ReadByte();
            if (version != EntityVersion)
            {
                throw new ProtocolException($"Unknown entity data version {version}.");
            }

            if (r.ReadBool())
            {
                if (e.Container != null)
                {
                    ReadContainer(ref r, e.Container, content, warnings);
                }
                else
                {
                    var discard = new ItemContainer(ContainerRole.Storage, 64);
                    ReadContainer(ref r, discard, content, warnings);
                }
            }

            string label = r.ReadString(64);
            e.Label = label.Length > 0 ? label : null;
            e.DecayAt = ReadDouble(ref r);
            e.SpawnerIndex = r.ReadInt();
            e.FuelLeft = r.ReadFloat();
            e.Progress = r.ReadFloat();
            e.CooldownUntil = ReadDouble(ref r);
        }

        static void WriteVector(PacketWriter w, Vector3 v)
        {
            w.WriteFloat(v.X);
            w.WriteFloat(v.Y);
            w.WriteFloat(v.Z);
        }

        static Vector3 ReadVector(ref PacketReader r) => new Vector3(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());

        public static void WriteDouble(PacketWriter w, double d)
        {
            long bits = System.BitConverter.DoubleToInt64Bits(d);
            w.WriteUInt((uint)bits);
            w.WriteUInt((uint)(bits >> 32));
        }

        public static double ReadDouble(ref PacketReader r)
        {
            ulong lo = r.ReadUInt();
            ulong hi = r.ReadUInt();
            return System.BitConverter.Int64BitsToDouble((long)(lo | (hi << 32)));
        }
    }
}
