using System;
using System.Collections.Generic;
using Kindling.Core.World;
using NUnit.Framework;

namespace Kindling.Tests.Pure
{
    public class WorldGridTests
    {
        // Launch map: 2 km × 2 km, 64 m chunks → 32 × 32 = 1024 chunks (blueprint 05 §5.1).
        static WorldGrid LaunchMap() => new WorldGrid(2048f);

        [Test]
        public void LaunchMapHas1024Chunks()
        {
            var grid = LaunchMap();
            Assert.That(grid.ChunksPerAxis, Is.EqualTo(32));
            Assert.That(grid.ChunkCount, Is.EqualTo(1024));
            Assert.That(grid.MinChunk, Is.EqualTo(-16));
            Assert.That(grid.MaxChunk, Is.EqualTo(15));
        }

        [Test]
        public void RejectsSizesThatAreNotEvenMultiplesOfTheChunkSize()
        {
            Assert.Throws<ArgumentException>(() => new WorldGrid(2000f));
            Assert.Throws<ArgumentException>(() => new WorldGrid(192f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorldGrid(-128f));
        }

        [Test]
        public void ContainsPositionUsesHalfOpenBounds()
        {
            var grid = LaunchMap();
            Assert.That(grid.ContainsPosition(-1024, -1024), Is.True);
            Assert.That(grid.ContainsPosition(1023.99, 1023.99), Is.True);
            Assert.That(grid.ContainsPosition(1024, 0), Is.False);
            Assert.That(grid.ContainsPosition(0, -1024.01), Is.False);
        }

        [Test]
        public void ChunkAtCornersMapsToExtremeChunks()
        {
            var grid = LaunchMap();
            Assert.That(grid.ChunkAt(-1024, -1024), Is.EqualTo(new ChunkCoord(-16, -16)));
            Assert.That(grid.ChunkAt(1023.9, 1023.9), Is.EqualTo(new ChunkCoord(15, 15)));
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.ChunkAt(1024, 0));
        }

        [Test]
        public void IndexRoundTripsForEveryChunk()
        {
            var grid = LaunchMap();
            var seen = new HashSet<int>();
            for (int z = grid.MinChunk; z <= grid.MaxChunk; z++)
            {
                for (int x = grid.MinChunk; x <= grid.MaxChunk; x++)
                {
                    var chunk = new ChunkCoord(x, z);
                    int index = grid.IndexOf(chunk);
                    Assert.That(index, Is.InRange(0, grid.ChunkCount - 1));
                    Assert.That(seen.Add(index), Is.True, "indices must be unique");
                    Assert.That(grid.ChunkOfIndex(index), Is.EqualTo(chunk));
                }
            }

            Assert.That(seen.Count, Is.EqualTo(grid.ChunkCount));
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.IndexOf(new ChunkCoord(16, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.ChunkOfIndex(1024));
        }

        [Test]
        public void ZeroRadiusReturnsOnlyTheContainingChunk()
        {
            var results = new List<ChunkCoord>();
            int added = LaunchMap().CollectChunksInRadius(10, 10, 0, results);
            Assert.That(added, Is.EqualTo(1));
            Assert.That(results, Is.EqualTo(new[] { new ChunkCoord(0, 0) }));
        }

        [Test]
        public void RadiusSelectsChunksOverlappingTheCircleOnly()
        {
            var grid = LaunchMap();
            var results = new List<ChunkCoord>();
            // Centre of chunk (0,0) is (32,32). A 40 m radius reaches the 4 edge neighbours (8 m into them)
            // but not the diagonal neighbours (their nearest corner is ~45.25 m away).
            grid.CollectChunksInRadius(32, 32, 40, results);
            Assert.That(results.Count, Is.EqualTo(5));
            Assert.That(results, Does.Contain(new ChunkCoord(1, 0)));
            Assert.That(results, Does.Not.Contain(new ChunkCoord(1, 1)));

            results.Clear();
            grid.CollectChunksInRadius(32, 32, 46, results);
            Assert.That(results.Count, Is.EqualTo(9));
        }

        [Test]
        public void RadiusIsClippedToTheMap()
        {
            var grid = LaunchMap();
            var results = new List<ChunkCoord>();
            grid.CollectChunksInRadius(-1020, -1020, 100, results);
            foreach (var chunk in results)
            {
                Assert.That(grid.ContainsChunk(chunk), Is.True, chunk.ToString());
            }

            Assert.That(results, Does.Contain(new ChunkCoord(-16, -16)));
        }

        [Test]
        public void LargeRadiusCoversWholeMapExactlyOnce()
        {
            var grid = LaunchMap();
            var results = new List<ChunkCoord>();
            grid.CollectChunksInRadius(0, 0, 5000, results);
            Assert.That(results.Count, Is.EqualTo(grid.ChunkCount));
            Assert.That(new HashSet<ChunkCoord>(results).Count, Is.EqualTo(grid.ChunkCount));
        }

        [Test]
        public void CollectAppendsWithoutClearing()
        {
            var results = new List<ChunkCoord> { new ChunkCoord(99, 99) };
            LaunchMap().CollectChunksInRadius(10, 10, 0, results);
            Assert.That(results.Count, Is.EqualTo(2));
        }
    }
}
