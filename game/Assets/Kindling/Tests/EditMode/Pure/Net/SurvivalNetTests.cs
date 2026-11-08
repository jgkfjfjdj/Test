using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Kindling.Content;
using Kindling.Core.Serialization;
using Kindling.Core.World;
using Kindling.Net.Client;
using Kindling.Net.Protocol;
using Kindling.Net.Server;
using Kindling.Net.Transport;
using Kindling.Simulation;
using Kindling.Simulation.Game;
using Kindling.Simulation.Items;
using Kindling.Simulation.Movement;
using Kindling.Simulation.Persistence;
using Kindling.Simulation.World;
using NUnit.Framework;

namespace Kindling.Tests.Pure.Net
{
    /// <summary>The survival loop end to end: real client sessions against the server on a simulated network.</summary>
    public class SurvivalNetTests
    {
        static ContentDatabase Db => ContentDatabase.LoadDefault();
        static ItemDef Item(string key) => Db.Item(key);

        static ClientSession Join(NetHarness h, string name, NetworkConditions conditions = null)
        {
            ClientSession c = h.AddClient(name, conditions ?? NetworkConditions.Of(30, 10, 0.01));
            h.RunUntil(() => c.Status == ClientStatus.Connected && c.WorldState.HasOwnerState, 5);
            Assert.That(c.Status, Is.EqualTo(ClientStatus.Connected));
            return c;
        }

        static Character CharacterOf(NetHarness h, ClientSession c) => h.Server.FindPlayer(c.PlayerId).Character;

        /// <summary>Teleports the player next to a node and returns the yaw/pitch that aims at it.</summary>
        static int StandAtNode(NetHarness h, ClientSession c, Func<NodePlacement, bool> filter, out float yaw, out float pitch)
        {
            GameWorld w = h.World;
            Character ch = CharacterOf(h, c);
            for (int attempt = 0; attempt < 40; attempt++)
            {
                int node = w.Resources.FindNearest(new Vector3(attempt * 37 % 400 - 200, 0, attempt * 53 % 400 - 200), 400f, filter);
                NodePlacement p = w.Resources.Placement(node);
                for (int dir = 0; dir < 8; dir++)
                {
                    double a = dir * Math.PI / 4;
                    float offset = 1.4f + Math.Max(p.Def.Size.X, p.Def.Size.Z) * 0.5f;
                    var feet = new Vector3(p.Position.X + (float)Math.Sin(a) * offset, 0.01f, p.Position.Z + (float)Math.Cos(a) * offset);
                    if (w.Collision.Overlaps(Kindling.Simulation.Collision.Aabb.FromFeet(feet, 0.35f, 1.9f), new List<int>()))
                    {
                        continue;
                    }

                    ch.Motion.Position = feet;
                    Vector3 eye = w.EyeOf(ch);
                    Vector3 target = p.Position + new Vector3(0f, Math.Min(1.2f, p.Def.Size.Y * 0.5f), 0f);
                    Vector3 d = target - eye;
                    yaw = (float)(Math.Atan2(d.X, d.Z) * 180.0 / Math.PI);
                    pitch = (float)(Math.Atan2(d.Y, Math.Sqrt(d.X * d.X + d.Z * d.Z)) * 180.0 / Math.PI);
                    if (w.Resources.RayCast(eye, GameWorld.ViewDirection(yaw, pitch), 2.4f, out int hit, out _) && hit == node)
                    {
                        h.Server.TeleportPlayer(c.PlayerId, feet);
                        return node;
                    }
                }
            }

            Assert.Fail("No reachable node");
            yaw = pitch = 0;
            return -1;
        }

        static ActionError Await(NetHarness h, ClientSession c, ushort request)
        {
            Assert.That(request, Is.Not.Zero, "sent");
            ActionError result = ActionError.None;
            bool done = false;
            h.RunUntil(() => done = c.TryGetResult(request, out result), 2);
            Assert.That(done, Is.True, "answered");
            return result;
        }

