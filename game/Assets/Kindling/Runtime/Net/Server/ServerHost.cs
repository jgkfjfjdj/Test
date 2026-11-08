using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Kindling.Core.Diagnostics;
using Kindling.Net.Protocol;
using Kindling.Net.Transport;
using Kindling.Simulation.Collision;
using Kindling.Simulation.Movement;
using Kindling.Simulation.World;
using Kindling.Core.Serialization;

namespace Kindling.Net.Server
{
    /// <summary>
    /// The authoritative game server for the M1 movement slice. Call <see cref="Tick"/> at the tick rate.
    /// Each tick: receive messages, consume exactly one input per player (repeating the last one, without
    /// jump, when none arrived), simulate, then send each connected player a snapshot of the players
    /// around them. Engine-independent: runs in the Unity dedicated server, the .NET dev server and tests.
    /// </summary>
    public sealed class ServerHost : ITransportEvents, IDisposable
    {
        const string LogCategory = "Server";
        const uint MaxInputLead = 1024;

        readonly IServerTransport m_Transport;
        readonly ServerConfig m_Config;
        readonly IJoinAuthenticator m_Authenticator;
        readonly MovementSettings m_Movement = new MovementSettings();
        readonly CollisionWorld m_World;
        readonly List<ServerPlayer> m_Players = new List<ServerPlayer>();
        readonly Dictionary<int, Connection> m_Connections = new Dictionary<int, Connection>();
        readonly List<int> m_Scratch = new List<int>();
        readonly List<PlayerInput> m_InputScratch = new List<PlayerInput>(InputsMessage.MaxInputs);
        readonly List<EntitySnapshot> m_Entities = new List<EntitySnapshot>();
        readonly List<PlayerInfo> m_InfoScratch = new List<PlayerInfo>();
        readonly PacketWriter m_Writer = new PacketWriter(2048);
        readonly RandomNumberGenerator m_Rng = RandomNumberGenerator.Create();
        readonly Stopwatch m_Stopwatch = new Stopwatch();
        readonly float m_Dt;
        ushort m_NextId = 1;
        bool m_Started;

        sealed class Connection
        {
            public int Id;
            public long ConnectedAtTick;
            public ServerPlayer Player;
        }

        public ServerHost(IServerTransport transport, ServerConfig config, IJoinAuthenticator authenticator = null)
        {
            m_Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            m_Config = config ?? new ServerConfig();
            m_Authenticator = authenticator;
            Layout = GreyboxLayout.Generate(m_Config.MapSeed);
            m_World = Layout.BuildCollisionWorld();
            m_Dt = 1f / m_Config.TickRate;
        }

        public GreyboxLayout Layout { get; }

        public ServerConfig Config => m_Config;

        public ServerStats Stats { get; } = new ServerStats();

        /// <summary>Ticks simulated so far.</summary>
        public uint CurrentTick { get; private set; }

        public IReadOnlyList<ServerPlayer> Players => m_Players;

        public bool Start()
        {
            if (m_Started)
            {
                return true;
            }

            m_Started = m_Transport.Start(m_Config.Port);
            if (m_Started)
            {
                Log.Info(LogCategory, $"Listening on port {m_Config.Port} ({m_Config.TickRate} Hz, max {m_Config.MaxPlayers} players, map seed {m_Config.MapSeed}).");
            }
            else
            {
                Log.Error(LogCategory, $"Could not listen on port {m_Config.Port}.");
            }

            return m_Started;
        }

        public ServerPlayer FindPlayer(ushort id)
        {
            foreach (var p in m_Players)
            {
                if (p.Id == id)
                {
                    return p;
                }
            }

            return null;
        }

        /// <summary>Moves a player (admin teleport, respawn). Their client snaps to the new position.</summary>
        public bool TeleportPlayer(ushort id, System.Numerics.Vector3 feet)
        {
            ServerPlayer player = FindPlayer(id);
            if (player == null)
            {
                return false;
            }

            player.StateField.Position = feet;
            player.StateField.Velocity = default;
            player.StateField.Grounded = false;
            return true;
        }

