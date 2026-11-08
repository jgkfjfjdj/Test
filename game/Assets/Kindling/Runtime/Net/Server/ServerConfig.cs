using Kindling.Net.Protocol;

namespace Kindling.Net.Server
{
    public sealed class ServerConfig
    {
        public int Port = NetProtocol.DefaultPort;
        public int MaxPlayers = 100;
        public int TickRate = NetProtocol.TickRate;
        public int MapSeed = 1;

        /// <summary>Accept connections without a join ticket (local/dev servers only).</summary>
        public bool AllowDevAuth = true;

        /// <summary>Players within this distance are sent every tick.</summary>
        public float NearRadius = 100f;

        /// <summary>Players beyond this distance are not sent at all.</summary>
        public float InterestRadius = 250f;

        /// <summary>Players between near and interest radius are sent every Nth tick.</summary>
        public int FarUpdateInterval = 3;

        public double HandshakeTimeoutSeconds = 5;

        /// <summary>How long a dropped player stays in the world, waiting to be resumed.</summary>
        public double ResumeGraceSeconds = 120;

        /// <summary>Inputs buffered per player; extra ones (a client running fast) drop the oldest.</summary>
        public int InputQueueCapacity = 8;

        /// <summary>Clients receive world entities for chunks within this many chunks (64 m each) of them…</summary>
        public int ChunkSubscribeRadius = 3;

        /// <summary>…and keep them until they are this far (hysteresis).</summary>
        public int ChunkUnsubscribeRadius = 4;

        /// <summary>Seconds between incremental world saves (when persistence is enabled).</summary>
        public double SaveIntervalSeconds = 60;

        /// <summary>Seconds between full checkpoints.</summary>
        public double CheckpointIntervalSeconds = 1800;
    }
}
