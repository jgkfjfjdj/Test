using System;
using System.Collections.Generic;
using System.Numerics;
using Kindling.Content;
using Kindling.Simulation.Collision;
using Kindling.Simulation.Items;
using Kindling.Simulation.World;

namespace Kindling.Simulation.Game
{
    public sealed partial class GameWorld
    {
        const float DeployReach = 4f;
        const float PondDrinkThirst = 25f;
        readonly List<int> m_CollisionScratch = new List<int>();

        public ActionError MoveItem(Character c, ContainerRef from, int fromSlot, ContainerRef to, int toSlot, int amount)
        {
            if (!c.IsAlive)
            {
                return ActionError.Dead;
            }

            ItemContainer src = Resolve(c, from, out ActionError e1);
            if (src == null)
            {
                return e1;
            }

            ItemContainer dst = Resolve(c, to, out ActionError e2);
            if (dst == null)
            {
                return e2;
            }

            ActionError result = toSlot < 0 ? InventoryOps.QuickMove(src, fromSlot, dst) : InventoryOps.Move(src, fromSlot, dst, toSlot, amount);
            TouchEntityContainer(from);
            TouchEntityContainer(to);
            return result;
        }

        void TouchEntityContainer(ContainerRef r)
        {
            if (r.Kind == ContainerKind.Entity && Entities.Get(r.EntityId) is WorldEntity e)
            {
                Entities.MarkChanged(e);
            }
        }

        public ActionError Drop(Character c, ContainerRef from, int slot, int amount)
        {
            if (c.Life == LifeState.Dead)
            {
                return ActionError.Dead;
            }

            ItemContainer src = Resolve(c, from, out ActionError error);
            if (src == null)
            {
                return error;
            }

            if (!src.IsValidSlot(slot))
            {
                return ActionError.InvalidSlot;
            }

            if (src[slot].IsEmpty)
            {
                return ActionError.EmptySlot;
            }

            if (amount <= 0 || amount > src[slot].Amount)
            {
                return ActionError.InvalidAmount;
            }

            ItemStack taken = src.Take(slot, amount);
            DropAt(c.Motion.Position + Forward(c) * 1.0f, taken);
            TouchEntityContainer(from);
            return ActionError.None;
        }

        public ActionError SelectHotbar(Character c, int slot)
        {
            if (slot < -1 || slot >= CharacterInventory.HotbarSlots)
            {
                return ActionError.InvalidSlot;
            }

            c.Inventory.SelectSlot(slot);
            c.StateVersion++;
            return ActionError.None;
        }

        /// <summary>Eat, drink, bandage, or put on clothing from the main inventory or hotbar.</summary>
        public ActionError UseItem(Character c, ContainerRef from, int slot)
        {
            if (!c.IsAlive)
            {
                return c.Life == LifeState.Dead ? ActionError.Dead : ActionError.Downed;
            }

            if (from.Kind != ContainerKind.Main && from.Kind != ContainerKind.Hotbar)
            {
                return ActionError.NotUsable;
            }

            ItemContainer src = Resolve(c, from, out ActionError error);
            if (src == null)
            {
                return error;
            }

            if (!src.IsValidSlot(slot))
            {
                return ActionError.InvalidSlot;
            }

            ItemStack s = src[slot];
            if (s.IsEmpty)
            {
                return ActionError.EmptySlot;
            }

            if (s.Def.Slot != EquipSlot.None)
            {
                return InventoryOps.Move(src, slot, c.Inventory.Wear, (int)s.Def.Slot - 1, 1);
            }

            ConsumeEffect effect = s.Def.Consume;
            if (effect == null)
            {
                return ActionError.NotUsable;
            }

            src.Take(slot, 1);
            Apply(c, effect);
            return ActionError.None;
        }

