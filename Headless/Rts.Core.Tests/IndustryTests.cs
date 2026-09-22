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
    /// technical-design-v3 12.2 (V3-2 PR2): a mine digs the ore point under it, a smelter makes metal from ore, and belts
    /// join them to each other and to the core. The lines are built the way a player would - place, build, lay belts -
    /// and run for thousands of ticks, far past the dig, smelt and belt clocks and the full buffers.
    /// </summary>
    public sealed class IndustryTests
    {
        private const int Width = 128, Height = 64;

        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> f, string name) => long.Parse(f[name], CultureInfo.InvariantCulture);

        private static int CellOf(SimPoint p) => (int)(p.Z.Raw / 65536 / 2) * Width + (int)(p.X.Raw / 65536 / 2);

        private static bool InsideCore(ScenarioDefinition s, int cell)
        {
            long x = (cell % Width) * 2 + 1, z = (cell / Width) * 2 + 1;
            foreach (var c in s.Cores)
            {
                long dx = x - c.Position.X.Raw / 65536, dz = z - c.Position.Z.Raw / 65536;
                if (dx * dx + dz * dz <= 16) return true;
            }
            return false;
        }

        private static int[] Footprint(int origin, int size)
        {
            var cells = new List<int>();
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++) cells.Add(origin + z * Width + x);
            return cells.ToArray();
        }

        private static int Neighbour(int cell, Facing f)
        {
            int x = cell % Width, z = cell / Width;
            return f switch
            {
                Facing.North => z + 1 < Height ? cell + Width : -1,
                Facing.East => x + 1 < Width ? cell + 1 : -1,
                Facing.South => z > 0 ? cell - Width : -1,
                _ => x > 0 ? cell - 1 : -1
            };
        }

        private static int OutputCell(int origin, int size, Facing f)
        {
            int x0 = origin % Width, z0 = origin / Width;
            var (x, z) = f switch
            {
                Facing.North => (x0 + size / 2, z0 + size),
                Facing.East => (x0 + size, z0 + size / 2),
                Facing.South => (x0 + size / 2, z0 - 1),
                _ => (x0 - 1, z0 + size / 2)
            };
            return x < 0 || z < 0 || x >= Width || z >= Height ? -1 : z * Width + x;
        }

        /// <summary>
        /// Shortest 4-neighbour run of belt cells from <paramref name="start"/> to a cell next to <paramref name="into"/>,
        /// over open cells that are not taken; each belt faces the next, the last faces into the target.
        /// </summary>
        private static (int[] cells, Facing[] facings) Route(ScenarioDefinition s, int start, Func<int, bool> into, HashSet<int> taken)
        {
            var blocked = new HashSet<int>(s.Map.BlockedCellIds);
            var nodes = new HashSet<int>(s.ResourceNodes.Select(n => CellOf(n.Position)));
            bool Open(int c) => c >= 0 && !blocked.Contains(c) && !nodes.Contains(c) && !taken.Contains(c) && !InsideCore(s, c);
            if (!Open(start)) return (null, null);
            var from = new Dictionary<int, int> { [start] = -1 };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                foreach (Facing f in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
                {
                    int next = Neighbour(cell, f);
                    if (next < 0) continue;
                    if (into(next))
                    {
                        var path = new List<int>();
                        for (int c = cell; c != -1; c = from[c]) path.Add(c);
                        path.Reverse();
                        var facings = new Facing[path.Count];
                        for (int i = 0; i + 1 < path.Count; i++)
                            facings[i] = new[] { Facing.North, Facing.East, Facing.South, Facing.West }.First(d => Neighbour(path[i], d) == path[i + 1]);
                        facings[path.Count - 1] = f;
                        return (path.ToArray(), facings);
                    }
                    if (!Open(next) || from.ContainsKey(next)) continue;
                    from[next] = cell;
                    queue.Enqueue(next);
                }
            }
            return (null, null);
        }

        private sealed class Match
        {
            public ScenarioDefinition S;
            public Battle Sim;
            public CommandGateway Gateway;
            public ulong Seq;
            public void Steps(int n) { for (int i = 0; i < n && !Sim.Capture(1).Result.HasEnded; i++) Gateway.Step(); }
            public void Send(EconomyCommand c) => Gateway.SubmitEconomy(c);
            public Dictionary<string, string> F => Fields(Sim);
        }

        /// <summary>Both automatic economies off, so only the test places and assigns anything; lots of wood.</summary>
        private static Match Start(ulong seed)
        {
            var s = MapGenerator.Generate(seed, true, true);
            s.Economy.StartWood = 1000;
            var m = new Match { S = s, Sim = new Battle(s) };
            m.Gateway = new CommandGateway(m.Sim);
            m.Send(EconomyCommand.Auto(1, ++m.Seq, false));
            m.Send(EconomyCommand.Auto(2, ++m.Seq, false));
            m.Steps(1);
            return m;
        }

        /// <summary>Places a building by trying the candidates in order; returns its id and origin.</summary>
        private static (uint id, int origin) Place(Match m, BuildingKind kind, Facing facing, IEnumerable<int> origins)
        {
            foreach (int origin in origins)
            {
                long before = Number(m.F, "Buildings.Count");
                m.Send(EconomyCommand.Place(1, ++m.Seq, kind, origin, facing));
                m.Steps(1);
                if (Number(m.F, "Buildings.Count") > before) return ((uint)(before + 1), origin);
            }
            return (0, -1);
        }

        private static void Build(Match m, uint id)
        {
            m.Send(EconomyCommand.Assign(1, ++m.Seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, id));
            for (int t = 0; t < 3000 && m.F["Buildings[" + id + "].Complete"] != "1"; t += 20) m.Steps(20);
            Assert.That(m.F["Buildings[" + id + "].Complete"], Is.EqualTo("1"), "building " + id + " finished");
        }

        private static ResourceNodeDefinition NearestOre(ScenarioDefinition s)
        {
            int core = CellOf(s.Cores[0].Position);
            return s.ResourceNodes.Where(n => n.Kind == ResourceKind.Ore)
                .OrderBy(n => Math.Abs(CellOf(n.Position) % Width - core % Width) + Math.Abs(CellOf(n.Position) / Width - core / Width)).ThenBy(n => n.Id).First();
        }

        private static IEnumerable<int> MineOrigins(ResourceNodeDefinition ore)
        {
            int c = CellOf(ore.Position);
            foreach (var (dx, dz) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) }) yield return c - dz * Width - dx;
        }

        private static IEnumerable<int> Ring(int centre, int from, int to)
        {
            int cx = centre % Width, cz = centre / Width;
            for (int r = from; r <= to; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) == r && cx + dx >= 0 && cz + dz >= 0 && cx + dx < Width - 3 && cz + dz < Height - 3)
                            yield return (cz + dz) * Width + cx + dx;
        }

        /// <summary>A mine on the ore point nearest the west core, facing a side with a belt route to <paramref name="into"/>.</summary>
        private static (uint id, int origin, int[] cells, Facing[] facings) MineWithRoute(Match m, Func<int, bool> into, HashSet<int> taken)
        {
            var ore = NearestOre(m.S);
            foreach (int origin in MineOrigins(ore))
                foreach (Facing f in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
                {
                    var foot = new HashSet<int>(taken.Concat(Footprint(origin, m.S.Economy.MineSizeCells)));
                    var (cells, facings) = Route(m.S, OutputCell(origin, m.S.Economy.MineSizeCells, f), into, foot);
                    if (cells == null) continue;
                    var (id, placed) = Place(m, BuildingKind.Mine, f, new[] { origin });
                    if (id != 0) return (id, placed, cells, facings);
                }
            return (0, -1, null, null);
        }

        [TestCase(1UL)]
        [TestCase(3UL)]
        public void AMineDigsItsOrePointAndABeltCarriesTheOreToTheCore(ulong seed)
        {
            var m = Start(seed);
            var ore = NearestOre(m.S);
            var (mine, origin, cells, facings) = MineWithRoute(m, c => InsideCore(m.S, c), new HashSet<int>());
            Assume.That(mine, Is.Not.EqualTo(0u), "a mine site with a route to the core");
            var f = m.F;
            Assert.That(f["Buildings[" + mine + "].Kind"], Is.EqualTo(((byte)BuildingKind.Mine).ToString(CultureInfo.InvariantCulture)));
            Assert.That(Number(f, "Buildings[" + mine + "].NodeId"), Is.EqualTo(ore.Id));
            Assert.That(Number(f, "Economy[1].Wood"), Is.EqualTo(1000 - m.S.Economy.MineWoodCost));
            Build(m, mine);

            // No belt yet: the mine fills its output and stops there.
            m.Steps(m.S.Economy.MineIntervalTicks * (m.S.Economy.BufferLimit + 5));
            f = m.F;
            Assert.That(Number(f, "Buildings[" + mine + "].Output"), Is.EqualTo(m.S.Economy.BufferLimit));
            Assert.That(Number(f, "ResourceNodes[" + ore.Id + "].Remaining"), Is.EqualTo(ore.Amount - m.S.Economy.BufferLimit));

            m.Send(EconomyCommand.PlaceBelt(1, ++m.Seq, cells, facings));
            m.Steps(3000);
            f = m.F;
            long dug = ore.Amount - Number(f, "ResourceNodes[" + ore.Id + "].Remaining");
            long onBelts = cells.Count(c => f["Belts[" + c + "].Item"] != "0");
            Assert.That(dug, Is.EqualTo(Number(f, "Buildings[" + mine + "].Output") + onBelts + Number(f, "Economy[1].Ore")), "every dug ore is somewhere");
            // The belt is faster than the mine, so the mine never waits: one ore per interval once it runs.
            Assert.That(Number(f, "Economy[1].Ore"), Is.GreaterThan(3000 / m.S.Economy.MineIntervalTicks - cells.Length - m.S.Economy.BufferLimit));
        }

        [TestCase(1UL)]
        [TestCase(6UL)]
        public void OreBecomesMetalOnTheWayMineToSmelterToCoreAndTheMatchReplays(ulong seed)
        {
            var m = Start(seed);
            int core = CellOf(m.S.Cores[0].Position);
            int smelterSize = m.S.Economy.SmelterSizeCells;
            // The smelter first, near the core, facing a side with a route into the core.
            uint smelter = 0; int smelterOrigin = -1; int[] toCore = null; Facing[] toCoreFacings = null;
            foreach (int origin in Ring(core, 5, 12))
            {
                foreach (Facing f in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
                {
                    var (cells, facings) = Route(m.S, OutputCell(origin, smelterSize, f), c => InsideCore(m.S, c), new HashSet<int>(Footprint(origin, smelterSize)));
                    if (cells == null) continue;
                    var (id, placed) = Place(m, BuildingKind.Smelter, f, new[] { origin });
                    if (id != 0) { smelter = id; smelterOrigin = placed; toCore = cells; toCoreFacings = facings; }
                    break; // a failed site fails for every facing
                }
                if (smelter != 0) break;
            }
            Assume.That(smelter, Is.Not.EqualTo(0u), "a smelter site near the core");
            var smelterCells = new HashSet<int>(Footprint(smelterOrigin, smelterSize));
            var taken = new HashSet<int>(smelterCells.Concat(toCore));
            var (mine, _, toSmelter, toSmelterFacings) = MineWithRoute(m, c => smelterCells.Contains(c), taken);
            Assume.That(mine, Is.Not.EqualTo(0u), "a mine with a route to the smelter");
            Build(m, smelter);
            Build(m, mine);
            m.Send(EconomyCommand.PlaceBelt(1, ++m.Seq, toSmelter, toSmelterFacings));
            m.Send(EconomyCommand.PlaceBelt(1, ++m.Seq, toCore, toCoreFacings));
            var rules = m.S.Economy;
            var ore = NearestOre(m.S);
            for (int k = 0; k < 40; k++)
            {
                m.Steps(100);
                var f = m.F;
                long dug = ore.Amount - Number(f, "ResourceNodes[" + ore.Id + "].Remaining");
                long oreOnBelts = 0, metalOnBelts = 0;
                foreach (int c in toSmelter.Concat(toCore))
                {
                    string item = f["Belts[" + c + "].Item"];
                    if (item == ((byte)ResourceKind.Ore).ToString(CultureInfo.InvariantCulture)) oreOnBelts++;
                    else if (item == ((byte)ResourceKind.Metal).ToString(CultureInfo.InvariantCulture)) metalOnBelts++;
                }
                long smelting = Number(f, "Buildings[" + smelter + "].Timer") > 0 ? 1 : 0;
                long metal = Number(f, "Economy[1].Metal") + metalOnBelts + Number(f, "Buildings[" + smelter + "].Output") + smelting;
                long oreLeft = Number(f, "Buildings[" + mine + "].Output") + oreOnBelts + Number(f, "Buildings[" + smelter + "].Input") + Number(f, "Economy[1].Ore");
                Assert.That(dug, Is.EqualTo(oreLeft + metal * rules.OrePerMetal), "ore is conserved, after " + (k + 1) * 100 + " ticks");
            }
            var end = m.F;
            Assert.That(Number(end, "Economy[1].Metal"), Is.GreaterThanOrEqualTo(20), "metal reaches the core");
            Assert.That(Number(end, "Economy[1].Ore"), Is.EqualTo(0), "all ore went to the smelter");

            var inputs = m.Gateway.Inputs;
            foreach (var input in inputs.Where(i => i.Kind == InputKind.Economy))
            {
                var copy = InputBinary.Decode(InputBinary.Encode(input));
                Assert.That(InputBinary.Encode(copy), Is.EqualTo(InputBinary.Encode(input)));
                Assert.That(copy.Economy.Facing, Is.EqualTo(input.Economy.Facing));
            }
            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                ReplayRunner.Record(stream, m.S, inputs, m.Sim.Capture(1).Tick, build);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, build);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        [Test]
        public void MinesNeedOreSmeltersTrainNothingAndNobodyWalksToACoveredPoint()
        {
            var m = Start(2);
            var food = m.S.ResourceNodes.First(n => n.Kind == ResourceKind.Food);
            long wood = Number(m.F, "Economy[1].Wood");
            // On a food point, and on bare ground next to the core: no mine.
            var (none, _) = Place(m, BuildingKind.Mine, Facing.North, MineOrigins(food).Concat(Ring(CellOf(m.S.Cores[0].Position), 5, 6)));
            Assert.That(none, Is.EqualTo(0u));
            Assert.That(Number(m.F, "Economy[1].Wood"), Is.EqualTo(wood), "a refused site costs nothing");

            var ore = NearestOre(m.S);
            var (mine, _) = Place(m, BuildingKind.Mine, Facing.North, MineOrigins(ore));
            Assume.That(mine, Is.Not.EqualTo(0u));
            m.Send(EconomyCommand.Assign(1, ++m.Seq, new uint[] { 1 }, EconomyTargetKind.ResourceNode, ore.Id));
            m.Steps(1);
            Assert.That(Number(m.F, "Villagers[1].NodeId"), Is.Not.EqualTo(ore.Id), "the covered point cannot be assigned");

            Build(m, mine);
            m.Send(EconomyCommand.Train(1, ++m.Seq, mine, UnitKind.Infantry));
            m.Steps(1);
            Assert.That(Number(m.F, "Buildings[" + mine + "].Queued"), Is.EqualTo(0), "only a barracks trains soldiers");
        }

        /// <summary>The same mine and smelter, built by hand; the caller lays belts or sends carriers.</summary>
        private static (Match m, uint mine, uint smelter, int[] toSmelter, Facing[] toSmelterFacings, int[] toCore, Facing[] toCoreFacings) Workshop(ulong seed)
        {
            var m = Start(seed);
            int core = CellOf(m.S.Cores[0].Position);
            int size = m.S.Economy.SmelterSizeCells;
            foreach (int origin in Ring(core, 5, 12))
                foreach (Facing f in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
                {
                    var (toCore, toCoreFacings) = Route(m.S, OutputCell(origin, size, f), c => InsideCore(m.S, c), new HashSet<int>(Footprint(origin, size)));
                    if (toCore == null) continue;
                    var (smelter, placed) = Place(m, BuildingKind.Smelter, f, new[] { origin });
                    if (smelter == 0) break;
                    var smelterCells = new HashSet<int>(Footprint(placed, size));
                    var (mine, _, toSmelter, toSmelterFacings) = MineWithRoute(m, c => smelterCells.Contains(c), new HashSet<int>(smelterCells.Concat(toCore)));
                    if (mine == 0) return (null, 0, 0, null, null, null, null);
                    Build(m, smelter);
                    Build(m, mine);
                    return (m, mine, smelter, toSmelter, toSmelterFacings, toCore, toCoreFacings);
                }
            return (null, 0, 0, null, null, null, null);
        }

        /// <summary>
        /// Gate 1 of V3-2 (10): with the same mine, smelter and villagers, a line brings more metal to the core than two
        /// villagers carrying by hand (one mine to smelter, one smelter to core), over the same long stretch.
        /// </summary>
        [TestCase(1UL)]
        [TestCase(6UL)]
        public void ALineBringsMoreMetalThanCarryingByHand(ulong seed)
        {
            const int Ticks = 8000;
            var line = Workshop(seed);
            Assume.That(line.m, Is.Not.Null);
            line.m.Send(EconomyCommand.PlaceBelt(1, ++line.m.Seq, line.toSmelter, line.toSmelterFacings));
            line.m.Send(EconomyCommand.PlaceBelt(1, ++line.m.Seq, line.toCore, line.toCoreFacings));
            long lineStart = Number(line.m.F, "Economy[1].Metal");
            line.m.Steps(Ticks);
            long byLine = Number(line.m.F, "Economy[1].Metal") - lineStart;

            var hand = Workshop(seed);
            hand.m.Send(EconomyCommand.Assign(1, ++hand.m.Seq, new uint[] { 1 }, EconomyTargetKind.Building, hand.mine));
            hand.m.Send(EconomyCommand.Assign(1, ++hand.m.Seq, new uint[] { 2 }, EconomyTargetKind.Building, hand.smelter));
            long handStart = Number(hand.m.F, "Economy[1].Metal");
            hand.m.Steps(Ticks);
            long byHand = Number(hand.m.F, "Economy[1].Metal") - handStart;

            TestContext.WriteLine("seed " + seed + ": metal in " + Ticks + " ticks, line " + byLine + ", by hand " + byHand
                + " (belts " + (line.toSmelter.Length + line.toCore.Length) + ")");
            Assert.That(byHand, Is.GreaterThan(0), "carrying by hand works at all");
            Assert.That(byLine, Is.GreaterThan(byHand), "the line is faster");
        }

        /// <summary>13: left alone, each side builds a mine, a smelter and the belts between them, and metal reaches its core.</summary>
        [TestCase(1UL)]
        [TestCase(2UL)]
        [TestCase(5UL)]
        public void TheAutomaticEconomyBuildsTheLineAndUsesTheMetal(ulong seed)
        {
            var s = MapGenerator.Generate(seed, true, true);
            var sim = new Battle(s);
            var lineTick = new long[2];
            for (long t = 1; t <= 15000 && !sim.Capture(1).Result.HasEnded; t++)
            {
                sim.Step(t, Array.Empty<ScheduledInput>());
                if (t % 100 != 0) continue;
                var f = Fields(sim);
                for (int side = 0; side < 2; side++)
                {
                    string faction = (side + 1).ToString(CultureInfo.InvariantCulture);
                    if (lineTick[side] == 0 && f.Keys.Any(k => k.StartsWith("Belts[", StringComparison.Ordinal) && k.EndsWith("].Item", StringComparison.Ordinal)
                        && f[k] == ((byte)ResourceKind.Metal).ToString(CultureInfo.InvariantCulture)
                        && f[k.Replace("].Item", "].FactionId")] == faction)) lineTick[side] = t;
                }
            }
            var end = Fields(sim);
            TestContext.WriteLine("seed " + seed + ": first metal on a belt at tick west " + lineTick[0] + ", east " + lineTick[1]
                + "; ended " + end["Result.HasEnded"] + " at " + end["Tick"]);
            for (int side = 0; side < 2; side++) Assert.That(lineTick[side], Is.GreaterThan(0), "side " + (side + 1) + " moved metal on its own belts");
            // Infantry costs metal on this map, so every soldier past the starting 40 was paid for by a line.
            Assert.That(Number(end, "NextSoldierId"), Is.GreaterThan(s.Soldiers.Length + 1), "metal became infantry");
        }

        [Test]
        public void AVerThreeOneMapPlacesNoMine()
        {
            var s = MapGenerator.Generate(1, true);
            s.Economy.StartWood = 1000;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Place(1, 1, BuildingKind.Smelter, CellOf(s.Cores[0].Position) + 8, Facing.East));
            gateway.Step();
            Assert.That(Number(Fields(sim), "Buildings.Count"), Is.EqualTo(0), "industry buildings need an industry map");
            Assert.That(Fields(sim).Keys.Any(k => k.EndsWith("].Facing", StringComparison.Ordinal)), Is.False);
        }
    }
}
