using Kindling.Simulation.Game;
using Kindling.Simulation.Movement;

namespace Kindling.Net.Server
{
    /// <summary>What a character can do in each life state. Shared by the server and client prediction.</summary>
    public static class LifeRules
    {
        public static PlayerInput Restrict(PlayerInput input, LifeState life)
        {
            if (life == LifeState.Alive)
            {
                return input;
            }

            input.Move = default;
            input.Buttons = InputButtons.None;
            return input;
        }
    }
}
