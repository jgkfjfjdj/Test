using System;
using System.Globalization;
using Kindling.Net.Transport;

namespace Kindling.Tools
{
    public static class LinkSimulationArgs
    {
        /// <summary>Reads --sim-latency a-b and --sim-loss pct. Returns null when neither is given.</summary>
        public static LinkSimulation Parse(Args args)
        {
            if (!args.Has("sim-latency") && !args.Has("sim-loss"))
            {
                return null;
            }

            var sim = new LinkSimulation { LossPercent = args.Int("sim-loss", 0) };
            string latency = args.String("sim-latency", "0-0");
            string[] parts = latency.Split('-');
            sim.MinLatencyMs = int.Parse(parts[0], CultureInfo.InvariantCulture);
            sim.MaxLatencyMs = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : sim.MinLatencyMs;
            if (sim.MinLatencyMs < 0 || sim.MaxLatencyMs < sim.MinLatencyMs || sim.LossPercent < 0 || sim.LossPercent > 100)
            {
                throw new ArgumentException("Invalid --sim-latency or --sim-loss.");
            }

            return sim;
        }
    }
}
