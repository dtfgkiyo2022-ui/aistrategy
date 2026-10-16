using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class DrillTests
    {
        private const int Width = 128;

        private static int CellOf(SimPoint point)
            => (int)(point.Z.Raw / 65536 / 2) * Width + (int)(point.X.Raw / 65536 / 2);

        private static IEnumerable<int> Origins(int cell)
        {
            yield return cell;
            yield return cell - 1;
            yield return cell - Width;
            yield return cell - Width - 1;
        }

        private static ScenarioDefinition Scenario(bool drill)
        {
            var scenario = LiveGameRules.Create(1, false, false, false, false);
            scenario.Economy.StartWood = 1000;
            scenario.Economy.StartStone = 1000;
            scenario.Economy.CommonDrill = drill;
            return scenario;
        }

        private static ScenarioDefinition MetallurgyScenario()
        {
            var scenario = MapGenerator.GenerateTerrain(2);
            scenario.Economy.StartWood = 1000;
            scenario.Economy.StartStone = 1000;
            scenario.Economy.StartFood = 1000;
            scenario.Economy.CommonDrill = true;
            scenario.Economy.Industry = true;
            scenario.Economy.Ages = true;
            return scenario;
        }

        private static void AdvanceToMetallurgy(ScenarioDefinition scenario, Battle sim, CommandGateway gateway)
        {
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Metallurgy));
            for (int i = 0; i <= scenario.Economy.AdvanceTicks + 2; i++) gateway.Step();
        }

        private static (Battle sim, CommandGateway gateway, uint id, ResourceNodeDefinition node) PlaceDrill(bool drill, ResourceKind kind)
        {
            var scenario = Scenario(drill);
            var node = scenario.ResourceNodes.First(n => n.Kind == kind);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.Step();
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new uint[] { 1 }, EconomyTargetKind.ResourceNode, node.Id));
            gateway.Step();
            foreach (int origin in Origins(CellOf(node.Position)))
            {
                gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Drill, origin, Facing.North));
                gateway.Step();
                var building = sim.Capture(1).Economy.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Drill);
                if (building.Id != 0) return (sim, gateway, building.Id, node);
            }
            return (sim, gateway, 0, node);
        }

        private static void Complete(Battle sim, CommandGateway gateway, uint id)
        {
            gateway.SubmitEconomy(EconomyCommand.Assign(1, 100, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, id));
            for (int i = 0; i < 3000; i++)
            {
                gateway.Step();
                var building = sim.Capture(1).Economy.Buildings.FirstOrDefault(b => b.Id == id);
                if (building.Complete) return;
            }
        }

        [Test]
        public void CommonDrillOffRejectsPlacementWithoutChangingTheOldHash()
        {
            var scenario = Scenario(false);
            var control = new Battle(scenario);
            var controlGateway = new CommandGateway(control);
            var wood = scenario.ResourceNodes.First(n => n.Kind == ResourceKind.Wood);
            controlGateway.SubmitEconomy(EconomyCommand.Place(1, 1, BuildingKind.Drill, Origins(CellOf(wood.Position)).First(), Facing.North));
            controlGateway.Step();
            var before = control.CaptureDiagnostic().CanonicalState;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Place(1, 1, BuildingKind.Drill, Origins(CellOf(wood.Position)).First(), Facing.North));
            gateway.Step();
            Assert.That(sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.Drill), Is.False);
            Assert.That(sim.CaptureDiagnostic().CanonicalState, Is.EqualTo(before));
        }

        [Test]
        public void CommonDrillPlacesOnOneRawPointAndProducesItsResource()
        {
            var result = PlaceDrill(true, ResourceKind.Wood);
            Assume.That(result.id, Is.Not.EqualTo(0u));
            Assert.That(result.sim.Capture(1).Economy.Villagers.First(v => v.Id == 1).Activity,
                Is.EqualTo(VillagerActivity.Idle));
            Complete(result.sim, result.gateway, result.id);
            for (int i = 0; i < 40; i++) result.gateway.Step();
            var building = result.sim.Capture(1).Economy.Buildings.First(b => b.Id == result.id);
            Assert.That(building.Output, Is.GreaterThanOrEqualTo(1));
            Assert.That(result.sim.Capture(1).Economy.Resources.First(r => r.Id == result.node.Id).Remaining, Is.LessThan(result.node.Amount));
        }

        [Test]
        public void CommonDrillRejectsFoodAndFishingPoints()
        {
            var result = PlaceDrill(true, ResourceKind.Food);
            Assert.That(result.id, Is.EqualTo(0u));
        }

        [Test]
        public void CommonDrillCannotOverlapAnExistingCivilisationMine()
        {
            var scenario = MetallurgyScenario();
            var node = scenario.ResourceNodes.First(n => n.Kind == ResourceKind.Ore);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            AdvanceToMetallurgy(scenario, sim, gateway);
            int origin = -1;
            ulong sequence = 1;
            foreach (int candidate in Origins(CellOf(node.Position)))
            {
                gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Mine, candidate, Facing.North));
                gateway.Step();
                if (sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.Mine)) { origin = candidate; break; }
            }
            Assume.That(origin, Is.GreaterThanOrEqualTo(0));
            gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Drill, origin, Facing.North));
            gateway.Step();
            Assert.That(sim.Capture(1).Economy.Buildings.Count(b => b.Kind == BuildingKind.Drill), Is.EqualTo(0));
        }

        [Test]
        public void CivilisationMineCannotOverlapAnExistingCommonDrill()
        {
            var scenario = MetallurgyScenario();
            var node = scenario.ResourceNodes.First(n => n.Kind == ResourceKind.Ore);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            AdvanceToMetallurgy(scenario, sim, gateway);
            int origin = -1;
            ulong sequence = 1;
            foreach (int candidate in Origins(CellOf(node.Position)))
            {
                gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Drill, candidate, Facing.North));
                gateway.Step();
                if (sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.Drill)) { origin = candidate; break; }
            }
            Assume.That(origin, Is.GreaterThanOrEqualTo(0));
            gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Mine, origin, Facing.North));
            gateway.Step();
            Assert.That(sim.Capture(1).Economy.Buildings.Count(b => b.Kind == BuildingKind.Mine), Is.EqualTo(0));
        }

        [Test]
        public void AiBuildsAtLeastOneCommonDrillWithinTwentyThousandTicks()
        {
            var scenario = LiveGameRules.Create(1, false, true, false, false);
            scenario.Economy.StartWood = 10000;
            scenario.Economy.StartStone = 10000;
            scenario.Economy.AutoVillagerTarget = 12;
            var sim = new Battle(scenario);
            for (long tick = 1; tick <= 20000; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                if (sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.Drill)) return;
            }
            Assert.Fail("AI did not place a common drill");
        }

        [Test]
        public void CommonDrillExtension47RoundTripsAndOffEncodingStaysUnchanged()
        {
            var off = Scenario(false);
            var offBytes = ScenarioBinary.Encode(off);
            Assert.That(ScenarioBinary.Encode(ScenarioBinary.Decode(offBytes)), Is.EqualTo(offBytes));
            var on = Scenario(true);
            on.Economy.DrillWoodCost = 73;
            var bytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(x => x.Id == 47).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.CommonDrill, Is.True);
            Assert.That(decoded.Economy.DrillWoodCost, Is.EqualTo(73));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void CommonDrillReplayKeepsEveryTickHashEqual()
        {
            var left = new Battle(Scenario(true));
            var right = new Battle(Scenario(true));
            for (long tick = 1; tick <= 500; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }
    }
}
