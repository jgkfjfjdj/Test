namespace Kindling.Controls
{
    /// <summary>Turns finger drags (points) into view yaw/pitch (degrees). Positive pitch looks up.</summary>
    public sealed class LookProcessor
    {
        public const float MaxPitch = 89f;

        readonly ControlSettings m_Settings;

        public LookProcessor(ControlSettings settings)
        {
            m_Settings = settings;
        }

        /// <summary>0..360, 0 = +Z (north), 90 = +X (east).</summary>
        public float Yaw { get; private set; }

        public float Pitch { get; private set; }

        public void Set(float yaw, float pitch)
        {
            Yaw = Wrap(yaw);
            Pitch = ControlSettings.Clamp(pitch, -MaxPitch, MaxPitch);
        }

        public void ApplyDrag(float dxPoints, float dyPoints, bool aiming)
        {
            float scale = aiming ? m_Settings.AimMultiplier : 1f;
            float yaw = Yaw + dxPoints / 100f * m_Settings.LookSensitivityX * scale;
            float dy = dyPoints / 100f * m_Settings.LookSensitivityY * scale;
            Set(yaw, Pitch + (m_Settings.InvertY ? -dy : dy));
        }

        static float Wrap(float degrees)
        {
            degrees %= 360f;
            return degrees < 0f ? degrees + 360f : degrees;
        }
    }
}