        void Apply(Character c, ConsumeEffect effect)
        {
            SurvivalStats st = c.Stats;
            st.Health = Math.Min(Rules.Survival.MaxHealth, st.Health + effect.Health);
            st.Hunger = Math.Max(0f, Math.Min(100f, st.Hunger + effect.Hunger));
            st.Thirst = Math.Max(0f, Math.Min(100f, st.Thirst + effect.Thirst));
            if (effect.PoisonChance > 0 && Rng.NextFloat() < effect.PoisonChance)
            {
                st.PoisonLeft = Rules.Survival.PoisonSeconds;
                st.Set(StatusEffect.Poisoned, true);
            }

            if (effect.StopsBleeding)
            {
                st.Set(StatusEffect.Bleeding, false);
            }

            if (effect.HealOverTime > 0)
            {
                st.HealLeft += effect.HealOverTime;
                st.HealRate = effect.HealOverTime / effect.HealSeconds;
            }

            c.StateVersion++;
        }

        /// <summary>Places the deployable in a hotbar slot at a position in front of the player (on the ground, not overlapping anything).</summary>
        public ActionError Deploy(Character c, int hotbarSlot, Vector3 position, float yaw)
        {
            if (!c.IsAlive)
            {
                return ActionError.Dead;
            }

            if (hotbarSlot < 0 || hotbarSlot >= CharacterInventory.HotbarSlots)
            {
                return ActionError.InvalidSlot;
            }

            ItemStack s = c.Inventory.Hotbar[hotbarSlot];
            if (s.IsEmpty)
            {
                return ActionError.EmptySlot;
            }

            if (s.Def.Deploys == null)
            {
                return ActionError.NotUsable;
            }

            if (float.IsNaN(position.X) || float.IsNaN(position.Z) || float.IsNaN(yaw))
            {
                return ActionError.InvalidPlacement;
            }

            float dx = position.X - c.Motion.Position.X;
            float dz = position.Z - c.Motion.Position.Z;
            if (dx * dx + dz * dz > DeployReach * DeployReach)
            {
                return ActionError.TooFar;
            }

            yaw = (float)(Math.Round(yaw / 90.0) * 90.0 % 360.0);
            if (yaw < 0)
            {
                yaw += 360f;
            }

            var probe = new WorldEntityProbe(s.Def.Deploys, new Vector3(position.X, 0f, position.Z), yaw);
            Aabb bounds = probe.Bounds;
            if (Math.Abs(bounds.Min.X) > Map.Static.MapSize / 2 - 3 || Math.Abs(bounds.Min.Z) > Map.Static.MapSize / 2 - 3 || Map.Static.IsInPond(probe.Position, 0.5f))
            {
                return ActionError.InvalidPlacement;
            }

            if (Collision.Overlaps(new Aabb(bounds.Min + new Vector3(0f, 0.01f, 0f), bounds.Max), m_CollisionScratch))
            {
                return ActionError.InvalidPlacement;
            }

            Entities.Query(probe.Position, 4f, m_EntityScratch, e => e.Def.Kind != EntityKind.DroppedItem);
            foreach (WorldEntity other in m_EntityScratch)
            {
                if (BoundsOf(other).Intersects(bounds))
                {
                    return ActionError.InvalidPlacement;
                }
            }

            c.Inventory.Hotbar.Take(hotbarSlot, 1);
            WorldEntity placed = Entities.Spawn(s.Def.Deploys, probe.Position, yaw);
            placed.OwnerId = c.AccountId;
            Entities.MarkChanged(placed);
            return ActionError.None;
        }

