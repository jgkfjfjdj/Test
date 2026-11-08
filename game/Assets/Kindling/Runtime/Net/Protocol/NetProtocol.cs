namespace Kindling.Net.Protocol
{
    /// <summary>Wire protocol constants shared by client, server and bots (docs/protocol/game-netcode.md).</summary>
    public static class NetProtocol
    {
        /// <summary>Bumped on any incompatible wire change. Client and server must match exactly.</summary>
        public const ushort Version = 2;

        /// <summary>Transport-level key: filters out stray UDP traffic before any message parsing.</summary>
        public const string ConnectionKey = "kindling-v1";

        public const int DefaultPort = 27015;
        public const int TickRate = 30;

        /// <summary>Each input packet repeats the newest N inputs so a lost packet rarely loses an input.</summary>
        public const int InputRedundancy = 3;

        public const int MaxDisplayNameBytes = 48;
        public const int MaxBuildVersionBytes = 32;
        public const int MaxAuthTokenBytes = 2048;
        public const int ResumeTokenLength = 16;

        /// <summary>
        /// Upper limit for one snapshot packet; larger snapshots are split into parts. The server also
        /// stays under each connection's current sequenced packet limit (504 bytes before MTU discovery).
        /// </summary>
        public const int SnapshotBudgetBytes = 1100;

        /// <summary>Smallest packet budget the server will build snapshots for.</summary>
        public const int MinSnapshotBudgetBytes = 200;
    }

    public enum MessageType : byte
    {
        Hello = 1,
        Welcome = 2,
        Inputs = 3,
        Snapshot = 4,
        PlayerInfo = 5,
        PlayerLeft = 6,
        Background = 7,

        // Protocol 2 (survival):
        MapData = 8,
        WorldState = 9,
        OwnerState = 10,
        Container = 11,
        Events = 12,
        ChunkSnapshot = 13,
        ChunkDelta = 14,
        ChunkUnload = 15,
        Action = 16,
        ActionResult = 17,
    }

    /// <summary>Why a connection ended. Sent as the first byte of the transport's disconnect payload.</summary>
    public enum DisconnectCode : byte
    {
        None = 0,
        ClientQuit = 1,
        ProtocolMismatch = 2,
        ServerFull = 3,
        AuthFailed = 4,
        HandshakeTimeout = 5,
        Kicked = 6,
        ServerShutdown = 7,
        ProtocolError = 8,

        /// <summary>The same player connected again (resumed) from another connection.</summary>
        Replaced = 9,

        /// <summary>Client and server have different game content (items, recipes…): update the game.</summary>
        ContentMismatch = 10,

        // Local-only codes (never sent): the transport reports these.
        Timeout = 100,
        ConnectionFailed = 101,
    }

    /// <summary>How a message is delivered. Maps to LiteNetLib delivery methods.</summary>
    public enum Delivery : byte
    {
        /// <summary>May be lost; older packets arriving after newer ones are dropped. Inputs, snapshots.</summary>
        Sequenced = 0,

        /// <summary>Never lost, arrives in order. Handshake, player info, events.</summary>
        ReliableOrdered = 1,
    }
}
