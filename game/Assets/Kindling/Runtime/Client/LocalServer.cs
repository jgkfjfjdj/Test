using System;
using System.Net;
using System.Net.Sockets;
using Kindling.Net.Protocol;
using Kindling.Net.Server;
using Kindling.Net.Transport;

namespace Kindling.Client
{
    /// <summary>
    /// A server running inside the client process: offline play (in-memory network, nothing leaves the
    /// phone) or hosting on Wi-Fi (UDP, so other devices on the same network can join).
    /// </summary>
    public sealed class LocalServer : IDisposable
    {
        readonly InMemoryNetwork m_Network;
        readonly ServerHost m_Server;
        readonly double m_TickSeconds;
        double m_Accumulator;

        LocalServer(InMemoryNetwork network, IServerTransport transport, ServerConfig config)
        {
            m_Network = network;
            Config = config;
            m_Server = new ServerHost(transport, config);
            m_TickSeconds = 1.0 / config.TickRate;
        }

        public ServerConfig Config { get; }

        public ServerHost Server => m_Server;

        public bool IsUdp => m_Network == null;

        public static LocalServer CreateOffline()
        {
            var network = new InMemoryNetwork();
            var server = new LocalServer(network, network.CreateServer(), new ServerConfig { MaxPlayers = 8 });
            return server.m_Server.Start() ? server : null;
        }

        public static LocalServer CreateUdp(int port, int maxPlayers = 16)
        {
            var config = new ServerConfig { Port = port, MaxPlayers = maxPlayers };
            var server = new LocalServer(null, new LiteNetServerTransport(maxPlayers + 4), config);
            if (server.m_Server.Start())
            {
                return server;
            }

            server.Dispose();
            return null;
        }

        /// <summary>Client transport for offline play (only valid for <see cref="CreateOffline"/>).</summary>
        public IClientTransport CreateClientTransport() =>
            m_Network != null ? m_Network.CreateClient() : throw new InvalidOperationException("UDP servers are joined over UDP.");

        public void Step(double deltaSeconds)
        {
            m_Network?.Advance(deltaSeconds);
            m_Accumulator += deltaSeconds;
            int ticks = 0;
            while (m_Accumulator >= m_TickSeconds)
            {
                m_Accumulator -= m_TickSeconds;
                if (++ticks > 5)
                {
                    m_Accumulator = 0;
                    break;
                }

                m_Server.Tick();
            }
        }

        public void Dispose() => m_Server.Dispose();

        /// <summary>This device's address on the local network (best effort; null if unknown).</summary>
        public static string GuessLanAddress()
        {
            try
            {
                // "Connecting" a UDP socket sends nothing; it just picks the outgoing interface.
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect("192.0.2.1", NetProtocol.DefaultPort); // TEST-NET-1, never routed
                return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
