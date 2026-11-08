using System;
using System.Linq;
using System.Numerics;
using Kindling.Content;
using Kindling.Core.Numerics;
using Kindling.Net.Client;
using Kindling.Net.Transport;
using Kindling.Simulation;
using Kindling.Simulation.Game;
using Kindling.Simulation.Movement;
using Kindling.Simulation.World;

namespace Kindling.Bots
{
    /// <summary>
    /// One headless player. Wanderers roam, sprint, jump and crouch at random. Gatherers walk to trees and
    /// chop them, craft and place a campfire, eat and drink when needed and respawn when they die — so a
    /// load test exercises gathering, crafting, actions, chunk updates and owner state, not just movement.
    /// </summary>
    public sealed class Bot : IDisposable
    {
        enum Goal
        {
            Wander,
            Gather,
            Drink,
        }

        readonly DeterministicRandom m_Random;
        readonly bool m_Gatherer;
        double m_NextDecision;
        Vector2 m_Move;
        float m_Yaw;
        float m_Pitch;
        float m_TurnRate;
        InputButtons m_Held;
        double m_ConnectedAt = -1;
        int m_CorrectionsAtSteadyStart = -1;
        int m_TargetNode = -1;
        Vector3 m_LastProgressPos;
        double m_LastProgressAt;
        double m_UnstickUntil;
        double m_NextAction;
        bool m_CampfirePlaced;
        Goal m_Goal = Goal.Wander;

        public Bot(int index, ulong seed, string buildVersion, LinkSimulation simulation, bool gatherer)
        {
            Index = index;
            m_Gatherer = gatherer;
            m_Random = new DeterministicRandom(seed + (ulong)index * 7919UL);
            Transport = new LiteNetClientTransport(simulation: simulation);
            Session = new ClientSession(Transport, new ClientSettings { DisplayName = $"bot{index:000}", BuildVersion = buildVersion, AuthToken = $"bot-{seed}-{index}" });
            m_Yaw = m_Random.Range(0f, 360f);
        }

        public int Index { get; }

        public LiteNetClientTransport Transport { get; }

        public ClientSession Session { get; }

        public int SteadyCorrections { get; private set; }

        public float SteadyMaxCorrection { get; private set; }

        public double ConnectedSeconds { get; private set; }

        public int WoodGathered => Session.WorldState.Inventory.Count(Session.Content.Item("item.resource.wood"));

        public int Deaths { get; private set; }

        public int ActionsSent { get; private set; }

        public bool PlacedCampfire => m_CampfirePlaced;

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
            ClientWorld w = Session.WorldState;
            if (!w.HasOwnerState || w.Map == null)
            {
                return;
            }

            if (w.Owner.Life == LifeState.Dead)
            {
                if (time >= m_NextAction)
                {
                    m_NextAction = time + 2;
                    Deaths++;
                    Send(Session.Respawn(0));
                    m_TargetNode = -1;
                }

                return;
            }

            if (w.Owner.Life == LifeState.Downed)
            {
                Session.SetInput(Vector2.Zero, m_Yaw, 0, InputButtons.None);
                return;
            }

            if (time >= m_NextAction)
            {
                m_NextAction = time + 1.5;
                if (ManageNeeds(w))
                {
                    return;
                }
            }

            if (m_Gatherer && m_Goal != Goal.Drink)
            {
                m_Goal = Goal.Gather;
                GatherStep(w, time);
            }
            else if (m_Goal == Goal.Drink)
            {
                DrinkStep(w, time);
            }
            else
            {
                WanderStep(time, dt);
            }
        }

        /// <summary>Eat, drink, craft and place a campfire. Returns true if an action was sent.</summary>
        bool ManageNeeds(ClientWorld w)
        {
            ContentDatabase db = Session.Content;
            if (w.Owner.Hunger < 40)
            {
                for (int i = 0; i < 24; i++)
                {
                    var s = w.Inventory.Main[i];
                    if (!s.IsEmpty && s.Def.Category == ItemCategory.Food && (s.Def.Consume?.PoisonChance ?? 0) == 0)
                    {
                        return Send(Session.Use(ContainerRef.Main, i));
                    }
                }
            }

            if (w.Owner.Thirst < 30 && m_Goal != Goal.Drink)
            {
                m_Goal = Goal.Drink;
                return false;
            }

            if (!m_CampfirePlaced && w.Inventory.Count(db.Item("item.resource.wood")) >= 100 && w.Inventory.Count(db.Item("item.deployable.campfire")) == 0 && w.Owner.Crafting.Count == 0)
            {
                return Send(Session.Craft(db.Recipe("recipe.campfire")));
            }

            for (int i = 0; i < 6; i++)
            {
                if (!m_CampfirePlaced && w.Inventory.Hotbar[i].Def == db.Item("item.deployable.campfire"))
                {
                    m_CampfirePlaced = true;
                    Vector3 p = Session.PredictedState.Position + GameWorld.ViewDirection(m_Yaw + 180f, 0f) * 2.5f;
                    return Send(Session.Deploy(i, p, 0));
                }
            }

            if (w.Owner.ActiveSlot < 0 || w.Inventory.Hotbar[w.Owner.ActiveSlot].Def?.Tool == null)
            {
                for (int i = 0; i < 6; i++)
                {
                    if (w.Inventory.Hotbar[i].Def?.Tool?.PowerFor(GatherKind.Wood) != null)
                    {
                        return Send(Session.SelectHotbar(i));
                    }
                }
            }

            return false;
        }

