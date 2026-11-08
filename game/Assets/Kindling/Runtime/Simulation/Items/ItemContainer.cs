using System;
using System.Collections.Generic;
using Kindling.Content;

namespace Kindling.Simulation.Items
{
    public enum ContainerRole : byte
    {
        Main,
        Hotbar,

        /// <summary>Worn clothing: slot i only accepts items for <see cref="EquipSlot"/> (i + 1).</summary>
        Wear,
        Storage,
    }

    /// <summary>
    /// A fixed number of item slots. Every change bumps <see cref="Version"/> and marks the slot dirty,
    /// which drives replication to viewers and saving.
    /// </summary>
    public sealed class ItemContainer
    {
        readonly ItemStack[] m_Slots;
        readonly bool[] m_Dirty;
        bool m_AnyDirty;

        public ItemContainer(ContainerRole role, int capacity)
        {
            if (capacity < 0 || capacity > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Role = role;
            m_Slots = new ItemStack[capacity];
            m_Dirty = new bool[capacity];
        }

        public ContainerRole Role { get; }

        public int Capacity => m_Slots.Length;

        public uint Version { get; private set; }

        public bool AnyDirty => m_AnyDirty;

        public ItemStack this[int slot] => m_Slots[slot];

        public bool IsValidSlot(int slot) => slot >= 0 && slot < m_Slots.Length;

        public bool CanAccept(int slot, ItemDef def)
        {
            if (!IsValidSlot(slot) || def == null)
            {
                return false;
            }

            return Role != ContainerRole.Wear || (def.Slot != EquipSlot.None && (int)def.Slot == slot + 1);
        }

        public void Set(int slot, ItemStack stack)
        {
            if (stack.IsEmpty)
            {
                stack = ItemStack.Empty;
            }

            if (m_Slots[slot].Equals(stack) && m_Slots[slot].Def == stack.Def)
            {
                return;
            }

            m_Slots[slot] = stack;
            m_Dirty[slot] = true;
            m_AnyDirty = true;
            Version++;
        }

        /// <summary>Adds items, filling existing stacks first, then empty slots. Returns how many didn't fit.</summary>
        public int Add(ItemDef def, int amount, float condition = -1f)
        {
            if (def == null || amount <= 0)
            {
                return Math.Max(0, amount);
            }

            float cond = condition < 0 ? def.MaxCondition : condition;
            if (def.IsStackable)
            {
                for (int i = 0; i < m_Slots.Length && amount > 0; i++)
                {
                    ItemStack s = m_Slots[i];
                    if (!s.IsEmpty && s.Def == def && s.Amount < def.StackSize)
                    {
                        int put = Math.Min(amount, def.StackSize - s.Amount);
                        Set(i, s.WithAmount(s.Amount + put));
                        amount -= put;
                    }
                }
            }

            for (int i = 0; i < m_Slots.Length && amount > 0; i++)
            {
                if (m_Slots[i].IsEmpty && CanAccept(i, def))
                {
                    int put = Math.Min(amount, def.StackSize);
                    Set(i, new ItemStack(def, put, cond));
                    amount -= put;
                }
            }

            return amount;
        }

        /// <summary>How many of <paramref name="amount"/> would fit (without changing anything).</summary>
        public int SpaceFor(ItemDef def, int amount)
        {
            int space = 0;
            for (int i = 0; i < m_Slots.Length && space < amount; i++)
            {
                ItemStack s = m_Slots[i];
                if (s.IsEmpty)
                {
                    if (CanAccept(i, def))
                    {
                        space += def.StackSize;
                    }
                }
                else if (s.Def == def && def.IsStackable)
                {
                    space += def.StackSize - s.Amount;
                }
            }

            return Math.Min(space, amount);
        }

        public int Count(ItemDef def)
        {
            int total = 0;
            foreach (ItemStack s in m_Slots)
            {
                if (!s.IsEmpty && s.Def == def)
                {
                    total += s.Amount;
                }
            }

            return total;
        }

        /// <summary>Removes up to <paramref name="amount"/>, taking from the last slots first. Returns how many were removed.</summary>
        public int Remove(ItemDef def, int amount)
        {
            int removed = 0;
            for (int i = m_Slots.Length - 1; i >= 0 && removed < amount; i--)
            {
                ItemStack s = m_Slots[i];
                if (!s.IsEmpty && s.Def == def)
                {
                    int take = Math.Min(amount - removed, s.Amount);
                    Set(i, s.WithAmount(s.Amount - take));
                    removed += take;
                }
            }

            return removed;
        }

        /// <summary>Takes up to <paramref name="amount"/> from one slot.</summary>
        public ItemStack Take(int slot, int amount)
        {
            ItemStack s = m_Slots[slot];
            if (s.IsEmpty || amount <= 0)
            {
                return ItemStack.Empty;
            }

            int take = Math.Min(amount, s.Amount);
            Set(slot, s.WithAmount(s.Amount - take));
            return s.WithAmount(take);
        }

        public int FirstEmptySlot(ItemDef forDef = null)
        {
            for (int i = 0; i < m_Slots.Length; i++)
            {
                if (m_Slots[i].IsEmpty && (forDef == null || CanAccept(i, forDef)))
                {
                    return i;
                }
            }

            return -1;
        }

        public bool IsEmpty
        {
            get
            {
                foreach (ItemStack s in m_Slots)
                {
                    if (!s.IsEmpty)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < m_Slots.Length; i++)
            {
                Set(i, ItemStack.Empty);
            }
        }

        /// <summary>Moves every item into <paramref name="target"/>; returns false if some didn't fit (they stay here).</summary>
        public bool MoveAllTo(ItemContainer target)
        {
            bool all = true;
            for (int i = 0; i < m_Slots.Length; i++)
            {
                ItemStack s = m_Slots[i];
                if (s.IsEmpty)
                {
                    continue;
                }

                int left = target.Add(s.Def, s.Amount, s.Condition);
                Set(i, s.WithAmount(left));
                all &= left == 0;
            }

            return all;
        }

        /// <summary>Collects and clears the dirty slot list (replication).</summary>
        public void ConsumeDirty(List<int> slots)
        {
            slots.Clear();
            if (!m_AnyDirty)
            {
                return;
            }

            for (int i = 0; i < m_Dirty.Length; i++)
            {
                if (m_Dirty[i])
                {
                    slots.Add(i);
                    m_Dirty[i] = false;
                }
            }

            m_AnyDirty = false;
        }

        /// <summary>Marks every slot dirty (a new viewer needs the full contents).</summary>
        public void MarkAllDirty()
        {
            for (int i = 0; i < m_Dirty.Length; i++)
            {
                m_Dirty[i] = true;
            }

            m_AnyDirty = m_Dirty.Length > 0;
        }
    }
}
