using Kindling.Core.Diagnostics;
using Kindling.Gameplay.Boot;
using Kindling.Net.Protocol;
using Kindling.Net.Server;
using Kindling.Net.Transport;
using UnityEngine;

namespace Kindling.Gameplay.Server
{
    /// <summary>
    /// Runs the authoritative <see cref="ServerHost"/> inside Unity: the Linux dedicated server build,
    /// or any desktop build/editor started with <c>-kindlingServer</c>. Arguments: <c>-port</c>,
    /// <c>-maxPlayers</c>, <c>-seed</c>, <c>-tickRate</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DedicatedServerRunner : MonoBehaviour
    {
        const string LogCategory = "Server";
        const float StatsIntervalSeconds = 30f;
        const int MaxTicksPerFrame = 5;

        LiteNetServerTransport m_Transport;
        ServerHost m_Server;
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

            m_TickSeconds = 1.0 / config.TickRate;
            m_Transport = new LiteNetServerTransport(config.MaxPlayers + 16);
            m_Server = new ServerHost(m_Transport, config);
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
                Log.Info(LogCategory, $"players={s.Players} tick avg={s.AverageTickMs:0.00}ms max={s.MaxTickMs:0.00}ms starved={s.StarvedTicks} dropped={s.DroppedInputs}");
                s.ResetWindow();
            }
        }

        void OnDestroy()
        {
            if (m_Server != null)
            {
                m_Server.Dispose();
                m_Server = null;
            }
        }
    }
}
