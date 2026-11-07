using System;
using System.Collections.Generic;

namespace Kindling.Core.World
{
    /// <summary>
    /// Square world map centred on the origin and divided into square chunks.
    /// Valid positions satisfy -HalfSize &lt;= x,z &lt; +HalfSize.
    /// Chunks are the unit of world replication, interest management and persistence
    /// (docs/blueprint/05-multiplayer-networking.md §5.1).
    /// </summary>
    public sealed class WorldGrid
    {
        public const float DefaultChunkSize = 64f;

        /// <summary>Edge length of the map in metres.</summary>
        public float MapSize { get; }

        /// <summary>Edge length of one chunk in metres.</summary>
        public float ChunkSize { get; }

        /// <summary>Number of chunks along one axis.</summary>
        public int ChunksPerAxis { get; }

        /// <summary>Total number of chunks in the map.</summary>
        public int ChunkCount => ChunksPerAxis * ChunksPerAxis;

        public float HalfSize => MapSize * 0.5f;

        /// <summary>Lowest valid chunk coordinate on each axis.</summary>
        public int MinChunk => -ChunksPerAxis / 2;

        /// <summary>Highest valid chunk coordinate on each axis.</summary>
        public int MaxChunk => MinChunk + ChunksPerAxis - 1;

        public WorldGrid(float mapSize, float chunkSize = DefaultChunkSize)
        {
            if (!(chunkSize > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), chunkSize, "Chunk size must be positive.");
            }

            if (!(mapSize > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(mapSize), mapSize, "Map size must be positive.");
            }

            double chunks = mapSize / (double)chunkSize;
            int whole = (int)Math.Round(chunks);
            if (Math.Abs(chunks - whole) > 1e-6 || whole % 2 != 0)
            {
                throw new ArgumentException(
                    $"Map size {mapSize} must be an even multiple of the chunk size {chunkSize} so the map can be centred on chunk boundaries.");
            }

            MapSize = mapSize;
            ChunkSize = chunkSize;
            ChunksPerAxis = whole;
        }

        public bool ContainsPosition(double x, double z) =>
            x >= -HalfSize && x < HalfSize && z >= -HalfSize && z < HalfSize;

        public bool ContainsChunk(ChunkCoord chunk) =>
            chunk.X >= MinChunk && chunk.X <= MaxChunk && chunk.Z >= MinChunk && chunk.Z <= MaxChunk;

        /// <summary>The chunk containing a world position. Throws if the position is outside the map.</summary>
        public ChunkCoord ChunkAt(double x, double z)
        {
            if (!ContainsPosition(x, z))
            {
                throw new ArgumentOutOfRangeException(nameof(x), $"Position ({x}, {z}) is outside the map.");
            }

            return ChunkCoord.FromWorld(x, z, ChunkSize);
        }

        /// <summary>Dense index in [0, ChunkCount) for array-backed per-chunk storage.</summary>
        public int IndexOf(ChunkCoord chunk)
        {
            if (!ContainsChunk(chunk))
            {
                throw new ArgumentOutOfRangeException(nameof(chunk), $"Chunk {chunk} is outside the map.");
            }

            return (chunk.Z - MinChunk) * ChunksPerAxis + (chunk.X - MinChunk);
        }

        /// <summary>Inverse of <see cref="IndexOf"/>.</summary>
        public ChunkCoord ChunkOfIndex(int index)
        {
            if (index < 0 || index >= ChunkCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Chunk index is outside the map.");
            }

            return new ChunkCoord(index % ChunksPerAxis + MinChunk, index / ChunksPerAxis + MinChunk);
        }

        /// <summary>
        /// Adds every in-map chunk whose square overlaps the circle (x, z, radius) to <paramref name="results"/>.
        /// The list is not cleared first, so callers can reuse it without allocations.
        /// Returns the number of chunks added.
        /// </summary>
        public int CollectChunksInRadius(double x, double z, double radius, List<ChunkCoord> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (double.IsNaN(radius) || radius < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must be non-negative.");
            }

            int minX = Math.Max(MinChunk, (int)Math.Floor((x - radius) / ChunkSize));
            int maxX = Math.Min(MaxChunk, (int)Math.Floor((x + radius) / ChunkSize));
            int minZ = Math.Max(MinChunk, (int)Math.Floor((z - radius) / ChunkSize));
            int maxZ = Math.Min(MaxChunk, (int)Math.Floor((z + radius) / ChunkSize));
            double radiusSq = radius * radius;
            int added = 0;

            for (int cz = minZ; cz <= maxZ; cz++)
            {
                double z0 = (double)cz * ChunkSize;
                double nearestZ = Clamp(z, z0, z0 + ChunkSize);
                for (int cx = minX; cx <= maxX; cx++)
                {
                    double x0 = (double)cx * ChunkSize;
                    double nearestX = Clamp(x, x0, x0 + ChunkSize);
                    double dx = x - nearestX;
                    double dz = z - nearestZ;
                    if (dx * dx + dz * dz <= radiusSq)
                    {
                        results.Add(new ChunkCoord(cx, cz));
                        added++;
                    }
                }
            }

            return added;
        }

        static double Clamp(double value, double min, double max) => value < min ? min : (value > max ? max : value);
    }
}
