using System.Numerics;
using Kindling.Simulation.Movement;
using Kindling.Core.Serialization;

namespace Kindling.Net.Protocol
{
    /// <summary>
    /// Full-precision player state, sent only to the owning client so its reconciliation replays from
    /// exactly the server's state (41 bytes).
    /// </summary>
    public static class PlayerStateCodec
    {
        [System.Flags]
        enum Bits : byte
        {
            Grounded = 1,
            Crouched = 2,
            Sprinting = 4,
            JumpHeld = 8,
            Exhausted = 16,
        }

        public static void Write(PacketWriter w, in PlayerState s)
        {
            WriteVector(w, s.Position);
            WriteVector(w, s.Velocity);
            w.WriteFloat(s.Yaw);
            w.WriteFloat(s.Pitch);
            Bits bits = 0;
            if (s.Grounded) bits |= Bits.Grounded;
            if (s.Crouched) bits |= Bits.Crouched;
            if (s.Sprinting) bits |= Bits.Sprinting;
            if (s.JumpHeld) bits |= Bits.JumpHeld;
            if (s.Exhausted) bits |= Bits.Exhausted;
            w.WriteByte((byte)bits);
            w.WriteFloat(s.Stamina);
            w.WriteFloat(s.StaminaRegenDelay);
        }

        public static PlayerState Read(ref PacketReader r)
        {
            var s = new PlayerState
            {
                Position = ReadVector(ref r),
                Velocity = ReadVector(ref r),
                Yaw = r.ReadFloat(),
                Pitch = r.ReadFloat(),
            };
            var bits = (Bits)r.ReadByte();
            s.Grounded = (bits & Bits.Grounded) != 0;
            s.Crouched = (bits & Bits.Crouched) != 0;
            s.Sprinting = (bits & Bits.Sprinting) != 0;
            s.JumpHeld = (bits & Bits.JumpHeld) != 0;
            s.Exhausted = (bits & Bits.Exhausted) != 0;
            s.Stamina = r.ReadFloat();
            s.StaminaRegenDelay = r.ReadFloat();
            return s;
        }

        static void WriteVector(PacketWriter w, Vector3 v)
        {
            w.WriteFloat(v.X);
            w.WriteFloat(v.Y);
            w.WriteFloat(v.Z);
        }

        static Vector3 ReadVector(ref PacketReader r) => new Vector3(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
    }
}
