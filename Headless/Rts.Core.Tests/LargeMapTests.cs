using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>S-2 mapgen-4: deterministic, symmetric and playable on the larger grid.</summary>
    public sealed class LargeMapTests
    {
        private static int Cell(ScenarioDefinition s, SimPoint p) =>
            (int)(p.Z.Raw / 65536 / s.Map.CellSizeMeters) * s.Map.WidthCells
                + (int)(p.X.Raw / 65536 / s.Map.CellSizeMeters);

        private static int Mirror(ScenarioDefinition s, int cell) =>
            (s.Map.HeightCells - 1 - cell / s.Map.WidthCells) * s.Map.WidthCells
                + s.Map.WidthCells - 1 - cell % s.Map.WidthCells;

        private static bool[] Passable(ScenarioDefinition s)
        {
            var result = new bool[s.Map.WidthCells * s.Map.HeightCells];
            Array.Fill(result, true);
            foreach (int cell in s.Map.BlockedCellIds) result[cell] = false;
            return result;
        }

        private static int[] Distances(ScenarioDefinition s, int start)
        {
            var passable = Passable(s);
            var result = new int[passable.Length]; Array.Fill(result, -1);
            var queue = new int[passable.Length]; int head = 0, tail = 0;
            if (!passable[start]) return result;
            result[start] = 0; queue[tail++] = start;
            while (head < tail)
            {
                int cell = queue[head++], x = cell % s.Map.WidthCells, z = cell / s.Map.WidthCells;
                Visit(z + 1 < s.Map.HeightCells ? cell + s.Map.WidthCells : -1);
                Visit(x + 1 < s.Map.WidthCells ? cell + 1 : -1);
                Visit(z > 0 ? cell - s.Map.WidthCells : -1);
                Visit(x > 0 ? cell - 1 : -1);
            }
            return result;
            void Visit(int cell)
            {
                if (cell < 0 || cell >= result.Length || !passable[cell] || result[cell] >= 0) return;
                result[cell] = result[queue[head - 1]] + 1; queue[tail++] = cell;
            }
        }

        [Test]
        public void SeedsOneToTwentyAreDeterministicAndValidate()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var first = MapGenerator.GenerateLarge(seed);
                var second = MapGenerator.GenerateLarge(seed);
                Assert.That(first.Map.WidthMeters, Is.EqualTo(512));
                Assert.That(first.Map.HeightMeters, Is.EqualTo(256));
                Assert.That(first.Map.WidthCells * first.Map.HeightCells, Is.EqualTo(32768));
                Assert.That(first.Outposts, Has.Length.EqualTo(6));
                Assert.That(ScenarioBinary.Encode(first), Is.EqualTo(ScenarioBinary.Encode(second)), "seed " + seed);
                Assert.DoesNotThrow(() => new Battle(first), "seed " + seed);
            }
        }

        [TestCase(1UL)]
        [TestCase(7UL)]
        [TestCase(20UL)]
        public void CoresOutpostsTerrainAndResourceClustersArePointSymmetric(ulong seed)
        {
            var s = MapGenerator.GenerateLarge(seed);
            var passable = Passable(s);
            var fromWest = Distances(s, Cell(s, s.Cores[0].Position));
            var fromEast = Distances(s, Cell(s, s.Cores[1].Position));
            Assert.That(fromWest[Cell(s, s.Cores[1].Position)], Is.GreaterThan(0));
            for (int cell = 0; cell < s.Map.Terrain.Length; cell++)
                Assert.That(s.Map.Terrain[cell], Is.EqualTo(s.Map.Terrain[Mirror(s, cell)]), "terrain cell " + cell);
            for (int i = 0; i < s.Outposts.Length; i += 2)
            {
                int west = Cell(s, s.Outposts[i].Position), east = Cell(s, s.Outposts[i + 1].Position);
                Assert.That(east, Is.EqualTo(Mirror(s, west)), "outpost pair " + s.Outposts[i].Id);
                Assert.That(fromWest[west], Is.GreaterThanOrEqualTo(0));
                Assert.That(fromEast[east], Is.GreaterThanOrEqualTo(0));
                Assert.That(Math.Abs(fromWest[west] - fromEast[east]), Is.LessThanOrEqualTo(Math.Max(1, fromWest[west] / 10)));
            }
            for (int i = 0; i < s.ResourceNodes.Length; i += 2)
            {
                int west = Cell(s, s.ResourceNodes[i].Position), east = Cell(s, s.ResourceNodes[i + 1].Position);
                Assert.That(s.ResourceNodes[i].Kind, Is.EqualTo(s.ResourceNodes[i + 1].Kind));
                Assert.That(east, Is.EqualTo(Mirror(s, west)), "resource pair " + s.ResourceNodes[i].Id);
                Assert.That(passable[west] && passable[east]);
            }
            foreach (var core in s.Cores)
            {
                Assert.That(s.ResourceNodes.Count(n => n.Kind == ResourceKind.Wood && DistanceMeters(s, core.Position, n.Position) <= 32), Is.GreaterThanOrEqualTo(4));
                Assert.That(s.ResourceNodes.Count(n => n.Kind == ResourceKind.Food && DistanceMeters(s, core.Position, n.Position) <= 32), Is.GreaterThanOrEqualTo(3));
            }
        }

        private static long DistanceMeters(ScenarioDefinition s, SimPoint a, SimPoint b)
        {
            long dx = a.X.Raw / 65536 - b.X.Raw / 65536, dz = a.Z.Raw / 65536 - b.Z.Raw / 65536;
            return dx * dx + dz * dz <= 0 ? 0 : (long)Math.Sqrt(dx * dx + dz * dz);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AutomaticMatchReplaysForTwentyThousandTicks(bool allCivilisations)
        {
            var scenario = MapGenerator.GenerateLarge(3, gold: allCivilisations);
            if (allCivilisations)
            {
                scenario.Economy.Forestry = true; scenario.Economy.Masonry = true; scenario.Economy.Caravan = true;
                scenario.Economy.Cavalry = true; scenario.Economy.Bridge = true; scenario.Economy.Academy = true;
                scenario.Economy.Cult = true; scenario.Economy.MonksEnabled = true; scenario.Economy.Mountain = true;
                scenario.Economy.FishingCiv = true; scenario.Economy.FishingEnabled = true;
                scenario.Economy.Tollgate = true; scenario.Economy.Metropolis = true; scenario.Economy.Sanctuary = true;
            }
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 20000, identity);
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, identity);
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }
    }
}
