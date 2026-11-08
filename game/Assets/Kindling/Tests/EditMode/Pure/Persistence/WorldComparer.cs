using System.Linq;
using Kindling.Simulation.Game;
using Kindling.Simulation.Items;
using Kindling.Simulation.World;
using NUnit.Framework;

namespace Kindling.Tests.Pure.Persistence
{
    /// <summary>Deep comparison of everything a save is supposed to keep.</summary>
    public static class WorldComparer
    {
        public static void AssertSame(GameWorld a, GameWorld b)
        {
            Assert.That(b.Now, Is.EqualTo(a.Now), "world time");
            Assert.That(b.Characters.Select(c => c.AccountId), Is.EquivalentTo(a.Characters.Select(c => c.AccountId)));
            foreach (Character ca in a.Characters)
            {
                Character cb = b.FindCharacter(ca.AccountId);
                string who = ca.AccountId;
                Assert.That(cb.Name, Is.EqualTo(ca.Name), who);
                Assert.That(cb.Life, Is.EqualTo(ca.Life), who);
                Assert.That(cb.Motion.Position, Is.EqualTo(ca.Motion.Position), who);
                Assert.That(cb.Motion.Yaw, Is.EqualTo(ca.Motion.Yaw), who);
                Assert.That(cb.Motion.Stamina, Is.EqualTo(ca.Motion.Stamina), who);
                Assert.That(cb.Stats.Health, Is.EqualTo(ca.Stats.Health), who);
                Assert.That(cb.Stats.Hunger, Is.EqualTo(ca.Stats.Hunger), who);
                Assert.That(cb.Stats.Thirst, Is.EqualTo(ca.Stats.Thirst), who);
                Assert.That(cb.Stats.Warmth, Is.EqualTo(ca.Stats.Warmth), who);
                Assert.That(cb.Stats.Effects, Is.EqualTo(ca.Stats.Effects), who);
                Assert.That(cb.Stats.PoisonLeft, Is.EqualTo(ca.Stats.PoisonLeft), who);
                Assert.That(cb.Knowledge, Is.EquivalentTo(ca.Knowledge), who);
                Assert.That(cb.Inventory.ActiveSlot, Is.EqualTo(ca.Inventory.ActiveSlot), who);
                SameContainer(ca.Inventory.Main, cb.Inventory.Main, who + " main");
                SameContainer(ca.Inventory.Hotbar, cb.Inventory.Hotbar, who + " hotbar");
                SameContainer(ca.Inventory.Wear, cb.Inventory.Wear, who + " wear");
                Assert.That(cb.Crafting.Count, Is.EqualTo(ca.Crafting.Count), who + " crafting");
                for (int i = 0; i < ca.Crafting.Count; i++)
                {
                    Assert.That(cb.Crafting[i].Recipe, Is.SameAs(ca.Crafting[i].Recipe));
                    Assert.That(cb.Crafting[i].Remaining, Is.EqualTo(ca.Crafting[i].Remaining));
                    Assert.That(cb.Crafting[i].Progress, Is.EqualTo(ca.Crafting[i].Progress));
                }

                Assert.That(cb.NextCraftJobId, Is.EqualTo(ca.NextCraftJobId), who);
            }

            var aEntities = a.Entities.All.Where(e => e.Def.Kind != Kindling.Content.EntityKind.Sleeper).ToDictionary(e => e.Id);
            var bEntities = b.Entities.All.Where(e => e.Def.Kind != Kindling.Content.EntityKind.Sleeper).ToDictionary(e => e.Id);
            Assert.That(bEntities.Keys, Is.EquivalentTo(aEntities.Keys), "entity ids");
            foreach (WorldEntity ea in aEntities.Values)
            {
                WorldEntity eb = bEntities[ea.Id];
                string what = ea.ToString();
                Assert.That(eb.Def, Is.SameAs(ea.Def), what);
                Assert.That(eb.Position, Is.EqualTo(ea.Position), what);
                Assert.That(eb.Yaw, Is.EqualTo(ea.Yaw), what);
                Assert.That(eb.OwnerId, Is.EqualTo(ea.OwnerId), what);
                Assert.That(eb.Label, Is.EqualTo(ea.Label), what);
                Assert.That(eb.Health, Is.EqualTo(ea.Health), what);
                Assert.That(eb.Flags, Is.EqualTo(ea.Flags), what);
                Assert.That(eb.DecayAt, Is.EqualTo(ea.DecayAt), what);
                Assert.That(eb.SpawnerIndex, Is.EqualTo(ea.SpawnerIndex), what);
                Assert.That(eb.FuelLeft, Is.EqualTo(ea.FuelLeft), what);
                Assert.That(eb.Progress, Is.EqualTo(ea.Progress), what);
                Assert.That(eb.CooldownUntil, Is.EqualTo(ea.CooldownUntil), what);
                if (ea.Container != null)
                {
                    SameContainer(ea.Container, eb.Container, what);
                }
            }

            for (int i = 0; i < a.Resources.Count; i++)
            {
                Assert.That(b.Resources.IsAlive(i), Is.EqualTo(a.Resources.IsAlive(i)), $"node {i} alive");
                Assert.That(b.Resources.Remaining(i), Is.EqualTo(a.Resources.Remaining(i)), $"node {i} remaining");
                Assert.That(b.Resources.RespawnAt(i), Is.EqualTo(a.Resources.RespawnAt(i)), $"node {i} respawn");
            }

            for (int i = 0; i < a.LootSpawnStates.Length; i++)
            {
                Assert.That(b.LootSpawnStates[i].EntityId, Is.EqualTo(a.LootSpawnStates[i].EntityId), $"loot {i}");
                Assert.That(b.LootSpawnStates[i].NextAt, Is.EqualTo(a.LootSpawnStates[i].NextAt), $"loot {i}");
            }
        }

        static void SameContainer(ItemContainer a, ItemContainer b, string what)
        {
            Assert.That(b.Capacity, Is.EqualTo(a.Capacity), what);
            for (int i = 0; i < a.Capacity; i++)
            {
                Assert.That(b[i].Def, Is.SameAs(a[i].Def), $"{what} slot {i}");
                Assert.That(b[i].Amount, Is.EqualTo(a[i].Amount), $"{what} slot {i}");
                Assert.That(b[i].Condition, Is.EqualTo(a[i].Condition), $"{what} slot {i}");
            }
        }
    }
}
