using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Kindling.Content;
using Kindling.Simulation;
using Kindling.Simulation.Game;
using Kindling.Simulation.Items;
using Kindling.Simulation.World;
using NUnit.Framework;

namespace Kindling.Tests.Pure.Game
{
    public class SurvivalAndLifeTests
    {
        [Test]
        public void NewPlayersSpawnAtTheEdgeWithAStarterKitAndRejoinTheSameCharacter()
        {
            var kit = new GameKit();
            Character c = kit.Join("acc-1", "Ana");
            Assert.That(c.IsAlive, Is.True);
            Assert.That(System.Math.Max(System.Math.Abs(c.Motion.Position.X), System.Math.Abs(c.Motion.Position.Z)), Is.GreaterThan(200f), "edge spawn");
            Assert.That(c.Inventory.Count(GameKit.Item("item.tool.rock")), Is.EqualTo(1));
            Assert.That(c.Inventory.Count(GameKit.Item("item.tool.torch")), Is.EqualTo(1));
            Assert.That(c.Stats.Health, Is.EqualTo(GameKit.Db.Rules.Death.RespawnHealth));

            c.Inventory.Main.Set(5, ItemStack.Of(GameKit.Item("item.resource.wood"), 77));
            kit.World.Leave(c);
            WorldEntity sleeper = kit.World.Entities.Get(c.SleeperEntityId);
            Assert.That(sleeper.Def.Kind, Is.EqualTo(EntityKind.Sleeper));
            Assert.That(sleeper.Label, Is.EqualTo("Ana"));

            Character again = kit.World.Join("acc-1", "Ana");
            Assert.That(again, Is.SameAs(c));
            Assert.That(again.Inventory.Count(GameKit.Item("item.resource.wood")), Is.EqualTo(77));
            Assert.That(kit.World.Entities.Get(sleeper.Id), Is.Null, "awake: sleeper gone");
            Assert.That(kit.World.Characters.Count, Is.EqualTo(1));
        }

        [Test]
        public void VanishModeHidesSleepersAfterAMinuteButKeepsTheCharacter()
        {
            var kit = new GameKit(new GameConfig { SleeperMode = SleeperMode.Vanish });
            Character c = kit.Join();
            kit.World.Leave(c);
            uint sleeper = c.SleeperEntityId;
            kit.Run(30);
            Assert.That(kit.World.Entities.Get(sleeper), Is.Not.Null);
            kit.Run(31);
            Assert.That(kit.World.Entities.Get(sleeper), Is.Null);
            Assert.That(kit.World.FindCharacter("acc-1"), Is.SameAs(c));
        }

        [Test]
        public void HungerAndThirstDrainFasterWhenSprinting()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            c.Stats.Hunger = c.Stats.Thirst = 80;
            kit.Run(60);
            Assert.That(c.Stats.Hunger, Is.EqualTo(80 - 1.5f).Within(0.05f));
            Assert.That(c.Stats.Thirst, Is.EqualTo(80 - 2.5f).Within(0.05f));
            c.Motion.Sprinting = true;
            kit.Run(60);
            Assert.That(c.Stats.Hunger, Is.EqualTo(80 - 1.5f - 3f).Within(0.1f));
        }