        [Test]
        public void JoiningDeliversTheMapInventoryOwnStateAndNearbyChunks()
        {
            var h = new NetHarness();
            ClientSession c = Join(h, "ana");
            h.Run(1);
            Assert.That(c.Map.Nodes.Count, Is.EqualTo(h.World.Map.Static.Nodes.Count));
            Assert.That(c.Map.Nodes[100].Position, Is.EqualTo(h.World.Map.Static.Nodes[100].Position), "exact floats");
            Assert.That(c.WorldState.Inventory.Count(Item("item.tool.rock")), Is.EqualTo(1));
            Assert.That(c.WorldState.Owner.Life, Is.EqualTo(LifeState.Alive));
            Assert.That(c.WorldState.Owner.Health, Is.EqualTo(Db.Rules.Death.RespawnHealth).Within(1f));
            Assert.That(c.WorldState.LoadedChunkCount, Is.GreaterThan(8));
            Assert.That(c.WorldState.WorldSeconds, Is.EqualTo(h.World.Now).Within(1.5));
            ChunkCoord here = h.World.Entities.ChunkOf(c.PredictedState.Position);
            foreach (int node in h.World.Resources.NodesInChunk(here))
            {
                Assert.That(c.WorldState.Nodes.IsAlive(node), Is.EqualTo(h.World.Resources.IsAlive(node)));
            }
        }

