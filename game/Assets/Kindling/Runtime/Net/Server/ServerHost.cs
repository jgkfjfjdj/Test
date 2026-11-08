using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Kindling.Content;
using Kindling.Core.Diagnostics;
using Kindling.Core.Serialization;
using Kindling.Core.World;
using Kindling.Net.Protocol;
using Kindling.Net.Transport;
using Kindling.Simulation;
using Kindling.Simulation.Game;
using Kindling.Simulation.Items;
using Kindling.Simulation.Movement;
using Kindling.Simulation.Persistence;
using Kindling.Simulation.World;

namespace Kindling.Net.Server
{
    /// <summary>
    /// The authoritative game server. Call <see cref="Tick"/> at the tick rate. Each tick: receive
    /// messages, consume exactly one input per player (repeating the last one without jump when none
    /// arrived), simulate movement and swings, tick the <see cref="GameWorld"/>, then send each player
    /// their snapshot, chunk updates around them and their own state, containers and events.
    /// Engine-independent: runs in the Unity dedicated server, the .NET dev server and tests.
    /// </summary>
    public sealed class ServerHost : ITransportEvents, IDisposable
    {
        const string LogCategory = "Server";
        const uint MaxInputLead = 1024;
        const uint SwingFlagTicks = 9;

        readonly IServerTransport m_Transport;
        readonly ServerConfig m_Config;
        readonly IJoinAuthenticator m_Authenticator;
        readonly GameWorld m_World;
        readonly WorldPersistence m_Persistence;
        readonly List<ServerPlayer> m_Players = new List<ServerPlayer>();
        readonly Dictionary<int, Connection> m_Connections = new Dictionary<int, Connection>();
        readonly Dictionary<string, string> m_ResumeAccounts = new Dictionary<string, string>();
        readonly List<int> m_Scratch = new List<int>();
        readonly List<PlayerInput> m_InputScratch = new List<PlayerInput>(InputsMessage.MaxInputs);
        readonly List<EntitySnapshot> m_Entities = new List<EntitySnapshot>();
        readonly List<PlayerInfo> m_InfoScratch = new List<PlayerInfo>();
        readonly List<EntityChangeRecord> m_EntityChanges = new List<EntityChangeRecord>();
        readonly List<int> m_NodeChanges = new List<int>();
        readonly Dictionary<ChunkCoord, ChunkChanges> m_ChunkChanges = new Dictionary<ChunkCoord, ChunkChanges>();
        readonly List<ChunkCoord> m_ChunkScratch = new List<ChunkCoord>();
        readonly List<WorldEntity> m_EntityScratch = new List<WorldEntity>();
        readonly PacketWriter m_Writer = new PacketWriter(2048);
        readonly PacketWriter m_MapWriter = new PacketWriter(32 * 1024);
        readonly OwnerStateMessage m_OwnerState = new OwnerStateMessage();
        readonly RandomNumberGenerator m_Rng = RandomNumberGenerator.Create();
        readonly Stopwatch m_Stopwatch = new Stopwatch();
        readonly Stopwatch m_Section = new Stopwatch();
        readonly float m_Dt;
        ushort m_NextId = 1;
        bool m_Started;
        double m_NextSave;
        double m_NextCheckpoint;

        sealed class Connection
        {
            public int Id;
            public long ConnectedAtTick;
            public ServerPlayer Player;
        }

        sealed class ChunkChanges
        {
            public readonly HashSet<uint> Upserts = new HashSet<uint>();
            public readonly HashSet<uint> Removes = new HashSet<uint>();
            public readonly HashSet<int> Nodes = new HashSet<int>();
        }

        public ServerHost(IServerTransport transport, ServerConfig config, GameWorld world, WorldPersistence persistence = null, IJoinAuthenticator authenticator = null)
        {
            m_Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            m_Config = config ?? new ServerConfig();
            m_World = world ?? throw new ArgumentNullException(nameof(world));
            m_Persistence = persistence;
            m_Authenticator = authenticator;
            m_Dt = 1f / m_Config.TickRate;
            MapDataMessage.Write(m_MapWriter, world.Map.Static);
            m_NextSave = m_Config.SaveIntervalSeconds;
            m_NextCheckpoint = m_Config.CheckpointIntervalSeconds;
        }

