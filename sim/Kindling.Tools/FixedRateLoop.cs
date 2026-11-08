using System;
using System.Diagnostics;
using System.Threading;

namespace Kindling.Tools
{
    /// <summary>Calls a step at a fixed rate on the current thread (sleeps, then spins for the last millisecond).</summary>
    public static class FixedRateLoop
    {
        public static void Run(int rate, Func<bool> step, CancellationToken cancel)
        {
            double interval = 1.0 / rate;
            var clock = Stopwatch.StartNew();
            double next = 0;
            while (!cancel.IsCancellationRequested)
            {
                double now = clock.Elapsed.TotalSeconds;
                double wait = next - now;
                if (wait > 0.002)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(wait - 0.001));
                    continue;
                }

                if (wait > 0)
                {
                    Thread.SpinWait(50);
                    continue;
                }

                if (!step())
                {
                    return;
                }

                next += interval;
                if (now - next > 0.25)
                {
                    next = now; // fell far behind (paused, overloaded): don't try to catch up
                }
            }
        }
    }
}