        public void Tick()
        {
            m_Stopwatch.Restart();
            m_Transport.Poll(this);
            ExpireConnectionsAndPlayers();

            foreach (ServerPlayer player in m_Players)
            {
                SimulatePlayer(player);
            }

            CurrentTick++;
            SendSnapshots();
            m_Transport.Flush();

            m_Stopwatch.Stop();
            double ms = m_Stopwatch.Elapsed.TotalMilliseconds;
            Stats.Tick = CurrentTick;
            Stats.Players = m_Players.Count;
            Stats.Connections = m_Connections.Count;
            Stats.LastTickMs = ms;
            Stats.AverageTickMs = Stats.AverageTickMs <= 0 ? ms : Stats.AverageTickMs + (ms - Stats.AverageTickMs) * 0.05;
            Stats.MaxTickMs = Math.Max(Stats.MaxTickMs, ms);
        }

        public void Shutdown()
        {
            foreach (int id in new List<int>(m_Connections.Keys))
            {
                m_Transport.Disconnect(id, DisconnectCode.ServerShutdown, "Server is shutting down.");
            }

            m_Connections.Clear();
            m_Transport.Flush();
            m_Transport.Stop();
            m_Started = false;
        }

        public void Dispose()
        {
            Shutdown();
            m_Rng.Dispose();
        }

        // ---------------------------------------------------------------- simulation

        void SimulatePlayer(ServerPlayer player)
        {
            PlayerInput input;
            if (player.Attached && player.PendingInputs.Count > 0)
            {
                input = player.PendingInputs[0];
                player.PendingInputs.RemoveAt(0);
                player.LastProcessedInput = input.Sequence;
                player.LastInput = input;
            }
            else
            {
                // No input for this tick: keep moving as before (hides a late or lost packet), but never
                // repeat a jump. Away and detached players stand still.
                input = player.LastInput;
                input.Buttons &= ~InputButtons.Jump;
                if (!player.Attached || player.Away)
                {
                    input.Move = default;
                    input.Buttons &= InputButtons.Crouch;
                }
                else
                {
                    player.StarvedTicks++;
                    Stats.StarvedTicks++;
                }
            }

            CharacterMotor.Simulate(ref player.StateField, input, m_Movement, m_World, m_Scratch, m_Dt);
        }

        void SendSnapshots()
        {
            m_Entities.Clear();
            foreach (ServerPlayer p in m_Players)
            {
                PlayerState s = p.StateField;
                var flags = EntityFlags.None;
                if (s.Grounded) flags |= EntityFlags.Grounded;
                if (s.Crouched) flags |= EntityFlags.Crouched;
                if (s.Sprinting) flags |= EntityFlags.Sprinting;
                if (p.Away || !p.Attached) flags |= EntityFlags.Away;
                m_Entities.Add(new EntitySnapshot { Id = p.Id, Position = s.Position, Yaw = s.Yaw, Pitch = s.Pitch, Flags = flags });
            }

            float near2 = m_Config.NearRadius * m_Config.NearRadius;
            float far2 = m_Config.InterestRadius * m_Config.InterestRadius;
            int interval = Math.Max(1, m_Config.FarUpdateInterval);

            for (int o = 0; o < m_Players.Count; o++)
            {
                ServerPlayer observer = m_Players[o];
                if (!observer.Attached)
                {
                    continue;
                }

                int budget = Math.Min(NetProtocol.SnapshotBudgetBytes, m_Transport.GetMaxSequencedPayload(observer.ConnectionId));
                if (budget < NetProtocol.MinSnapshotBudgetBytes)
                {
                    continue; // connection unknown to the transport (closing)
                }

                var origin = observer.StateField.Position;
                byte depth = (byte)Math.Min(255, observer.PendingInputs.Count);
                m_Writer.Reset();
                int countOffset = SnapshotMessage.Begin(m_Writer, CurrentTick, depth, true, observer.LastProcessedInput, observer.StateField);
                int count = 0;

                for (int e = 0; e < m_Entities.Count; e++)
                {
                    EntitySnapshot entity = m_Entities[e];
                    if (entity.Id == observer.Id)
                    {
                        continue;
                    }

                    float dx = entity.Position.X - origin.X;
                    float dz = entity.Position.Z - origin.Z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 > far2 || (d2 > near2 && (CurrentTick + entity.Id) % interval != 0))
                    {
                        continue;
                    }

                    if (count == 255 || m_Writer.Length + EntitySnapshot.MaxEncodedSize > budget)
                    {
                        SnapshotMessage.SetEntityCount(m_Writer, countOffset, count);
                        SendSnapshotPart(observer);
                        m_Writer.Reset();
                        countOffset = SnapshotMessage.Begin(m_Writer, CurrentTick, depth, false, 0, default);
                        count = 0;
                    }

                    entity.Write(m_Writer);
                    count++;
                }

                SnapshotMessage.SetEntityCount(m_Writer, countOffset, count);
                SendSnapshotPart(observer);
            }
        }

