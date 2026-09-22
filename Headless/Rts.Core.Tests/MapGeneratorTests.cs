using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>technical-design-v3 2: the random map is deterministic, fair in the V3-1 sense, and connected.</summary>
    public sealed class MapGeneratorTests
    {
        private static readonly ulong[] Seeds = Enumerable.Range(0, 40).Select(i => (ulong)i * 7919UL + 1).ToArray();

        private static int Cell(ScenarioDefinition s, SimPoint p) =>
            (int)(p.Z.Raw / 65536 / s.Map.CellSizeMeters) * s.Map.WidthCells + (int)(p.X.Raw / 65536 / s.Map.CellSizeMeters);

        private static bool[] Blocked(ScenarioDefinition s)
        {
            var blocked = new bool[s.Map.WidthCells * s.Map.HeightCells];
            foreach (int id in s.Map.BlockedCellIds) blocked[id] = true;
            return blocked;
        }

        [Test]
        public void TheSameSeedAlwaysGivesTheSameBytesAndDifferentSeedsDiffer()
        {
            var a = ScenarioBinary.Encode(MapGenerator.Generate(12345));
            var b = ScenarioBinary.Encode(MapGenerator.Generate(12345));
            var c = ScenarioBinary.Encode(MapGenerator.Generate(12346));
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.Not.EqualTo(c));
        }

        // Pins what seed 1 produces under mapgen-1. Two machines must build the same map from a seed before an online
        // match (2.1). If this changes on purpose, raise MapGenerator.Version and update the value.
        [Test]
        public void SeedOneUnderMapgenOneIsPinned()
        {
            var hash = SHA256.HashData(ScenarioBinary.Encode(MapGenerator.Generate(1)));
            Assert.That(System.Convert.ToHexString(hash).ToLowerInvariant(), Is.EqualTo(PinnedSeedOneHash));
        }

        private const string PinnedSeedOneHash = "78cc1966cb880208cb8de357d8da86b4e5541b9dbe327e0325c5dc3429d7277b";

        [TestCaseSource(nameof(Seeds))]
        public void EveryCoreHasTheGuaranteedResourcesNearby(ulong seed)
        {
            var s = MapGenerator.Generate(seed);
            foreach (var core in s.Cores)
            {
                int wood = 0, food = 0;
                foreach (var n in s.ResourceNodes)
                {
                    long dx = (n.Position.X.Raw - core.Position.X.Raw) / 65536, dz = (n.Position.Z.Raw - core.Position.Z.Raw) / 65536;
                    if (dx * dx + dz * dz > 32 * 32) continue; // 30 m ring plus the snap to a cell centre
                    if (n.Kind == ResourceKind.Wood) wood++; else food++;
                }
                Assert.That(wood, Is.GreaterThanOrEqualTo(4), "wood near core " + core.Id);
                Assert.That(food, Is.GreaterThanOrEqualTo(3), "food near core " + core.Id);
            }
        }

        [TestCaseSource(nameof(Seeds))]
        public void EverythingThatMattersIsReachableFromTheWestCore(ulong seed)
        {
            var s = MapGenerator.Generate(seed);
            var reachable = MapGenerator.Reachable(Blocked(s), Cell(s, s.Cores[0].Position));
            Assert.That(reachable[Cell(s, s.Cores[1].Position)], "east core");
            foreach (var o in s.Outposts) Assert.That(reachable[Cell(s, o.Position)], "outpost " + o.Id);
            foreach (var n in s.ResourceNodes) Assert.That(reachable[Cell(s, n.Position)], "resource " + n.Id);
            foreach (var d in s.Soldiers) Assert.That(reachable[Cell(s, d.Position)], "soldier " + d.Id);
        }

        [TestCaseSource(nameof(Seeds))]
        public void TheLayoutKeepsTheShapeTheVerOneAiExpects(ulong seed)
        {
            var s = MapGenerator.Generate(seed);
            Assert.That(s.Map.BlockedCellIds.Length * 1000 / (s.Map.WidthCells * s.Map.HeightCells), Is.LessThanOrEqualTo(200));
            Assert.That(s.Outposts, Has.Length.EqualTo(2));
            Assert.That(s.Outposts[0].Position.Z.Raw, Is.GreaterThanOrEqualTo(s.Outposts[1].Position.Z.Raw), "outpost 1 is north");
            long dx = (s.Cores[1].Position.X.Raw - s.Cores[0].Position.X.Raw) / 65536, dz = (s.Cores[1].Position.Z.Raw - s.Cores[0].Position.Z.Raw) / 65536;
            Assert.That(dx * dx + dz * dz, Is.GreaterThanOrEqualTo(150L * 150));
            Assert.That(s.ResourceNodes.Select(n => Cell(s, n.Position)).Distinct().Count(), Is.EqualTo(s.ResourceNodes.Length));
            Assert.That(s.ResourceNodes.Count(n => n.Kind == ResourceKind.Wood), Is.EqualTo(4 * 2 + 16));
            Assert.That(s.ResourceNodes.Count(n => n.Kind == ResourceKind.Food), Is.EqualTo(3 * 2 + 10));
            Assert.That(s.Economy.Enabled, Is.False);
        }

        [Test]
        public void TheBinaryRoundTripsAndOnlyGeneratedMapsUseSchemaThree()
        {
            var generated = ScenarioBinary.Encode(MapGenerator.Generate(99));
            Assert.That(System.BitConverter.ToInt32(generated, 0), Is.EqualTo(3));
            Assert.That(ScenarioBinary.Encode(ScenarioBinary.Decode(generated)), Is.EqualTo(generated));
            // Ver.1 scenarios keep their old bytes, and so their Config.Hash.
            Assert.That(System.BitConverter.ToInt32(ScenarioBinary.Encode(WeekTwoScenario.Create()), 0), Is.EqualTo(1));
            Assert.That(System.BitConverter.ToInt32(ScenarioBinary.Encode(WeekOneScenario.Create()), 0), Is.EqualTo(1));
        }

        [Test]
        public void TwoResourcesInOneCellAreRejected()
        {
            var s = MapGenerator.Generate(5);
            s.ResourceNodes[1].Position = s.ResourceNodes[0].Position;
            Assert.Throws<System.ArgumentException>(() => new Rts.Simulation.Simulation(s));
        }

        // The Ver.1 automatic AI plays on the generated ground without faulting. Long enough to cross the allocation,
        // pursuit and capture timers (20, 60, 200 ticks) many times over.
        [TestCase(1UL)]
        [TestCase(2UL)]
        [TestCase(3UL)]
        public void TheAutomaticAiPlaysAGeneratedMapWithoutFaulting(ulong seed)
        {
            var sim = new Rts.Simulation.Simulation(MapGenerator.Generate(seed));
            for (long t = 1; t <= 3000 && !sim.Capture(1).Result.HasEnded; t++)
            {
                sim.Step(t, System.Array.Empty<ScheduledInput>());
                Assert.That(sim.Capture(1).Result.IsFault, Is.False, "fault at tick " + t);
            }
        }
    }
}