        void GatherStep(ClientWorld w, double time)
        {
            Vector3 me = Session.PredictedState.Position;
            if (m_TargetNode < 0 || !w.Nodes.IsAlive(m_TargetNode))
            {
                m_TargetNode = w.Nodes.FindNearest(me, 80f, n => n.Def.Kind == GatherKind.Wood);
                m_LastProgressAt = time;
                m_LastProgressPos = me;
                if (m_TargetNode < 0)
                {
                    WanderStep(time, 0.016);
                    return;
                }
            }

            NodePlacement target = w.Nodes.Placement(m_TargetNode);
            Vector3 to = target.Position - me;
            float distance = new Vector2(to.X, to.Z).Length() - target.Def.Size.X * 0.5f;
            m_Yaw = (float)(Math.Atan2(to.X, to.Z) * 180.0 / Math.PI);
            if (time < m_UnstickUntil)
            {
                Session.SetInput(new Vector2(0.7f, 0.7f), m_Yaw + 70f, 0, InputButtons.None);
                return;
            }

            if (distance > 1.6f)
            {
                if (Vector3.Distance(me, m_LastProgressPos) > 1f)
                {
                    m_LastProgressPos = me;
                    m_LastProgressAt = time;
                }
                else if (time - m_LastProgressAt > 2.5)
                {
                    m_UnstickUntil = time + 1.2; // blocked: sidestep, then try again
                    m_LastProgressAt = time + 1.2;
                }

                Session.SetInput(new Vector2(0, 1), m_Yaw, 0, distance > 15 ? InputButtons.Sprint : InputButtons.None);
                return;
            }

            Vector3 eye = Session.EyePosition;
            Vector3 aim = target.Position + new Vector3(0, 1.2f, 0) - eye;
            m_Pitch = (float)(Math.Atan2(aim.Y, Math.Sqrt(aim.X * aim.X + aim.Z * aim.Z)) * 180.0 / Math.PI);
            Session.SetInput(Vector2.Zero, m_Yaw, m_Pitch, InputButtons.Attack);
        }

        void DrinkStep(ClientWorld w, double time)
        {
            Vector3 me = Session.PredictedState.Position;
            if (w.Map.IsInPond(me, 1f))
            {
                if (time >= m_NextAction)
                {
                    m_NextAction = time + 0.5;
                    Send(Session.Interact(InteractTarget.Pond, 0));
                }

                if (w.Owner.Thirst > 90)
                {
                    m_Goal = m_Gatherer ? Goal.Gather : Goal.Wander;
                }

                Session.SetInput(Vector2.Zero, m_Yaw, 0, InputButtons.None);
                return;
            }

            var pond = w.Map.Ponds.OrderBy(p => Vector3.DistanceSquared((p.Min + p.Max) * 0.5f, me)).First();
            Vector3 to = (pond.Min + pond.Max) * 0.5f - me;
            m_Yaw = (float)(Math.Atan2(to.X, to.Z) * 180.0 / Math.PI);
            Session.SetInput(new Vector2(0, 1), m_Yaw, 0, InputButtons.Sprint);
        }

        void WanderStep(double time, double dt)
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
                m_Yaw = (float)(Math.Atan2(-p.X, -p.Z) * 180.0 / Math.PI);
            }
            else
            {
                m_Yaw += m_TurnRate * (float)dt;
            }

            Session.SetInput(m_Move, m_Yaw, 0f, m_Held);
        }

        bool Send(ushort request)
        {
            if (request != 0)
            {
                ActionsSent++;
            }

            return request != 0;
        }

        public void Dispose() => Session.Dispose();
    }
}
