using System;
using System.Numerics;

namespace Kindling.Simulation.Movement
{
    [Flags]
    public enum InputButtons : byte
    {
        None = 0,
        Jump = 1 << 0,
        Crouch = 1 << 1,
        Sprint = 1 << 2,
        Attack = 1 << 3,
        Interact = 1 << 4,
        Aim = 1 << 5,
    }

    /// <summary>
    /// One simulation tick of player intent. Clients send these (never positions); the server applies
    /// exactly one per tick per player, in sequence order.
    /// </summary>
    public struct PlayerInput : IEquatable<PlayerInput>
    {
        /// <summary>Increases by one per client tick; used for acknowledgement and replay.</summary>
        public uint Sequence;

        /// <summary>Movement stick: X = strafe right, Y = forward; length ≤ 1 (analog).</summary>
        public Vector2 Move;

        /// <summary>Look direction in degrees: yaw around Y (0 = +Z), pitch positive = looking down.</summary>
        public float Yaw;

        public float Pitch;
        public InputButtons Buttons;

        public bool Has(InputButtons button) => (Buttons & button) != 0;

        public bool Equals(PlayerInput other) =>
            Sequence == other.Sequence && Move.Equals(other.Move) && Yaw.Equals(other.Yaw) && Pitch.Equals(other.Pitch) && Buttons == other.Buttons;

        public override bool Equals(object obj) => obj is PlayerInput other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Sequence, Move, Yaw, Pitch, Buttons);

        public override string ToString() => $"#{Sequence} move={Move} yaw={Yaw:0.#} pitch={Pitch:0.#} {Buttons}";
    }
}
