namespace Kindling.Client.View
{
    /// <summary>Simulation (System.Numerics) ↔ Unity vectors. Both use X right, Y up, Z forward.</summary>
    public static class Conversions
    {
        public static UnityEngine.Vector3 ToUnity(this System.Numerics.Vector3 v) => new UnityEngine.Vector3(v.X, v.Y, v.Z);

        public static System.Numerics.Vector3 ToNumerics(this UnityEngine.Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);

        /// <summary>Simulation yaw/pitch (degrees, positive pitch looks up) to a Unity rotation.</summary>
        public static UnityEngine.Quaternion ViewRotation(float yaw, float pitch) => UnityEngine.Quaternion.Euler(-pitch, yaw, 0f);
    }
}
