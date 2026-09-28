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
    public sealed class FishingTests
    {
        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> fields, string name)
            => long.Parse(fields[name], CultureInfo.InvariantCulture);

        private static ResourceNodeDefinition FishingNode(ScenarioDefinition scenario)
        {
            long reach = Fix64.FromInt(scenario.Economy.FishReach).Raw;
            long limit = checked(reach * reach);
            for (int i = 0; i < scenario.ResourceNodes.Length; i++)
            {
                var node = scenario.ResourceNodes[i];
                if (node.Kind != ResourceKind.Food) continue;
                for (int cell = 0; cell < scenario.Map.Terrain.Length; cell++)
                {
                    if (scenario.Map.Terrain[cell] != (byte)TerrainKind.River) continue;
                    long centreX = Fix64.FromInt((cell % scenario.Map.WidthCells) * scenario.Map.CellSizeMeters + scenario.Map.CellSizeMeters / 2).Raw;
                    long centreZ = Fix64.FromInt((cell / scenario.Map.WidthCells) * scenario.Map.CellSizeMeters + scenario.Map.CellSizeMeters / 2).Raw;
                    long dx = node.Position.X.Raw - centreX, dz = node.Position.Z.Raw - centreZ;
                    if (checked(dx * dx + dz * dz) <= limit) return node;
                }
            }
            Assert.Fail("The generated terrain has no food node within fishing reach of a river cell.");
            return default;
        }

        private static ScenarioDefinition FishingScenario(int amount, int gatherInterval, int regrowTicks)
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            var fish = FishingNode(scenario);
            var node = scenario.ResourceNodes[fish.Id - 1];
            node.Amount = amount;
            scenario.ResourceNodes[fish.Id - 1] = node;
            scenario.Economy.FishingEnabled = true;
            scenario.Economy.FishRegrowTicks = regrowTicks;
            scenario.Economy.GatherIntervalTicks = gatherInterval;
            scenario.Economy.ToolsGatherTicks = 0;
            scenario.Economy.AutoVillagerTarget = 0;
            scenario.Economy.StartFood = 5000;
            scenario.Economy.StartWood = 5000;
            scenario.Economy.AdvanceTicks = 1;
            scenario.Villagers[0].Position = fish.Position;
            return scenario;
        }

        private static void Step(Battle sim, CommandGateway gateway)
        {
            gateway.Step();
            Assert.That(sim.Capture(1).Result.IsFault, Is.False);
        }

        [Test]
        public void FishingDefaultsKeepScenarioBytesAndCanonicalHashesUnchanged()
        {
            var implicitDefaults = MapGenerator.GenerateTerrain(1);
            var explicitDefaults = MapGenerator.GenerateTerrain(1);
            explicitDefaults.Economy.FishingEnabled = false;
            explicitDefaults.Economy.FishRegrowTicks = 100;
            explicitDefaults.Economy.FishAgrarianBonusPermille = 300;
            explicitDefaults.Economy.FishReach = 6;
            Assert.That(ScenarioBinary.Encode(explicitDefaults), Is.EqualTo(ScenarioBinary.Encode(implicitDefaults)));

            var left = new Battle(implicitDefaults);
            var right = new Battle(explicitDefaults);
            for (long tick = 1; tick <= 400; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void FishingRegrowsOneAtEachMultipleEvenAfterItReachesZero()
        {
            var scenario = FishingScenario(1, 1, 3);
            var fish = FishingNode(scenario);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            Step(sim, gateway);
            gateway.SubmitEconomy(EconomyCommand.Assign(1, 2, new[] { 1U }, EconomyTargetKind.ResourceNode, fish.Id));
            Step(sim, gateway); // tick 2: the villager arrives at the node and starts its clock
            Assert.That(Number(Fields(sim), "ResourceNodes[" + fish.Id + "].Remaining"), Is.EqualTo(1));
            Step(sim, gateway); // tick 3: gather to zero
            Assert.That(Number(Fields(sim), "ResourceNodes[" + fish.Id + "].Remaining"), Is.EqualTo(0));
            Step(sim, gateway); Step(sim, gateway); // ticks 4-5: still empty
            Assert.That(Number(Fields(sim), "ResourceNodes[" + fish.Id + "].Remaining"), Is.EqualTo(0));
            Step(sim, gateway); // tick 6: one fish returns, even though it was empty
            Assert.That(Number(Fields(sim), "ResourceNodes[" + fish.Id + "].Remaining"), Is.EqualTo(1));
            Step(sim, gateway); Step(sim, gateway); Step(sim, gateway); // tick 9: capped at the initial amount
            Assert.That(Number(Fields(sim), "ResourceNodes[" + fish.Id + "].Remaining"), Is.EqualTo(1));
        }

        [Test]
        public void AgrarianVillagersGatherFishingFasterThanMetallurgyVillagers()
        {
            var agrarian = FishingScenario(100, 20, 100000);
            var metallurgy = FishingScenario(100, 20, 100000);
            var agrarianFish = FishingNode(agrarian);
            var metallurgyFish = FishingNode(metallurgy);
            var agrarianSim = new Battle(agrarian);
            var metallurgySim = new Battle(metallurgy);
            var agrarianGateway = new CommandGateway(agrarianSim);
            var metallurgyGateway = new CommandGateway(metallurgySim);

            agrarianGateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            metallurgyGateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            Step(agrarianSim, agrarianGateway); Step(metallurgySim, metallurgyGateway);
            agrarianGateway.SubmitEconomy(EconomyCommand.Advance(1, 2, CivKind.Agrarian));
            metallurgyGateway.SubmitEconomy(EconomyCommand.Advance(1, 2, CivKind.Metallurgy));
            Step(agrarianSim, agrarianGateway); Step(metallurgySim, metallurgyGateway);
            Assert.That(Fields(agrarianSim)["Economy[1].Civ"], Is.EqualTo(((byte)CivKind.Agrarian).ToString(CultureInfo.InvariantCulture)));
            Assert.That(Fields(metallurgySim)["Economy[1].Civ"], Is.EqualTo(((byte)CivKind.Metallurgy).ToString(CultureInfo.InvariantCulture)));
            agrarianGateway.SubmitEconomy(EconomyCommand.Assign(1, 3, new[] { 1U }, EconomyTargetKind.ResourceNode, agrarianFish.Id));
            metallurgyGateway.SubmitEconomy(EconomyCommand.Assign(1, 3, new[] { 1U }, EconomyTargetKind.ResourceNode, metallurgyFish.Id));
            Step(agrarianSim, agrarianGateway); Step(metallurgySim, metallurgyGateway); // tick 3
            for (int i = 0; i < 14; i++) { Step(agrarianSim, agrarianGateway); Step(metallurgySim, metallurgyGateway); }

            Assert.That(Number(Fields(agrarianSim), "ResourceNodes[" + agrarianFish.Id + "].Remaining"), Is.EqualTo(99), "14 ticks after assignment");
            Assert.That(Number(Fields(metallurgySim), "ResourceNodes[" + metallurgyFish.Id + "].Remaining"), Is.EqualTo(100), "the old interval remains 20 ticks");
        }

        [Test]
        public void FishingScenarioReplaysWithEveryTickEqual()
        {
            var scenario = FishingScenario(2, 1, 3);
            var fish = FishingNode(scenario);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            Step(sim, gateway);
            gateway.SubmitEconomy(EconomyCommand.Assign(1, 2, new[] { 1U }, EconomyTargetKind.ResourceNode, fish.Id));
            for (int i = 0; i < 12; i++) Step(sim, gateway);

            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, gateway.Inputs, sim.Capture(1).Tick, build);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, build);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        [Test]
        public void FishingCountsAreReportedForTerrainSeedsOneToEight()
        {
            for (ulong seed = 1; seed <= 8; seed++)
            {
                var scenario = MapGenerator.GenerateTerrain(seed);
                scenario.Economy.FishingEnabled = true;
                var sim = new Battle(scenario);
                var fields = Fields(sim);
                var fishing = scenario.ResourceNodes.Where(n => fields["ResourceNodes[" + n.Id + "].Fishing"] == "1").ToArray();
                int[] near = scenario.Cores.Select(core => fishing.Count(n => Within(n.Position, core.Position, 30))).ToArray();
                TestContext.WriteLine("seed " + seed + ": fishing " + fishing.Length + ", west<=30m " + near[0] + ", east<=30m " + near[1]);
            }
        }

        private static bool Within(SimPoint a, SimPoint b, int meters)
        {
            long dx = a.X.Raw - b.X.Raw, dz = a.Z.Raw - b.Z.Raw, reach = Fix64.FromInt(meters).Raw;
            return checked(dx * dx + dz * dz) <= checked(reach * reach);
        }
    }
}
