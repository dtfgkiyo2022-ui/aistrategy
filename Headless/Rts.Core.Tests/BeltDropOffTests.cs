using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class BeltDropOffTests
    {
        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static int CellOf(ScenarioDefinition scenario, SimPoint point)
            => (int)(point.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * scenario.Map.WidthCells
                + (int)(point.X.Raw / 65536 / scenario.Map.CellSizeMeters);

        private static int Neighbour(ScenarioDefinition scenario, int cell, Facing facing)
        {
            int width = scenario.Map.WidthCells, height = scenario.Map.HeightCells, x = cell % width, z = cell / width;
            return facing switch
            {
                Facing.North => z + 1 < height ? cell + width : -1,
                Facing.East => x + 1 < width ? cell + 1 : -1,
                Facing.South => z > 0 ? cell - width : -1,
                _ => x > 0 ? cell - 1 : -1
            };
        }

        private static Facing FacingBetween(ScenarioDefinition scenario, int from, int to)
        {
            foreach (Facing facing in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
                if (Neighbour(scenario, from, facing) == to) return facing;
            throw new InvalidOperationException("cells are not adjacent");
        }

        private static bool InCore(ScenarioDefinition scenario, int cell, int coreIndex = 0)
        {
            if (!InMap(scenario, cell)) return false;
            int width = scenario.Map.WidthCells, size = scenario.Map.CellSizeMeters;
            long x = (cell % width) * size + size / 2;
            long z = (cell / width) * size + size / 2;
            var core = scenario.Cores[coreIndex].Position;
            long dx = x * 65536L - core.X.Raw, dz = z * 65536L - core.Z.Raw;
            long radius = scenario.Rules.CoreRadius.Raw;
            return dx * dx + dz * dz <= radius * radius;
        }

        private static bool Open(ScenarioDefinition scenario, int cell)
        {
            if (cell < 0 || cell >= scenario.Map.WidthCells * scenario.Map.HeightCells || !InMap(scenario, cell)) return false;
            if (scenario.Map.BlockedCellIds.Contains(cell) || InCore(scenario, cell)) return false;
            return scenario.ResourceNodes.All(node => CellOf(scenario, node.Position) != cell);
        }

        private static bool InMap(ScenarioDefinition scenario, int cell)
            => cell >= 0 && cell < scenario.Map.WidthCells * scenario.Map.HeightCells;

        private static (int[] cells, Facing[] facings) RouteFromNodeToCore(ScenarioDefinition scenario, int nodeCell, int coreIndex = 0)
        {
            int width = scenario.Map.WidthCells, range = 2;
            int ox = nodeCell % width, oz = nodeCell / width;
            foreach (int candidate in Enumerable.Range(0, (range * 2 + 1) * (range * 2 + 1))
                .Select(i => { int dx = i % (range * 2 + 1) - range, dz = i / (range * 2 + 1) - range; return (oz + dz) * width + ox + dx; })
                .Where(c => Open(scenario, c)))
            {
                var previous = new int[scenario.Map.WidthCells * scenario.Map.HeightCells];
                var enteredBy = new Facing[previous.Length];
                Array.Fill(previous, -2);
                var queue = new Queue<int>(); previous[candidate] = -1; queue.Enqueue(candidate);
                while (queue.Count > 0)
                {
                    int cell = queue.Dequeue();
                    foreach (Facing facing in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
                    {
                        int next = Neighbour(scenario, cell, facing);
                        if (InCore(scenario, next, coreIndex))
                        {
                            var cells = new List<int>();
                            for (int at = cell; at >= 0; at = previous[at]) cells.Add(at);
                            cells.Reverse();
                            var facings = new Facing[cells.Count];
                            for (int i = 0; i + 1 < cells.Count; i++) facings[i] = FacingBetween(scenario, cells[i], cells[i + 1]);
                            facings[^1] = facing;
                            return (cells.ToArray(), facings);
                        }
                        if (!Open(scenario, next) || previous[next] != -2) continue;
                        previous[next] = cell; enteredBy[next] = facing; queue.Enqueue(next);
                    }
                }
            }
            return (null, null);
        }

        private static ScenarioDefinition Scenario(bool beltDropOff)
        {
            var scenario = MapGenerator.GenerateTerrain(1207UL, false, true);
            var e = scenario.Economy;
            e.Industry = true;
            e.BeltComponents = true;
            e.BeltDropOff = beltDropOff;
            e.StartFood = 100000;
            e.StartWood = 100000;
            e.VillagerFoodCost = 0;
            e.GatherIntervalTicks = 1;
            e.ToolsGatherTicks = 0;
            e.BeltTicksPerCell = 1;
            e.BeltLimit = 2000;
            e.AutoVillagerTarget = 3;
            e.PopulationCap = 100;
            var node = scenario.ResourceNodes.First(n => n.Kind == ResourceKind.Wood);
            scenario.Villagers[0].Position = node.Position;
            var route = RouteFromNodeToCore(scenario, CellOf(scenario, node.Position));
            Assume.That(route.cells, Is.Not.Null, "a deterministic test route from the wood point to the core");
            scenario.Belts = route.cells.Select((cell, i) => new BeltDefinition
            {
                Cell = cell, FactionId = 1, Facing = route.facings[i]
            }).ToArray();
            return scenario;
        }

        private static void Step(Battle sim, CommandGateway gateway, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static VillagerView Villager(Battle sim, uint id)
            => sim.Capture(1).Economy.Villagers.Single(value => value.Id == id);

        private static void InvalidateReachabilityForTest(Battle sim)
            => typeof(Battle).GetMethod("InvalidateBeltDropOffReachability", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(sim, null);

        private static (ScenarioDefinition scenario, ResourceNodeDefinition node, int[] route, Facing[] facings)
            ManualRoute()
        {
            var scenario = Scenario(true);
            scenario.Economy.AutoVillagerTarget = 0;
            scenario.Economy.BeltDropOffRangeCells = 2;
            scenario.Economy.BeltTicksPerCell = 1;
            scenario.Economy.BeltLimit = 2000;
            scenario.Economy.DropSiteWoodCost = 0;
            scenario.Economy.DropSiteWork = 1;
            var node = scenario.ResourceNodes.First(value => value.Kind == ResourceKind.Wood);
            var route = RouteFromNodeToCore(scenario, CellOf(scenario, node.Position));
            Assume.That(route.cells, Is.Not.Null);
            return (scenario, node, route.cells, route.facings);
        }

        private static void AssignOne(Battle sim, CommandGateway gateway, uint faction, uint villager, uint node, ref ulong sequence)
        {
            gateway.SubmitEconomy(EconomyCommand.Auto(faction, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Assign(faction, ++sequence, new[] { villager },
                EconomyTargetKind.ResourceNode, node));
            Step(sim, gateway, 1);
        }

        private static long DistanceSquared(SimPoint left, SimPoint right)
        {
            long dx = (left.X.Raw - right.X.Raw) / 65536, dz = (left.Z.Raw - right.Z.Raw) / 65536;
            return dx * dx + dz * dz;
        }

        [Test]
        public void Extension76RoundTripsItsFlagAndParameters()
        {
            var scenario = Scenario(true);
            scenario.Economy.BeltDropOffRangeCells = 3;
            scenario.Economy.BeltDropOffWaitTicks = 47;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(value => value.Id == 76);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.BeltDropOff, Is.True);
            Assert.That(decoded.Economy.BeltDropOffRangeCells, Is.EqualTo(3));
            Assert.That(decoded.Economy.BeltDropOffWaitTicks, Is.EqualTo(47));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void VillagerLoadsAConnectedNearbyBeltAndKeepsGatheringAtThePoint()
        {
            var scenario = Scenario(true);
            var node = scenario.ResourceNodes.First(value => value.Kind == ResourceKind.Wood);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new[] { scenario.Villagers[0].Id },
                EconomyTargetKind.ResourceNode, node.Id));
            Step(sim, gateway, 500);
            var fields = Fields(sim);
            Assert.That(long.Parse(fields["Economy[1].Wood"], CultureInfo.InvariantCulture), Is.GreaterThan(scenario.Economy.StartWood));
            Assert.That(fields["Villagers[1].Carry"], Is.EqualTo("0"));
            int remaining = int.Parse(fields["ResourceNodes[" + node.Id.ToString(CultureInfo.InvariantCulture) + "].Remaining"], CultureInfo.InvariantCulture);
            if (remaining > 0)
                Assert.That(new[] { ((byte)1).ToString(CultureInfo.InvariantCulture), ((byte)2).ToString(CultureInfo.InvariantCulture) },
                    Does.Contain(fields["Villagers[1].Task"]));
            else
                Assert.That(fields["Villagers[1].Task"], Is.EqualTo("0"));
            Assert.That(fields["Villagers[1].NodeId"], Is.EqualTo(node.Id.ToString(CultureInfo.InvariantCulture)));
        }

        [Test]
        public void VillagerKeepsGatheringNearAnUnexhaustedPointAfterTheBeltLoadIsGone()
        {
            var scenario = Scenario(true);
            var node = scenario.ResourceNodes.First(value => value.Kind == ResourceKind.Wood);
            int nodeIndex = Array.FindIndex(scenario.ResourceNodes, value => value.Id == node.Id);
            node.Amount = 100000;
            scenario.ResourceNodes[nodeIndex] = node;
            scenario.Villagers[0].Position = node.Position;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new[] { scenario.Villagers[0].Id },
                EconomyTargetKind.ResourceNode, node.Id));
            Step(sim, gateway, 600);
            var fields = Fields(sim);
            Assert.That(new[] { ((byte)1).ToString(CultureInfo.InvariantCulture), ((byte)2).ToString(CultureInfo.InvariantCulture) },
                Does.Contain(fields["Villagers[1].Task"]));
            Assert.That(fields["Villagers[1].NodeId"], Is.EqualTo(node.Id.ToString(CultureInfo.InvariantCulture)));
        }

        [Test]
        public void BothFactionsLoadTheirOwnConnectedBelts()
        {
            var scenario = Scenario(true);
            scenario.Economy.LargeEconomy = true;
            scenario.Economy.AutoVillagerTarget = 0;
            scenario.Economy.BeltTicksPerCell = 1;
            var node1 = scenario.ResourceNodes.First(value => value.Kind == ResourceKind.Wood);
            var node2 = scenario.ResourceNodes.Where(value => value.Kind == ResourceKind.Wood)
                .OrderByDescending(value => DistanceSquared(value.Position, scenario.Cores[1].Position)).First();
            var route1 = RouteFromNodeToCore(scenario, CellOf(scenario, node1.Position), 0);
            var route2 = RouteFromNodeToCore(scenario, CellOf(scenario, node2.Position), 1);
            Assume.That(route1.cells, Is.Not.Null);
            Assume.That(route2.cells, Is.Not.Null);
            Assume.That(route1.cells.Intersect(route2.cells), Is.Empty);
            scenario.Belts = route1.cells.Select((cell, i) => new BeltDefinition { Cell = cell, FactionId = 1, Facing = route1.facings[i] })
                .Concat(route2.cells.Select((cell, i) => new BeltDefinition { Cell = cell, FactionId = 2, Facing = route2.facings[i] })).ToArray();
            var villagers = scenario.Villagers.ToArray();
            var first = villagers.First(value => value.FactionId == 1);
            var second = villagers.First(value => value.FactionId == 2);
            first.Position = node1.Position; second.Position = node2.Position;
            villagers[Array.FindIndex(villagers, value => value.Id == first.Id)] = first;
            villagers[Array.FindIndex(villagers, value => value.Id == second.Id)] = second;
            scenario.Villagers = villagers;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new[] { first.Id }, EconomyTargetKind.ResourceNode, node1.Id));
            gateway.SubmitEconomy(EconomyCommand.Assign(2, ++sequence, new[] { second.Id }, EconomyTargetKind.ResourceNode, node2.Id));
            Step(sim, gateway, 500);
            var observation = sim.CaptureLargeEconomyObservation();
            Assert.That(observation.Factions[0].BeltDropped, Is.GreaterThan(0));
            Assert.That(observation.Factions[1].BeltDropped, Is.GreaterThan(0));
            Assert.That(sim.Capture(1).Economy.Wood, Is.GreaterThan(scenario.Economy.StartWood));
            Assert.That(sim.Capture(2).Economy.Wood, Is.GreaterThan(scenario.Economy.StartWood));
        }

        [Test]
        public void JammedBeltFallsBackToWalkingEveryTickWithinWalkingTime()
        {
            var setup = ManualRoute();
            setup.scenario.Economy.BeltTicksPerCell = 1000;
            setup.scenario.Economy.BeltDropOffWaitTicks = 20;
            setup.scenario.Belts = setup.route.Select((cell, i) => new BeltDefinition
            {
                Cell = cell, FactionId = 1, Facing = setup.facings[i], Item = i == 0 ? ResourceKind.Wood : 0
            }).ToArray();
            var villager = setup.scenario.Villagers.First(value => value.FactionId == 1);
            villager.Position = setup.node.Position;
            setup.scenario.Villagers[Array.FindIndex(setup.scenario.Villagers, value => value.Id == villager.Id)] = villager;
            var jammed = new Battle(setup.scenario);
            var jammedGateway = new CommandGateway(jammed);
            ulong sequence = 0;
            AssignOne(jammed, jammedGateway, 1, villager.Id, setup.node.Id, ref sequence);
            var positions = new List<SimPoint>();
            bool timedOut = false;
            for (int i = 0; i < 3000; i++)
            {
                jammedGateway.Step();
                var view = Villager(jammed, villager.Id);
                var fields = Fields(jammed);
                if (fields.TryGetValue("Villagers[" + villager.Id + "].BeltWaitUntilTick", out string wait) && wait == "-1") timedOut = true;
                if (jammed.Capture(1).Economy.Wood > setup.scenario.Economy.StartWood) break;
                if (timedOut && view.Carry > 0) positions.Add(view.Position);
            }
            Assert.That(timedOut, Is.True);
            Assert.That(positions.Count, Is.GreaterThan(5));
            for (int i = 1; i < positions.Count; i++) Assert.That(positions[i], Is.Not.EqualTo(positions[i - 1]), "walking tick " + i);
        }

        [Test]
        public void FullStorageStillReceivesRawResourcesThroughABelt()
        {
            var scenario = Scenario(true);
            scenario.Belts = Array.Empty<BeltDefinition>();
            scenario.Economy.Ages = true;
            scenario.Economy.AgeClock = false;
            scenario.Economy.AgeClockUx = false;
            scenario.Economy.AiBelts = true;
            scenario.Economy.StorageCapacity = 1;
            scenario.Economy.StorageWoodCost = 0;
            scenario.Economy.StorageWork = 1;
            scenario.Economy.AdvanceFoodCost = scenario.Economy.AdvanceWoodCost = 0;
            scenario.Economy.AdvanceTicks = 1;
            var node = scenario.ResourceNodes.First(value => value.Kind == ResourceKind.Wood);
            var worker = scenario.Villagers.First(value => value.FactionId == 1);
            worker.Position = node.Position;
            scenario.Villagers[Array.FindIndex(scenario.Villagers, value => value.Id == worker.Id)] = worker;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Metallurgy));
            Step(sim, gateway, 2);
            int nodeCell = CellOf(scenario, node.Position), width = scenario.Map.WidthCells;
            uint storageId = 0; int storageOrigin = -1;
            for (int radius = 2; radius <= 10 && storageId == 0; radius++)
                for (int dz = -radius; dz <= radius && storageId == 0; dz++)
                    for (int dx = -radius; dx <= radius && storageId == 0; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius) continue;
                        int candidate = nodeCell + dz * width + dx;
                        long before = sim.Capture(1).Economy.Buildings.Count;
                        gateway.SubmitEconomy(EconomyCommand.PlaceStorage(1, ++sequence, candidate));
                        Step(sim, gateway, 1);
                        var placed = sim.Capture(1).Economy.Buildings.LastOrDefault(value => value.Kind == BuildingKind.Storage);
                        if (sim.Capture(1).Economy.Buildings.Count > before) { storageId = placed.Id; storageOrigin = candidate; }
                    }
            Assert.That(storageId, Is.Not.EqualTo(0u), "a storage site near the wood point");
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new[] { 1u, 2u, 3u }, EconomyTargetKind.Building, storageId));
            Step(sim, gateway, 10);
            int beltCell = -1; Facing beltFacing = Facing.North;
            foreach (Facing facing in new[] { Facing.East, Facing.North, Facing.West, Facing.South })
            {
                int candidate = Neighbour(scenario, storageOrigin, (Facing)(((int)facing + 2) % 4));
                if (Open(scenario, candidate) && !scenario.Map.BlockedCellIds.Contains(candidate)) { beltCell = candidate; beltFacing = facing; break; }
            }
            Assert.That(beltCell, Is.GreaterThanOrEqualTo(0), "an input cell beside storage");
            gateway.SubmitEconomy(EconomyCommand.PlaceBelt(1, ++sequence, new[] { beltCell }, new[] { beltFacing }));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new[] { worker.Id }, EconomyTargetKind.ResourceNode, node.Id));
            Step(sim, gateway, 1000);
            Assert.That(sim.Capture(1).Economy.Wood, Is.GreaterThan(scenario.Economy.StartWood));
        }

        [Test]
        public void DropOffBuildingChangesMatchAForcedReachabilityInvalidation()
        {
            var leftScenario = ManualRoute().scenario;
            var rightScenario = ScenarioBinary.Decode(ScenarioBinary.Encode(leftScenario));
            var route = leftScenario.Belts.Select(value => value.Cell).ToHashSet();
            int width = leftScenario.Map.WidthCells, size = leftScenario.Economy.DropSiteSizeCells;
            var probe = new Battle(ScenarioBinary.Decode(ScenarioBinary.Encode(leftScenario)));
            var probeGateway = new CommandGateway(probe);
            ulong probeSequence = 0;
            probeGateway.SubmitEconomy(EconomyCommand.Auto(1, ++probeSequence, false));
            probeGateway.Step();
            int origin = -1;
            foreach (int cell in Enumerable.Range(0, leftScenario.Map.WidthCells * leftScenario.Map.HeightCells)
                .OrderBy(cell => Math.Abs(cell % width - CellOf(leftScenario, leftScenario.Cores[0].Position) % width)
                    + Math.Abs(cell / width - CellOf(leftScenario, leftScenario.Cores[0].Position) / width)))
            {
                int x = cell % width, z = cell / width;
                bool footprintOpen = Enumerable.Range(0, size).All(dz => Enumerable.Range(0, size).All(dx =>
                {
                    int at = (z + dz) * width + x + dx;
                    return at >= 0 && at < width * leftScenario.Map.HeightCells && Open(leftScenario, at) && !route.Contains(at);
                }));
                if (!footprintOpen) continue;
                int before = probe.Capture(1).Economy.Buildings.Count;
                probeGateway.SubmitEconomy(EconomyCommand.Place(1, ++probeSequence, BuildingKind.DropSite, cell, Facing.North));
                probeGateway.Step();
                if (probe.Capture(1).Economy.Buildings.Count > before) { origin = cell; break; }
            }
            Assert.That(origin, Is.GreaterThanOrEqualTo(0), "a completed-dropoff placement candidate");
            var left = new Battle(leftScenario);
            var right = new Battle(rightScenario);
            var leftGateway = new CommandGateway(left);
            var rightGateway = new CommandGateway(right);
            ulong sequence = 0;
            leftGateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            rightGateway.SubmitEconomy(EconomyCommand.Auto(1, sequence, false));
            leftGateway.Step(); rightGateway.Step();
            leftGateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.DropSite, origin, Facing.North));
            rightGateway.SubmitEconomy(EconomyCommand.Place(1, sequence, BuildingKind.DropSite, origin, Facing.North));
            leftGateway.Step(); rightGateway.Step();
            var building = left.Capture(1).Economy.Buildings.Single(value => value.Kind == BuildingKind.DropSite);
            var rightBuilding = right.Capture(1).Economy.Buildings.Single(value => value.Kind == BuildingKind.DropSite);
            leftGateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new[] { 1u, 2u, 3u }, EconomyTargetKind.Building, building.Id));
            rightGateway.SubmitEconomy(EconomyCommand.Assign(1, sequence, new[] { 1u, 2u, 3u }, EconomyTargetKind.Building, rightBuilding.Id));
            for (long tick = left.Capture(1).Tick + 1; tick <= 500; tick++)
            {
                leftGateway.Step();
                rightGateway.Step();
                InvalidateReachabilityForTest(right);
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
            Assert.That(left.Capture(1).Economy.Buildings.Any(value => value.Kind == BuildingKind.DropSite && value.Complete), Is.True);
            leftGateway.SubmitEconomy(EconomyCommand.RemoveBuilding(1, ++sequence, building.Id));
            rightGateway.SubmitEconomy(EconomyCommand.RemoveBuilding(1, sequence, rightBuilding.Id));
            for (int i = 0; i < 3; i++)
            {
                leftGateway.Step();
                rightGateway.Step();
                InvalidateReachabilityForTest(right);
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "remove tick " + i);
            }
            Assert.That(left.Capture(1).Economy.Buildings.Any(value => value.Kind == BuildingKind.DropSite), Is.False);
        }

        [Test]
        public void RebuildingBeltReachabilityReusesItsWorkspaces()
        {
            var setup = ManualRoute();
            var sim = new Battle(setup.scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            AssignOne(sim, gateway, 1, 1, setup.node.Id, ref sequence);
            Step(sim, gateway, 100);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            var reachable = typeof(Battle).GetMethod("BeltDropOffReachable", BindingFlags.Instance | BindingFlags.NonPublic);
            reachable.Invoke(sim, new object[] { (int)ResourceKind.Wood, 1u });
            for (int i = 0; i < 20; i++)
            {
                InvalidateReachabilityForTest(sim);
                reachable.Invoke(sim, new object[] { (int)ResourceKind.Wood, 1u });
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.Progress.WriteLine("BeltDropOffReachability rebuild allocation=" + allocated + " bytes / 20 = " + allocated / 20.0 + " bytes");
            Assert.That(allocated / 20.0, Is.LessThanOrEqualTo(4096), "rebuilds allocated " + allocated + " bytes");
        }

        [Test]
        public void FlagOffDoesNotWriteBeltWaitStateToTheDiagnostic()
        {
            var fields = Fields(new Battle(Scenario(false)));
            Assert.That(fields.Keys.Any(key => key.EndsWith(".BeltWaitUntilTick", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void FlagOffKeepsTheExistingTickHashesAndReplayPath()
        {
            var left = new Battle(Scenario(false));
            var right = new Battle(Scenario(false));
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState.ToArray()),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState.ToArray())), "tick " + tick);
            }
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, Scenario(false), Array.Empty<ScheduledInput>(), 300,
                    new BuildIdentity());
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }
    }
}
