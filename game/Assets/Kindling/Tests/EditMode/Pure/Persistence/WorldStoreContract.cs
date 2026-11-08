using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Kindling.Content;
using Kindling.Simulation;
using Kindling.Simulation.Game;
using Kindling.Simulation.Items;
using Kindling.Simulation.Persistence;
using Kindling.Simulation.World;
using NUnit.Framework;

namespace Kindling.Tests.Pure.Persistence
{
    /// <summary>The same save/restore behavior is required from every <see cref="IWorldStore"/>.</summary>
    public abstract class WorldStoreContract
    {
        protected static ContentDatabase Db => ContentDatabase.LoadDefault();
        static readonly WorldMap s_Map = WorldMap.Generate(1, Db);
        string m_Dir;

        protected abstract IWorldStore CreateStore(string directory);

        [SetUp]
        public void MakeDirectory()
        {
            m_Dir = Path.Combine(Path.GetTempPath(), "kindling-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Dir);
        }

        [TearDown]
        public void RemoveDirectory()
        {
            try
            {
                Directory.Delete(m_Dir, true);
            }
            catch (IOException)
            {
            }
        }

        protected string Dir => m_Dir;

        GameWorld Load(IWorldStore store, out bool loaded, List<string> warnings = null) =>
            WorldPersistence.LoadOrCreate(Db, s_Map, GameConfig.FromRules(Db.Rules), 42, store, warnings ?? new List<string>(), out loaded);

        static ItemDef Item(string key) => Db.Item(key);

        /// <summary>A world with a bit of everything in it.</summary>
        static GameWorld Busy(IWorldStore store)
        {
            var world = WorldPersistence.LoadOrCreate(Db, s_Map, GameConfig.FromRules(Db.Rules), 42, store, new List<string>(), out bool loaded);
            Assert.That(loaded, Is.False);
            for (int i = 0; i < 30 * 90; i++)
            {
                world.Tick(1f / 30f);
            }

            Character a = world.Join("acc-a", "Ana");
            Vector3 home = OpenGround(world);
            a.Motion.Position = home + new Vector3(0.25f, 0f, -0.5f);
            a.Motion.Yaw = 123.5f;
            a.Motion.Stamina = 42.5f;
            a.Inventory.Main.Set(3, ItemStack.Of(Item("item.resource.wood"), 777));
            a.Inventory.Main.Set(5, ItemStack.Of(Item("item.resource.fiber"), 30));
            a.Inventory.Wear.Set(1, new ItemStack(Item("item.clothing.fiber_shirt"), 1, 33.25f));
            a.Inventory.Hotbar.Set(2, new ItemStack(Item("item.tool.stone_hatchet"), 1, 71.5f));
            a.Inventory.SelectSlot(2);
            a.Stats.Hunger = 33.3f;
            a.Stats.Thirst = 44.4f;
            a.Stats.Warmth = -12.75f;
            a.Stats.PoisonLeft = 7.5f;
            a.Stats.Effects = StatusEffect.Poisoned | StatusEffect.Bleeding;
            a.Knowledge.Add(Db.Recipe("recipe.smelter").NetId);
            a.Inventory.Main.Set(0, ItemStack.Of(Item("item.resource.fiber"), 500));
            Assert.That(world.Craft(a, Db.Recipe("recipe.cloth"), 5), Is.EqualTo(ActionError.None));
            for (int i = 0; i < 30 * 3; i++)
            {
                world.Tick(1f / 30f);
            }

            Vector3 camp = home + new Vector3(0f, 0f, 2.5f);
            a.Inventory.Hotbar.Set(4, ItemStack.Of(Item("item.deployable.campfire")));
            Assert.That(world.Deploy(a, 4, camp, 90), Is.EqualTo(ActionError.None));
            WorldEntity fire = world.Entities.All.Single(e => e.Def.Key.Value == "entity.campfire");
            fire.Container.Set(0, ItemStack.Of(Item("item.resource.wood"), 20));
            fire.Container.Set(1, ItemStack.Of(Item("item.food.mushroom_raw"), 4));
            Assert.That(world.ToggleProcessor(a, fire.Id), Is.EqualTo(ActionError.None));
            a.Inventory.Hotbar.Set(4, ItemStack.Of(Item("item.deployable.bedroll")));
            Assert.That(world.Deploy(a, 4, home + new Vector3(2.5f, 0f, -0.5f), 0), Is.EqualTo(ActionError.None));
            Assert.That(world.Drop(a, ContainerRef.Main, 3, 12), Is.EqualTo(ActionError.None));

            Character b = world.Join("acc-b", "Bo");
            b.Inventory.Main.Set(7, ItemStack.Of(Item("item.resource.stone"), 99));
            world.Die(b, DamageCause.Cold);

            int tree = world.Resources.FindNearest(Vector3.Zero, 400, n => n.Def.Kind == GatherKind.Wood);
            world.Resources.Harvest(tree, 50f, world.Now);
            int rock = world.Resources.FindNearest(Vector3.Zero, 400, n => n.Def.Kind == GatherKind.Stone);
            world.Resources.Harvest(rock, 10000f, world.Now);

            for (int i = 0; i < 30 * 4; i++)
            {
                world.Tick(1f / 30f);
            }

            return world;
        }

        static Vector3 OpenGround(GameWorld world)
        {
            var scratch = new List<int>();
            for (int x = -100; x <= 100; x += 7)
            {
                for (int z = -100; z <= 100; z += 7)
                {
                    var p = new Vector3(x, 0f, z);
                    if (x * x + z * z < 40 * 40)
                    {
                        continue;
                    }

                    var probe = new Kindling.Simulation.Collision.Aabb(p - new Vector3(5, 0, 5), p + new Vector3(5, 3, 5));
                    if (!world.Collision.Overlaps(probe, scratch) && !world.Map.Static.IsInPond(p, 6f) && world.Resources.FindNearest(p, 6f, n => true) < 0
                        && !world.Entities.All.Any(e => Vector3.Distance(e.Position, p) < 6f))
                    {
                        return p;
                    }
                }
            }

            Assert.Fail("No open ground");
            return default;
        }

        [Test]
        public void EmptyStoreStartsAFreshPopulatedWorld()
        {
            using IWorldStore store = CreateStore(Dir);
            Assert.That(store.Load(), Is.Null.Or.Property("Meta").Empty);
            GameWorld world = Load(CreateStore(Dir), out bool loaded);
            Assert.That(loaded, Is.False);
            Assert.That(world.Entities.All.Count(e => e.Def.Kind == EntityKind.LootContainer), Is.GreaterThan(0));
        }

        [Test]
        public void CheckpointRestoresTheWorldExactly()
        {
            GameWorld original;
            using (IWorldStore store = CreateStore(Dir))
            {
                original = Busy(store);
                new WorldPersistence(original, store).Checkpoint();
                store.Flush();
            }

            var warnings = new List<string>();
            using IWorldStore reopened = CreateStore(Dir);
            GameWorld restored = Load(reopened, out bool loaded, warnings);
            Assert.That(loaded, Is.True);
            Assert.That(warnings, Is.Empty);
            WorldComparer.AssertSame(original, restored);
        }

        [Test]
        public void IncrementalSavesApplyChangesAndRemovals()
        {
            GameWorld world;
            using (IWorldStore store = CreateStore(Dir))
            {
                world = Busy(store);
                var persistence = new WorldPersistence(world, store);
                persistence.Checkpoint();

                Character a = world.FindCharacter("acc-a");
                a.Inventory.Main.Set(3, ItemStack.Of(Item("item.resource.wood"), 5));
                WorldEntity bed = world.Entities.All.Single(e => e.Def.IsRespawnPoint);
                world.Entities.Remove(bed.Id);
                WorldEntity chest = world.Entities.Spawn(Db.Entity("entity.small_chest"), new Vector3(-30, 0, 90), 180);
                chest.OwnerId = "acc-a";
                chest.Container.Set(4, ItemStack.Of(Item("item.medical.bandage"), 3));
                world.Entities.MarkChanged(chest);
                int pine = world.Resources.FindNearest(new Vector3(100, 0, 100), 400, n => n.Def.Kind == GatherKind.Wood);
                world.Resources.Harvest(pine, 33f, world.Now);

                SaveBatch batch = persistence.SaveChanges();
                Assert.That(batch.Full, Is.False);
                Assert.That(batch.RemovedEntities, Has.Member(bed.Id));
                store.Flush();
            }

            using IWorldStore reopened = CreateStore(Dir);
            GameWorld restored = Load(reopened, out _);
            WorldComparer.AssertSame(world, restored);
        }

        [Test]
        public void RestartedCharactersSleepWhereTheyWereAndIdsAreNotReused()
        {
            uint nextId;
            using (IWorldStore store = CreateStore(Dir))
            {
                GameWorld world = Busy(store);
                nextId = world.Entities.NextId;
                new WorldPersistence(world, store).Checkpoint();
                store.Flush();
            }

            using IWorldStore reopened = CreateStore(Dir);
            GameWorld restored = Load(reopened, out _);
            Character a = restored.FindCharacter("acc-a");
            Assert.That(a.Online, Is.False);
            WorldEntity sleeper = restored.Entities.Get(a.SleeperEntityId);
            Assert.That(sleeper.Def.Kind, Is.EqualTo(EntityKind.Sleeper));
            Assert.That(sleeper.Position, Is.EqualTo(a.Motion.Position));
            Assert.That(sleeper.Id, Is.GreaterThanOrEqualTo(nextId), "new ids continue after the saved ones");
            Assert.That(restored.FindCharacter("acc-b").SleeperEntityId, Is.EqualTo(0u), "the dead don't sleep");

            Character back = restored.Join("acc-a", "Ana");
            Assert.That(back, Is.SameAs(a));
            Assert.That(back.Inventory.Main[3].Amount, Is.EqualTo(765));
        }

        [Test]
        public void ASaveForAnotherSeedIsRefused()
        {
            using (IWorldStore store = CreateStore(Dir))
            {
                GameWorld world = Busy(store);
                new WorldPersistence(world, store).Checkpoint();
                store.Flush();
            }

            using IWorldStore reopened = CreateStore(Dir);
            WorldMap other = WorldMap.Generate(2, Db);
            Assert.Throws<InvalidOperationException>(() =>
                WorldPersistence.LoadOrCreate(Db, other, GameConfig.FromRules(Db.Rules), 42, reopened, new List<string>(), out _));
        }
    }

