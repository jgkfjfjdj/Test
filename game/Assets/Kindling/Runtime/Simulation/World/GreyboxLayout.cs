using System;
using System.Collections.Generic;
using System.Numerics;
using Kindling.Core.Numerics;
using Kindling.Simulation.Collision;

namespace Kindling.Simulation.World
{
    /// <summary>Kind of greybox piece, so renderers can colour them and tests can find them.</summary>
    public enum GreyboxKind : byte
    {
        Wall = 0,
        Crate = 1,
        Step = 2,
        Platform = 3,
        TunnelRoof = 4,
        TunnelWall = 5,
        Tower = 6,
    }

    public readonly struct GreyboxPiece
    {
        public readonly Aabb Box;
        public readonly GreyboxKind Kind;

        public GreyboxPiece(Aabb box, GreyboxKind kind)
        {
            Box = box;
            Kind = kind;
        }
    }

    /// <summary>
    /// The M1 test map (512 m × 512 m, centred on the origin), generated from a seed so client, server and
    /// bots build exactly the same collision world without shipping map files. Contains a clear spawn
    /// plaza, stairs and a platform (step-up), a wall too high to step over, a low tunnel (crouch only),
    /// a tower, and scattered crates.
    /// </summary>
    public sealed class GreyboxLayout
    {
        public const float MapSize = 512f;
        public const float WallHeight = 6f;
        public const float StepRise = 0.3f;
        public const float TunnelClearance = 1.4f;

        public int Seed { get; }
        public IReadOnlyList<GreyboxPiece> Pieces { get; }
        public IReadOnlyList<Vector3> SpawnPoints { get; }

        /// <summary>Key locations for tests and bots.</summary>
        public Vector3 StairsStart { get; }

        public Vector3 PlatformTop { get; }
        public Vector3 TunnelEntrance { get; }
        public Vector3 HighWallFront { get; }

        GreyboxLayout(int seed, List<GreyboxPiece> pieces, List<Vector3> spawns, Vector3 stairs, Vector3 platform, Vector3 tunnel, Vector3 wall)
        {
            Seed = seed;
            Pieces = pieces;
            SpawnPoints = spawns;
            StairsStart = stairs;
            PlatformTop = platform;
            TunnelEntrance = tunnel;
            HighWallFront = wall;
        }

        public CollisionWorld BuildCollisionWorld()
        {
            var boxes = new Aabb[Pieces.Count];
            for (int i = 0; i < boxes.Length; i++)
            {
                boxes[i] = Pieces[i].Box;
            }

            return new CollisionWorld(boxes);
        }

        public static GreyboxLayout Generate(int seed = 1)
        {
            var pieces = new List<GreyboxPiece>();
            void Add(Vector3 min, Vector3 max, GreyboxKind kind) => pieces.Add(new GreyboxPiece(new Aabb(min, max), kind));

            float half = MapSize * 0.5f;

            // Perimeter walls (2 m thick) so nobody walks off the map.
            Add(new Vector3(-half - 2, 0, -half - 2), new Vector3(half + 2, WallHeight, -half), GreyboxKind.Wall);
            Add(new Vector3(-half - 2, 0, half), new Vector3(half + 2, WallHeight, half + 2), GreyboxKind.Wall);
            Add(new Vector3(-half - 2, 0, -half), new Vector3(-half, WallHeight, half), GreyboxKind.Wall);
            Add(new Vector3(half, 0, -half), new Vector3(half + 2, WallHeight, half), GreyboxKind.Wall);

            // Stairs: 8 steps of 0.3 m rise and 0.5 m run, 3 m wide, along +Z from (12, 0, 10), then a platform.
            var stairs = new Vector3(13.5f, 0f, 8f);
            for (int i = 0; i < 8; i++)
            {
                float z0 = 10f + i * 0.5f;
                Add(new Vector3(12f, 0f, z0), new Vector3(15f, StepRise * (i + 1), z0 + 0.5f), GreyboxKind.Step);
            }

            Add(new Vector3(12f, 0f, 14f), new Vector3(18f, StepRise * 8, 20f), GreyboxKind.Platform);
            var platformTop = new Vector3(15f, StepRise * 8, 17f);

            // High wall (1.2 m) that can't be stepped over, west of spawn.
            Add(new Vector3(-16f, 0f, -4f), new Vector3(-15f, 1.2f, 4f), GreyboxKind.Wall);
            var wallFront = new Vector3(-13f, 0f, 0f);

            // Tunnel: 10 m long along +X at z ∈ [-14, -12], roof at 1.4 m — only crouching fits.
            Add(new Vector3(20f, 0f, -14.6f), new Vector3(30f, TunnelClearance, -14f), GreyboxKind.TunnelWall);
            Add(new Vector3(20f, 0f, -12f), new Vector3(30f, TunnelClearance, -11.4f), GreyboxKind.TunnelWall);
            Add(new Vector3(20f, TunnelClearance, -14.6f), new Vector3(30f, TunnelClearance + 0.4f, -11.4f), GreyboxKind.TunnelRoof);
            var tunnel = new Vector3(18f, 0f, -13f);

            // Tower: stacked boxes reachable by stepping around them (visual landmark, tests line of sight later).
            for (int level = 0; level < 5; level++)
            {
                float inset = level * 0.9f;
                Add(new Vector3(-40f + inset, level * 0.4f, 30f + inset), new Vector3(-30f - inset, (level + 1) * 0.4f, 40f - inset), GreyboxKind.Tower);
            }

            // Crates, deterministic from the seed, kept out of the 40 m spawn plaza.
            var rng = new DeterministicRandom((ulong)seed);
            int placed = 0;
            while (placed < 300)
            {
                float x = rng.Range(-half + 10f, half - 10f);
                float z = rng.Range(-half + 10f, half - 10f);
                if (Math.Abs(x) < 40f && Math.Abs(z) < 40f)
                {
                    continue;
                }

                float w = rng.Range(1f, 4f);
                float d = rng.Range(1f, 4f);
                float h = rng.Range(0.6f, 3.5f);
                Add(new Vector3(x, 0f, z), new Vector3(x + w, h, z + d), GreyboxKind.Crate);
                placed++;
            }

            // 16 spawn points on a circle around the origin.
            var spawns = new List<Vector3>(16);
            for (int i = 0; i < 16; i++)
            {
                double a = i * Math.PI * 2 / 16;
                spawns.Add(new Vector3((float)Math.Sin(a) * 6f, 0f, (float)Math.Cos(a) * 6f));
            }

            return new GreyboxLayout(seed, pieces, spawns, stairs, platformTop, tunnel, wallFront);
        }
    }
}
