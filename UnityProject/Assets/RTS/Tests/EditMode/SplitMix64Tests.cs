using System;
using System.Numerics;
using NUnit.Framework;
using Rts.Contracts;

namespace Rts.Tests.EditMode
{
    public sealed class SplitMix64Tests
    {
        [Test]
        public void SeedZeroMatchesKnownVectorsAndCountsDraws()
        {
            var rng = new SplitMix64(0);
            Assert.That(rng.State, Is.Zero);
            Assert.That(rng.CallCount, Is.Zero);
            foreach (ulong expected in new[] { 0xE220A8397B1DCDAFUL, 0x6E789E6AA1B965F4UL, 0x06C45D188009454FUL })
                Assert.That(rng.NextUInt64(), Is.EqualTo(expected));
            Assert.That(rng.CallCount, Is.EqualTo(3UL));
            Assert.That(rng.State, Is.EqualTo(unchecked(3UL * 0x9E3779B97F4A7C15UL)));
        }

        [Test]
        public void RestoringStateAndCountContinuesTheSameSequence()
        {
            var rng = new SplitMix64(123);
            for (int i = 0; i < 17; i++) rng.NextUInt64();
            var restored = new SplitMix64(0);
            restored.Restore(rng.State, rng.CallCount);
            for (int i = 0; i < 1000; i++)
            {
                Assert.That(restored.NextUInt64(), Is.EqualTo(rng.NextUInt64()));
                Assert.That(restored.NextUInt64(0x8000000000000001UL), Is.EqualTo(rng.NextUInt64(0x8000000000000001UL)));
                Assert.That(restored.State, Is.EqualTo(rng.State));
                Assert.That(restored.CallCount, Is.EqualTo(rng.CallCount));
            }
        }

        [Test]
        public void RangesMatchIndependentRejectionReference()
        {
            foreach (ulong bound in new[] { 1UL, 2UL, 3UL, 17UL, 256UL, 0x8000000000000001UL, ulong.MaxValue })
            {
                var rng = new SplitMix64(0);
                var reference = new SplitMix64(0);
                ulong threshold = (ulong)((BigInteger.One << 64) % bound);
                for (int i = 0; i < 1000; i++)
                {
                    ulong sample;
                    do { sample = reference.NextUInt64(); } while (sample < threshold);
                    ulong actual = rng.NextUInt64(bound);
                    Assert.That(actual, Is.LessThan(bound));
                    Assert.That(actual, Is.EqualTo(sample % bound));
                    Assert.That(rng.CallCount, Is.EqualTo(reference.CallCount));
                }
                if (bound == 0x8000000000000001UL)
                    Assert.That(rng.CallCount, Is.GreaterThan(1000UL), "Exercise actual rejection, not just modulo.");
            }
        }

        [Test]
        public void InvalidRangeAndCounterOverflowDoNotMutateState()
        {
            var rng = new SplitMix64(99);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextUInt64(0));
            Assert.That(rng.State, Is.EqualTo(99UL));
            Assert.That(rng.CallCount, Is.Zero);
            rng.Restore(ulong.MaxValue, ulong.MaxValue);
            Assert.Throws<OverflowException>(() => rng.NextUInt64());
            Assert.That(rng.State, Is.EqualTo(ulong.MaxValue));
            Assert.That(rng.CallCount, Is.EqualTo(ulong.MaxValue));
        }
    }
}