    public class FileWorldStoreTests : WorldStoreContract
    {
        protected override IWorldStore CreateStore(string directory) => new FileWorldStore(Path.Combine(directory, "world.kws"));

        [Test]
        public void ADamagedSaveFallsBackToTheBackup()
        {
            string path = Path.Combine(Dir, "world.kws");
            var store = new FileWorldStore(path);
            store.Load();
            var batch = new SaveBatch { Full = true };
            batch.Meta["world_time"] = "10";
            store.Write(batch);
            batch.Meta["world_time"] = "20";
            store.Write(batch);
            Assert.That(File.Exists(path + ".bak"), Is.True);

            byte[] bytes = File.ReadAllBytes(path);
            bytes[bytes.Length / 2] ^= 0xFF;
            File.WriteAllBytes(path, bytes);

            var reopened = new FileWorldStore(path);
            WorldSaveData data = reopened.Load();
            Assert.That(data.Meta["world_time"], Is.EqualTo("10"), "previous save");
            Assert.That(reopened.Warnings, Has.Some.Contains("Checksum"));

            File.WriteAllText(path + ".bak", "garbage");
            Assert.Throws<InvalidDataException>(() => new FileWorldStore(path).Load());
        }

        [Test]
        public void UnknownItemsInASaveAreDroppedWithAWarning()
        {
            var c = new ItemContainer(ContainerRole.Storage, 4);
            c.Set(1, ItemStack.Of(Db.Item("item.resource.wood"), 10));
            var w = new Kindling.Core.Serialization.PacketWriter();
            SaveCodec.WriteContainer(w, c);
            byte[] bytes = w.ToArray();
            bytes[3] = 0xFE; // net id low byte → an id that doesn't exist
            bytes[4] = 0xFF;
            var r = new Kindling.Core.Serialization.PacketReader(bytes);
            var warnings = new List<string>();
            var target = new ItemContainer(ContainerRole.Storage, 4);
            SaveCodec.ReadContainer(ref r, target, Db, warnings);
            Assert.That(target.IsEmpty, Is.True);
            Assert.That(warnings, Has.Count.EqualTo(1));
        }
    }
}
