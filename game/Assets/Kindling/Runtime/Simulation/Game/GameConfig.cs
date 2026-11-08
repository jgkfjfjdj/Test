using Kindling.Content;

namespace Kindling.Simulation.Game
{
    /// <summary>Per-server gameplay settings (server.json); defaults come from rules.json.</summary>
    public sealed class GameConfig
    {
        public float GatherMultiplier = 1f;
        public SleeperMode SleeperMode = SleeperMode.ProtectedInClaim;
        public float DayLengthMinutes = 60f;
        public bool StarterKit = true;

        public static GameConfig FromRules(RulesDef rules) => new GameConfig
        {
            GatherMultiplier = rules.Gathering.Multiplier,
            SleeperMode = rules.World.SleeperMode,
            DayLengthMinutes = rules.World.DayLengthMinutes,
        };
    }

    /// <summary>Which container an item action refers to.</summary>
    public enum ContainerKind : byte
    {
        Main,
        Hotbar,
        Wear,

        /// <summary>A world entity's container (chest, campfire, body…); the player must have it open and be in range.</summary>
        Entity,
    }

    public struct ContainerRef
    {
        public ContainerKind Kind;
        public uint EntityId;

        public static ContainerRef Main => new ContainerRef { Kind = ContainerKind.Main };

        public static ContainerRef Hotbar => new ContainerRef { Kind = ContainerKind.Hotbar };

        public static ContainerRef Wear => new ContainerRef { Kind = ContainerKind.Wear };

        public static ContainerRef OfEntity(uint id) => new ContainerRef { Kind = ContainerKind.Entity, EntityId = id };

        public override string ToString() => Kind == ContainerKind.Entity ? $"entity:{EntityId}" : Kind.ToString();
    }

    public enum InteractTarget : byte
    {
        None,
        Node,
        Entity,
        Pond,

        /// <summary>Another player (revive when downed). Target id = their character's index in the online list.</summary>
        Character,
    }
}
