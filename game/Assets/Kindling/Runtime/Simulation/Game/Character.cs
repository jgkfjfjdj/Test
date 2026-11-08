using System.Collections.Generic;
using System.Numerics;
using Kindling.Content;
using Kindling.Simulation.Items;
using Kindling.Simulation.Movement;

namespace Kindling.Simulation.Game
{
    public enum LifeState : byte
    {
        Alive,

        /// <summary>Health hit 0: on the ground for a while; a teammate can revive, otherwise death.</summary>
        Downed,

        /// <summary>Waiting to respawn.</summary>
        Dead,
    }

    public enum DamageCause : byte
    {
        None,
        Starvation,
        Dehydration,
        Cold,
        Bleeding,
        Poison,
        GaveUp,
        Admin,
    }

    public sealed class SurvivalStats
    {
        public float Health = 100f;
        public float Hunger = 100f;
        public float Thirst = 100f;

        /// <summary>−100 … +100; comfortable inside the rules' comfort band.</summary>
        public float Warmth;

        public StatusEffect Effects;
        public float PoisonLeft;
        public float HealLeft;
        public float HealRate;

        public bool Has(StatusEffect e) => (Effects & e) != 0;

        public void Set(StatusEffect e, bool on) => Effects = on ? Effects | e : Effects & ~e;

        public SurvivalStats Clone() => (SurvivalStats)MemberwiseClone();
    }

    public sealed class CraftJob
    {
        public int Id;
        public RecipeDef Recipe;

        /// <summary>How many are still to be made (inputs for all of them were taken up front).</summary>
        public int Remaining;

        public float Progress;
    }

    public enum GameEventKind : byte
    {
        /// <summary>A short message for the player (Code = <see cref="ActionError"/> or a notice id).</summary>
        Notice,
        GatherHit,
        ItemsGained,
        ToolBroke,
        Downed,
        Died,
        Respawned,
        CraftFinished,
    }

    /// <summary>Something to tell one player (gather feedback, toasts, death). Drained by the network layer.</summary>
    public struct GameEvent
    {
        public GameEventKind Kind;
        public int Code;
        public ushort ItemNetId;
        public int Amount;
        public int Target;
        public Vector3 Position;
        public bool Flag;
    }

    /// <summary>A player's persistent character: body, inventory, stats and progress. Exists while offline (sleeping) and dead.</summary>
    public sealed class Character
    {
        public Character(string accountId, string name)
        {
            AccountId = accountId;
            Name = name;
        }

        public string AccountId { get; }

        public string Name { get; set; }

        public PlayerState Motion;

        public CharacterInventory Inventory { get; } = new CharacterInventory();

        public SurvivalStats Stats { get; set; } = new SurvivalStats();

        public LifeState Life { get; set; } = LifeState.Dead;

        public float DownedLeft { get; set; }

        public DamageCause LastDamage { get; set; }

        public bool Online { get; set; }

        /// <summary>World time the character went offline.</summary>
        public double OfflineSince { get; set; }

        /// <summary>The sleeper entity showing this character while offline (0 if none).</summary>
        public uint SleeperEntityId { get; set; }

        public List<CraftJob> Crafting { get; } = new List<CraftJob>();

        public int NextCraftJobId { get; set; } = 1;

        /// <summary>Learned recipes (net ids). Default-known recipes don't need an entry.</summary>
        public HashSet<ushort> Knowledge { get; } = new HashSet<ushort>();

        public double NextSwingAt { get; set; }

        /// <summary>Entity whose container this player is looking at (0 = none).</summary>
        public uint OpenContainerId { get; set; }

        /// <summary>Bumped when stats, life state or the crafting queue change (owner replication).</summary>
        public uint StateVersion { get; set; }

        public readonly List<GameEvent> Events = new List<GameEvent>();

        public bool IsAlive => Life == LifeState.Alive;

        public bool Knows(RecipeDef recipe) => recipe.DefaultKnown || Knowledge.Contains(recipe.NetId);

        public void Notify(ActionError error) => Events.Add(new GameEvent { Kind = GameEventKind.Notice, Code = (int)error });

        public override string ToString() => $"{Name} ({AccountId})";
    }
}
