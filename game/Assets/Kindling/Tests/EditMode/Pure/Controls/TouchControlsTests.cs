using System.Collections.Generic;
using System.Numerics;
using Kindling.Controls;
using Kindling.Simulation.Movement;
using NUnit.Framework;

namespace Kindling.Tests.Pure.Controls
{
    public class TouchControlsTests
    {
        // iPhone-like landscape screen in points with a notch inset on the left and right.
        static readonly Vector2 Screen = new Vector2(844, 390);
        static readonly ScreenRect Safe = new ScreenRect(47, 21, 750, 369);

        ControlSettings m_Settings;
        TouchRouter m_Router;
        readonly List<TouchInput> m_Touches = new List<TouchInput>();
        double m_Time;

        [SetUp]
        public void SetUp()
        {
            m_Settings = new ControlSettings();
            m_Router = new TouchRouter(ControlLayout.CreateDefault(Screen, Safe, m_Settings), m_Settings);
            m_Time = 0;
        }

        ControlFrame Frame(params TouchInput[] touches)
        {
            m_Touches.Clear();
            m_Touches.AddRange(touches);
            m_Router.Process(m_Touches, m_Time);
            m_Time += 1.0 / 60.0;
            return m_Router.Consume();
        }

        static TouchInput T(int finger, float x, float y, TouchPhase phase) => new TouchInput(finger, new Vector2(x, y), phase);

        Vector2 Center(ControlButton id) => m_Router.Layout.Find(id).Bounds.Center;

        [Test]
        public void DefaultLayoutIsValidOnPhoneAndTabletScreens()
        {
            Assert.That(m_Router.Layout.Validate(), Is.Empty);
            var ipad = ControlLayout.CreateDefault(new Vector2(1194, 834), new ScreenRect(0, 20, 1194, 814), m_Settings);
            Assert.That(ipad.Validate(), Is.Empty);
            var broken = new ControlLayout(Screen, Safe, ipad.MovementZone, ipad.JoystickHome, ipad.Buttons);
            Assert.That(broken.Validate(), Is.Not.Empty, "tablet positions don't fit a phone's safe area");
        }

