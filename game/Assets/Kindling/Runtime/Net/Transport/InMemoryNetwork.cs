using System;
using System.Collections.Generic;
using Kindling.Core.Numerics;
using Kindling.Net.Protocol;

namespace Kindling.Net.Transport
{
    /// <summary>Simulated link quality for <see cref="InMemoryNetwork"/>.</summary>
    public sealed class NetworkConditions
    {
        /// <summary>One-way latency in milliseconds.</summary>
        public double LatencyMs;

        /// <summary>Extra random one-way delay, uniformly 0..JitterMs.</summary>
        public double JitterMs;

        /// <summary>Probability (0..1) that a sequenced packet is lost. Reliable packets are delayed by a retransmit instead.</summary>
        public double LossRate;

        public static NetworkConditions Perfect => new NetworkConditions();

        public static NetworkConditions Of(double latencyMs, double jitterMs = 0, double lossRate = 0) =>
            new NetworkConditions { LatencyMs = latencyMs, JitterMs = jitterMs, LossRate = lossRate };
    }

    /// <summary>
    /// A deterministic, in-process network with a virtual clock: latency, jitter, loss, reordering of
    /// unreliable packets, retransmit delays for reliable ones, and abrupt link loss. Used by tests and by
    /// offline play (server and client in one process).
    /// </summary>
    public sealed class InMemoryNetwork
    {
        readonly DeterministicRandom m_Random;
        readonly Dictionary<int, InMemoryServerTransport> m_Servers = new Dictionary<int, InMemoryServerTransport>();

        public InMemoryNetwork(ulong seed = 1)
        {
            m_Random = new DeterministicRandom(seed);
        }

        /// <summary>Virtual time in seconds.</summary>
        public double Now { get; private set; }

        public double DisconnectTimeoutSeconds { get; set; } = 5;

        public void Advance(double seconds)
        {
            if (seconds > 0)
            {
                Now += seconds;
            }
        }

        public InMemoryServerTransport CreateServer() => new InMemoryServerTransport(this);

        public InMemoryClientTransport CreateClient(NetworkConditions conditions = null) =>
            new InMemoryClientTransport(this, conditions ?? NetworkConditions.Perfect);

        /// <summary>Cuts a client's link abruptly (no disconnect message); both sides time out.</summary>
        public void Sever(InMemoryClientTransport client) => client.Sever();

        internal void Register(int port, InMemoryServerTransport server) => m_Servers[port] = server;

        internal void Unregister(int port, InMemoryServerTransport server)
        {
            if (m_Servers.TryGetValue(port, out var existing) && existing == server)
            {
                m_Servers.Remove(port);
            }
        }

        internal InMemoryServerTransport Find(int port) => m_Servers.TryGetValue(port, out var s) ? s : null;

        internal double Delay(NetworkConditions c) => (c.LatencyMs + m_Random.NextFloat() * c.JitterMs) / 1000.0;

        internal bool Lose(NetworkConditions c) => c.LossRate > 0 && m_Random.NextFloat() < c.LossRate;
    }

    internal enum PacketKind : byte
    {
        Connect,
        Accept,
        Data,
        Disconnect,
    }

    internal struct InFlightPacket
    {
        public double At;
        public long SendIndex;
        public PacketKind Kind;
        public Delivery Delivery;
        public byte[] Data;
    }

    internal sealed class PacketQueue
    {
        readonly List<InFlightPacket> m_Packets = new List<InFlightPacket>();
        long m_NextSendIndex;
        long m_LastSequencedDelivered = -1;
        double m_LastReliableAt;

        public void Enqueue(InMemoryNetwork network, NetworkConditions conditions, PacketKind kind, byte[] data, Delivery delivery)
        {
            long index = m_NextSendIndex++;
            double at = network.Now + network.Delay(conditions);
            bool reliable = kind != PacketKind.Data || delivery == Delivery.ReliableOrdered;
            if (reliable)
            {
                while (network.Lose(conditions))
                {
                    at += Math.Max(0.02, 2 * conditions.LatencyMs / 1000.0); // retransmit after about one RTT
                }

                at = Math.Max(at, m_LastReliableAt); // ordered
                m_LastReliableAt = at;
            }
            else if (network.Lose(conditions))
            {
                return;
            }

            m_Packets.Add(new InFlightPacket { At = at, SendIndex = index, Kind = kind, Delivery = delivery, Data = data });
        }

        public bool TryDequeue(double now, out InFlightPacket packet)
        {
            while (true)
            {
                int best = -1;
                for (int i = 0; i < m_Packets.Count; i++)
                {
                    var p = m_Packets[i];
                    if (p.At <= now && (best < 0 || p.At < m_Packets[best].At || (p.At == m_Packets[best].At && p.SendIndex < m_Packets[best].SendIndex)))
                    {
                        best = i;
                    }
                }

                if (best < 0)
                {
                    packet = default;
                    return false;
                }

                packet = m_Packets[best];
                m_Packets.RemoveAt(best);
                if (packet.Kind == PacketKind.Data && packet.Delivery == Delivery.Sequenced)
                {
                    if (packet.SendIndex < m_LastSequencedDelivered)
                    {
                        continue; // older than one already delivered: sequenced channels drop it
                    }

                    m_LastSequencedDelivered = packet.SendIndex;
                }

                return true;
            }
        }

