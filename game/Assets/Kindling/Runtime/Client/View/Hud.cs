using Kindling.Controls;
using Kindling.Net.Client;
using Kindling.Net.Protocol;
using UnityEngine;

namespace Kindling.Client.View
{
    /// <summary>In-game HUD drawn with IMGUI from the same layout the touch router hit-tests.</summary>
    public sealed class Hud
    {
        static readonly Color Fill = new Color(0f, 0f, 0f, 0.28f);
        static readonly Color Outline = new Color(1f, 1f, 1f, 0.55f);
        static readonly Color Active = new Color(0.98f, 0.55f, 0.2f, 0.75f);

        readonly GuiKit m_Kit;
        float m_SmoothedDelta = 1f / 60f;

        public Hud(GuiKit kit)
        {
            m_Kit = kit;
        }

        public void Tick(float deltaTime) => m_SmoothedDelta = Mathf.Lerp(m_SmoothedDelta, deltaTime, 0.05f);

        /// <summary>Draws the HUD. Returns true if the desktop Leave button was clicked.</summary>
        public bool Draw(ClientSession session, TouchRouter router, ControlSettings controls, PlayerViews views, bool touchMode, bool showStats, string hostInfo)
        {
            m_Kit.EnsureStyles();
            DrawCrosshair();
            DrawNames(views);
            DrawStamina(session);
            if (showStats)
            {
                DrawStats(session, views, hostInfo);
            }

            if (touchMode)
            {
                DrawTouchControls(router, controls);
                return false;
            }

            Rect leave = new Rect(Screen.width - m_Kit.Px(120), m_Kit.Px(12), m_Kit.Px(108), m_Kit.Px(44));
            GUI.Label(new Rect(m_Kit.Px(12), Screen.height - m_Kit.Px(28), Screen.width, m_Kit.Px(24)),
                "WASD move · mouse look (click to capture, Esc to release) · Shift sprint · C crouch · Space jump", m_Kit.SmallLabel);
            return GUI.Button(leave, "Leave", m_Kit.Button);
        }

        void DrawCrosshair()
        {
            var c = new Vector2(Screen.width / 2f, Screen.height / 2f);
            float len = m_Kit.Px(7);
            float w = Mathf.Max(1f, m_Kit.Px(1.5f));
            var color = new Color(1f, 1f, 1f, 0.85f);
            m_Kit.DrawRect(new Rect(c.x - len, c.y - w / 2, len * 2, w), color);
            m_Kit.DrawRect(new Rect(c.x - w / 2, c.y - len, w, len * 2), color);
        }

        void DrawNames(PlayerViews views)
        {
            Camera cam = views.Camera;
            if (cam == null)
            {
                return;
            }

            foreach (RemotePlayer remote in views.Remotes)
            {
                Vector3 screen = cam.WorldToScreenPoint(PlayerViews.HeadOf(remote));
                if (screen.z <= 0f || screen.z > 60f)
                {
                    continue;
                }

                string label = (remote.Flags & EntityFlags.Away) != 0 ? $"{remote.Name} (away)" : remote.Name;
                var rect = new Rect(screen.x - m_Kit.Px(80), Screen.height - screen.y - m_Kit.Px(10), m_Kit.Px(160), m_Kit.Px(20));
                GUI.Label(rect, label, m_Kit.CenteredLabel);
            }
        }

        void DrawStamina(ClientSession session)
        {
            float stamina = session.PredictedState.Stamina / 100f;
            float width = m_Kit.Px(160);
            var back = new Rect(Screen.width / 2f - width / 2f, Screen.height - m_Kit.Px(22), width, m_Kit.Px(6));
            m_Kit.DrawRect(back, Fill);
            m_Kit.DrawRect(new Rect(back.x, back.y, back.width * Mathf.Clamp01(stamina), back.height),
                session.PredictedState.Exhausted ? new Color(0.85f, 0.3f, 0.25f, 0.9f) : new Color(0.95f, 0.85f, 0.4f, 0.9f));
        }

        void DrawStats(ClientSession session, PlayerViews views, string hostInfo)
        {
            ClientStats s = session.Stats;
            Rect safe = Screen.safeArea;
            float x = safe.x + m_Kit.Px(10);
            float y = Screen.height - safe.yMax + m_Kit.Px(8);
            string text =
                $"{1f / Mathf.Max(0.0001f, m_SmoothedDelta):0} fps · ping {(s.RoundTripMs >= 0 ? s.RoundTripMs + " ms" : "—")} · players {views.Remotes.Count + 1}\n" +
                $"tick {s.ServerTick} · corrections {s.Corrections} (last {s.LastCorrection:0.00} m) · interp {s.InterpolationDelayMs:0} ms\n" +
                $"↓ {s.BytesInPerSecond / 1024.0:0.0} KB/s · ↑ {s.BytesOutPerSecond / 1024.0:0.0} KB/s · input buffer {s.ServerInputBuffer}" +
                (string.IsNullOrEmpty(hostInfo) ? string.Empty : $"\n{hostInfo}");
            GUI.Label(new Rect(x, y, m_Kit.Px(520), m_Kit.Px(80)), text, m_Kit.SmallLabel);
        }

        void DrawTouchControls(TouchRouter router, ControlSettings controls)
        {
            FloatingJoystick stick = router.Joystick;
            float radius = m_Kit.Px(controls.JoystickRadius);
            Vector2 origin = GuiKit.ToGui(stick.Origin);
            m_Kit.DrawCircle(origin, radius, Fill);
            m_Kit.DrawCircle(origin, radius, stick.SprintLocked ? Active : Outline, ring: true);
            m_Kit.DrawCircle(GuiKit.ToGui(stick.Active ? stick.Knob : stick.Origin), radius * 0.45f, stick.Sprinting ? Active : new Color(1f, 1f, 1f, 0.6f));
            if (stick.SprintLocked)
            {
                GUI.Label(new Rect(origin.x - m_Kit.Px(60), origin.y - radius - m_Kit.Px(26), m_Kit.Px(120), m_Kit.Px(20)), "AUTO-RUN", m_Kit.CenteredLabel);
            }

            foreach (TouchButton button in router.Layout.Buttons)
            {
                if (!button.Visible)
                {
                    continue;
                }

                Rect r = GuiKit.ToGui(button.Bounds);
                Vector2 c = r.center;
                float br = Mathf.Min(r.width, r.height) / 2f;
                m_Kit.DrawCircle(c, br, button.Active ? Active : Fill);
                m_Kit.DrawCircle(c, br, Outline, ring: true);
                GUI.Label(r, LabelFor(button.Id), m_Kit.CenteredLabel);
            }
        }

        static string LabelFor(ControlButton id)
        {
            switch (id)
            {
                case ControlButton.Attack: return "FIRE";
                case ControlButton.Jump: return "JUMP";
                case ControlButton.Crouch: return "CROUCH";
                case ControlButton.Aim: return "AIM";
                case ControlButton.Sprint: return "SPRINT";
                case ControlButton.Interact: return "USE";
                case ControlButton.Menu: return "II";
                default: return id.ToString();
            }
        }
    }
}
