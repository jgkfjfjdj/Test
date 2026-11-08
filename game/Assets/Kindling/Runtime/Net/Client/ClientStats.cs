namespace Kindling.Net.Client
{
    public sealed class ClientStats
    {
        public int RoundTripMs = -1;
        public uint ServerTick;
        public long SnapshotsReceived;
        public int Corrections;

        /// <summary>Size of the most recent correction in metres.</summary>
        public float LastCorrection;

        public float MaxCorrection;

        /// <summary>Inputs waiting on the server after its last tick (target 1–3).</summary>
        public int ServerInputBuffer;

        /// <summary>Current tick interval scale (below 1 = running faster to fill the server's input buffer).</summary>
        public double TickScale = 1;

        public double InterpolationDelayMs;
        public double BytesInPerSecond;
        public double BytesOutPerSecond;
        public int RemotePlayers;
    }
}
