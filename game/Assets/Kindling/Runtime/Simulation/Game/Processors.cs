using System;
using Kindling.Content;
using Kindling.Simulation.Items;
using Kindling.Simulation.World;

namespace Kindling.Simulation.Game
{
    public sealed partial class GameWorld
    {
        /// <summary>Lights or puts out a campfire/smelter. Lighting needs fuel inside.</summary>
        public ActionError ToggleProcessor(Character c, uint entityId)
        {
            if (!c.IsAlive)
            {
                return ActionError.Dead;
            }

            WorldEntity e = Entities.Get(entityId);
            if (e == null || e.Def.Processor == null)
            {
                return ActionError.InvalidTarget;
            }

            if (!InReach(c, e, Rules.Crafting.InteractRange + 1f))
            {
                return ActionError.TooFar;
            }

            if (e.IsLit)
            {
                e.Flags &= ~WorldEntityFlags.Lit;
            }
            else
            {
                if (e.FuelLeft <= 0 && FindFuelSlot(e.Container) < 0)
                {
                    return ActionError.NotEnoughItems;
                }

                e.Flags |= WorldEntityFlags.Lit;
            }

            Entities.MarkChanged(e);
            return ActionError.None;
        }

        void UpdateProcessors(float dt)
        {
            foreach (WorldEntity e in Entities.All)
            {
                if (e.IsLit && e.Def.Processor != null)
                {
                    StepProcessor(e, dt);
                }
            }
        }

        /// <summary>Burns fuel (making the byproduct) and runs one conversion at a time while fuel lasts.</summary>
        void StepProcessor(WorldEntity e, float dt)
        {
            ProcessorDef p = e.Def.Processor;
            ItemContainer box = e.Container;
            float remaining = dt;
            while (remaining > 0)
            {
                if (e.FuelLeft <= 0)
                {
                    int fuel = FindFuelSlot(box);
                    if (fuel < 0)
                    {
                        e.Flags &= ~WorldEntityFlags.Lit;
                        e.Progress = 0;
                        Entities.MarkChanged(e);
                        return;
                    }

                    ItemDef fuelDef = box[fuel].Def;
                    box.Take(fuel, 1);
                    e.FuelLeft += fuelDef.FuelSeconds;
                    if (p.Byproduct != null && p.Byproduct != fuelDef)
                    {
                        box.Add(p.Byproduct, 1); // no room: the charcoal is lost
                    }
                }

                float step = Math.Min(remaining, e.FuelLeft);
                e.FuelLeft -= step;
                remaining -= step;
                Convert(e, p, step);
            }
        }

        void Convert(WorldEntity e, ProcessorDef p, float seconds)
        {
            ItemContainer box = e.Container;
            Conversion conversion = null;
            int inputSlot = -1;
            for (int i = 0; i < box.Capacity && conversion == null; i++)
            {
                ItemStack s = box[i];
                if (!s.IsEmpty)
                {
                    conversion = p.ConversionFor(s.Def);
                    inputSlot = conversion != null ? i : -1;
                }
            }

            if (conversion == null)
            {
                e.Progress = 0;
                return;
            }

            e.Progress += seconds;
            while (e.Progress >= conversion.Seconds && !box[inputSlot].IsEmpty && box[inputSlot].Def == conversion.Input)
            {
                if (box.SpaceFor(conversion.Output, conversion.OutputAmount) < conversion.OutputAmount)
                {
                    e.Progress = conversion.Seconds; // full: wait for space
                    return;
                }

                e.Progress -= conversion.Seconds;
                box.Take(inputSlot, 1);
                box.Add(conversion.Output, conversion.OutputAmount);
            }
        }

        static int FindFuelSlot(ItemContainer box)
        {
            if (box == null)
            {
                return -1;
            }

            for (int i = 0; i < box.Capacity; i++)
            {
                if (!box[i].IsEmpty && box[i].Def.FuelSeconds > 0)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
