using System.Collections.Generic;
using System.Numerics;

namespace Kindling.Content
{
    /// <summary>Base for every keyed definition. Net ids come from content.lock.json and never change.</summary>
    public abstract class ContentDef
    {
        public ContentKey Key;
        public ushort NetId;
        public string Name;

        public override string ToString() => Key.Value;
    }

    public struct ItemAmount
    {
        public ContentKey ItemKey;
        public ItemDef Item;
        public int Amount;

        public ItemAmount(ContentKey key, int amount)
        {
            ItemKey = key;
            Item = null;
            Amount = amount;
        }
    }

    public sealed class RecipeDef : ContentDef
    {
        public ItemAmount Output;
        public readonly List<ItemAmount> Inputs = new List<ItemAmount>();
        public StationKind Station;
        public float Seconds = 1f;
        public int Tier;

        /// <summary>Known by every character from the start (others must be learned).</summary>
        public bool DefaultKnown = true;
    }

    public struct BonusYield
    {
        public ContentKey ItemKey;
        public ItemDef Item;
        public float Chance;
        public int Amount;
    }

    public sealed class NodeDef : ContentDef
    {
        public GatherKind Kind;

        /// <summary>Tool tier needed to gather at all (0 = hands work).</summary>
        public int MinTier;

        public ContentKey PrimaryKey;
        public ItemDef Primary;

        /// <summary>Total primary resource in a full node (before the server's gather multiplier).</summary>
        public float TotalYield;

        public readonly List<BonusYield> Bonus = new List<BonusYield>();
        public float RespawnSeconds = 600f;

        /// <summary>Blocking box (width, height, depth) centered on the node's feet position.</summary>
        public Vector3 Size;

        /// <summary>Plants are picked with Interact instead of hit.</summary>
        public bool Pickable;

        /// <summary>How many map-wide copies the greybox generator places.</summary>
        public int MapCount;
    }

    public sealed class Conversion
    {
        public ContentKey InputKey;
        public ItemDef Input;
        public ContentKey OutputKey;
        public ItemDef Output;
        public int OutputAmount = 1;
        public float Seconds = 1f;
    }

    /// <summary>Fuel-burning processing (campfire cooking, smelting).</summary>
    public sealed class ProcessorDef
    {
        public readonly List<Conversion> Conversions = new List<Conversion>();

        /// <summary>Optional item produced for each fuel item burned (wood → charcoal).</summary>
        public ContentKey ByproductKey;
        public ItemDef Byproduct;

        public Conversion ConversionFor(ItemDef input)
        {
            foreach (Conversion c in Conversions)
            {
                if (c.Input == input)
                {
                    return c;
                }
            }

            return null;
        }
    }

    public sealed class EntityDef : ContentDef
    {
        public EntityKind Kind;
        public Vector3 Size = new Vector3(1f, 1f, 1f);
        public int ContainerSlots;
        public StationKind Station = StationKind.Hand;
        public bool IsStation;
        public ProcessorDef Processor;
        public bool IsRespawnPoint;
        public float RespawnCooldownSeconds;
        public float Health;
        public ContentKey LootTableKey;
        public LootTableDef LootTable;

        /// <summary>Seconds until a loot container refills or a breakable respawns.</summary>
        public float RefillSeconds;

        /// <summary>Seconds until the entity removes itself (bodies, dropped items); 0 = never.</summary>
        public float DecaySeconds;

        /// <summary>Warmth given to players within <see cref="HeatRadius"/> while burning.</summary>
        public float Heat;

        public float HeatRadius;

        /// <summary>How many the greybox generator places (loot containers, breakables).</summary>
        public int MapCount;
    }

    public sealed class LootEntry
    {
        public ContentKey ItemKey;
        public ItemDef Item;
        public float Weight = 1f;
        public int Min = 1;
        public int Max = 1;
    }

    public sealed class LootTableDef : ContentDef
    {
        public int MinRolls = 1;
        public int MaxRolls = 1;
        public readonly List<LootEntry> Entries = new List<LootEntry>();

        public float TotalWeight
        {
            get
            {
                float total = 0;
                foreach (LootEntry e in Entries)
                {
                    total += e.Weight;
                }

                return total;
            }
        }
    }
}
