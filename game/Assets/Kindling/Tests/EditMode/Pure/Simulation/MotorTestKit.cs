using System.Collections.Generic;
using System.Numerics;
using Kindling.Simulation.Collision;
using Kindling.Simulation.Movement;
using Kindling.Simulation.World;

namespace Kindling.Tests.Pure.Simulation
{
    /// <summary>Drives the motor at 30 Hz against the greybox map (or a custom world).</summary>
    public sealed class MotorTestKit
    {
        public const float Dt = 1f / 30f;
        public static readonly GreyboxLayout Layout = GreyboxLayout.Generate(1);
        static readonly CollisionWorld s_World = Layout.BuildCollisionWorld();

        public readonly CollisionWorld World;
        public readonly MovementSettings Settings = new MovementSettings();
        public PlayerState State;
        readonly List<int> m_Scratch = new List<int>();
        uint m_Sequence;

        public MotorTestKit(Vector3 spawn, float yaw = 0f, CollisionWorld world = null)
        {
            World = world ?? s_World;
            State = PlayerState.SpawnAt(spawn, yaw);
            Settle();
        }

        /// <summary>Lets the player land after spawning.</summary>
        public void Settle() => Run(10, Vector2.Zero);

        public void Run(int ticks, Vector2 move, InputButtons buttons = InputButtons.None, float? yaw = null)
        {
            for (int i = 0; i < ticks; i++)
            {
                var input = new PlayerInput { Sequence = ++m_Sequence, Move = move, Yaw = yaw ?? State.Yaw, Buttons = buttons };
                CharacterMotor.Simulate(ref State, input, Settings, World, m_Scratch, Dt);
            }
        }

        public float HorizontalSpeed => new Vector2(State.Velocity.X, State.Velocity.Z).Length();
    }
}
