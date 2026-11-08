using System;
using System.Numerics;
using Kindling.Core.Numerics;
using Kindling.Core.Serialization;

namespace Kindling.Net.Protocol
{
    [Flags]
    public enum EntityFlags : byte
    {
        None = 0,
        Grounded = 1,
        Crouched = 2,
        Sprinting = 4,

        /// <summary>The player's app is in the background or their connection dropped (resume grace).</summary>
        Away = 8,

        /// <summary>On the ground, waiting for a revive.</summary>
        Downed = 16,

        /// <summary>Swung a tool or weapon within the last few ticks (animation).</summary>
        Swinging = 32,
    }

    /// <summary>
    /// A remote player as other clients see it (≈13 bytes): X/Z as signed 24-bit centimetres (±83 km),
    /// Y via <see cref="PositionQuantizer"/>, yaw 16-bit, pitch 8-bit.
    /// </summary>
    public struct EntitySnapshot
    {
        public const int MaxEncodedSize = 3 + 3 + 3 + 2 + 2 + 1 + 1 + 3;
        const float CentimetresPerMetre = 100f;
        const float PitchScale = 127f / 90f;

        public ushort Id;
        public Vector3 Position;
        public float Yaw;
        public float Pitch;
        public EntityFlags Flags;

        /// <summary>Content net id of the item in their hands (0 = none).</summary>
        public ushort HeldItem;

        public void Write(PacketWriter w)
        {
            w.WriteVarUInt(Id);
            w.WriteInt24(ToCentimetres(Position.X));
            w.WriteInt24(ToCentimetres(Position.Z));
            w.WriteUShort(PositionQuantizer.EncodeY(Position.Y));
            w.WriteUShort(InputCodec.EncodeYaw(Yaw));
            w.WriteSByte((sbyte)Math.Round(Math.Max(-90f, Math.Min(90f, Pitch)) * PitchScale));
            w.WriteByte((byte)Flags);
            w.WriteVarUInt(HeldItem);
        }

        public static EntitySnapshot Read(ref PacketReader r)
        {
            uint id = r.ReadVarUInt();
            if (id == 0 || id > ushort.MaxValue)
            {
                throw new ProtocolException("Invalid entity id.");
            }

            float x = r.ReadInt24() / CentimetresPerMetre;
            float z = r.ReadInt24() / CentimetresPerMetre;
            float y = PositionQuantizer.DecodeY(r.ReadUShort());
            return new EntitySnapshot
            {
                Id = (ushort)id,
                Position = new Vector3(x, y, z),
                Yaw = InputCodec.DecodeYaw(r.ReadUShort()),
                Pitch = r.ReadSByte() / PitchScale,
                Flags = (EntityFlags)r.ReadByte(),
                HeldItem = (ushort)r.ReadVarUInt(),
            };
        }

        /// <summary>Applies the same rounding as a write/read round trip.</summary>
        public static Vector3 QuantizePosition(Vector3 p) =>
            new Vector3(ToCentimetres(p.X) / CentimetresPerMetre, PositionQuantizer.DecodeY(PositionQuantizer.EncodeY(p.Y)), ToCentimetres(p.Z) / CentimetresPerMetre);

        static int ToCentimetres(float metres)
        {
            double cm = Math.Round(metres * (double)CentimetresPerMetre);
            return (int)Math.Max(-8388608.0, Math.Min(8388607.0, cm));
        }
    }
}
