using Kindling.Controls;
using Kindling.Simulation.Movement;
using UnityEngine;
using UnityEngine.InputSystem;
using Vector2 = System.Numerics.Vector2;

namespace Kindling.Client.Input
{
    /// <summary>
    /// Keyboard and mouse for the editor and desktop test builds: WASD, mouse look (cursor locked
    /// after a click, Esc releases it), Shift sprint, C/Ctrl crouch, Space jump, left click attack.
    /// </summary>
    public sealed class DesktopControls
    {
        const float MousePointsPerPixel = 0.25f;

        public bool Available => Keyboard.current != null;

        public bool CursorLocked => Cursor.lockState == CursorLockMode.Locked;

        /// <summary>Esc pressed this frame while the cursor was already free (i.e. "open the menu").</summary>
        public bool MenuRequested { get; private set; }

        public ControlFrame Read(LookProcessor look, bool aiming)
        {
            var frame = new ControlFrame();
            MenuRequested = false;
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard == null)
            {
                frame.Yaw = look.Yaw;
                frame.Pitch = look.Pitch;
                return frame;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (CursorLocked)
                {
                    SetCursorLocked(false);
                }
                else
                {
                    MenuRequested = true;
                }
            }

            if (mouse != null && !CursorLocked && mouse.leftButton.wasPressedThisFrame && !IsOverTopRightCorner(mouse))
            {
                SetCursorLocked(true);
            }

            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            var move = new Vector2(x, y);
            if (move.LengthSquared() > 1f)
            {
                move = Vector2.Normalize(move);
            }

            frame.Move = move;
            if (keyboard.leftShiftKey.isPressed)
            {
                frame.Held |= InputButtons.Sprint;
            }

            if (keyboard.cKey.isPressed || keyboard.leftCtrlKey.isPressed)
            {
                frame.Held |= InputButtons.Crouch;
            }

            if (keyboard.spaceKey.isPressed)
            {
                frame.Held |= InputButtons.Jump;
            }

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                frame.Pressed |= InputButtons.Jump;
            }

            if (mouse != null && CursorLocked)
            {
                UnityEngine.Vector2 delta = mouse.delta.ReadValue();
                look.ApplyDrag(delta.x * MousePointsPerPixel, delta.y * MousePointsPerPixel, aiming);
                if (mouse.leftButton.isPressed)
                {
                    frame.Held |= InputButtons.Attack;
                }

                if (mouse.rightButton.isPressed)
                {
                    frame.Held |= InputButtons.Aim;
                }
            }

            frame.Yaw = look.Yaw;
            frame.Pitch = look.Pitch;
            return frame;
        }

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        static bool IsOverTopRightCorner(Mouse mouse)
        {
            // The HUD's Leave button (120 × 56 points from the top-right corner), with a margin.
            float scale = View.GuiKit.PixelsPerPoint;
            UnityEngine.Vector2 p = mouse.position.ReadValue();
            return p.x > Screen.width - 140f * scale && p.y > Screen.height - 70f * scale;
        }
    }
}
