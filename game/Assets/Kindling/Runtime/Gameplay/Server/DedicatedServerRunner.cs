using System.Collections.Generic;
using System.IO;
using Kindling.Content;
using Kindling.Core.Diagnostics;
using Kindling.Gameplay.Boot;
using Kindling.Gameplay.Content;
using Kindling.Net.Protocol;
using Kindling.Net.Server;
using Kindling.Net.Transport;
using Kindling.Simulation.Game;
using Kindling.Simulation.Persistence;
using Kindling.Simulation.World;
using UnityEngine;

namespace Kindling.Gameplay.Server
{
    /// <summary>
    /// Runs the authoritative <see cref="ServerHost"/> inside Unity: the Linux dedicated server build,
    /// or any desktop build/editor started with <c>-kindlingServer</c>. Arguments: <c>-port</c>,
    /// <c>-maxPlayers</c>, <c>-seed</c>, <c>-tickRate</c>, <c>-world &lt;file&gt;</c>, <c>-wipe</c>, <c>-noSave</c>.
    /// The Unity server saves to a single file (<see cref="FileWorldStore"/>); the .NET dev server uses SQLite.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DedicatedServerRunner : MonoBehaviour
    {
        const string LogCategory = "Server";
        const float StatsIntervalSeconds = 30f;
        const int MaxTicksPerFrame = 5;

        LiteNetServerTransport m_Transport;
        ServerHost m_Server;
        IWorldStore m_Store;
        double m_Accumulator;
        double m_TickSeconds;
        float m_NextStats;

        public ServerHost Server => m_Server;

        void Start()
        {
            var config = new ServerConfig
            {
                Port = CommandLine.GetInt("-port", NetProtocol.DefaultPort),
                MaxPlayers = CommandLine.GetInt("-maxPlayers", 100),
                MapSeed = CommandLine.GetInt("-seed", 1),
                TickRate = CommandLine.GetInt("-tickRate", NetProtocol.TickRate),
                AllowDevAuth = !CommandLine.Has("-noDevAuth"),
            };

            ContentDatabase content = UnityContent.Database;
            GameConfig game = GameConfig.FromRules(content.Rules);
            WorldMap map = WorldMap.Generate(config.MapSeed, content);
            GameWorld world;
            WorldPersistence persistence = null;
            if (CommandLine.Has("-noSave"))
            {
                world = ServerHost.CreateWorld(content, config, game);
            }
            else
            {
                string path = CommandLine.Get("-world", Path.Combine(Application.persistentDataPath, "world.kws"));
                if (CommandLine.Has("-wipe") && File.Exists(path))
                {
                    File.Delete(path);
                }

                m_Store = new FileWorldStore(path);
                var warnings = new List<string>();
                world = WorldPersistence.LoadOrCreate(content, map, game, (ulong)config.MapSeed * 7919UL + 1, m_Store, warnings, out bool loaded);
                foreach (string w in warnings)
                {
                    Log.Warning(LogCategory, w);
                }

                persistence = new WorldPersistence(world, m_Store);
                Log.Info(LogCategory, loaded ? $"Loaded {path} ({world.Characters.Count} characters)." : $"New world at {path}.");
            }

            m_TickSeconds = 1.0 / config.TickRate;
            m_Transport = new LiteNetServerTransport(config.MaxPlayers + 16);
            m_Server = new ServerHost(m_Transport, config, world, persistence);
            if (!m_Server.Start())
            {
                Log.Error(LogCategory, "Server failed to start; quitting.");
                Application.Quit(1);
                enabled = false;
                return;
            }

            m_NextStats = Time.realtimeSinceStartup + StatsIntervalSeconds;
        }

        void Update()
        {
            if (m_Server == null)
            {
                return;
            }

            m_Accumulator += Time.unscaledDeltaTime;
            int ticks = 0;
            while (m_Accumulator >= m_TickSeconds)
            {
                m_Accumulator -= m_TickSeconds;
                if (++ticks > MaxTicksPerFrame)
                {
                    m_Accumulator = 0; // a long hitch: skip time instead of spiralling
                    break;
                }

                m_Server.Tick();
            }

            if (Time.realtimeSinceStartup >= m_NextStats)
            {
                m_NextStats += StatsIntervalSeconds;
                ServerStats s = m_Server.Stats;
                Log.Info(LogCategory, $"players={s.Players} tick avg={s.AverageTickMs:0.00}ms max={s.MaxTickMs:0.00}ms world={s.LastWorldMs:0.00}ms saves={s.Saves}");
                s.ResetWindow();
            }
        }

        void OnApplicationQuit() => Stop();

        void OnDestroy() => Stop();

        void Stop()
        {
            if (m_Server != null)
            {
                m_Server.Shutdown(); // saves the world
                m_Server = null;
            }

            m_Store?.Dispose();
            m_Store = null;
        }
    }
}
