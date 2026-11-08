using System.Numerics;

namespace Kindling.Simulation.Collision
{
    /// <summary>Axis-aligned box (min/max corners). Used for world geometry and the player collider.</summary>
    public readonly struct Aabb
    {
        public readonly Vector3 Min;
        public readonly Vector3 Max;

        public Aabb(Vector3 min, Vector3 max)
        {
            Min = Vector3.Min(min, max);
            Max = Vector3.Max(min, max);
        }

        /// <summary>Box standing on <paramref name="feet"/> (bottom centre).</summary>
        public static Aabb FromFeet(Vector3 feet, float halfWidth, float height) =>
            new Aabb(new Vector3(feet.X - halfWidth, feet.Y, feet.Z - halfWidth), new Vector3(feet.X + halfWidth, feet.Y + height, feet.Z + halfWidth));

        public static Aabb FromCenterSize(Vector3 center, Vector3 size) => new Aabb(center - size * 0.5f, center + size * 0.5f);

        public Vector3 Center => (Min + Max) * 0.5f;
        public Vector3 Size => Max - Min;
        public Vector3 Feet => new Vector3((Min.X + Max.X) * 0.5f, Min.Y, (Min.Z + Max.Z) * 0.5f);

        public Aabb Offset(Vector3 delta) => new Aabb(Min + delta, Max + delta);

        public Aabb Offset(float x, float y, float z) => Offset(new Vector3(x, y, z));

        /// <summary>Box grown to cover a movement by <paramref name="motion"/> (for broad-phase queries).</summary>
        public Aabb Expand(Vector3 motion) => new Aabb(Vector3.Min(Min, Min + motion), Vector3.Max(Max, Max + motion));

        public Aabb Inflate(float amount) => new Aabb(Min - new Vector3(amount), Max + new Vector3(amount));

        /// <summary>True if the boxes overlap with positive volume (touching faces don't count).</summary>
        public bool Intersects(in Aabb other) =>
            Max.X > other.Min.X && Min.X < other.Max.X &&
            Max.Y > other.Min.Y && Min.Y < other.Max.Y &&
            Max.Z > other.Min.Z && Min.Z < other.Max.Z;

        public override string ToString() => $"Aabb({Min} → {Max})";
    }
}
