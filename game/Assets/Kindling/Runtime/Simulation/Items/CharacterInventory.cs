using System;
using System.Collections.Generic;
using Kindling.Content;

namespace Kindling.Simulation.Items
{
    /// <summary>A character's items: 24 main slots, a 6-slot hotbar (one active) and 6 clothing slots.</summary>
    public sealed class CharacterInventory
    {
        public const int MainSlots = 24;
        public const int HotbarSlots = 6;
        public const int WearSlots = 6;

        public CharacterInventory()
        {
            Main = new ItemContainer(ContainerRole.Main, MainSlots);
            Hotbar = new ItemContainer(ContainerRole.Hotbar, HotbarSlots);
            Wear = new ItemContainer(ContainerRole.Wear, WearSlots);
        }

        public ItemContainer Main { get; }

        public ItemContainer Hotbar { get; }

        public ItemContainer Wear { get; }

        /// <summary>Selected hotbar slot, or -1 for bare hands.</summary>
        public int ActiveSlot { get; private set; } = -1;

        public ItemStack ActiveItem => ActiveSlot >= 0 ? Hotbar[ActiveSlot] : ItemStack.Empty;

        public IEnumerable<ItemContainer> Carried
        {
            get
            {
                yield return Hotbar;
                yield return Main;
            }
        }

        public IEnumerable<ItemContainer> All
        {
            get
            {
                yield return Main;
                yield return Hotbar;
                yield return Wear;
            }
        }

        public void SelectSlot(int slot) => ActiveSlot = slot >= 0 && slot < HotbarSlots ? slot : -1;

        /// <summary>
        /// Adds picked-up or crafted items: tools, weapons and deployables prefer the hotbar, everything
        /// else the main inventory. Returns how many didn't fit.
        /// </summary>
        public int Add(ItemDef def, int amount, float condition = -1f)
        {
            bool hotbarFirst = def.Category == ItemCategory.Tool || def.Category == ItemCategory.Weapon || def.Category == ItemCategory.Deployable || def.Category == ItemCategory.Medical;
            ItemContainer first = hotbarFirst ? Hotbar : Main;
            ItemContainer second = hotbarFirst ? Main : Hotbar;

            // Top up existing stacks anywhere before taking a new slot.
            if (def.IsStackable)
            {
                amount = TopUp(Main, def, amount);
                amount = TopUp(Hotbar, def, amount);
            }

            amount = first.Add(def, amount, condition);
            return second.Add(def, amount, condition);
        }

        static int TopUp(ItemContainer c, ItemDef def, int amount)
        {
            for (int i = 0; i < c.Capacity && amount > 0; i++)
            {
                ItemStack s = c[i];
                if (!s.IsEmpty && s.Def == def && s.Amount < def.StackSize)
                {
                    int put = Math.Min(amount, def.StackSize - s.Amount);
                    c.Set(i, s.WithAmount(s.Amount + put));
                    amount -= put;
                }
            }

            return amount;
        }

        public int SpaceFor(ItemDef def, int amount)
        {
            int space = Main.SpaceFor(def, amount);
            return Math.Min(amount, space + Hotbar.SpaceFor(def, amount - space));
        }

        /// <summary>Count in the main inventory and hotbar (worn items don't count for crafting).</summary>
        public int Count(ItemDef def) => Main.Count(def) + Hotbar.Count(def);

        /// <summary>Removes from the main inventory first, then the hotbar. Returns how many were removed.</summary>
        public int Remove(ItemDef def, int amount)
        {
            int removed = Main.Remove(def, amount);
            return removed + Hotbar.Remove(def, amount - removed);
        }

        public bool Has(IReadOnlyList<ItemAmount> items, int times = 1)
        {
            foreach (ItemAmount a in items)
            {
                if (Count(a.Item) < a.Amount * times)
                {
                    return false;
                }
            }

            return true;
        }

        public float Insulation
        {
            get
            {
                float total = 0;
                for (int i = 0; i < Wear.Capacity; i++)
                {
                    ItemStack s = Wear[i];
                    if (!s.IsEmpty)
                    {
                        total += s.Def.Insulation;
                    }
                }

                return total;
            }
        }

        public ItemContainer ByRole(ContainerRole role) => role switch
        {
            ContainerRole.Main => Main,
            ContainerRole.Hotbar => Hotbar,
            ContainerRole.Wear => Wear,
            _ => null,
        };

        /// <summary>Moves everything (worn items too) into <paramref name="target"/>, e.g. a body on death.</summary>
        public void MoveAllTo(ItemContainer target)
        {
            Hotbar.MoveAllTo(target);
            Main.MoveAllTo(target);
            Wear.MoveAllTo(target);
            ActiveSlot = -1;
        }

        public void Clear()
        {
            Main.Clear();
            Hotbar.Clear();
            Wear.Clear();
            ActiveSlot = -1;
        }
    }
}
