using System;
using System.Numerics;

namespace Kindling.Controls
{
    /// <summary>
    /// Virtual movement stick. Output is analog (-1..1 per axis) with a dead zone; pushing past the
    /// sprint threshold straight forward sprints; sliding further up locks sprint (auto-run) until the
    /// player pulls back.
    /// </summary>
    public sealed class FloatingJoystick
    {
        readonly ControlSettings m_Settings;
        Vector2 m_Home;

        public FloatingJoystick(ControlSettings settings, Vector2 home)
        {
            m_Settings = settings;
            m_Home = home;
            Origin = home;
        }

        public bool Active { get; private set; }

        /// <summary>Center of the stick (where the thumb landed, for a floating stick).</summary>
        public Vector2 Origin { get; private set; }

        /// <summary>Where the knob is drawn (clamped to the radius).</summary>
        public Vector2 Knob { get; private set; }

        /// <summary>Movement after the dead zone, length ≤ 1. X = strafe right, Y = forward.</summary>
        public Vector2 Value { get; private set; }

        public bool Sprinting { get; private set; }

        public bool SprintLocked { get; private set; }

        public void SetHome(Vector2 home)
        {
            m_Home = home;
            if (!Active)
            {
                Origin = home;
                Knob = home;
            }
        }

        public void Begin(Vector2 position)
        {
            Active = true;
            Origin = m_Settings.FloatingJoystick ? position : m_Home;
            Drag(position);
        }

        public void Drag(Vector2 position)
        {
            float radius = Math.Max(1f, m_Settings.JoystickRadius);
            Vector2 delta = position - Origin;
            float distance = delta.Length();
            Vector2 raw = distance > radius ? delta / distance : delta / radius;
            Knob = Origin + raw * radius;

            float magnitude = raw.Length();
            float dz = ControlSettings.Clamp(m_Settings.DeadZone, 0f, 0.9f);
            Value = magnitude <= dz ? Vector2.Zero : raw / magnitude * ((magnitude - dz) / (1f - dz));

            if (SprintLocked && raw.Y < -0.2f)
            {
                SprintLocked = false; // pulled back
            }
            else if (m_Settings.SprintLock && delta.Y > radius + m_Settings.SprintLockDistance && Math.Abs(delta.X) < radius)
            {
                SprintLocked = true;
            }

            Sprinting = SprintLocked || raw.Y >= m_Settings.SprintThreshold;
        }

        public void End()
        {
            Active = false;
            Origin = m_Home;
            Knob = m_Home;
            if (SprintLocked)
            {
                Value = new Vector2(0f, 1f); // auto-run
                Sprinting = true;
            }
            else
            {
                Value = Vector2.Zero;
                Sprinting = false;
            }
        }

        public void CancelSprintLock()
        {
            SprintLocked = false;
            if (!Active)
            {
                Value = Vector2.Zero;
                Sprinting = false;
            }
        }
    }
}
