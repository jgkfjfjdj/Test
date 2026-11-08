using System.Collections.Generic;

namespace Kindling.Content
{
    /// <summary>Semantic checks that JSON parsing can't catch. Run on every load (tests, CI, game start).</summary>
    public static class ContentValidator
    {
        public static void Validate(ContentDatabase db, List<string> problems)
        {
            var deployed = new HashSet<EntityDef>();
            foreach (ItemDef i in db.Items)
            {
                Check(problems, !string.IsNullOrWhiteSpace(i.Name), i, "has no name");
                Check(problems, i.StackSize >= 1, i, "stack must be at least 1");
                Check(problems, i.Key.Kind == ContentKind.Item, i, "key must start with item.");
                Check(problems, i.MaxCondition >= 0 && i.Weight >= 0 && i.FuelSeconds >= 0 && i.Insulation >= 0, i, "negative value");
                if (i.HasCondition || i.Tool != null || i.Slot != EquipSlot.None)
                {
                    Check(problems, i.StackSize == 1, i, "items with condition, tools and clothing don't stack");
                }

                if (i.Category == ItemCategory.Clothing)
                {
                    Check(problems, i.Slot != EquipSlot.None, i, "clothing needs a slot");
                }

                if (i.Category == ItemCategory.Deployable)
                {
                    Check(problems, i.Deploys != null && i.Deploys.Kind == EntityKind.Deployable, i, "deployables must deploy a deployable entity");
                }

                if (i.Deploys != null)
                {
                    deployed.Add(i.Deploys);
                }

                if (i.Consume != null)
                {
                    Check(problems, i.Category == ItemCategory.Food || i.Category == ItemCategory.Medical, i, "only food and medical items can be consumed");
                    Check(problems, i.Consume.PoisonChance >= 0 && i.Consume.PoisonChance <= 1, i, "poisonChance must be 0..1");
                    Check(problems, i.Consume.HealOverTime <= 0 || i.Consume.HealSeconds > 0, i, "healOverTime needs healSeconds");
                }

                if (i.Tool != null)
                {
                    Check(problems, i.Tool.SwingSeconds > 0.1f && i.Tool.Reach > 0, i, "tool needs swingSeconds > 0.1 and reach > 0");
                    foreach (var p in i.Tool.Power)
                    {
                        Check(problems, p.Key != GatherKind.None && p.Value.Tier >= 0 && p.Value.Yield > 0, i, $"invalid tool power for {p.Key}");
                    }
                }
            }

            foreach (RecipeDef r in db.Recipes)
            {
                Check(problems, r.Key.Kind == ContentKind.Recipe, r, "key must start with recipe.");
                Check(problems, r.Output.Amount >= 1, r, "output amount must be at least 1");
                Check(problems, r.Inputs.Count >= 1, r, "needs at least one input");
                Check(problems, r.Seconds > 0, r, "seconds must be positive");
                if (r.Output.Item != null)
                {
                    Check(problems, r.Output.Amount <= r.Output.Item.StackSize, r, "output exceeds the item's stack size");
                }

                foreach (ItemAmount input in r.Inputs)
                {
                    Check(problems, input.Amount >= 1, r, $"input {input.ItemKey} amount must be at least 1");
                    Check(problems, input.Item == null || input.Item != r.Output.Item, r, "an item can't be its own input");
                }
            }

            foreach (NodeDef n in db.Nodes)
            {
                Check(problems, n.Key.Kind == ContentKind.Node, n, "key must start with node.");
                Check(problems, !string.IsNullOrWhiteSpace(n.Name), n, "has no name");
                Check(problems, n.Kind != GatherKind.None, n, "needs a gather kind");
                Check(problems, n.TotalYield > 0 && n.RespawnSeconds > 0, n, "yield and respawnSeconds must be positive");
                Check(problems, n.Size.X > 0 && n.Size.Y > 0 && n.Size.Z > 0, n, "size must be positive");
                Check(problems, n.MinTier >= 0 && n.MapCount >= 0, n, "minTier and mapCount can't be negative");
                foreach (BonusYield b in n.Bonus)
                {
                    Check(problems, b.Chance > 0 && b.Chance <= 1 && b.Amount >= 1, n, $"bonus {b.ItemKey} needs chance 0..1 and amount >= 1");
                }
            }

            foreach (EntityDef e in db.Entities)
            {
                Check(problems, e.Key.Kind == ContentKind.Entity, e, "key must start with entity.");
                Check(problems, !string.IsNullOrWhiteSpace(e.Name), e, "has no name");
                Check(problems, e.ContainerSlots >= 0 && e.ContainerSlots <= 64, e, "slots must be 0..64");
                Check(problems, e.Size.X > 0 && e.Size.Y > 0 && e.Size.Z > 0, e, "size must be positive");
                switch (e.Kind)
                {
                    case EntityKind.LootContainer:
                        Check(problems, e.LootTable != null && e.ContainerSlots > 0 && e.RefillSeconds > 0, e, "loot containers need loot, slots and refillSeconds");
                        break;
                    case EntityKind.Breakable:
                        Check(problems, e.LootTable != null && e.Health > 0 && e.RefillSeconds > 0, e, "breakables need loot, health and refillSeconds");
                        break;
                    case EntityKind.Deployable:
                        Check(problems, deployed.Contains(e), e, "no item deploys this entity");
                        break;
                    case EntityKind.Body:
                    case EntityKind.DroppedItem:
                        Check(problems, e.DecaySeconds > 0, e, "needs decaySeconds");
                        break;
                }

                if (e.Processor != null)
                {
                    Check(problems, e.ContainerSlots >= 2, e, "a processor needs at least 2 slots");
                    foreach (Conversion c in e.Processor.Conversions)
                    {
                        Check(problems, c.Seconds > 0 && c.OutputAmount >= 1, e, $"conversion {c.InputKey} needs seconds > 0 and amount >= 1");
                    }
                }

                if (e.IsRespawnPoint)
                {
                    Check(problems, e.RespawnCooldownSeconds >= 0, e, "respawnCooldownSeconds can't be negative");
                }
            }

            foreach (LootTableDef t in db.LootTables)
            {
                Check(problems, t.Key.Kind == ContentKind.LootTable, t, "key must start with loot.");
                Check(problems, t.Entries.Count > 0, t, "needs entries");
                Check(problems, t.MinRolls >= 1 && t.MaxRolls >= t.MinRolls, t, "rolls must be [min, max] with 1 <= min <= max");
                foreach (LootEntry entry in t.Entries)
                {
                    Check(problems, entry.Weight > 0 && entry.Min >= 1 && entry.Max >= entry.Min, t, $"entry {entry.ItemKey} needs weight > 0 and 1 <= min <= max");
                    if (entry.Item != null)
                    {
                        Check(problems, entry.Max <= entry.Item.StackSize, t, $"entry {entry.ItemKey} max exceeds its stack size");
                    }
                }
            }

            RulesDef rules = db.Rules;
            if (rules != null)
            {
                Check(problems, rules.World.DayLengthMinutes > 0 && rules.World.NightFraction >= 0 && rules.World.NightFraction < 1, "rules.world", "dayLengthMinutes > 0 and nightFraction 0..1");
                Check(problems, rules.Survival.MaxHealth > 0 && rules.Death.DownedSeconds >= 0, "rules", "maxHealth > 0, downedSeconds >= 0");
                Check(problems, rules.Gathering.Multiplier > 0, "rules.gathering", "multiplier must be positive");
                Check(problems, rules.Crafting.QueueSize >= 1, "rules.crafting", "queueSize must be at least 1");
            }
        }

        static void Check(List<string> problems, bool ok, object what, string message)
        {
            if (!ok)
            {
                problems.Add($"{what}: {message}");
            }
        }
    }
}
