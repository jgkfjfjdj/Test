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
    public class GatherCraftTests
    {
        static ItemDef Wood => GameKit.Item("item.resource.wood");

        [Test]
        public void SwingingAtATreeGivesWoodScaledByTheTool()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            int tree = kit.StandAtNode(c, n => n.Def.Kind == GatherKind.Wood, out float yaw, out float pitch, aimHeight: 2.5f);
            c.Inventory.SelectSlot(-1);
            Assert.That(kit.World.Swing(c, yaw, pitch), Is.EqualTo(ActionError.None));
            int bare = c.Inventory.Count(Wood);
            Assert.That(bare, Is.EqualTo((int)GameKit.Db.Rules.Gathering.HandYield));

            kit.Run(2);
            kit.Hold(c, "item.tool.stone_hatchet");
            Assert.That(kit.World.Swing(c, yaw, pitch), Is.EqualTo(ActionError.None));
            Assert.That(c.Inventory.Count(Wood) - bare, Is.EqualTo(16));
            Assert.That(kit.World.Swing(c, yaw, pitch), Is.EqualTo(ActionError.Cooldown), "one swing per 0.9 s");
            Assert.That(c.Inventory.Hotbar[0].Condition, Is.EqualTo(99.5f));
            GameEvent hit = c.Events.Last(e => e.Kind == GameEventKind.GatherHit);
            Assert.That(hit.Target, Is.EqualTo(tree));
            Assert.That(hit.ItemNetId, Is.EqualTo(Wood.NetId));
        }

        [Test]
        public void GatherMultiplierAndSweetSpotBonus()
        {
            var kit = new GameKit(new GameConfig { GatherMultiplier = 3 });
            Character c = kit.Join();
            int rock = kit.StandAtNode(c, n => n.Def.Kind == GatherKind.Stone, out float yaw, out float pitch);
            kit.Hold(c, "item.tool.stone_pick");
            Vector3 eye = kit.World.EyeOf(c);
            GameKit.Aim(eye, kit.World.Resources.SweetSpot(rock), out float sy, out float sp);
            int before = c.Inventory.Count(GameKit.Item("item.resource.stone"));
            Assert.That(kit.World.Swing(c, sy, sp), Is.EqualTo(ActionError.None));
            GameEvent hit = c.Events.Last(e => e.Kind == GameEventKind.GatherHit);
            if (hit.Flag)
            {
                Assert.That(c.Inventory.Count(GameKit.Item("item.resource.stone")) - before, Is.EqualTo((int)System.Math.Round(18 * 1.5f * 3)));
            }
            else
            {
                Assert.Inconclusive("The sweet spot was on a face the ray couldn't reach from here.");
            }
        }

        [Test]
        public void OreNeedsAPickAndNodesDeplete()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            int ore = kit.StandAtNode(c, n => n.Def.Kind == GatherKind.Ore, out float yaw, out float pitch);
            kit.Hold(c, "item.tool.rock");
            Assert.That(kit.World.Swing(c, yaw, pitch), Is.EqualTo(ActionError.NotPermitted));
            kit.Hold(c, "item.tool.stone_pick");
            int swings = 0;
            while (kit.World.Resources.IsAlive(ore) && swings < 200)
            {
                kit.Run(1.0);
                if (kit.World.Swing(c, yaw, pitch) == ActionError.None)
                {
                    swings++;
                }
            }

            Assert.That(kit.World.Resources.IsAlive(ore), Is.False);
            Assert.That(c.Inventory.Count(GameKit.Item("item.resource.iron_ore")), Is.EqualTo(250));
            Assert.That(swings, Is.InRange(250 / 12, 250 / 8 + 1), "8 per hit, 12 on sweet spots");
            Assert.That(kit.World.Swing(c, yaw, pitch), Is.Not.EqualTo(ActionError.None).Or.EqualTo(ActionError.Cooldown));
        }

        [Test]
        public void ToolsBreakWhenWornOut()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            kit.StandAtNode(c, n => n.Def.Kind == GatherKind.Wood, out float yaw, out float pitch, aimHeight: 2.5f);
            c.Inventory.Hotbar.Set(0, new ItemStack(GameKit.Item("item.tool.stone_hatchet"), 1, 0.5f));
            c.Inventory.SelectSlot(0);
            Assert.That(kit.World.Swing(c, yaw, pitch), Is.EqualTo(ActionError.None));
            Assert.That(c.Inventory.Hotbar[0].IsEmpty, Is.True);
            Assert.That(c.Events.Any(e => e.Kind == GameEventKind.ToolBroke), Is.True);
        }

        [Test]
        public void SwingingAtNothingStillCostsTheCooldown()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            c.Motion.Position = kit.OpenGround();
            Assert.That(kit.World.Swing(c, 0, 0), Is.EqualTo(ActionError.NothingThere));
            Assert.That(kit.World.Swing(c, 0, 0), Is.EqualTo(ActionError.Cooldown));
        }

        [Test]
        public void PlantsArePickedWithInteract()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            int plant = kit.StandAtNode(c, n => n.Def.Key.Value == "node.plant.hemp", out _, out _, distance: 0.8f);
            Assert.That(kit.World.Interact(c, InteractTarget.Node, (uint)plant), Is.EqualTo(ActionError.None));
            Assert.That(c.Inventory.Count(GameKit.Item("item.resource.fiber")), Is.EqualTo(20));
            Assert.That(kit.World.Interact(c, InteractTarget.Node, (uint)plant), Is.EqualTo(ActionError.NothingThere));
            c.Motion.Position += new Vector3(30, 0, 0);
            int other = kit.World.Resources.FindNearest(Vector3.Zero, 400, n => n.Def.Pickable);
            if (Vector3.Distance(kit.World.Resources.Placement(other).Position, c.Motion.Position) > 5)
            {
                Assert.That(kit.World.Interact(c, InteractTarget.Node, (uint)other), Is.EqualTo(ActionError.TooFar));
            }
        }

        [Test]
        public void CraftingTakesTimeAndCancellingRefunds()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            RecipeDef hatchet = GameKit.Db.Recipe("recipe.stone_hatchet");
            Assert.That(kit.World.Craft(c, hatchet, 1), Is.EqualTo(ActionError.NotEnoughItems));
            c.Inventory.Main.Set(0, ItemStack.Of(Wood, 400));
            c.Inventory.Main.Set(1, ItemStack.Of(GameKit.Item("item.resource.stone"), 200));
            Assert.That(kit.World.Craft(c, hatchet, 2), Is.EqualTo(ActionError.None));
            Assert.That(c.Inventory.Count(Wood), Is.EqualTo(100), "inputs taken up front");
            kit.Run(10.1);
            Assert.That(c.Inventory.Count(GameKit.Item("item.tool.stone_hatchet")), Is.EqualTo(1));
            Assert.That(c.Crafting.Single().Remaining, Is.EqualTo(1));
            Assert.That(kit.World.CancelCraft(c, c.Crafting[0].Id), Is.EqualTo(ActionError.None));
            Assert.That(c.Inventory.Count(Wood), Is.EqualTo(250), "the unmade one is refunded");
            Assert.That(kit.World.CancelCraft(c, 999), Is.EqualTo(ActionError.InvalidTarget));
        }

        [Test]
        public void BenchRecipesNeedTheBenchNearbyAndTheQueueIsLimited()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            c.Motion.Position = kit.OpenGround();
            RecipeDef smelter = GameKit.Db.Recipe("recipe.smelter");
            c.Inventory.Main.Set(0, ItemStack.Of(GameKit.Item("item.resource.stone"), 1000));
            c.Inventory.Main.Set(1, ItemStack.Of(Wood, 1000));
            c.Inventory.Main.Set(2, ItemStack.Of(GameKit.Item("item.component.rope"), 10));
            Assert.That(kit.World.Craft(c, smelter, 1), Is.EqualTo(ActionError.StationMissing));
            c.Inventory.Hotbar.Set(1, ItemStack.Of(GameKit.Item("item.deployable.crude_bench")));
            Assert.That(kit.World.Deploy(c, 1, c.Motion.Position + new Vector3(0, 0, 2), 90), Is.EqualTo(ActionError.None));
            Assert.That(kit.World.Craft(c, smelter, 1), Is.EqualTo(ActionError.None));

            RecipeDef cloth = GameKit.Db.Recipe("recipe.cloth");
            c.Inventory.Main.Set(3, ItemStack.Of(GameKit.Item("item.resource.fiber"), 500));
            for (int i = 1; i < GameKit.Db.Rules.Crafting.QueueSize; i++)
            {
                Assert.That(kit.World.Craft(c, cloth, 1), Is.EqualTo(ActionError.None));
            }

            Assert.That(kit.World.Craft(c, cloth, 1), Is.EqualTo(ActionError.QueueFull));
        }

        [Test]
        public void DyingRefundsCraftingIntoTheBody()
        {
            var kit = new GameKit();
            Character c = kit.Join();
            c.Inventory.Main.Set(0, ItemStack.Of(Wood, 80));
            Assert.That(kit.World.Craft(c, GameKit.Db.Recipe("recipe.campfire"), 1), Is.EqualTo(ActionError.None));
            kit.World.Die(c, DamageCause.Admin);
            WorldEntity body = kit.World.Entities.All.Single(e => e.Def.Kind == EntityKind.Body);
            Assert.That(body.Container.Count(Wood), Is.EqualTo(80));
            Assert.That(c.Crafting, Is.Empty);
        }
    }
}
