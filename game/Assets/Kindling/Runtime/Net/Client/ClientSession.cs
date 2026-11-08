using System;
using System.Collections.Generic;
using System.Numerics;
using Kindling.Core.Diagnostics;
using Kindling.Net.Protocol;
using Kindling.Net.Transport;
using Kindling.Simulation.Collision;
using Kindling.Simulation.Movement;
using Kindling.Simulation.World;
using Kindling.Core.Serialization;

namespace Kindling.Net.Client
{
    public enum ClientStatus
    {
        Disconnected,
        Connecting,
        Handshaking,
        Connected,
    }

    /// <summary>
    /// Client side of the M1 netcode. Call <see cref="SetInput"/> whenever controls change and
    /// <see cref="Update"/> every frame. The local player is predicted (simulated immediately with the same
    /// motor as the server) and reconciled against the server's authoritative state; remote players are
    /// interpolated a few ticks in the past. Engine-independent: used by the Unity client and by bots.
    /// </summary>
    public sealed class ClientSession : ITransportEvents, IDisposable
    {
        const string LogCategory = "Client";
        const int HistorySize = 256;

        readonly IClientTransport m_Transport;
        readonly ClientSettings m_Settings;
        readonly Func<int, GreyboxLayout> m_LayoutForSeed;
        readonly MovementSettings m_Movement = new MovementSettings();
        readonly PacketWriter m_Writer = new PacketWriter(256);
        readonly List<int> m_Scratch = new List<int>();
        readonly PlayerInput[] m_InputHistory = new PlayerInput[HistorySize];
        readonly PlayerState[] m_StateHistory = new PlayerState[HistorySize];
        readonly PlayerInput[] m_SendScratch = new PlayerInput[NetProtocol.InputRedundancy];
        readonly Dictionary<ushort, RemoteEntityBuffer> m_Remotes = new Dictionary<ushort, RemoteEntityBuffer>();
        readonly Dictionary<ushort, string> m_Names = new Dictionary<ushort, string>();
        readonly List<PlayerInfo> m_InfoScratch = new List<PlayerInfo>();
        readonly List<ushort> m_RemoveScratch = new List<ushort>();

        CollisionWorld m_World;
        int m_WorldSeed = int.MinValue;
        string m_Host;
        int m_Port;
        byte[] m_ResumeToken;
        double m_LocalTime;
        double m_StatusSince;
        double m_Accumulator;
        float m_Dt = 1f / NetProtocol.TickRate;
        int m_TickRate = NetProtocol.TickRate;
        uint m_Sequence;
        uint m_FirstSequence;
        double m_TickScale = 1;
        PlayerState m_State;
        PlayerState m_PreviousState;
        Vector3 m_CorrectionOffset;

        Vector2 m_Move;
        float m_Yaw;
        float m_Pitch;
        InputButtons m_Held;
        InputButtons m_Latched;

        uint m_LatestSnapshotTick;
        bool m_HasTickOffset;
        double m_TickOffset;
        double m_Jitter;
        double m_InterpolationTicks;
        double m_AverageBufferDepth = 2;
        double m_RateWindowStart;
        long m_RateBytesIn;
        long m_RateBytesOut;

        public ClientSession(IClientTransport transport, ClientSettings settings = null, Func<int, GreyboxLayout> layoutForSeed = null)
        {
            m_Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            m_Settings = settings ?? new ClientSettings();
            m_LayoutForSeed = layoutForSeed ?? (seed => GreyboxLayout.Generate(seed));
            m_InterpolationTicks = m_Settings.MinInterpolationTicks;
        }

        public event Action<ClientStatus> StatusChanged;

        public ClientStatus Status { get; private set; }

        public ClientSettings Settings => m_Settings;

        public ClientStats Stats { get; } = new ClientStats();

        public ushort PlayerId { get; private set; }

        public int MapSeed { get; private set; }

        public GreyboxLayout Layout { get; private set; }

        public DisconnectCode LastDisconnectCode { get; private set; }

        public string LastDisconnectMessage { get; private set; } = string.Empty;

        /// <summary>True once a Welcome arrived and until a clean quit: a reconnect will try to resume the same player.</summary>
        public bool CanResume => m_ResumeToken != null;

        public bool LastJoinWasResume { get; private set; }

        /// <summary>The predicted local player state after the newest tick.</summary>
        public PlayerState PredictedState => m_State;

        public uint InputSequence => m_Sequence;

        /// <summary>Visual offset still being smoothed out after a correction.</summary>
        public Vector3 CorrectionOffset => m_CorrectionOffset;

