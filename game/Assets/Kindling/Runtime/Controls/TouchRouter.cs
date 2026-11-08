using System.Collections.Generic;
using System.Numerics;
using Kindling.Simulation.Movement;

namespace Kindling.Controls
{
    /// <summary>The controls' output for one frame.</summary>
    public struct ControlFrame
    {
        public Vector2 Move;
        public float Yaw;
        public float Pitch;

        /// <summary>Buttons held (or toggled on) this frame, including sprint.</summary>
        public InputButtons Held;

        /// <summary>Buttons that went down this frame (so a tap between network ticks still counts).</summary>
        public InputButtons Pressed;
    }

    /// <summary>
    /// Routes touches to controls. Each finger belongs to the first control it touched until it lifts:
    /// a visible button, else the joystick (in the movement zone), else the camera. One finger looks at a
    /// time; the joystick, look and any buttons work together.
    /// </summary>
    public sealed class TouchRouter
    {
        const int LookOwner = -1;
        const int JoystickOwner = -2;

        readonly ControlSettings m_Settings;
        readonly Dictionary<int, int> m_Owners = new Dictionary<int, int>(); // finger → button index / owner code
        readonly Dictionary<int, Vector2> m_LastPositions = new Dictionary<int, Vector2>();
        InputButtons m_Pressed;
        int m_LookFinger = int.MinValue;

        public TouchRouter(ControlLayout layout, ControlSettings settings)
        {
            m_Settings = settings;
            Layout = layout;
            Joystick = new FloatingJoystick(settings, layout.JoystickHome);
            Look = new LookProcessor(settings);
        }

        public ControlLayout Layout { get; private set; }

        public FloatingJoystick Joystick { get; }

        public LookProcessor Look { get; }

        public int ActiveFingers => m_Owners.Count;

        public bool Aiming => Layout.Find(ControlButton.Aim)?.Active ?? false;

        /// <summary>Swaps the layout (rotation, HUD editor). Releases every finger.</summary>
        public void SetLayout(ControlLayout layout)
        {
            ReleaseAll(0);
            Layout = layout;
            Joystick.SetHome(layout.JoystickHome);
        }

        public void Process(IReadOnlyList<TouchInput> touches, double time)
        {
            for (int i = 0; i < touches.Count; i++)
            {
                TouchInput t = touches[i];
                switch (t.Phase)
                {
                    case TouchPhase.Began:
                        Begin(t, time);
                        break;
                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        Move(t);
                        break;
                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        Move(t);
                        End(t.FingerId, time);
                        break;
                }
            }
        }

        /// <summary>Reads this frame's result and clears the pressed-this-frame buttons.</summary>
        public ControlFrame Consume()
        {
            var frame = new ControlFrame
            {
                Move = Joystick.Value,
                Yaw = Look.Yaw,
                Pitch = Look.Pitch,
                Pressed = m_Pressed,
            };

            foreach (TouchButton b in Layout.Buttons)
            {
                if (b.Active && b.Visible)
                {
                    frame.Held |= b.Action;
                }
            }

            if (Joystick.Sprinting)
            {
                frame.Held |= InputButtons.Sprint;
            }

            m_Pressed = InputButtons.None;
            return frame;
        }

        /// <summary>Lifts every finger (app backgrounded, menu opened).</summary>
        public void ReleaseAll(double time)
        {
            foreach (int finger in new List<int>(m_Owners.Keys))
            {
                End(finger, time);
            }

            Joystick.CancelSprintLock();
        }

        void Begin(in TouchInput t, double time)
        {
            if (m_Owners.ContainsKey(t.FingerId))
            {
                End(t.FingerId, time); // missed an end: treat as a new touch
            }

            m_LastPositions[t.FingerId] = t.Position;
            List<TouchButton> buttons = Layout.Buttons;
            for (int i = 0; i < buttons.Count; i++)
            {
                TouchButton b = buttons[i];
                if (b.Visible && b.Bounds.Contains(t.Position))
                {
                    m_Owners[t.FingerId] = i;
                    b.Press(time);
                    m_Pressed |= b.Action;
                    return;
                }
            }

            if (!Joystick.Active && Layout.MovementZone.Contains(t.Position))
            {
                m_Owners[t.FingerId] = JoystickOwner;
                Joystick.Begin(t.Position);
                return;
            }

            if (m_LookFinger == int.MinValue)
            {
                m_Owners[t.FingerId] = LookOwner;
                m_LookFinger = t.FingerId;
            }

            // Otherwise: a second finger in the look zone while one is already looking. Ignored.
        }

        void Move(in TouchInput t)
        {
            if (!m_Owners.TryGetValue(t.FingerId, out int owner))
            {
                return;
            }

            Vector2 last = m_LastPositions.TryGetValue(t.FingerId, out Vector2 p) ? p : t.Position;
            Vector2 delta = t.Position - last;
            m_LastPositions[t.FingerId] = t.Position;

            if (owner == JoystickOwner)
            {
                Joystick.Drag(t.Position);
            }
            else if (owner == LookOwner || (owner >= 0 && owner < Layout.Buttons.Count && Layout.Buttons[owner].DragSteersCamera))
            {
                Look.ApplyDrag(delta.X, delta.Y, Aiming);
            }
        }

        void End(int finger, double time)
        {
            if (m_Owners.TryGetValue(finger, out int owner))
            {
                if (owner == JoystickOwner)
                {
                    Joystick.End();
                }
                else if (owner >= 0 && owner < Layout.Buttons.Count)
                {
                    Layout.Buttons[owner].Release(time, m_Settings.TapMaxSeconds);
                }

                m_Owners.Remove(finger);
            }

            if (m_LookFinger == finger)
            {
                m_LookFinger = int.MinValue;
            }

            m_LastPositions.Remove(finger);
        }
    }
}