        [Test]
        public void StarvingLeadsToDownedThenDeathWithABody()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            c.Stats.Hunger = 0;
            c.Stats.Thirst = 0;
            c.Stats.Health = 3;
            c.Inventory.Main.Set(0, ItemStack.Of(GameKit.Item("item.resource.stone"), 50));
            kit.Run(20);
            Assert.That(c.Life, Is.EqualTo(LifeState.Downed));
            Assert.That(c.Events.Any(e => e.Kind == GameEventKind.Downed), Is.True);
            kit.Run(31);
            Assert.That(c.Life, Is.EqualTo(LifeState.Dead));
            Assert.That(c.LastDamage, Is.EqualTo(DamageCause.Dehydration));
            GameEvent died = c.Events.Single(e => e.Kind == GameEventKind.Died);
            WorldEntity body = kit.World.Entities.Get((uint)died.Target);
            Assert.That(body.Def.Kind, Is.EqualTo(EntityKind.Body));
            Assert.That(body.Container.Count(GameKit.Item("item.resource.stone")), Is.EqualTo(50));
            Assert.That(body.Container.Count(GameKit.Item("item.tool.rock")), Is.EqualTo(1));
            Assert.That(c.Inventory.Count(GameKit.Item("item.resource.stone")), Is.Zero);
            kit.Run(GameKit.Db.Entity("entity.body").DecaySeconds + 1);
            Assert.That(kit.World.Entities.Get(body.Id), Is.Null, "bodies decay");
        }

        [Test]
        public void DownedPlayersCanBeRevivedOrGiveUp()
        {
            var kit = new GameKit();
            Character a = kit.Join("a");
            Character b = kit.Join("b");
            b.Motion.Position = a.Motion.Position + new Vector3(1, 0, 0);
            kit.World.Damage(a, 500, DamageCause.Admin);
            Assert.That(a.Life, Is.EqualTo(LifeState.Downed));
            Assert.That(kit.World.Revive(b, a), Is.EqualTo(ActionError.None));
            Assert.That(a.IsAlive, Is.True);
            Assert.That(a.Stats.Health, Is.EqualTo(10f));

            kit.World.Damage(a, 500, DamageCause.Admin);
            Assert.That(kit.World.GiveUp(a), Is.EqualTo(ActionError.None));
            Assert.That(a.Life, Is.EqualTo(LifeState.Dead));
            Assert.That(kit.World.Revive(b, a), Is.EqualTo(ActionError.InvalidTarget));
        }

        [Test]
        public void RespawnAtYourOwnBedrollWithACooldown()
        {
            var kit = new GameKit();
            Character c = kit.Join("owner");
            Character other = kit.Join("other");
            Vector3 spot = kit.OpenGround();
            c.Motion.Position = spot;
            c.Inventory.Hotbar.Set(2, ItemStack.Of(GameKit.Item("item.deployable.bedroll")));
            Assert.That(kit.World.Deploy(c, 2, spot + new Vector3(0, 0, 2), 0), Is.EqualTo(ActionError.None));
            WorldEntity bed = kit.World.Entities.All.Single(e => e.Def.IsRespawnPoint);
            Assert.That(bed.OwnerId, Is.EqualTo("owner"));

            kit.World.Die(c, DamageCause.Admin);
            kit.World.Die(other, DamageCause.Admin);
            Assert.That(kit.World.Respawn(other, bed.Id), Is.EqualTo(ActionError.NoAccess));
            Assert.That(kit.World.Respawn(c, bed.Id), Is.EqualTo(ActionError.None));
            Assert.That(Vector3.Distance(c.Motion.Position, bed.Position), Is.LessThan(0.5f));
            Assert.That(c.Inventory.Count(GameKit.Item("item.tool.rock")), Is.EqualTo(1), "starter kit again");

            kit.World.Die(c, DamageCause.Admin);
            Assert.That(kit.World.Respawn(c, bed.Id), Is.EqualTo(ActionError.Cooldown));
            Assert.That(kit.World.Respawn(c, 0), Is.EqualTo(ActionError.None), "edge spawn has no cooldown");
            kit.World.Die(c, DamageCause.Admin);
            kit.Run(bed.Def.RespawnCooldownSeconds);
            Assert.That(kit.World.Respawn(c, bed.Id), Is.EqualTo(ActionError.None));
            var beds = new List<WorldEntity>();
            kit.World.RespawnPointsOf(c, beds);
            Assert.That(beds, Is.EqualTo(new[] { bed }));
        }

        [Test]
        public void NightIsColdWithoutClothesOrFireAndCampfiresWarm()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            c.Motion.Position = kit.OpenGround();
            kit.World.Clock.Seconds = kit.World.Clock.DayLengthSeconds * 0.8; // night
            c.Stats.Health = 100;
            kit.Run(180);
            Assert.That(c.Stats.Warmth, Is.LessThan(-30f));
            Assert.That(c.Stats.Has(StatusEffect.Cold), Is.True);
            Assert.That(c.Stats.Health, Is.LessThan(100f), "the cold hurts");

            foreach (string key in new[] { "item.clothing.fiber_hood", "item.clothing.fiber_shirt", "item.clothing.fiber_pants", "item.clothing.fiber_wraps" })
            {
                c.Inventory.Main.Set(0, ItemStack.Of(GameKit.Item(key)));
                Assert.That(kit.World.UseItem(c, ContainerRef.Main, 0), Is.EqualTo(ActionError.None), "use = wear");
            }

            Assert.That(c.Inventory.Insulation, Is.EqualTo(33f));
            c.Inventory.Hotbar.Set(3, ItemStack.Of(GameKit.Item("item.deployable.campfire")));
            Assert.That(kit.World.Deploy(c, 3, c.Motion.Position + new Vector3(1.5f, 0, 0), 0), Is.EqualTo(ActionError.None));
            WorldEntity fire = kit.World.Entities.All.Single(e => e.Def.Key.Value == "entity.campfire");
            fire.Container.Add(GameKit.Item("item.resource.wood"), 200);
            kit.World.OpenContainerOf(c);
            Assert.That(kit.World.ToggleProcessor(c, fire.Id), Is.EqualTo(ActionError.None));
            kit.Run(240);
            Assert.That(c.Stats.Has(StatusEffect.Cold), Is.False);
            Assert.That(c.Stats.Has(StatusEffect.Comfort), Is.True);
            Assert.That(c.Stats.Warmth, Is.GreaterThan(-20f));
        }

        [Test]
        public void FoodWaterAndBandages()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            c.Stats.Hunger = 50;
            c.Inventory.Main.Set(0, ItemStack.Of(GameKit.Item("item.food.berries"), 3));
            Assert.That(kit.World.UseItem(c, ContainerRef.Main, 0), Is.EqualTo(ActionError.None));
            Assert.That(c.Stats.Hunger, Is.EqualTo(56f));
            Assert.That(c.Inventory.Main[0].Amount, Is.EqualTo(2));

            int poisoned = 0;
            for (int i = 0; i < 100; i++)
            {
                c.Stats.PoisonLeft = 0;
                c.Inventory.Main.Set(1, ItemStack.Of(GameKit.Item("item.food.mushroom_raw")));
                kit.World.UseItem(c, ContainerRef.Main, 1);
                poisoned += c.Stats.PoisonLeft > 0 ? 1 : 0;
            }

            Assert.That(poisoned, Is.InRange(15, 45), "raw mushrooms poison about 30% of the time");

            c.Stats.Health = 50;
            c.Stats.Set(StatusEffect.Bleeding, true);
            c.Stats.PoisonLeft = 0;
            c.Inventory.Main.Set(2, ItemStack.Of(GameKit.Item("item.medical.bandage")));
            Assert.That(kit.World.UseItem(c, ContainerRef.Main, 2), Is.EqualTo(ActionError.None));
            Assert.That(c.Stats.Has(StatusEffect.Bleeding), Is.False);
            kit.Run(10);
            Assert.That(c.Stats.Health, Is.GreaterThan(64f), "bandage heals 15 over 10 s");

            c.Inventory.Main.Set(3, ItemStack.Of(GameKit.Item("item.resource.wood"), 5));
            Assert.That(kit.World.UseItem(c, ContainerRef.Main, 3), Is.EqualTo(ActionError.NotUsable));
        }

        [Test]
        public void DrinkingFromAPond()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            c.Stats.Thirst = 10;
            Assert.That(kit.World.Interact(c, InteractTarget.Pond, 0), Is.EqualTo(ActionError.TooFar));
            var pond = kit.World.Map.Static.Ponds[0];
            c.Motion.Position = (pond.Min + pond.Max) * 0.5f;
            Assert.That(kit.World.Interact(c, InteractTarget.Pond, 0), Is.EqualTo(ActionError.None));
            Assert.That(c.Stats.Thirst, Is.EqualTo(35f));
        }
    }
}