        [Test]
        public void SwingingAtATreeFillsTheClientsInventory()
        {
            var h = new NetHarness();
            ClientSession c = Join(h, "lumberjack");
            int tree = StandAtNode(h, c, n => n.Def.Kind == GatherKind.Wood, out float yaw, out float pitch);
            h.Run(0.5);
            Assert.That(Await(h, c, c.SelectHotbar(0)), Is.EqualTo(ActionError.None), "the rock");
            h.Run(5, () => c.SetInput(Vector2.Zero, yaw, pitch, InputButtons.Attack));
            c.SetInput(Vector2.Zero, yaw, pitch, InputButtons.None);
            h.Run(0.5);
            int wood = c.WorldState.Inventory.Count(Item("item.resource.wood"));
            Assert.That(wood, Is.GreaterThanOrEqualTo(6 * 4), "4+ swings with the rock");
            Assert.That(wood, Is.EqualTo(CharacterOf(h, c).Inventory.Count(Item("item.resource.wood"))));
            var events = new List<GameEvent>();
            c.WorldState.DrainEvents(events);
            Assert.That(events.Count(e => e.Kind == GameEventKind.GatherHit && e.Target == tree), Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void CraftingQueueAndResultReachTheClient()
        {
            var h = new NetHarness();
            ClientSession c = Join(h, "crafter");
            Character ch = CharacterOf(h, c);
            ch.Inventory.Main.Set(0, ItemStack.Of(Item("item.resource.wood"), 300));
            ch.Inventory.Main.Set(1, ItemStack.Of(Item("item.resource.stone"), 200));
            h.Run(0.3);
            Assert.That(Await(h, c, c.Craft(Db.Recipe("recipe.stone_hatchet"))), Is.EqualTo(ActionError.None));
            h.Run(0.5);
            Assert.That(c.WorldState.Owner.Crafting.Single().RecipeId, Is.EqualTo(Db.Recipe("recipe.stone_hatchet").NetId));
            Assert.That(c.WorldState.Inventory.Count(Item("item.resource.wood")), Is.EqualTo(150));
            h.Run(10);
            Assert.That(c.WorldState.Inventory.Count(Item("item.tool.stone_hatchet")), Is.EqualTo(1));
            Assert.That(c.WorldState.Owner.Crafting, Is.Empty);
            Assert.That(Await(h, c, c.Craft(Db.Recipe("recipe.smelter"))), Is.EqualTo(ActionError.StationMissing));
        }

        [Test]
        public void CampfireCookingThroughContainersAndOthersSeeIt()
        {
            var h = new NetHarness();
            ClientSession c = Join(h, "cook");
            ClientSession other = Join(h, "neighbor");
            Vector3 spot = h.FreeSpotNear(new Vector3(-90, 0, 80));
            h.Server.TeleportPlayer(c.PlayerId, spot);
            h.Server.TeleportPlayer(other.PlayerId, spot + new Vector3(5, 0, 0));
            Character ch = CharacterOf(h, c);
            ch.Inventory.Hotbar.Set(3, ItemStack.Of(Item("item.deployable.campfire")));
            ch.Inventory.Main.Set(0, ItemStack.Of(Item("item.resource.wood"), 20));
            ch.Inventory.Main.Set(1, ItemStack.Of(Item("item.food.mushroom_raw"), 2));
            ch.Stats.Hunger = 40;
            h.Run(1);

            Assert.That(Await(h, c, c.Deploy(3, spot + new Vector3(0, 0, 2f), 0)), Is.EqualTo(ActionError.None));
            h.Run(0.5);
            ChunkEntity fire = c.WorldState.Entities.Values.Single(e => e.TypeId == Db.Entity("entity.campfire").NetId);
            Assert.That(other.WorldState.Entities.ContainsKey(fire.Id), Is.True, "the neighbor sees it");

            var box = ContainerRef.OfEntity(fire.Id);
            Assert.That(Await(h, c, c.MoveItem(ContainerRef.Main, 0, box, 0, 20)), Is.EqualTo(ActionError.NoAccess), "not opened yet");
            Assert.That(Await(h, c, c.Interact(InteractTarget.Entity, fire.Id)), Is.EqualTo(ActionError.None));
            h.Run(0.3);
            Assert.That(c.WorldState.OpenContainerId, Is.EqualTo(fire.Id));
            Assert.That(Await(h, c, c.MoveItem(ContainerRef.Main, 0, box, 0, 20)), Is.EqualTo(ActionError.None));
            Assert.That(Await(h, c, c.QuickMove(ContainerRef.Main, 1, box)), Is.EqualTo(ActionError.None));
            Assert.That(Await(h, c, c.ToggleProcessor(fire.Id)), Is.EqualTo(ActionError.None));
            h.Run(18);
            Assert.That(c.WorldState.OpenContainer.Count(Item("item.food.mushroom_cooked")), Is.EqualTo(2));
            Assert.That(other.WorldState.Entities[fire.Id].Flags & WorldEntityFlags.Lit, Is.EqualTo(WorldEntityFlags.Lit));

            int slot = Enumerable.Range(0, c.WorldState.OpenContainer.Capacity).First(i => c.WorldState.OpenContainer[i].Def == Item("item.food.mushroom_cooked"));
            Assert.That(Await(h, c, c.QuickMove(box, slot, ContainerRef.Main)), Is.EqualTo(ActionError.None));
            h.Run(0.3);
            int mainSlot = Enumerable.Range(0, 24).First(i => c.WorldState.Inventory.Main[i].Def == Item("item.food.mushroom_cooked"));
            Assert.That(Await(h, c, c.Use(ContainerRef.Main, mainSlot)), Is.EqualTo(ActionError.None));
            h.Run(0.5);
            Assert.That(c.WorldState.Owner.Hunger, Is.GreaterThan(50f), "ate a cooked mushroom (+14)");
        }

        [Test]
        public void DeathLeavesABodyAndYouRespawnAtYourBedroll()
        {
            var h = new NetHarness();
            ClientSession c = Join(h, "unlucky");
            ClientSession other = Join(h, "witness");
            Vector3 spot = h.FreeSpotNear(new Vector3(100, 0, 60));
            h.Server.TeleportPlayer(c.PlayerId, spot);
            h.Server.TeleportPlayer(other.PlayerId, spot + new Vector3(6, 0, 0));
            Character ch = CharacterOf(h, c);
            ch.Inventory.Hotbar.Set(2, ItemStack.Of(Item("item.deployable.bedroll")));
            h.Run(1);
            Assert.That(Await(h, c, c.Deploy(2, spot + new Vector3(2.5f, 0, 0), 0)), Is.EqualTo(ActionError.None));
            h.Run(0.3);
            uint bed = c.WorldState.Entities.Values.Single(e => e.TypeId == Db.Entity("entity.bedroll").NetId).Id;

            h.Server.TeleportPlayer(c.PlayerId, spot + new Vector3(-60, 0, 0));
            h.Run(1);
            h.World.Damage(ch, 1000, DamageCause.Admin);
            h.Run(0.5);
            Assert.That(c.WorldState.Owner.Life, Is.EqualTo(LifeState.Downed));
            Assert.That(Await(h, c, c.GiveUp()), Is.EqualTo(ActionError.None));
            h.Run(0.5);
            Assert.That(c.WorldState.Owner.Life, Is.EqualTo(LifeState.Dead));
            Assert.That(c.WorldState.Owner.RespawnPoints.Select(p => p.EntityId), Is.EqualTo(new[] { bed }));
            var events = new List<GameEvent>();
            c.WorldState.DrainEvents(events);
            uint bodyId = (uint)events.Single(e => e.Kind == GameEventKind.Died).Target;
            Assert.That(other.WorldState.Entities[bodyId].Label, Is.EqualTo("unlucky"), "the witness sees the body");

            Assert.That(Await(h, c, c.Respawn(bed)), Is.EqualTo(ActionError.None));
            h.Run(1);
            Assert.That(c.WorldState.Owner.Life, Is.EqualTo(LifeState.Alive));
            Assert.That(Vector3.Distance(c.PredictedState.Position, h.World.Entities.Get(bed).Position), Is.LessThan(0.6f));
            Assert.That(c.WorldState.Inventory.Count(Item("item.tool.rock")), Is.EqualTo(1), "starter kit");
        }

        [Test]
        public void ChunksLoadAndUnloadAsPlayersMove()
        {
            var h = new NetHarness(new ServerConfig { ChunkSubscribeRadius = 1, ChunkUnsubscribeRadius = 2 });
            ClientSession c = Join(h, "traveller");
            h.Server.TeleportPlayer(c.PlayerId, h.FreeSpotNear(new Vector3(-200, 0, -200)));
            h.Run(1);
            ChunkCoord far = h.World.Entities.ChunkOf(new Vector3(200, 0, 200));
            Assert.That(c.WorldState.IsLoaded(far), Is.False);
            Assert.That(c.WorldState.LoadedChunkCount, Is.LessThanOrEqualTo(25));

            h.Server.TeleportPlayer(c.PlayerId, h.FreeSpotNear(new Vector3(200, 0, 200)));
            h.Run(1);
            Assert.That(c.WorldState.IsLoaded(far), Is.True);
            Assert.That(c.WorldState.IsLoaded(h.World.Entities.ChunkOf(new Vector3(-200, 0, -200))), Is.False, "unloaded");
            Assert.That(c.WorldState.Entities.Values.All(e => h.World.Entities.Get(e.Id) != null), Is.True);
        }

        [Test]
        public void ADifferentContentVersionIsRejected()
        {
            var h = new NetHarness();
            InMemoryClientTransport t = h.Network.CreateClient();
            var events = new Recorder();
            t.Connect("sim", NetHarness.Port);
            h.RunUntil(() => { t.Poll(events); return t.IsConnected; }, 2);
            var w = new PacketWriter();
            new HelloMessage { ProtocolVersion = NetProtocol.Version, DisplayName = "old", AuthToken = "x", ContentFingerprint = "0000000000000000" }.Write(w);
            t.Send(w.Buffer, 0, w.Length, Delivery.ReliableOrdered);
            h.Run(0.5);
            t.Poll(events);
            Assert.That(events.Code, Is.EqualTo(DisconnectCode.ContentMismatch));
        }

        sealed class Recorder : ITransportEvents
        {
            public DisconnectCode Code = DisconnectCode.None;

            public void OnConnected(int connectionId)
            {
            }

            public void OnDisconnected(int connectionId, DisconnectCode code, string message) => Code = code;

            public void OnData(int connectionId, byte[] data, int offset, int count)
            {
            }
        }

        [Test]
        public void TheWholeLoopSurvivesAServerRestart()
        {
            string dir = Path.Combine(Path.GetTempPath(), "kindling-e2e-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string savePath = Path.Combine(dir, "world.kws");
                var config = new ServerConfig { SaveIntervalSeconds = 5 };
                var warnings = new List<string>();
                var store = new FileWorldStore(savePath);
                GameWorld world = WorldPersistence.LoadOrCreate(Db, WorldMap.Generate(config.MapSeed, Db), GameConfig.FromRules(Db.Rules), 9, store, warnings, out _);
                var h = new NetHarness(config, world: world, persistence: new WorldPersistence(world, store));
                ClientSession c = Join(h, "survivor");
                Character ch = CharacterOf(h, c);

                // Gather wood with the rock.
                int tree = StandAtNode(h, c, n => n.Def.Kind == GatherKind.Wood, out float yaw, out float pitch);
                Await(h, c, c.SelectHotbar(0));
                h.Run(30, () => c.SetInput(Vector2.Zero, yaw, pitch, InputButtons.Attack));
                c.SetInput(Vector2.Zero, yaw, pitch, InputButtons.None);
                h.Run(0.5);
                int wood = c.WorldState.Inventory.Count(Item("item.resource.wood"));
                Assert.That(wood, Is.GreaterThan(100), "about 27 swings × 6");

                // Craft a campfire and a bedroll (cloth comes from fiber the test hands over).
                ch.Inventory.Main.Add(Item("item.resource.fiber"), 200);
                ch.Inventory.Main.Add(Item("item.food.mushroom_raw"), 2);
                h.Run(0.3);
                Assert.That(Await(h, c, c.Craft(Db.Recipe("recipe.campfire"))), Is.EqualTo(ActionError.None));
                Assert.That(Await(h, c, c.Craft(Db.Recipe("recipe.cloth"), 20)), Is.EqualTo(ActionError.None));
                h.Run(5 + 40 + 1);
                Assert.That(Await(h, c, c.Craft(Db.Recipe("recipe.bedroll"))), Is.EqualTo(ActionError.None));
                h.Run(9);
                Assert.That(c.WorldState.Inventory.Count(Item("item.deployable.campfire")), Is.EqualTo(1));
                Assert.That(c.WorldState.Inventory.Count(Item("item.deployable.bedroll")), Is.EqualTo(1));

                // Set up camp, cook, eat.
                Vector3 camp = h.FreeSpotNear(new Vector3(-100, 0, -100));
                h.Server.TeleportPlayer(c.PlayerId, camp);
                h.Run(1);
                int fireSlot = Enumerable.Range(0, 6).First(i => c.WorldState.Inventory.Hotbar[i].Def == Item("item.deployable.campfire"));
                int bedSlot = Enumerable.Range(0, 6).First(i => c.WorldState.Inventory.Hotbar[i].Def == Item("item.deployable.bedroll"));
                Assert.That(Await(h, c, c.Deploy(fireSlot, camp + new Vector3(0, 0, 2f), 0)), Is.EqualTo(ActionError.None));
                Assert.That(Await(h, c, c.Deploy(bedSlot, camp + new Vector3(2.5f, 0, 0), 90)), Is.EqualTo(ActionError.None));
                h.Run(0.5);
                uint fire = c.WorldState.Entities.Values.Single(e => e.TypeId == Db.Entity("entity.campfire").NetId).Id;
                uint bed = c.WorldState.Entities.Values.Single(e => e.TypeId == Db.Entity("entity.bedroll").NetId).Id;
                Await(h, c, c.Interact(InteractTarget.Entity, fire));
                int woodSlot = Enumerable.Range(0, 24).First(i => c.WorldState.Inventory.Main[i].Def == Item("item.resource.wood"));
                int shroomSlot = Enumerable.Range(0, 24).First(i => c.WorldState.Inventory.Main[i].Def == Item("item.food.mushroom_raw"));
                Assert.That(Await(h, c, c.MoveItem(ContainerRef.Main, woodSlot, ContainerRef.OfEntity(fire), 0, 10)), Is.EqualTo(ActionError.None));
                Assert.That(Await(h, c, c.QuickMove(ContainerRef.Main, shroomSlot, ContainerRef.OfEntity(fire))), Is.EqualTo(ActionError.None));
                Assert.That(Await(h, c, c.ToggleProcessor(fire)), Is.EqualTo(ActionError.None));
                h.Run(17);
                int cooked = Enumerable.Range(0, 6).First(i => c.WorldState.OpenContainer[i].Def == Item("item.food.mushroom_cooked"));
                Assert.That(Await(h, c, c.QuickMove(ContainerRef.OfEntity(fire), cooked, ContainerRef.Main)), Is.EqualTo(ActionError.None));
                h.Run(0.3);
                float hunger = c.WorldState.Owner.Hunger;
                int mainCooked = Enumerable.Range(0, 24).First(i => c.WorldState.Inventory.Main[i].Def == Item("item.food.mushroom_cooked"));
                Assert.That(Await(h, c, c.Use(ContainerRef.Main, mainCooked)), Is.EqualTo(ActionError.None));
                h.Run(0.5);
                Assert.That(c.WorldState.Owner.Hunger, Is.GreaterThan(hunger + 10));

                // Die far away, respawn at the bedroll.
                h.Server.TeleportPlayer(c.PlayerId, h.FreeSpotNear(new Vector3(150, 0, 150)));
                h.Run(0.5);
                h.World.Damage(ch, 1000, DamageCause.Admin);
                h.Run(31);
                Assert.That(c.WorldState.Owner.Life, Is.EqualTo(LifeState.Dead), "downed time ran out");
                Assert.That(Await(h, c, c.Respawn(bed)), Is.EqualTo(ActionError.None));
                h.Run(1);
                Assert.That(Vector3.Distance(c.PredictedState.Position, camp + new Vector3(2.5f, 0, 0)), Is.LessThan(1f));

                // Restart the server: the world comes back from the save, the player takes their character back.
                Vector3 before = ch.Motion.Position;
                var inventoryBefore = ch.Inventory.Main.Count(Item("item.food.mushroom_cooked")) + ch.Inventory.Count(Item("item.tool.rock"));
                double timeBefore = h.World.Now;
                int depleted = Enumerable.Range(0, h.World.Resources.Count).Count(i => !h.World.Resources.IsAlive(i));
                h.RestartServer(() =>
                {
                    var store2 = new FileWorldStore(savePath);
                    GameWorld loaded = WorldPersistence.LoadOrCreate(Db, WorldMap.Generate(config.MapSeed, Db), GameConfig.FromRules(Db.Rules), 9, store2, warnings, out bool wasLoaded);
                    Assert.That(wasLoaded, Is.True);
                    return (loaded, new WorldPersistence(loaded, store2));
                });
                Assert.That(c.Status, Is.EqualTo(ClientStatus.Disconnected));
                Assert.That(warnings, Is.Empty);
                Assert.That(h.World.Now, Is.EqualTo(timeBefore).Within(0.1));
                Assert.That(Enumerable.Range(0, h.World.Resources.Count).Count(i => !h.World.Resources.IsAlive(i)), Is.EqualTo(depleted));

                c.Connect("sim", NetHarness.Port);
                h.RunUntil(() => c.Status == ClientStatus.Connected && c.WorldState.HasOwnerState, 5);
                h.Run(1);
                Assert.That(c.LastJoinWasResume, Is.True);
                Assert.That(Vector3.Distance(c.PredictedState.Position, before), Is.LessThan(0.05f));
                Character restored = h.Server.FindPlayer(c.PlayerId).Character;
                Assert.That(restored.Inventory.Main.Count(Item("item.food.mushroom_cooked")) + restored.Inventory.Count(Item("item.tool.rock")), Is.EqualTo(inventoryBefore));
                Assert.That(c.WorldState.Entities.ContainsKey(bed), Is.True, "the bedroll is still there");
                Assert.That(c.WorldState.Entities.ContainsKey(fire), Is.True, "and the campfire");
                Assert.That(c.WorldState.Owner.Life, Is.EqualTo(LifeState.Alive));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
