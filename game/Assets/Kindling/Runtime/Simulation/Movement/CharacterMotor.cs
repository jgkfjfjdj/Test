using System;
using System.Collections.Generic;
using System.Numerics;
using Kindling.Simulation.Collision;

namespace Kindling.Simulation.Movement
{
    /// <summary>
    /// Kinematic first-person movement, identical on client (prediction), server (authority) and bots.
    /// Order per tick: look → crouch/stand → horizontal acceleration → jump → gravity → collide (Y, X, Z
    /// with step-up) → ground snap → stamina.
    /// </summary>
    public static class CharacterMotor
    {
        const float DegToRad = (float)(Math.PI / 180.0);

        /// <summary>Forward (+Z at yaw 0) and right vectors on the ground plane for a yaw in degrees.</summary>
        public static void Basis(float yawDegrees, out Vector3 forward, out Vector3 right)
        {
            float r = yawDegrees * DegToRad;
            float sin = (float)Math.Sin(r);
            float cos = (float)Math.Cos(r);
            forward = new Vector3(sin, 0f, cos);
            right = new Vector3(cos, 0f, -sin);
        }

        public static void Simulate(ref PlayerState s, in PlayerInput input, MovementSettings m, CollisionWorld world, List<int> scratch, float dt)
        {
            if (!(dt > 0))
            {
                return;
            }

            s.Yaw = WrapDegrees(input.Yaw);
            s.Pitch = Clamp(input.Pitch, -89f, 89f);

            // --- crouch: crouching is instant; standing up needs head room.
            if (input.Has(InputButtons.Crouch))
            {
                s.Crouched = true;
            }
            else if (s.Crouched)
            {
                var standing = Aabb.FromFeet(s.Position, m.HalfWidth - CollisionWorld.Skin, m.StandingHeight);
                if (!world.Overlaps(standing, scratch))
                {
                    s.Crouched = false;
                }
            }

            float height = m.HeightFor(s.Crouched);

            // --- horizontal intent
            Vector2 move = input.Move;
            float magnitude = move.Length();
            if (magnitude > 1f)
            {
                move /= magnitude;
                magnitude = 1f;
            }

            Basis(s.Yaw, out Vector3 forward, out Vector3 right);
            Vector3 wish = forward * move.Y + right * move.X;

            s.Sprinting = input.Has(InputButtons.Sprint) && move.Y > 0.5f && !s.Crouched && !s.Exhausted && s.Stamina > 0f;
            float speed = s.Crouched ? m.CrouchSpeed : s.Sprinting ? m.SprintSpeed : m.WalkSpeed;
            var target = new Vector2(wish.X * speed, wish.Z * speed);
            var horizontal = new Vector2(s.Velocity.X, s.Velocity.Z);
            float accel = s.Grounded
                ? (target.LengthSquared() >= horizontal.LengthSquared() ? m.GroundAcceleration : m.GroundDeceleration)
                : m.AirAcceleration;
            horizontal = MoveTowards(horizontal, target, accel * dt);

            // --- jump on press (not while held)
            bool jumpHeld = input.Has(InputButtons.Jump);
            bool jumpPressed = jumpHeld && !s.JumpHeld;
            s.JumpHeld = jumpHeld;
            float vy = s.Velocity.Y;
            if (jumpPressed && s.Grounded && !s.Crouched && s.Stamina >= m.JumpStaminaCost)
            {
                vy = m.JumpSpeed;
                s.Grounded = false;
                s.Stamina -= m.JumpStaminaCost;
                s.StaminaRegenDelay = m.StaminaRegenDelaySeconds;
            }

            vy = Math.Max(vy - m.Gravity * dt, -m.TerminalSpeed);

            // --- collide
            var motion = new Vector3(horizontal.X * dt, vy * dt, horizontal.Y * dt);
            var box = Aabb.FromFeet(s.Position, m.HalfWidth, height);
            world.Gather(box.Expand(motion).Inflate(m.StepHeight + 0.1f), scratch);

            float dy = world.ClipY(box, motion.Y, scratch);
            bool hitGround = motion.Y < 0 && dy > motion.Y + 1e-6f;
            bool hitCeiling = motion.Y > 0 && dy < motion.Y - 1e-6f;
            box = box.Offset(0f, dy, 0f);
            if (hitGround || hitCeiling)
            {
                vy = 0f;
            }

            bool wasGrounded = s.Grounded;
            bool canStep = wasGrounded || hitGround;
            box = MoveHorizontal(box, motion.X, 0, canStep, m, world, scratch, ref horizontal.X, dt);
            box = MoveHorizontal(box, motion.Z, 2, canStep, m, world, scratch, ref horizontal.Y, dt);

            bool grounded = hitGround;
            if (!grounded && wasGrounded && vy <= 0f)
            {
                // Walking down steps/off small ledges: stick to the ground within step height.
                float down = world.ClipY(box, -m.StepHeight, scratch);
                if (down > -m.StepHeight + 1e-5f)
                {
                    box = box.Offset(0f, down, 0f);
                    grounded = true;
                    vy = 0f;
                }
            }

            s.Grounded = grounded;
            s.Position = box.Feet;
            s.Velocity = new Vector3(horizontal.X, vy, horizontal.Y);

            // --- stamina
            if (s.Sprinting && magnitude > 0.1f)
            {
                s.Stamina = Math.Max(0f, s.Stamina - m.SprintStaminaPerSecond * dt);
                s.StaminaRegenDelay = m.StaminaRegenDelaySeconds;
                if (s.Stamina <= 0f)
                {
                    s.Exhausted = true;
                }
            }
            else if (s.StaminaRegenDelay > 0f)
            {
                s.StaminaRegenDelay = Math.Max(0f, s.StaminaRegenDelay - dt);
            }
            else
            {
                s.Stamina = Math.Min(100f, s.Stamina + m.StaminaRegenPerSecond * dt);
            }

            if (s.Exhausted && s.Stamina >= m.SprintResumeStamina)
            {
                s.Exhausted = false;
            }
        }

