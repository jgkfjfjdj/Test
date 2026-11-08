using System.Collections.Generic;

namespace Kindling.Content
{
    /// <summary>Gameplay tuning from rules.json. Servers can override some of it in their config.</summary>
    public sealed class RulesDef
    {
        public SurvivalRules Survival = new SurvivalRules();
        public TemperatureRules Temperature = new TemperatureRules();
        public DeathRules Death = new DeathRules();
        public GatherRules Gathering = new GatherRules();
        public CraftingRules Crafting = new CraftingRules();
        public WorldRules World = new WorldRules();
    }

    public sealed class SurvivalRules
    {
        public float MaxHealth = 100f;
        public float HungerPerMinute = 1.5f;
        public float ThirstPerMinute = 2.5f;
        public float SprintDrainMultiplier = 2f;
        public float ColdHungerMultiplier = 1.5f;
        public float HotThirstMultiplier = 1.5f;
        public float HealthRegenPerMinute = 3f;
        public float RegenMinHunger = 20f;
        public float RegenMinThirst = 20f;
        public float StarvationDamagePerMinute = 5f;
        public float DehydrationDamagePerMinute = 6f;
        public float ColdDamagePerMinute = 4f;
        public float BleedingDamagePerMinute = 6f;
        public float PoisonDamagePerMinute = 4f;
        public float PoisonSeconds = 30f;
        public float WellFedThreshold = 90f;
        public float WellFedRegenBonusPerMinute = 3f;
        public float ComfortRegenBonusPerMinute = 3f;

        /// <summary>Warmth moves toward its target by this much per minute.</summary>
        public float WarmthChangePerMinute = 20f;

        public float ComfortMin = -20f;
        public float ComfortMax = 20f;
    }

    public sealed class TemperatureRules
    {
        public float DayWarmth = 5f;
        public float NightWarmth = -35f;
    }

    public sealed class DeathRules
    {
        public float DownedSeconds = 30f;
        public float RespawnHealth = 60f;
        public float RespawnHunger = 60f;
        public float RespawnThirst = 60f;
        public readonly List<ItemAmount> StarterKit = new List<ItemAmount>();
    }

    public sealed class GatherRules
    {
        public float Multiplier = 1f;
        public float SweetSpotRadius = 0.4f;
        public float SweetSpotBonus = 1.5f;
        public float HandSwingSeconds = 1.2f;
        public float HandReach = 2.2f;
        public float HandYield = 4f;
    }

    public sealed class CraftingRules
    {
        public int QueueSize = 8;
        public float StationRange = 3.5f;
        public float InteractRange = 3f;
    }

    public sealed class WorldRules
    {
        public float DayLengthMinutes = 60f;
        public float NightFraction = 0.25f;
        public float DroppedItemSeconds = 300f;
        public SleeperMode SleeperMode = SleeperMode.ProtectedInClaim;
        public float VanishAfterSeconds = 60f;
    }
}