        void SendSnapshotPart(ServerPlayer observer)
        {
            m_Transport.Send(observer.ConnectionId, m_Writer.Buffer, 0, m_Writer.Length, Delivery.Sequenced);
            Stats.SnapshotPackets++;
            Stats.SnapshotBytes += m_Writer.Length;
            Stats.MaxSnapshotBytes = Math.Max(Stats.MaxSnapshotBytes, m_Writer.Length);
        }

        // ---------------------------------------------------------------- connections

        void ExpireConnectionsAndPlayers()
        {
            long handshakeTicks = (long)Math.Ceiling(m_Config.HandshakeTimeoutSeconds * m_Config.TickRate);
            List<int> expired = null;
            foreach (var pair in m_Connections)
            {
                if (pair.Value.Player == null && CurrentTick - pair.Value.ConnectedAtTick > handshakeTicks)
                {
                    (expired ??= new List<int>()).Add(pair.Key);
                }
            }

            if (expired != null)
            {
                foreach (int id in expired)
                {
                    DropConnection(id, DisconnectCode.HandshakeTimeout, "No hello received.");
                }
            }

            long graceTicks = (long)Math.Ceiling(m_Config.ResumeGraceSeconds * m_Config.TickRate);
            for (int i = m_Players.Count - 1; i >= 0; i--)
            {
                ServerPlayer p = m_Players[i];
                if (!p.Attached && CurrentTick - p.DetachedAtTick > graceTicks)
                {
                    Log.Info(LogCategory, $"Player {p.Id} ({p.Name}) resume grace expired.");
                    RemovePlayer(p);
                }
            }
        }

        void ITransportEvents.OnConnected(int connectionId)
        {
            m_Connections[connectionId] = new Connection { Id = connectionId, ConnectedAtTick = CurrentTick };
        }

        void ITransportEvents.OnDisconnected(int connectionId, DisconnectCode code, string message)
        {
            if (!m_Connections.TryGetValue(connectionId, out Connection c))
            {
                return;
            }

            m_Connections.Remove(connectionId);
            if (c.Player != null && c.Player.ConnectionId == connectionId)
            {
                if (code == DisconnectCode.ClientQuit)
                {
                    Log.Info(LogCategory, $"Player {c.Player.Id} ({c.Player.Name}) left.");
                    RemovePlayer(c.Player);
                }
                else
                {
                    Log.Info(LogCategory, $"Player {c.Player.Id} ({c.Player.Name}) dropped ({code}); keeping them for {m_Config.ResumeGraceSeconds:0} s.");
                    Detach(c.Player);
                }
            }
        }