        /// <summary>
        /// Use what the player is looking at: pick a plant, pick up a dropped item, open a container,
        /// drink from a pond.
        /// </summary>
        public ActionError Interact(Character c, InteractTarget target, uint id)
        {
            if (!c.IsAlive)
            {
                return c.Life == LifeState.Dead ? ActionError.Dead : ActionError.Downed;
            }

            float range = Rules.Crafting.InteractRange + 0.5f;
            switch (target)
            {
                case InteractTarget.Node:
                {
                    if (id >= Resources.Count)
                    {
                        return ActionError.InvalidTarget;
                    }

                    int node = (int)id;
                    NodePlacement p = Resources.Placement(node);
                    if (!p.Def.Pickable)
                    {
                        return ActionError.NotUsable;
                    }

                    if (!Resources.IsAlive(node))
                    {
                        return ActionError.NothingThere;
                    }

                    if (Vector3.Distance(EyeOf(c), p.Position + new Vector3(0f, p.Def.Size.Y * 0.5f, 0f)) > range + 1f)
                    {
                        return ActionError.TooFar;
                    }

                    float taken = Resources.Harvest(node, Resources.Remaining(node), Now);
                    Give(c, p.Def.Primary, Math.Max(1, (int)Math.Round(taken * Config.GatherMultiplier)));
                    return ActionError.None;
                }

                case InteractTarget.Entity:
                {
                    WorldEntity e = Entities.Get(id);
                    if (e == null)
                    {
                        return ActionError.NothingThere;
                    }

                    if (!InReach(c, e, range))
                    {
                        return ActionError.TooFar;
                    }

                    if (e.Def.Kind == EntityKind.DroppedItem)
                    {
                        ItemStack s = e.Container[0];
                        int left = c.Inventory.Add(s.Def, s.Amount, s.Condition);
                        if (left == s.Amount)
                        {
                            return ActionError.NotEnoughSpace;
                        }

                        c.Events.Add(new GameEvent { Kind = GameEventKind.ItemsGained, ItemNetId = s.Def.NetId, Amount = s.Amount - left });
                        e.Container.Set(0, s.WithAmount(left));
                        if (left == 0)
                        {
                            Entities.Remove(e.Id);
                        }
                        else
                        {
                            Entities.MarkChanged(e);
                        }

                        return ActionError.None;
                    }

                    if (e.Container != null && e.Def.Kind != EntityKind.Sleeper)
                    {
                        c.OpenContainerId = e.Id;
                        c.StateVersion++;
                        return ActionError.None;
                    }

                    return ActionError.NotUsable;
                }

                case InteractTarget.Pond:
                {
                    if (!Map.Static.IsInPond(c.Motion.Position, 1.5f))
                    {
                        return ActionError.TooFar;
                    }

                    c.Stats.Thirst = Math.Min(100f, c.Stats.Thirst + PondDrinkThirst);
                    c.StateVersion++;
                    return ActionError.None;
                }

                default:
                    return ActionError.InvalidTarget;
            }
        }

        public void CloseContainer(Character c)
        {
            if (c.OpenContainerId != 0)
            {
                c.OpenContainerId = 0;
                c.StateVersion++;
            }
        }

        /// <summary>The open container, or null if it's gone or out of range (then it's closed).</summary>
        public WorldEntity OpenContainerOf(Character c)
        {
            if (c.OpenContainerId == 0)
            {
                return null;
            }

            WorldEntity e = Entities.Get(c.OpenContainerId);
            if (e == null || e.Container == null || !c.IsAlive || !InReach(c, e, Rules.Crafting.InteractRange + 1.5f))
            {
                CloseContainer(c);
                return null;
            }

            return e;
        }

        readonly struct WorldEntityProbe
        {
            public readonly Vector3 Position;
            public readonly Aabb Bounds;

            public WorldEntityProbe(EntityDef def, Vector3 position, float yaw)
            {
                Position = position;
                bool sideways = ((int)Math.Round(yaw / 90f) & 1) == 1;
                float sx = sideways ? def.Size.Z : def.Size.X;
                float sz = sideways ? def.Size.X : def.Size.Z;
                Bounds = new Aabb(position - new Vector3(sx / 2, 0f, sz / 2), position + new Vector3(sx / 2, def.Size.Y, sz / 2));
            }
        }
    }
}
