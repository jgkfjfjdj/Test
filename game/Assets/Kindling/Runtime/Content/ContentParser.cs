using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Kindling.Core.Json;

namespace Kindling.Content
{
    /// <summary>Turns the JSON content files into definition objects (references are resolved later).</summary>
    internal static class ContentParser
    {
        public static ContentKey Key(JsonValue v, string field, List<string> problems)
        {
            string text = v.GetString(field);
            if (text == null)
            {
                problems.Add($"Missing \"{field}\" (line {v.Line})");
                return default;
            }

            if (!ContentKey.TryParse(text, out ContentKey key, out string error))
            {
                problems.Add($"Invalid key \"{text}\" (line {v.Line}): {error}");
            }

            return key;
        }

        static ContentKey OptionalKey(JsonValue v, string field, List<string> problems) => v.Has(field) ? Key(v, field, problems) : default;

        static Vector3 Vec3(JsonValue v, string field, Vector3 fallback)
        {
            JsonValue a = v.Get(field);
            if (a == null)
            {
                return fallback;
            }

            if (a.Kind != JsonKind.Array || a.Count != 3)
            {
                throw new JsonException($"\"{field}\" must be [x, y, z]", a.Line, a.Column);
            }

            return new Vector3(a.Items[0].AsFloat(), a.Items[1].AsFloat(), a.Items[2].AsFloat());
        }

        static void Common(ContentDef def, JsonValue v, List<string> problems)
        {
            def.Key = Key(v, "key", problems);
            def.Name = v.GetString("name", string.Empty);
        }

        public static ItemDef Item(JsonValue v, List<string> problems)
        {
            var d = new ItemDef();
            Common(d, v, problems);
            d.Category = ContentEnums.Parse<ItemCategory>(v.GetString("category"), "item category");
            d.StackSize = v.GetInt("stack", 1);
            d.Weight = v.GetFloat("weight");
            d.MaxCondition = v.GetFloat("condition");
            d.Slot = v.Has("slot") ? ContentEnums.Parse<EquipSlot>(v.GetString("slot"), "equipment slot") : EquipSlot.None;
            d.Insulation = v.GetFloat("insulation");
            d.FuelSeconds = v.GetFloat("fuelSeconds");
            d.DeploysKey = OptionalKey(v, "deploys", problems);
            if (v.Get("tool") is JsonValue t)
            {
                d.Tool = new ToolStats
                {
                    SwingSeconds = t.GetFloat("swingSeconds", 1f),
                    Reach = t.GetFloat("reach", 2.5f),
                    ConditionPerHit = t.GetFloat("conditionPerHit", 1f),
                };
                if (t.Get("power") is JsonValue power)
                {
                    foreach (var p in power.Members)
                    {
                        d.Tool.Power[ContentEnums.Parse<GatherKind>(p.Key, "gather kind")] = new ToolPower { Tier = p.Value.GetInt("tier"), Yield = p.Value.GetFloat("yield") };
                    }
                }
            }

            if (v.Get("consume") is JsonValue c)
            {
                d.Consume = new ConsumeEffect
                {
                    Health = c.GetFloat("health"),
                    Hunger = c.GetFloat("hunger"),
                    Thirst = c.GetFloat("thirst"),
                    PoisonChance = c.GetFloat("poisonChance"),
                    StopsBleeding = c.GetBool("stopsBleeding"),
                    HealOverTime = c.GetFloat("healOverTime"),
                    HealSeconds = c.GetFloat("healSeconds"),
                };
            }

            return d;
        }

        public static RecipeDef Recipe(JsonValue v, List<string> problems)
        {
            var d = new RecipeDef();
            Common(d, v, problems);
            d.Output = new ItemAmount(Key(v, "output", problems), v.GetInt("amount", 1));
            d.Station = ContentEnums.Parse<StationKind>(v.GetString("station", "hand"), "station");
            d.Seconds = v.GetFloat("seconds", 1f);
            d.Tier = v.GetInt("tier");
            d.DefaultKnown = v.GetBool("defaultKnown", true);
            foreach (var input in v.Required("inputs").Members)
            {
                if (!ContentKey.TryParse(input.Key, out ContentKey key, out string error))
                {
                    problems.Add($"{d.Key}: invalid input key \"{input.Key}\": {error}");
                }

                d.Inputs.Add(new ItemAmount(key, input.Value.AsInt()));
            }

            return d;
        }

        public static NodeDef Node(JsonValue v, List<string> problems)
        {
            var d = new NodeDef();
            Common(d, v, problems);
            d.Kind = ContentEnums.Parse<GatherKind>(v.GetString("kind"), "gather kind");
            d.MinTier = v.GetInt("minTier");
            d.PrimaryKey = Key(v, "primary", problems);
            d.TotalYield = v.GetFloat("yield");
            d.RespawnSeconds = v.GetFloat("respawnSeconds", 600f);
            d.Size = Vec3(v, "size", new Vector3(1f, 1f, 1f));
            d.Pickable = v.GetBool("pickable");
            d.MapCount = v.GetInt("mapCount");
            if (v.Get("bonus") is JsonValue bonus)
            {
                foreach (JsonValue b in bonus.Items)
                {
                    d.Bonus.Add(new BonusYield { ItemKey = Key(b, "item", problems), Chance = b.GetFloat("chance", 1f), Amount = b.GetInt("amount", 1) });
                }
            }

            return d;
        }

