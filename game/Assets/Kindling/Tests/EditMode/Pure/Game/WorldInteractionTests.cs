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
    public class WorldInteractionTests
    {
        static ItemDef Wood => GameKit.Item("item.resource.wood");

        static WorldEntity Place(GameKit kit, Character c, string itemKey, Vector3 offset, float yaw = 0)
        {
            c.Inventory.Hotbar.Set(4, ItemStack.Of(GameKit.Item(itemKey)));
            Assert.That(kit.World.Deploy(c, 4, c.Motion.Position + offset, yaw), Is.EqualTo(ActionError.None));
            return kit.World.Entities.All.Last(e => e.Def == GameKit.Item(itemKey).Deploys);
        }

        [Test]
        public void DeployingChecksReachOverlapsAndConsumesTheItem()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            c.Motion.Position = kit.OpenGround();
            c.Inventory.Hotbar.Set(4, ItemStack.Of(GameKit.Item("item.deployable.small_chest")));
            Assert.That(kit.World.Deploy(c, 4, c.Motion.Position + new Vector3(10, 0, 0), 0), Is.EqualTo(ActionError.TooFar));
            Assert.That(kit.World.Deploy(c, 0, c.Motion.Position + new Vector3(1, 0, 0), 0), Is.EqualTo(ActionError.NotUsable), "slot 0 holds the rock");
            Assert.That(kit.World.Deploy(c, 4, c.Motion.Position + new Vector3(2, 0, 0), 37), Is.EqualTo(ActionError.None));
            WorldEntity chest = kit.World.Entities.All.Single(e => e.Def.Key.Value == "entity.small_chest");
            Assert.That(chest.Yaw, Is.EqualTo(0f), "snapped to 90° steps");
            Assert.That(c.Inventory.Hotbar[4].IsEmpty, Is.True);

            c.Inventory.Hotbar.Set(4, ItemStack.Of(GameKit.Item("item.deployable.campfire")));
            Assert.That(kit.World.Deploy(c, 4, chest.Position + new Vector3(0.2f, 0, 0), 0), Is.EqualTo(ActionError.InvalidPlacement), "overlaps the chest");

            int tree = kit.World.Resources.FindNearest(c.Motion.Position, 300, n => n.Def.Kind == GatherKind.Wood);
            c.Motion.Position = kit.World.Resources.Placement(tree).Position + new Vector3(2, 0, 0);
            Assert.That(kit.World.Deploy(c, 4, kit.World.Resources.Placement(tree).Position, 0), Is.EqualTo(ActionError.InvalidPlacement), "inside a tree");
        }

        [Test]
        public void ContainersMustBeOpenAndInRange()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            c.Motion.Position = kit.OpenGround();
            WorldEntity chest = Place(kit, c, "item.deployable.small_chest", new Vector3(0, 0, 1.5f));
            c.Inventory.Main.Set(0, ItemStack.Of(Wood, 300));
            ContainerRef box = ContainerRef.OfEntity(chest.Id);
            Assert.That(kit.World.MoveItem(c, ContainerRef.Main, 0, box, 0, 300), Is.EqualTo(ActionError.NoAccess));
            Assert.That(kit.World.Interact(c, InteractTarget.Entity, chest.Id), Is.EqualTo(ActionError.None));
            Assert.That(kit.World.MoveItem(c, ContainerRef.Main, 0, box, 0, 200), Is.EqualTo(ActionError.None));
            Assert.That(kit.World.MoveItem(c, ContainerRef.Main, 0, box, -1, 0), Is.EqualTo(ActionError.None), "quick move merges the rest");
            Assert.That(chest.Container.Count(Wood), Is.EqualTo(300));

            c.Motion.Position += new Vector3(20, 0, 0);
            Assert.That(kit.World.MoveItem(c, box, 0, ContainerRef.Main, 0, 10), Is.EqualTo(ActionError.TooFar));
            Assert.That(kit.World.OpenContainerOf(c), Is.Null, "walking away closes it");
            Assert.That(c.OpenContainerId, Is.EqualTo(0u));
        }

        [Test]
        public void DroppedItemsCanBePickedUpAndDecay()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            c.Motion.Position = kit.OpenGround();
            c.Inventory.Main.Set(0, ItemStack.Of(Wood, 100));
            Assert.That(kit.World.Drop(c, ContainerRef.Main, 0, 40), Is.EqualTo(ActionError.None));
            WorldEntity dropped = kit.World.Entities.All.Single(e => e.Def.Kind == EntityKind.DroppedItem);
            Assert.That(dropped.Container[0].Amount, Is.EqualTo(40));
            Assert.That(kit.World.Interact(c, InteractTarget.Entity, dropped.Id), Is.EqualTo(ActionError.None));
            Assert.That(c.Inventory.Count(Wood), Is.EqualTo(100));
            Assert.That(kit.World.Entities.Get(dropped.Id), Is.Null);

            kit.World.Drop(c, ContainerRef.Main, 0, 10);
            uint id = kit.World.Entities.All.Single(e => e.Def.Kind == EntityKind.DroppedItem).Id;
            kit.Run(GameKit.Db.Rules.World.DroppedItemSeconds + 1);
            Assert.That(kit.World.Entities.Get(id), Is.Null);
        }

        [Test]
        public void CampfiresCookWithFuelAndGoOut()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            c.Motion.Position = kit.OpenGround();
            WorldEntity fire = Place(kit, c, "item.deployable.campfire", new Vector3(0, 0, 1.5f));
            Assert.That(kit.World.ToggleProcessor(c, fire.Id), Is.EqualTo(ActionError.NotEnoughItems), "no fuel");
            fire.Container.Set(0, ItemStack.Of(Wood, 10));
            fire.Container.Set(1, ItemStack.Of(GameKit.Item("item.food.mushroom_raw"), 3));
            Assert.That(kit.World.ToggleProcessor(c, fire.Id), Is.EqualTo(ActionError.None));
            Assert.That(fire.IsLit, Is.True);
            kit.Run(25);
            Assert.That(fire.Container.Count(GameKit.Item("item.food.mushroom_cooked")), Is.EqualTo(3));
            Assert.That(fire.Container.Count(GameKit.Item("item.resource.charcoal")), Is.GreaterThan(0), "burning wood makes charcoal");
            kit.Run(10);
            Assert.That(fire.IsLit, Is.False, "30 s of wood burned out");
            Assert.That(fire.Container.Count(Wood), Is.Zero);
        }

        [Test]
        public void SmeltersTurnOreIntoBars()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            c.Motion.Position = kit.OpenGround();
            WorldEntity smelter = Place(kit, c, "item.deployable.smelter", new Vector3(0, 0, 2f));
            smelter.Container.Set(0, ItemStack.Of(Wood, 100));
            smelter.Container.Set(1, ItemStack.Of(GameKit.Item("item.resource.iron_ore"), 5));
            Assert.That(kit.World.ToggleProcessor(c, smelter.Id), Is.EqualTo(ActionError.None));
            kit.Run(21);
            Assert.That(smelter.Container.Count(GameKit.Item("item.resource.iron_bar")), Is.EqualTo(5));
            Assert.That(smelter.Container.Count(GameKit.Item("item.resource.iron_ore")), Is.Zero);
        }

        [Test]
        public void BarrelsBreakIntoLootAndRespawnAndCratesRefill()
        {
            var kit = new GameKit(populate: true);
            Character c = kit.Join();
            WorldEntity barrel = kit.World.Entities.All.First(e => e.Def.Kind == EntityKind.Breakable);
            int spawn = barrel.SpawnerIndex;
            Vector3 eye = barrel.Position + new Vector3(0, 0, -1.6f);
            c.Motion.Position = eye;
            GameKit.Aim(kit.World.EyeOf(c), barrel.Position + new Vector3(0, 0.5f, 0), out float yaw, out float pitch);
            kit.Hold(c, "item.tool.rock");
            int dropsBefore = kit.World.Entities.All.Count(e => e.Def.Kind == EntityKind.DroppedItem);
            for (int i = 0; i < 4; i++)
            {
                Assert.That(kit.World.Swing(c, yaw, pitch), Is.EqualTo(ActionError.None), $"hit {i}");
                kit.Run(1.2);
            }

            Assert.That(kit.World.Entities.Get(barrel.Id), Is.Null, "40 health, 10 per hit");
            Assert.That(kit.World.Entities.All.Count(e => e.Def.Kind == EntityKind.DroppedItem), Is.GreaterThan(dropsBefore));
            kit.Run(barrel.Def.RefillSeconds + 1);
            Assert.That(kit.World.Entities.Get(kit.World.LootSpawnStates[spawn].EntityId)?.Def, Is.SameAs(barrel.Def), "a new barrel");

            WorldEntity crate = kit.World.Entities.All.First(e => e.Def.Kind == EntityKind.LootContainer);
            Assert.That(crate.Container.IsEmpty, Is.False);
            crate.Container.Clear();
            kit.Run(crate.Def.RefillSeconds + 1);
            Assert.That(crate.Container.IsEmpty, Is.False, "refilled");
        }

        [Test]
        public void ClothingCanBeWornAndItemActionsNeedALivingCharacter()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            c.Inventory.Main.Set(0, ItemStack.Of(GameKit.Item("item.clothing.fiber_shirt")));
            Assert.That(kit.World.UseItem(c, ContainerRef.Main, 0), Is.EqualTo(ActionError.None));
            Assert.That(c.Inventory.Wear[(int)EquipSlot.Chest - 1].Def.Key.Value, Is.EqualTo("item.clothing.fiber_shirt"));
            kit.World.Die(c, DamageCause.Admin);
            Assert.That(kit.World.MoveItem(c, ContainerRef.Main, 0, ContainerRef.Main, 1, 1), Is.EqualTo(ActionError.Dead));
            Assert.That(kit.World.Swing(c, 0, 0), Is.EqualTo(ActionError.Dead));
            Assert.That(kit.World.Craft(c, GameKit.Db.Recipe("recipe.cloth"), 1), Is.EqualTo(ActionError.Dead));
        }
    }
}
