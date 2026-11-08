using Kindling.Net.Protocol;

namespace Kindling.Net.Client
{
    public sealed class ClientSettings
    {
        public string DisplayName = "Player";
        public string BuildVersion = "dev";

        /// <summary>Join ticket from the backend; empty for dev servers.</summary>
        public string AuthToken = string.Empty;

        public double HandshakeTimeoutSeconds = 10;

        /// <summary>Prediction error (metres) above which the client snaps to the server state and replays.</summary>
        public float ReconcileThreshold = 0.01f;

        /// <summary>Corrections larger than this are applied instantly instead of smoothed (teleports).</summary>
        public float MaxSmoothedCorrection = 2f;

        /// <summary>How fast the visual correction offset decays (per second, exponential).</summary>
        public float CorrectionSmoothingRate = 12f;

        /// <summary>Interpolation delay for remote players, in ticks: starts here and grows with jitter.</summary>
        public double MinInterpolationTicks = 3;

        public double MaxInterpolationTicks = 10;

        /// <summary>Remote players not updated for this long left our area (or the server).</summary>
        public double StaleEntitySeconds = 2;

        /// <summary>Most ticks run in one <c>Update</c> call; more means we fell behind and skip time.</summary>
        public int MaxTicksPerUpdate = 5;

        public ushort ProtocolVersion = NetProtocol.Version;
    }
}