        void ITransportEvents.OnData(int connectionId, byte[] data, int offset, int count)
        {
            if (!m_Connections.TryGetValue(connectionId, out Connection c))
            {
                return;
            }

            try
            {
                var r = new PacketReader(data, offset, count);
                var type = (MessageType)r.ReadByte();
                switch (type)
                {
                    case MessageType.Hello when c.Player == null:
                        HandleHello(c, HelloMessage.Read(ref r));
                        break;
                    case MessageType.Inputs when c.Player != null:
                        HandleInputs(c.Player, ref r);
                        break;
                    case MessageType.Background when c.Player != null:
                        c.Player.Away = r.ReadBool();
                        break;
                    default:
                        throw new ProtocolException($"Unexpected message {type}.");
                }
            }
            catch (ProtocolException e)
            {
                Stats.ProtocolErrors++;
                Log.Warning(LogCategory, $"Connection {connectionId}: {e.Message}");
                DropConnection(connectionId, DisconnectCode.ProtocolError, e.Message);
            }
        }

        void HandleHello(Connection c, HelloMessage hello)
        {
            if (hello.ProtocolVersion != NetProtocol.Version)
            {
                DropConnection(c.Id, DisconnectCode.ProtocolMismatch, $"Server protocol is {NetProtocol.Version}, client is {hello.ProtocolVersion}. Update the game.");
                return;
            }

            string name;
            if (m_Config.AllowDevAuth)
            {
                name = hello.DisplayName;
            }
            else if (m_Authenticator == null || !m_Authenticator.TryAuthenticate(hello, out name, out string reason))
            {
                DropConnection(c.Id, DisconnectCode.AuthFailed, "Join ticket rejected.");
                return;
            }

            ServerPlayer player = FindByResumeToken(hello.ResumeToken);
            bool resumed = player != null;
            if (resumed)
            {
                if (player.Attached)
                {
                    int old = player.ConnectionId;
                    m_Connections.Remove(old);
                    m_Transport.Disconnect(old, DisconnectCode.Replaced, "Connected from somewhere else.");
                }

                player.PendingInputs.Clear();
                player.Away = false;
            }
            else
            {
                if (m_Players.Count >= m_Config.MaxPlayers)
                {
                    DropConnection(c.Id, DisconnectCode.ServerFull, "Server is full.");
                    return;
                }

                ushort id = AllocateId();
                var token = new byte[NetProtocol.ResumeTokenLength];
                m_Rng.GetBytes(token);
                player = new ServerPlayer(id, SanitizeName(name, id), token);
                var spawns = Layout.SpawnPoints;
                player.StateField = PlayerState.SpawnAt(spawns[(id - 1) % spawns.Count], 0f);
                m_Players.Add(player);
            }

            player.ConnectionId = c.Id;
            c.Player = player;

            m_Writer.Reset();
            new WelcomeMessage
            {
                PlayerId = player.Id,
                ServerTick = CurrentTick,
                TickRate = (byte)m_Config.TickRate,
                MapSeed = m_Config.MapSeed,
                ResumeToken = player.ResumeToken,
                Resumed = resumed,
                LastInputSequence = player.LastProcessedInput,
                State = player.StateField,
            }.Write(m_Writer);
            m_Transport.Send(c.Id, m_Writer.Buffer, 0, m_Writer.Length, Delivery.ReliableOrdered);

            SendAllPlayerInfo(c.Id);
            if (!resumed)
            {
                m_InfoScratch.Clear();
                m_InfoScratch.Add(new PlayerInfo { Id = player.Id, DisplayName = player.Name });
                m_Writer.Reset();
                PlayerInfoMessage.Write(m_Writer, m_InfoScratch, 0, 1);
                Broadcast(Delivery.ReliableOrdered, exceptPlayer: player);
            }

            Log.Info(LogCategory, $"Player {player.Id} ({player.Name}) {(resumed ? "resumed" : "joined")} on connection {c.Id}.");
        }

