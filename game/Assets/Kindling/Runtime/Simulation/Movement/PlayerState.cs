using System.Numerics;

namespace Kindling.Simulation.Movement
{
    /// <summary>Complete movement state of one player. Small enough to copy freely (prediction buffers).</summary>
    public struct PlayerState
    {
        /// <summary>Bottom centre of the collider.</summary>
        public Vector3 Position;

        public Vector3 Velocity;
        public float Yaw;
        public float Pitch;
        public bool Grounded;
        public bool Crouched;
        public bool Sprinting;

        /// <summary>Jump was held last tick (jumps trigger on press, not while held).</summary>
        public bool JumpHeld;

        /// <summary>0–100. Sprinting drains it; it refills after a short pause.</summary>
        public float Stamina;

        /// <summary>Set when stamina ran out; sprint is blocked until it recovers to the resume threshold.</summary>
        public bool Exhausted;

        /// <summary>Seconds until stamina starts regenerating again.</summary>
        public float StaminaRegenDelay;

        public static PlayerState SpawnAt(Vector3 feet, float yaw = 0f) => new PlayerState
        {
            Position = feet,
            Yaw = yaw,
            Stamina = 100f,
            Grounded = false,
        };
    }
}
