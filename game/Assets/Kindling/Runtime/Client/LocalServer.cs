using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Kindling.Content;
using Kindling.Core.Diagnostics;
using Kindling.Net.Protocol;
using Kindling.Net.Server;
using Kindling.Net.Transport;
using Kindling.Simulation.Game;
using Kindling.Simulation.Persistence;
using Kindling.Simulation.World;
using UnityEngine;

namespace Kindling.Client
{
    /// <summary>
    /// A server running inside the client process, with its world saved on the device: offline play
    /// (in-memory network, nothing leaves the phone) or hosting on Wi-Fi (UDP, so other devices on the
    /// same network can join).
    /// </summary>
    public sealed class LocalServer : IDisposable
    {
        readonly InMemoryNetwork m_Network;
        readonly ServerHost m_Server;
        readonly IWorldStore m_Store;
        readonly double m_TickSeconds;
        double m_Accumulator;

        LocalServer(InMemoryNetwork network, IServerTransport transport, ServerConfig config, GameWorld world, WorldPersistence persistence, IWorldStore store)
        {
            m_Network = network;
            m_Store = store;
            Config = config;
            m_Server = new ServerHost(transport, config, world, persistence);
            m_TickSeconds = 1.0 / config.TickRate;
        }

        public ServerConfig Config { get; }

        public ServerHost Server => m_Server;

        public bool IsUdp => m_Network == null;

        public static LocalServer CreateOffline(ContentDatabase content)
        {
            var network = new InMemoryNetwork();
            var config = new ServerConfig { MaxPlayers = 8, SaveIntervalSeconds = 30 };
            LocalServer server = Create(content, config, network, network.CreateServer(), "offline");
            return server.m_Server.Start() ? server : null;
        }

        public static LocalServer CreateUdp(ContentDatabase content, int port, int maxPlayers = 16)
        {
            var config = new ServerConfig { Port = port, MaxPlayers = maxPlayers, SaveIntervalSeconds = 30 };
            LocalServer server = Create(content, config, null, new LiteNetServerTransport(maxPlayers + 4), "hosted");
            if (server.m_Server.Start())
            {
                return server;
            }

            server.Dispose();
            return null;
        }

        static LocalServer Create(ContentDatabase content, ServerConfig config, InMemoryNetwork network, IServerTransport transport, string folder)
        {
            var store = new FileWorldStore(Path.Combine(Application.persistentDataPath, folder, "world.kws"));
            var warnings = new List<string>();
            GameWorld world;
            try
            {
                world = WorldPersistence.LoadOrCreate(content, WorldMap.Generate(config.MapSeed, content), GameConfig.FromRules(content.Rules), (ulong)config.MapSeed * 7919UL + 1, store, warnings, out _);
            }
            catch (Exception e) when (e is InvalidDataException || e is InvalidOperationException)
            {
                // An unreadable or incompatible local save: keep it aside and start fresh rather than locking the player out.
                Log.Error("LocalServer", $"Local world at {store.Path} can't be loaded; starting a new one.", e);
                File.Move(store.Path, store.Path + ".broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
                store = new FileWorldStore(store.Path);
                world = WorldPersistence.LoadOrCreate(content, WorldMap.Generate(config.MapSeed, content), GameConfig.FromRules(content.Rules), (ulong)config.MapSeed * 7919UL + 1, store, warnings, out _);
            }

            foreach (string w in warnings)
            {
                Log.Warning("LocalServer", w);
            }

            return new LocalServer(network, transport, config, world, new WorldPersistence(world, store), store);
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

        /// <summary>Saves now (the app is going to the background and may be killed).</summary>
        public void Save() => m_Server.SaveNow();

        public void Dispose()
        {
            m_Server.Dispose(); // saves the world
            m_Store.Dispose();
        }

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
