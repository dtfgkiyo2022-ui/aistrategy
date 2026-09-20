using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Decision;

namespace Rts.Tests.EditMode
{
    /// <summary>
    /// The decision layer's distance tests were rewritten without BigInteger (which allocated on every call). They must
    /// return exactly what the wide arithmetic returns, so each one is compared against the original BigInteger code on a
    /// fixed-seed sweep that includes boundary cases (a point exactly on the radius) and degenerate segments.
    /// </summary>
    public sealed class DecisionArithmeticTests
    {
        private static SimPoint P(long x, long z) => new SimPoint(Fix64.FromRaw(x), Fix64.FromRaw(z));

        // The code as it was before the change.
        private static BigInteger ReferenceDistance(SimPoint a, SimPoint b)
        {
            var x = new BigInteger(a.X.Raw) - b.X.Raw; var z = new BigInteger(a.Z.Raw) - b.Z.Raw;
            return x * x + z * z;
        }

        private static bool ReferenceNearRoute(SimPoint point, IReadOnlyList<SimPoint> cells, int meters)
        {
            if (cells == null || cells.Count == 0) return false;
            BigInteger radius = new BigInteger(Fix64.FromInt(meters).Raw); radius *= radius;
            for (int i = 0; i < cells.Count; i++)
            {
                SimPoint a = cells[i], b = cells[Math.Min(i + 1, cells.Count - 1)];
                BigInteger ax = a.X.Raw, az = a.Z.Raw, bx = b.X.Raw, bz = b.Z.Raw, px = point.X.Raw, pz = point.Z.Raw;
                BigInteger dx = bx - ax, dz = bz - az, ux = px - ax, uz = pz - az, length = dx * dx + dz * dz;
                BigInteger cross;
                if (length == 0) { cross = ux * ux + uz * uz; if (cross <= radius) return true; }
                else
                {
                    BigInteger dot = ux * dx + uz * dz;
                    if (dot <= 0) { if (ux * ux + uz * uz <= radius) return true; continue; }
                    else if (dot >= length) { BigInteger vx = px - bx, vz = pz - bz; if (vx * vx + vz * vz <= radius) return true; continue; }
                    else cross = (ux * ux + uz * uz) * length - dot * dot;
                    if (cross <= radius * length) return true;
                }
            }
            return false;
        }

        [Test]
        public void DistanceSquaredEqualsTheBigIntegerDistance()
        {
            var random = new Random(20260921);
            for (int i = 0; i < 200000; i++)
            {
                // On-map coordinates, and now and then coordinates far outside the map (up to about 2^45 raw units).
                long span = i % 10 == 0 ? 1L << 45 : 1L << 24;
                var a = P(NextLong(random, -span, span), NextLong(random, -span, span));
                var b = P(NextLong(random, -span, span), NextLong(random, -span, span));
                Assert.That(PolicyDecision.DistanceSquared(a, b).ToBigInteger(), Is.EqualTo(ReferenceDistance(a, b)), "case " + i);
            }
        }

        [Test]
        public void SegmentLengthEqualsTheBigIntegerRoot()
        {
            var random = new Random(20260923);
            for (int i = 0; i < 200000; i++)
            {
                long span = i % 10 == 0 ? 1L << 45 : 1L << 24;
                var a = P(NextLong(random, -span, span), NextLong(random, -span, span));
                var b = P(NextLong(random, -span, span), NextLong(random, -span, span));
                Assert.That(PolicyDecision.SegmentLength(a, b), Is.EqualTo((long)FixMath.IntegerSqrt(ReferenceDistance(a, b))), "case " + i);
            }
        }

        [Test]
        public void SegmentLengthIsExactAroundPerfectSquares()
        {
            // Along one axis the squared distance is n*n, so the root is n; one axis step either side must round down correctly.
            foreach (long n in new long[] { 0, 1, 2, 3, 65535, 65536, 65537, (1L << 24) - 1, 1L << 24, 3037000499L })
            {
                var a = P(0, 0);
                Assert.That(PolicyDecision.SegmentLength(a, P(n, 0)), Is.EqualTo(n), "n=" + n);
                Assert.That(PolicyDecision.SegmentLength(a, P(0, -n)), Is.EqualTo(n), "n=" + n);
            }
            // (n, 1): n*n + 1 is not a square for n >= 1, its floor root is n.
            Assert.That(PolicyDecision.SegmentLength(P(0, 0), P(65536, 1)), Is.EqualTo(65536));
            // 3-4-5 in raw units.
            Assert.That(PolicyDecision.SegmentLength(P(0, 0), P(3L * 65536, 4L * 65536)), Is.EqualTo(5L * 65536));
        }

