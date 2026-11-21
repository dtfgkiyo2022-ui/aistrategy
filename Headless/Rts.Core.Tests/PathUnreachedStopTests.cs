using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>Design 93: stopping the search at the nearest reachable cell returns the same path as the full search.</summary>
    public sealed class PathUnreachedStopTests
    {
        private static MapDefinition Map(int width, int height, int[] blocked)
            => new MapDefinition { WidthCells = width, HeightCells = height, WidthMeters = width * 2,
                HeightMeters = height * 2, CellSizeMeters = 2, DefaultPassable = true, BlockedCellIds = blocked };

        private static int[] RandomBlocked(Random random, int cells, int percent)
            => Enumerable.Range(0, cells).Where(_ => random.Next(100) < percent).ToArray();

        private static SimPoint RandomGoal(Random random, int width, int height)
        {
            // Cell centres, cell corners, and points just outside the map all occur in matches.
            int mode = random.Next(3);
            long x = mode == 2 ? random.Next(-3, width * 2 + 3) : random.Next(0, width * 2);
            long z = mode == 2 ? random.Next(-3, height * 2 + 3) : random.Next(0, height * 2);
            var px = mode == 0 ? Fix64.FromRaw(Fix64.FromInt((int)x).Raw + Fix64.FromInt(1).Raw / 3) : Fix64.FromInt((int)x);
            var pz = mode == 0 ? Fix64.FromRaw(Fix64.FromInt((int)z).Raw + Fix64.FromInt(1).Raw / 7) : Fix64.FromInt((int)z);
            return new SimPoint(px, pz);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SamePathAsTheFullSearchOnRandomMaps(bool sideFair)
        {
            var random = new Random(20261122);
            int compared = 0, unreached = 0;
            for (int round = 0; round < 60; round++)
            {
                int width = 12 + random.Next(20), height = 8 + random.Next(12);
                var definition = Map(width, height, RandomBlocked(random, width * height, 25 + random.Next(25)));
                var stop = new GridMap(definition) { SideFair = sideFair };
                var full = new GridMap(definition) { SideFair = sideFair, UnreachedStop = false };
                for (int query = 0; query < 40; query++)
                {
                    int start = random.Next(width * height);
                    var goal = RandomGoal(random, width, height);
                    var a = stop.FindPath(start, goal, 1);
                    var b = full.FindPath(start, goal, 1);
                    Assert.That(a, Is.EqualTo(b), "round " + round + " start " + start + " goal " + goal.X.Raw + "," + goal.Z.Raw);
                    compared++;
                    int target = stop.Cell(goal);
                    if (b.Length > 0 && (target < 0 || b[b.Length - 1] != target)) unreached++;
                }
            }
            Assert.That(compared, Is.EqualTo(2400));
            Assert.That(unreached, Is.GreaterThan(200), "the random maps must exercise unreachable goals");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ArmyFlowRouteIsTheSameAsTheFullSearch(bool sideFair)
        {
            var random = new Random(1122);
            for (int round = 0; round < 40; round++)
            {
                int width = 12 + random.Next(20), height = 8 + random.Next(12);
                var definition = Map(width, height, RandomBlocked(random, width * height, 25 + random.Next(25)));
                var stop = new GridMap(definition) { SideFair = sideFair };
                var full = new GridMap(definition) { SideFair = sideFair, UnreachedStop = false };
                for (int query = 0; query < 40; query++)
                {
                    int start = random.Next(width * height);
                    var goal = RandomGoal(random, width, height);
                    int lookahead = 1 + random.Next(16);
                    Assert.That(stop.FindArmyFlowPath(start, goal, 1, lookahead), Is.EqualTo(full.FindArmyFlowPath(start, goal, 1, lookahead)),
                        "round " + round + " start " + start);
                }
            }
        }

        [Test]
        public void StillTheSameAfterTheTerrainChanges()
        {
            var random = new Random(7);
            const int width = 20, height = 12;
            var definition = Map(width, height, RandomBlocked(random, width * height, 30));
            var stop = new GridMap(definition);
            var full = new GridMap(definition) { UnreachedStop = false };
            for (int step = 0; step < 200; step++)
            {
                if (step % 5 == 0)
                {
                    int cell = random.Next(width * height);
                    bool open = random.Next(2) == 0;
                    stop.SetPassable(cell, open); full.SetPassable(cell, open);
                }
                if (step % 17 == 0) { stop.InvalidateRoutes(); full.InvalidateRoutes(); }
                int start = random.Next(width * height);
                var goal = RandomGoal(random, width, height);
                Assert.That(stop.FindPath(start, goal, 2), Is.EqualTo(full.FindPath(start, goal, 2)), "step " + step);
                Assert.That(stop.FindArmyFlowPath(start, goal, 2, 8), Is.EqualTo(full.FindArmyFlowPath(start, goal, 2, 8)), "flow step " + step);
            }
        }

        [Test]
        public void WalledGoalStopsEarly()
        {
            // A goal inside a closed ring: the full search would close every outside cell.
            const int width = 40, height = 40;
            var ring = Enumerable.Range(0, width * height).Where(id =>
            {
                int x = id % width, z = id / width;
                return (x == 30 || x == 36) && z >= 30 && z <= 36 || (z == 30 || z == 36) && x >= 30 && x <= 36;
            }).ToArray();
            var definition = Map(width, height, ring);
            var stop = new GridMap(definition);
            var full = new GridMap(definition) { UnreachedStop = false };
            var goal = stop.Center(33 * width + 33);
            Assert.That(stop.FindPath(0, goal, 1), Is.EqualTo(full.FindPath(0, goal, 1)));
        }
    }
}