        [Test]
        public void JoystickHasADeadZoneAndIsAnalog()
        {
            Frame(T(1, 200, 120, TouchPhase.Began));
            Assert.That(Frame(T(1, 203, 123, TouchPhase.Moved)).Move, Is.EqualTo(Vector2.Zero), "inside the 10% dead zone");

            ControlFrame half = Frame(T(1, 200, 150, TouchPhase.Moved)); // 30 of 60 points forward
            Assert.That(half.Move.Y, Is.EqualTo((0.5f - 0.1f) / 0.9f).Within(1e-4f));
            Assert.That(half.Move.X, Is.EqualTo(0f));
            Assert.That(half.Held & InputButtons.Sprint, Is.EqualTo(InputButtons.None));

            ControlFrame far = Frame(T(1, 400, 120, TouchPhase.Moved)); // far right: clamped
            Assert.That(far.Move.Length(), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(m_Router.Joystick.Knob, Is.EqualTo(new Vector2(260, 120)));

            Assert.That(Frame(T(1, 400, 120, TouchPhase.Ended)).Move, Is.EqualTo(Vector2.Zero));
        }

        [Test]
        public void PushingFullyForwardSprintsButDiagonalsDont()
        {
            Frame(T(1, 200, 120, TouchPhase.Began));
            Assert.That(Frame(T(1, 200, 179, TouchPhase.Moved)).Held & InputButtons.Sprint, Is.EqualTo(InputButtons.Sprint));
            Assert.That(Frame(T(1, 250, 170, TouchPhase.Moved)).Held & InputButtons.Sprint, Is.EqualTo(InputButtons.None));
        }

        [Test]
        public void SlidingAboveTheStickLocksSprintUntilPulledBack()
        {
            Frame(T(1, 200, 100, TouchPhase.Began));
            Frame(T(1, 200, 210, TouchPhase.Moved)); // 110 points up: past radius 60 + lock 40
            Assert.That(m_Router.Joystick.SprintLocked, Is.True);

            ControlFrame autoRun = Frame(T(1, 200, 210, TouchPhase.Ended));
            Assert.That(autoRun.Move, Is.EqualTo(new Vector2(0, 1)), "keeps running with the thumb lifted");
            Assert.That(autoRun.Held & InputButtons.Sprint, Is.EqualTo(InputButtons.Sprint));

            Frame(T(2, 200, 100, TouchPhase.Began));
            ControlFrame back = Frame(T(2, 200, 60, TouchPhase.Moved));
            Assert.That(m_Router.Joystick.SprintLocked, Is.False);
            Assert.That(back.Move.Y, Is.LessThan(0f));
        }

        [Test]
        public void LookDragUsesSensitivityAimMultiplierAndInvert()
        {
            Frame(T(5, 500, 300, TouchPhase.Began));
            ControlFrame f = Frame(T(5, 600, 320, TouchPhase.Moved));
            Assert.That(f.Yaw, Is.EqualTo(20f).Within(1e-4f), "100 points = 20 degrees");
            Assert.That(f.Pitch, Is.EqualTo(3f).Within(1e-4f), "20 points up = 3 degrees up");

            Frame(T(5, 600, 320, TouchPhase.Ended));
            Vector2 aim = Center(ControlButton.Aim);
            Frame(T(6, aim.X, aim.Y, TouchPhase.Began));
            Frame(T(6, aim.X, aim.Y, TouchPhase.Ended)); // tap: aim toggled on
            Assert.That(m_Router.Aiming, Is.True);
            Frame(T(5, 500, 300, TouchPhase.Began));
            Assert.That(Frame(T(5, 600, 300, TouchPhase.Moved)).Yaw, Is.EqualTo(30f).Within(1e-4f), "aiming halves sensitivity");

            m_Settings.InvertY = true;
            Assert.That(Frame(T(5, 600, 400, TouchPhase.Moved)).Pitch, Is.EqualTo(3f - 7.5f).Within(1e-4f));
            Assert.That(Frame(T(5, 600, -5000, TouchPhase.Moved)).Pitch, Is.EqualTo(LookProcessor.MaxPitch));
        }

        [Test]
        public void FingersStayWithTheControlTheyStartedOn()
        {
            Frame(T(1, 200, 120, TouchPhase.Began));
            ControlFrame f = Frame(T(1, 700, 300, TouchPhase.Moved)); // drags into the look zone
            Assert.That(f.Move.Length(), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(f.Yaw, Is.EqualTo(0f), "joystick finger never turns the camera");

            Vector2 jump = Center(ControlButton.Jump);
            Frame(T(2, jump.X, jump.Y, TouchPhase.Began));
            Assert.That(Frame(T(2, jump.X - 300, jump.Y + 100, TouchPhase.Moved)).Yaw, Is.EqualTo(0f), "jump finger never turns the camera");
        }

        [Test]
        public void MoveLookAndButtonsWorkAtTheSameTime()
        {
            Vector2 attack = Center(ControlButton.Attack);
            Frame(T(1, 200, 120, TouchPhase.Began), T(2, 500, 250, TouchPhase.Began), T(3, attack.X, attack.Y, TouchPhase.Began));
            ControlFrame f = Frame(T(1, 200, 150, TouchPhase.Moved), T(2, 550, 250, TouchPhase.Moved), T(3, attack.X, attack.Y, TouchPhase.Stationary));
            Assert.That(f.Move.Y, Is.GreaterThan(0.3f));
            Assert.That(f.Yaw, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(f.Held & InputButtons.Attack, Is.EqualTo(InputButtons.Attack));
            Assert.That(m_Router.ActiveFingers, Is.EqualTo(3));

            // A second finger in the look zone is ignored while the first one looks.
            Frame(T(4, 600, 200, TouchPhase.Began));
            Assert.That(Frame(T(4, 700, 200, TouchPhase.Moved)).Yaw, Is.EqualTo(10f).Within(1e-4f));
        }

        [Test]
        public void AttackDragAlsoAims()
        {
            Vector2 attack = Center(ControlButton.Attack);
            ControlFrame down = Frame(T(3, attack.X, attack.Y, TouchPhase.Began));
            Assert.That(down.Pressed & InputButtons.Attack, Is.EqualTo(InputButtons.Attack));
            ControlFrame drag = Frame(T(3, attack.X - 50, attack.Y, TouchPhase.Moved));
            Assert.That(drag.Yaw, Is.EqualTo(350f).Within(1e-3f));
            Assert.That(drag.Pressed, Is.EqualTo(InputButtons.None), "pressed only on the first frame");
            Assert.That(drag.Held & InputButtons.Attack, Is.EqualTo(InputButtons.Attack));
        }

        [Test]
        public void CrouchTapTogglesAndHoldIsMomentary()
        {
            Vector2 crouch = Center(ControlButton.Crouch);
            Frame(T(1, crouch.X, crouch.Y, TouchPhase.Began));
            Assert.That(Frame(T(1, crouch.X, crouch.Y, TouchPhase.Ended)).Held & InputButtons.Crouch, Is.EqualTo(InputButtons.Crouch), "tap: stays crouched");
            Frame(T(1, crouch.X, crouch.Y, TouchPhase.Began));
            Assert.That(Frame(T(1, crouch.X, crouch.Y, TouchPhase.Ended)).Held & InputButtons.Crouch, Is.EqualTo(InputButtons.None), "tap again: stands");

            Frame(T(1, crouch.X, crouch.Y, TouchPhase.Began));
            for (int i = 0; i < 30; i++)
            {
                Assert.That(Frame(T(1, crouch.X, crouch.Y, TouchPhase.Stationary)).Held & InputButtons.Crouch, Is.EqualTo(InputButtons.Crouch));
            }

            Assert.That(Frame(T(1, crouch.X, crouch.Y, TouchPhase.Ended)).Held & InputButtons.Crouch, Is.EqualTo(InputButtons.None), "held for 0.5 s: momentary");
        }

        [Test]
        public void JumpTapIsReportedEvenIfReleasedWithinOneFrame()
        {
            Vector2 jump = Center(ControlButton.Jump);
            ControlFrame f = Frame(T(1, jump.X, jump.Y, TouchPhase.Began), T(1, jump.X, jump.Y, TouchPhase.Ended));
            Assert.That(f.Pressed & InputButtons.Jump, Is.EqualTo(InputButtons.Jump));
            Assert.That(f.Held & InputButtons.Jump, Is.EqualTo(InputButtons.None));
        }

        [Test]
        public void HiddenButtonsAndCanceledTouchesAreHandled()
        {
            Vector2 interact = Center(ControlButton.Interact);
            Frame(T(1, interact.X, interact.Y, TouchPhase.Began));
            Assert.That(Frame(T(1, interact.X + 100, interact.Y, TouchPhase.Moved)).Yaw, Is.EqualTo(20f).Within(1e-4f), "hidden interact button: the touch looks instead");
            Frame(T(1, interact.X + 100, interact.Y, TouchPhase.Canceled));
            Assert.That(m_Router.ActiveFingers, Is.EqualTo(0));

            Frame(T(2, 200, 120, TouchPhase.Began), T(2, 200, 170, TouchPhase.Moved));
            m_Router.ReleaseAll(m_Time);
            Assert.That(m_Router.Consume().Move, Is.EqualTo(Vector2.Zero));
        }

        [Test]
        public void MenuButtonCountsPressesWithoutDrivingTheSimulation()
        {
            Vector2 menu = Center(ControlButton.Menu);
            TouchButton button = m_Router.Layout.Find(ControlButton.Menu);
            ControlFrame f = Frame(T(1, menu.X, menu.Y, TouchPhase.Began));
            Assert.That(button.PressCount, Is.EqualTo(1));
            Assert.That(f.Pressed, Is.EqualTo(InputButtons.None));
            Assert.That(f.Held, Is.EqualTo(InputButtons.None));
        }

        [Test]
        public void FixedJoystickStaysAtItsHome()
        {
            m_Settings.FloatingJoystick = false;
            Frame(T(1, 150, 200, TouchPhase.Began));
            Assert.That(m_Router.Joystick.Origin, Is.EqualTo(m_Router.Layout.JoystickHome));
        }
    }
}
