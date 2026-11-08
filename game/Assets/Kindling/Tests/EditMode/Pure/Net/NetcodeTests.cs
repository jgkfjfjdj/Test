using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Kindling.Net.Client;
using Kindling.Net.Protocol;
using Kindling.Net.Server;
using Kindling.Net.Transport;
using Kindling.Simulation.Movement;
using NUnit.Framework;

namespace Kindling.Tests.Pure.Net
{
    public class NetcodeTests
    {
        static readonly Vector2 Forward = new Vector2(0, 1);

        static ClientSession Join(NetHarness h, string name, NetworkConditions conditions = null, ClientSettings settings = null)
        {
            ClientSession c = h.AddClient(name, conditions, settings);
            h.RunUntil(() => c.Status == ClientStatus.Connected, 5);
            Assert.That(c.Status, Is.EqualTo(ClientStatus.Connected), $"{name} connected");
            return c;
        }

        static void Wander(NetHarness h, ClientSession c, double seconds)
        {
            int frame = 0;
            h.Run(seconds, () =>
            {
                frame++;
                var held = (frame / 600) % 2 == 0 ? InputButtons.Sprint : InputButtons.None;
                if (frame % 240 == 0)
                {
                    c.Press(InputButtons.Jump);
                }

                c.SetInput(new Vector2((float)Math.Sin(frame * 0.01), 1), frame * 0.5f, 0, held);
            });
            c.SetInput(Vector2.Zero, c.PredictedState.Yaw, 0, InputButtons.None);
        }

        [TestCase(0, 0, 0.0)]
        [TestCase(80, 20, 0.03)]
        [TestCase(150, 50, 0.10)]
        public void PredictionMatchesTheServer(double latencyMs, double jitterMs, double loss)
        {
            var h = new NetHarness();
            ClientSession c = Join(h, "a", NetworkConditions.Of(latencyMs, jitterMs, loss));
            h.Run(1);
            int before = c.Stats.Corrections;

            Wander(h, c, 10);
            h.Run(2);

            ServerPlayer p = h.Server.FindPlayer(c.PlayerId);
            Assert.That(Vector3.Distance(p.State.Position, h.Server.Layout.SpawnPoints[0]), Is.GreaterThan(10f), "actually moved (in curves)");
            Assert.That(Vector3.Distance(p.State.Position, c.PredictedState.Position), Is.LessThan(0.01f), "settled on the server's position");
            Assert.That(c.Stats.Corrections - before, Is.LessThanOrEqualTo(3), "corrections while moving");
            Assert.That(c.Stats.MaxCorrection, Is.LessThan(0.5f));
            Assert.That(c.Stats.ServerInputBuffer, Is.InRange(1, 4), "time dilation keeps a small input buffer");
        }

        [Test]
        public void ServerTeleportCorrectsThePrediction()
        {
            var h = new NetHarness();
            ClientSession c = Join(h, "a", NetworkConditions.Of(30));
            h.Run(1);
            Vector3 target = h.FreeSpotNear(new Vector3(60, 0, 60));
            h.Server.TeleportPlayer(c.PlayerId, target);
            h.Run(1);
            Assert.That(Vector3.Distance(c.PredictedState.Position, h.Server.FindPlayer(c.PlayerId).State.Position), Is.LessThan(0.01f));
            Assert.That(c.Stats.LastCorrection, Is.GreaterThan(50f));
            Assert.That(c.CorrectionOffset, Is.EqualTo(Vector3.Zero), "large corrections snap instead of sliding");
        }

        [Test]
        public void RemotePlayersMoveSmoothlyAndCloseBehindTheServer()
        {
            var h = new NetHarness();
            var conditions = NetworkConditions.Of(60, 15, 0.02);
            ClientSession a = Join(h, "watcher", conditions);
            ClientSession b = Join(h, "walker", conditions);
            h.Run(1);
            Assert.That(a.GetPlayerName(b.PlayerId), Is.EqualTo("walker"));

            var remotes = new List<RemotePlayer>();
            float previousZ = float.NegativeInfinity;
            float maxStep = 0;
            float maxLag = 0;
            int samples = 0;
            h.Run(4, () =>
            {
                b.SetInput(Forward, 0f, 0f, InputButtons.None); // walk +Z at 4 m/s
                a.GetRemotePlayers(remotes);
                RemotePlayer seen = remotes.Single(r => r.Id == b.PlayerId);
                if (h.Time > 3)
                {
                    Assert.That(seen.Position.Z, Is.GreaterThanOrEqualTo(previousZ - 1e-4f), "never jumps backwards");
                    maxStep = Math.Max(maxStep, seen.Position.Z - previousZ);
                    maxLag = Math.Max(maxLag, h.Server.FindPlayer(b.PlayerId).State.Position.Z - seen.Position.Z);
                    samples++;
                }

                previousZ = seen.Position.Z;
            });

            Assert.That(samples, Is.GreaterThan(100));
            Assert.That(maxStep, Is.LessThan(0.1f), "no visible pops (4 m/s at 120 fps is 0.033 m per frame)");
            Assert.That(maxLag, Is.LessThan(4f * 0.4f), "drawn at most ~0.4 s behind the server");
        }