        public void Clear() => m_Packets.Clear();
    }

    internal sealed class InMemoryLink
    {
        public readonly PacketQueue ToServer = new PacketQueue();
        public readonly PacketQueue ToClient = new PacketQueue();
        public NetworkConditions Conditions;
        public InMemoryServerTransport Server;
        public int ServerConnectionId;
        public bool Accepted;
        public bool ClientConnected;
        public bool ServerDone;
        public bool ClientDone;
        public bool Severed;
        public double SeveredAt;
        public double ConnectStartedAt;
    }

    public sealed class InMemoryServerTransport : IServerTransport
    {
        readonly InMemoryNetwork m_Network;
        readonly List<InMemoryLink> m_Links = new List<InMemoryLink>();
        readonly Dictionary<int, InMemoryLink> m_Connections = new Dictionary<int, InMemoryLink>();
        int m_NextConnectionId = 1;
        int m_Port = -1;

        internal InMemoryServerTransport(InMemoryNetwork network)
        {
            m_Network = network;
        }

        public TransportCounters Counters { get; } = new TransportCounters();

        public int ConnectionCount => m_Connections.Count;

        public bool Start(int port)
        {
            if (m_Port >= 0 || m_Network.Find(port) != null)
            {
                return false;
            }

            m_Port = port;
            m_Network.Register(port, this);
            return true;
        }

        internal void Attach(InMemoryLink link) => m_Links.Add(link);

        public void Poll(ITransportEvents events)
        {
            double now = m_Network.Now;
            for (int i = 0; i < m_Links.Count; i++)
            {
                InMemoryLink link = m_Links[i];
                if (link.ServerDone)
                {
                    continue;
                }

                if (link.Severed)
                {
                    if (now >= link.SeveredAt + m_Network.DisconnectTimeoutSeconds)
                    {
                        link.ServerDone = true;
                        if (link.Accepted && m_Connections.Remove(link.ServerConnectionId))
                        {
                            events.OnDisconnected(link.ServerConnectionId, DisconnectCode.Timeout, string.Empty);
                        }
                    }

                    continue;
                }

                while (!link.ServerDone && link.ToServer.TryDequeue(now, out InFlightPacket p))
                {
                    switch (p.Kind)
                    {
                        case PacketKind.Connect:
                            if (m_Port < 0 || link.Accepted)
                            {
                                break;
                            }

                            link.Accepted = true;
                            link.ServerConnectionId = m_NextConnectionId++;
                            m_Connections[link.ServerConnectionId] = link;
                            link.ToClient.Enqueue(m_Network, link.Conditions, PacketKind.Accept, null, Delivery.ReliableOrdered);
                            events.OnConnected(link.ServerConnectionId);
                            break;
                        case PacketKind.Data:
                            if (link.Accepted)
                            {
                                Counters.CountReceived(p.Data.Length);
                                events.OnData(link.ServerConnectionId, p.Data, 0, p.Data.Length);
                            }

                            break;
                        case PacketKind.Disconnect:
                            link.ServerDone = true;
                            if (link.Accepted && m_Connections.Remove(link.ServerConnectionId))
                            {
                                DisconnectCode code = DisconnectPayload.Decode(p.Data, 0, p.Data?.Length ?? 0, DisconnectCode.None, out string message);
                                events.OnDisconnected(link.ServerConnectionId, code, message);
                            }

                            break;
                    }
                }
            }

            m_Links.RemoveAll(l => l.ServerDone && l.ClientDone);
        }

        public void Send(int connectionId, byte[] data, int offset, int count, Delivery delivery)
        {
            if (m_Connections.TryGetValue(connectionId, out InMemoryLink link) && !link.Severed)
            {
                var copy = new byte[count];
                Buffer.BlockCopy(data, offset, copy, 0, count);
                link.ToClient.Enqueue(m_Network, link.Conditions, PacketKind.Data, copy, delivery);
                Counters.CountSent(count);
            }
        }

        public void Disconnect(int connectionId, DisconnectCode code, string message = null)
        {
            if (m_Connections.TryGetValue(connectionId, out InMemoryLink link))
            {
                m_Connections.Remove(connectionId);
                link.ServerDone = true;
                if (!link.Severed)
                {
                    link.ToClient.Enqueue(m_Network, link.Conditions, PacketKind.Disconnect, DisconnectPayload.Encode(code, message), Delivery.ReliableOrdered);
                }
            }
        }

