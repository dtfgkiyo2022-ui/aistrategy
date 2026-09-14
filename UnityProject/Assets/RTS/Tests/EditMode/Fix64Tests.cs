using System;
using System.Numerics;
using NUnit.Framework;
using Rts.Contracts;

namespace Rts.Tests.EditMode
{
    public sealed class Fix64Tests
    {
        private static Fix64 F(long raw) => Fix64.FromRaw(raw);
        private static SimPoint P(long x, long z) => new SimPoint(F(x), F(z));
        private static readonly long[] Values = {
            long.MinValue, long.MinValue + 1, -140737488355328L, -65537, -65536,
            -65535, -3, -2, -1, 0, 1, 2, 3, 21845, 65535, 65536, 65537,
            140737488355327L, long.MaxValue - 1, long.MaxValue
        };

        [Test]
        public void NegativeFractionsTruncateTowardZero()
        {
            Assert.That(Fix64.FromRatio(-1, 3).Raw, Is.EqualTo(-21845));
            Assert.That(Fix64.FromRatio(1, -3).Raw, Is.EqualTo(-21845));
            Assert.That(Fix64.FromRatio(-1, -3).Raw, Is.EqualTo(21845));
            Assert.That((F(-1) * F(21845)).Raw, Is.Zero);
            Assert.That((F(-65536) / F(196608)).Raw, Is.EqualTo(-21845));
            Assert.That((F(1) / F(-196608)).Raw, Is.Zero);
        }

        [Test]
        public void ArithmeticChecksBothLongBoundaries()
        {
            Assert.That((F(long.MaxValue) + F(0)).Raw, Is.EqualTo(long.MaxValue));
            Assert.That((F(long.MinValue) - F(0)).Raw, Is.EqualTo(long.MinValue));
            Assert.Throws<OverflowException>(() => { var x = F(long.MaxValue) + F(1); });
            Assert.Throws<OverflowException>(() => { var x = F(long.MinValue) + F(-1); });
            Assert.Throws<OverflowException>(() => { var x = F(long.MinValue) - F(1); });
            Assert.Throws<OverflowException>(() => { var x = F(long.MaxValue) - F(-1); });
            Assert.Throws<OverflowException>(() => { var x = F(long.MinValue) * F(-65536); });
            Assert.Throws<OverflowException>(() => { var x = F(long.MinValue) / F(-65536); });
            Assert.Throws<OverflowException>(() => Fix64.FromRatio(long.MaxValue, 1));
            Assert.Throws<OverflowException>(() => Fix64.FromRatio(long.MinValue, -1));
            Assert.Throws<DivideByZeroException>(() => { var x = F(1) / F(0); });
            Assert.Throws<DivideByZeroException>(() => Fix64.FromRatio(0, 0));
        }

        [Test]
        public void RawRoundTripAndOrderingCoverBoundaries()
        {
            foreach (long a in Values)
            {
                Assert.That(F(F(a).Raw).Raw, Is.EqualTo(a));
                foreach (long b in Values)
                {
                    Assert.That(F(a) < F(b), Is.EqualTo(a < b));
                    Assert.That(F(a) <= F(b), Is.EqualTo(a <= b));
                    Assert.That(F(a) > F(b), Is.EqualTo(a > b));
                    Assert.That(F(a) >= F(b), Is.EqualTo(a >= b));
                    Assert.That(((IComparable<Fix64>)F(a)).CompareTo(F(b)), Is.EqualTo(a.CompareTo(b)));
                }
            }
        }

        [Test]
        public void ArithmeticMatchesBigIntegerForTableAndFixedSeedSamples()
        {
            foreach (long a in Values)
            foreach (long b in Values) CheckPair(a, b);
            // Independent fixed-seed xorshift64 generator, not the production SplitMix64.
            ulong seed = 0x123456789ABCDEF0UL;
            for (int i = 0; i < 2048; i++)
            {
                long a = unchecked((long)Sample(ref seed));
                long b = unchecked((long)Sample(ref seed));
                CheckPair(a, b);
                CheckPair(a / 16777216, b / 16777216);
                CheckPair(a, b / 140737488355328L);
            }
        }

        private static ulong Sample(ref ulong s)
        {
            s ^= s << 13; s ^= s >> 7; s ^= s << 17;
            return s;
        }

        private static void CheckPair(long a, long b)
        {
            string input = "a=" + a + ", b=" + b;
            Check(new BigInteger(a) * b / 65536, () => F(a) * F(b), input + " multiply");
            if (b == 0)
            {
                Assert.Throws<DivideByZeroException>(() => { var x = F(a) / F(b); }, input);
                Assert.Throws<DivideByZeroException>(() => Fix64.FromRatio(a, b), input);
                return;
            }
            BigInteger expected = new BigInteger(a) * 65536 / b;
            Check(expected, () => F(a) / F(b), input + " divide");
            Check(expected, () => Fix64.FromRatio(a, b), input + " ratio");
        }

        private static void Check(BigInteger expected, Func<Fix64> actual, string input)
        {
            if (expected < long.MinValue || expected > long.MaxValue)
                Assert.Throws<OverflowException>(() => actual(), input);
            else Assert.That(actual().Raw, Is.EqualTo((long)expected), input);
        }