        /// <summary>Fraction (0..1) of the way from the previous tick to the next one, for render interpolation.</summary>
        public float TickAlpha => (float)Math.Min(1.0, m_Accumulator / (m_Dt * m_TickScale));

        public MovementSettings Movement => m_Movement;

        public CollisionWorld World => m_World;

        public void Connect(string host, int port)
        {
            bool sameServer = host == m_Host && port == m_Port;
            if (!sameServer)
            {
                m_ResumeToken = null;
            }

            m_Host = host;
            m_Port = port;
            LastDisconnectCode = DisconnectCode.None;
            LastDisconnectMessage = string.Empty;
            SetStatus(ClientStatus.Connecting);
            m_Transport.Connect(host, port);
        }

        /// <summary>Leaves the server for good (the player is removed; no resume).</summary>
        public void Disconnect()
        {
            if (Status != ClientStatus.Disconnected)
            {
                m_Transport.Disconnect(DisconnectCode.ClientQuit);
                m_Transport.Flush();
            }

            m_ResumeToken = null;
            LastDisconnectCode = DisconnectCode.ClientQuit;
            ResetWorldState();
            SetStatus(ClientStatus.Disconnected);
        }

        public void Dispose()
        {
            Disconnect();
            m_Transport.Dispose();
        }

        /// <summary>Current control state. Buttons in <paramref name="held"/> apply while held.</summary>
        public void SetInput(Vector2 move, float yaw, float pitch, InputButtons held)
        {
            m_Move = move;
            m_Yaw = yaw;
            m_Pitch = pitch;
            m_Held = held;
        }

        /// <summary>Buttons tapped since the last tick (so a quick tap between ticks still counts).</summary>
        public void Press(InputButtons buttons) => m_Latched |= buttons;

        /// <summary>Tell the server the app is going to the background (iOS) or coming back.</summary>
        public void NotifyBackground(bool away)
        {
            if (Status != ClientStatus.Connected)
            {
                return;
            }

            m_Writer.Reset();
            m_Writer.WriteByte((byte)MessageType.Background);
            m_Writer.WriteBool(away);
            m_Transport.Send(m_Writer.Buffer, 0, m_Writer.Length, Delivery.ReliableOrdered);
            m_Transport.Flush();
            if (!away)
            {
                m_Accumulator = 0; // don't try to catch up on time spent suspended
            }
        }

        public void Update(double deltaSeconds)
        {
            if (!(deltaSeconds >= 0) || double.IsInfinity(deltaSeconds))
            {
                deltaSeconds = 0;
            }

            m_LocalTime += deltaSeconds;
            m_Transport.Poll(this);

            if (Status == ClientStatus.Handshaking && m_LocalTime - m_StatusSince > m_Settings.HandshakeTimeoutSeconds)
            {
                Fail(DisconnectCode.HandshakeTimeout, "No answer from the server.");
            }

            if (Status == ClientStatus.Connected)
            {
                m_Accumulator += deltaSeconds;
                double interval = m_Dt * m_TickScale;
                int ticks = 0;
                while (m_Accumulator >= interval)
                {
                    m_Accumulator -= interval;
                    if (ticks++ >= m_Settings.MaxTicksPerUpdate)
                    {
                        m_Accumulator = 0; // too far behind (debugger, long frame): skip time
                        break;
                    }

                    RunTick();
                }

                if (ticks > 0)
                {
                    m_Transport.Flush();
                }

                float decay = (float)Math.Exp(-m_Settings.CorrectionSmoothingRate * deltaSeconds);
                m_CorrectionOffset *= decay;
                if (m_CorrectionOffset.LengthSquared() < 1e-8f)
                {
                    m_CorrectionOffset = Vector3.Zero;
                }

                UpdateInterpolationDelay(deltaSeconds);
                RemoveStaleRemotes();
            }

            UpdateRates();
        }

        /// <summary>Where to draw the local player's feet this frame (between ticks, plus correction smoothing).</summary>
        public Vector3 GetRenderPosition() =>
            Vector3.Lerp(m_PreviousState.Position, m_State.Position, TickAlpha) + m_CorrectionOffset;

