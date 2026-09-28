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

        private static ProcessingMatch StartProcessing(ulong seed)
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
            var match = new ProcessingMatch { Scenario = scenario, Simulation = new Battle(scenario) };
            match.Gateway = new CommandGateway(match.Simulation);
            match.Send(EconomyCommand.Auto(1, ++match.Sequence, false));
            match.Send(EconomyCommand.Auto(2, ++match.Sequence, false));
            match.Steps(1);
            match.Send(EconomyCommand.Advance(1, ++match.Sequence, CivKind.Metallurgy));
            match.Steps(2);
            match.Send(EconomyCommand.Advance(1, ++match.Sequence, CivKind.Metallurgy));
            match.Steps(2);
            Assert.That(match.State["Economy[1].Age"], Is.EqualTo("2"), "test setup reaches metallurgy's second age");
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

        [Test]
        public void DisabledProcessingChainIsByteForByteTheExistingScenario()
        {
            var implicitOff = MapGenerator.GenerateTerrain(8675309UL);
            var explicitOff = MapGenerator.GenerateTerrain(8675309UL, false);
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
            var off = MapGenerator.GenerateTerrain(42UL, false);
            var on = MapGenerator.GenerateTerrain(42UL, true);
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
            var scenario = MapGenerator.GenerateTerrain(99UL, true);
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
        public void MiningAndHandHaulingFeedSteelworksAndSteelReachesTheCore()
        {
            var match = StartProcessing(2468UL);
            var steelworks = PlaceWithRoute(match, BuildingKind.Steelworks, c => InsideCore(match.Scenario, c), new HashSet<int>());
            Assume.That(steelworks.id, Is.Not.EqualTo(0u));
            var steelFootprint = new HashSet<int>(Footprint(steelworks.origin, match.Scenario.Economy.SteelworksSizeCells));
            var steelToCore = steelworks.belt;
            var smelter = PlaceWithRoute(match, BuildingKind.Smelter, steelFootprint.Contains, new HashSet<int>(steelFootprint.Concat(steelToCore)));
            Assume.That(smelter.id, Is.Not.EqualTo(0u));
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
            Assume.That(minePlaced, Is.Not.EqualTo(0u));
            var kilnTaken = new HashSet<int>(steelFootprint);
            kilnTaken.UnionWith(steelToCore); kilnTaken.UnionWith(smelterFootprint); kilnTaken.UnionWith(smelter.belt);
            var kiln = PlaceWithRoute(match, BuildingKind.CharcoalKiln, steelFootprint.Contains, kilnTaken);
            Assume.That(kiln.id, Is.Not.EqualTo(0u));
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

            var scenario = MapGenerator.GenerateTerrain(13579UL, true);
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
        public void ProcessingChainResourceNumbersLeaveGoldSlotSevenFree()
        {
            Assert.That((byte)ResourceKind.Charcoal, Is.EqualTo(8));
            Assert.That((byte)ResourceKind.Steel, Is.EqualTo(9));
            Assert.That(Enum.IsDefined(typeof(ResourceKind), (byte)7), Is.False);
        }
    }
}