        [Test]
        public void IntegerSqrtFloorsSquaresAndNeighbours()
        {
            Assert.That(FixMath.IntegerSqrt(0), Is.EqualTo(BigInteger.Zero));
            foreach (BigInteger root in new[] { BigInteger.One, new BigInteger(2),
                new BigInteger(65536), new BigInteger(long.MaxValue), BigInteger.One << 128 })
            {
                BigInteger square = root * root;
                Assert.That(FixMath.IntegerSqrt(square - 1), Is.EqualTo(root - 1));
                Assert.That(FixMath.IntegerSqrt(square), Is.EqualTo(root));
                Assert.That(FixMath.IntegerSqrt(square + 1), Is.EqualTo(root));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => FixMath.IntegerSqrt(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => FixMath.Sqrt(F(-1)));
        }

        [TestCase(262143L, 131071L)]
        [TestCase(262144L, 131072L)]
        [TestCase(262145L, 131072L)]
        [TestCase(0L, 0L)]
        [TestCase(1L, 256L)]
        public void FixSqrtScalesAndFloors(long raw, long expected)
        {
            Assert.That(FixMath.Sqrt(F(raw)).Raw, Is.EqualTo(expected));
        }

        [Test]
        public void SqrtAtMaximumSatisfiesFloorBounds()
        {
            BigInteger root = FixMath.Sqrt(F(long.MaxValue)).Raw;
            BigInteger value = new BigInteger(long.MaxValue) * 65536;
            Assert.That(root * root <= value, Is.True);
            Assert.That((root + 1) * (root + 1) > value, Is.True);
        }

        [Test]
        public void DistanceComparisonUsesWideSquares()
        {
            Assert.That(FixMath.CompareDistanceSquared(-3, 4, 5), Is.Zero);
            Assert.That(FixMath.CompareDistanceSquared(-3, 4, 4), Is.GreaterThan(0));
            Assert.That(FixMath.CompareDistanceSquared(-3, 4, 6), Is.LessThan(0));
            Assert.That(FixMath.CompareDistanceSquared(0, 0, 0), Is.Zero);
            Assert.That(FixMath.CompareDistanceSquared(long.MaxValue, 0, long.MaxValue), Is.Zero);
            Assert.That(FixMath.CompareDistanceSquared(long.MinValue, long.MaxValue, long.MaxValue), Is.GreaterThan(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => FixMath.CompareDistanceSquared(0, 0, -1));
        }

        [Test]
        public void MovementUsesCeilingLengthAndTruncatesNegativeAxes()
        {
            AssertPoint(FixMath.MoveTowards(P(0, 0), P(2, -2), F(2)), 1, -1);
            AssertPoint(FixMath.MoveTowards(P(0, 0), P(-3, 4), F(2)), -1, 1);
            AssertPoint(FixMath.MoveTowards(P(0, 0), P(1, 1), F(1)), 0, 0);
            AssertPoint(FixMath.MoveTowards(P(7, 9), P(7, 9), F(10)), 7, 9);
            AssertPoint(FixMath.MoveTowards(P(7, 9), P(15, 20), F(0)), 7, 9);
            AssertPoint(FixMath.MoveTowards(P(10, 10), P(13, 14), F(5)), 13, 14);
            AssertPoint(FixMath.MoveTowards(P(10, 10), P(8, 8), F(3)), 8, 8);
            AssertPoint(FixMath.MoveTowards(P(long.MaxValue - 5, 0), P(long.MaxValue, 0), F(3)), long.MaxValue - 2, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => FixMath.MoveTowards(P(0, 0), P(0, 0), F(-1)));
            Assert.Throws<OverflowException>(() => FixMath.MoveTowards(P(long.MinValue, 0), P(long.MaxValue, 0), F(1)));
            Assert.Throws<OverflowException>(() => FixMath.MoveTowards(P(0, long.MaxValue), P(0, long.MinValue), F(1)));
        }

        [Test]
        public void DiagonalStepsNeverExceedStepOrOvershoot()
        {
            foreach (long x in new long[] { -100000, -3, -1, 1, 2, 300000, long.MaxValue })
            foreach (long z in new long[] { -90000, -2, 1, 3, 700000, long.MaxValue })
            foreach (long step in new long[] { 0, 1, 2, 5, 65536, long.MaxValue })
            {
                SimPoint p = FixMath.MoveTowards(P(0, 0), P(x, z), F(step));
                BigInteger travelled = new BigInteger(p.X.Raw) * p.X.Raw + new BigInteger(p.Z.Raw) * p.Z.Raw;
                Assert.That(travelled <= new BigInteger(step) * step, Is.True);
                Assert.That(p.X.Raw, Is.InRange(Math.Min(0, x), Math.Max(0, x)));
                Assert.That(p.Z.Raw, Is.InRange(Math.Min(0, z), Math.Max(0, z)));
                if (new BigInteger(x) * x + new BigInteger(z) * z <= new BigInteger(step) * step)
                    AssertPoint(p, x, z);
            }
        }

        private static void AssertPoint(SimPoint p, long x, long z)
        {
            Assert.That(p.X.Raw, Is.EqualTo(x));
            Assert.That(p.Z.Raw, Is.EqualTo(z));
        }
    }
}