        void HandleInputs(ServerPlayer player, ref PacketReader r)
        {
            InputsMessage.Read(ref r, m_InputScratch);

            // Late inputs still count, but don't end "away": only Background(false) or a resume does,
            // otherwise jittered packets arriving after Background(true) would let the player run on.
            uint last = player.LastProcessedInput;
            foreach (PlayerInput input in m_InputScratch)
            {
                uint seq = input.Sequence;
                if (seq <= last || seq > last + MaxInputLead)
                {
                    continue;
                }

                List<PlayerInput> queue = player.PendingInputs;
                int index = queue.Count;
                bool duplicate = false;
                for (int i = 0; i < queue.Count; i++)
                {
                    if (queue[i].Sequence == seq)
                    {
                        duplicate = true;
                        break;
                    }

                    if (queue[i].Sequence > seq)
                    {
                        index = i;
                        break;
                    }
                }

                if (!duplicate)
                {
                    queue.Insert(index, input);
                }
            }

            while (player.PendingInputs.Count > m_Config.InputQueueCapacity)
            {
                player.PendingInputs.RemoveAt(0);
                player.DroppedInputs++;
                Stats.DroppedInputs++;
            }
        }

        void SendAllPlayerInfo(int connectionId)
        {
            m_InfoScratch.Clear();
            foreach (ServerPlayer p in m_Players)
            {
                m_InfoScratch.Add(new PlayerInfo { Id = p.Id, DisplayName = p.Name });
            }

            const int batch = 20;
            for (int start = 0; start < m_InfoScratch.Count; start += batch)
            {
                m_Writer.Reset();
                PlayerInfoMessage.Write(m_Writer, m_InfoScratch, start, Math.Min(batch, m_InfoScratch.Count - start));
                m_Transport.Send(connectionId, m_Writer.Buffer, 0, m_Writer.Length, Delivery.ReliableOrdered);
            }
        }

        void Broadcast(Delivery delivery, ServerPlayer exceptPlayer)
        {
            foreach (ServerPlayer p in m_Players)
            {
                if (p.Attached && p != exceptPlayer)
                {
                    m_Transport.Send(p.ConnectionId, m_Writer.Buffer, 0, m_Writer.Length, delivery);
                }
            }
        }

        void Detach(ServerPlayer player)
        {
            player.ConnectionId = -1;
            player.DetachedAtTick = CurrentTick;
            player.PendingInputs.Clear();
        }

        void RemovePlayer(ServerPlayer player)
        {
            if (player.Attached)
            {
                m_Connections.Remove(player.ConnectionId);
                player.ConnectionId = -1;
            }

            m_Players.Remove(player);
            m_Writer.Reset();
            m_Writer.WriteByte((byte)MessageType.PlayerLeft);
            m_Writer.WriteUShort(player.Id);
            Broadcast(Delivery.ReliableOrdered, exceptPlayer: null);
        }

        void DropConnection(int connectionId, DisconnectCode code, string message)
        {
            if (m_Connections.TryGetValue(connectionId, out Connection c) && c.Player != null && c.Player.ConnectionId == connectionId)
            {
                Detach(c.Player);
            }

            m_Connections.Remove(connectionId);
            m_Transport.Disconnect(connectionId, code, message);
        }

        ServerPlayer FindByResumeToken(byte[] token)
        {
            if (token == null || token.Length != NetProtocol.ResumeTokenLength)
            {
                return null;
            }

            foreach (ServerPlayer p in m_Players)
            {
                int diff = 0;
                for (int i = 0; i < token.Length; i++)
                {
                    diff |= token[i] ^ p.ResumeToken[i];
                }

                if (diff == 0)
                {
                    return p;
                }
            }

            return null;
        }

        ushort AllocateId()
        {
            while (true)
            {
                ushort id = m_NextId;
                m_NextId = (ushort)(m_NextId == ushort.MaxValue ? 1 : m_NextId + 1);
                if (FindPlayer(id) == null)
                {
                    return id;
                }
            }
        }

        static string SanitizeName(string name, ushort id)
        {
            var sb = new StringBuilder();
            foreach (char ch in name ?? string.Empty)
            {
                if (!char.IsControl(ch) && (sb.Length > 0 || !char.IsWhiteSpace(ch)))
                {
                    sb.Append(ch);
                }

                if (sb.Length >= 24)
                {
                    break;
                }
            }

            string result = sb.ToString().TrimEnd();
            return result.Length == 0 ? $"Player {id}" : result;
        }
    }
}
