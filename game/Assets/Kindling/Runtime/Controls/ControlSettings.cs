namespace Kindling.Controls
{
    /// <summary>Player-adjustable touch control settings (Settings → Controls).</summary>
    public sealed class ControlSettings
    {
        /// <summary>Degrees of yaw per 100 points dragged.</summary>
        public float LookSensitivityX = 20f;

        /// <summary>Degrees of pitch per 100 points dragged.</summary>
        public float LookSensitivityY = 15f;

        /// <summary>Look sensitivity multiplier while aiming down sights.</summary>
        public float AimMultiplier = 0.5f;

        public bool InvertY;

        /// <summary>Joystick appears where the thumb lands (true) or stays at its layout position (false).</summary>
        public bool FloatingJoystick = true;

        /// <summary>Joystick radius in points: dragging this far is full speed.</summary>
        public float JoystickRadius = 60f;

        /// <summary>Fraction of the radius that counts as no movement.</summary>
        public float DeadZone = 0.1f;

        /// <summary>Forward deflection (0..1) past which the joystick sprints.</summary>
        public float SprintThreshold = 0.95f;

        /// <summary>Sliding this far (points) past the top of the stick locks sprint (auto-run) until you pull back.</summary>
        public bool SprintLock = true;

        public float SprintLockDistance = 40f;

        /// <summary>Crouch: tap toggles, hold is momentary. Off: momentary only.</summary>
        public bool CrouchTapToggles = true;

        /// <summary>Aim: tap toggles, hold is momentary. Off: momentary only.</summary>
        public bool AimTapToggles = true;

        /// <summary>Touches shorter than this (seconds) are taps.</summary>
        public float TapMaxSeconds = 0.25f;

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        public ControlSettings Clone() => (ControlSettings)MemberwiseClone();
    }
}
