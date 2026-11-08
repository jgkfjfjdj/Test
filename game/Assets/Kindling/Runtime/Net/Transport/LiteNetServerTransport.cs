using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Kindling.Net.Protocol;
using LiteNetLib;

namespace Kindling.Net.Transport
{
    /// <summary>UDP server transport on LiteNetLib (reliable + sequenced channels, IPv4 and IPv6).</summary>
    public sealed class LiteNetServerTransport : IServerTransport, INetEventListener
    {
        readonly NetManager m_Manager;
        readonly Dictionary<int, NetPeer> m_Peers = new Dictionary<int, NetPeer>();
        readonly int m_MaxConnections;
        ITransportEvents m_Events;

        /// <param name="maxConnections">Transport limit; keep it above the game's player limit so a "server full" reason can still be delivered.</param>
        public LiteNetServerTransport(int maxConnections, int disconnectTimeoutMs = 10000, LinkSimulation simulation = null)
        {
            m_MaxConnections = maxConnections;
            m_Manager = new NetManager(this)
            {
                AutoRecycle = true,
                UnsyncedEvents = false,
                EnableStatistics = true,
                IPv6Enabled = true,
                DisconnectTimeout = disconnectTimeoutMs,
                UpdateTime = 10,
                ChannelsCount = 1,
            };
            if (simulation != null)
            {
                m_Manager.SimulateLatency = simulation.MaxLatencyMs > 0;
                m_Manager.SimulationMinLatency = simulation.MinLatencyMs;
                m_Manager.SimulationMaxLatency = System.Math.Max(simulation.MinLatencyMs, simulation.MaxLatencyMs);
                m_Manager.SimulatePacketLoss = simulation.LossPercent > 0;
                m_Manager.SimulationPacketLossChance = simulation.LossPercent;
            }
        }

        public TransportCounters Counters { get; } = new TransportCounters();

        public int ConnectionCount => m_Peers.Count;

        public bool Start(int port) => m_Manager.Start(port);

        public void Poll(ITransportEvents events)
        {
            m_Events = events;
            m_Manager.PollEvents();
            m_Events = null;
            Counters.WireBytesSent = m_Manager.Statistics.BytesSent;
            Counters.WireBytesReceived = m_Manager.Statistics.BytesReceived;
        }

        public void Send(int connectionId, byte[] data, int offset, int count, Delivery delivery)
        {
            if (m_Peers.TryGetValue(connectionId, out NetPeer peer))
            {
                DeliveryMethod method = ToMethod(delivery);
                if (method == DeliveryMethod.Sequenced && count > peer.GetMaxSinglePacketSize(method))
                {
                    Counters.OversizeDropped++; // LiteNetLib would throw; never take the server down for it
                    return;
                }

                peer.Send(data, offset, count, method);
                Counters.CountSent(count);
            }
        }

        public int GetMaxSequencedPayload(int connectionId) =>
            m_Peers.TryGetValue(connectionId, out NetPeer peer) ? peer.GetMaxSinglePacketSize(DeliveryMethod.Sequenced) : 0;

        public void Disconnect(int connectionId, DisconnectCode code, string message = null)
        {
            if (m_Peers.TryGetValue(connectionId, out NetPeer peer))
            {
                m_Peers.Remove(connectionId);
                m_Manager.DisconnectPeer(peer, DisconnectPayload.Encode(code, message));
            }
        }

        public int GetRoundTripMs(int connectionId) => m_Peers.TryGetValue(connectionId, out NetPeer peer) ? peer.RoundTripTime : -1;

        public void Flush() => m_Manager.TriggerUpdate();

        public void Stop()
        {
            m_Manager.Stop(true);
            m_Peers.Clear();
        }

        public void Dispose() => Stop();

        internal static DeliveryMethod ToMethod(Delivery delivery) =>
            delivery == Delivery.ReliableOrdered ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced;

        internal static DisconnectCode Translate(DisconnectInfo info, out string message)
        {
            message = string.Empty;
            switch (info.Reason)
            {
                case DisconnectReason.RemoteConnectionClose:
                case DisconnectReason.ConnectionRejected:
                    var data = info.AdditionalData;
                    if (data != null && data.AvailableBytes > 0)
                    {
                        return DisconnectPayload.Decode(data.RawData, data.Position, data.AvailableBytes, DisconnectCode.ConnectionFailed, out message);
                    }

                    return info.Reason == DisconnectReason.ConnectionRejected ? DisconnectCode.ConnectionFailed : DisconnectCode.None;
                case DisconnectReason.Timeout:
                    return DisconnectCode.Timeout;
                default:
                    message = info.Reason.ToString();
                    return DisconnectCode.ConnectionFailed;
            }
        }

        void INetEventListener.OnConnectionRequest(ConnectionRequest request)
        {
            if (m_Peers.Count >= m_MaxConnections)
            {
                request.Reject(DisconnectPayload.Encode(DisconnectCode.ServerFull, "Server is full."));
                return;
            }

            request.AcceptIfKey(NetProtocol.ConnectionKey);
        }

        void INetEventListener.OnPeerConnected(NetPeer peer)
        {
            m_Peers[peer.Id] = peer;
            m_Events?.OnConnected(peer.Id);
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (disconnectInfo.Reason == DisconnectReason.DisconnectPeerCalled)
            {
                return; // we initiated it
            }

            if (m_Peers.TryGetValue(peer.Id, out NetPeer known) && known == peer)
            {
                m_Peers.Remove(peer.Id);
                DisconnectCode code = Translate(disconnectInfo, out string message);
                m_Events?.OnDisconnected(peer.Id, code, message);
            }
        }

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (m_Peers.ContainsKey(peer.Id))
            {
                Counters.CountReceived(reader.AvailableBytes);
                m_Events?.OnData(peer.Id, reader.RawData, reader.Position, reader.AvailableBytes);
            }
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
        }

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
        }
    }
}
