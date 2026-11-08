using Kindling.Content;
using UnityEngine;

namespace Kindling.Client.View
{
    /// <summary>Greybox colors for content (until real art arrives in M6).</summary>
    public static class Palette
    {
        public static readonly Color Bark = new Color(0.36f, 0.25f, 0.16f);
        public static readonly Color BirchBark = new Color(0.82f, 0.8f, 0.74f);
        public static readonly Color Leaves = new Color(0.22f, 0.42f, 0.2f);
        public static readonly Color Stone = new Color(0.52f, 0.52f, 0.5f);
        public static readonly Color Ore = new Color(0.55f, 0.36f, 0.28f);
        public static readonly Color Fiber = new Color(0.45f, 0.62f, 0.3f);
        public static readonly Color Berry = new Color(0.55f, 0.12f, 0.35f);
        public static readonly Color Mushroom = new Color(0.86f, 0.78f, 0.62f);
        public static readonly Color Water = new Color(0.2f, 0.38f, 0.55f);
        public static readonly Color Wood = new Color(0.55f, 0.4f, 0.24f);
        public static readonly Color Metal = new Color(0.32f, 0.33f, 0.36f);
        public static readonly Color Cloth = new Color(0.62f, 0.55f, 0.42f);
        public static readonly Color Ember = new Color(1f, 0.45f, 0.1f);
        public static readonly Color Body = new Color(0.4f, 0.32f, 0.3f);
        public static readonly Color Sleeper = new Color(0.5f, 0.5f, 0.56f);
        public static readonly Color Item = new Color(0.85f, 0.75f, 0.3f);

        public static Color ForNode(NodeDef def)
        {
            switch (def.Kind)
            {
                case GatherKind.Wood: return def.Key.Value.Contains("birch") ? BirchBark : Bark;
                case GatherKind.Stone: return Stone;
                case GatherKind.Ore: return Ore;
                default:
                    if (def.Key.Value.Contains("berry")) return Berry;
                    if (def.Key.Value.Contains("mushroom")) return Mushroom;
                    return Fiber;
            }
        }

        public static Color ForEntity(EntityDef def)
        {
            string key = def.Key.Value;
            switch (def.Kind)
            {
                case EntityKind.Body: return Body;
                case EntityKind.Sleeper: return Sleeper;
                case EntityKind.DroppedItem: return Item;
                case EntityKind.Breakable: return new Color(0.3f, 0.4f, 0.55f);
                case EntityKind.LootContainer: return new Color(0.35f, 0.45f, 0.3f);
            }

            if (key.Contains("smelter")) return Metal;
            if (key.Contains("bedroll")) return Cloth;
            if (key.Contains("campfire")) return new Color(0.25f, 0.2f, 0.18f);
            return Wood;
        }
    }
}
