using System;
using Kindling.Core.World;
using NUnit.Framework;

namespace Kindling.Tests.Pure
{
    public class ChunkCoordTests
    {
        [TestCase(0.0, 0.0, 0, 0)]
        [TestCase(63.99, 63.99, 0, 0)]
        [TestCase(64.0, 0.0, 1, 0)]
        [TestCase(-0.01, -0.01, -1, -1)]
        [TestCase(-64.0, 128.5, -1, 2)]
        [TestCase(-64.01, 0.0, -2, 0)]
        public void FromWorldUsesFloorDivision(double x, double z, int expectedX, int expectedZ)
        {
            Assert.That(ChunkCoord.FromWorld(x, z, 64f), Is.EqualTo(new ChunkCoord(expectedX, expectedZ)));
        }

        [Test]
        public void FromWorldRejectsInvalidInput()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ChunkCoord.FromWorld(0, 0, 0f));
            Assert.Throws<ArgumentException>(() => ChunkCoord.FromWorld(double.NaN, 0, 64f));
        }

        [Test]
        public void EqualityAndHashing()
        {
            var a = new ChunkCoord(3, -4);
            var b = new ChunkCoord(3, -4);
            Assert.That(a == b, Is.True);
            Assert.That(a != new ChunkCoord(-4, 3), Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void ChebyshevDistanceCountsRings()
        {
            Assert.That(new ChunkCoord(0, 0).ChebyshevDistance(new ChunkCoord(2, -1)), Is.EqualTo(2));
            Assert.That(new ChunkCoord(-3, 5).ChebyshevDistance(new ChunkCoord(-3, 5)), Is.EqualTo(0));
        }

        [Test]
        public void MinCornerMatchesFromWorld()
        {
            var chunk = new ChunkCoord(-2, 7);
            Assert.That(ChunkCoord.FromWorld(chunk.MinWorldX(64f), chunk.MinWorldZ(64f), 64f), Is.EqualTo(chunk));
        }
    }
}
