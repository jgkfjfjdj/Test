using System.Collections.Generic;
using System.Numerics;
using Kindling.Simulation.Movement;

namespace Kindling.Controls
{
    /// <summary>
    /// Where the touch controls are (docs/blueprint/04-client-and-ios-controls.md §3.1): movement zone on
    /// the left, look zone everywhere else, buttons on the right, all inside the safe area. The HUD draws
    /// from the same layout the router hit-tests, so they can't disagree.
    /// </summary>
    public sealed class ControlLayout
    {
        public ControlLayout(Vector2 screenSize, ScreenRect safeArea, ScreenRect movementZone, Vector2 joystickHome, List<TouchButton> buttons)
        {
            ScreenSize = screenSize;
            SafeArea = safeArea;
            MovementZone = movementZone;
            JoystickHome = joystickHome;
            Buttons = buttons;
        }

        public Vector2 ScreenSize { get; }

        public ScreenRect SafeArea { get; }

        public ScreenRect MovementZone { get; }

        public Vector2 JoystickHome { get; }

        public List<TouchButton> Buttons { get; }

        public TouchButton Find(ControlButton id) => Buttons.Find(b => b.Id == id);

        /// <summary>The default layout for a landscape screen (sizes in points).</summary>
        public static ControlLayout CreateDefault(Vector2 screenSize, ScreenRect safeArea, ControlSettings settings)
        {
            float margin = 16f;
            float big = 96f;
            float small = 64f;
            float right = safeArea.XMax - margin;
            float bottom = safeArea.Y + margin;

            var attackCenter = new Vector2(right - big / 2, bottom + small + 24f + big / 2);
            var buttons = new List<TouchButton>
            {
                new TouchButton(ControlButton.Attack, ScreenRect.FromCenter(attackCenter, big), ButtonBehavior.Momentary, InputButtons.Attack) { DragSteersCamera = true },
                new TouchButton(ControlButton.Jump, ScreenRect.FromCenter(new Vector2(attackCenter.X - big / 2 - 12f - small / 2, bottom + small / 2), small), ButtonBehavior.Momentary, InputButtons.Jump),
                new TouchButton(ControlButton.Crouch, ScreenRect.FromCenter(new Vector2(attackCenter.X - big / 2 - 12f - small / 2 - small - 12f, bottom + small / 2 + 40f), small),
                    settings.CrouchTapToggles ? ButtonBehavior.TapToggleHoldMomentary : ButtonBehavior.Momentary, InputButtons.Crouch),
                new TouchButton(ControlButton.Aim, ScreenRect.FromCenter(new Vector2(attackCenter.X, attackCenter.Y + big / 2 + 12f + small / 2), small),
                    settings.AimTapToggles ? ButtonBehavior.TapToggleHoldMomentary : ButtonBehavior.Momentary, InputButtons.Aim),
                new TouchButton(ControlButton.Sprint, ScreenRect.FromCenter(new Vector2(safeArea.X + margin + small / 2, bottom + small / 2), small), ButtonBehavior.Toggle, InputButtons.Sprint),
                new TouchButton(ControlButton.Interact, new ScreenRect(screenSize.X / 2 - 110f, screenSize.Y * 0.32f, 220f, 52f), ButtonBehavior.Momentary, InputButtons.Interact) { Visible = false },
            };

            var movementZone = new ScreenRect(safeArea.X, safeArea.Y, safeArea.Width * 0.4f, safeArea.Height * 0.75f);
            var joystickHome = new Vector2(safeArea.X + margin + small + 24f + settings.JoystickRadius, bottom + settings.JoystickRadius + 24f);
            return new ControlLayout(screenSize, safeArea, movementZone, joystickHome, buttons);
        }

        /// <summary>Problems that would make a control unreachable (for the HUD editor and tests).</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            for (int i = 0; i < Buttons.Count; i++)
            {
                TouchButton a = Buttons[i];
                if (!a.Bounds.Inside(SafeArea))
                {
                    problems.Add($"{a.Id} is outside the safe area.");
                }

                if (a.Bounds.Width < 44f || a.Bounds.Height < 44f)
                {
                    problems.Add($"{a.Id} is smaller than 44 points.");
                }

                for (int j = i + 1; j < Buttons.Count; j++)
                {
                    if (a.Bounds.Overlaps(Buttons[j].Bounds))
                    {
                        problems.Add($"{a.Id} overlaps {Buttons[j].Id}.");
                    }
                }
            }

            return problems;
        }
    }
}
