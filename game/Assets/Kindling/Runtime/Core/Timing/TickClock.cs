using System;

namespace Kindling.Core.Timing
{
    /// <summary>
    /// Converts between simulation ticks and seconds for a fixed tick rate.
    /// Tick 0 starts at time 0. The server simulates at <see cref="DefaultTickRate"/> Hz
    /// (docs/blueprint/05-multiplayer-networking.md §3).
    /// </summary>
    public readonly struct TickClock : IEquatable<TickClock>
    {
        public const int DefaultTickRate = 30;
        public const int MinTickRate = 1;
        public const int MaxTickRate = 128;

        public static readonly TickClock Default = new TickClock(DefaultTickRate);

        /// <summary>Ticks per second.</summary>
        public int TickRate { get; }

        /// <summary>Duration of one tick in seconds.</summary>
        public double TickDuration => 1.0 / TickRate;

        public TickClock(int tickRate)
        {
            if (tickRate < MinTickRate || tickRate > MaxTickRate)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tickRate), tickRate, $"Tick rate must be between {MinTickRate} and {MaxTickRate}.");
            }

            TickRate = tickRate;
        }

        /// <summary>Start time (seconds) of the given tick.</summary>
        public double TimeOfTick(long tick)
        {
            if (tick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick), tick, "Tick must not be negative.");
            }

            return tick / (double)TickRate;
        }

        /// <summary>The tick that is running at the given time (floor). Negative times map to tick 0.</summary>
        public long TickAtTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                throw new ArgumentException("Time must be a finite number.", nameof(seconds));
            }

            if (seconds <= 0)
            {
                return 0;
            }

            // A small epsilon keeps exact tick boundaries (e.g. 1/30 * 30) from rounding down a tick.
            return (long)Math.Floor(seconds * TickRate + 1e-9);
        }

        /// <summary>Number of whole ticks needed to cover a duration (ceiling). Used for cooldowns and timers.</summary>
        public long TicksForDuration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            {
                throw new ArgumentException("Duration must be a finite, non-negative number.", nameof(seconds));
            }

            return (long)Math.Ceiling(seconds * TickRate - 1e-9);
        }

        public bool Equals(TickClock other) => TickRate == other.TickRate;

        public override bool Equals(object obj) => obj is TickClock other && Equals(other);

        public override int GetHashCode() => TickRate;

        public override string ToString() => $"TickClock({TickRate} Hz)";
    }
}
