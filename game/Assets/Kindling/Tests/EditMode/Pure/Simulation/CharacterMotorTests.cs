using System;
using System.Collections.Generic;
using System.Numerics;
using Kindling.Simulation.Collision;
using Kindling.Simulation.Movement;
using NUnit.Framework;

namespace Kindling.Tests.Pure.Simulation
{
    public class CharacterMotorTests
    {
        static readonly Vector2 Forward = new Vector2(0f, 1f);

        [Test]
        public void SpawnedPlayerLandsOnTheGround()
        {
            var kit = new MotorTestKit(new Vector3(0f, 2f, 0f));
            kit.Run(40, Vector2.Zero);
            Assert.That(kit.State.Grounded, Is.True);
            Assert.That(kit.State.Position.Y, Is.EqualTo(0f).Within(0.01f));
            Assert.That(kit.State.Velocity.Y, Is.EqualTo(0f));
        }

        [Test]
        public void WalkSprintAndCrouchReachTheirSpeeds()
        {
            var kit = new MotorTestKit(Vector3.Zero, yaw: 180f);
            kit.Run(30, Forward);
            Assert.That(kit.HorizontalSpeed, Is.EqualTo(kit.Settings.WalkSpeed).Within(0.01f));
            kit.Run(30, Forward, InputButtons.Sprint);
            Assert.That(kit.HorizontalSpeed, Is.EqualTo(kit.Settings.SprintSpeed).Within(0.01f));
            Assert.That(kit.State.Sprinting, Is.True);
            kit.Run(30, Forward, InputButtons.Crouch | InputButtons.Sprint);
            Assert.That(kit.HorizontalSpeed, Is.EqualTo(kit.Settings.CrouchSpeed).Within(0.01f));
            Assert.That(kit.State.Sprinting, Is.False);
        }

        [Test]
        public void AnalogStickScalesSpeedAndYawRotatesDirection()
        {
            var kit = new MotorTestKit(Vector3.Zero, yaw: 90f);
            kit.Run(30, new Vector2(0f, 0.5f));
            Assert.That(kit.HorizontalSpeed, Is.EqualTo(kit.Settings.WalkSpeed * 0.5f).Within(0.01f));
            Assert.That(kit.State.Velocity.X, Is.GreaterThan(1.9f), "yaw 90 faces +X");
            Assert.That(Math.Abs(kit.State.Velocity.Z), Is.LessThan(0.01f));
        }

        [Test]
        public void JumpReachesAboutOneMetreAndOnlyTriggersOnPress()
        {
            var kit = new MotorTestKit(Vector3.Zero);
            float peak = 0f;
            for (int i = 0; i < 40; i++)
            {
                kit.Run(1, Vector2.Zero, InputButtons.Jump);
                peak = Math.Max(peak, kit.State.Position.Y);
            }

            float expected = kit.Settings.JumpSpeed * kit.Settings.JumpSpeed / (2f * kit.Settings.Gravity);
            Assert.That(peak, Is.EqualTo(expected).Within(0.12f));
            Assert.That(kit.State.Grounded, Is.True, "landed");
            kit.Run(20, Vector2.Zero, InputButtons.Jump); // still held: no second jump
            Assert.That(kit.State.Position.Y, Is.EqualTo(0f).Within(0.01f));
            kit.Run(1, Vector2.Zero);
            kit.Run(1, Vector2.Zero, InputButtons.Jump); // pressed again
            Assert.That(kit.State.Position.Y, Is.GreaterThan(0.1f));
        }

