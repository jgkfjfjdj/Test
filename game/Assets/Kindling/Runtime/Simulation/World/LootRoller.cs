using System;
using System.Collections.Generic;
using Kindling.Content;
using Kindling.Core.Numerics;
using Kindling.Simulation.Items;

namespace Kindling.Simulation.World
{
    public static class LootRoller
    {
        /// <summary>Rolls a loot table: each roll picks one entry by weight, with an amount from min to max.</summary>
        public static void Roll(LootTableDef table, DeterministicRandom rng, List<ItemStack> results)
        {
            results.Clear();
            int rolls = rng.Range(table.MinRolls, table.MaxRolls + 1);
            float total = table.TotalWeight;
            for (int r = 0; r < rolls; r++)
            {
                float pick = rng.NextFloat() * total;
                LootEntry chosen = table.Entries[table.Entries.Count - 1];
                foreach (LootEntry e in table.Entries)
                {
                    pick -= e.Weight;
                    if (pick < 0)
                    {
                        chosen = e;
                        break;
                    }
                }

                results.Add(ItemStack.Of(chosen.Item, rng.Range(chosen.Min, chosen.Max + 1)));
            }
        }

        public static void Fill(ItemContainer container, LootTableDef table, DeterministicRandom rng, List<ItemStack> scratch)
        {
            container.Clear();
            Roll(table, rng, scratch);
            foreach (ItemStack s in scratch)
            {
                container.Add(s.Def, s.Amount, s.Condition);
            }
        }
    }
}
