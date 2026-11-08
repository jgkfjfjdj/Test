using System.Collections.Generic;
using Kindling.Content;
using Kindling.Simulation;
using Kindling.Simulation.Items;
using NUnit.Framework;

namespace Kindling.Tests.Pure.Simulation
{
    public class InventoryTests
    {
        static ContentDatabase Db => ContentDatabase.LoadDefault();
        static ItemDef Wood => Db.Item("item.resource.wood");
        static ItemDef Stone => Db.Item("item.resource.stone");
        static ItemDef Hatchet => Db.Item("item.tool.stone_hatchet");
        static ItemDef Shirt => Db.Item("item.clothing.fiber_shirt");
        static ItemDef Hood => Db.Item("item.clothing.fiber_hood");

        [Test]
        public void AddFillsStacksThenEmptySlotsAndReportsOverflow()
        {
            var c = new ItemContainer(ContainerRole.Storage, 3);
            Assert.That(c.Add(Wood, 1500), Is.EqualTo(0));
            Assert.That(c[0].Amount, Is.EqualTo(1000));
            Assert.That(c[1].Amount, Is.EqualTo(500));
            Assert.That(c.Add(Wood, 600), Is.EqualTo(0), "tops up slot 1 first, then slot 2");
            Assert.That(c[1].Amount, Is.EqualTo(1000));
            Assert.That(c[2].Amount, Is.EqualTo(100));
            Assert.That(c.Add(Wood, 1000), Is.EqualTo(100), "100 didn't fit");
            Assert.That(c.Count(Wood), Is.EqualTo(3000));
        }

        [Test]
        public void ToolsKeepTheirConditionAndNeverStack()
        {
            var c = new ItemContainer(ContainerRole.Storage, 4);
            c.Add(Hatchet, 2, condition: 37f);
            Assert.That(c[0].Amount, Is.EqualTo(1));
            Assert.That(c[1].Amount, Is.EqualTo(1));
            Assert.That(c[0].Condition, Is.EqualTo(37f));
            c.Add(Hatchet, 1);
            Assert.That(c[2].Condition, Is.EqualTo(Hatchet.MaxCondition), "default is full condition");
        }

        [Test]
        public void RemoveTakesFromTheLastSlotsAndCountsAcrossStacks()
        {
            var c = new ItemContainer(ContainerRole.Storage, 3);
            c.Set(0, ItemStack.Of(Wood, 100));
            c.Set(2, ItemStack.Of(Wood, 50));
            Assert.That(c.Remove(Wood, 80), Is.EqualTo(80));
            Assert.That(c[2].IsEmpty, Is.True);
            Assert.That(c[0].Amount, Is.EqualTo(70));
            Assert.That(c.Remove(Wood, 500), Is.EqualTo(70));
        }

        [Test]
        public void MoveSplitsMergesAndSwaps()
        {
            var a = new ItemContainer(ContainerRole.Main, 4);
            var b = new ItemContainer(ContainerRole.Storage, 4);
            a.Set(0, ItemStack.Of(Wood, 300));
            a.Set(1, ItemStack.Of(Stone, 10));

            Assert.That(InventoryOps.Move(a, 0, b, 0, 100), Is.EqualTo(ActionError.None), "partial into empty = split");
            Assert.That(a[0].Amount, Is.EqualTo(200));
            Assert.That(b[0].Amount, Is.EqualTo(100));

            Assert.That(InventoryOps.Move(a, 0, b, 0, 200), Is.EqualTo(ActionError.None), "merge");
            Assert.That(a[0].IsEmpty, Is.True);
            Assert.That(b[0].Amount, Is.EqualTo(300));

            b.Set(1, ItemStack.Of(Wood, 990));
            Assert.That(InventoryOps.Move(b, 0, b, 1, 300), Is.EqualTo(ActionError.None), "merge up to the stack size");
            Assert.That(b[1].Amount, Is.EqualTo(1000));
            Assert.That(b[0].Amount, Is.EqualTo(290), "the rest stays");

            Assert.That(InventoryOps.Move(a, 1, b, 0, 10), Is.EqualTo(ActionError.None), "different item, whole stack = swap");
            Assert.That(a[1].Def, Is.SameAs(Wood));
            Assert.That(b[0].Def, Is.SameAs(Stone));

            Assert.That(InventoryOps.Move(a, 1, b, 0, 5), Is.EqualTo(ActionError.NotEnoughSpace), "partial onto a different item");
            Assert.That(InventoryOps.Move(a, 3, b, 0, 1), Is.EqualTo(ActionError.EmptySlot));
            Assert.That(InventoryOps.Move(a, 1, b, 9, 1), Is.EqualTo(ActionError.InvalidSlot));
            Assert.That(InventoryOps.Move(a, 1, b, 2, 0), Is.EqualTo(ActionError.InvalidAmount));
            Assert.That(InventoryOps.Move(a, 1, b, 2, 9999), Is.EqualTo(ActionError.InvalidAmount));
        }

