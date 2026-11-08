using System.Collections.Generic;
using System.Linq;
using Kindling.Content;
using NUnit.Framework;

namespace Kindling.Tests.Pure.Content
{
    public class ContentDatabaseTests
    {
        static Dictionary<string, string> RepoFiles()
        {
            DirectoryContentSource source = DirectoryContentSource.FindDefault();
            var files = new Dictionary<string, string>();
            foreach (string f in ContentFiles.DefinitionFiles.Concat(new[] { ContentFiles.Rules, ContentFiles.Lock }))
            {
                files[f] = source.Read(f);
            }

            return files;
        }

        static IReadOnlyList<string> ProblemsAfter(string file, string find, string replace)
        {
            var files = RepoFiles();
            Assert.That(files[file], Does.Contain(find), "test setup: text to replace");
            files[file] = files[file].Replace(find, replace);
            ContentDatabase.Parse(new DictionaryContentSource(files), requireLockIds: true, out List<string> problems);
            return problems;
        }

        [Test]
        public void RepositoryContentLoadsAndResolves()
        {
            ContentDatabase db = ContentDatabase.LoadDefault();
            Assert.That(db.Items.Count, Is.GreaterThan(20));
            ItemDef hatchet = db.Item("item.tool.stone_hatchet");
            Assert.That(hatchet.Tool.PowerFor(GatherKind.Wood).Tier, Is.EqualTo(1));
            Assert.That(db.ByNetId<ItemDef>(hatchet.NetId), Is.SameAs(hatchet));
            RecipeDef recipe = db.RecipesFor(hatchet).Single();
            Assert.That(recipe.Inputs.All(i => i.Item != null), Is.True);
            Assert.That(db.Item("item.deployable.campfire").Deploys.Processor.Conversions[0].Output.Key.Value, Is.EqualTo("item.food.mushroom_cooked"));
            Assert.That(db.Entity("entity.loot_crate").LootTable, Is.Not.Null);
            Assert.That(db.Rules.Death.StarterKit.Select(k => k.Item.Key.Value), Is.EquivalentTo(new[] { "item.tool.rock", "item.tool.torch" }));
            Assert.That(db.Rules.World.SleeperMode, Is.EqualTo(SleeperMode.ProtectedInClaim));
        }

        [Test]
        public void NetIdsAreUniqueAndCoverEveryDefinition()
        {
            ContentDatabase db = ContentDatabase.LoadDefault();
            var ids = db.AllKeys.Select(k => db.Get(k).NetId).ToList();
            Assert.That(ids, Is.Unique);
            Assert.That(ids, Has.None.EqualTo((ushort)0));
        }

        [TestCase("recipes.json", "\"item.resource.stone\": 80", "\"item.resource.stoen\": 80", "unknown reference item.resource.stoen")]
        [TestCase("items.json", "\"stack\": 20, \"weight\": 0.05, \"consume\": { \"hunger\": 6", "\"stack\": 0, \"weight\": 0.05, \"consume\": { \"hunger\": 6", "stack must be at least 1")]
        [TestCase("items.json", "\"deploys\": \"entity.campfire\"", "\"deploys\": \"item.resource.wood\"", "wrong kind of reference")]
        [TestCase("rules.json", "\"hungerPerMinute\"", "\"hungerPerMinut\"", "unknown setting")]
        [TestCase("nodes.json", "\"yield\": 400", "\"yield\": 0", "yield and respawnSeconds must be positive")]
        [TestCase("loot.json", "\"rolls\": [1, 2]", "\"rolls\": [3, 2]", "rolls must be")]
        [TestCase("items.json", "\"key\": \"item.resource.stone\"", "\"key\": \"item.resource.wood\"", "duplicate key item.resource.wood")]
        [TestCase("entities.json", "\"slots\": 8, \"loot\": \"loot.crate\"", "\"slots\": 0, \"loot\": \"loot.crate\"", "loot containers need")]
        public void ValidatorCatchesBrokenContent(string file, string find, string replace, string expected)
        {
            IReadOnlyList<string> problems = ProblemsAfter(file, find, replace);
            Assert.That(problems, Has.Some.Contains(expected), string.Join("\n", problems));
        }

        [Test]
        public void NewDefinitionsNeedALockId()
        {
            IReadOnlyList<string> problems = ProblemsAfter("items.json", "{ \"key\": \"item.resource.stone\"", "{ \"key\": \"item.resource.clay\", \"name\": \"Clay\", \"category\": \"resource\" },\n    { \"key\": \"item.resource.stone\"");
            Assert.That(problems, Has.Some.Contains("item.resource.clay has no network id"));
        }

        [Test]
        public void LockKeepsIdsForeverAndRetiresRemovedKeys()
        {
            ContentLock l = ContentLock.Parse("{\"ids\": {\"item.a.x\": 1, \"item.a.y\": 2}}");
            Assert.That(l.Update(new[] { "item.a.x", "item.a.z" }), Is.True);
            Assert.That(l.Ids["item.a.x"], Is.EqualTo(1));
            Assert.That(l.Ids["item.a.z"], Is.EqualTo(3), "never reuses the retired id 2");
            Assert.That(l.Retired["item.a.y"], Is.EqualTo(2));
            l.Update(new[] { "item.a.x", "item.a.y", "item.a.z" });
            Assert.That(l.Ids["item.a.y"], Is.EqualTo(2), "a re-added key gets its old id back");
            Assert.That(l.Update(new[] { "item.a.x", "item.a.y", "item.a.z" }), Is.False);
            Assert.That(ContentLock.Parse(l.ToJson()).Ids, Is.EquivalentTo(l.Ids));
        }

        [Test]
        public void ExportIsDeterministicAndCommitted()
        {
            ContentDatabase db = ContentDatabase.LoadDefault();
            string export = ContentExporter.Export(db);
            Assert.That(ContentExporter.Export(db), Is.EqualTo(export));
            Assert.That(export, Does.Contain("\"item.tool.stone_hatchet\""));
        }
    }
}
