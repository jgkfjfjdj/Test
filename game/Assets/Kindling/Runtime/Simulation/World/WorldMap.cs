using System;
using System.Collections.Generic;
using System.Numerics;
using Kindling.Content;
using Kindling.Core.Numerics;
using Kindling.Simulation.Collision;

namespace Kindling.Simulation.World
{
    public struct LootSpawn
    {
        public EntityDef Def;
        public Vector3 Position;
        public float Yaw;
    }

    /// <summary>
    /// Server-side map: the static map plus spawn points and loot spawn positions, generated from the
    /// greybox layout seed and the content's map counts. Deterministic for a seed (the server is the only
    /// one that generates it).
    /// </summary>
    public sealed class WorldMap
    {
        const float PlazaRadius = 35f;
        const float EdgeMargin = 14f;

        WorldMap(GreyboxLayout layout, StaticMap map, List<Vector3> spawns, List<LootSpawn> loot)
        {
            Layout = layout;
            Static = map;
            SpawnPoints = spawns;
            LootSpawns = loot;
        }

        public GreyboxLayout Layout { get; }

        public StaticMap Static { get; }

        /// <summary>Random respawn points near the map edge ("coast").</summary>
        public IReadOnlyList<Vector3> SpawnPoints { get; }

        public IReadOnlyList<LootSpawn> LootSpawns { get; }

        public static WorldMap Generate(int seed, ContentDatabase content)
        {
            GreyboxLayout layout = GreyboxLayout.Generate(seed);
            var rng = new DeterministicRandom((ulong)seed * 0x9E3779B97F4A7C15UL + 17UL);
            var pieces = new List<GreyboxPiece>(layout.Pieces);
            var occupied = new List<Aabb>();
            foreach (GreyboxPiece p in pieces)
            {
                occupied.Add(p.Box);
            }

            // Ponds first (largest), then nodes, then loot spawns.
            var ponds = new List<Aabb>();
            for (int i = 0, attempts = 0; i < 6 && attempts < 500; attempts++)
            {
                float w = rng.Range(8, 17);
                float d = rng.Range(8, 17);
                Vector3 c = RandomPoint(rng, EdgeMargin + 10f);
                var pond = new Aabb(new Vector3(c.X - w / 2, 0f, c.Z - d / 2), new Vector3(c.X + w / 2, 0.05f, c.Z + d / 2));
                if (InPlaza(c, PlazaRadius + 10f) || Overlaps(occupied, pond.Inflate(2f)))
                {
                    continue;
                }

                ponds.Add(pond);
                occupied.Add(new Aabb(pond.Min, pond.Max + new Vector3(0f, 3f, 0f)));
                i++;
            }

            var nodes = new List<NodePlacement>();
            foreach (NodeDef def in content.Nodes)
            {
                int placed = 0;
                for (int attempts = 0; placed < def.MapCount && attempts < def.MapCount * 20; attempts++)
                {
                    var placement = new NodePlacement { Def = def, Position = RandomPoint(rng, EdgeMargin), Yaw = rng.Range(0, 360) };
                    if (InPlaza(placement.Position, PlazaRadius))
                    {
                        continue;
                    }

                    Aabb bounds = placement.Bounds;
                    if (Overlaps(occupied, bounds.Inflate(def.Pickable ? 0.3f : 1.2f)))
                    {
                        continue;
                    }

                    nodes.Add(placement);
                    occupied.Add(bounds);
                    placed++;
                }
            }

            var loot = new List<LootSpawn>();
            foreach (EntityDef def in content.Entities)
            {
                if (def.MapCount <= 0 || (def.Kind != EntityKind.LootContainer && def.Kind != EntityKind.Breakable))
                {
                    continue;
                }

                int placed = 0;
                for (int attempts = 0; placed < def.MapCount && attempts < def.MapCount * 30; attempts++)
                {
                    Vector3 p = RandomPoint(rng, EdgeMargin);
                    var bounds = new Aabb(p - new Vector3(def.Size.X / 2, 0f, def.Size.Z / 2), p + new Vector3(def.Size.X / 2, def.Size.Y, def.Size.Z / 2));
                    if (InPlaza(p, 15f) || Overlaps(occupied, bounds.Inflate(1f)))
                    {
                        continue;
                    }

                    loot.Add(new LootSpawn { Def = def, Position = p, Yaw = rng.Range(0, 4) * 90f });
                    occupied.Add(bounds);
                    placed++;
                }
            }

            var map = new StaticMap(seed, GreyboxLayout.MapSize, pieces, nodes, ponds);
            CollisionWorld world = map.BuildCollisionWorld();
            var scratch = new List<int>();
            var spawns = new List<Vector3>();
            float edge = GreyboxLayout.MapSize / 2f - 10f;
            for (int i = 0; i < 64 && spawns.Count < 32; i++)
            {
                float t = i / 64f * 4f;
                int side = (int)t;
                float along = (t - side) * 2f - 1f;
                Vector3 p = side switch
                {
                    0 => new Vector3(along * edge, 0.01f, edge),
                    1 => new Vector3(edge, 0.01f, -along * edge),
                    2 => new Vector3(-along * edge, 0.01f, -edge),
                    _ => new Vector3(-edge, 0.01f, along * edge),
                };
                if (!world.Overlaps(Aabb.FromFeet(p, 0.4f, 1.9f), scratch) && !map.IsInPond(p, 1f))
                {
                    spawns.Add(p);
                }
            }

            return new WorldMap(layout, map, spawns, loot);
        }

        static Vector3 RandomPoint(DeterministicRandom rng, float margin)
        {
            float half = GreyboxLayout.MapSize / 2f - margin;
            return new Vector3(rng.Range(-half, half), 0f, rng.Range(-half, half));
        }

        static bool InPlaza(Vector3 p, float radius) => p.X * p.X + p.Z * p.Z < radius * radius;

        static bool Overlaps(List<Aabb> boxes, Aabb box)
        {
            foreach (Aabb b in boxes)
            {
                if (b.Intersects(box))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