        [Test]
        public void ClientsCannotMoveFasterBySendingInputsFaster()
        {
            var h = new NetHarness();
            var transport = h.Network.CreateClient(NetworkConditions.Of(20));
            var cheater = new RawClient(h, transport);
            cheater.Join();
            Vector3 start = h.Server.Players[0].State.Position;
            uint sequence = 0;

            // Four sprinting inputs per server tick instead of one.
            for (int tick = 0; tick < 90; tick++)
            {
                for (int k = 0; k < 4; k++)
                {
                    sequence++;
                    cheater.SendInput(new PlayerInput { Sequence = sequence, Move = Forward, Buttons = InputButtons.Sprint });
                }

                h.Run(1.0 / 30.0);
            }

            ServerPlayer p = h.Server.Players[0];
            float distance = Vector3.Distance(start, p.State.Position);
            Assert.That(distance, Is.LessThanOrEqualTo(6.5f * 3f + 0.5f), "3 s at sprint speed at most");
            Assert.That(p.DroppedInputs, Is.GreaterThan(100));
        }

        [Test]
        public void DroppedPlayersCanResumeWithinTheGracePeriodThenAreRemoved()
        {
            var h = new NetHarness(new ServerConfig { ResumeGraceSeconds = 15 });
            ClientSession a = Join(h, "flaky", NetworkConditions.Of(40));
            ClientSession b = Join(h, "observer", NetworkConditions.Of(40));
            h.Run(0.5);
            h.Run(2, () => a.SetInput(Forward, 90f, 0f, InputButtons.None));
            a.SetInput(Vector2.Zero, 90f, 0f, InputButtons.None);
            h.Run(0.5);
            ushort id = a.PlayerId;
            Vector3 position = h.Server.FindPlayer(id).State.Position;

            h.Network.Sever(h.TransportOf(a));
            h.Run(6);
            Assert.That(a.Status, Is.EqualTo(ClientStatus.Disconnected));
            Assert.That(a.LastDisconnectCode, Is.EqualTo(DisconnectCode.Timeout));
            Assert.That(a.CanResume, Is.True);
            ServerPlayer waiting = h.Server.FindPlayer(id);
            Assert.That(waiting, Is.Not.Null, "kept for the grace period");
            Assert.That(waiting.Attached, Is.False);
            var remotes = new List<RemotePlayer>();
            b.GetRemotePlayers(remotes);
            Assert.That(remotes.Single(r => r.Id == id).Flags & EntityFlags.Away, Is.EqualTo(EntityFlags.Away));

            a.Connect("sim", NetHarness.Port);
            h.RunUntil(() => a.Status == ClientStatus.Connected, 5);
            Assert.That(a.LastJoinWasResume, Is.True);
            Assert.That(a.PlayerId, Is.EqualTo(id));
            Assert.That(Vector3.Distance(a.PredictedState.Position, position), Is.LessThan(0.01f));
            h.Run(1, () => a.SetInput(Forward, 0f, 0f, InputButtons.None));
            Assert.That(h.Server.FindPlayer(id).State.Position.Z, Is.GreaterThan(position.Z + 2f), "inputs flow again after resuming");

            h.Network.Sever(h.TransportOf(a));
            h.Run(15 + 6);
            Assert.That(h.Server.FindPlayer(id), Is.Null, "removed after the grace period");
            b.GetRemotePlayers(remotes);
            Assert.That(remotes.Any(r => r.Id == id), Is.False);
            Assert.That(b.GetPlayerName(id), Is.Null);
        }

        [Test]
        public void CleanQuitRemovesThePlayerImmediately()
        {
            var h = new NetHarness();
            ClientSession a = Join(h, "a");
            ClientSession b = Join(h, "b");
            h.Run(0.5);
            a.Disconnect();
            h.Run(0.2);
            Assert.That(h.Server.Players.Count, Is.EqualTo(1));
            var remotes = new List<RemotePlayer>();
            b.GetRemotePlayers(remotes);
            Assert.That(remotes, Is.Empty);
            Assert.That(a.CanResume, Is.False);
        }

