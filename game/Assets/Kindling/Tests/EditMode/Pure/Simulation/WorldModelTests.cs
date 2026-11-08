using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Kindling.Content;
using Kindling.Core.Numerics;
using Kindling.Core.World;
using Kindling.Simulation.Collision;
using Kindling.Simulation.Items;
using Kindling.Simulation.World;
using NUnit.Framework;

namespace Kindling.Tests.Pure.Simulation
{
    public class WorldModelTests
    {
        static ContentDatabase Db => ContentDatabase.LoadDefault();
        static readonly WorldMap Map = WorldMap.Generate(1, Db);
        static WorldGrid Grid => new WorldGrid(GreyboxLayout.MapSize);

        [Test]
        public void MapPlacesNodesPondsSpawnsAndLootWithoutOverlaps()
        {
            foreach (NodeDef def in Db.Nodes)
            {
                int count = Map.Static.Nodes.Count(n => n.Def == def);
                Assert.That(count, Is.GreaterThan(def.MapCount * 0.9), def.Key.Value);
            }

            Assert.That(Map.Static.Ponds.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(Map.SpawnPoints.Count, Is.GreaterThanOrEqualTo(24));
            Assert.That(Map.LootSpawns.Count(l => l.Def.Kind == EntityKind.Breakable), Is.EqualTo(Db.Entity("entity.loot_barrel").MapCount));

            var boxes = Map.Static.Pieces.Select(p => p.Box).ToList();
            foreach (NodePlacement n in Map.Static.Nodes)
            {
                Assert.That(n.Position.X * n.Position.X + n.Position.Z * n.Position.Z, Is.GreaterThan(30f * 30f), "outside the spawn plaza");
                Assert.That(boxes.Any(b => b.Intersects(n.Bounds)), Is.False, $"{n.Def.Key} at {n.Position} overlaps map geometry");
            }

            CollisionWorld world = Map.Static.BuildCollisionWorld();
            var scratch = new List<int>();
            foreach (Vector3 spawn in Map.SpawnPoints)
            {
                Assert.That(world.Overlaps(Aabb.FromFeet(spawn, 0.3f, 1.8f), scratch), Is.False);
            }
        }

        [Test]
        public void MapIsDeterministicAndTreesBlockButPlantsDont()
        {
            WorldMap again = WorldMap.Generate(1, Db);
            Assert.That(again.Static.Nodes.Select(n => n.Position), Is.EqualTo(Map.Static.Nodes.Select(n => n.Position)));
            int blocking = Map.Static.Nodes.Count(n => n.Blocks);
            Assert.That(Map.Static.BuildCollisionWorld().BoxCount, Is.EqualTo(Map.Static.Pieces.Count + blocking));
            Assert.That(Map.Static.Nodes.Where(n => n.Def.Pickable).All(n => !n.Blocks), Is.True);
        }

        [Test]
        public void HarvestingDepletesAndRespawnsNodes()
        {
            var field = new ResourceField(Map.Static, Grid);
            int tree = field.FindNearest(Vector3.Zero, 300f, n => n.Def.Key.Value == "node.tree.pine");
            Assert.That(tree, Is.GreaterThanOrEqualTo(0));
            ChunkCoord chunk = field.ChunkOf(field.Placement(tree).Position);
            uint version = field.ChunkVersion(chunk);

            Assert.That(field.Harvest(tree, 100f, now: 10), Is.EqualTo(100f));
            Assert.That(field.ChunkVersion(chunk), Is.EqualTo(version), "partial harvest isn't replicated");
            Assert.That(field.Harvest(tree, 500f, now: 11), Is.EqualTo(200f), "only what's left");
            Assert.That(field.IsAlive(tree), Is.False);
            Assert.That(field.ChunkVersion(chunk), Is.EqualTo(version + 1));
            Assert.That(field.Harvest(tree, 10f, now: 12), Is.EqualTo(0f));

            var changed = new List<int>();
            field.ConsumeChanges(changed);
            Assert.That(changed, Is.EqualTo(new[] { tree }));

            field.Update(11 + 899);
            Assert.That(field.IsAlive(tree), Is.False, "respawns after 900 s, not before");
            field.Update(11 + 900);
            Assert.That(field.IsAlive(tree), Is.True);
            Assert.That(field.Remaining(tree), Is.EqualTo(300f));
        }

        [Test]
        public void RayCastFindsTheNearestAliveNodeAndSweetSpotsMove()
        {
            var field = new ResourceField(Map.Static, Grid);
            int rock = field.FindNearest(Vector3.Zero, 300f, n => n.Def.Kind == GatherKind.Stone);
            NodePlacement p = field.Placement(rock);
            Vector3 eye = p.Position + new Vector3(0f, 1.0f, -3f);
            Vector3 dir = Vector3.Normalize(p.Position + new Vector3(0f, 0.7f, 0f) - eye);
            Assert.That(field.RayCast(eye, dir, 4f, out int hit, out Vector3 point), Is.True);
            Assert.That(hit, Is.EqualTo(rock));
            Assert.That(point.Z, Is.EqualTo(p.Bounds.Min.Z).Within(0.01f), "hits the near face");
            Assert.That(field.RayCast(eye, dir, 1.5f, out _, out _), Is.False, "out of reach");

            Vector3 spot = field.SweetSpot(rock);
            Assert.That(p.Bounds.Inflate(0.01f).Intersects(new Aabb(spot, spot + new Vector3(0.001f))), Is.True, "on the node");
            field.Harvest(rock, 1f, 0);
            Assert.That(field.SweetSpot(rock), Is.Not.EqualTo(spot), "moves after a hit");

            field.Harvest(rock, 1000f, 0);
            Assert.That(field.RayCast(eye, dir, 4f, out int after, out _) && after == rock, Is.False, "depleted nodes can't be hit");
        }

        [Test]
        public void EntitiesLiveInChunksAndLogChanges()
        {
            var store = new EntityStore(Grid);
            EntityDef chest = Db.Entity("entity.small_chest");
            WorldEntity e = store.Spawn(chest, new Vector3(10, 0, 10), 0);
            Assert.That(e.Container.Capacity, Is.EqualTo(12));
            ChunkCoord a = store.ChunkOf(e.Position);
            Assert.That(store.InChunk(a), Has.Member(e));

            var changes = new List<EntityChangeRecord>();
            store.ConsumeChanges(changes);
            Assert.That(changes.Single().Kind, Is.EqualTo(EntityChange.Created));

            store.Move(e, new Vector3(100, 0, 10));
            ChunkCoord b = store.ChunkOf(e.Position);
            Assert.That(b, Is.Not.EqualTo(a));
            Assert.That(store.InChunk(a), Has.No.Member(e));
            store.ConsumeChanges(changes);
            Assert.That(changes.Select(c => c.Kind), Is.EqualTo(new[] { EntityChange.Removed, EntityChange.Created }));

            var found = new List<WorldEntity>();
            store.Query(new Vector3(98, 0, 10), 3f, found);
            Assert.That(found, Has.Member(e));
            store.Query(new Vector3(90, 0, 10), 3f, found);
            Assert.That(found, Is.Empty);

            var dirty = new List<WorldEntity>();
            var removed = new List<uint>();
            store.ConsumeDirty(dirty, removed);
            Assert.That(dirty, Has.Member(e));
            Assert.That(store.Remove(e.Id), Is.True);
            store.ConsumeDirty(dirty, removed);
            Assert.That(removed, Is.EqualTo(new[] { e.Id }));
            Assert.That(store.ChunkVersion(b), Is.GreaterThan(0u));
        }

        [Test]
        public void ClockRunsDayAndNight()
        {
            var rules = new WorldRules { DayLengthMinutes = 60, NightFraction = 0.25f };
            var clock = new WorldClock(rules);
            var temp = new TemperatureRules { DayWarmth = 5, NightWarmth = -35 };
            Assert.That(clock.IsNight, Is.False);
            Assert.That(clock.TimeOfDay, Is.EqualTo(WorldClock.StartTimeOfDay).Within(1e-9));
            clock.Advance(3600 * 0.3);
            Assert.That(clock.Daylight, Is.EqualTo(1f));
            Assert.That(clock.AmbientWarmth(temp), Is.EqualTo(5f));
            clock.Advance(3600 * 0.45);
            Assert.That(clock.IsNight, Is.True);
            Assert.That(clock.AmbientWarmth(temp), Is.EqualTo(-35f));
            clock.Advance(3600 * 0.2);
            Assert.That(clock.IsNight, Is.False, "next morning");
        }

        [Test]
        public void LootRollsAreDeterministicAndWithinRanges()
        {
            LootTableDef crate = Db.LootTable("loot.crate");
            var a = new List<ItemStack>();
            var b = new List<ItemStack>();
            LootRoller.Roll(crate, new DeterministicRandom(5), a);
            LootRoller.Roll(crate, new DeterministicRandom(5), b);
            Assert.That(a, Is.EqualTo(b));

            var rng = new DeterministicRandom(9);
            var seen = new HashSet<ItemDef>();
            for (int i = 0; i < 500; i++)
            {
                LootRoller.Roll(crate, rng, a);
                Assert.That(a.Count, Is.InRange(crate.MinRolls, crate.MaxRolls));
                foreach (ItemStack s in a)
                {
                    LootEntry entry = crate.Entries.First(e => e.Item == s.Def);
                    Assert.That(s.Amount, Is.InRange(entry.Min, entry.Max));
                    seen.Add(s.Def);
                }
            }

            Assert.That(seen.Count, Is.EqualTo(crate.Entries.Count), "every entry shows up");
        }
    }
}
