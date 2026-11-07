using System;
using Kindling.Core.Timing;
using NUnit.Framework;

namespace Kindling.Tests.Pure
{
    public class TickClockTests
    {
        [Test]
        public void DefaultClockRunsAtThirtyHertz()
        {
            Assert.That(TickClock.Default.TickRate, Is.EqualTo(30));
            Assert.That(TickClock.Default.TickDuration, Is.EqualTo(1.0 / 30).Within(1e-12));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(129)]
        public void RejectsTickRatesOutsideSupportedRange(int rate)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TickClock(rate));
        }

        [TestCase(0L, 0.0)]
        [TestCase(30L, 1.0)]
        [TestCase(45L, 1.5)]
        public void TimeOfTick(long tick, double expectedSeconds)
        {
            Assert.That(TickClock.Default.TimeOfTick(tick), Is.EqualTo(expectedSeconds).Within(1e-12));
        }

        [Test]
        public void TickAtTimeIsInverseOfTimeOfTickOnBoundaries()
        {
            var clock = TickClock.Default;
            for (long tick = 0; tick < 10_000; tick += 7)
            {
                Assert.That(clock.TickAtTime(clock.TimeOfTick(tick)), Is.EqualTo(tick), $"tick {tick}");
            }
        }

        [Test]
        public void TickAtTimeFloorsWithinATick()
        {
            var clock = TickClock.Default;
            Assert.That(clock.TickAtTime(0.0), Is.EqualTo(0));
            Assert.That(clock.TickAtTime(0.0333), Is.EqualTo(0));
            Assert.That(clock.TickAtTime(0.034), Is.EqualTo(1));
            Assert.That(clock.TickAtTime(-5.0), Is.EqualTo(0));
        }

        [Test]
        public void TicksForDurationRoundsUp()
        {
            var clock = TickClock.Default;
            Assert.That(clock.TicksForDuration(0), Is.EqualTo(0));
            Assert.That(clock.TicksForDuration(1.0), Is.EqualTo(30));
            Assert.That(clock.TicksForDuration(0.01), Is.EqualTo(1));
            Assert.That(clock.TicksForDuration(0.25), Is.EqualTo(8));
        }

        [Test]
        public void RejectsNonFiniteTimes()
        {
            Assert.Throws<ArgumentException>(() => TickClock.Default.TickAtTime(double.NaN));
            Assert.Throws<ArgumentException>(() => TickClock.Default.TicksForDuration(double.PositiveInfinity));
            Assert.Throws<ArgumentException>(() => TickClock.Default.TicksForDuration(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => TickClock.Default.TimeOfTick(-1));
        }
    }
}
