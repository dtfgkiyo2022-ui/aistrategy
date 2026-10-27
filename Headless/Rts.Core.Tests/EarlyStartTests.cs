using System;
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
    public sealed class EarlyStartTests
    {
        [Test]
        public void FlagOffMatchesTheEncodedScenarioEveryTick()
        {
            var source = LiveGameRules.Create(2, true, true, false, false);
            LiveGameRules.SetEarlyStart(source, false);
            var left = new Battle(source);
            var right = new Battle(ScenarioBinary.Decode(ScenarioBinary.Encode(source)));
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void Extension73RoundTripsEarlyStartRules()
        {
            var source = LiveGameRules.Create(2, true, true, false, false);
            source.Economy.EarlyStartVillagers = 11;
            source.Economy.EarlyStartVillagersPerFarm = 6;
            source.Economy.EarlyStartPrimitiveFarmMax = 7;
            byte[] bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Any(x => x.Id == 73 && x.Version == 1), Is.True);
            Assert.That(decoded.Economy.EarlyStart, Is.True);
            Assert.That(decoded.Economy.EarlyStartVillagers, Is.EqualTo(11));
            Assert.That(decoded.Economy.EarlyStartVillagersPerFarm, Is.EqualTo(6));
            Assert.That(decoded.Economy.EarlyStartPrimitiveFarmMax, Is.EqualTo(7));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void LargeMapStartsWithTenAndSmallMapStaysAtThree()
        {
            var large = LiveGameRules.Create(2, true, true, false, false);
            var largeSimulation = new Battle(large);
            Assert.That(large.Economy.EarlyStart, Is.True);
            Assert.That(largeSimulation.Capture(1).Economy.Villagers.Count(v => v.IsOwn), Is.EqualTo(10));
            Assert.That(largeSimulation.Capture(2).Economy.Villagers.Count(v => v.IsOwn), Is.EqualTo(10));

            LiveGameRules.SetEarlyStart(large, false);
            var offSimulation = new Battle(large);
            Assert.That(offSimulation.Capture(1).Economy.Villagers.Count(v => v.IsOwn), Is.EqualTo(3));
            Assert.That(offSimulation.Capture(2).Economy.Villagers.Count(v => v.IsOwn), Is.EqualTo(3));

            var small = LiveGameRules.Create(2, false, true, false, false);
            var smallBefore = new Battle(small).Capture(1).Economy.Villagers.Count(v => v.IsOwn);
            LiveGameRules.SetEarlyStart(small, true);
            var smallAfter = new Battle(small).Capture(1).Economy.Villagers.Count(v => v.IsOwn);
            Assert.That(small.Economy.EarlyStart, Is.False);
            Assert.That(smallBefore, Is.EqualTo(3));
            Assert.That(smallAfter, Is.EqualTo(3));
        }

        [Test]
        public void AutomaticPrimitiveFarmIsFirstAndFollowsEarlyPopulationTarget()
        {
            var scenario = FastLargeScenario();
            var simulation = new Battle(scenario);
            BuildingKind first = 0;
            for (long tick = 1; tick <= 1000; tick++)
            {
                simulation.Step(tick, Array.Empty<ScheduledInput>());
                var buildings = simulation.Capture(1).Economy.Buildings.OrderBy(b => b.Id).ToArray();
                if (buildings.Length != 0) { first = buildings[0].Kind; break; }
            }
            Assert.That(first, Is.EqualTo(BuildingKind.Farm));
            for (long tick = simulation.Capture(1).Tick + 1; tick <= 3000; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());
            var economy = simulation.Capture(1).Economy;
            Assert.That(economy.Villagers.Count(v => v.IsOwn), Is.GreaterThanOrEqualTo(25));
            Assert.That(economy.Buildings.Count(b => b.Kind == BuildingKind.Farm && b.Complete), Is.GreaterThanOrEqualTo(5));
        }

        [Test]
        public void NonAgrarianAgeOneFarmTargetCanExceedFour()
        {
            var scenario = FastLargeScenario();
            scenario.Economy.LargeVillagersPerFarm = 10;
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Forestry));
            for (int i = 0; i < 5000 && !simulation.Capture(1).Result.HasEnded; i++) gateway.Step();
            var economy = simulation.Capture(1).Economy;
            Assert.That(economy.Age, Is.GreaterThanOrEqualTo(1));
            Assert.That(economy.Villagers.Count(v => v.IsOwn), Is.GreaterThanOrEqualTo(50));
            Assert.That(economy.Buildings.Count(b => b.Kind == BuildingKind.Farm && b.Complete), Is.GreaterThan(4));
        }

        [Test]
        public void EarlyStartRecordReplayMatchesEveryTick()
        {
            using (var stream = new MemoryStream())
            {
                var scenario = FastLargeScenario();
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 3000, identity);
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, identity);
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }

        private static ScenarioDefinition FastLargeScenario()
        {
            var scenario = LiveGameRules.Create(68003, true, true, false, false);
            var e = scenario.Economy;
            e.StartFood = e.StartWood = e.StartStone = e.StartMetal = 1000000;
            e.VillagerFoodCost = 0;
            e.VillagerTrainTicks = e.LargeVillagerTrainTicks = 1;
            e.LargeVillagerBatch = 20;
            e.AutoVillagerTarget = 100;
            e.AutoInfantryQueue = 0;
            e.FarmWoodCost = 0;
            e.FarmWork = 1;
            e.FarmBaseTicks = e.FarmMinTicks = 1;
            e.FarmStepTicks = 0;
            e.AdvanceFoodCost = e.AdvanceWoodCost = 100000000;
            e.AgeClock = false;
            e.AgeClockUx = false;
            e.BarracksWoodCost = e.HutWoodCost = e.HouseWoodCost = 100000000;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000000;
            return scenario;
        }
    }
}
