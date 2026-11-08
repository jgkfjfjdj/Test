using System;
using System.Numerics;
using Kindling.Simulation.Movement;

namespace Kindling.Net.Protocol
{
    /// <summary>
    /// Fixed-point encoding of <see cref="PlayerInput"/> (7 bytes + sequence). The client simulates the
    /// <em>quantized</em> input, so its prediction runs on exactly the values the server receives.
    /// </summary>
    public static class InputCodec
    {
        public const int EncodedSize = 7;

        public static PlayerInput Quantize(in PlayerInput input)
        {
            var q = input;
            q.Move = new Vector2(DecodeAxis(EncodeAxis(input.Move.X)), DecodeAxis(EncodeAxis(input.Move.Y)));
            q.Yaw = DecodeYaw(EncodeYaw(input.Yaw));
            q.Pitch = DecodePitch(EncodePitch(input.Pitch));
            return q;
        }

        public static void Write(PacketWriter w, in PlayerInput input)
        {
            w.WriteSByte(EncodeAxis(input.Move.X));
            w.WriteSByte(EncodeAxis(input.Move.Y));
            w.WriteUShort(EncodeYaw(input.Yaw));
            w.WriteShort(EncodePitch(input.Pitch));
            w.WriteByte((byte)input.Buttons);
        }

        public static PlayerInput Read(ref PacketReader r, uint sequence)
        {
            sbyte x = r.ReadSByte();
            sbyte y = r.ReadSByte();
            ushort yaw = r.ReadUShort();
            short pitch = r.ReadShort();
            byte buttons = r.ReadByte();
            return new PlayerInput
            {
                Sequence = sequence,
                Move = new Vector2(DecodeAxis(x), DecodeAxis(y)),
                Yaw = DecodeYaw(yaw),
                Pitch = DecodePitch(pitch),
                Buttons = (InputButtons)(buttons & (byte)AllButtons),
            };
        }

        const InputButtons AllButtons = InputButtons.Jump | InputButtons.Crouch | InputButtons.Sprint | InputButtons.Attack | InputButtons.Interact | InputButtons.Aim;

        static sbyte EncodeAxis(float v)
        {
            if (float.IsNaN(v))
            {
                return 0;
            }

            return (sbyte)Math.Round(Clamp(v, -1f, 1f) * 127f);
        }

        static float DecodeAxis(sbyte v) => Math.Max(-1f, v / 127f);

        public static ushort EncodeYaw(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees))
            {
                return 0;
            }

            double wrapped = degrees % 360.0;
            if (wrapped < 0)
            {
                wrapped += 360.0;
            }

            return (ushort)((long)Math.Round(wrapped / 360.0 * 65536.0) & 0xFFFF);
        }

        public static float DecodeYaw(ushort v) => (float)(v * (360.0 / 65536.0));

        static short EncodePitch(float degrees)
        {
            if (float.IsNaN(degrees))
            {
                return 0;
            }

            return (short)Math.Round(Clamp(degrees, -89f, 89f) * 100f);
        }

        static float DecodePitch(short v) => Clamp(v / 100f, -89f, 89f);

        static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
