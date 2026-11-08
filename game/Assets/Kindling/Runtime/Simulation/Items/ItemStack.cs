using System;
using Kindling.Content;

namespace Kindling.Simulation.Items
{
    /// <summary>An amount of one item in a slot. Items with condition (tools, clothing) never stack.</summary>
    public struct ItemStack : IEquatable<ItemStack>
    {
        public static readonly ItemStack Empty = default;

        public ItemDef Def;
        public int Amount;

        /// <summary>Current condition for items with durability; 0 otherwise.</summary>
        public float Condition;

        public ItemStack(ItemDef def, int amount, float condition)
        {
            Def = def;
            Amount = amount;
            Condition = def != null && def.HasCondition ? condition : 0f;
        }

        public static ItemStack Of(ItemDef def, int amount = 1) => new ItemStack(def, amount, def?.MaxCondition ?? 0f);

        public bool IsEmpty => Def == null || Amount <= 0;

        public int SpaceLeft => IsEmpty ? 0 : Def.StackSize - Amount;

        public bool CanMergeWith(in ItemStack other) => !IsEmpty && !other.IsEmpty && Def == other.Def && Def.IsStackable;

        public ItemStack WithAmount(int amount) => amount <= 0 ? Empty : new ItemStack(Def, amount, Condition);

        public bool Equals(ItemStack o) => (IsEmpty && o.IsEmpty) || (Def == o.Def && Amount == o.Amount && Condition.Equals(o.Condition));

        public override bool Equals(object obj) => obj is ItemStack o && Equals(o);

        public override int GetHashCode() => IsEmpty ? 0 : HashCode.Combine(Def.NetId, Amount, Condition);

        public override string ToString() => IsEmpty ? "(empty)" : $"{Def.Key}×{Amount}" + (Def.HasCondition ? $" ({Condition:0.#})" : string.Empty);
    }
}