        /// <summary>Fills <paramref name="results"/> with interpolated remote players.</summary>
        public void GetRemotePlayers(List<RemotePlayer> results)
        {
            results.Clear();
            double renderTick = RenderTick;
            foreach (var pair in m_Remotes)
            {
                EntitySnapshot s = pair.Value.Sample(renderTick);
                m_Names.TryGetValue(pair.Key, out string name);
                results.Add(new RemotePlayer
                {
                    Id = pair.Key,
                    Name = name ?? $"Player {pair.Key}",
                    Position = s.Position,
                    Yaw = s.Yaw,
                    Pitch = s.Pitch,
                    Flags = s.Flags,
                    NewestTick = pair.Value.NewestTick,
                });
            }
        }

        public string GetPlayerName(ushort id) => m_Names.TryGetValue(id, out string name) ? name : null;

        /// <summary>The (fractional) server tick remote players are currently drawn at.</summary>
        public double RenderTick => m_LocalTime * m_TickRate + m_TickOffset - m_InterpolationTicks;

        /// <summary>Best estimate of the server's current tick.</summary>
        public double EstimatedServerTick => m_LocalTime * m_TickRate + m_TickOffset;

        // ---------------------------------------------------------------- ticking

        void RunTick()
        {
            m_Sequence++;
            var raw = new PlayerInput
            {
                Sequence = m_Sequence,
                Move = m_Move,
                Yaw = m_Yaw,
                Pitch = m_Pitch,
                Buttons = m_Held | m_Latched,
            };
            m_Latched = InputButtons.None;
            PlayerInput input = InputCodec.Quantize(raw);

            m_PreviousState = m_State;
            CharacterMotor.Simulate(ref m_State, input, m_Movement, m_World, m_Scratch, m_Dt);
            int slot = (int)(m_Sequence % HistorySize);
            m_InputHistory[slot] = input;
            m_StateHistory[slot] = m_State;

            int count = 0;
            for (uint s = m_Sequence; count < NetProtocol.InputRedundancy && s > m_FirstSequence; s--)
            {
                m_SendScratch[count++] = m_InputHistory[s % HistorySize];
            }

            m_Writer.Reset();
            InputsMessage.Write(m_Writer, m_SendScratch, count);
            m_Transport.Send(m_Writer.Buffer, 0, m_Writer.Length, Delivery.Sequenced);
        }

        void Reconcile(uint sequence, in PlayerState server)
        {
            if (sequence > m_Sequence || m_Sequence - sequence >= HistorySize - 1 || sequence < m_FirstSequence)
            {
                HardReset(sequence, server);
                return;
            }

            int slot = (int)(sequence % HistorySize);
            PlayerState predicted = m_StateHistory[slot];
            float error = Vector3.Distance(predicted.Position, server.Position);
            bool mismatch = error > m_Settings.ReconcileThreshold
                || Vector3.DistanceSquared(predicted.Velocity, server.Velocity) > 0.01f
                || predicted.Crouched != server.Crouched
                || predicted.Grounded != server.Grounded;
            if (!mismatch)
            {
                return;
            }

            Vector3 before = m_State.Position;
            m_State = server;
            m_StateHistory[slot] = server;
            for (uint s = sequence + 1; s <= m_Sequence; s++)
            {
                int i = (int)(s % HistorySize);
                CharacterMotor.Simulate(ref m_State, m_InputHistory[i], m_Movement, m_World, m_Scratch, m_Dt);
                m_StateHistory[i] = m_State;
            }

            Vector3 shift = m_State.Position - before;
            float size = shift.Length();
            m_PreviousState.Position += shift;
            if (size <= m_Settings.MaxSmoothedCorrection)
            {
                m_CorrectionOffset -= shift;
            }
            else
            {
                m_CorrectionOffset = Vector3.Zero;
            }

            Stats.Corrections++;
            Stats.LastCorrection = size;
            Stats.MaxCorrection = Math.Max(Stats.MaxCorrection, size);
        }

        void HardReset(uint sequence, in PlayerState server)
        {
            m_State = server;
            m_PreviousState = server;
            m_CorrectionOffset = Vector3.Zero;
            if (sequence > m_Sequence)
            {
                m_Sequence = sequence;
            }

            m_FirstSequence = m_Sequence;
            m_StateHistory[m_Sequence % HistorySize] = server;
            Stats.Corrections++;
        }

        // ---------------------------------------------------------------- transport events

        void ITransportEvents.OnConnected(int connectionId)
        {
            if (Status != ClientStatus.Connecting)
            {
                return;
            }

            SetStatus(ClientStatus.Handshaking);
            m_Writer.Reset();
            new HelloMessage
            {
                ProtocolVersion = m_Settings.ProtocolVersion,
                BuildVersion = m_Settings.BuildVersion,
                DisplayName = m_Settings.DisplayName,
                AuthToken = m_Settings.AuthToken,
                ResumeToken = m_ResumeToken,
            }.Write(m_Writer);
            m_Transport.Send(m_Writer.Buffer, 0, m_Writer.Length, Delivery.ReliableOrdered);
            m_Transport.Flush();
        }

