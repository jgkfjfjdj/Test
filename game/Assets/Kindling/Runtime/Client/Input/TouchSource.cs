using System;
using System.Collections.Generic;
using Kindling.Controls;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ControlsTouchPhase = Kindling.Controls.TouchPhase;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using InputSystemTouchPhase = UnityEngine.InputSystem.TouchPhase;
using LegacyInput = UnityEngine.Input;
using LegacyTouchPhase = UnityEngine.TouchPhase;
using Vector2 = System.Numerics.Vector2;

namespace Kindling.Client.Input
{
    /// <summary>
    /// Reads all fingers each frame and converts them to <see cref="TouchInput"/> in screen points.
    /// Uses the Input System's EnhancedTouch; falls back to the legacy Input class if the Input System
    /// has no touchscreen (e.g. its backend is disabled in Player Settings).
    /// </summary>
    public sealed class TouchSource : IDisposable
    {
        readonly List<TouchInput> m_Touches = new List<TouchInput>(10);
        bool m_EnhancedEnabled;
        bool m_LegacyFailed;

        public TouchSource()
        {
            EnhancedTouchSupport.Enable();
            m_EnhancedEnabled = true;
        }

        /// <summary>True if this device has a touchscreen (otherwise use keyboard and mouse).</summary>
        public bool HasTouchscreen
        {
            get
            {
                if (Touchscreen.current != null)
                {
                    return true;
                }

                if (m_LegacyFailed)
                {
                    return false;
                }

                try
                {
                    return LegacyInput.touchSupported;
                }
                catch (InvalidOperationException)
                {
                    m_LegacyFailed = true; // legacy input backend disabled
                    return false;
                }
            }
        }

        /// <param name="pointsPerPixel">Converts Unity's pixel positions to layout points.</param>
        public IReadOnlyList<TouchInput> Read(float pointsPerPixel)
        {
            m_Touches.Clear();
            if (Touchscreen.current != null)
            {
                foreach (ETouch touch in ETouch.activeTouches)
                {
                    if (TryMap(touch.phase, out ControlsTouchPhase phase))
                    {
                        UnityEngine.Vector2 p = touch.screenPosition;
                        m_Touches.Add(new TouchInput(touch.finger.index, new Vector2(p.x * pointsPerPixel, p.y * pointsPerPixel), phase));
                    }
                }

                return m_Touches;
            }

            if (!m_LegacyFailed)
            {
                try
                {
                    for (int i = 0; i < LegacyInput.touchCount; i++)
                    {
                        UnityEngine.Touch touch = LegacyInput.GetTouch(i);
                        UnityEngine.Vector2 p = touch.position;
                        m_Touches.Add(new TouchInput(touch.fingerId, new Vector2(p.x * pointsPerPixel, p.y * pointsPerPixel), Map(touch.phase)));
                    }
                }
                catch (InvalidOperationException)
                {
                    m_LegacyFailed = true;
                }
            }

            return m_Touches;
        }

        static bool TryMap(InputSystemTouchPhase phase, out ControlsTouchPhase result)
        {
            switch (phase)
            {
                case InputSystemTouchPhase.Began:
                    result = ControlsTouchPhase.Began;
                    return true;
                case InputSystemTouchPhase.Moved:
                    result = ControlsTouchPhase.Moved;
                    return true;
                case InputSystemTouchPhase.Stationary:
                    result = ControlsTouchPhase.Stationary;
                    return true;
                case InputSystemTouchPhase.Ended:
                    result = ControlsTouchPhase.Ended;
                    return true;
                case InputSystemTouchPhase.Canceled:
                    result = ControlsTouchPhase.Canceled;
                    return true;
                default:
                    result = ControlsTouchPhase.Stationary;
                    return false;
            }
        }

        static ControlsTouchPhase Map(LegacyTouchPhase phase)
        {
            switch (phase)
            {
                case LegacyTouchPhase.Began: return ControlsTouchPhase.Began;
                case LegacyTouchPhase.Moved: return ControlsTouchPhase.Moved;
                case LegacyTouchPhase.Ended: return ControlsTouchPhase.Ended;
                case LegacyTouchPhase.Canceled: return ControlsTouchPhase.Canceled;
                default: return ControlsTouchPhase.Stationary;
            }
        }

        public void Dispose()
        {
            if (m_EnhancedEnabled)
            {
                EnhancedTouchSupport.Disable();
                m_EnhancedEnabled = false;
            }
        }
    }
}
