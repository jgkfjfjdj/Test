using System;

namespace Kindling.Simulation.Items
{
    /// <summary>
    /// Item moves between containers. The server runs these on player requests; the client UI calls the
    /// same functions on a copy to pre-check (it never applies them itself).
    /// </summary>
    public static class InventoryOps
    {
        /// <summary>
        /// Moves <paramref name="amount"/> from one slot to another: into an empty slot (splitting if
        /// partial), merging into the same item (the rest stays), or swapping a whole stack with a
        /// different item.
        /// </summary>
        public static ActionError Move(ItemContainer src, int srcSlot, ItemContainer dst, int dstSlot, int amount)
        {
            if (src == null || dst == null || !src.IsValidSlot(srcSlot) || !dst.IsValidSlot(dstSlot))
            {
                return ActionError.InvalidSlot;
            }

            ItemStack from = src[srcSlot];
            if (from.IsEmpty)
            {
                return ActionError.EmptySlot;
            }

            if (amount <= 0 || amount > from.Amount)
            {
                return ActionError.InvalidAmount;
            }

            if (src == dst && srcSlot == dstSlot)
            {
                return ActionError.None;
            }

            if (!dst.CanAccept(dstSlot, from.Def))
            {
                return ActionError.SlotRestricted;
            }

            ItemStack to = dst[dstSlot];
            if (to.IsEmpty)
            {
                dst.Set(dstSlot, from.WithAmount(amount));
                src.Set(srcSlot, from.WithAmount(from.Amount - amount));
                return ActionError.None;
            }

            if (to.CanMergeWith(from))
            {
                int put = Math.Min(amount, to.SpaceLeft);
                if (put <= 0)
                {
                    return ActionError.NotEnoughSpace;
                }

                dst.Set(dstSlot, to.WithAmount(to.Amount + put));
                src.Set(srcSlot, from.WithAmount(from.Amount - put));
                return ActionError.None;
            }

            // Different items: swap whole stacks, if each slot accepts the other's item.
            if (amount != from.Amount)
            {
                return ActionError.NotEnoughSpace;
            }

            if (!src.CanAccept(srcSlot, to.Def))
            {
                return ActionError.SlotRestricted;
            }

            dst.Set(dstSlot, from);
            src.Set(srcSlot, to);
            return ActionError.None;
        }

        /// <summary>Moves a whole stack into another container (merging first, then the first free slots). Partial moves leave the rest.</summary>
        public static ActionError QuickMove(ItemContainer src, int srcSlot, ItemContainer dst)
        {
            if (src == null || dst == null || !src.IsValidSlot(srcSlot))
            {
                return ActionError.InvalidSlot;
            }

            ItemStack from = src[srcSlot];
            if (from.IsEmpty)
            {
                return ActionError.EmptySlot;
            }

            int left = dst.Add(from.Def, from.Amount, from.Condition);
            if (left == from.Amount)
            {
                return ActionError.NotEnoughSpace;
            }

            src.Set(srcSlot, from.WithAmount(left));
            return ActionError.None;
        }

        /// <summary>Splits off <paramref name="amount"/> into the first empty slot of the same container.</summary>
        public static ActionError Split(ItemContainer container, int slot, int amount)
        {
            if (container == null || !container.IsValidSlot(slot))
            {
                return ActionError.InvalidSlot;
            }

            ItemStack s = container[slot];
            if (s.IsEmpty)
            {
                return ActionError.EmptySlot;
            }

            if (amount <= 0 || amount >= s.Amount)
            {
                return ActionError.InvalidAmount;
            }

            int empty = container.FirstEmptySlot(s.Def);
            if (empty < 0)
            {
                return ActionError.NotEnoughSpace;
            }

            container.Set(empty, s.WithAmount(amount));
            container.Set(slot, s.WithAmount(s.Amount - amount));
            return ActionError.None;
        }
    }
}