        void ITransportEvents.OnDisconnected(int connectionId, DisconnectCode code, string message)
        {
            if (Status == ClientStatus.Disconnected)
            {
                return;
            }

            LastDisconnectCode = code;
            LastDisconnectMessage = message ?? string.Empty;
            if (code != DisconnectCode.Timeout && code != DisconnectCode.ConnectionFailed)
            {
                m_ResumeToken = null; // the server ended it on purpose: nothing to resume
            }

            Log.Info(LogCategory, $"Disconnected: {code} {message}");
            SetStatus(ClientStatus.Disconnected);
        }

        void ITransportEvents.OnData(int connectionId, byte[] data, int offset, int count)
        {
            try
            {
                var r = new PacketReader(data, offset, count);
                var type = (MessageType)r.ReadByte();
                switch (type)
                {
                    case MessageType.Welcome when Status == ClientStatus.Handshaking:
                        HandleWelcome(WelcomeMessage.Read(ref r));
                        break;
                    case MessageType.Snapshot when Status == ClientStatus.Connected:
                        HandleSnapshot(ref r);
                        break;
                    case MessageType.PlayerInfo when Status == ClientStatus.Connected:
                        PlayerInfoMessage.Read(ref r, m_InfoScratch);
                        foreach (PlayerInfo info in m_InfoScratch)
                        {
                            m_Names[info.Id] = info.DisplayName;
                        }

                        break;
                    case MessageType.PlayerLeft when Status == ClientStatus.Connected:
                        ushort id = r.ReadUShort();
                        m_Remotes.Remove(id);
                        m_Names.Remove(id);
                        break;
                    default:
                        // Messages for a state we already left (e.g. a snapshot racing the welcome) are ignored.
                        break;
                }
            }
            catch (ProtocolException e)
            {
                Log.Warning(LogCategory, $"Bad message from server: {e.Message}");
                m_Transport.Disconnect(DisconnectCode.ProtocolError);
                Fail(DisconnectCode.ProtocolError, e.Message);
            }
        }

        void HandleWelcome(in WelcomeMessage welcome)
        {
            PlayerId = welcome.PlayerId;
            MapSeed = welcome.MapSeed;
            m_TickRate = welcome.TickRate;
            m_Dt = 1f / welcome.TickRate;
            m_ResumeToken = welcome.ResumeToken;
            LastJoinWasResume = welcome.Resumed;
            if (m_WorldSeed != welcome.MapSeed || m_World == null)
            {
                Layout = m_LayoutForSeed(welcome.MapSeed);
                m_World = Layout.BuildCollisionWorld();
                m_WorldSeed = welcome.MapSeed;
            }

            if (!welcome.Resumed)
            {
                m_Remotes.Clear();
                m_Names.Clear();
            }

            m_Sequence = welcome.LastInputSequence;
            m_FirstSequence = m_Sequence;
            m_State = welcome.State;
            m_PreviousState = welcome.State;
            m_StateHistory[m_Sequence % HistorySize] = welcome.State;
            m_CorrectionOffset = Vector3.Zero;
            m_Accumulator = 0;
            m_Latched = InputButtons.None;
            m_HasTickOffset = false;
            m_LatestSnapshotTick = 0;
            m_AverageBufferDepth = 2;
            m_TickScale = 1;
            Stats.TickScale = 1;
            ResyncClock(welcome.ServerTick);
            SetStatus(ClientStatus.Connected);
            Log.Info(LogCategory, $"{(welcome.Resumed ? "Resumed" : "Joined")} as player {PlayerId} (map seed {MapSeed}).");
        }

