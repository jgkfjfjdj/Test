namespace Kindling.Net.Server
{
    public sealed class ServerStats
    {
        public long Tick;
        public int Players;
        public int Connections;

        /// <summary>Wall time of the last tick in milliseconds (simulation + snapshot building + sends).</summary>
        public double LastTickMs;

        /// <summary>Exponential moving average of tick time.</summary>
        public double AverageTickMs;

        /// <summary>Highest tick time since the last <see cref="ResetWindow"/>.</summary>
        public double MaxTickMs;

        public long SnapshotPackets;
        public long SnapshotBytes;

        /// <summary>Largest snapshot packet sent (bytes).</summary>
        public int MaxSnapshotBytes;
        public long StarvedTicks;
        public long DroppedInputs;
        public long ProtocolErrors;

        /// <summary>Time of the last GameWorld tick (survival, crafting, processors, loot) in ms.</summary>
        public double LastWorldMs;

        public long ChunkMessages;
        public long OwnerMessages;
        public long Actions;
        public long Saves;

        /// <summary>Time to capture the last save batch on the main thread (writing happens elsewhere).</summary>
        public double LastSaveCaptureMs;

        public void ResetWindow() => MaxTickMs = 0;
    }
}
