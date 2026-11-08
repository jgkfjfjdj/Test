using Kindling.Simulation.Movement;

namespace Kindling.Net.Protocol
{
    /// <summary>Server → client, accepts the Hello (reliable).</summary>
    public struct WelcomeMessage
    {
        public ushort PlayerId;
        public uint ServerTick;
        public byte TickRate;
        public int MapSeed;
        public byte[] ResumeToken;

        /// <summary>True when this connection took back an existing player.</summary>
        public bool Resumed;

        /// <summary>Last input sequence the server processed for this player (0 for a new player).</summary>
        public uint LastInputSequence;

        public PlayerState State;

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)MessageType.Welcome);
            w.WriteUShort(PlayerId);
            w.WriteUInt(ServerTick);
            w.WriteByte(TickRate);
            w.WriteInt(MapSeed);
            w.WriteBlob(ResumeToken);
            w.WriteBool(Resumed);
            w.WriteUInt(LastInputSequence);
            PlayerStateCodec.Write(w, State);
        }

        public static WelcomeMessage Read(ref PacketReader r)
        {
            var m = new WelcomeMessage
            {
                PlayerId = r.ReadUShort(),
                ServerTick = r.ReadUInt(),
                TickRate = r.ReadByte(),
                MapSeed = r.ReadInt(),
                ResumeToken = r.ReadBlob(NetProtocol.ResumeTokenLength),
                Resumed = r.ReadBool(),
                LastInputSequence = r.ReadUInt(),
            };
            m.State = PlayerStateCodec.Read(ref r);
            if (m.TickRate == 0 || m.PlayerId == 0)
            {
                throw new ProtocolException("Invalid welcome.");
            }

            return m;
        }
    }
}