        void HandleSnapshot(ref PacketReader r)
        {
            SnapshotMessage.Header header = SnapshotMessage.ReadHeader(ref r);
            if (header.ServerTick < m_LatestSnapshotTick)
            {
                return;
            }

            bool newTick = header.ServerTick > m_LatestSnapshotTick;
            m_LatestSnapshotTick = header.ServerTick;
            Stats.ServerTick = header.ServerTick;
            Stats.SnapshotsReceived++;

            if (header.HasOwner)
            {
                SnapshotMessage.ReadOwner(ref r, out uint lastInput, out PlayerState server);
                Reconcile(lastInput, server);
                UpdateTimeDilation(header.InputBufferDepth);
            }

            if (newTick)
            {
                ObserveServerTick(header.ServerTick);
            }

            int count = r.ReadByte();
            for (int i = 0; i < count; i++)
            {
                EntitySnapshot e = EntitySnapshot.Read(ref r);
                if (e.Id == PlayerId)
                {
                    continue;
                }

                if (!m_Remotes.TryGetValue(e.Id, out RemoteEntityBuffer buffer))
                {
                    buffer = new RemoteEntityBuffer();
                    m_Remotes[e.Id] = buffer;
                }

                buffer.Add(header.ServerTick, e, m_LocalTime);
            }

            Stats.RemotePlayers = m_Remotes.Count;
        }

        // ---------------------------------------------------------------- clock sync

        void ResyncClock(uint serverTick)
        {
            m_TickOffset = serverTick - m_LocalTime * m_TickRate;
            m_HasTickOffset = true;
            m_Jitter = 0;
        }

        void ObserveServerTick(uint serverTick)
        {
            double sample = serverTick - m_LocalTime * m_TickRate;
            if (!m_HasTickOffset || Math.Abs(sample - m_TickOffset) > 15)
            {
                ResyncClock(serverTick);
                return;
            }

            double diff = sample - m_TickOffset;

            // Packets that arrive early tell us the true offset; late ones are mostly jitter.
            m_TickOffset += diff * (diff > 0 ? 0.2 : 0.02);
            m_Jitter += (Math.Abs(diff) - m_Jitter) * 0.05;
        }

        void UpdateInterpolationDelay(double deltaSeconds)
        {
            double target = Math.Max(m_Settings.MinInterpolationTicks, Math.Min(m_Settings.MaxInterpolationTicks, m_Settings.MinInterpolationTicks + m_Jitter * 2.5));
            double step = deltaSeconds * 2.0; // change by at most 2 ticks per second so motion stays smooth
            m_InterpolationTicks += Math.Max(-step, Math.Min(step, target - m_InterpolationTicks));
            Stats.InterpolationDelayMs = m_InterpolationTicks * 1000.0 / m_TickRate;
        }

        void UpdateTimeDilation(int depth)
        {
            Stats.ServerInputBuffer = depth;
            m_AverageBufferDepth += (depth - m_AverageBufferDepth) * 0.1;
            if (m_AverageBufferDepth < 1.5)
            {
                m_TickScale = 0.97; // run slightly faster so inputs arrive before the server needs them
            }
            else if (m_AverageBufferDepth > 3.5)
            {
                m_TickScale = 1.03;
            }
            else
            {
                m_TickScale = 1.0;
            }

            Stats.TickScale = m_TickScale;
        }

        // ---------------------------------------------------------------- housekeeping

        void RemoveStaleRemotes()
        {
            m_RemoveScratch.Clear();
            foreach (var pair in m_Remotes)
            {
                if (m_LocalTime - pair.Value.LastUpdateTime > m_Settings.StaleEntitySeconds)
                {
                    m_RemoveScratch.Add(pair.Key);
                }
            }

            foreach (ushort id in m_RemoveScratch)
            {
                m_Remotes.Remove(id);
            }
        }

        void UpdateRates()
        {
            Stats.RoundTripMs = m_Transport.RoundTripMs;
            double elapsed = m_LocalTime - m_RateWindowStart;
            if (elapsed >= 1.0)
            {
                TransportCounters c = m_Transport.Counters;
                Stats.BytesInPerSecond = (c.PayloadBytesReceived - m_RateBytesIn) / elapsed;
                Stats.BytesOutPerSecond = (c.PayloadBytesSent - m_RateBytesOut) / elapsed;
                m_RateBytesIn = c.PayloadBytesReceived;
                m_RateBytesOut = c.PayloadBytesSent;
                m_RateWindowStart = m_LocalTime;
            }
        }

        void Fail(DisconnectCode code, string message)
        {
            LastDisconnectCode = code;
            LastDisconnectMessage = message;
            SetStatus(ClientStatus.Disconnected);
        }

        void ResetWorldState()
        {
            m_Remotes.Clear();
            m_Names.Clear();
            m_CorrectionOffset = Vector3.Zero;
        }

        void SetStatus(ClientStatus status)
        {
            if (Status == status)
            {
                return;
            }

            Status = status;
            m_StatusSince = m_LocalTime;
            StatusChanged?.Invoke(status);
        }
    }
}
