using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <summary>
    /// technical-design-v3 11 (V3-2 PR1): belts carry single items, one per cell, BeltTicksPerCell ticks per cell,
    /// downstream first. The runs are many cell times long, so jams, merges and loops are seen after they settle.
    /// </summary>
    public sealed class BeltTests
    {
        private const int Width = 128;

        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> f, string name) => long.Parse(f[name], CultureInfo.InvariantCulture);

        private static void Run(Battle sim, long from, long to, Action<long> each = null)
        {
            for (long t = from; t <= to && !sim.Capture(1).Result.HasEnded; t++)
            {
                sim.Step(t, Array.Empty<ScheduledInput>());
                each?.Invoke(t);
            }
        }

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static int CellOf(SimPoint p) => (int)(p.Z.Raw / 65536 / 2) * Width + (int)(p.X.Raw / 65536 / 2);

        private static bool Free(ScenarioDefinition s, int x, int z)
        {
            if (x < 0 || z < 0 || x >= Width || z >= 64) return false;
            int cell = z * Width + x;
            return !s.Map.BlockedCellIds.Contains(cell) && s.ResourceNodes.All(n => CellOf(n.Position) != cell);
        }

        private static Facing Opposite(Facing f) => (Facing)(((int)f + 2) % 4);

        private static (int dx, int dz) Step(Facing f) => f switch
        {
            Facing.North => (0, 1), Facing.East => (1, 0), Facing.South => (0, -1), _ => (-1, 0)
        };

        /// <summary>
        /// A straight run of free cells leading away from the west core, starting 3 cells out (just outside its 4 m
        /// radius). Returned nearest the core first; a belt facing <c>toward</c> the core carries into it.
        /// </summary>
        private static (int[] cells, Facing toward) LineFromWestCore(ScenarioDefinition s, int length)
        {
            int core = CellOf(s.Cores[0].Position), cx = core % Width, cz = core / Width;
            foreach (var away in new[] { Facing.East, Facing.North, Facing.South, Facing.West })
            {
                var (dx, dz) = Step(away);
                var cells = new int[length];
                bool ok = true;
                for (int i = 0; i < length && ok; i++)
                {
                    int x = cx + dx * (3 + i), z = cz + dz * (3 + i);
                    ok = Free(s, x, z);
                    cells[i] = z * Width + x;
                }
                if (ok) return (cells, Opposite(away));
            }
            throw new InvalidOperationException("no free line next to the west core");
        }

        private static ScenarioDefinition Industry(ulong seed) => MapGenerator.Generate(seed, true, true);

        private static int ItemsOnBelts(Dictionary<string, string> f)
            => f.Count(p => p.Key.StartsWith("Belts[", StringComparison.Ordinal) && p.Key.EndsWith("].Item", StringComparison.Ordinal) && p.Value != "0");

        [TestCase(1UL)]
        [TestCase(4UL)]
        public void AFullLineDeliversOneItemPerCellTimeWithoutGaps(ulong seed)
        {
            var s = Industry(seed);
            var (cells, toward) = LineFromWestCore(s, 6);
            s.Belts = cells.Select(c => new BeltDefinition { Cell = c, FactionId = 1, Facing = toward, Item = ResourceKind.Ore }).ToArray();
            int perCell = s.Economy.BeltTicksPerCell;
            var sim = new Battle(s);
            var delivered = new Dictionary<long, long>();
            Run(sim, 1, perCell * 6 + 20, t => delivered[t] = Number(Fields(sim), "Economy[1].Ore"));
            Assert.That(delivered[perCell - 1], Is.EqualTo(0), "the first item waits a full cell time");
            // A packed line moves as one: one item reaches the core every cell time, no gaps open behind the head.
            for (int k = 1; k <= 6; k++)
            {
                Assert.That(delivered[perCell * k], Is.EqualTo(k), "after " + k + " cell times");
                if (k < 6) Assert.That(delivered[perCell * k + perCell - 1], Is.EqualTo(k), "not faster than one per cell time");
            }
            Assert.That(ItemsOnBelts(Fields(sim)), Is.EqualTo(0));
        }

        [Test]
        public void ALineThatEndsOnBareGroundJamsAndLosesNothing()
        {
            var s = Industry(2);
            var (cells, toward) = LineFromWestCore(s, 6);
            // Facing away from the core: the last cell hands to bare ground, which takes nothing.
            var away = Opposite(toward);
            s.Belts = cells.Select((c, i) => new BeltDefinition { Cell = c, FactionId = 1, Facing = away, Item = i < 3 ? ResourceKind.Ore : 0 }).ToArray();
            var sim = new Battle(s);
            Run(sim, 1, 400);
            var f = Fields(sim);
            Assert.That(ItemsOnBelts(f), Is.EqualTo(3));
            Assert.That(Number(f, "Economy[1].Ore"), Is.EqualTo(0));
            for (int i = 0; i < 6; i++)
                Assert.That(f["Belts[" + cells[i] + "].Item"], Is.EqualTo(i >= 3 ? ((byte)ResourceKind.Ore).ToString(CultureInfo.InvariantCulture) : "0"),
                    "the three items packed at the far end, cell " + i);
        }

        [Test]
        public void TwoLinesMergeAndEveryItemIsSomewhereEveryTick()
        {
            var s = Industry(3);
            var (cells, toward) = LineFromWestCore(s, 6);
            var (dx, dz) = Step(toward);
            // A side line of three cells joining the main line at its fourth cell, from one side.
            int join = cells[3], jx = join % Width, jz = join / Width;
            int sx = dz, sz = dx; // perpendicular to the main line
            var side = new List<BeltDefinition>();
            for (int flip = 0; flip < 2 && side.Count < 3; flip++, sx = -sx, sz = -sz)
            {
                side.Clear();
                Facing intoMain = sx == 1 ? Facing.West : sx == -1 ? Facing.East : sz == 1 ? Facing.South : Facing.North;
                for (int i = 1; i <= 3; i++)
                {
                    int x = jx + sx * i, z = jz + sz * i;
                    if (!Free(s, x, z)) break;
                    side.Add(new BeltDefinition { Cell = z * Width + x, FactionId = 1, Facing = intoMain, Item = ResourceKind.Metal });
                }
            }
            Assume.That(side.Count, Is.EqualTo(3));
            s.Belts = cells.Select(c => new BeltDefinition { Cell = c, FactionId = 1, Facing = toward, Item = ResourceKind.Ore }).Concat(side).ToArray();
            var sim = new Battle(s);
            Run(sim, 1, 400, t =>
            {
                var f = Fields(sim);
                Assert.That(ItemsOnBelts(f) + Number(f, "Economy[1].Ore") + Number(f, "Economy[1].Metal"), Is.EqualTo(9), "tick " + t);
            });
            var end = Fields(sim);
            Assert.That(Number(end, "Economy[1].Ore"), Is.EqualTo(6));
            Assert.That(Number(end, "Economy[1].Metal"), Is.EqualTo(3), "the side line gets in once the main line has room");
        }

        [Test]
        public void ALoopKeepsItsItemsGoingRound()
        {
            var s = Industry(5);
            var (cells, toward) = LineFromWestCore(s, 6);
            // A 2x2 loop on the first free square next to the line's far end.
            int x0 = cells[4] % Width, z0 = cells[4] / Width;
            int[] loop = null;
            foreach (var (ox, oz) in new[] { (0, 1), (0, -2), (1, 0), (-2, 0) })
            {
                int x = x0 + ox, z = z0 + oz;
                if (Free(s, x, z) && Free(s, x + 1, z) && Free(s, x, z + 1) && Free(s, x + 1, z + 1)) { loop = new[] { z * Width + x, z * Width + x + 1, (z + 1) * Width + x + 1, (z + 1) * Width + x }; break; }
            }
            Assume.That(loop, Is.Not.Null);
            // Lower-left east, lower-right north, upper-right west, upper-left south: round and round.
            var facings = new[] { Facing.East, Facing.North, Facing.West, Facing.South };
            s.Belts = loop.Select((c, i) => new BeltDefinition { Cell = c, FactionId = 1, Facing = facings[i], Item = i < 3 ? ResourceKind.Ore : 0 }).ToArray();
            var sim = new Battle(s);
            var seen = new HashSet<string>();
            Run(sim, 1, 300, t =>
            {
                var f = Fields(sim);
                Assert.That(ItemsOnBelts(f), Is.EqualTo(3), "tick " + t);
                seen.Add(string.Join(",", loop.Select(c => f["Belts[" + c + "].Item"])));
            });
            Assert.That(seen.Count, Is.GreaterThan(1), "the items move around the loop");
        }

        [Test]
        public void PlacingABeltRunChecksEveryCellAndRemovingTakesOnlyTheOwn()
        {
            var s = Industry(1);
            s.Economy.StartWood = 4;
            var (cells, toward) = LineFromWestCore(s, 4);
            int core = CellOf(s.Cores[0].Position);
            int blocked = s.Map.BlockedCellIds[0], node = CellOf(s.ResourceNodes[0].Position);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong seq = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++seq, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++seq, false));
            var run = new[] { cells[0], blocked, node, core, cells[1], cells[1], -1, cells[2] };
            gateway.SubmitEconomy(EconomyCommand.PlaceBelt(1, ++seq, run, run.Select(_ => toward).ToArray()));
            Steps(gateway, sim, 1);
            var f = Fields(sim);
            Assert.That(Number(f, "Belts.Count"), Is.EqualTo(3), "the obstacle, the resource point, the core, the repeat and the off-map cell are skipped");
            Assert.That(Number(f, "Economy[1].Wood"), Is.EqualTo(4 - 3 * s.Economy.BeltWoodCost));
            foreach (int c in new[] { cells[0], cells[1], cells[2] }) Assert.That(f["Belts[" + c + "].FactionId"], Is.EqualTo("1"));

            gateway.SubmitEconomy(EconomyCommand.RemoveBelt(2, ++seq, cells[0]));  // not the east's belt
            gateway.SubmitEconomy(EconomyCommand.RemoveBelt(1, ++seq, cells[1]));
            Steps(gateway, sim, 1);
            f = Fields(sim);
            Assert.That(Number(f, "Belts.Count"), Is.EqualTo(2));
            Assert.That(f.ContainsKey("Belts[" + cells[1] + "].FactionId"), Is.False);

            // One wood left: the run pays for its first cell and stops at the second.
            gateway.SubmitEconomy(EconomyCommand.PlaceBelt(1, ++seq, new[] { cells[1], cells[3] }, new[] { toward, toward }));
            Steps(gateway, sim, 1);
            f = Fields(sim);
            Assert.That(Number(f, "Economy[1].Wood"), Is.EqualTo(0));
            Assert.That(Number(f, "Belts.Count"), Is.EqualTo(3));
            Assert.That(f.ContainsKey("Belts[" + cells[3] + "].FactionId"), Is.False);
        }

        [Test]
        public void AVillagerCarriesOreByHandIntoTheOreStock()
        {
            var s = Industry(1);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            int core = CellOf(s.Cores[0].Position);
            var ore = s.ResourceNodes.Where(n => n.Kind == ResourceKind.Ore)
                .OrderBy(n => Math.Abs(CellOf(n.Position) % Width - core % Width) + Math.Abs(CellOf(n.Position) / Width - core / Width)).First();
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, 2, new uint[] { 1 }, EconomyTargetKind.ResourceNode, ore.Id));
            Steps(gateway, sim, 2000);
            var f = Fields(sim);
            Assert.That(Number(f, "Economy[1].Ore"), Is.GreaterThanOrEqualTo(s.Economy.CarryCapacity), "at least one full load arrived");
            Assert.That(Number(f, "ResourceNodes[" + ore.Id + "].Remaining"), Is.LessThan(ore.Amount));
        }

        [Test]
        public void AMatchWithBeltsReplaysTickForTick()
        {
            var s = Industry(6);
            var (cells, toward) = LineFromWestCore(s, 8);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            Steps(gateway, sim, 5);
            gateway.SubmitEconomy(EconomyCommand.PlaceBelt(1, 1, cells, cells.Select(_ => toward).ToArray()));
            Steps(gateway, sim, 300);
            gateway.SubmitEconomy(EconomyCommand.RemoveBelt(1, 2, cells[3]));
            Steps(gateway, sim, 1500);
            var inputs = gateway.Inputs;
            foreach (var input in inputs.Where(i => i.Kind == InputKind.Economy))
            {
                var copy = InputBinary.Decode(InputBinary.Encode(input));
                Assert.That(InputBinary.Encode(copy), Is.EqualTo(InputBinary.Encode(input)));
                Assert.That(copy.Economy.Cells, Is.EqualTo(input.Economy.Cells));
                Assert.That(copy.Economy.Facings, Is.EqualTo(input.Economy.Facings));
            }
            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                ReplayRunner.Record(stream, s, inputs, sim.Capture(1).Tick, build);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, build);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        [Test]
        public void TheIndustryScenarioRoundTripsAndAVerThreeOneMapIsUntouched()
        {
            var s = Industry(3);
            var (cells, toward) = LineFromWestCore(s, 3);
            s.Belts = cells.Select(c => new BeltDefinition { Cell = c, FactionId = 1, Facing = toward, Item = ResourceKind.Ore }).ToArray();
            var bytes = ScenarioBinary.Encode(s);
            Assert.That(BitConverter.ToInt32(bytes, 0), Is.EqualTo(4));
            Assert.That(ScenarioBinary.Encode(ScenarioBinary.Decode(bytes)), Is.EqualTo(bytes));

            var plain = MapGenerator.Generate(3, true);
            Assert.That(BitConverter.ToInt32(ScenarioBinary.Encode(plain), 0), Is.EqualTo(3), "a V3-1 economy map keeps schema 3");
            var f = Fields(new Battle(plain));
            Assert.That(f.Keys.Any(k => k.StartsWith("Belts", StringComparison.Ordinal) || k.EndsWith("].Ore", StringComparison.Ordinal)), Is.False,
                "a V3-1 economy writes no industry state");
            // Belts and ore need industry.
            plain.Belts = s.Belts;
            Assert.Throws<ArgumentException>(() => new Battle(plain));
        }

        [TestCase(1UL)]
        [TestCase(2UL)]
        [TestCase(8UL)]
        public void MapgenTwoIsMapgenOnePlusOre(ulong seed)
        {
            var one = MapGenerator.Generate(seed, true);
            var two = MapGenerator.Generate(seed, true, true);
            Assert.That(ScenarioBinary.Encode(MapGenerator.Generate(seed, true, true)), Is.EqualTo(ScenarioBinary.Encode(two)), "same seed, same bytes");
            Assert.That(two.Map.BlockedCellIds, Is.EqualTo(one.Map.BlockedCellIds));
            Assert.That(two.Cores.Select(c => c.Position), Is.EqualTo(one.Cores.Select(c => c.Position)));
            Assert.That(two.ResourceNodes.Take(one.ResourceNodes.Length).Select(n => (n.Kind, n.Position, n.Amount)),
                Is.EqualTo(one.ResourceNodes.Select(n => (n.Kind, n.Position, n.Amount))));
            var ore = two.ResourceNodes.Skip(one.ResourceNodes.Length).ToArray();
            Assert.That(ore.Length, Is.EqualTo(2 * 2 + 6));
            Assert.That(ore.All(n => n.Kind == ResourceKind.Ore));
            var blocked = new bool[Width * 64];
            foreach (int c in two.Map.BlockedCellIds) blocked[c] = true;
            var reach = MapGenerator.Reachable(blocked, CellOf(two.Cores[0].Position));
            Assert.That(ore.All(n => reach[CellOf(n.Position)]), "every ore point can be walked to");
            foreach (var core in two.Cores)
            {
                int near = ore.Count(n =>
                {
                    long dx = (n.Position.X.Raw - core.Position.X.Raw) / 65536, dz = (n.Position.Z.Raw - core.Position.Z.Raw) / 65536;
                    return dx * dx + dz * dz <= 36 * 36;
                });
                Assert.That(near, Is.GreaterThanOrEqualTo(2), "two guaranteed ore points near each core");
            }
            new Battle(two); // valid as a scenario
        }
    }
}
