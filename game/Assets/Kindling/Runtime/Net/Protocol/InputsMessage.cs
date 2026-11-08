using System;
using System.Collections.Generic;
using Kindling.Simulation.Movement;
using Kindling.Core.Serialization;

namespace Kindling.Net.Protocol
{
    /// <summary>
    /// Client → server every tick (sequenced): the newest input plus the previous ones
    /// (<see cref="NetProtocol.InputRedundancy"/>), newest first, with consecutive sequence numbers.
    /// </summary>
    public static class InputsMessage
    {
        public const int MaxInputs = 8;

        /// <summary>Writes <paramref name="count"/> inputs ending at <paramref name="newest"/> from a history list (oldest first).</summary>
        public static void Write(PacketWriter w, IReadOnlyList<PlayerInput> newestFirst, int count)
        {
            count = Math.Min(Math.Min(count, newestFirst.Count), MaxInputs);
            if (count <= 0)
            {
                throw new ArgumentException("No inputs to send.", nameof(count));
            }

            w.WriteByte((byte)MessageType.Inputs);
            w.WriteUInt(newestFirst[0].Sequence);
            w.WriteByte((byte)count);
            for (int i = 0; i < count; i++)
            {
                if (newestFirst[i].Sequence != newestFirst[0].Sequence - (uint)i)
                {
                    throw new ArgumentException("Inputs must have consecutive sequence numbers, newest first.");
                }

                InputCodec.Write(w, newestFirst[i]);
            }
        }

        /// <summary>Reads the body into <paramref name="newestFirst"/> (cleared first). Sequence 0 is never valid.</summary>
        public static void Read(ref PacketReader r, List<PlayerInput> newestFirst)
        {
            newestFirst.Clear();
            uint newest = r.ReadUInt();
            int count = r.ReadByte();
            if (count == 0 || count > MaxInputs)
            {
                throw new ProtocolException("Invalid input count.");
            }

            for (int i = 0; i < count; i++)
            {
                uint sequence = newest - (uint)i;
                PlayerInput input = InputCodec.Read(ref r, sequence);
                if (sequence != 0 && sequence <= newest)
                {
                    newestFirst.Add(input);
                }
            }
        }
    }
}
