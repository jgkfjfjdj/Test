namespace Kindling.Simulation.Movement
{
    /// <summary>Movement tuning. Server and clients must use identical values (sent in the welcome message later).</summary>
    public sealed class MovementSettings
    {
        public float HalfWidth = 0.3f;
        public float StandingHeight = 1.8f;
        public float CrouchHeight = 1.2f;
        public float StandingEyeHeight = 1.62f;
        public float CrouchEyeHeight = 1.05f;

        public float WalkSpeed = 4.0f;
        public float SprintSpeed = 6.5f;
        public float CrouchSpeed = 2.0f;
        public float GroundAcceleration = 40f;
        public float GroundDeceleration = 50f;
        public float AirAcceleration = 8f;

        public float Gravity = 18f;
        public float JumpSpeed = 6.0f;
        public float TerminalSpeed = 50f;
        public float StepHeight = 0.45f;

        public float SprintStaminaPerSecond = 12f;
        public float JumpStaminaCost = 8f;
        public float StaminaRegenPerSecond = 20f;
        public float StaminaRegenDelaySeconds = 1f;
        public float SprintResumeStamina = 25f;

        public static readonly MovementSettings Default = new MovementSettings();

        public float HeightFor(bool crouched) => crouched ? CrouchHeight : StandingHeight;

        public float EyeHeightFor(bool crouched) => crouched ? CrouchEyeHeight : StandingEyeHeight;
    }
}
