using System;
using Kindling.Core.Numerics;
using NUnit.Framework;

namespace Kindling.Tests.Pure
{
    public class PositionQuantizerTests
    {
        const float HalfStep = PositionQuantizer.Step * 0.5f + 1e-5f;

        [Test]
        public void LocalCoordinatesRoundTripWithinHalfACentimetre()
        {
            for (float v = 0f; v <= 64f; v += 0.0137f)
            {
                float decoded = PositionQuantizer.DecodeLocal(PositionQuantizer.EncodeLocal(v, 64f));
                Assert.That(decoded, Is.EqualTo(v).Within(HalfStep), $"value {v}");
            }
        }

        [Test]
        public void HeightsRoundTripWithinHalfACentimetre()
        {
            for (float y = PositionQuantizer.MinY; y <= PositionQuantizer.MaxY; y += 0.731f)
            {
                float decoded = PositionQuantizer.DecodeY(PositionQuantizer.EncodeY(y));
                Assert.That(decoded, Is.EqualTo(y).Within(HalfStep), $"height {y}");
            }
        }

        [Test]
        public void HeightRangeCoversMountainsAndCaves()
        {
            Assert.That(PositionQuantizer.MinY, Is.LessThanOrEqualTo(-100f));
            Assert.That(PositionQuantizer.MaxY, Is.GreaterThanOrEqualTo(500f));
        }

        [Test]
        public void OutOfRangeValuesAreClamped()
        {
            Assert.That(PositionQuantizer.EncodeLocal(-3f, 64f), Is.EqualTo(0));
            Assert.That(PositionQuantizer.DecodeLocal(PositionQuantizer.EncodeLocal(70f, 64f)), Is.EqualTo(64f).Within(HalfStep));
            Assert.That(PositionQuantizer.EncodeY(-10_000f), Is.EqualTo(0));
            Assert.That(PositionQuantizer.EncodeY(10_000f), Is.EqualTo(ushort.MaxValue));
        }

        [Test]
        public void RejectsNonFiniteValuesAndBadChunkSizes()
        {
            Assert.Throws<ArgumentException>(() => PositionQuantizer.EncodeLocal(float.NaN, 64f));
            Assert.Throws<ArgumentException>(() => PositionQuantizer.EncodeY(float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => PositionQuantizer.EncodeLocal(1f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => PositionQuantizer.EncodeLocal(1f, 1000f));
        }
    }
}
