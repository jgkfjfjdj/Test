using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using Kindling.Content;
using Kindling.Core.Diagnostics;
using Kindling.Net.Protocol;
using Kindling.Net.Server;
using Kindling.Net.Transport;
using Kindling.Server;
using Kindling.Simulation.Game;
using Kindling.Simulation.Persistence;
using Kindling.Simulation.World;
using Kindling.Tools;

namespace Kindling.DevServer
{
    public static class Program
    {
        const string Usage = @"Kindling dev server (M1 movement slice)

  --port <n>            UDP port (default 27015)
  --max-players <n>     default 100
  --tick-rate <n>       default 30
  --seed <n>            greybox map seed (default 1)
  --duration <s>        stop after this many seconds (default: run until Ctrl+C)
  --stats-interval <s>  print stats every N seconds (default 5)
  --stats-json <path>   write a JSON stats summary on exit
  --no-dev-auth         refuse clients without a join ticket (refuses everyone until join tickets land)
  --world <path>        SQLite world save (default ./world/world.db); created if missing
  --wipe                delete the saved world first
  --no-save             don't load or save the world (throwaway)
  --save-interval <s>   seconds between incremental saves (default 60)
  --backup-dir <dir>    hourly backups (default next to the save); --backups-keep <n> (default 24)
  --gather <x>          gather multiplier (default 1)
  --sleepers <mode>     vulnerable | protectedInClaim | vanish
  --day-minutes <m>     length of a day (default 60)
  --sim-latency <a-b>   delay received packets a..b ms (needs -p:LiteNetSimulation=true build)
  --sim-loss <pct>      drop this percentage of received packets (same)";

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

            Log.SetSink(new ConsoleLogSink());
            var config = new ServerConfig
            {
                Port = args.Int("port", NetProtocol.DefaultPort),
                MaxPlayers = args.Int("max-players", 100),
                TickRate = args.Int("tick-rate", NetProtocol.TickRate),
                MapSeed = args.Int("seed", 1),
                AllowDevAuth = !args.Flag("no-dev-auth"),
            };
            double duration = args.Double("duration", 0);
            double statsInterval = args.Double("stats-interval", 5);
            string statsJson = args.String("stats-json", null);

            config.SaveIntervalSeconds = args.Double("save-interval", 60);
            ContentDatabase content = ContentDatabase.LoadDefault();
            GameConfig game = GameConfig.FromRules(content.Rules);
            game.GatherMultiplier = (float)args.Double("gather", game.GatherMultiplier);
            game.DayLengthMinutes = (float)args.Double("day-minutes", game.DayLengthMinutes);
            if (args.Has("sleepers"))
            {
                game.SleeperMode = ContentEnums.Parse<SleeperMode>(args.String("sleepers", null), "sleeper mode");
            }

            WorldMap map = WorldMap.Generate(config.MapSeed, content);
            GameWorld world;
            SqliteWorldStore store = null;
            WorldPersistence persistence = null;
            string worldPath = Path.GetFullPath(args.String("world", Path.Combine("world", "world.db")));
            string backupDir = args.String("backup-dir", Path.Combine(Path.GetDirectoryName(worldPath), "backups"));
            int backupsKeep = args.Int("backups-keep", 24);
            if (args.Flag("no-save"))
            {
                world = ServerHost.CreateWorld(content, config, game);
                Log.Info("Server", "World saving is off (--no-save).");
            }
            else
            {
                if (args.Flag("wipe"))
                {
                    foreach (string f in new[] { worldPath, worldPath + "-wal", worldPath + "-shm" })
                    {
                        File.Delete(f);
                    }

                    Log.Info("Server", $"Wiped {worldPath}.");
                }

                store = new SqliteWorldStore(worldPath) { OnError = e => Log.Error("Save", "World save failed.", e) };
                var warnings = new System.Collections.Generic.List<string>();
                world = WorldPersistence.LoadOrCreate(content, map, game, (ulong)config.MapSeed * 7919UL + 1, store, warnings, out bool loaded);
                foreach (string w in warnings)
                {
                    Log.Warning("Save", w);
                }

                persistence = new WorldPersistence(world, store);
                if (!loaded)
                {
                    persistence.Checkpoint();
                }

                Log.Info("Server", loaded
                    ? $"Loaded {worldPath}: {world.Characters.Count} characters, {world.Entities.Count} entities, world time {world.Now / 60:0} min."
                    : $"New world at {worldPath}.");
            }

            using var transport = new LiteNetServerTransport(config.MaxPlayers + 16, simulation: LinkSimulationArgs.Parse(args));
            var server = new ServerHost(transport, config, world, persistence);
            if (!server.Start())
            {
                return 1;
            }

