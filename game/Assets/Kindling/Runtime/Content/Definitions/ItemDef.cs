using System.Collections.Generic;

namespace Kindling.Content
{
    public sealed class ToolPower
    {
        /// <summary>Tier this tool counts as for the gather kind (0 = bare hands level).</summary>
        public int Tier;

        /// <summary>Resource units removed from a node per hit (before multipliers).</summary>
        public float Yield;
    }

    public sealed class ToolStats
    {
        public float SwingSeconds = 1f;
        public float Reach = 2.5f;

        /// <summary>Condition lost per successful hit.</summary>
        public float ConditionPerHit = 1f;

        public readonly Dictionary<GatherKind, ToolPower> Power = new Dictionary<GatherKind, ToolPower>();

        public ToolPower PowerFor(GatherKind kind) => Power.TryGetValue(kind, out ToolPower p) ? p : null;
    }

    public sealed class ConsumeEffect
    {
        public float Health;
        public float Hunger;
        public float Thirst;

        /// <summary>Chance (0..1) of becoming poisoned.</summary>
        public float PoisonChance;

        public bool StopsBleeding;

        /// <summary>Health restored gradually over <see cref="HealSeconds"/>.</summary>
        public float HealOverTime;

        public float HealSeconds;
    }

    public sealed class ItemDef : ContentDef
    {
        public ItemCategory Category;
        public int StackSize = 1;
        public float Weight;

        /// <summary>Maximum condition; 0 means the item has no durability.</summary>
        public float MaxCondition;

        public ToolStats Tool;
        public ConsumeEffect Consume;
        public EquipSlot Slot;

        /// <summary>Warmth added while worn.</summary>
        public float Insulation;

        /// <summary>Seconds of burn time when used as fuel (0 = not a fuel).</summary>
        public float FuelSeconds;

        public ContentKey DeploysKey;

        /// <summary>The entity this item places (deployables only).</summary>
        public EntityDef Deploys;

        public bool HasCondition => MaxCondition > 0;

        public bool IsStackable => StackSize > 1;
    }
}