        [Test]
        public void MismatchedProtocolIsRejectedWithAReason()
        {
            var h = new NetHarness();
            ClientSession c = h.AddClient("old", settings: new ClientSettings { ProtocolVersion = 999 });
            h.Run(1);
            Assert.That(c.Status, Is.EqualTo(ClientStatus.Disconnected));
            Assert.That(c.LastDisconnectCode, Is.EqualTo(DisconnectCode.ProtocolMismatch));
            Assert.That(c.LastDisconnectMessage, Does.Contain("Update"));
            Assert.That(h.Server.Players, Is.Empty);
        }

        [Test]
        public void FullServerRejectsNewPlayers()
        {
            var h = new NetHarness(new ServerConfig { MaxPlayers = 1 });
            Join(h, "first");
            ClientSession second = h.AddClient("second");
            h.Run(1);
            Assert.That(second.LastDisconnectCode, Is.EqualTo(DisconnectCode.ServerFull));
            Assert.That(h.Server.Players.Count, Is.EqualTo(1));
        }

        [Test]
        public void DevAuthCanBeTurnedOff()
        {
            var h = new NetHarness(new ServerConfig { AllowDevAuth = false });
            ClientSession c = h.AddClient("nobody");
            h.Run(1);
            Assert.That(c.LastDisconnectCode, Is.EqualTo(DisconnectCode.AuthFailed));
        }

        [Test]
        public void SilentAndMalformedConnectionsAreDropped()
        {
            var h = new NetHarness();
            var silent = new RawClient(h, h.Network.CreateClient());
            silent.ConnectOnly();
            var garbage = new RawClient(h, h.Network.CreateClient());
            garbage.Join();
            garbage.SendRaw(new byte[] { (byte)MessageType.Inputs, 1, 2 }); // truncated
            h.Run(h.Server.Config.HandshakeTimeoutSeconds + 1);
            silent.Poll();
            garbage.Poll();
            Assert.That(silent.DisconnectCode, Is.EqualTo(DisconnectCode.HandshakeTimeout));
            Assert.That(garbage.DisconnectCode, Is.EqualTo(DisconnectCode.ProtocolError));
            Assert.That(h.Server.Stats.ProtocolErrors, Is.EqualTo(1));
        }

        [Test]
        public void InterestManagementSendsFarPlayersLessOftenAndDistantOnesNotAtAll()
        {
            var h = new NetHarness();
            ClientSession a = Join(h, "a");
            ClientSession mid = Join(h, "mid");
            ClientSession far = Join(h, "far");
            h.Server.TeleportPlayer(a.PlayerId, h.FreeSpotNear(new Vector3(-120, 0, 0)));
            h.Server.TeleportPlayer(mid.PlayerId, h.FreeSpotNear(new Vector3(30, 0, 0)));   // ~150 m from a
            h.Server.TeleportPlayer(far.PlayerId, h.FreeSpotNear(new Vector3(200, 0, 0)));  // ~320 m from a
            h.Run(3);

            var remotes = new List<RemotePlayer>();
            var ticksSeen = new SortedSet<uint>();
            h.Run(1, () =>
            {
                a.GetRemotePlayers(remotes);
                Assert.That(remotes.Any(r => r.Id == far.PlayerId), Is.False, "beyond the interest radius");
                ticksSeen.Add(remotes.Single(r => r.Id == mid.PlayerId).NewestTick);
            });

            uint[] ticks = ticksSeen.ToArray();
            Assert.That(ticks.Length, Is.InRange(9, 12), "about 10 updates per second (every 3rd tick)");
            for (int i = 1; i < ticks.Length; i++)
            {
                Assert.That(ticks[i] - ticks[i - 1], Is.EqualTo(3u));
            }

            mid.GetRemotePlayers(remotes);
            Assert.That(remotes.Select(r => r.Id), Is.EquivalentTo(new[] { a.PlayerId, far.PlayerId }), "mid is within range of both");
        }

