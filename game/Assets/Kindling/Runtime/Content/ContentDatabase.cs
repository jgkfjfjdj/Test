using System;
using System.Collections.Generic;
using Kindling.Core.Json;

namespace Kindling.Content
{
    /// <summary>
    /// All game content, loaded from the JSON files and validated. Shared by client, server, bots and
    /// tools; immutable after loading.
    /// </summary>
    public sealed class ContentDatabase
    {
        readonly Dictionary<ContentKey, ContentDef> m_ByKey = new Dictionary<ContentKey, ContentDef>();
        readonly Dictionary<ushort, ContentDef> m_ByNetId = new Dictionary<ushort, ContentDef>();
        readonly Dictionary<ItemDef, List<RecipeDef>> m_RecipesByOutput = new Dictionary<ItemDef, List<RecipeDef>>();
        readonly List<ItemDef> m_Items = new List<ItemDef>();
        readonly List<RecipeDef> m_Recipes = new List<RecipeDef>();
        readonly List<NodeDef> m_Nodes = new List<NodeDef>();
        readonly List<EntityDef> m_Entities = new List<EntityDef>();
        readonly List<LootTableDef> m_LootTables = new List<LootTableDef>();

        ContentDatabase()
        {
        }

        public IReadOnlyList<ItemDef> Items => m_Items;

        public IReadOnlyList<RecipeDef> Recipes => m_Recipes;

        public IReadOnlyList<NodeDef> Nodes => m_Nodes;

        public IReadOnlyList<EntityDef> Entities => m_Entities;

        public IReadOnlyList<LootTableDef> LootTables => m_LootTables;

        public RulesDef Rules { get; private set; }

        public ContentLock Lock { get; private set; }

        /// <summary>Every definition key, for the lock tool.</summary>
        public IEnumerable<ContentKey> AllKeys => m_ByKey.Keys;

        static ContentDatabase s_Default;

        /// <summary>The repository's content (tests, tools, dev server).</summary>
        public static ContentDatabase LoadDefault() => s_Default ??= Load(DirectoryContentSource.FindDefault());

        /// <summary>Loads and validates. Throws <see cref="ContentException"/> listing every problem.</summary>
        public static ContentDatabase Load(IContentSource source)
        {
            ContentDatabase db = Parse(source, requireLockIds: true, out List<string> problems);
            if (problems.Count > 0)
            {
                throw new ContentException(problems);
            }

            return db;
        }

        /// <summary>Parses without throwing; with <paramref name="requireLockIds"/> false, keys missing from the lock are allowed (lock tool).</summary>
        public static ContentDatabase Parse(IContentSource source, bool requireLockIds, out List<string> problems)
        {
            problems = new List<string>();
            var db = new ContentDatabase();
            try
            {
                db.Lock = ContentLock.Parse(source.Read(ContentFiles.Lock));
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                problems.Add($"{ContentFiles.Lock}: {e.Message}");
                db.Lock = new ContentLock();
            }

            db.ReadList(source, ContentFiles.Items, "items", ContentParser.Item, db.m_Items, problems);
            db.ReadList(source, ContentFiles.Recipes, "recipes", ContentParser.Recipe, db.m_Recipes, problems);
            db.ReadList(source, ContentFiles.Nodes, "nodes", ContentParser.Node, db.m_Nodes, problems);
            db.ReadList(source, ContentFiles.Entities, "entities", ContentParser.Entity, db.m_Entities, problems);
            db.ReadList(source, ContentFiles.Loot, "tables", ContentParser.Loot, db.m_LootTables, problems);
            try
            {
                db.Rules = ContentParser.Rules(JsonReader.Parse(source.Read(ContentFiles.Rules)), problems);
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                problems.Add($"{ContentFiles.Rules}: {e.Message}");
                db.Rules = new RulesDef();
            }

            db.AssignNetIds(requireLockIds, problems);
            db.Resolve(problems);
            ContentValidator.Validate(db, problems);
            return db;
        }

        void ReadList<T>(IContentSource source, string file, string arrayName, Func<JsonValue, List<string>, T> parse, List<T> into, List<string> problems)
            where T : ContentDef
        {
            JsonValue root;
            try
            {
                root = JsonReader.Parse(source.Read(file));
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                problems.Add($"{file}: {e.Message}");
                return;
            }

            JsonValue list = root.Get(arrayName);
            if (list == null || list.Kind != JsonKind.Array)
            {
                problems.Add($"{file}: expected an array \"{arrayName}\"");
                return;
            }

            foreach (JsonValue entry in list.Items)
            {
                T def;
                try
                {
                    def = parse(entry, problems);
                }
                catch (Exception e) when (e is JsonException || e is FormatException || e is OverflowException)
                {
                    problems.Add($"{file} (line {entry.Line}): {e.Message}");
                    continue;
                }

                if (!def.Key.IsValid)
                {
                    continue;
                }

                if (m_ByKey.ContainsKey(def.Key))
                {
                    problems.Add($"{file}: duplicate key {def.Key}");
                    continue;
                }

                m_ByKey[def.Key] = def;
                into.Add(def);
            }
        }

