using System;
using System.Numerics;
using Kindling.Content;
using Kindling.Simulation;
using Kindling.Simulation.Game;
using Kindling.Simulation.Items;
using Kindling.Simulation.World;
using NUnit.Framework;

namespace Kindling.Tests.Pure.Game
{
    /// <summary>A fresh survival world plus helpers to stand characters next to things and step time.</summary>
    public sealed class GameKit
    {
        public static ContentDatabase Db => ContentDatabase.LoadDefault();
        static readonly WorldMap s_Map = WorldMap.Generate(1, Db);

        public readonly GameWorld World;

        public GameKit(GameConfig config = null, bool populate = false)
        {
            World = new GameWorld(Db, s_Map, config ?? GameConfig.FromRules(Db.Rules), seed: 42);
            if (populate)
            {
                World.PopulateFreshWorld();
            }
        }

        public static ItemDef Item(string key) => Db.Item(key);

        public Character Join(string id = "acc-1", string name = "Tester")
        {
            Character c = World.Join(id, name);
            c.Events.Clear();
            return c;
        }

        /// <summary>Advances world time in 1/30 s ticks.</summary>
        public void Run(double seconds)
        {
            int ticks = (int)Math.Round(seconds * 30);
            for (int i = 0; i < ticks; i++)
            {
                World.Tick(1f / 30f);
            }
        }

        public void Hold(Character c, string itemKey, int amount = 1)
        {
            c.Inventory.Hotbar.Set(0, ItemStack.Of(Item(itemKey), amount));
            c.Inventory.SelectSlot(0);
        }

        /// <summary>Finds a node of a kind and stands the character within reach with a clear view of it. Returns the node index.</summary>
        public int StandAtNode(Character c, Func<NodePlacement, bool> filter, out float yaw, out float pitch, float distance = 1.6f, float aimHeight = 1.0f)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                int node = World.Resources.FindNearest(new Vector3(attempt * 37 % 400 - 200, 0, attempt * 53 % 400 - 200), 400f, filter);
                if (node < 0)
                {
                    continue;
                }

                NodePlacement p = World.Resources.Placement(node);
                for (int dir = 0; dir < 8; dir++)
                {
                    double a = dir * Math.PI / 4;
                    float offset = distance + Math.Max(p.Def.Size.X, p.Def.Size.Z) * 0.5f;
                    var feet = new Vector3(p.Position.X + (float)Math.Sin(a) * offset, 0f, p.Position.Z + (float)Math.Cos(a) * offset);
                    c.Motion.Position = feet;
                    Vector3 eye = World.EyeOf(c);
                    Vector3 target = p.Position + new Vector3(0f, Math.Min(aimHeight, p.Def.Size.Y * 0.5f), 0f);
                    Aim(eye, target, out yaw, out pitch);
                    if (World.Resources.RayCast(eye, GameWorld.ViewDirection(yaw, pitch), 3f, out int hit, out _) && hit == node)
                    {
                        c.Motion.Yaw = yaw;
                        return node;
                    }
                }
            }

            Assert.Fail("No reachable node found");
            yaw = pitch = 0;
            return -1;
        }

        public static void Aim(Vector3 eye, Vector3 target, out float yaw, out float pitch)
        {
            Vector3 d = target - eye;
            yaw = (float)(Math.Atan2(d.X, d.Z) * 180.0 / Math.PI);
            pitch = (float)(Math.Atan2(d.Y, Math.Sqrt(d.X * d.X + d.Z * d.Z)) * 180.0 / Math.PI);
        }

        /// <summary>A clear spot on open ground, away from nodes and geometry.</summary>
        public Vector3 OpenGround()
        {
            for (int x = -100; x <= 100; x += 7)
            {
                for (int z = -100; z <= 100; z += 7)
                {
                    var p = new Vector3(x, 0f, z);
                    if (x * x + z * z < 40 * 40)
                    {
                        continue;
                    }

                    var probe = new Kindling.Simulation.Collision.Aabb(p - new Vector3(4, 0, 4), p + new Vector3(4, 3, 4));
                    if (!World.Collision.Overlaps(probe, new System.Collections.Generic.List<int>()) && !World.Map.Static.IsInPond(p, 5f)
                        && World.Resources.FindNearest(p, 5f, n => true) < 0)
                    {
                        return p;
                    }
                }
            }

            Assert.Fail("No open ground");
            return default;
        }
    }
}
