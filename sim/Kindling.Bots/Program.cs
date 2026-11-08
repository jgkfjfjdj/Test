using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Kindling.Core.Diagnostics;
using Kindling.Net.Client;
using Kindling.Net.Protocol;
using Kindling.Tools;

namespace Kindling.Bots
{
    public static class Program
    {
        const string Usage = @"Kindling bots: headless clients for load tests (real UDP)

  --host <addr>       server address (default 127.0.0.1)
  --port <n>          default 27015
  --count <n>         number of bots (default 30)
  --duration <s>      seconds to run after the last bot joined (default 60)
  --ramp <s>          seconds between joins (default 0.05)
  --seed <n>          behavior seed (default 1)
  --summary-json <p>  write a JSON summary
  --verbose           log every bot's connection events
  --sim-latency <a-b> delay received packets a..b ms (needs -p:LiteNetSimulation=true build)
  --sim-loss <pct>    drop this percentage of received packets (same)";

        public static int Main(string[] argv)
        {
            Args args;
            try
            {
                args = new Args(argv);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine(Usage);
                return 2;
            }

            if (args.Has("help"))
            {
                Console.WriteLine(Usage);
                return 0;
            }

            if (args.Flag("verbose"))
            {
                Log.SetSink(new ConsoleLogSink());
            }

            string host = args.String("host", "127.0.0.1");
            int port = args.Int("port", NetProtocol.DefaultPort);
            int count = args.Int("count", 30);
            double duration = args.Double("duration", 60);
            double ramp = args.Double("ramp", 0.05);
            ulong seed = (ulong)args.Int("seed", 1);
            string summaryPath = args.String("summary-json", null);

            var bots = new List<Bot>();
            for (int i = 0; i < count; i++)
            {
                bots.Add(new Bot(i, seed, "bots", LinkSimulationArgs.Parse(args)));
            }

            using var cancel = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cancel.Cancel();
            };

            var clock = Stopwatch.StartNew();
            double last = 0;
            int joined = 0;
            double end = count * ramp + duration;
            double nextReport = 5;
            const int rate = 60;
            Console.WriteLine($"Starting {count} bots against {host}:{port}, {duration:0} s after ramp-up.");

            FixedRateLoop.Run(rate, () =>
            {
                double now = clock.Elapsed.TotalSeconds;
                double dt = now - last;
                last = now;
                while (joined < count && now >= joined * ramp)
                {
                    bots[joined].Session.Connect(host, port);
                    joined++;
                }

                foreach (Bot bot in bots)
                {
                    bot.Update(now, dt);
                }

                if (now >= nextReport)
                {
                    nextReport += 5;
                    var connected = bots.Where(b => b.Session.Status == ClientStatus.Connected).ToList();
                    double rtt = connected.Count > 0 ? connected.Average(b => Math.Max(0, b.Session.Stats.RoundTripMs)) : 0;
                    double inKBps = connected.Count > 0 ? connected.Average(b => b.Session.Stats.BytesInPerSecond) / 1024.0 : 0;
                    double remotes = connected.Count > 0 ? connected.Average(b => b.Session.Stats.RemotePlayers) : 0;
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "[bots] t={0:0}s connected={1}/{2} rtt={3:0}ms in/bot={4:0.0}KB/s sees={5:0.0} corrections={6}",
                        now, connected.Count, count, rtt, inKBps, remotes, bots.Sum(b => b.SteadyCorrections)));
                }

                return now < end;
            }, cancel.Token);

            Summary summary = Summarize(bots, host, port, clock.Elapsed.TotalSeconds);
            summary.BotProcessCpuPercent = Math.Round(Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds / clock.Elapsed.TotalSeconds * 100.0, 1);
            summary.ProcessorCount = Environment.ProcessorCount;
            string json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
            if (!string.IsNullOrEmpty(summaryPath))
            {
                File.WriteAllText(summaryPath, json);
            }

            foreach (Bot bot in bots)
            {
                bot.Dispose();
            }

            return summary.Connected == count ? 0 : 1;
        }

        static Summary Summarize(List<Bot> bots, string host, int port, double seconds)
        {
            var connected = bots.Where(b => b.Session.Status == ClientStatus.Connected).ToList();
            double steadyMinutes = connected.Sum(b => Math.Max(0, b.ConnectedSeconds - 2)) / 60.0;
            return new Summary
            {
                Target = $"{host}:{port}",
                Bots = bots.Count,
                Connected = connected.Count,
                Seconds = Math.Round(seconds, 1),
                DisconnectCodes = bots.Where(b => b.Session.Status != ClientStatus.Connected)
                    .GroupBy(b => b.Session.LastDisconnectCode.ToString())
                    .ToDictionary(g => g.Key, g => g.Count()),
                AverageRttMs = connected.Count > 0 ? Math.Round(connected.Average(b => Math.Max(0, b.Session.Stats.RoundTripMs)), 1) : 0,
                SteadyCorrections = connected.Sum(b => b.SteadyCorrections),
                CorrectionsPerBotMinute = steadyMinutes > 0 ? Math.Round(connected.Sum(b => b.SteadyCorrections) / steadyMinutes, 3) : 0,
                MaxCorrectionMetres = connected.Count > 0 ? Math.Round(connected.Max(b => b.SteadyMaxCorrection), 3) : 0,
                AverageDownKBpsPerBot = connected.Count > 0 ? Math.Round(connected.Average(b => b.Session.Stats.BytesInPerSecond) / 1024.0, 2) : 0,
                AverageUpKBpsPerBot = connected.Count > 0 ? Math.Round(connected.Average(b => b.Session.Stats.BytesOutPerSecond) / 1024.0, 2) : 0,
                AverageWireDownKBpsPerBot = connected.Count > 0 ? Math.Round(connected.Average(b => b.Transport.Counters.WireBytesReceived / Math.Max(1, b.ConnectedSeconds)) / 1024.0, 2) : 0,
                AverageRemotePlayersSeen = connected.Count > 0 ? Math.Round(connected.Average(b => b.Session.Stats.RemotePlayers), 1) : 0,
                AverageServerInputBuffer = connected.Count > 0 ? Math.Round(connected.Average(b => b.Session.Stats.ServerInputBuffer), 2) : 0,
            };
        }

        sealed class Summary
        {
            public string Target { get; set; }
            public int Bots { get; set; }
            public int Connected { get; set; }
            public double Seconds { get; set; }
            public Dictionary<string, int> DisconnectCodes { get; set; }
            public double AverageRttMs { get; set; }
            public int SteadyCorrections { get; set; }
            public double CorrectionsPerBotMinute { get; set; }
            public double MaxCorrectionMetres { get; set; }
            public double AverageDownKBpsPerBot { get; set; }
            public double AverageUpKBpsPerBot { get; set; }
            public double AverageWireDownKBpsPerBot { get; set; }
            public double AverageRemotePlayersSeen { get; set; }
            public double AverageServerInputBuffer { get; set; }
            public double BotProcessCpuPercent { get; set; }
            public int ProcessorCount { get; set; }
        }
    }
}