        public int GetRoundTripMs(int connectionId) =>
            m_Connections.TryGetValue(connectionId, out InMemoryLink link) ? (int)(2 * link.Conditions.LatencyMs + link.Conditions.JitterMs) : -1;

        public void Flush()
        {
        }

        public void Stop()
        {
            foreach (int id in new List<int>(m_Connections.Keys))
            {
                Disconnect(id, DisconnectCode.ServerShutdown, "Server stopped.");
            }

            if (m_Port >= 0)
            {
                m_Network.Unregister(m_Port, this);
                m_Port = -1;
            }
        }

        public void Dispose() => Stop();
    }

    public sealed class InMemoryClientTransport : IClientTransport
    {
        readonly InMemoryNetwork m_Network;
        InMemoryLink m_Link;

        internal InMemoryClientTransport(InMemoryNetwork network, NetworkConditions conditions)
        {
            m_Network = network;
            Conditions = conditions;
        }

        /// <summary>Conditions for the next connection. Mutating the object also changes the current link.</summary>
        public NetworkConditions Conditions { get; set; }

        public TransportCounters Counters { get; } = new TransportCounters();

        public int RoundTripMs => m_Link != null && m_Link.ClientConnected ? (int)(2 * Conditions.LatencyMs + Conditions.JitterMs) : -1;

        public bool IsConnected => m_Link != null && m_Link.ClientConnected;

        public void Connect(string host, int port)
        {
            Disconnect(DisconnectCode.ClientQuit);
            m_Link = new InMemoryLink { Conditions = Conditions, ConnectStartedAt = m_Network.Now };
            InMemoryServerTransport server = m_Network.Find(port);
            if (server != null)
            {
                m_Link.Server = server;
                server.Attach(m_Link);
                m_Link.ToServer.Enqueue(m_Network, Conditions, PacketKind.Connect, null, Delivery.ReliableOrdered);
            }
            else
            {
                m_Link.ServerDone = true;
            }
        }

        internal void Sever()
        {
            if (m_Link != null && !m_Link.Severed)
            {
                m_Link.Severed = true;
                m_Link.SeveredAt = m_Network.Now;
                m_Link.ToClient.Clear();
                m_Link.ToServer.Clear();
            }
        }

        public void Poll(ITransportEvents events)
        {
            InMemoryLink link = m_Link;
            if (link == null)
            {
                return;
            }

            double now = m_Network.Now;
            if (link.Severed)
            {
                if (now >= link.SeveredAt + m_Network.DisconnectTimeoutSeconds)
                {
                    Close(link);
                    events.OnDisconnected(0, link.ClientConnected ? DisconnectCode.Timeout : DisconnectCode.ConnectionFailed, string.Empty);
                }

                return;
            }

            if (!link.ClientConnected && now >= link.ConnectStartedAt + m_Network.DisconnectTimeoutSeconds)
            {
                Close(link);
                events.OnDisconnected(0, DisconnectCode.ConnectionFailed, "No answer from server.");
                return;
            }

            while (m_Link == link && link.ToClient.TryDequeue(now, out InFlightPacket p))
            {
                switch (p.Kind)
                {
                    case PacketKind.Accept:
                        link.ClientConnected = true;
                        events.OnConnected(0);
                        break;
                    case PacketKind.Data:
                        if (link.ClientConnected)
                        {
                            Counters.CountReceived(p.Data.Length);
                            events.OnData(0, p.Data, 0, p.Data.Length);
                        }

                        break;
                    case PacketKind.Disconnect:
                        Close(link);
                        DisconnectCode code = DisconnectPayload.Decode(p.Data, 0, p.Data?.Length ?? 0, DisconnectCode.None, out string message);
                        events.OnDisconnected(0, code, message);
                        break;
                }
            }
        }

        public void Send(byte[] data, int offset, int count, Delivery delivery)
        {
            if (m_Link != null && m_Link.ClientConnected && !m_Link.Severed)
            {
                var copy = new byte[count];
                Buffer.BlockCopy(data, offset, copy, 0, count);
                m_Link.ToServer.Enqueue(m_Network, m_Link.Conditions, PacketKind.Data, copy, delivery);
                Counters.CountSent(count);
            }
        }

        public void Disconnect(DisconnectCode code)
        {
            InMemoryLink link = m_Link;
            if (link == null)
            {
                return;
            }

            Close(link);
            if (!link.Severed && !link.ServerDone)
            {
                link.ToServer.Enqueue(m_Network, link.Conditions, PacketKind.Disconnect, DisconnectPayload.Encode(code), Delivery.ReliableOrdered);
            }
        }

        public void Flush()
        {
        }

        public void Dispose() => Disconnect(DisconnectCode.ClientQuit);

        void Close(InMemoryLink link)
        {
            link.ClientDone = true;
            if (m_Link == link)
            {
                m_Link = null;
            }
        }
    }
}
