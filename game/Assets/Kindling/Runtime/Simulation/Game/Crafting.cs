using System;
using Kindling.Content;
using Kindling.Simulation.World;

namespace Kindling.Simulation.Game
{
    public sealed partial class GameWorld
    {
        /// <summary>
        /// Queues <paramref name="count"/> crafts. Ingredients for all of them are taken now (cancelling
        /// refunds them). Benches must be in range when crafting starts; crafting continues while you move.
        /// </summary>
        public ActionError Craft(Character c, RecipeDef recipe, int count)
        {
            if (!c.IsAlive)
            {
                return c.Life == LifeState.Dead ? ActionError.Dead : ActionError.Downed;
            }

            if (recipe == null)
            {
                return ActionError.UnknownRecipe;
            }

            if (count < 1 || count > 100)
            {
                return ActionError.InvalidAmount;
            }

            if (!c.Knows(recipe))
            {
                return ActionError.NotKnown;
            }

            if (recipe.Station != StationKind.Hand && !StationNearby(c, recipe.Station))
            {
                return ActionError.StationMissing;
            }

            if (c.Crafting.Count >= Rules.Crafting.QueueSize)
            {
                return ActionError.QueueFull;
            }

            if (!c.Inventory.Has(recipe.Inputs, count))
            {
                return ActionError.NotEnoughItems;
            }

            foreach (ItemAmount input in recipe.Inputs)
            {
                c.Inventory.Remove(input.Item, input.Amount * count);
            }

            c.Crafting.Add(new CraftJob { Id = c.NextCraftJobId++, Recipe = recipe, Remaining = count });
            c.StateVersion++;
            return ActionError.None;
        }

        /// <summary>Cancels a queued job and refunds the ingredients of everything not yet made.</summary>
        public ActionError CancelCraft(Character c, int jobId)
        {
            int index = c.Crafting.FindIndex(j => j.Id == jobId);
            if (index < 0)
            {
                return ActionError.InvalidTarget;
            }

            CraftJob job = c.Crafting[index];
            c.Crafting.RemoveAt(index);
            Refund(c, job);
            c.StateVersion++;
            return ActionError.None;
        }

        void CancelAllCrafting(Character c)
        {
            foreach (CraftJob job in c.Crafting)
            {
                Refund(c, job);
            }

            if (c.Crafting.Count > 0)
            {
                c.Crafting.Clear();
                c.StateVersion++;
            }
        }

        void Refund(Character c, CraftJob job)
        {
            foreach (ItemAmount input in job.Recipe.Inputs)
            {
                Give(c, input.Item, input.Amount * job.Remaining);
            }
        }

        void UpdateCrafting(Character c, float dt)
        {
            if (c.Crafting.Count == 0 || !c.IsAlive)
            {
                return;
            }

            CraftJob job = c.Crafting[0];
            job.Progress += dt;
            if (job.Progress < job.Recipe.Seconds)
            {
                return;
            }

            job.Progress = 0;
            job.Remaining--;
            Give(c, job.Recipe.Output.Item, job.Recipe.Output.Amount);
            c.Events.Add(new GameEvent { Kind = GameEventKind.CraftFinished, ItemNetId = job.Recipe.Output.Item.NetId, Amount = job.Recipe.Output.Amount });
            if (job.Remaining <= 0)
            {
                c.Crafting.RemoveAt(0);
            }

            c.StateVersion++;
        }

        public bool StationNearby(Character c, StationKind station)
        {
            Entities.Query(c.Motion.Position, Rules.Crafting.StationRange + 2f, m_EntityScratch, e => e.Def.IsStation && e.Def.Station == station);
            foreach (WorldEntity e in m_EntityScratch)
            {
                if (InReach(c, e, Rules.Crafting.StationRange))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