        [Test]
        public void WearSlotsOnlyAcceptMatchingClothing()
        {
            var inv = new CharacterInventory();
            inv.Main.Set(0, ItemStack.Of(Shirt));
            inv.Main.Set(1, ItemStack.Of(Wood, 5));
            Assert.That(InventoryOps.Move(inv.Main, 0, inv.Wear, 0, 1), Is.EqualTo(ActionError.SlotRestricted), "slot 0 is the head");
            Assert.That(InventoryOps.Move(inv.Main, 0, inv.Wear, (int)EquipSlot.Chest - 1, 1), Is.EqualTo(ActionError.None));
            Assert.That(InventoryOps.Move(inv.Main, 1, inv.Wear, 2, 5), Is.EqualTo(ActionError.SlotRestricted));
            Assert.That(inv.Insulation, Is.EqualTo(Shirt.Insulation));

            // Swapping worn clothing out for something it can't go back into is refused.
            inv.Main.Set(2, ItemStack.Of(Hood));
            Assert.That(InventoryOps.Move(inv.Main, 1, inv.Wear, 1, 5), Is.EqualTo(ActionError.SlotRestricted));
        }

        [Test]
        public void CharacterAddPrefersHotbarForToolsAndMainForResources()
        {
            var inv = new CharacterInventory();
            Assert.That(inv.Add(Hatchet, 1), Is.EqualTo(0));
            Assert.That(inv.Hotbar[0].Def, Is.SameAs(Hatchet));
            Assert.That(inv.Add(Wood, 50), Is.EqualTo(0));
            Assert.That(inv.Main[0].Def, Is.SameAs(Wood));

            inv.Hotbar.Set(3, ItemStack.Of(Stone, 5));
            inv.Add(Stone, 10);
            Assert.That(inv.Hotbar[3].Amount, Is.EqualTo(15), "tops up an existing stack wherever it is");
            Assert.That(inv.Count(Stone), Is.EqualTo(15));

            inv.SelectSlot(0);
            Assert.That(inv.ActiveItem.Def, Is.SameAs(Hatchet));
            inv.SelectSlot(9);
            Assert.That(inv.ActiveSlot, Is.EqualTo(-1));
        }

        [Test]
        public void HasIngredientsAndRemoveAcrossContainers()
        {
            var inv = new CharacterInventory();
            inv.Main.Set(0, ItemStack.Of(Wood, 100));
            inv.Hotbar.Set(0, ItemStack.Of(Wood, 60));
            inv.Main.Set(1, ItemStack.Of(Stone, 80));
            RecipeDef recipe = Db.Recipe("recipe.stone_hatchet");
            Assert.That(inv.Has(recipe.Inputs), Is.True);
            Assert.That(inv.Has(recipe.Inputs, times: 2), Is.False);
            Assert.That(inv.Remove(Wood, 150), Is.EqualTo(150));
            Assert.That(inv.Count(Wood), Is.EqualTo(10));
        }

        [Test]
        public void QuickMoveSplitAndMoveAll()
        {
            var inv = new CharacterInventory();
            var chest = new ItemContainer(ContainerRole.Storage, 2);
            inv.Main.Set(0, ItemStack.Of(Wood, 2500 / 3));
            Assert.That(InventoryOps.Split(inv.Main, 0, 100), Is.EqualTo(ActionError.None));
            Assert.That(inv.Main[1].Amount, Is.EqualTo(100));
            Assert.That(InventoryOps.QuickMove(inv.Main, 1, chest), Is.EqualTo(ActionError.None));
            Assert.That(chest[0].Amount, Is.EqualTo(100));

            inv.Wear.Set(1, ItemStack.Of(Shirt));
            inv.Hotbar.Set(0, ItemStack.Of(Hatchet));
            var body = new ItemContainer(ContainerRole.Storage, 36);
            inv.MoveAllTo(body);
            Assert.That(body.Count(Wood) + body.Count(Shirt) + body.Count(Hatchet), Is.EqualTo(733 + 1 + 1));
            foreach (ItemContainer c in inv.All)
            {
                Assert.That(c.IsEmpty, Is.True);
            }
        }

        [Test]
        public void ChangesBumpTheVersionAndMarkSlotsDirty()
        {
            var c = new ItemContainer(ContainerRole.Storage, 4);
            uint v = c.Version;
            c.Add(Wood, 10);
            Assert.That(c.Version, Is.GreaterThan(v));
            var dirty = new List<int>();
            c.ConsumeDirty(dirty);
            Assert.That(dirty, Is.EqualTo(new[] { 0 }));
            c.ConsumeDirty(dirty);
            Assert.That(dirty, Is.Empty);
            v = c.Version;
            c.Set(0, ItemStack.Of(Wood, 10));
            Assert.That(c.Version, Is.EqualTo(v), "setting the same contents is not a change");
        }
    }
}
