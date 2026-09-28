using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;

namespace Rts.Core.Tests
{
    public sealed class GridMapCacheTests
    {
        private static MapDefinition Map(int width = 12, int height = 8)
            => new MapDefinition { WidthCells = width, HeightCells = height, WidthMeters = width * 2,
                HeightMeters = height * 2, CellSizeMeters = 2, DefaultPassable = true, BlockedCellIds = Array.Empty<int>() };

        private static void SetPassable(GridMap map, int cell, bool value)
            => typeof(GridMap).GetMethod("SetPassable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(map, new object[] { cell, value });

        [Test]
        public void PathAndRouteEvictionRecomputeTheSameResult()
        {
            var map = new GridMap(Map());
            var goal = map.Center(95);
            var expectedPath = map.FindPath(0, goal);
            var expectedRoute = map.SharedRoute(0, goal);
            for (int i = 0; i < 4097; i++)
            {
                int start = i % 96;
                map.FindPath(start, map.Center((i * 7) % 96));
                map.SharedRoute(start, map.Center((i * 11) % 96));
            }
            Assert.That(map.FindPath(0, goal), Is.EqualTo(expectedPath));
            Assert.That(map.SharedRoute(0, goal), Is.EqualTo(expectedRoute));
        }

        [Test]
        public void SetPassableInvalidatesBothCachesAndMatchesFreshMap()
        {
            var map = new GridMap(Map());
            int blocked = 1 + 1 * 12;
            var goal = map.Center(95);
            map.FindPath(0, goal); map.SharedRoute(0, goal);
            SetPassable(map, blocked, false);
            var freshDefinition = Map(); freshDefinition.BlockedCellIds = new[] { blocked };
            var fresh = new GridMap(freshDefinition);
            Assert.That(map.FindPath(0, goal), Is.EqualTo(fresh.FindPath(0, goal)));
            Assert.That(map.SharedRoute(0, goal), Is.EqualTo(fresh.SharedRoute(0, goal)));
        }
    }
}