        /// <summary>Moves along X (axis 0) or Z (axis 2), stepping up low obstacles when grounded.</summary>
        static Aabb MoveHorizontal(Aabb box, float delta, int axis, bool canStep, MovementSettings m, CollisionWorld world, List<int> scratch, ref float velocity, float dt)
        {
            if (delta == 0f)
            {
                return box;
            }

            float clipped = Clip(world, box, delta, axis, scratch);
            if (Math.Abs(clipped - delta) < 1e-6f)
            {
                return Shift(box, clipped, axis);
            }

            if (canStep)
            {
                float up = world.ClipY(box, m.StepHeight, scratch);
                if (up > 0f)
                {
                    var raised = box.Offset(0f, up, 0f);
                    float raisedClip = Clip(world, raised, delta, axis, scratch);
                    if (Math.Abs(raisedClip) > Math.Abs(clipped) + 1e-4f)
                    {
                        var moved = Shift(raised, raisedClip, axis);
                        float down = world.ClipY(moved, -up, scratch);
                        return moved.Offset(0f, down, 0f);
                    }
                }
            }

            // Blocked: keep what fits and stop velocity along this axis.
            velocity = dt > 0 ? clipped / dt : 0f;
            if (Math.Abs(clipped) < 1e-5f)
            {
                velocity = 0f;
            }

            return Shift(box, clipped, axis);
        }

        static float Clip(CollisionWorld world, in Aabb box, float delta, int axis, List<int> scratch) =>
            axis == 0 ? world.ClipX(box, delta, scratch) : world.ClipZ(box, delta, scratch);

        static Aabb Shift(in Aabb box, float delta, int axis) =>
            axis == 0 ? box.Offset(delta, 0f, 0f) : box.Offset(0f, 0f, delta);

        public static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDelta)
        {
            Vector2 diff = target - current;
            float distance = diff.Length();
            return distance <= maxDelta || distance < 1e-6f ? target : current + diff / distance * maxDelta;
        }

        public static float WrapDegrees(float degrees)
        {
            degrees %= 360f;
            return degrees < 0f ? degrees + 360f : degrees;
        }

        static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