        [Test]
        public void LargeSnapshotsAreSplitUnderTheBudget()
        {
            var h = new NetHarness(new ServerConfig { MaxPlayers = 130 });
            for (int i = 0; i < 120; i++)
            {
                h.AddClient($"p{i}");
            }

            h.RunUntil(() => h.Clients.All(c => c.Status == ClientStatus.Connected), 5);
            h.Run(1);
            var remotes = new List<RemotePlayer>();
            h.Clients[0].GetRemotePlayers(remotes);
            Assert.That(remotes.Count, Is.EqualTo(119));
            Assert.That(h.Server.Stats.MaxSnapshotBytes, Is.LessThanOrEqualTo(NetProtocol.SnapshotBudgetBytes));
            Assert.That(h.Server.Stats.SnapshotPackets, Is.GreaterThan(h.Server.Stats.Tick * 120));
        }

        [TestCase(20, 0)]
        [TestCase(40, 80)] // late, reordered inputs arriving after the background notice must not end "away"
        public void BackgroundedPlayersStandStillWithoutStarvingTheirInputBuffer(double latencyMs, double jitterMs)
        {
            var h = new NetHarness();
            ClientSession a = Join(h, "a", NetworkConditions.Of(latencyMs, jitterMs));
            h.Run(1, () => a.SetInput(Forward, 0f, 0f, InputButtons.None));
            a.NotifyBackground(true);
            long starvedBefore = h.Server.FindPlayer(a.PlayerId).StarvedTicks;
            Vector3 at = h.Server.FindPlayer(a.PlayerId).State.Position;

            // The app is suspended: no updates at all for 3 s.
            for (int i = 0; i < 90; i++)
            {
                h.Network.Advance(1.0 / 30.0);
                h.Server.Tick();
            }

            ServerPlayer p = h.Server.FindPlayer(a.PlayerId);
            Assert.That(p.Away, Is.True);
            Assert.That(Vector3.Distance(at, p.State.Position), Is.LessThan(1f), "runs its buffered inputs, decelerates and stops instead of running on (3 s would be 12 m)");
            Assert.That(p.StarvedTicks - starvedBefore, Is.LessThanOrEqualTo(4));

            a.NotifyBackground(false);
            h.Run(1, () => a.SetInput(Forward, 0f, 0f, InputButtons.None));
            Assert.That(h.Server.FindPlayer(a.PlayerId).Away, Is.False);
            Assert.That(Vector3.Distance(a.PredictedState.Position, h.Server.FindPlayer(a.PlayerId).State.Position), Is.LessThan(0.6f));
        }

        /// <summary>A hand-driven client for misbehaving on purpose.</summary>
        sealed class RawClient : ITransportEvents
        {
            readonly NetHarness m_Harness;
            readonly InMemoryClientTransport m_Transport;
            readonly PacketWriter m_Writer = new PacketWriter();
            bool m_Connected;
            bool m_Welcomed;

            public RawClient(NetHarness harness, InMemoryClientTransport transport)
            {
                m_Harness = harness;
                m_Transport = transport;
                harness.Network.Advance(0);
            }

            public DisconnectCode DisconnectCode { get; private set; } = DisconnectCode.None;

            public void ConnectOnly()
            {
                m_Transport.Connect("sim", NetHarness.Port);
                Pump(() => m_Connected, 2);
            }

            public void Join()
            {
                ConnectOnly();
                m_Writer.Reset();
                new HelloMessage { ProtocolVersion = NetProtocol.Version, DisplayName = "raw" }.Write(m_Writer);
                m_Transport.Send(m_Writer.Buffer, 0, m_Writer.Length, Delivery.ReliableOrdered);
                Pump(() => m_Welcomed, 2);
                Assert.That(m_Welcomed, Is.True);
            }

            public void SendInput(PlayerInput input)
            {
                m_Writer.Reset();
                InputsMessage.Write(m_Writer, new[] { input }, 1);
                m_Transport.Send(m_Writer.Buffer, 0, m_Writer.Length, Delivery.Sequenced);
            }

            public void SendRaw(byte[] data) => m_Transport.Send(data, 0, data.Length, Delivery.ReliableOrdered);

            public void Poll() => m_Transport.Poll(this);

            void Pump(Func<bool> done, double seconds)
            {
                double end = m_Harness.Time + seconds;
                while (!done() && m_Harness.Time < end)
                {
                    m_Harness.Run(NetHarness.FrameDt);
                    m_Transport.Poll(this);
                }
            }

            public void OnConnected(int connectionId) => m_Connected = true;

            public void OnDisconnected(int connectionId, DisconnectCode code, string message) => DisconnectCode = code;

            public void OnData(int connectionId, byte[] data, int offset, int count)
            {
                if (data[offset] == (byte)MessageType.Welcome)
                {
                    m_Welcomed = true;
                }
            }
        }
    }
}
