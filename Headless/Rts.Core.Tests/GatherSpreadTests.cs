using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class GatherSpreadTests
    {
        private static ScenarioDefinition Scenario(params ResourceNodeDefinition[] nodes)
        {
            var scenario = LiveGameRules.Create(86001UL, true, true, false, false);
            LiveGameRules.SetEarlyStart(scenario, false);
            scenario.Map.Terrain = new byte[scenario.Map.WidthCells * scenario.Map.HeightCells];
            scenario.Map.BlockedCellIds = Array.Empty<int>();
            scenario.ResourceNodes = nodes;
            var core = scenario.Cores[0].Position;
            scenario.Villagers = new VillagerDefinition[20];
            for (uint i = 0; i < scenario.Villagers.Length; i++)
                scenario.Villagers[i] = new VillagerDefinition { Id = i + 1, FactionId = 1, Position = core };
            var e = scenario.Economy;
            e.GatherSpread = true;
            e.StockAwareGather = false;
            e.ResourceFog = false; e.ExploreScouting = false; e.FastMovement = false;
            e.AutoVillagerTarget = 0; e.AutoInfantryQueue = 0;
            e.AiFactory = false; e.AiExpand = false; e.AiBeltParts = false;
            e.BeltDropOff = false; e.BeltDropOffAi = false; e.BeltStack = false; e.BeltCraft = false;
            e.GatherNodeWorkerCap = 12; e.GatherNodeRemainingPerWorker = 50;
            return scenario;
        }

        private static SimPoint At(SimPoint origin, int x = 0, int z = 0)
            => new SimPoint(Fix64.FromRaw(origin.X.Raw + Fix64.FromInt(x).Raw),
                Fix64.FromRaw(origin.Z.Raw + Fix64.FromInt(z).Raw));

        private static ResourceNodeDefinition Node(uint id, ResourceKind kind, SimPoint position, int amount)
            => new ResourceNodeDefinition { Id = id, Kind = kind, Position = position, Amount = amount };

        [Test]
        public void SmallPointAcceptsAtMostTwoWorkers()
        {
            var core = LiveGameRules.Create(86001UL, true, true, false, false).Cores[0].Position;
            var scenario = Scenario(Node(1, ResourceKind.Food, At(core, 5), 139),
                Node(2, ResourceKind.Food, At(core, 60), 1000),
                Node(3, ResourceKind.Food, At(core, 100), 1000));
            var simulation = new Battle(scenario);
            for (uint id = 1; id <= 20; id++) Assert.That(simulation.AssignGatherSpreadWorkForTest(id, ResourceKind.Food), Is.Not.EqualTo(0));
            var fields = DiagnosticComparison.Fields(simulation.CaptureDiagnostic()).ToDictionary(x => x.Key, x => x.Value);
            int first = fields.Keys.Count(key => key.StartsWith("Villagers[", StringComparison.Ordinal)
                && key.EndsWith(".NodeId", StringComparison.Ordinal) && fields[key] == "1");
            Assert.That(first, Is.LessThanOrEqualTo(2));
        }

        [Test]
        public void FullNearestClusterMovesToNextCluster()
        {
            var core = LiveGameRules.Create(86001UL, true, true, false, false).Cores[0].Position;
            var scenario = Scenario(Node(1, ResourceKind.Food, At(core, 5), 50),
                Node(2, ResourceKind.Food, At(core, 10), 50),
                Node(3, ResourceKind.Food, At(core, 70), 1000));
            var simulation = new Battle(scenario);
            Assert.That(simulation.AssignGatherSpreadWorkForTest(1, ResourceKind.Food), Is.EqualTo(1));
            Assert.That(simulation.AssignGatherSpreadWorkForTest(2, ResourceKind.Food), Is.EqualTo(2));
            Assert.That(simulation.AssignGatherSpreadWorkForTest(3, ResourceKind.Food), Is.EqualTo(3));
        }

        [Test]
        public void AllPointsFullStillAssignsNearestPoint()
        {
            var core = LiveGameRules.Create(86001UL, true, true, false, false).Cores[0].Position;
            var simulation = new Battle(Scenario(Node(1, ResourceKind.Food, At(core, 5), 1000)));
            for (uint id = 1; id <= 12; id++)
                Assert.That(simulation.AssignGatherSpreadWorkForTest(id, ResourceKind.Food), Is.EqualTo(1));
            Assert.That(simulation.AssignGatherSpreadWorkForTest(13, ResourceKind.Food), Is.EqualTo(1));
        }

        [Test]
        public void VillagerOnBlockedCellUsesAdjacentPassableCell()
        {
            var core = LiveGameRules.Create(86001UL, true, true, false, false).Cores[0].Position;
            var scenario = Scenario(Node(1, ResourceKind.Food, At(core, 5), 1000));
            scenario.Economy.AiExpand = true;
            var grid = new GridMap(scenario.Map);
            var simulation = new Battle(scenario);
            simulation.SetPassableForTest(grid.Cell(core), false);
            Assert.That(simulation.AssignGatherSpreadWorkForTest(1, ResourceKind.Food), Is.EqualTo(1));
        }

        [Test]
        public void MissingWoodUsesLowestStockRatioStoneFirst()
        {
            var core = LiveGameRules.Create(86001UL, true, true, false, false).Cores[0].Position;
            var simulation = new Battle(Scenario(Node(1, ResourceKind.Food, At(core, 5), 1000),
                Node(2, ResourceKind.Stone, At(core, 8), 1000)));
            simulation.SetStockAwareWorkTestState(1, 800, 500, 100);
            Assert.That(simulation.AssignGatherSpreadWorkForTest(1, ResourceKind.Wood), Is.EqualTo(2));
        }

        [Test]
        public void MissingAllGatherResourcesLeavesVillagerIdle()
        {
            var simulation = new Battle(Scenario());
            Assert.That(simulation.AssignGatherSpreadWorkForTest(1, ResourceKind.Wood), Is.EqualTo(0));
        }

        [Test]
        public void FlagOffPreservesEveryTickHashAndOmitsExtension86()
        {
            var leftScenario = Scenario(Node(1, ResourceKind.Food, At(LiveGameRules.Create(86001UL, true, true, false, false).Cores[0].Position), 1000));
            leftScenario.Economy.GatherSpread = false;
            var rightScenario = Scenario(Node(1, ResourceKind.Food, At(LiveGameRules.Create(86001UL, true, true, false, false).Cores[0].Position), 1000));
            rightScenario.Economy.GatherSpread = false;
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(leftScenario)).Extensions.Any(x => x.Id == 86), Is.False);
            var left = new Battle(leftScenario); var right = new Battle(rightScenario);
            for (long tick = 1; tick <= 1000; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void Extension86RoundTripsGatherSpreadRules()
        {
            var core = LiveGameRules.Create(86001UL, true, true, false, false).Cores[0].Position;
            var scenario = Scenario(Node(1, ResourceKind.Food, At(core, 5), 1000));
            scenario.Economy.GatherNodeWorkerCap = 9;
            scenario.Economy.GatherNodeRemainingPerWorker = 61;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Count(x => x.Id == 86 && x.Version == 1), Is.EqualTo(1));
            Assert.That((decoded.Economy.GatherSpread, decoded.Economy.GatherNodeWorkerCap,
                decoded.Economy.GatherNodeRemainingPerWorker), Is.EqualTo((true, 9, 61)));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }
    }
}
