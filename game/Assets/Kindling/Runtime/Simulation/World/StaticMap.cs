using System.Collections.Generic;
using System.Numerics;
using Kindling.Content;
using Kindling.Simulation.Collision;

namespace Kindling.Simulation.World
{
    public struct NodePlacement
    {
        public NodeDef Def;

        /// <summary>Feet position (bottom center).</summary>
        public Vector3 Position;

        public float Yaw;

        public Aabb Bounds => new Aabb(
            new Vector3(Position.X - Def.Size.X * 0.5f, Position.Y, Position.Z - Def.Size.Z * 0.5f),
            new Vector3(Position.X + Def.Size.X * 0.5f, Position.Y + Def.Size.Y, Position.Z + Def.Size.Z * 0.5f));

        /// <summary>Plants don't block movement; trees, rocks and ores do.</summary>
        public bool Blocks => !Def.Pickable;
    }

    /// <summary>
    /// Everything about the map that never changes while the server runs: greybox geometry, resource node
    /// placements and water. The server sends it to clients at join (so they don't depend on generating
    /// the same floats), and both sides build the same collision world from it.
    /// </summary>
    public sealed class StaticMap
    {
        public StaticMap(int seed, float mapSize, List<GreyboxPiece> pieces, List<NodePlacement> nodes, List<Aabb> ponds)
        {
            Seed = seed;
            MapSize = mapSize;
            Pieces = pieces;
            Nodes = nodes;
            Ponds = ponds;
        }

        public int Seed { get; }

        public float MapSize { get; }

        public List<GreyboxPiece> Pieces { get; }

        /// <summary>Node index = position in this list (replication and saves refer to it).</summary>
        public List<NodePlacement> Nodes { get; }

        public List<Aabb> Ponds { get; }

        public CollisionWorld BuildCollisionWorld()
        {
            var boxes = new List<Aabb>(Pieces.Count + Nodes.Count);
            foreach (GreyboxPiece p in Pieces)
            {
                boxes.Add(p.Box);
            }

            foreach (NodePlacement n in Nodes)
            {
                if (n.Blocks)
                {
                    boxes.Add(n.Bounds);
                }
            }

            return new CollisionWorld(boxes);
        }

        public bool IsInPond(Vector3 position, float margin = 0.5f)
        {
            foreach (Aabb pond in Ponds)
            {
                if (position.X >= pond.Min.X - margin && position.X <= pond.Max.X + margin && position.Z >= pond.Min.Z - margin && position.Z <= pond.Max.Z + margin)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
