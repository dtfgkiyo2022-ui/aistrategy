using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>V3-6: the opt-in processing branch must be deterministic and must not perturb old maps.</summary>
    public sealed class ProcessingChainTests
    {
        private const int Width = 128, Height = 64;

        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> fields, string name)
            => long.Parse(fields[name], CultureInfo.InvariantCulture);

        private sealed class ProcessingMatch
        {
            public ScenarioDefinition Scenario;
            public Battle Simulation;
            public CommandGateway Gateway;
            public ulong Sequence;
            public Dictionary<string, string> State => Fields(Simulation);
            public void Steps(int count)
            {
                for (int i = 0; i < count && !Simulation.Capture(1).Result.HasEnded; i++) Gateway.Step();
            }
            public void Send(EconomyCommand command) => Gateway.SubmitEconomy(command);
        }

        private static int CellOf(SimPoint p)
            => (int)(p.Z.Raw / 65536 / 2) * Width + (int)(p.X.Raw / 65536 / 2);

        private static int[] Footprint(int origin, int size)
        {
            var result = new int[size * size];
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++) result[z * size + x] = origin + z * Width + x;
            return result;
        }

        private static int Neighbour(int cell, Facing facing)
        {
            int x = cell % Width, z = cell / Width;
            return facing == Facing.North ? (z + 1 < Height ? cell + Width : -1)
                : facing == Facing.East ? (x + 1 < Width ? cell + 1 : -1)
                : facing == Facing.South ? (z > 0 ? cell - Width : -1)
                : (x > 0 ? cell - 1 : -1);
        }

        private static int OutputCell(int origin, int size, Facing facing)
        {
            int x0 = origin % Width, z0 = origin / Width;
            int x = facing == Facing.North ? x0 + size / 2 : facing == Facing.East ? x0 + size : facing == Facing.South ? x0 + size / 2 : x0 - 1;
            int z = facing == Facing.North ? z0 + size : facing == Facing.East ? z0 + size / 2 : facing == Facing.South ? z0 - 1 : z0 + size / 2;
            return x < 0 || z < 0 || x >= Width || z >= Height ? -1 : z * Width + x;
        }

        private static bool InsideCore(ScenarioDefinition scenario, int cell)
        {
            long x = (cell % Width) * 2 + 1, z = (cell / Width) * 2 + 1;
            foreach (var core in scenario.Cores)
            {
                long dx = x - core.Position.X.Raw / 65536, dz = z - core.Position.Z.Raw / 65536;
                if (dx * dx + dz * dz <= 16) return true;
            }
            return false;
        }

        private static IEnumerable<int> Ring(int centre, int from, int to)
        {
            int cx = centre % Width, cz = centre / Width;
            for (int r = from; r <= to; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) == r && cx + dx >= 0 && cz + dz >= 0 && cx + dx < Width - 8 && cz + dz < Height - 8)
                            yield return (cz + dz) * Width + cx + dx;
        }

        private static (int[] cells, Facing[] facings) Route(ScenarioDefinition scenario, int start, Func<int, bool> into, HashSet<int> taken)
        {
            var blocked = new HashSet<int>(scenario.Map.BlockedCellIds);
            var nodes = new HashSet<int>(scenario.ResourceNodes.Select(n => CellOf(n.Position)));
            bool Open(int cell) => cell >= 0 && !blocked.Contains(cell) && !nodes.Contains(cell) && !taken.Contains(cell) && !InsideCore(scenario, cell);
            if (!Open(start)) return (null, null);
            var from = new Dictionary<int, int> { [start] = -1 };
            var queue = new Queue<int>(); queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                foreach (Facing facing in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
                {
                    int next = Neighbour(cell, facing);
                    if (next < 0) continue;
                    if (into(next))
                    {
                        var path = new List<int>();
                        for (int c = cell; c != -1; c = from[c]) path.Add(c);
                        path.Reverse();
                        var facings = new Facing[path.Count];
                        for (int i = 0; i + 1 < path.Count; i++)
                            facings[i] = new[] { Facing.North, Facing.East, Facing.South, Facing.West }.First(d => Neighbour(path[i], d) == path[i + 1]);
                        facings[path.Count - 1] = facing;
                        return (path.ToArray(), facings);
                    }
                    if (!Open(next) || from.ContainsKey(next)) continue;
                    from[next] = cell; queue.Enqueue(next);
                }
            }
            return (null, null);
        }

        private static ProcessingMatch StartProcessing(ulong seed, int advances = 2, CivKind civ = CivKind.Metallurgy, Action<ScenarioDefinition> beforeSimulation = null)
        {
            var scenario = MapGenerator.Generate(seed, true, true);
            scenario.Map.Terrain = new byte[scenario.Map.WidthCells * scenario.Map.HeightCells];
            scenario.Economy.Ages = true; scenario.Economy.ProcessingChain = true;
            scenario.Economy.StartFood = 10000; scenario.Economy.StartWood = 10000;
            scenario.Economy.AdvanceFoodCost = 0; scenario.Economy.AdvanceWoodCost = 0; scenario.Economy.AdvanceTicks = 1;
            scenario.Economy.Age2FoodCost = 0; scenario.Economy.Age2WoodCost = 0; scenario.Economy.Age2Ticks = 1;
            scenario.Economy.BeltTicksPerCell = 1; scenario.Economy.MineIntervalTicks = 1; scenario.Economy.SmeltTicks = 60;
            scenario.Economy.BlastFurnaceTicks = 1;
            scenario.Economy.CharcoalTicks = 2; scenario.Economy.SteelTicks = 3;
            scenario.Economy.MineWork = 1; scenario.Economy.SmelterWork = 1; scenario.Economy.CharcoalKilnWork = 1;
            scenario.Economy.SteelworksWork = 1;
            scenario.Cores[0].Hp = 1000000; scenario.Cores[1].Hp = 1000000;
            beforeSimulation?.Invoke(scenario);
            var match = new ProcessingMatch { Scenario = scenario, Simulation = new Battle(scenario) };
            match.Gateway = new CommandGateway(match.Simulation);
            match.Send(EconomyCommand.Auto(1, ++match.Sequence, false));
            match.Send(EconomyCommand.Auto(2, ++match.Sequence, false));
            match.Steps(1);
            for (int age = 0; age < advances; age++)
            {
                match.Send(EconomyCommand.Advance(1, ++match.Sequence, civ));
                match.Steps(2);
            }
            Assert.That(match.State["Economy[1].Age"], Is.EqualTo(advances.ToString(CultureInfo.InvariantCulture)), "test setup reaches the requested age");
            return match;
        }

        private static ProcessingMatch StartAutomaticProcessing(ulong seed, Action<ScenarioDefinition> beforeSimulation = null)
        {
            var match = StartProcessing(seed, beforeSimulation: beforeSimulation);
            match.Send(EconomyCommand.Auto(1, ++match.Sequence, true));
            return match;
        }

        private static uint PlaceBarracks(ProcessingMatch match)
        {
            int centre = CellOf(match.Scenario.Cores[0].Position);
            int size = match.Scenario.Economy.BarracksSizeCells;
            foreach (int origin in Ring(centre, 5, 18))
            {
                long before = Number(match.State, "Buildings.Count");
                match.Send(EconomyCommand.Place(1, ++match.Sequence, BuildingKind.Barracks, origin, Facing.North));
                match.Steps(1);
                if (Number(match.State, "Buildings.Count") == before + 1) return (uint)before + 1;
            }
            return 0;
        }

        private static ProcessingMatch StartTrainingState(CivKind civ, int age)
        {
            int steelCell = -1;
            var match = StartProcessing(97531UL, age, civ, scenario =>
            {
                int core = CellOf(scenario.Cores[0].Position);
                int east = Neighbour(core, Facing.East), west = Neighbour(core, Facing.West);
                steelCell = east;
                scenario.Belts = new[]
                {
                    new BeltDefinition { Cell = east, FactionId = 1, Facing = Facing.West, Item = ResourceKind.Steel },
                    new BeltDefinition { Cell = west, FactionId = 1, Facing = Facing.East, Item = ResourceKind.Steel }
                };
            });
            match.Steps(20);
            var state = match.State;
            Assert.That(Number(state, "Economy[1].Steel"), Is.GreaterThanOrEqualTo(match.Scenario.Economy.HeavyInfantrySteelCost), "test setup has enough steel");
            uint barracks = PlaceBarracks(match);
            Assert.That(barracks, Is.GreaterThan(0u), "test setup placed a barracks");
            Build(match, barracks);
            return match;
        }

        private static (uint id, int origin, int[] belt, Facing[] facings) PlaceWithRoute(ProcessingMatch match, BuildingKind kind,
            Func<int, bool> into, HashSet<int> taken)
        {
            int centre = CellOf(match.Scenario.Cores[0].Position);
            int size = kind == BuildingKind.Mine ? match.Scenario.Economy.MineSizeCells
                : kind == BuildingKind.Smelter ? match.Scenario.Economy.SmelterSizeCells
                : kind == BuildingKind.CharcoalKiln ? match.Scenario.Economy.CharcoalKilnSizeCells
                : match.Scenario.Economy.SteelworksSizeCells;
            foreach (int origin in Ring(centre, 5, 24))
                foreach (Facing facing in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
                {
                    var footprint = new HashSet<int>(Footprint(origin, size));
                    if (footprint.Any(taken.Contains)) continue;
                    var reserved = new HashSet<int>(taken.Concat(footprint));
                    var route = Route(match.Scenario, OutputCell(origin, size, facing), into, reserved);
                    if (route.cells == null) continue;
                    long before = Number(match.State, "Buildings.Count");
                    match.Send(EconomyCommand.Place(1, ++match.Sequence, kind, origin, facing));
                    match.Steps(1);
                    if (Number(match.State, "Buildings.Count") == before + 1) return ((uint)before + 1, origin, route.cells, route.facings);
                }
            return (0, -1, null, null);
        }

        private static void Build(ProcessingMatch match, uint id)
        {
            match.Send(EconomyCommand.Assign(1, ++match.Sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, id));
            for (int i = 0; i < 3000 && match.State["Buildings[" + id + "].Complete"] != "1"; i++) match.Steps(1);
            Assert.That(match.State["Buildings[" + id + "].Complete"], Is.EqualTo("1"), "building " + id + " finished");
        }

        private static (uint id, int origin, Facing facing) PlaceManualSteelworks(ProcessingMatch match)
        {
            int centre = CellOf(match.Scenario.Cores[0].Position);
            int size = match.Scenario.Economy.SteelworksSizeCells;
            foreach (int origin in Ring(centre, 5, 24))
                foreach (Facing facing in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
                {
                    long before = Number(match.State, "Buildings.Count");
                    match.Send(EconomyCommand.Place(1, ++match.Sequence, BuildingKind.Steelworks, origin, facing));
                    match.Steps(1);
                    if (Number(match.State, "Buildings.Count") == before + 1)
                    {
                        uint id = (uint)before + 1;
                        Assert.That(match.State["Buildings[" + id + "].Kind"], Is.EqualTo(((byte)BuildingKind.Steelworks).ToString(CultureInfo.InvariantCulture)));
                        return (id, origin, facing);
                    }
                }
            Assert.Fail("手動の製鋼所を置けなかった");
            return (0, -1, Facing.North);
        }

        private static Facing Opposite(Facing facing)
            => facing == Facing.North ? Facing.South : facing == Facing.East ? Facing.West : facing == Facing.South ? Facing.North : Facing.East;

        private static bool IsInitialBeltCellOpen(ScenarioDefinition scenario, int cell)
        {
            if (cell < 0 || cell >= Width * Height || scenario.Map.BlockedCellIds.Contains(cell) || InsideCore(scenario, cell)) return false;
            if (scenario.ResourceNodes.Any(n => CellOf(n.Position) == cell)) return false;
            return true;
        }

        private static bool CanInputRun(ScenarioDefinition scenario, int origin, int size, Facing side, int length)
        {
            int cell = OutputCell(origin, size, side);
            Facing into = Opposite(side);
            for (int i = 0; i < length; i++)
            {
                if (!IsInitialBeltCellOpen(scenario, cell)) return false;
                cell = Neighbour(cell, Opposite(into));
            }
            return true;
        }

        private static BeltDefinition[] InputRun(ScenarioDefinition scenario, int origin, int size, Facing side, ResourceKind[] items)
        {
            Facing into = Opposite(side);
            Assert.That(CanInputRun(scenario, origin, size, side, items.Length), Is.True,
                "製鋼所の入力側に初期ベルトを置ける空きマスがない: " + side);
            int cell = OutputCell(origin, size, side);
            var result = new BeltDefinition[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                result[i] = new BeltDefinition { Cell = cell, FactionId = 1, Facing = into, Item = items[i] };
                cell = Neighbour(cell, Opposite(into));
            }
            return result;
        }

        private static Facing[] InputSides(ScenarioDefinition scenario, int origin, int size, Facing outputFacing, int length)
        {
            return new[] { Facing.North, Facing.East, Facing.South, Facing.West }
                .Where(side => side != outputFacing && CanInputRun(scenario, origin, size, side, length)).ToArray();
        }

        private static ProcessingMatch ManualSteelworks(ulong seed,
            Func<ScenarioDefinition, int, Facing, BeltDefinition[]> beltFactory,
            out uint id, out int origin, out Facing facing)
        {
            var first = StartProcessing(seed, 2, CivKind.Metallurgy);
            var firstPlacement = PlaceManualSteelworks(first);
            Build(first, firstPlacement.id);

            var second = StartProcessing(seed, 2, CivKind.Metallurgy, scenario =>
            {
                scenario.Belts = beltFactory == null ? Array.Empty<BeltDefinition>()
                    : beltFactory(scenario, firstPlacement.origin, firstPlacement.facing);
            });
            var secondPlacement = PlaceManualSteelworks(second);
            Assert.That(secondPlacement.id, Is.EqualTo(firstPlacement.id), "同じ命令順で製鋼所の ID が変わった");
            Assert.That(secondPlacement.origin, Is.EqualTo(firstPlacement.origin), "同じ種・同じ命令順で製鋼所の位置が変わった");
            Assert.That(secondPlacement.facing, Is.EqualTo(firstPlacement.facing), "同じ種・同じ命令順で製鋼所の向きが変わった");
            Build(second, secondPlacement.id);
            id = secondPlacement.id; origin = secondPlacement.origin; facing = secondPlacement.facing;
            return second;
        }

        private static int PositionCell(Dictionary<string, string> fields, string prefix)
            => (int)(Number(fields, prefix + "Position.Z.Raw") / 65536 / 2) * Width
                + (int)(Number(fields, prefix + "Position.X.Raw") / 65536 / 2);

        private static int CellDistanceToFootprint(int cell, int origin, int size)
        {
            int x = cell % Width, z = cell / Width, x0 = origin % Width, z0 = origin / Width;
            int dx = x < x0 ? x0 - x : x >= x0 + size ? x - (x0 + size - 1) : 0;
            int dz = z < z0 ? z0 - z : z >= z0 + size ? z - (z0 + size - 1) : 0;
            return dx + dz;
        }

        private static bool RaidCellHasNoFactionOneTarget(ScenarioDefinition scenario, Dictionary<string, string> fields, int cell)
        {
            int buildings = (int)Number(fields, "Buildings.Count");
            for (int id = 1; id <= buildings; id++)
            {
                string p = "Buildings[" + id + "].";
                if (fields[p + "FactionId"] == "1" && fields[p + "Alive"] == "1"
                    && CellDistanceToFootprint(cell, (int)Number(fields, p + "OriginCell"),
                        scenario.Economy.SteelworksSizeCells) <= 1) return false;
            }
            int villagers = (int)Number(fields, "Villagers.Count");
            for (int id = 1; id <= villagers; id++)
            {
                string p = "Villagers[" + id + "].";
                if (fields[p + "FactionId"] == "1" && fields[p + "Alive"] == "1"
                    && CellDistanceToFootprint(cell, PositionCell(fields, p), 1) <= 1) return false;
            }
            int soldiers = (int)Number(fields, "Soldiers.Count");
            for (int id = 1; id <= soldiers; id++)
            {
                string p = "Soldiers[" + id + "].";
                if (fields[p + "FactionId"] == "1" && fields[p + "Alive"] == "1"
                    && CellDistanceToFootprint(cell, PositionCell(fields, p), 1) <= 1) return false;
            }
            return true;
        }

        [Test]
        public void DisabledProcessingChainIsByteForByteTheExistingScenario()
        {
            var implicitOff = MapGenerator.GenerateTerrain(8675309UL);
            var explicitOff = MapGenerator.GenerateTerrain(8675309UL, gold: false, processingChain: false);
            Assert.That(ScenarioBinary.Encode(explicitOff), Is.EqualTo(ScenarioBinary.Encode(implicitOff)));

            var a = new Battle(implicitOff);
            var b = new Battle(explicitOff);
            for (long tick = 1; tick <= 120; tick++)
            {
                a.Step(tick, Array.Empty<ScheduledInput>());
                b.Step(tick, Array.Empty<ScheduledInput>());
            }
            Assert.That(a.CaptureDiagnostic().CanonicalState, Is.EqualTo(b.CaptureDiagnostic().CanonicalState));
        }

        [Test]
        public void ProcessingChainKeepsMapPlacementAndRoundTripsItsTail()
        {
            var off = MapGenerator.GenerateTerrain(42UL, gold: false, processingChain: false);
            var on = MapGenerator.GenerateTerrain(42UL, gold: false, processingChain: true);
            Assert.That(on.Map.BlockedCellIds, Is.EqualTo(off.Map.BlockedCellIds));
            Assert.That(on.Cores.Select(c => c.Position), Is.EqualTo(off.Cores.Select(c => c.Position)));
            Assert.That(on.Outposts.Select(o => o.Position), Is.EqualTo(off.Outposts.Select(o => o.Position)));
            Assert.That(on.ResourceNodes.Select(n => (n.Id, n.Kind, n.Position, n.Amount)),
                Is.EqualTo(off.ResourceNodes.Select(n => (n.Id, n.Kind, n.Position, n.Amount))));
            Assert.That(on.Economy.ProcessingChain, Is.True);
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(on)).Economy.ProcessingChain, Is.True);
            Assert.That(ScenarioBinary.Encode(ScenarioBinary.Decode(ScenarioBinary.Encode(on))),
                Is.EqualTo(ScenarioBinary.Encode(on)));
        }

        [Test]
        public void ProcessingChainSimulationReplaysIdentically()
        {
            var scenario = MapGenerator.GenerateTerrain(99UL, gold: false, processingChain: true);
            var left = new Battle(scenario);
            var right = new Battle(ScenarioBinary.Decode(ScenarioBinary.Encode(scenario)));
            for (long tick = 1; tick <= 300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
            }
            Assert.That(left.CaptureDiagnostic().CanonicalState, Is.EqualTo(right.CaptureDiagnostic().CanonicalState));
        }

        [Test]
        public void NoRequestKeepsTheDeterministicDiagnosticHash()
        {
            var left = StartProcessing(7001UL);
            var right = StartProcessing(7001UL);
            for (int tick = 0; tick < 40; tick++)
            {
                left.Steps(1); right.Steps(1);
                Assert.That(left.Simulation.CaptureDiagnostic().CanonicalState,
                    Is.EqualTo(right.Simulation.CaptureDiagnostic().CanonicalState), "tick " + tick);
                Assert.That(left.State.Keys.Any(k => k.Contains("RequestPending", StringComparison.Ordinal)), Is.False);
            }
        }

        [Test]
        public void RequestedLocationChoosesTheNearestResourceForTheLine()
        {
            var match = StartProcessing(7002UL);
            var ore = match.Scenario.ResourceNodes.First(n => n.Kind == ResourceKind.Ore);
            int requestedCell = CellOf(ore.Position);
            match.Send(EconomyCommand.RequestLineAt(1, ++match.Sequence, ProcessingLineKind.Steel, requestedCell));
            match.Send(EconomyCommand.Auto(1, ++match.Sequence, true));
            match.Steps(35);
            var state = match.State;
            Assert.That(Number(state, "ProcessingLines.Count"), Is.GreaterThanOrEqualTo(1));
            uint mineId = (uint)Number(state, "ProcessingLines[0].MineId");
            Assert.That(mineId, Is.Not.EqualTo(0u));
            int mineCell = (int)Number(state, "Buildings[" + mineId + "].OriginCell");
            int distance = Math.Abs(mineCell % Width - requestedCell % Width) + Math.Abs(mineCell / Width - requestedCell / Width);
            Assert.That(distance, Is.LessThanOrEqualTo(3), "the requested line's mine stays near the requested resource");
        }

        [Test]
        public void RequestLineReportsWoodShortfallInTheTimeline()
        {
            var match = StartProcessing(7003UL, beforeSimulation: scenario => scenario.Economy.StartWood = 0);
            int requestedCell = CellOf(match.Scenario.ResourceNodes.First(n => n.Kind == ResourceKind.Ore).Position);
            match.Send(EconomyCommand.RequestLineAt(1, ++match.Sequence, ProcessingLineKind.CoreMetal, requestedCell));
            match.Steps(1);
            var rejection = match.Simulation.Capture(1).Events.Single(e => e.Kind == EventKind.EconomyLineRejected);
            Assert.That(rejection.Reason, Is.EqualTo(ReasonCode.LineWoodShortfall));
            Assert.That(rejection.Value, Is.EqualTo((int)ProcessingLineKind.CoreMetal));
        }

        [Test]
        public void MiningAndHandHaulingFeedSteelworksAndSteelReachesTheCore()
        {
            var match = StartProcessing(2468UL);
            var steelworks = PlaceWithRoute(match, BuildingKind.Steelworks, c => InsideCore(match.Scenario, c), new HashSet<int>());
            Assert.That(steelworks.id, Is.Not.EqualTo(0u), "steelworks placement must succeed");
            var steelFootprint = new HashSet<int>(Footprint(steelworks.origin, match.Scenario.Economy.SteelworksSizeCells));
            var steelToCore = steelworks.belt;
            var smelter = PlaceWithRoute(match, BuildingKind.Smelter, steelFootprint.Contains, new HashSet<int>(steelFootprint.Concat(steelToCore)));
            Assert.That(smelter.id, Is.Not.EqualTo(0u), "dedicated smelter placement must succeed");
            var smelterFootprint = new HashSet<int>(Footprint(smelter.origin, match.Scenario.Economy.SmelterSizeCells));
            var mine = match.Scenario.ResourceNodes.Where(n => n.Kind == ResourceKind.Ore).OrderBy(n => n.Id).First();
            var minePlaced = (uint)0; var mineBelt = (int[])null; var mineFacings = (Facing[])null;
            foreach (int origin in new[] { CellOf(mine.Position), CellOf(mine.Position) - 1, CellOf(mine.Position) - Width, CellOf(mine.Position) - Width - 1 })
            {
                if (Footprint(origin, match.Scenario.Economy.MineSizeCells).Any(c => steelFootprint.Contains(c) || steelToCore.Contains(c) || smelterFootprint.Contains(c) || smelter.belt.Contains(c))) continue;
                var mineTaken = new HashSet<int>(steelFootprint.Concat(steelToCore).Concat(smelterFootprint).Concat(smelter.belt));
                var candidate = Route(match.Scenario, OutputCell(origin, match.Scenario.Economy.MineSizeCells, Facing.North), smelterFootprint.Contains, mineTaken);
                if (candidate.cells == null) continue;
                match.Send(EconomyCommand.Place(1, ++match.Sequence, BuildingKind.Mine, origin, Facing.North));
                match.Steps(1);
                if (Number(match.State, "Buildings.Count") == 3)
                {
                    minePlaced = 3; mineBelt = candidate.cells; mineFacings = candidate.facings; break;
                }
            }
            Assert.That(minePlaced, Is.Not.EqualTo(0u), "dedicated mine placement must succeed");
            var kilnTaken = new HashSet<int>(steelFootprint);
            kilnTaken.UnionWith(steelToCore); kilnTaken.UnionWith(smelterFootprint); kilnTaken.UnionWith(smelter.belt);
            var kiln = PlaceWithRoute(match, BuildingKind.CharcoalKiln, steelFootprint.Contains, kilnTaken);
            Assert.That(kiln.id, Is.Not.EqualTo(0u), "charcoal kiln placement must succeed");
            Build(match, steelworks.id); Build(match, smelter.id); Build(match, minePlaced); Build(match, kiln.id);
            match.Send(EconomyCommand.PlaceBelt(1, ++match.Sequence, mineBelt, mineFacings));
            match.Send(EconomyCommand.PlaceBelt(1, ++match.Sequence, smelter.belt, smelter.facings));
            match.Send(EconomyCommand.PlaceBelt(1, ++match.Sequence, kiln.belt, kiln.facings));
            match.Send(EconomyCommand.PlaceBelt(1, ++match.Sequence, steelToCore, steelworks.facings));
            match.Steps(1);
            TestContext.WriteLine("routes: steel=" + string.Join("/", steelToCore) + " smelter=" + string.Join("/", smelter.belt)
                + " kiln=" + string.Join("/", kiln.belt) + " mine=" + string.Join("/", mineBelt));
            match.Send(EconomyCommand.Assign(1, ++match.Sequence, new uint[] { 1 }, EconomyTargetKind.Building, kiln.id));
            match.Steps(3500);
            var end = match.State;
            Assert.That(Number(end, "Economy[1].Steel"), Is.GreaterThan(0), "steel passed the steelworks output belt into the core");
            Assert.That(Number(end, "Economy[1].Charcoal"), Is.GreaterThanOrEqualTo(0));
            Assert.That(Number(end, "Economy[1].Metal"), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void HaulDestinationIsRecordedAndReplaysToTheSameState()
        {
            var command = EconomyCommand.AssignHaul(1, 9, new uint[] { 1 }, 23, 47);
            var input = new ScheduledInput(4, 0, 1, command);
            var copy = InputBinary.Decode(InputBinary.Encode(input));
            Assert.That(copy.Economy.ProducerId, Is.EqualTo(47u));
            Assert.That(copy.Economy.HaulToId, Is.EqualTo(command.HaulToId));

            var scenario = MapGenerator.GenerateTerrain(13579UL, gold: false, processingChain: true);
            var left = new Battle(scenario);
            var right = new Battle(ScenarioBinary.Decode(ScenarioBinary.Encode(scenario)));
            for (long tick = 1; tick <= 120; tick++)
            {
                var inputs = tick == 1 ? new[] { input } : Array.Empty<ScheduledInput>();
                var replayInputs = tick == 1 ? new[] { copy } : Array.Empty<ScheduledInput>();
                left.Step(tick, inputs); right.Step(tick, replayInputs);
            }
            Assert.That(left.CaptureDiagnostic().CanonicalState, Is.EqualTo(right.CaptureDiagnostic().CanonicalState));
        }

        [Test]
        public void AutomaticEconomyBuildsASeparateSteelLineAndHumanCanReturnItToAuto()
        {
            var match = StartAutomaticProcessing(97531UL);
            for (int i = 0; i < 9000; i++) match.Steps(1);
            var state = match.State;
            Assert.That(Number(state, "ProcessingLines.Count"), Is.GreaterThanOrEqualTo(2), "automatic economy must create the core and steel lines; " + string.Join(", ", state.Where(p => p.Key.StartsWith("ProcessingLines", StringComparison.Ordinal)).Select(p => p.Key + "=" + p.Value)));
            var lineState = string.Join(", ", state.Where(p => p.Key.StartsWith("Economy[1]", StringComparison.Ordinal) || p.Key.StartsWith("ProcessingLines[1]", StringComparison.Ordinal)
                || p.Key.StartsWith("Buildings[10]", StringComparison.Ordinal) || p.Key.StartsWith("Buildings[11]", StringComparison.Ordinal)
                || p.Key.StartsWith("Buildings[14]", StringComparison.Ordinal) || p.Key.StartsWith("Buildings[15]", StringComparison.Ordinal)).Select(p => p.Key + "=" + p.Value));
            Assert.That(Number(state, "Economy[1].Steel"), Is.GreaterThan(0), "automatic steel must reach the core; " + lineState);
            Assert.That(state.Any(p => p.Key.EndsWith(".Class", StringComparison.Ordinal) && p.Value == ((byte)UnitKind.HeavyInfantry).ToString(CultureInfo.InvariantCulture)), Is.True, "automatic economy must train heavy infantry after steel arrives");
            Assert.That(state["ProcessingLines[0].Manager"], Is.EqualTo("0"));
            Assert.That(state["ProcessingLines[1].Manager"], Is.EqualTo("0"));
            int beltCount = (int)Number(state, "ProcessingLines[1].BeltCount");
            Assert.That(beltCount, Is.GreaterThan(0), "steel line must have a recorded route");
            int cell = (int)Number(state, "ProcessingLines[1].Belts[0].Cell");
            match.Send(EconomyCommand.RemoveBelt(1, ++match.Sequence, cell));
            match.Steps(1);
            state = match.State;
            Assert.That(state["ProcessingLines[1].Manager"], Is.EqualTo("1"), "touching one belt hands the whole line to the player");
            for (int i = 0; i < 2500; i++) match.Steps(1);
            Assert.That(match.State.ContainsKey("Belts[" + cell + "].FactionId"), Is.False, "manual lines must not replace a removed belt");
            match.Send(EconomyCommand.ReturnLineToAuto(1, ++match.Sequence, 2));
            match.Steps(1);
            Assert.That(match.State["ProcessingLines[1].Manager"], Is.EqualTo("0"), "line return did not apply: " + string.Join(", ", match.State.Where(p => p.Key.StartsWith("ProcessingLines[1]", StringComparison.Ordinal)).Select(p => p.Key + "=" + p.Value)));
            for (int i = 0; i < 1000; i++) match.Steps(1);
            Assert.That(match.State.ContainsKey("Belts[" + cell + "].FactionId"), Is.True, "returning a line to auto fills its missing belt");
        }

        [Test]
        public void SteelworksNeedsBothMaterialsAndConsumesThemTogether()
        {
            uint metalOnlyId; int metalOnlyOrigin; Facing metalOnlyFacing;
            var metalOnly = ManualSteelworks(97531UL, (scenario, origin, facing) =>
            {
                var side = InputSides(scenario, origin, scenario.Economy.SteelworksSizeCells, facing, 11).FirstOrDefault();
                Assert.That(CanInputRun(scenario, origin, scenario.Economy.SteelworksSizeCells, side, 11), Is.True, "金属だけの入力ベルトを置ける辺がない");
                return InputRun(scenario, origin, scenario.Economy.SteelworksSizeCells, side,
                    Enumerable.Repeat(ResourceKind.Metal, 11).ToArray());
            }, out metalOnlyId, out metalOnlyOrigin, out metalOnlyFacing);
            string metalPrefix = "Buildings[" + metalOnlyId + "].";
            metalOnly.Steps(300);
            var metalState = metalOnly.State;
            Assert.That(Number(metalState, metalPrefix + "Input"), Is.EqualTo(10), "金属だけで Input が満杯にならなかった");
            Assert.That(Number(metalState, metalPrefix + "Output"), Is.EqualTo(0), "木炭なしなのに鋼 Output が増えた");
            Assert.That(Number(metalState, "Economy[1].Steel"), Is.EqualTo(0), "木炭なしなのにコアの鋼が増えた");

            uint charcoalOnlyId; int charcoalOnlyOrigin; Facing charcoalOnlyFacing;
            var charcoalOnly = ManualSteelworks(97531UL, (scenario, origin, facing) =>
            {
                var side = InputSides(scenario, origin, scenario.Economy.SteelworksSizeCells, facing, 1).FirstOrDefault();
                Assert.That(InputSides(scenario, origin, scenario.Economy.SteelworksSizeCells, facing, 1).Length, Is.GreaterThan(0), "木炭だけの入力ベルトを置ける辺がない");
                return InputRun(scenario, origin, scenario.Economy.SteelworksSizeCells, side, new[] { ResourceKind.Charcoal });
            }, out charcoalOnlyId, out charcoalOnlyOrigin, out charcoalOnlyFacing);
            string charcoalPrefix = "Buildings[" + charcoalOnlyId + "].";
            charcoalOnly.Steps(300);
            var charcoalState = charcoalOnly.State;
            Assert.That(Number(charcoalState, charcoalPrefix + "Input"), Is.EqualTo(0), "木炭だけなのに金属 Input が増えた");
            Assert.That(Number(charcoalState, charcoalPrefix + "InputSecondary"), Is.EqualTo(1), "木炭だけで InputSecondary が増えなかった");
            Assert.That(Number(charcoalState, charcoalPrefix + "Output"), Is.EqualTo(0), "金属なしなのに鋼 Output が増えた");
            Assert.That(Number(charcoalState, "Economy[1].Steel"), Is.EqualTo(0), "金属なしなのにコアの鋼が増えた");

            uint bothId; int bothOrigin; Facing bothFacing;
            var both = ManualSteelworks(97531UL, (scenario, origin, facing) =>
            {
                var sides = InputSides(scenario, origin, scenario.Economy.SteelworksSizeCells, facing, 2);
                Assert.That(sides.Length, Is.GreaterThanOrEqualTo(2), "金属と木炭を別々の辺から入れられない");
                var metal = InputRun(scenario, origin, scenario.Economy.SteelworksSizeCells, sides[0], new[] { ResourceKind.Metal, ResourceKind.Metal });
                var charcoal = InputRun(scenario, origin, scenario.Economy.SteelworksSizeCells, sides[1], new[] { ResourceKind.Charcoal });
                return metal.Concat(charcoal).ToArray();
            }, out bothId, out bothOrigin, out bothFacing);
            // Two metal and one charcoal: one steel takes exactly one of each, and the second metal waits for charcoal.
            // (Both enter and are taken in the same tick, so the inputs never show 1 and 1; the leftover shows the count.)
            string bothPrefix = "Buildings[" + bothId + "].";
            both.Steps(300);
            var bothState = both.State;
            Assert.That(Number(bothState, bothPrefix + "Output") + Number(bothState, "Economy[1].Steel"), Is.EqualTo(1), "両材料から鋼が1つできなかった");
            Assert.That(Number(bothState, bothPrefix + "Input"), Is.EqualTo(1), "鋼1つで金属がちょうど1つ減っていない");
            Assert.That(Number(bothState, bothPrefix + "InputSecondary"), Is.EqualTo(0), "鋼1つで木炭がちょうど1つ減っていない");

            // A full metal input (10) does not stop charcoal entering from another side. The charcoal run is laid one cell
            // short of the steelworks and joined by a player command only after the metal input is seen full.
            uint fullId; int fullOrigin; Facing fullFacing;
            BeltDefinition joint = default;
            var full = ManualSteelworks(97531UL, (scenario, origin, facing) =>
            {
                var sides = InputSides(scenario, origin, scenario.Economy.SteelworksSizeCells, facing, 11);
                Assert.That(sides.Length, Is.GreaterThanOrEqualTo(2), "金属と木炭を別々の辺から入れられない");
                var metal = InputRun(scenario, origin, scenario.Economy.SteelworksSizeCells, sides[0], Enumerable.Repeat(ResourceKind.Metal, 11).ToArray());
                var charcoal = InputRun(scenario, origin, scenario.Economy.SteelworksSizeCells, sides[1], new[] { (ResourceKind)0, ResourceKind.Charcoal });
                joint = charcoal[0];
                return metal.Concat(charcoal.Skip(1)).ToArray();
            }, out fullId, out fullOrigin, out fullFacing);
            string fullPrefix = "Buildings[" + fullId + "].";
            full.Steps(300);
            var fullState = full.State;
            Assert.That(Number(fullState, fullPrefix + "Input"), Is.EqualTo(10), "金属置き場が10で止まらなかった");
            Assert.That(Number(fullState, fullPrefix + "InputSecondary") + Number(fullState, fullPrefix + "Output") + Number(fullState, "Economy[1].Steel"),
                Is.EqualTo(0), "つなぐ前に木炭が入った、または鋼ができた");
            full.Send(EconomyCommand.PlaceBelt(1, ++full.Sequence, new[] { joint.Cell }, new[] { joint.Facing }));
            bool charcoalEntered = false;
            for (int i = 0; i < 300 && !charcoalEntered; i++)
            {
                full.Steps(1);
                var s = full.State;
                charcoalEntered = Number(s, fullPrefix + "InputSecondary") > 0 || Number(s, fullPrefix + "Output") > 0 || Number(s, "Economy[1].Steel") > 0;
            }
            Assert.That(charcoalEntered, Is.True, "金属置き場が満杯のとき、別の辺の木炭が入らなかった");
        }

        [Test]
        public void MixedBeltStopsBehindAFullMetalInput()
        {
            int[] cells = null;
            uint id; int origin; Facing facing;
            var match = ManualSteelworks(97531UL, (scenario, steelworksOrigin, steelworksFacing) =>
            {
                var side = InputSides(scenario, steelworksOrigin, scenario.Economy.SteelworksSizeCells, steelworksFacing, 12).FirstOrDefault();
                var run = InputRun(scenario, steelworksOrigin, scenario.Economy.SteelworksSizeCells, side,
                    Enumerable.Repeat(ResourceKind.Metal, 11).Concat(new[] { ResourceKind.Charcoal }).ToArray());
                cells = run.Select(b => b.Cell).ToArray();
                return run;
            }, out id, out origin, out facing);
            string prefix = "Buildings[" + id + "].";
            match.Steps(300);
            var state = match.State;
            Assert.That(Number(state, prefix + "Input"), Is.EqualTo(10), "混ぜたベルトで金属 Input が10で止まらなかった");
            Assert.That(Number(state, prefix + "InputSecondary"), Is.EqualTo(0), "先頭の金属が詰まったのに後ろの木炭が入った");
            Assert.That(Number(state, prefix + "Output"), Is.EqualTo(0), "木炭が入っていないのに鋼ができた");
            Assert.That(Number(state, "Economy[1].Steel"), Is.EqualTo(0), "木炭が入っていないのにコアの鋼が増えた");
            Assert.That(state["Belts[" + cells[0] + "].Item"], Is.EqualTo(((byte)ResourceKind.Metal).ToString(CultureInfo.InvariantCulture)), "11個目の金属がベルト先頭に残っていない");
            Assert.That(cells.Skip(1).Any(cell => state.ContainsKey("Belts[" + cell + "].Item")
                && state["Belts[" + cell + "].Item"] == ((byte)ResourceKind.Charcoal).ToString(CultureInfo.InvariantCulture)),
                Is.True, "後ろの木炭が先頭詰まりで停止していない");
        }

        [Test]
        public void HeavyInfantryIsOnlyTrainableInMetallurgySecondAgeAndUsesSteel()
        {
            foreach (var unavailable in new[] { (CivKind.Primitive, 0), (CivKind.Metallurgy, 1), (CivKind.Agrarian, 2) })
            {
                var blocked = StartTrainingState(unavailable.Item1, unavailable.Item2);
                var blockedState = blocked.State;
                uint blockedBarracks = Enumerable.Range(1, (int)Number(blockedState, "Buildings.Count"))
                    .Where(id => blockedState["Buildings[" + id + "].FactionId"] == "1"
                        && blockedState["Buildings[" + id + "].Kind"] == ((byte)BuildingKind.Barracks).ToString(CultureInfo.InvariantCulture)
                        && blockedState["Buildings[" + id + "].Complete"] == "1")
                    .Select(id => (uint)id).First();
                long steelBefore = Number(blockedState, "Economy[1].Steel");
                blocked.Send(EconomyCommand.Train(1, ++blocked.Sequence, blockedBarracks, UnitKind.HeavyInfantry));
                blocked.Steps(1);
                blockedState = blocked.State;
                Assert.That(Number(blockedState, "Economy[1].Steel"), Is.EqualTo(steelBefore), "heavy infantry paid steel in an unavailable state: " + unavailable.Item1 + " age " + unavailable.Item2);
                Assert.That(Number(blockedState, "Buildings[" + blockedBarracks + "].Queued"), Is.EqualTo(0), "heavy infantry entered the queue in an unavailable state: " + unavailable.Item1 + " age " + unavailable.Item2);
            }

            var match = StartTrainingState(CivKind.Metallurgy, 2);
            var state = match.State;
            Assert.That(Number(state, "Economy[1].Age"), Is.GreaterThanOrEqualTo(2), "test setup did not reach metallurgy's second age");
            uint barracks = 0;
            for (int id = 1; id <= Number(state, "Buildings.Count"); id++)
                if (state["Buildings[" + id + "].FactionId"] == "1" && state["Buildings[" + id + "].Kind"] == ((byte)BuildingKind.Barracks).ToString(CultureInfo.InvariantCulture)
                    && state["Buildings[" + id + "].Complete"] == "1") { barracks = (uint)id; break; }
            Assert.That(barracks, Is.GreaterThan(0u), "test setup did not produce a completed barracks");
            long steel = Number(state, "Economy[1].Steel");
            Assert.That(steel, Is.GreaterThanOrEqualTo(match.Scenario.Economy.HeavyInfantrySteelCost), "test setup has no steel");
            match.Send(EconomyCommand.Auto(1, ++match.Sequence, false));
            match.Steps(1);
            steel = Number(match.State, "Economy[1].Steel");
            for (int i = 0; i < 5; i++) match.Send(EconomyCommand.CancelTrain(1, ++match.Sequence, barracks));
            match.Steps(1);
            match.Send(EconomyCommand.Train(1, ++match.Sequence, barracks, UnitKind.HeavyInfantry));
            match.Steps(1);
            state = match.State;
            Assert.That(Number(state, "Buildings[" + barracks + "].QueuedSteel"), Is.EqualTo(match.Scenario.Economy.HeavyInfantrySteelCost), "the heavy infantry queue reserved steel");
            for (int i = 0; i < match.Scenario.Economy.HeavyInfantryTrainTicks + 10; i++) match.Steps(1);
            state = match.State;
            Assert.That(state.Any(p => p.Key.EndsWith(".Class", StringComparison.Ordinal) && p.Value == ((byte)UnitKind.HeavyInfantry).ToString(CultureInfo.InvariantCulture)), Is.True, "heavy infantry was not spawned");
            string soldier = state.First(p => p.Key.EndsWith(".Class", StringComparison.Ordinal) && p.Value == ((byte)UnitKind.HeavyInfantry).ToString(CultureInfo.InvariantCulture)).Key;
            string soldierPrefix = soldier.Substring(0, soldier.Length - ".Class".Length);
            Assert.That(Number(state, soldierPrefix + ".Parameters.Hp"), Is.EqualTo(match.Scenario.Economy.HeavyInfantryHp));
            // The automatic economy researches weapons meanwhile; that bonus lands on heavy infantry as on any infantry.
            Assert.That(Number(state, soldierPrefix + ".Parameters.Damage"), Is.GreaterThanOrEqualTo(match.Scenario.Economy.HeavyInfantryDamage));
        }

        [Test]
        public void EnemyDestructionDoesNotHandAnAutomaticLineToThePlayer()
        {
            int recordedCell = -1, recordedTick = -1;
            // Both runs share this setup so the line takes the same route: soldiers barely move, and faction 1 has none,
            // so the one enemy placed on the belt is neither killed nor drawn into a fight before the belt exists.
            Action<ScenarioDefinition> quiet = scenario =>
            {
                for (int i = 0; i < scenario.UnitParameters.Length; i++)
                    if (scenario.UnitParameters[i].Kind == UnitKind.Infantry)
                    {
                        var parameters = scenario.UnitParameters[i];
                        parameters.Speed = Fix64.FromRaw(1);
                        // Off the 20-tick AI cycle: at 20 the blow and the automatic re-lay fall in the same tick, and the
                        // broken belt is never seen missing.
                        parameters.AttackIntervalTicks = 13;
                        scenario.UnitParameters[i] = parameters;
                    }
                for (int i = 0; i < scenario.Soldiers.Length; i++)
                    if (scenario.Soldiers[i].FactionId == 1)
                    {
                        var own = scenario.Soldiers[i];
                        own.Alive = false; own.Hp = 0;
                        scenario.Soldiers[i] = own;
                    }
                // Nor does it train any: new soldiers would find and kill the raiders, and make their army retreat.
                scenario.Economy.AutoInfantryQueue = 0;
                scenario.Economy.InfantryFoodCost = 1000000; scenario.Economy.ScoutFoodCost = 1000000;
                scenario.Economy.BeltHp = 1;
            };
            var firstRun = StartAutomaticProcessing(97531UL, quiet);
            for (int tick = 1; tick <= 20000 && recordedCell < 0; tick++)
            {
                firstRun.Steps(1);
                var firstState = firstRun.State;
                int beltCount = firstState.ContainsKey("ProcessingLines[1].BeltCount")
                    ? (int)Number(firstState, "ProcessingLines[1].BeltCount") : 0;
                var candidates = Enumerable.Range(0, beltCount)
                    .Where(i => firstState.ContainsKey("ProcessingLines[1].Belts[" + i + "].Cell"))
                    .Select(i => (int)Number(firstState, "ProcessingLines[1].Belts[" + i + "].Cell"))
                    .Where(cell => firstState.ContainsKey("Belts[" + cell + "].FactionId")
                        && RaidCellHasNoFactionOneTarget(firstRun.Scenario, firstState, cell))
                    .OrderByDescending(cell => Math.Abs(cell % Width - CellOf(firstRun.Scenario.Cores[0].Position) % Width)
                        + Math.Abs(cell / Width - CellOf(firstRun.Scenario.Cores[0].Position) / Width))
                    .ToArray();
                if (candidates.Length > 0)
                {
                    recordedCell = candidates[0];
                    recordedTick = tick;
                }
            }
            Assert.That(recordedCell, Is.GreaterThanOrEqualTo(0), "1回目を20000tick進めても、敵の射程内に他の標的がない鋼ラインのベルトが現れなかった");
            TestContext.WriteLine("first run recorded cell=" + recordedCell + " tick=" + recordedTick);
            var enemyDefinition = firstRun.Scenario.Soldiers
                .Where(s => s.FactionId == 2 && s.Kind == UnitKind.Infantry)
                .OrderByDescending(s => s.ArmyId == 7)
                .ThenBy(s => s.Id)
                .FirstOrDefault();
            int enemyId = enemyDefinition.Id > 0 && enemyDefinition.Id <= int.MaxValue ? (int)enemyDefinition.Id : -1;
            Assert.That(enemyId, Is.GreaterThan(0), "陣営2の敵兵を準備できなかった");
            var match = StartAutomaticProcessing(97531UL, scenario =>
            {
                quiet(scenario);
                var raidPoint = new SimPoint(Fix64.FromInt((recordedCell % Width) * 2 + 1), Fix64.FromInt((recordedCell / Width) * 2 + 1));
                for (int i = 0; i < scenario.Soldiers.Length; i++)
                {
                    var enemy = scenario.Soldiers[i];
                    if (enemy.FactionId != 2) continue;
                    // The whole army stands on the belt: a lone soldier makes its army retreat, and a retreating soldier never raids.
                    if (enemy.ArmyId == enemyDefinition.ArmyId) enemy.Position = raidPoint;
                    else { enemy.Alive = false; enemy.Hp = 0; }
                    scenario.Soldiers[i] = enemy;
                }
                uint armyId = scenario.Soldiers[enemyId - 1].ArmyId;
                scenario.Armies[armyId - 1].HomeObjective = new PolicyGoal(GoalKind.Outpost, 1, default);
                var outpost = scenario.Outposts[0];
                outpost.Position = raidPoint;
                scenario.Outposts[0] = outpost;
            });
            // Destroyed by the enemy: a steel-line belt that a faction 2 soldier aimed at on the previous tick is gone now.
            int destroyedCell = -1, destroyedTick = -1;
            var aimedBefore = new HashSet<int>();
            for (int tick = 1; tick <= 6000 && destroyedCell < 0; tick++)
            {
                match.Steps(1);
                var tickState = match.State;
                int beltCount = tickState.ContainsKey("ProcessingLines[1].BeltCount") ? (int)Number(tickState, "ProcessingLines[1].BeltCount") : 0;
                var lineCells = new HashSet<int>(Enumerable.Range(0, beltCount)
                    .Select(i => (int)Number(tickState, "ProcessingLines[1].Belts[" + i + "].Cell")));
                foreach (int cell in aimedBefore)
                    if (lineCells.Contains(cell) && !tickState.ContainsKey("Belts[" + cell + "].FactionId")) { destroyedCell = cell; destroyedTick = tick; break; }
                aimedBefore.Clear();
                for (int id = 1; id <= Number(tickState, "Soldiers.Count"); id++)
                    if (tickState["Soldiers[" + id + "].FactionId"] == "2" && tickState["Soldiers[" + id + "].Alive"] == "1"
                        && Number(tickState, "Soldiers[" + id + "].TargetKind") == 5)
                    {
                        int cell = (int)Number(tickState, "Soldiers[" + id + "].TargetId") - 1;
                        if (lineCells.Contains(cell) && tickState.ContainsKey("Belts[" + cell + "].FactionId")) aimedBefore.Add(cell);
                    }
            }
            TestContext.WriteLine("destroyed cell=" + destroyedCell + " tick=" + destroyedTick);
            Assert.That(destroyedCell, Is.GreaterThanOrEqualTo(0), "no enemy soldier destroyed a belt of the automatic steel line (placed at cell " + recordedCell + ")");
            var state = match.State;
            Assert.That(state["ProcessingLines[0].Manager"], Is.EqualTo("0"), "enemy damage changed the core line to manual management");
            Assert.That(state["ProcessingLines[1].Manager"], Is.EqualTo("0"), "enemy damage changed the steel line to manual management");
            // The enemy stays and may break it again, so one reappearance of the belt is the rebuild.
            bool beltWasRebuilt = false;
            for (int i = 0; i < 3000 && !beltWasRebuilt; i++)
            {
                match.Steps(1);
                var rebuiltState = match.State;
                beltWasRebuilt = rebuiltState.ContainsKey("Belts[" + destroyedCell + "].FactionId");
                Assert.That(rebuiltState["ProcessingLines[0].Manager"], Is.EqualTo("0"), "the core line became manual while being rebuilt");
                Assert.That(rebuiltState["ProcessingLines[1].Manager"], Is.EqualTo("0"), "the steel line became manual while being rebuilt");
            }
            Assert.That(beltWasRebuilt, Is.True, "automatic management did not rebuild the belt the enemy destroyed at cell " + destroyedCell);
        }

        [Test]
        public void ProcessingChainResourceNumbersFollowGold()
        {
            Assert.That((byte)ResourceKind.Gold, Is.EqualTo(7));
            Assert.That((byte)ResourceKind.Charcoal, Is.EqualTo(8));
            Assert.That((byte)ResourceKind.Steel, Is.EqualTo(9));
        }
    }
}