        /// <summary>A fresh, unsaved world for this config's map seed (tests, offline play).</summary>
        public static GameWorld CreateWorld(ContentDatabase content, ServerConfig config, GameConfig game = null)
        {
            var world = new GameWorld(content, WorldMap.Generate(config.MapSeed, content), game ?? GameConfig.FromRules(content.Rules), (ulong)config.MapSeed * 7919UL + 1);
            world.PopulateFreshWorld();
            return world;
        }

        public GameWorld World => m_World;

        public ServerConfig Config => m_Config;

        public ServerStats Stats { get; } = new ServerStats();

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
                Log.Info(LogCategory, $"Listening on port {m_Config.Port} ({m_Config.TickRate} Hz, max {m_Config.MaxPlayers} players, map seed {m_Config.MapSeed}, {m_World.Characters.Count} saved characters).");
            }
            else
            {
                Log.Error(LogCategory, $"Could not listen on port {m_Config.Port}.");
            }

            return m_Started;
        }

        public ServerPlayer FindPlayer(ushort id)
        {
            foreach (ServerPlayer p in m_Players)
            {
                if (p.Id == id)
                {
                    return p;
                }
            }

            return null;
        }

        public ServerPlayer FindPlayer(Character c)
        {
            foreach (ServerPlayer p in m_Players)
            {
                if (p.Character == c)
                {
                    return p;
                }
            }

            return null;
        }

        /// <summary>Moves a player (admin teleport). Their client snaps to the new position.</summary>
        public bool TeleportPlayer(ushort id, System.Numerics.Vector3 feet)
        {
            ServerPlayer player = FindPlayer(id);
            if (player == null)
            {
                return false;
            }

            player.Character.Motion.Position = feet;
            player.Character.Motion.Velocity = default;
            player.Character.Motion.Grounded = false;
            return true;
        }

        public void Tick()
        {
            m_Stopwatch.Restart();
            m_Transport.Poll(this);
            ExpireConnections();

            foreach (ServerPlayer player in m_Players)
            {
                SimulatePlayer(player);
            }

            m_Section.Restart();
            m_World.Tick(m_Dt);
            Stats.LastWorldMs = m_Section.Elapsed.TotalMilliseconds;
            CurrentTick++;

            SendSnapshots();
            ReplicateChunks();
            SendOwnerData();
            if (CurrentTick % (uint)m_Config.TickRate == 0)
            {
                SendWorldState(-1);
            }

            SaveIfDue();
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

        /// <summary>Puts everyone to sleep, saves the whole world and disconnects.</summary>
        public void Shutdown()
        {
            foreach (ServerPlayer p in m_Players)
            {
                m_World.Leave(p.Character);
                if (p.Attached)
                {
                    m_Transport.Disconnect(p.ConnectionId, DisconnectCode.ServerShutdown, "Server is shutting down.");
                }
            }

            foreach (int id in m_Connections.Keys)
            {
                m_Transport.Disconnect(id, DisconnectCode.ServerShutdown, "Server is shutting down.");
            }

            m_Players.Clear();
            m_Connections.Clear();
            if (m_Persistence != null)
            {
                m_Persistence.Checkpoint();
                m_Persistence.Store.Flush();
            }

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
            if (player.PendingInputs.Count > 0)
            {
                input = player.PendingInputs[0];
                player.PendingInputs.RemoveAt(0);
                player.LastProcessedInput = input.Sequence;
                player.LastInput = input;
            }
            else
            {
                // No input for this tick: keep moving as before (hides a late or lost packet), but never
                // repeat a jump. Away players stand still.
                input = player.LastInput;
                input.Buttons &= ~InputButtons.Jump;
                if (player.Away)
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

            Character c = player.Character;
            if (c.Life == LifeState.Dead)
            {
                return;
            }

            input = LifeRules.Restrict(input, c.Life);
            CharacterMotor.Simulate(ref c.Motion, input, m_World.Movement, m_World.Collision, m_Scratch, m_Dt);
            if (c.IsAlive && input.Has(InputButtons.Attack) && m_World.Swing(c, input.Yaw, input.Pitch) != ActionError.Cooldown)
            {
                player.SwingUntilTick = CurrentTick + SwingFlagTicks;
            }
        }

        void SendSnapshots()
        {
            m_Entities.Clear();
            foreach (ServerPlayer p in m_Players)
            {
                Character c = p.Character;
                if (c.Life == LifeState.Dead)
                {
                    continue;
                }

                PlayerState s = c.Motion;
                var flags = EntityFlags.None;
                if (s.Grounded) flags |= EntityFlags.Grounded;
                if (s.Crouched) flags |= EntityFlags.Crouched;
                if (s.Sprinting) flags |= EntityFlags.Sprinting;
                if (p.Away) flags |= EntityFlags.Away;
                if (c.Life == LifeState.Downed) flags |= EntityFlags.Downed;
                if (CurrentTick < p.SwingUntilTick) flags |= EntityFlags.Swinging;
                ItemStack held = c.Inventory.ActiveItem;
                m_Entities.Add(new EntitySnapshot { Id = p.Id, Position = s.Position, Yaw = s.Yaw, Pitch = s.Pitch, Flags = flags, HeldItem = held.IsEmpty ? (ushort)0 : held.Def.NetId });
            }

            float near2 = m_Config.NearRadius * m_Config.NearRadius;
            float far2 = m_Config.InterestRadius * m_Config.InterestRadius;
            int interval = Math.Max(1, m_Config.FarUpdateInterval);

            foreach (ServerPlayer observer in m_Players)
            {
                int budget = Math.Min(NetProtocol.SnapshotBudgetBytes, m_Transport.GetMaxSequencedPayload(observer.ConnectionId));
                if (budget < NetProtocol.MinSnapshotBudgetBytes)
                {
                    continue;
                }

                var origin = observer.Character.Motion.Position;
                byte depth = (byte)Math.Min(255, observer.PendingInputs.Count);
                m_Writer.Reset();
                int countOffset = SnapshotMessage.Begin(m_Writer, CurrentTick, depth, true, observer.LastProcessedInput, observer.Character.Motion);
                int count = 0;
                foreach (EntitySnapshot entity in m_Entities)
                {
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

        // ---------------------------------------------------------------- chunk replication

        void ReplicateChunks()
        {
            foreach (ChunkChanges c in m_ChunkChanges.Values)
            {
                c.Upserts.Clear();
                c.Removes.Clear();
                c.Nodes.Clear();
            }

            m_World.Entities.ConsumeChanges(m_EntityChanges);
            foreach (EntityChangeRecord change in m_EntityChanges)
            {
                ChunkChanges c = ChangesFor(change.Chunk);
                if (change.Kind == EntityChange.Removed)
                {
                    c.Upserts.Remove(change.EntityId);
                    c.Removes.Add(change.EntityId);
                }
                else
                {
                    c.Removes.Remove(change.EntityId);
                    c.Upserts.Add(change.EntityId);
                }
            }

            m_World.Resources.ConsumeChanges(m_NodeChanges);
            foreach (int node in m_NodeChanges)
            {
                ChangesFor(m_World.Resources.ChunkOf(m_World.Resources.Placement(node).Position)).Nodes.Add(node);
            }

            foreach (ServerPlayer p in m_Players)
            {
                UpdateSubscriptions(p);
                foreach (var pair in m_ChunkChanges)
                {
                    ChunkChanges changes = pair.Value;
                    if (!p.Chunks.Contains(pair.Key) || (changes.Upserts.Count == 0 && changes.Removes.Count == 0 && changes.Nodes.Count == 0))
                    {
                        continue;
                    }

                    m_Writer.Reset();
                    ChunkMessages.BeginDelta(m_Writer, pair.Key, out int countOffset);
                    int ops = 0;
                    foreach (uint id in changes.Removes)
                    {
                        ChunkMessages.WriteRemove(m_Writer, id);
                        ops++;
                    }

                    foreach (uint id in changes.Upserts)
                    {
                        WorldEntity e = m_World.Entities.Get(id);
                        if (e != null)
                        {
                            ChunkMessages.WriteUpsert(m_Writer, e);
                            ops++;
                        }
                    }

                    foreach (int node in changes.Nodes)
                    {
                        ChunkMessages.WriteNode(m_Writer, node, m_World.Resources.IsAlive(node));
                        ops++;
                    }

                    ChunkMessages.SetDeltaCount(m_Writer, countOffset, ops);
                    Send(p, Delivery.ReliableOrdered);
                    Stats.ChunkMessages++;
                }
            }
        }

        ChunkChanges ChangesFor(ChunkCoord chunk)
        {
            if (!m_ChunkChanges.TryGetValue(chunk, out ChunkChanges c))
            {
                m_ChunkChanges[chunk] = c = new ChunkChanges();
            }

            return c;
        }

        void UpdateSubscriptions(ServerPlayer p)
        {
            ChunkCoord center = m_World.Entities.ChunkOf(p.Character.Motion.Position);
            if (center.Equals(p.LastChunk))
            {
                return;
            }

            p.LastChunk = center;
            m_ChunkScratch.Clear();
            foreach (ChunkCoord c in p.Chunks)
            {
                if (c.ChebyshevDistance(center) > m_Config.ChunkUnsubscribeRadius)
                {
                    m_ChunkScratch.Add(c);
                }
            }

            foreach (ChunkCoord c in m_ChunkScratch)
            {
                p.Chunks.Remove(c);
                m_Writer.Reset();
                ChunkMessages.WriteUnload(m_Writer, c);
                Send(p, Delivery.ReliableOrdered);
            }

            int r = m_Config.ChunkSubscribeRadius;
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    var c = new ChunkCoord(center.X + dx, center.Z + dz);
                    if (!m_World.Grid.ContainsChunk(c) || !p.Chunks.Add(c))
                    {
                        continue;
                    }

                    m_Writer.Reset();
                    ChunkMessages.WriteSnapshot(m_Writer, c, m_World.Resources.NodesInChunk(c), m_World.Resources, m_World.Entities.InChunk(c));
                    Send(p, Delivery.ReliableOrdered);
                    Stats.ChunkMessages++;
                }
            }
        }

        // ---------------------------------------------------------------- owner data

        void SendOwnerData()
        {
            foreach (ServerPlayer p in m_Players)
            {
                Character c = p.Character;
                SendContainerIfChanged(p, ContainerKind.Main, 0, c.Inventory.Main, ref p.SentMain);
                SendContainerIfChanged(p, ContainerKind.Hotbar, 0, c.Inventory.Hotbar, ref p.SentHotbar);
                SendContainerIfChanged(p, ContainerKind.Wear, 0, c.Inventory.Wear, ref p.SentWear);

                WorldEntity open = m_World.OpenContainerOf(c);
                if (open != null && (open.Id != p.SentOpenId || open.Container.Version != p.SentOpenVersion))
                {
                    p.SentOpenId = open.Id;
                    p.SentOpenVersion = open.Container.Version;
                    m_Writer.Reset();
                    ContainerMessage.Write(m_Writer, ContainerKind.Entity, open.Id, open.Container);
                    Send(p, Delivery.ReliableOrdered);
                    Stats.OwnerMessages++;
                }
                else if (open == null)
                {
                    p.SentOpenId = 0;
                    p.SentOpenVersion = uint.MaxValue;
                }

                if (OwnerStateDue(p))
                {
                    SendOwnerState(p);
                }

                if (c.Events.Count > 0)
                {
                    m_Writer.Reset();
                    EventsMessage.Write(m_Writer, c.Events);
                    c.Events.Clear();
                    Send(p, Delivery.ReliableOrdered);
                    Stats.OwnerMessages++;
                }
            }
        }

        void SendContainerIfChanged(ServerPlayer p, ContainerKind kind, uint entityId, ItemContainer container, ref uint sent)
        {
            if (container.Version == sent)
            {
                return;
            }

            sent = container.Version;
            m_Writer.Reset();
            ContainerMessage.Write(m_Writer, kind, entityId, container);
            Send(p, Delivery.ReliableOrdered);
            Stats.OwnerMessages++;
        }

        bool OwnerStateDue(ServerPlayer p)
        {
            Character c = p.Character;
            uint since = CurrentTick - p.LastStateTick;
            if (c.StateVersion != p.SentStateVersion)
            {
                return since >= 2; // at most 15 Hz for bursts of real changes
            }

            if (since < (uint)m_Config.TickRate / 4)
            {
                return false; // drifting stats at most 4 Hz
            }

            SurvivalStats s = c.Stats;
            return Math.Abs(s.Health - p.SentHealth) >= 0.5f || Math.Abs(s.Hunger - p.SentHunger) >= 0.5f || Math.Abs(s.Thirst - p.SentThirst) >= 0.5f
                || Math.Abs(s.Warmth - p.SentWarmth) >= 1f || c.Crafting.Count > 0 || since >= (uint)m_Config.TickRate * 2;
        }

        void SendOwnerState(ServerPlayer p)
        {
            Character c = p.Character;
            SurvivalStats s = c.Stats;
            OwnerStateMessage m = m_OwnerState;
            m.Life = c.Life;
            m.Health = s.Health;
            m.Hunger = s.Hunger;
            m.Thirst = s.Thirst;
            m.Warmth = s.Warmth;
            m.Effects = s.Effects;
            m.DownedLeft = c.DownedLeft;
            m.LastDamage = (byte)c.LastDamage;
            m.ActiveSlot = (sbyte)c.Inventory.ActiveSlot;
            m.OpenContainer = c.OpenContainerId;
            m.Crafting.Clear();
            foreach (CraftJob job in c.Crafting)
            {
                m.Crafting.Add(new CraftJobInfo { Id = job.Id, RecipeId = job.Recipe.NetId, Remaining = job.Remaining, Progress = job.Progress });
            }

            m.RespawnPoints.Clear();
            if (c.Life == LifeState.Dead)
            {
                m_World.RespawnPointsOf(c, m_EntityScratch);
                foreach (WorldEntity bed in m_EntityScratch)
                {
                    m.RespawnPoints.Add(new RespawnPointInfo { EntityId = bed.Id, CooldownLeft = (float)Math.Max(0, bed.CooldownUntil - m_World.Now) });
                }
            }

            m_Writer.Reset();
            m.Write(m_Writer);
            Send(p, Delivery.ReliableOrdered);
            Stats.OwnerMessages++;
            p.SentStateVersion = c.StateVersion;
            p.SentHealth = s.Health;
            p.SentHunger = s.Hunger;
            p.SentThirst = s.Thirst;
            p.SentWarmth = s.Warmth;
            p.LastStateTick = CurrentTick;
        }

        void SendWorldState(int onlyConnection)
        {
            m_Writer.Reset();
            new WorldStateMessage
            {
                WorldSeconds = m_World.Now,
                DayLengthSeconds = (float)m_World.Clock.DayLengthSeconds,
                NightFraction = (float)m_World.Clock.NightFraction,
            }.Write(m_Writer);
            foreach (ServerPlayer p in m_Players)
            {
                if (onlyConnection < 0 || p.ConnectionId == onlyConnection)
                {
                    Send(p, Delivery.ReliableOrdered);
                }
            }
        }

        void Send(ServerPlayer p, Delivery delivery) => m_Transport.Send(p.ConnectionId, m_Writer.Buffer, 0, m_Writer.Length, delivery);

        // ---------------------------------------------------------------- persistence

        void SaveIfDue()
        {
            if (m_Persistence == null)
            {
                return;
            }

            if (m_World.Now >= m_NextCheckpoint)
            {
                m_NextCheckpoint = m_World.Now + m_Config.CheckpointIntervalSeconds;
                m_NextSave = m_World.Now + m_Config.SaveIntervalSeconds;
                m_Section.Restart();
                m_Persistence.Checkpoint();
                Stats.LastSaveCaptureMs = m_Section.Elapsed.TotalMilliseconds;
                Stats.Saves++;
            }
            else if (m_World.Now >= m_NextSave)
            {
                m_NextSave = m_World.Now + m_Config.SaveIntervalSeconds;
                m_Section.Restart();
                m_Persistence.SaveChanges();
                Stats.LastSaveCaptureMs = m_Section.Elapsed.TotalMilliseconds;
                Stats.Saves++;
            }
        }

        /// <summary>Saves what changed right now (e.g. before a planned restart).</summary>
        public void SaveNow()
        {
            m_Persistence?.SaveChanges();
            m_Persistence?.Store.Flush();
        }

        // ---------------------------------------------------------------- connections

        void ExpireConnections()
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
                Log.Info(LogCategory, $"Player {c.Player.Id} ({c.Player.Name}) {(code == DisconnectCode.ClientQuit ? "left" : "dropped (" + code + ")")}.");
                RemovePlayer(c.Player);
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
                    case MessageType.Action when c.Player != null:
                        HandleAction(c.Player, ActionRequest.Read(ref r));
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

            if (hello.ContentFingerprint != m_World.Content.Fingerprint)
            {
                DropConnection(c.Id, DisconnectCode.ContentMismatch, "This server runs a different game version. Update the game.");
                return;
            }

            string account;
            string name;
            if (m_Config.AllowDevAuth)
            {
                name = hello.DisplayName;
                account = ResumeAccount(hello.ResumeToken)
                    ?? (string.IsNullOrEmpty(hello.AuthToken) ? "guest:" + Guid.NewGuid().ToString("N") : "dev:" + Truncate(hello.AuthToken, 64));
            }
            else if (m_Authenticator == null || !m_Authenticator.TryAuthenticate(hello, out account, out name, out _))
            {
                DropConnection(c.Id, DisconnectCode.AuthFailed, "Join ticket rejected.");
                return;
            }

            // The same account connecting again takes over (the old connection is closed).
            foreach (ServerPlayer existing in m_Players.ToArray())
            {
                if (existing.Character.AccountId == account)
                {
                    m_Transport.Disconnect(existing.ConnectionId, DisconnectCode.Replaced, "Connected from somewhere else.");
                    m_Connections.Remove(existing.ConnectionId);
                    RemovePlayer(existing);
                }
            }

            if (m_Players.Count >= m_Config.MaxPlayers)
            {
                DropConnection(c.Id, DisconnectCode.ServerFull, "Server is full.");
                return;
            }

            bool existed = m_World.FindCharacter(account) != null;
            Character character = m_World.Join(account, SanitizeName(name));
            var token = new byte[NetProtocol.ResumeTokenLength];
            m_Rng.GetBytes(token);
            m_ResumeAccounts[Convert.ToBase64String(token)] = account;
            var player = new ServerPlayer(AllocateId(), character, c.Id, token);
            m_Players.Add(player);
            c.Player = player;

            m_Transport.Send(c.Id, m_MapWriter.Buffer, 0, m_MapWriter.Length, Delivery.ReliableOrdered);
            m_Writer.Reset();
            new WelcomeMessage
            {
                PlayerId = player.Id,
                ServerTick = CurrentTick,
                TickRate = (byte)m_Config.TickRate,
                MapSeed = m_World.Map.Static.Seed,
                ResumeToken = token,
                Resumed = existed,
                LastInputSequence = 0,
                State = character.Motion,
            }.Write(m_Writer);
            Send(player, Delivery.ReliableOrdered);
            SendWorldState(c.Id);

            SendAllPlayerInfo(c.Id);
            m_InfoScratch.Clear();
            m_InfoScratch.Add(new PlayerInfo { Id = player.Id, DisplayName = character.Name });
            m_Writer.Reset();
            PlayerInfoMessage.Write(m_Writer, m_InfoScratch, 0, 1);
            Broadcast(Delivery.ReliableOrdered, exceptPlayer: player);
            Log.Info(LogCategory, $"Player {player.Id} ({character.Name}, {account}) {(existed ? "returned" : "joined")} on connection {c.Id}.");
        }

        string ResumeAccount(byte[] token)
        {
            if (token == null || token.Length != NetProtocol.ResumeTokenLength)
            {
                return null;
            }

            return m_ResumeAccounts.TryGetValue(Convert.ToBase64String(token), out string account) ? account : null;
        }

        void HandleInputs(ServerPlayer player, ref PacketReader r)
        {
            InputsMessage.Read(ref r, m_InputScratch);

            // Late inputs still count, but don't end "away": only Background(false) does, otherwise
            // jittered packets arriving after Background(true) would let the player run on.
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

        void HandleAction(ServerPlayer player, ActionRequest a)
        {
            Character c = player.Character;
            Stats.Actions++;
            ActionError result;
            switch (a.Kind)
            {
                case ActionKind.MoveItem:
                    result = m_World.MoveItem(c, a.From, a.FromSlot, a.To, a.ToSlot, a.Amount);
                    break;
                case ActionKind.Drop:
                    result = m_World.Drop(c, a.From, a.FromSlot, a.Amount);
                    break;
                case ActionKind.Use:
                    result = m_World.UseItem(c, a.From, a.FromSlot);
                    break;
                case ActionKind.SelectHotbar:
                    result = m_World.SelectHotbar(c, a.FromSlot);
                    break;
                case ActionKind.Craft:
                    result = m_World.Craft(c, m_World.Content.ByNetId<RecipeDef>((ushort)Math.Min(a.Target, ushort.MaxValue)), a.Amount);
                    break;
                case ActionKind.CancelCraft:
                    result = m_World.CancelCraft(c, (int)Math.Min(a.Target, int.MaxValue));
                    break;
                case ActionKind.Deploy:
                    result = m_World.Deploy(c, a.FromSlot, a.Position, a.Yaw);
                    break;
                case ActionKind.Interact:
                    result = m_World.Interact(c, a.InteractTarget, a.Target);
                    break;
                case ActionKind.CloseContainer:
                    m_World.CloseContainer(c);
                    result = ActionError.None;
                    break;
                case ActionKind.ToggleProcessor:
                    result = m_World.ToggleProcessor(c, a.Target);
                    break;
                case ActionKind.Respawn:
                    result = m_World.Respawn(c, a.Target);
                    if (result == ActionError.None)
                    {
                        player.PendingInputs.Clear();
                    }

                    break;
                case ActionKind.GiveUp:
                    result = m_World.GiveUp(c);
                    break;
                case ActionKind.Revive:
                    result = m_World.Revive(c, FindPlayer((ushort)Math.Min(a.Target, ushort.MaxValue))?.Character);
                    break;
                default:
                    result = ActionError.InvalidTarget;
                    break;
            }

            m_Writer.Reset();
            ActionResultMessage.Write(m_Writer, a.RequestId, result);
            Send(player, Delivery.ReliableOrdered);
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
                if (p != exceptPlayer)
                {
                    m_Transport.Send(p.ConnectionId, m_Writer.Buffer, 0, m_Writer.Length, delivery);
                }
            }
        }

        /// <summary>The player goes away: their character falls asleep in the world and is saved.</summary>
        void RemovePlayer(ServerPlayer player)
        {
            m_Players.Remove(player);
            player.ConnectionId = -1;
            m_World.Leave(player.Character);
            if (m_Persistence != null)
            {
                m_Persistence.FlushCharacter(player.Character);
            }

            m_Writer.Reset();
            m_Writer.WriteByte((byte)MessageType.PlayerLeft);
            m_Writer.WriteUShort(player.Id);
            Broadcast(Delivery.ReliableOrdered, exceptPlayer: null);
        }

        void DropConnection(int connectionId, DisconnectCode code, string message)
        {
            if (m_Connections.TryGetValue(connectionId, out Connection c) && c.Player != null && c.Player.ConnectionId == connectionId)
            {
                RemovePlayer(c.Player);
            }

            m_Connections.Remove(connectionId);
            m_Transport.Disconnect(connectionId, code, message);
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

        static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

        static string SanitizeName(string name)
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
            return result.Length == 0 ? "Survivor" : result;
        }
    }
}
