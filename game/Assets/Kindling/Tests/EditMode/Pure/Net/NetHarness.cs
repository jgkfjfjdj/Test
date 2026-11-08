using System;
using System.Collections.Generic;
using System.Numerics;
using Kindling.Content;
using Kindling.Net.Client;
using Kindling.Net.Server;
using Kindling.Net.Transport;
using Kindling.Simulation.Collision;
using Kindling.Simulation.Game;
using Kindling.Simulation.Persistence;

namespace Kindling.Tests.Pure.Net
{
    /// <summary>A server and clients on an <see cref="InMemoryNetwork"/>, stepped in virtual time at 120 fps.</summary>
    public sealed class NetHarness
    {
        public const int Port = 27015;
        public const double FrameDt = 1.0 / 120.0;

        public readonly InMemoryNetwork Network;
        public InMemoryServerTransport ServerTransport;
        public ServerHost Server;
        public readonly List<ClientSession> Clients = new List<ClientSession>();
        public readonly List<InMemoryClientTransport> Transports = new List<InMemoryClientTransport>();
        double m_ServerAccumulator;

        public NetHarness(ServerConfig config = null, ulong seed = 1, GameConfig game = null, GameWorld world = null, WorldPersistence persistence = null)
        {
            Network = new InMemoryNetwork(seed);
            config ??= new ServerConfig();
            config.Port = Port;
            StartServer(config, world ?? ServerHost.CreateWorld(ContentDatabase.LoadDefault(), config, game), persistence);
        }

        public GameWorld World => Server.World;

        /// <summary>
        /// Stops the current server (it saves on shutdown if it has persistence), lets clients notice, then
        /// starts a new one on the same port with the world <paramref name="load"/> returns.
        /// </summary>
        public void RestartServer(Func<(GameWorld World, WorldPersistence Persistence)> load)
        {
            ServerConfig config = Server.Config;
            Server.Shutdown();
            Run(0.5);
            (GameWorld world, WorldPersistence persistence) = load();
            StartServer(config, world, persistence);
        }

        void StartServer(ServerConfig config, GameWorld world, WorldPersistence persistence)
        {
            ServerTransport = Network.CreateServer();
            Server = new ServerHost(ServerTransport, config, world, persistence);
            if (!Server.Start())
            {
                throw new InvalidOperationException("Server did not start.");
            }
        }

        public double Time { get; private set; }

        public ClientSession AddClient(string name, NetworkConditions conditions = null, ClientSettings settings = null, bool connect = true)
        {
            settings ??= new ClientSettings();
            settings.DisplayName = name;
            if (string.IsNullOrEmpty(settings.AuthToken))
            {
                settings.AuthToken = "test-" + name;
            }

            var transport = Network.CreateClient(conditions ?? NetworkConditions.Perfect);
            var session = new ClientSession(transport, settings);
            Clients.Add(session);
            Transports.Add(transport);
            if (connect)
            {
                session.Connect("sim", Port);
            }

            return session;
        }

        public InMemoryClientTransport TransportOf(ClientSession session) => Transports[Clients.IndexOf(session)];

        /// <summary>Runs for <paramref name="seconds"/>; <paramref name="perFrame"/> runs before the clients update.</summary>
        public void Run(double seconds, Action perFrame = null)
        {
            int frames = (int)Math.Round(seconds / FrameDt);
            for (int f = 0; f < frames; f++)
            {
                Network.Advance(FrameDt);
                Time += FrameDt;
                m_ServerAccumulator += FrameDt;
                while (m_ServerAccumulator >= 1.0 / Server.Config.TickRate - 1e-9)
                {
                    m_ServerAccumulator -= 1.0 / Server.Config.TickRate;
                    Server.Tick();
                }

                perFrame?.Invoke();
                foreach (ClientSession c in Clients)
                {
                    c.Update(FrameDt);
                }
            }
        }

        public void RunUntil(Func<bool> condition, double maxSeconds)
        {
            double end = Time + maxSeconds;
            while (!condition() && Time < end)
            {
                Run(FrameDt);
            }
        }

        /// <summary>A spot near <paramref name="target"/> where a standing player fits.</summary>
        public Vector3 FreeSpotNear(Vector3 target)
        {
            CollisionWorld world = Server.World.Collision;
            var scratch = new List<int>();
            for (int ring = 0; ring < 50; ring++)
            {
                for (int i = 0; i < 8; i++)
                {
                    var p = target + new Vector3(ring * 1.5f * (float)Math.Cos(i * Math.PI / 4), 0, ring * 1.5f * (float)Math.Sin(i * Math.PI / 4));
                    if (!world.Overlaps(Aabb.FromFeet(p + new Vector3(0, 0.01f, 0), 0.35f, 1.9f), scratch))
                    {
                        return p + new Vector3(0, 0.01f, 0);
                    }
                }
            }

            throw new InvalidOperationException("No free spot.");
        }
    }
}
