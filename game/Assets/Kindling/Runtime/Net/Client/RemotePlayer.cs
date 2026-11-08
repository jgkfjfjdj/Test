using System.Numerics;
using Kindling.Net.Protocol;

namespace Kindling.Net.Client
{
    /// <summary>An interpolated remote player, ready to render.</summary>
    public struct RemotePlayer
    {
        public ushort Id;
        public string Name;
        public Vector3 Position;
        public float Yaw;
        public float Pitch;
        public EntityFlags Flags;

        /// <summary>Server tick of the newest update received for this player.</summary>
        public uint NewestTick;
    }
}