            double nextBackup = store != null ? 3600 : double.MaxValue;

            using var cancel = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cancel.Cancel();
            };

            var summary = new Summary { TickRate = config.TickRate, ProcessorCount = Environment.ProcessorCount };
            var wall = Stopwatch.StartNew();
            double nextStats = statsInterval;
            long lastWireOut = 0, lastWireIn = 0, lastSnapshotBytes = 0;
            double lastStatsTime = 0;
            var process = Process.GetCurrentProcess();
            TimeSpan lastCpu = process.TotalProcessorTime;

            FixedRateLoop.Run(config.TickRate, () =>
            {
                server.Tick();
                double now = wall.Elapsed.TotalSeconds;
                if (now >= nextBackup)
                {
                    nextBackup = now + 3600;
                    try
                    {
                        string file = store.BackupWithRotation(backupDir, backupsKeep, DateTime.UtcNow);
                        Log.Info("Save", $"Backup written: {file}");
                    }
                    catch (Exception e)
                    {
                        Log.Error("Save", "Backup failed.", e);
                    }
                }

                if (now >= nextStats)
                {
                    ServerStats s = server.Stats;
                    double elapsed = now - lastStatsTime;
                    process.Refresh();
                    TimeSpan cpu = process.TotalProcessorTime;
                    double cpuPercent = (cpu - lastCpu).TotalSeconds / elapsed * 100.0;
                    double outKbps = (transport.Counters.WireBytesSent - lastWireOut) * 8 / 1000.0 / elapsed;
                    double inKbps = (transport.Counters.WireBytesReceived - lastWireIn) * 8 / 1000.0 / elapsed;
                    double snapKBps = (s.SnapshotBytes - lastSnapshotBytes) / 1024.0 / elapsed;
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "[stats] t={0:0}s players={1} conns={2} tick avg={3:0.00}ms max={4:0.00}ms world={12:0.00}ms cpu={5:0}% out={6:0}kbit/s in={7:0}kbit/s snapshots={8:0.0}KB/s starved={9} dropped={10} oversize={11} entities={13} actions={14} saves={15}",
                        now, s.Players, s.Connections, s.AverageTickMs, s.MaxTickMs, cpuPercent, outKbps, inKbps, snapKBps, s.StarvedTicks, s.DroppedInputs, transport.Counters.OversizeDropped,
                        s.LastWorldMs, world.Entities.Count, s.Actions, s.Saves));
                    summary.Samples.Add(new Sample
                    {
                        Time = now, Players = s.Players, AvgTickMs = s.AverageTickMs, MaxTickMs = s.MaxTickMs,
                        CpuPercent = cpuPercent, OutKbps = outKbps, InKbps = inKbps,
                    });
                    s.ResetWindow();
                    lastWireOut = transport.Counters.WireBytesSent;
                    lastWireIn = transport.Counters.WireBytesReceived;
                    lastSnapshotBytes = s.SnapshotBytes;
                    lastCpu = cpu;
                    lastStatsTime = now;
                    nextStats += statsInterval;
                }

                return duration <= 0 || now < duration;
            }, cancel.Token);

            Log.Info("Server", "Shutting down (saving the world).");
            summary.Ticks = server.Stats.Tick;
            summary.StarvedTicks = server.Stats.StarvedTicks;
            summary.DroppedInputs = server.Stats.DroppedInputs;
            summary.MaxSnapshotBytes = server.Stats.MaxSnapshotBytes;
            summary.OversizeDropped = transport.Counters.OversizeDropped;
            summary.Characters = world.Characters.Count;
            summary.Entities = world.Entities.Count;
            server.Shutdown();
            store?.Dispose();
            if (!string.IsNullOrEmpty(statsJson))
            {
                File.WriteAllText(statsJson, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            }

            return 0;
        }

        sealed class Summary
        {
            public int TickRate { get; set; }
            public int ProcessorCount { get; set; }
            public long Ticks { get; set; }
            public long StarvedTicks { get; set; }
            public long DroppedInputs { get; set; }
            public int MaxSnapshotBytes { get; set; }
            public long OversizeDropped { get; set; }
            public int Characters { get; set; }
            public int Entities { get; set; }
            public System.Collections.Generic.List<Sample> Samples { get; } = new System.Collections.Generic.List<Sample>();
        }

        sealed class Sample
        {
            public double Time { get; set; }
            public int Players { get; set; }
            public double AvgTickMs { get; set; }
            public double MaxTickMs { get; set; }
            public double CpuPercent { get; set; }
            public double OutKbps { get; set; }
            public double InKbps { get; set; }
        }
    }
}