        public static EntityDef Entity(JsonValue v, List<string> problems)
        {
            var d = new EntityDef();
            Common(d, v, problems);
            d.Kind = ContentEnums.Parse<EntityKind>(v.GetString("kind"), "entity kind");
            d.Size = Vec3(v, "size", d.Size);
            d.ContainerSlots = v.GetInt("slots");
            if (v.Has("station"))
            {
                d.Station = ContentEnums.Parse<StationKind>(v.GetString("station"), "station");
                d.IsStation = true;
            }

            d.IsRespawnPoint = v.GetBool("respawnPoint");
            d.RespawnCooldownSeconds = v.GetFloat("respawnCooldownSeconds");
            d.Health = v.GetFloat("health");
            d.LootTableKey = OptionalKey(v, "loot", problems);
            d.RefillSeconds = v.GetFloat("refillSeconds");
            d.DecaySeconds = v.GetFloat("decaySeconds");
            d.Heat = v.GetFloat("heat");
            d.HeatRadius = v.GetFloat("heatRadius");
            d.MapCount = v.GetInt("mapCount");
            if (v.Get("processor") is JsonValue p)
            {
                d.Processor = new ProcessorDef { ByproductKey = OptionalKey(p, "fuelByproduct", problems) };
                foreach (JsonValue c in p.Required("conversions").Items)
                {
                    d.Processor.Conversions.Add(new Conversion
                    {
                        InputKey = Key(c, "input", problems),
                        OutputKey = Key(c, "output", problems),
                        OutputAmount = c.GetInt("amount", 1),
                        Seconds = c.GetFloat("seconds", 1f),
                    });
                }
            }

            return d;
        }

        public static LootTableDef Loot(JsonValue v, List<string> problems)
        {
            var d = new LootTableDef();
            Common(d, v, problems);
            JsonValue rolls = v.Get("rolls");
            if (rolls != null)
            {
                d.MinRolls = rolls.Items[0].AsInt();
                d.MaxRolls = rolls.Items[rolls.Count - 1].AsInt();
            }

            foreach (JsonValue e in v.Required("entries").Items)
            {
                d.Entries.Add(new LootEntry { ItemKey = Key(e, "item", problems), Weight = e.GetFloat("weight", 1f), Min = e.GetInt("min", 1), Max = e.GetInt("max", e.GetInt("min", 1)) });
            }

            return d;
        }

        /// <summary>Binds rules.json onto <see cref="RulesDef"/> by field name (camelCase in JSON). Unknown keys are errors.</summary>
        public static RulesDef Rules(JsonValue root, List<string> problems)
        {
            var rules = new RulesDef();
            Bind(rules, root, "rules", problems);
            return rules;
        }

        static void Bind(object target, JsonValue obj, string path, List<string> problems)
        {
            Type type = target.GetType();
            foreach (var member in obj.Members)
            {
                if (member.Key == "comment")
                {
                    continue;
                }

                FieldInfo field = type.GetField(member.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field == null)
                {
                    problems.Add($"{path}.{member.Key}: unknown setting (line {member.Value.Line})");
                    continue;
                }

                try
                {
                    Type ft = field.FieldType;
                    if (ft == typeof(float))
                    {
                        field.SetValue(target, member.Value.AsFloat());
                    }
                    else if (ft == typeof(int))
                    {
                        field.SetValue(target, member.Value.AsInt());
                    }
                    else if (ft == typeof(bool))
                    {
                        field.SetValue(target, member.Value.AsBool());
                    }
                    else if (ft.IsEnum)
                    {
                        field.SetValue(target, Enum.Parse(ft, member.Value.AsString().Replace("-", string.Empty), true));
                    }
                    else if (ft == typeof(List<ItemAmount>))
                    {
                        var list = (List<ItemAmount>)field.GetValue(target);
                        foreach (var item in member.Value.Members)
                        {
                            if (!ContentKey.TryParse(item.Key, out ContentKey key, out string error))
                            {
                                problems.Add($"{path}.{member.Key}: invalid key \"{item.Key}\": {error}");
                            }

                            list.Add(new ItemAmount(key, item.Value.AsInt()));
                        }
                    }
                    else if (ft.IsClass)
                    {
                        Bind(field.GetValue(target), member.Value, path + "." + member.Key, problems);
                    }
                }
                catch (Exception e) when (e is JsonException || e is ArgumentException || e is OverflowException)
                {
                    problems.Add($"{path}.{member.Key}: {e.Message}");
                }
            }
        }
    }
}
