using System.Net;
using System.Net.Sockets;
using Kindling.Net.Protocol;
using LiteNetLib;
using Kindling.Core.Serialization;

namespace Kindling.Net.Transport
{
    /// <summary>UDP client transport on LiteNetLib. Can reconnect after a disconnect.</summary>
    public sealed class LiteNetClientTransport : IClientTransport, INetEventListener
    {
        readonly NetManager m_Manager;
        NetPeer m_Peer;
        ITransportEvents m_Events;

        public LiteNetClientTransport(int disconnectTimeoutMs = 10000, LinkSimulation simulation = null)
        {
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

        public int RoundTripMs => m_Peer != null ? m_Peer.RoundTripTime : -1;

        public void Connect(string host, int port)
        {
            if (!m_Manager.IsRunning)
            {
                m_Manager.Start();
            }

            if (m_Peer != null)
            {
                NetPeer old = m_Peer;
                m_Peer = null;
                m_Manager.DisconnectPeer(old);
            }

            m_Peer = m_Manager.Connect(host, port, NetProtocol.ConnectionKey);
        }

        public void Poll(ITransportEvents events)
        {
            m_Events = events;
            m_Manager.PollEvents();
            m_Events = null;
            Counters.WireBytesSent = m_Manager.Statistics.BytesSent;
            Counters.WireBytesReceived = m_Manager.Statistics.BytesReceived;
        }

        public void Send(byte[] data, int offset, int count, Delivery delivery)
        {
            if (m_Peer != null && m_Peer.ConnectionState == ConnectionState.Connected)
            {
                DeliveryMethod method = LiteNetServerTransport.ToMethod(delivery);
                if (method == DeliveryMethod.Sequenced && count > m_Peer.GetMaxSinglePacketSize(method))
                {
                    Counters.OversizeDropped++;
                    return;
                }

                m_Peer.Send(data, offset, count, method);
                Counters.CountSent(count);
            }
        }

        public void Disconnect(DisconnectCode code)
        {
            if (m_Peer != null)
            {
                NetPeer peer = m_Peer;
                m_Peer = null;
                m_Manager.DisconnectPeer(peer, DisconnectPayload.Encode(code));
                m_Manager.TriggerUpdate();
            }
        }

        public void Flush() => m_Manager.TriggerUpdate();

        public void Dispose()
        {
            m_Peer = null;
            m_Manager.Stop(true);
        }

        void INetEventListener.OnPeerConnected(NetPeer peer)
        {
            if (peer == m_Peer)
            {
                m_Events?.OnConnected(0);
            }
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (peer != m_Peer || disconnectInfo.Reason == DisconnectReason.DisconnectPeerCalled)
            {
                return;
            }

            m_Peer = null;
            DisconnectCode code = LiteNetServerTransport.Translate(disconnectInfo, out string message);
            m_Events?.OnDisconnected(0, code, message);
        }

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (peer == m_Peer)
            {
                Counters.CountReceived(reader.AvailableBytes);
                m_Events?.OnData(0, reader.RawData, reader.Position, reader.AvailableBytes);
            }
        }

        void INetEventListener.OnConnectionRequest(ConnectionRequest request) => request.Reject();

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
