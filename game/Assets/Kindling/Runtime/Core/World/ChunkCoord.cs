using System;

namespace Kindling.Core.World
{
    /// <summary>
    /// Integer coordinate of a square world chunk on the X/Z plane. Chunk (0,0) starts at the
    /// world origin; negative coordinates are valid (the map is centred on the origin).
    /// </summary>
    public readonly struct ChunkCoord : IEquatable<ChunkCoord>
    {
        public int X { get; }
        public int Z { get; }

        public ChunkCoord(int x, int z)
        {
            X = x;
            Z = z;
        }

        /// <summary>The chunk containing a world position. Positions on a boundary belong to the higher chunk.</summary>
        public static ChunkCoord FromWorld(double worldX, double worldZ, float chunkSize)
        {
            if (!(chunkSize > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), chunkSize, "Chunk size must be positive.");
            }

            if (double.IsNaN(worldX) || double.IsNaN(worldZ) || double.IsInfinity(worldX) || double.IsInfinity(worldZ))
            {
                throw new ArgumentException("World position must be finite.");
            }

            return new ChunkCoord((int)Math.Floor(worldX / chunkSize), (int)Math.Floor(worldZ / chunkSize));
        }

        /// <summary>World X of this chunk's minimum corner.</summary>
        public double MinWorldX(float chunkSize) => (double)X * chunkSize;

        /// <summary>World Z of this chunk's minimum corner.</summary>
        public double MinWorldZ(float chunkSize) => (double)Z * chunkSize;

        /// <summary>Chebyshev distance in chunks (number of rings between two chunks).</summary>
        public int ChebyshevDistance(ChunkCoord other) => Math.Max(Math.Abs(X - other.X), Math.Abs(Z - other.Z));

        public bool Equals(ChunkCoord other) => X == other.X && Z == other.Z;

        public override bool Equals(object obj) => obj is ChunkCoord other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 73856093) ^ (Z * 19349663);
            }
        }

        public static bool operator ==(ChunkCoord a, ChunkCoord b) => a.Equals(b);

        public static bool operator !=(ChunkCoord a, ChunkCoord b) => !a.Equals(b);

        public override string ToString() => $"({X}, {Z})";
    }
}