        [Test]
        public void WallsBlockMovement()
        {
            // Perimeter wall at z = +256: walk north from z = 250 for 4 seconds.
            var kit = new MotorTestKit(new Vector3(0f, 0f, 250f));
            kit.Run(120, Forward);
            Assert.That(kit.State.Position.Z, Is.LessThan(256f - kit.Settings.HalfWidth + 0.01f));
            Assert.That(kit.State.Position.Z, Is.GreaterThan(255f));
            Assert.That(kit.State.Velocity.Z, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void ClimbsStairsOntoThePlatform()
        {
            var kit = new MotorTestKit(MotorTestKit.Layout.StairsStart);
            kit.Run(75, Forward); // walk north up the stairs (~10 m)
            Assert.That(kit.State.Position.Y, Is.EqualTo(MotorTestKit.Layout.PlatformTop.Y).Within(0.01f));
            Assert.That(kit.State.Position.Z, Is.InRange(14f, 20f));
            Assert.That(kit.State.Grounded, Is.True);
        }

        [Test]
        public void WalksDownStairsWithoutLeavingTheGround()
        {
            var top = MotorTestKit.Layout.PlatformTop;
            var kit = new MotorTestKit(new Vector3(MotorTestKit.Layout.StairsStart.X, top.Y, top.Z), yaw: 180f);
            int airborneTicks = 0;
            for (int i = 0; i < 150; i++)
            {
                kit.Run(1, Forward);
                if (!kit.State.Grounded)
                {
                    airborneTicks++;
                }
            }

            Assert.That(kit.State.Position.Y, Is.EqualTo(0f).Within(0.01f));
            Assert.That(airborneTicks, Is.LessThanOrEqualTo(2), "steps are snapped, not fallen down");
        }

        [Test]
        public void CannotStepOverTheHighWallButCanJumpOnIt()
        {
            var kit = new MotorTestKit(MotorTestKit.Layout.HighWallFront, yaw: 270f);
            kit.Run(90, Forward);
            Assert.That(kit.State.Position.X, Is.GreaterThan(-15f), "blocked by the 1.2 m wall");
            Assert.That(kit.State.Position.Y, Is.EqualTo(0f).Within(0.01f));

            // A running jump clears 1.0 m, not 1.2 m: still blocked (and doesn't clip through).
            kit.Run(5, Forward, InputButtons.Jump);
            kit.Run(60, Forward);
            Assert.That(kit.State.Position.X, Is.GreaterThan(-15f));
        }

        [Test]
        public void TunnelOnlyFitsWhenCrouchedAndStandingUpIsBlockedInside()
        {
            var kit = new MotorTestKit(MotorTestKit.Layout.TunnelEntrance, yaw: 90f);
            kit.Run(90, Forward);
            Assert.That(kit.State.Position.X, Is.LessThan(20f), "standing: blocked by the 1.4 m roof");

            kit.Run(150, Forward, InputButtons.Crouch);
            Assert.That(kit.State.Position.X, Is.GreaterThan(23f), "crouched: inside the tunnel");
            float x = kit.State.Position.X;

            kit.Run(5, Vector2.Zero); // release crouch inside
            Assert.That(kit.State.Crouched, Is.True, "no head room to stand");
            kit.Run(200, Forward); // keeps crawling out the other side
            Assert.That(kit.State.Position.X, Is.GreaterThan(30f));
            Assert.That(kit.State.Crouched, Is.False, "stood up after leaving the tunnel");
            Assert.That(x, Is.LessThan(kit.State.Position.X));
        }

        [Test]
        public void SprintDrainsStaminaUntilExhaustedThenRecovers()
        {
            var kit = new MotorTestKit(Vector3.Zero, yaw: 180f);
            int ticks = 0;
            while (!kit.State.Exhausted && ticks < 30 * 20)
            {
                kit.Run(1, Forward, InputButtons.Sprint);
                ticks++;
            }

            float expectedSeconds = 100f / kit.Settings.SprintStaminaPerSecond;
            Assert.That(ticks * MotorTestKit.Dt, Is.EqualTo(expectedSeconds).Within(0.1f));
            kit.Run(30, Forward, InputButtons.Sprint); // still holding sprint: walk speed only
            Assert.That(kit.State.Sprinting, Is.False);
            Assert.That(kit.HorizontalSpeed, Is.EqualTo(kit.Settings.WalkSpeed).Within(0.05f));
            Assert.That(kit.State.Exhausted, Is.True, "regen hasn't reached the resume threshold yet");

            // Recovery: the delay ran out during the last second, so 1 more second gives +20.
            float before = kit.State.Stamina;
            kit.Run(30, Vector2.Zero);
            Assert.That(kit.State.Stamina - before, Is.EqualTo(kit.Settings.StaminaRegenPerSecond).Within(1f));
            Assert.That(kit.State.Exhausted, Is.True, "20 is still below the resume threshold (25)");
            kit.Run(15, Vector2.Zero);
            Assert.That(kit.State.Exhausted, Is.False, "past the resume threshold");
            kit.Run(10, Forward, InputButtons.Sprint);
            Assert.That(kit.State.Sprinting, Is.True);
        }

        [Test]
        public void JumpingCostsStaminaAndDelaysRegen()
        {
            var kit = new MotorTestKit(Vector3.Zero);
            kit.Run(1, Vector2.Zero, InputButtons.Jump);
            Assert.That(kit.State.Stamina, Is.EqualTo(100f - kit.Settings.JumpStaminaCost).Within(0.01f));
            kit.Run(25, Vector2.Zero); // still inside the 1 s delay
            Assert.That(kit.State.Stamina, Is.EqualTo(100f - kit.Settings.JumpStaminaCost).Within(0.01f));
            kit.Run(30, Vector2.Zero);
            Assert.That(kit.State.Stamina, Is.EqualTo(100f));
        }

        [Test]
        public void IdenticalInputsGiveIdenticalStates()
        {
            PlayerState RunScript()
            {
                var kit = new MotorTestKit(MotorTestKit.Layout.StairsStart, yaw: 10f);
                for (int i = 0; i < 300; i++)
                {
                    var buttons = (i % 40 == 0 ? InputButtons.Jump : InputButtons.None) | (i > 150 ? InputButtons.Sprint : InputButtons.None);
                    kit.Run(1, new Vector2((float)Math.Sin(i * 0.05), 1f), buttons, yaw: 10f + i * 0.7f);
                }

                return kit.State;
            }

            PlayerState a = RunScript();
            PlayerState b = RunScript();
            Assert.That(a.Position, Is.EqualTo(b.Position));
            Assert.That(a.Velocity, Is.EqualTo(b.Velocity));
            Assert.That(a.Stamina, Is.EqualTo(b.Stamina));
        }

        [Test]
        public void ResolvesAgainstACustomWorld()
        {
            var world = new CollisionWorld(new[] { new Aabb(new Vector3(-1, 0, 2), new Vector3(1, 3, 3)) });
            var kit = new MotorTestKit(Vector3.Zero, world: world);
            kit.Run(60, Forward);
            Assert.That(kit.State.Position.Z, Is.EqualTo(2f - kit.Settings.HalfWidth).Within(0.01f));
            var scratch = new List<int>();
            Assert.That(world.Overlaps(Aabb.FromFeet(kit.State.Position, kit.Settings.HalfWidth, 1.8f), scratch), Is.False);
        }
    }
}
