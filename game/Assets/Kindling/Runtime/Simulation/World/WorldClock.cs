using System;
using Kindling.Content;

namespace Kindling.Simulation.World
{
    /// <summary>
    /// Server-authoritative time of day. <see cref="TimeOfDay"/> runs 0 → 1 over one day: day from 0 (sunrise)
    /// to 1 − nightFraction (sunset), then night.
    /// </summary>
    public sealed class WorldClock
    {
        /// <summary>The world starts in the morning.</summary>
        public const double StartTimeOfDay = 0.1;

        public WorldClock(WorldRules rules)
        {
            DayLengthSeconds = Math.Max(60, rules.DayLengthMinutes * 60.0);
            NightFraction = Math.Max(0, Math.Min(0.9, rules.NightFraction));
        }

        /// <summary>World time in seconds since the world was created (saved; keeps running across restarts).</summary>
        public double Seconds { get; set; }

        public double DayLengthSeconds { get; }

        public double NightFraction { get; }

        public double TimeOfDay => ((Seconds / DayLengthSeconds) + StartTimeOfDay) % 1.0;

        public bool IsNight => TimeOfDay >= 1.0 - NightFraction;

        public void Advance(double seconds) => Seconds += Math.Max(0, seconds);

        /// <summary>
        /// 1 at midday, 0 at night, easing over the first and last 10% of the day.
        /// </summary>
        public float Daylight
        {
            get
            {
                double t = TimeOfDay;
                double dayEnd = 1.0 - NightFraction;
                if (t >= dayEnd)
                {
                    return 0f;
                }

                double edge = Math.Min(0.1, dayEnd / 2);
                if (t < edge)
                {
                    return (float)(t / edge);
                }

                return t > dayEnd - edge ? (float)((dayEnd - t) / edge) : 1f;
            }
        }

        /// <summary>Environmental warmth target before clothing and fires.</summary>
        public float AmbientWarmth(TemperatureRules rules) => rules.NightWarmth + (rules.DayWarmth - rules.NightWarmth) * Daylight;
    }
}
