using System;
using Kindling.Net.Protocol;

namespace Kindling.Net.Transport
{
    /// <summary>
    /// Receives transport events during <c>Poll</c>, on the polling thread. Calling <c>Disconnect</c>
    /// yourself never raises <see cref="OnDisconnected"/> for that connection.
    /// </summary>
    public interface ITransportEvents
    {
        void OnConnected(int connectionId);

        void OnDisconnected(int connectionId, DisconnectCode code, string message);

        /// <summary>The buffer is only valid during the call.</summary>
        void OnData(int connectionId, byte[] data, int offset, int count);
    }

    public interface IServerTransport : IDisposable
    {
        TransportCounters Counters { get; }

        bool Start(int port);

        void Poll(ITransportEvents events);

        void Send(int connectionId, byte[] data, int offset, int count, Delivery delivery);

        void Disconnect(int connectionId, DisconnectCode code, string message = null);

        /// <summary>Round-trip time in milliseconds, or -1 if unknown.</summary>
        int GetRoundTripMs(int connectionId);

        /// <summary>Sends anything queued now instead of waiting for the transport's own update.</summary>
        void Flush();

        void Stop();
    }

    /// <summary>Client side of a transport. Connection id is always 0.</summary>
    public interface IClientTransport : IDisposable
    {
        TransportCounters Counters { get; }

        int RoundTripMs { get; }

        void Connect(string host, int port);

        void Poll(ITransportEvents events);

        void Send(byte[] data, int offset, int count, Delivery delivery);

        void Disconnect(DisconnectCode code);

        void Flush();
    }

    /// <summary>Byte and packet counts. Payload counts are message bytes; wire counts include transport headers where known.</summary>
    public sealed class TransportCounters
    {
        public long PayloadBytesSent;
        public long PayloadBytesReceived;
        public long PacketsSent;
        public long PacketsReceived;
        public long WireBytesSent;
        public long WireBytesReceived;

        public void CountSent(int bytes)
        {
            PayloadBytesSent += bytes;
            PacketsSent++;
        }

        public void CountReceived(int bytes)
        {
            PayloadBytesReceived += bytes;
            PacketsReceived++;
        }
    }
}
