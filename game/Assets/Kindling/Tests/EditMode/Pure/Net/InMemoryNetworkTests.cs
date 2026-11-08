using System.Collections.Generic;
using Kindling.Net.Protocol;
using Kindling.Net.Transport;
using NUnit.Framework;

namespace Kindling.Tests.Pure.Net
{
    public class InMemoryNetworkTests
    {
        sealed class Recorder : ITransportEvents
        {
            public readonly List<int> Connected = new List<int>();
            public readonly List<(int Id, DisconnectCode Code)> Disconnected = new List<(int, DisconnectCode)>();
            public readonly List<byte> Data = new List<byte>();
            public int LastConnection = -1;

            public void OnConnected(int connectionId)
            {
                Connected.Add(connectionId);
                LastConnection = connectionId;
            }

            public void OnDisconnected(int connectionId, DisconnectCode code, string message) => Disconnected.Add((connectionId, code));

            public void OnData(int connectionId, byte[] data, int offset, int count) => Data.Add(data[offset]);
        }

        static (InMemoryNetwork, InMemoryServerTransport, InMemoryClientTransport, Recorder, Recorder) Connect(NetworkConditions conditions)
        {
            var network = new InMemoryNetwork(7);
            var server = network.CreateServer();
            Assert.That(server.Start(1000), Is.True);
            var client = network.CreateClient(conditions);
            var se = new Recorder();
            var ce = new Recorder();
            client.Connect("sim", 1000);
            for (int i = 0; i < 100 && !client.IsConnected; i++)
            {
                network.Advance(0.01);
                server.Poll(se);
                client.Poll(ce);
            }

            Assert.That(client.IsConnected, Is.True);
            return (network, server, client, se, ce);
        }

        [Test]
        public void ReliableMessagesArriveInOrderDespiteLoss()
        {
            var (network, server, client, se, ce) = Connect(NetworkConditions.Of(40, 30, 0.3));
            for (byte i = 0; i < 100; i++)
            {
                client.Send(new[] { i }, 0, 1, Delivery.ReliableOrdered);
                network.Advance(0.005);
                server.Poll(se);
            }

            for (int i = 0; i < 400; i++)
            {
                network.Advance(0.01);
                server.Poll(se);
            }

            Assert.That(se.Data.Count, Is.EqualTo(100));
            for (int i = 0; i < 100; i++)
            {
                Assert.That(se.Data[i], Is.EqualTo(i));
            }
        }

        [Test]
        public void SequencedMessagesDropLostAndOutOfOrderPackets()
        {
            var (network, server, client, se, ce) = Connect(NetworkConditions.Of(40, 60, 0.1));
            for (byte i = 0; i < 200; i++)
            {
                client.Send(new[] { i }, 0, 1, Delivery.Sequenced);
                network.Advance(0.005);
                server.Poll(se);
            }

            network.Advance(1);
            server.Poll(se);
            Assert.That(se.Data.Count, Is.InRange(40, 185), "10% lost, and heavy jitter reorders many more, which are dropped");
            for (int i = 1; i < se.Data.Count; i++)
            {
                Assert.That(se.Data[i], Is.GreaterThan(se.Data[i - 1]), "never older than what was already delivered");
            }
        }

        [Test]
        public void SeveredLinksTimeOutOnBothSidesAndDisconnectsCarryReasons()
        {
            var (network, server, client, se, ce) = Connect(NetworkConditions.Of(20));
            network.Sever(client);
            network.Advance(network.DisconnectTimeoutSeconds + 0.1);
            server.Poll(se);
            client.Poll(ce);
            Assert.That(se.Disconnected, Has.Exactly(1).Matches<(int, DisconnectCode Code)>(d => d.Code == DisconnectCode.Timeout));
            Assert.That(ce.Disconnected, Has.Exactly(1).Matches<(int, DisconnectCode Code)>(d => d.Code == DisconnectCode.Timeout));

            client.Connect("sim", 1000);
            for (int i = 0; i < 20; i++)
            {
                network.Advance(0.01);
                server.Poll(se);
                client.Poll(ce);
            }

            server.Disconnect(se.LastConnection, DisconnectCode.Kicked, "bye");
            network.Advance(0.1);
            client.Poll(ce);
            Assert.That(ce.Disconnected[^1].Code, Is.EqualTo(DisconnectCode.Kicked));
        }

        [Test]
        public void ConnectingToNothingFails()
        {
            var network = new InMemoryNetwork();
            var client = network.CreateClient();
            var ce = new Recorder();
            client.Connect("sim", 4242);
            network.Advance(network.DisconnectTimeoutSeconds + 0.1);
            client.Poll(ce);
            Assert.That(ce.Disconnected[0].Code, Is.EqualTo(DisconnectCode.ConnectionFailed));
        }
    }
}
