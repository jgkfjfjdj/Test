using Kindling.Simulation.Movement;

namespace Kindling.Net.Protocol
{
    /// <summary>
    /// Server → client every tick (sequenced). Layout: header, optional owner block (the receiver's own
    /// authoritative state + last processed input, part 0 only), then up to 255 remote entities.
    /// A tick's entities may be split across several parts to stay under
    /// <see cref="NetProtocol.SnapshotBudgetBytes"/>; each part is self-contained.
    /// </summary>
    public static class SnapshotMessage
    {
        public const byte FlagHasOwner = 1;

        public struct Header
        {
            public uint ServerTick;
            public byte Flags;

            /// <summary>Inputs waiting in the server's queue for this player after this tick (for client time dilation).</summary>
            public byte InputBufferDepth;

            public bool HasOwner => (Flags & FlagHasOwner) != 0;
        }

        /// <summary>Writes the header and owner block. Returns the offset of the entity count byte to patch later.</summary>
        public static int Begin(PacketWriter w, uint serverTick, byte inputBufferDepth, bool includeOwner, uint lastProcessedInput, in PlayerState owner)
        {
            w.WriteByte((byte)MessageType.Snapshot);
            w.WriteUInt(serverTick);
            w.WriteByte(includeOwner ? FlagHasOwner : (byte)0);
            w.WriteByte(inputBufferDepth);
            if (includeOwner)
            {
                w.WriteUInt(lastProcessedInput);
                PlayerStateCodec.Write(w, owner);
            }

            int countOffset = w.Length;
            w.WriteByte(0);
            return countOffset;
        }

        public static void SetEntityCount(PacketWriter w, int countOffset, int count) => w.Buffer[countOffset] = (byte)count;

        public static Header ReadHeader(ref PacketReader r) => new Header
        {
            ServerTick = r.ReadUInt(),
            Flags = r.ReadByte(),
            InputBufferDepth = r.ReadByte(),
        };

        public static void ReadOwner(ref PacketReader r, out uint lastProcessedInput, out PlayerState state)
        {
            lastProcessedInput = r.ReadUInt();
            state = PlayerStateCodec.Read(ref r);
        }
    }
}