        [Test]
        public void NearRouteEqualsTheBigIntegerVersion()
        {
            var random = new Random(20260922);
            int hits = 0;
            for (int i = 0; i < 60000; i++)
            {
                var route = new List<SimPoint>();
                // Cell-to-cell routes like the simulation's, and now and then long straight legs (up to the map's size).
                bool longLegs = i % 7 == 0;
                long x = NextLong(random, 0, 1L << 24), z = NextLong(random, 0, 1L << 24);
                int count = random.Next(0, 12);
                for (int c = 0; c < count; c++)
                {
                    route.Add(P(x, z));
                    long step = longLegs ? 1L << 24 : 4L * 65536;
                    if (random.Next(2) == 0) x = Math.Clamp(x + NextLong(random, -step, step + 1), 0, 1L << 24);
                    else z = Math.Clamp(z + NextLong(random, -step, step + 1), 0, 1L << 24);
                    if (i % 5 == 0 && c % 3 == 0) route.Add(route[route.Count - 1]); // a repeated point: a zero-length segment
                }
                var point = route.Count > 0 && i % 3 == 0
                    ? P(route[route.Count / 2].X.Raw + NextLong(random, -30L * 65536, 30L * 65536), route[route.Count / 2].Z.Raw + NextLong(random, -30L * 65536, 30L * 65536))
                    : P(NextLong(random, 0, 1L << 24), NextLong(random, 0, 1L << 24));
                bool expected = ReferenceNearRoute(point, route, 24);
                if (expected) hits++;
                Assert.That(PolicyDecision.NearRoute(point, route, 24), Is.EqualTo(expected), "case " + i);
            }
            Assert.That(hits, Is.GreaterThan(1000), "the sweep must actually reach the near case, or it proves nothing");
        }

        [Test]
        public void NearRouteAgreesOnTheExactBoundary()
        {
            // A point exactly 24 m from a horizontal segment is near (the test is <=); one raw unit further is not.
            long r = Fix64.FromInt(24).Raw;
            var route = new List<SimPoint> { P(0, 0), P(4L * 65536, 0) };
            Assert.That(PolicyDecision.NearRoute(P(2L * 65536, r), route, 24), Is.True);
            Assert.That(PolicyDecision.NearRoute(P(2L * 65536, r + 1), route, 24), Is.False);
            // Past an end of the segment the distance is to the end point.
            Assert.That(PolicyDecision.NearRoute(P(4L * 65536 + r, 0), route, 24), Is.True);
            Assert.That(PolicyDecision.NearRoute(P(4L * 65536 + r + 1, 0), route, 24), Is.False);
            Assert.That(PolicyDecision.NearRoute(P(0, 0), new List<SimPoint>(), 24), Is.False);
        }

        [Test]
        public void DistanceIsExactAtTheExtremesOfTheCoordinateRange()
        {
            // The full long range along one axis, where a plain long subtraction would wrap: still exact.
            var edge = P(long.MinValue, 0);
            var far = P(long.MaxValue, 0);
            Assert.That(PolicyDecision.DistanceSquared(edge, far).ToBigInteger(), Is.EqualTo(ReferenceDistance(edge, far)));
            Assert.That(PolicyDecision.DistanceSquared(edge, edge).ToBigInteger(), Is.EqualTo(BigInteger.Zero));
            // Corner to corner of the whole range needs 129 bits, so it is an error rather than a wrong number.
            Assert.Throws<OverflowException>(() => PolicyDecision.DistanceSquared(P(long.MinValue, long.MinValue), P(long.MaxValue, long.MaxValue)));
        }

        // Random.NextInt64 does not exist in every runtime the Unity tests run on, so the range is built from bytes.
        private static long NextLong(Random random, long minInclusive, long maxExclusive)
        {
            ulong range = unchecked((ulong)maxExclusive - (ulong)minInclusive);
            return unchecked((long)((ulong)minInclusive + NextUInt64(random) % range));
        }

        private static ulong NextUInt64(Random random)
        {
            var bytes = new byte[8];
            random.NextBytes(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }

        [Test]
        public void WideMultiplyAndCompareMatchBigInteger()
        {
            var random = new Random(20260924);
            for (int i = 0; i < 200000; i++)
            {
                ulong a = NextUInt64(random), b = NextUInt64(random);
                if (i % 5 == 0) { a >>= random.Next(64); b >>= random.Next(64); }
                var product = Wide.Multiply(a, b);
                Assert.That(product.ToBigInteger(), Is.EqualTo(new BigInteger(a) * b), "case " + i);
                var other = Wide.Multiply(b, a >> 3);
                Assert.That(product.CompareTo(other), Is.EqualTo(new BigInteger(a) * b > new BigInteger(b) * (a >> 3) ? 1 : new BigInteger(a) * b == new BigInteger(b) * (a >> 3) ? 0 : -1), "compare " + i);
                if (product >= other) Assert.That(Wide.Subtract(product, other).ToBigInteger(), Is.EqualTo(product.ToBigInteger() - other.ToBigInteger()), "subtract " + i);
            }
            Assert.Throws<OverflowException>(() => Wide.Subtract(Wide.Multiply(1, 1), Wide.Multiply(2, 2)));
        }
    }
}
