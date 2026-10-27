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

        private static (int[] cells, Facing[] facings) RouteFromNodeToCore(ScenarioDefinition scenario, int nodeCell)
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
                        if (InCore(scenario, next))
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
