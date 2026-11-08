using System.Collections.Generic;
using Kindling.Simulation.Movement;

namespace Kindling.Net.Server
{
    /// <summary>A player in the server's world. Survives connection drops for the resume grace period.</summary>
    public sealed class ServerPlayer
    {
        internal readonly List<PlayerInput> PendingInputs = new List<PlayerInput>(8);
        internal PlayerState StateField;
        internal PlayerInput LastInput;

        internal ServerPlayer(ushort id, string name, byte[] resumeToken)
        {
            Id = id;
            Name = name;
            ResumeToken = resumeToken;
        }

        public ushort Id { get; }

        public string Name { get; }

        internal byte[] ResumeToken { get; }

        public PlayerState State => StateField;

        /// <summary>Transport connection, or -1 while waiting to be resumed.</summary>
        public int ConnectionId { get; internal set; } = -1;

        public bool Attached => ConnectionId >= 0;

        public bool Away { get; internal set; }

        public uint LastProcessedInput { get; internal set; }

        internal long DetachedAtTick { get; set; }

        /// <summary>Ticks simulated without a fresh input (input arrived late or was lost).</summary>
        public long StarvedTicks { get; internal set; }

        /// <summary>Inputs dropped because the queue was full (client running faster than the server).</summary>
        public long DroppedInputs { get; internal set; }

        public int PendingInputCount => PendingInputs.Count;
    }
}
