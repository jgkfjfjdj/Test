using Kindling.Simulation.Movement;

namespace Kindling.Controls
{
    public enum ControlButton : byte
    {
        Jump,
        Crouch,
        Sprint,
        Attack,
        Aim,
        Interact,
    }

    public enum ButtonBehavior : byte
    {
        /// <summary>Active while held.</summary>
        Momentary,

        /// <summary>Each tap flips it.</summary>
        Toggle,

        /// <summary>Tap flips it; holding is momentary (released = off).</summary>
        TapToggleHoldMomentary,
    }

    /// <summary>An on-screen button: hit area, behavior and state.</summary>
    public sealed class TouchButton
    {
        bool m_ToggledAtPress;
        double m_PressedAt;

        public TouchButton(ControlButton id, ScreenRect bounds, ButtonBehavior behavior, InputButtons action)
        {
            Id = id;
            Bounds = bounds;
            Behavior = behavior;
            Action = action;
        }

        public ControlButton Id { get; }

        public ScreenRect Bounds { get; set; }

        public ButtonBehavior Behavior { get; set; }

        /// <summary>Simulation input this button drives (None for UI-only buttons).</summary>
        public InputButtons Action { get; }

        public bool Visible { get; set; } = true;

        /// <summary>Dragging a finger that started on this button also turns the camera (Attack).</summary>
        public bool DragSteersCamera { get; set; }

        public bool Held { get; private set; }

        public bool Toggled { get; private set; }

        /// <summary>On for this frame: held, or toggled on.</summary>
        public bool Active => Held || Toggled;

        internal void Press(double time)
        {
            Held = true;
            m_PressedAt = time;
            m_ToggledAtPress = Toggled;
            if (Behavior == ButtonBehavior.Toggle)
            {
                Toggled = !Toggled;
            }
        }

        internal void Release(double time, float tapMaxSeconds)
        {
            Held = false;
            if (Behavior == ButtonBehavior.TapToggleHoldMomentary)
            {
                bool tap = time - m_PressedAt <= tapMaxSeconds;
                Toggled = tap && !m_ToggledAtPress;
            }
        }

        public void ClearToggle() => Toggled = false;
    }
}
