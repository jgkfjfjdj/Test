using System.Collections.Generic;
using Kindling.Content;
using Kindling.Controls;
using Kindling.Net.Client;
using Kindling.Net.Protocol;
using Kindling.Simulation.Game;
using Kindling.Simulation.Items;
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
        public bool Draw(ClientSession session, TouchRouter router, ControlSettings controls, PlayerViews views, EntityViews entities, bool touchMode, bool showStats, string hostInfo, string prompt, IReadOnlyList<Toast> toasts)
        {
            m_Kit.EnsureStyles();
            DrawCrosshair();
            DrawNames(views);
            DrawEntityLabels(views, entities);
            DrawStamina(session);
            DrawSurvival(session.WorldState);
            DrawHotbar(session.WorldState, router, touchMode);
            DrawCrafting(session.WorldState, session.Content);
            if (!string.IsNullOrEmpty(prompt))
            {
                GUI.Label(new Rect(Screen.width / 2f - m_Kit.Px(160), Screen.height / 2f + m_Kit.Px(18), m_Kit.Px(320), m_Kit.Px(24)), prompt, m_Kit.CenteredLabel);
            }

            DrawToasts(toasts);
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
                "WASD move · mouse look (click to capture, Esc to release) · Shift sprint · C crouch · Space jump · click hit · 1-6 hotbar · F use/place · E interact · Tab inventory", m_Kit.SmallLabel);
            return GUI.Button(leave, "Leave", m_Kit.Button);
        }

        public struct Toast
        {
            public string Text;
            public float Until;
            public bool Bad;
        }

        void DrawSurvival(ClientWorld w)
        {
            if (!w.HasOwnerState)
            {
                return;
            }

            OwnerStateMessage o = w.Owner;
            Rect safe = Screen.safeArea;
            float x = safe.x + m_Kit.Px(10);
            float y = Screen.height - safe.yMax + m_Kit.Px(64);
            Bar(ref y, x, "HP", o.Health / 100f, new Color(0.85f, 0.25f, 0.22f));
            Bar(ref y, x, "Food", o.Hunger / 100f, new Color(0.9f, 0.6f, 0.2f));
            Bar(ref y, x, "Water", o.Thirst / 100f, new Color(0.3f, 0.6f, 0.95f));
            Bar(ref y, x, "Warmth", (o.Warmth + 100f) / 200f, o.Warmth < -20 ? new Color(0.5f, 0.75f, 1f) : o.Warmth > 20 ? new Color(1f, 0.5f, 0.3f) : new Color(0.7f, 0.85f, 0.6f));
            string effects = o.Effects == StatusEffect.None ? string.Empty : o.Effects.ToString().Replace(",", " ·");
            if (effects.Length > 0)
            {
                GUI.Label(new Rect(x, y, m_Kit.Px(260), m_Kit.Px(20)), effects, m_Kit.SmallLabel);
            }
        }

        void Bar(ref float y, float x, string label, float fraction, Color color)
        {
            float w = m_Kit.Px(110);
            float h = m_Kit.Px(9);
            GUI.Label(new Rect(x, y - m_Kit.Px(4), m_Kit.Px(48), m_Kit.Px(16)), label, m_Kit.SmallLabel);
            var back = new Rect(x + m_Kit.Px(50), y, w, h);
            m_Kit.DrawRect(back, Fill);
            m_Kit.DrawRect(new Rect(back.x, back.y, back.width * Mathf.Clamp01(fraction), back.height), color);
            y += m_Kit.Px(15);
        }

        void DrawHotbar(ClientWorld w, TouchRouter router, bool touchMode)
        {
            for (int i = 0; i < CharacterInventory.HotbarSlots; i++)
            {
                TouchButton b = router.Layout.Find((ControlButton)((int)ControlButton.Hotbar1 + i));
                if (b == null)
                {
                    continue;
                }

                Rect r = GuiKit.ToGui(b.Bounds);
                bool active = w.Owner.ActiveSlot == i;
                m_Kit.DrawRect(r, active ? Active : Fill);
                ItemStack s = w.Inventory.Hotbar[i];
                if (!s.IsEmpty)
                {
                    GUI.Label(new Rect(r.x, r.y, r.width, r.height * 0.7f), ShortName(s.Def), m_Kit.CenteredLabel);
                    if (s.Amount > 1)
                    {
                        GUI.Label(new Rect(r.x, r.y + r.height * 0.55f, r.width - m_Kit.Px(3), r.height * 0.45f), s.Amount.ToString(), m_Kit.SmallLabel);
                    }

                    if (s.Def.HasCondition)
                    {
                        m_Kit.DrawRect(new Rect(r.x + 2, r.yMax - m_Kit.Px(4), (r.width - 4) * Mathf.Clamp01(s.Condition / s.Def.MaxCondition), m_Kit.Px(3)), new Color(0.5f, 0.9f, 0.5f, 0.9f));
                    }
                }

                if (!touchMode)
                {
                    GUI.Label(new Rect(r.x + m_Kit.Px(2), r.y, m_Kit.Px(14), m_Kit.Px(14)), (i + 1).ToString(), m_Kit.SmallLabel);
                }
            }

            TouchButton bag = router.Layout.Find(ControlButton.Bag);
            if (bag != null)
            {
                Rect r = GuiKit.ToGui(bag.Bounds);
                m_Kit.DrawRect(r, Fill);
                GUI.Label(r, "BAG", m_Kit.CenteredLabel);
            }
        }

        void DrawCrafting(ClientWorld w, ContentDatabase content)
        {
            if (!w.HasOwnerState || w.Owner.Crafting.Count == 0)
            {
                return;
            }

            CraftJobInfo job = w.Owner.Crafting[0];
            RecipeDef recipe = content.ByNetId<RecipeDef>(job.RecipeId);
            if (recipe == null)
            {
                return;
            }

            float left = Mathf.Max(0f, recipe.Seconds - job.Progress);
            string more = w.Owner.Crafting.Count > 1 || job.Remaining > 1 ? $" (+{job.Remaining - 1 + w.Owner.Crafting.Count - 1} queued)" : string.Empty;
            Rect safe = Screen.safeArea;
            GUI.Label(new Rect(safe.x + m_Kit.Px(10), Screen.height - safe.yMax + m_Kit.Px(140), m_Kit.Px(320), m_Kit.Px(20)), $"Crafting {recipe.Name} {left:0}s{more}", m_Kit.SmallLabel);
        }

        void DrawToasts(IReadOnlyList<Toast> toasts)
        {
            float y = Screen.height * 0.6f;
            for (int i = toasts.Count - 1; i >= 0 && i >= toasts.Count - 4; i--)
            {
                Toast t = toasts[i];
                Color old = GUI.color;
                GUI.color = t.Bad ? new Color(1f, 0.6f, 0.55f) : Color.white;
                GUI.Label(new Rect(Screen.width / 2f - m_Kit.Px(160), y, m_Kit.Px(320), m_Kit.Px(22)), t.Text, m_Kit.CenteredLabel);
                GUI.color = old;
                y -= m_Kit.Px(22);
            }
        }

        void DrawEntityLabels(PlayerViews views, EntityViews entities)
        {
            Camera cam = views.Camera;
            if (cam == null || entities == null)
            {
                return;
            }

            foreach (EntityViews.Label label in entities.Labels)
            {
                Vector3 screen = cam.WorldToScreenPoint(label.Position);
                if (screen.z > 0f && screen.z < 25f)
                {
                    GUI.Label(new Rect(screen.x - m_Kit.Px(90), Screen.height - screen.y - m_Kit.Px(10), m_Kit.Px(180), m_Kit.Px(20)), label.Text, m_Kit.CenteredLabel);
                }
            }
        }

        public static string ShortName(ItemDef def)
        {
            string name = def.Name ?? def.Key.Value;
            string[] words = name.Split(' ');
            string last = words[words.Length - 1];
            return last.Length > 7 ? last.Substring(0, 7) : last;
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

                if (button.Id == ControlButton.Bag || (button.Id >= ControlButton.Hotbar1 && button.Id <= ControlButton.Hotbar6))
                {
                    continue; // drawn by DrawHotbar
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
                case ControlButton.Attack: return "HIT";
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
