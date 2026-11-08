using System;

namespace Kindling.Content
{
    public enum ItemCategory : byte
    {
        Resource,
        Component,
        Tool,
        Weapon,
        Food,
        Medical,
        Clothing,
        Deployable,
        Misc,
    }

    public enum EquipSlot : byte
    {
        None,
        Head,
        Chest,
        Legs,
        Feet,
        Hands,
        Back,
    }

    /// <summary>What a resource node gives and which tool power applies.</summary>
    public enum GatherKind : byte
    {
        None,
        Wood,
        Stone,
        Ore,
        Plant,
        Salvage,
    }

    public enum StationKind : byte
    {
        Hand,
        CrudeBench,
        ForgeBench,
        MachinistBench,
        Campfire,
        Smelter,
        TinkerBench,
    }

    public enum EntityKind : byte
    {
        /// <summary>Placed from an item (campfire, bench, chest, bedroll).</summary>
        Deployable,

        /// <summary>Spawned by the map with loot inside (crate). Refills on a timer.</summary>
        LootContainer,

        /// <summary>Spawned by the map; break it to get its loot (barrel).</summary>
        Breakable,

        DroppedItem,
        Body,
        Sleeper,
    }

    [Flags]
    public enum StatusEffect : ushort
    {
        None = 0,
        Bleeding = 1,
        Wet = 2,
        Cold = 4,
        Hot = 8,
        Poisoned = 16,
        WellFed = 32,
        Comfort = 64,
    }

    public enum SleeperMode : byte
    {
        /// <summary>Sleepers can be killed and looted anywhere.</summary>
        Vulnerable,

        /// <summary>Sleepers inside a claim they're authorized on take no damage (official default).</summary>
        ProtectedInClaim,

        /// <summary>Characters disappear after a short time offline and come back on login.</summary>
        Vanish,
    }

    public static class ContentEnums
    {
        public static T Parse<T>(string text, string what) where T : struct, Enum
        {
            string normalized = (text ?? string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);
            if (Enum.TryParse(normalized, true, out T value) && Enum.IsDefined(typeof(T), value))
            {
                return value;
            }

            throw new FormatException($"Unknown {what} '{text}'. Expected one of: {string.Join(", ", Enum.GetNames(typeof(T)))}");
        }
    }
}
