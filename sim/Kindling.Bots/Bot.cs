using System;
using System.Numerics;
using Kindling.Core.Numerics;
using Kindling.Net.Client;
using Kindling.Net.Transport;
using Kindling.Simulation.Movement;

namespace Kindling.Bots
{
    /// <summary>One headless player: wanders, sprints, jumps and crouches at random.</summary>
    public sealed class Bot : IDisposable
    {
        readonly DeterministicRandom m_Random;
        double m_NextDecision;
        Vector2 m_Move;
        float m_Yaw;
        float m_TurnRate;
        InputButtons m_Held;
        double m_ConnectedAt = -1;
        int m_CorrectionsAtSteadyStart = -1;

        public Bot(int index, ulong seed, string buildVersion, LinkSimulation simulation)
        {
            Index = index;
            m_Random = new DeterministicRandom(seed + (ulong)index * 7919UL);
            Transport = new LiteNetClientTransport(simulation: simulation);
            Session = new ClientSession(Transport, new ClientSettings { DisplayName = $"bot{index:000}", BuildVersion = buildVersion });
            m_Yaw = m_Random.Range(0f, 360f);
        }

        public int Index { get; }

        public LiteNetClientTransport Transport { get; }

        public ClientSession Session { get; }

        /// <summary>Corrections counted from 2 s after joining (join-time corrections are expected).</summary>
        public int SteadyCorrections { get; private set; }

        public float SteadyMaxCorrection { get; private set; }

        public double ConnectedSeconds { get; private set; }

        public long SnapshotsAtSteadyStart { get; private set; }

        public void Update(double time, double dt)
        {
            if (Session.Status == ClientStatus.Connected)
            {
                if (m_ConnectedAt < 0)
                {
                    m_ConnectedAt = time;
                }

                ConnectedSeconds = time - m_ConnectedAt;
                if (m_CorrectionsAtSteadyStart < 0 && ConnectedSeconds >= 2)
                {
                    m_CorrectionsAtSteadyStart = Session.Stats.Corrections;
                    SnapshotsAtSteadyStart = Session.Stats.SnapshotsReceived;
                }

                Think(time, dt);
            }

            int corrections = Session.Stats.Corrections;
            Session.Update(dt);
            if (m_CorrectionsAtSteadyStart >= 0 && Session.Stats.Corrections > corrections)
            {
                SteadyCorrections = Session.Stats.Corrections - m_CorrectionsAtSteadyStart;
                SteadyMaxCorrection = Math.Max(SteadyMaxCorrection, Session.Stats.LastCorrection);
            }
        }

        void Think(double time, double dt)
        {
            if (time >= m_NextDecision)
            {
                m_NextDecision = time + m_Random.Range(1.5f, 5f);
                float r = m_Random.NextFloat();
                m_Move = r < 0.1f ? Vector2.Zero : new Vector2(m_Random.Range(-0.5f, 0.5f), 1f);
                m_TurnRate = m_Random.Range(-60f, 60f);
                m_Held = InputButtons.None;
                if (m_Random.NextFloat() < 0.4f)
                {
                    m_Held |= InputButtons.Sprint;
                }
                else if (m_Random.NextFloat() < 0.15f)
                {
                    m_Held |= InputButtons.Crouch;
                }
            }

            if (m_Random.NextFloat() < dt * 0.3f)
            {
                Session.Press(InputButtons.Jump);
            }

            // Turn back toward the middle when far out, so bots stay in each other's interest range.
            Vector3 p = Session.PredictedState.Position;
            if (p.X * p.X + p.Z * p.Z > 150f * 150f)
            {
                float toCenter = (float)(Math.Atan2(-p.X, -p.Z) * 180.0 / Math.PI);
                m_Yaw = toCenter;
            }
            else
            {
                m_Yaw += m_TurnRate * (float)dt;
            }

            Session.SetInput(m_Move, m_Yaw, 0f, m_Held);
        }

        public void Dispose() => Session.Dispose();
    }
}
