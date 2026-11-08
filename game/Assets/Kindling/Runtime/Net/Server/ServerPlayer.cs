using System.Collections.Generic;
using Kindling.Core.World;
using Kindling.Simulation.Game;
using Kindling.Simulation.Movement;

namespace Kindling.Net.Server
{
    /// <summary>A connected player: the network side of a <see cref="Character"/>.</summary>
    public sealed class ServerPlayer
    {
        internal readonly List<PlayerInput> PendingInputs = new List<PlayerInput>(8);
        internal PlayerInput LastInput;
        internal readonly HashSet<ChunkCoord> Chunks = new HashSet<ChunkCoord>();
        internal ChunkCoord LastChunk = new ChunkCoord(int.MinValue, int.MinValue);
        internal uint SentMain = uint.MaxValue;
        internal uint SentHotbar = uint.MaxValue;
        internal uint SentWear = uint.MaxValue;
        internal uint SentOpenId;
        internal uint SentOpenVersion = uint.MaxValue;
        internal uint SentStateVersion = uint.MaxValue;
        internal float SentHealth = -1;
        internal float SentHunger = -1;
        internal float SentThirst = -1;
        internal float SentWarmth = -999;
        internal uint LastStateTick;
        internal uint SwingUntilTick;

        internal ServerPlayer(ushort id, Character character, int connectionId, byte[] resumeToken)
        {
            Id = id;
            Character = character;
            ConnectionId = connectionId;
            ResumeToken = resumeToken;
        }

        public ushort Id { get; }

        public Character Character { get; }

        public string Name => Character.Name;

        internal byte[] ResumeToken { get; }

        /// <summary>The character's movement state (authoritative).</summary>
        public PlayerState State => Character.Motion;

        public int ConnectionId { get; internal set; }

        public bool Attached => ConnectionId >= 0;

        public bool Away { get; internal set; }

        public uint LastProcessedInput { get; internal set; }

        /// <summary>Ticks simulated without a fresh input (input arrived late or was lost).</summary>
        public long StarvedTicks { get; internal set; }

        /// <summary>Inputs dropped because the queue was full (client running faster than the server).</summary>
        public long DroppedInputs { get; internal set; }

        public int PendingInputCount => PendingInputs.Count;
    }
}
