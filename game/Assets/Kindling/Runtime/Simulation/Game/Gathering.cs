using System;
using System.Numerics;
using Kindling.Content;
using Kindling.Simulation.Items;
using Kindling.Simulation.World;

namespace Kindling.Simulation.Game
{
    public sealed partial class GameWorld
    {
        const float BreakableDamagePerHit = 10f;
        const float BreakableDamageBareHands = 4f;

        /// <summary>
        /// One swing with the active item (or bare hands) along the view direction, triggered by the
        /// Attack input. Hits the nearest resource node or breakable within reach. Swing speed is limited
        /// by the tool's cooldown; checks run against the server's own position and view.
        /// </summary>
        public ActionError Swing(Character c, float yaw, float pitch)
        {
            if (c.Life == LifeState.Dead)
            {
                return ActionError.Dead;
            }

            if (c.Life == LifeState.Downed)
            {
                return ActionError.Downed;
            }

            if (Now < c.NextSwingAt - 1e-6)
            {
                return ActionError.Cooldown;
            }

            ItemStack held = c.Inventory.ActiveItem;
            ToolStats tool = held.IsEmpty ? null : held.Def.Tool;
            if (!held.IsEmpty && tool == null)
            {
                return ActionError.NotUsable; // holding something that isn't a tool (e.g. food)
            }

            float swing = tool?.SwingSeconds ?? Rules.Gathering.HandSwingSeconds;
            float reach = tool?.Reach ?? Rules.Gathering.HandReach;
            c.NextSwingAt = Now + swing;

            Vector3 eye = EyeOf(c);
            Vector3 dir = ViewDirection(yaw, pitch);
            bool hitNode = Resources.RayCast(eye, dir, reach, out int node, out Vector3 nodePoint);
            WorldEntity breakable = RayCastBreakable(eye, dir, reach, out float breakableDistance);
            if (breakable != null && (!hitNode || breakableDistance < Vector3.Distance(eye, nodePoint)))
            {
                return HitBreakable(c, breakable, tool != null);
            }

            if (!hitNode)
            {
                return ActionError.NothingThere;
            }

            return HitNode(c, node, nodePoint, held, tool);
        }

        ActionError HitNode(Character c, int node, Vector3 point, ItemStack held, ToolStats tool)
        {
            NodePlacement p = Resources.Placement(node);
            if (p.Def.Pickable)
            {
                return ActionError.NotUsable; // plants are picked with Interact
            }

            ToolPower power = tool?.PowerFor(p.Def.Kind);
            if (tool == null && p.Def.Kind == GatherKind.Wood)
            {
                power = new ToolPower { Tier = 0, Yield = Rules.Gathering.HandYield };
            }

            if (power == null || power.Tier < p.Def.MinTier)
            {
                c.Notify(ActionError.NotPermitted);
                return ActionError.NotPermitted;
            }

            Vector3 sweet = Resources.SweetSpot(node);
            bool sweetHit = Vector3.Distance(point, sweet) <= Rules.Gathering.SweetSpotRadius;
            float amount = power.Yield * (sweetHit ? Rules.Gathering.SweetSpotBonus : 1f);
            float taken = Resources.Harvest(node, amount, Now);
            int items = (int)Math.Round(taken * Config.GatherMultiplier);
            if (items > 0)
            {
                Give(c, p.Def.Primary, items);
            }

            foreach (BonusYield bonus in p.Def.Bonus)
            {
                if (Rng.NextFloat() < bonus.Chance)
                {
                    Give(c, bonus.Item, Math.Max(1, (int)Math.Round(bonus.Amount * Config.GatherMultiplier)));
                }
            }

            WearActiveTool(c, held, tool);
            c.Events.Add(new GameEvent
            {
                Kind = GameEventKind.GatherHit,
                Target = node,
                ItemNetId = p.Def.Primary.NetId,
                Amount = items,
                Flag = sweetHit,
                Position = Resources.IsAlive(node) ? Resources.SweetSpot(node) : point,
                Code = Resources.IsAlive(node) ? 0 : 1,
            });
            return ActionError.None;
        }

        void WearActiveTool(Character c, ItemStack held, ToolStats tool)
        {
            if (tool == null || !held.Def.HasCondition || tool.ConditionPerHit <= 0)
            {
                return;
            }

            int slot = c.Inventory.ActiveSlot;
            ItemStack now = c.Inventory.Hotbar[slot];
            now.Condition -= tool.ConditionPerHit;
            if (now.Condition <= 0)
            {
                c.Inventory.Hotbar.Set(slot, ItemStack.Empty);
                c.Events.Add(new GameEvent { Kind = GameEventKind.ToolBroke, ItemNetId = held.Def.NetId });
            }
            else
            {
                c.Inventory.Hotbar.Set(slot, now);
            }
        }

        WorldEntity RayCastBreakable(Vector3 eye, Vector3 dir, float reach, out float distance)
        {
            distance = float.MaxValue;
            WorldEntity best = null;
            Entities.Query(eye, reach + 2f, m_EntityScratch, e => e.Def.Kind == EntityKind.Breakable);
            foreach (WorldEntity e in m_EntityScratch)
            {
                if (ResourceField.RayAabb(eye, dir, BoundsOf(e), out float t) && t <= reach && t < distance)
                {
                    distance = t;
                    best = e;
                }
            }

            return best;
        }

        ActionError HitBreakable(Character c, WorldEntity e, bool withTool)
        {
            e.Health -= withTool ? BreakableDamagePerHit : BreakableDamageBareHands;
            if (e.Health > 0)
            {
                Entities.MarkChanged(e);
                return ActionError.None;
            }

            LootRoller.Roll(e.Def.LootTable, Rng, m_StackScratch);
            for (int i = 0; i < m_StackScratch.Count; i++)
            {
                float angle = i * 2.4f;
                DropAt(e.Position + new Vector3((float)Math.Sin(angle) * 0.6f, 0f, (float)Math.Cos(angle) * 0.6f), m_StackScratch[i]);
            }

            OnLootEntityGone(e);
            Entities.Remove(e.Id);
            return ActionError.None;
        }
    }
}