        void AssignNetIds(bool required, List<string> problems)
        {
            foreach (ContentDef def in m_ByKey.Values)
            {
                if (Lock.TryGet(def.Key.Value, out ushort id))
                {
                    def.NetId = id;
                    if (m_ByNetId.TryGetValue(id, out ContentDef other))
                    {
                        problems.Add($"{ContentFiles.Lock}: id {id} is used by both {other.Key} and {def.Key}");
                    }
                    else
                    {
                        m_ByNetId[id] = def;
                    }
                }
                else if (required)
                {
                    problems.Add($"{def.Key} has no network id: run `dotnet run --project sim/Kindling.ContentTool -- update-lock`");
                }
            }
        }

        void Resolve(List<string> problems)
        {
            foreach (ItemDef item in m_Items)
            {
                if (item.DeploysKey.IsValid)
                {
                    item.Deploys = Ref<EntityDef>(item.DeploysKey, item, problems);
                }
            }

            foreach (RecipeDef r in m_Recipes)
            {
                r.Output.Item = Ref<ItemDef>(r.Output.ItemKey, r, problems);
                for (int i = 0; i < r.Inputs.Count; i++)
                {
                    ItemAmount input = r.Inputs[i];
                    input.Item = Ref<ItemDef>(input.ItemKey, r, problems);
                    r.Inputs[i] = input;
                }

                if (r.Output.Item != null)
                {
                    if (!m_RecipesByOutput.TryGetValue(r.Output.Item, out List<RecipeDef> list))
                    {
                        m_RecipesByOutput[r.Output.Item] = list = new List<RecipeDef>();
                    }

                    list.Add(r);
                }
            }

            foreach (NodeDef n in m_Nodes)
            {
                n.Primary = Ref<ItemDef>(n.PrimaryKey, n, problems);
                for (int i = 0; i < n.Bonus.Count; i++)
                {
                    BonusYield b = n.Bonus[i];
                    b.Item = Ref<ItemDef>(b.ItemKey, n, problems);
                    n.Bonus[i] = b;
                }
            }

            foreach (EntityDef e in m_Entities)
            {
                if (e.LootTableKey.IsValid)
                {
                    e.LootTable = Ref<LootTableDef>(e.LootTableKey, e, problems);
                }

                if (e.Processor != null)
                {
                    if (e.Processor.ByproductKey.IsValid)
                    {
                        e.Processor.Byproduct = Ref<ItemDef>(e.Processor.ByproductKey, e, problems);
                    }

                    foreach (Conversion c in e.Processor.Conversions)
                    {
                        c.Input = Ref<ItemDef>(c.InputKey, e, problems);
                        c.Output = Ref<ItemDef>(c.OutputKey, e, problems);
                    }
                }
            }

            foreach (LootTableDef t in m_LootTables)
            {
                foreach (LootEntry entry in t.Entries)
                {
                    entry.Item = Ref<ItemDef>(entry.ItemKey, t, problems);
                }
            }

            for (int i = 0; i < Rules.Death.StarterKit.Count; i++)
            {
                ItemAmount a = Rules.Death.StarterKit[i];
                a.Item = m_ByKey.TryGetValue(a.ItemKey, out ContentDef d) ? d as ItemDef : null;
                if (a.Item == null)
                {
                    problems.Add($"rules.death.starterKit: unknown item {a.ItemKey}");
                }

                Rules.Death.StarterKit[i] = a;
            }
        }

        T Ref<T>(ContentKey key, ContentDef from, List<string> problems) where T : ContentDef
        {
            if (!key.IsValid)
            {
                return null;
            }

            if (m_ByKey.TryGetValue(key, out ContentDef def) && def is T typed)
            {
                return typed;
            }

            problems.Add($"{from.Key}: {(def == null ? "unknown" : "wrong kind of")} reference {key} (expected {typeof(T).Name})");
            return null;
        }

        public ContentDef Get(ContentKey key) => m_ByKey.TryGetValue(key, out ContentDef d) ? d : null;

        public T Get<T>(string key) where T : ContentDef => m_ByKey.TryGetValue(ContentKey.Parse(key), out ContentDef d) && d is T t ? t : throw new KeyNotFoundException(key);

        public ItemDef Item(string key) => Get<ItemDef>(key);

        public RecipeDef Recipe(string key) => Get<RecipeDef>(key);

        public NodeDef Node(string key) => Get<NodeDef>(key);

        public EntityDef Entity(string key) => Get<EntityDef>(key);

        public LootTableDef LootTable(string key) => Get<LootTableDef>(key);

        string m_Fingerprint;

        /// <summary>Short hash of the content catalogue (keys, net ids, names): client and server must match.</summary>
        public string Fingerprint
        {
            get
            {
                if (m_Fingerprint == null)
                {
                    ulong h = 1469598103934665603UL;
                    foreach (char ch in ContentExporter.Export(this))
                    {
                        h ^= ch;
                        h *= 1099511628211UL;
                    }

                    m_Fingerprint = h.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
                }

                return m_Fingerprint;
            }
        }

        public ContentDef ByNetId(ushort id) => m_ByNetId.TryGetValue(id, out ContentDef d) ? d : null;

        public T ByNetId<T>(ushort id) where T : ContentDef => ByNetId(id) as T;

        public IReadOnlyList<RecipeDef> RecipesFor(ItemDef output) =>
            m_RecipesByOutput.TryGetValue(output, out List<RecipeDef> list) ? list : (IReadOnlyList<RecipeDef>)Array.Empty<RecipeDef>();
    }
}
