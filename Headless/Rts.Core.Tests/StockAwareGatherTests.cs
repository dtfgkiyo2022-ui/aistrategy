using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class StockAwareGatherTests
    {
        private static ScenarioDefinition Scenario()
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            var e = scenario.Economy;
            e.StockAwareGather = true; e.AiExpand = true;
            e.ResourceFog = false; e.ExploreScouting = false; e.FastMovement = false;
            e.BeltDropOff = false; e.BeltDropOffAi = false; e.BeltStack = false; e.BeltCraft = false;
            e.GatherFoodHigh = 3000; e.GatherWoodHigh = 3000; e.GatherStoneHigh = 2000;
            e.GatherFoodLow = 400; e.GatherWoodLow = 400; e.GatherStoneLow = 200;
            e.GatherLowSharePermille = 1500;
            e.GatherSwitchPerInterval = 1; e.GatherSwitchIntervalTicks = 200;
            return scenario;
        }

        [Test]
        public void FlagOffKeepsEveryTickHashAndOmitsExtension84()
        {
            var leftScenario = Scenario(); leftScenario.Economy.StockAwareGather = false;
            var rightScenario = Scenario(); rightScenario.Economy.StockAwareGather = false;
            var left = new Battle(leftScenario); var right = new Battle(rightScenario);
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(leftScenario)).Extensions.Any(x => x.Id == 84), Is.False);
            for (long tick = 1; tick <= 1000; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void Extension84RoundTripsAllStockAwareRules()
        {
            var scenario = Scenario();
            var e = scenario.Economy;
            e.GatherFoodHigh = 3100; e.GatherWoodHigh = 3200; e.GatherStoneHigh = 2100;
            e.GatherFoodLow = 410; e.GatherWoodLow = 420; e.GatherStoneLow = 220;
            e.GatherSwitchPerInterval = 11; e.GatherSwitchIntervalTicks = 201;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            var d = decoded.Economy;
            Assert.That(decoded.Extensions.Count(x => x.Id == 84 && x.Version == 2), Is.EqualTo(1));
            Assert.That((d.StockAwareGather, d.GatherFoodHigh, d.GatherWoodHigh, d.GatherStoneHigh,
                d.GatherFoodLow, d.GatherWoodLow, d.GatherStoneLow, d.GatherLowSharePermille,
                d.GatherSwitchPerInterval, d.GatherSwitchIntervalTicks),
                Is.EqualTo((true, 3100, 3200, 2100, 410, 420, 220, 1500, 11, 201)));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void VersionOneExtensionDefaultsLowShareAndUpgradesOnWrite()
        {
            var versionTwo = ScenarioBinary.Encode(Scenario());
            byte[] marker = BitConverter.GetBytes(0x4E545845);
            int markerOffset = -1;
            for (int i = 0; i <= versionTwo.Length - marker.Length; i++)
                if (versionTwo.Skip(i).Take(marker.Length).SequenceEqual(marker)) { markerOffset = i; break; }
            Assert.That(markerOffset, Is.GreaterThanOrEqualTo(0));

            int sectionLength = BitConverter.ToInt32(versionTwo, markerOffset + 8);
            int record = markerOffset + 12;
            int targetRecord = -1, targetData = -1, targetLength = 0;
            int sectionEnd = markerOffset + 8 + sectionLength;
            while (record < sectionEnd)
            {
                int id = BitConverter.ToInt32(versionTwo, record);
                int length = BitConverter.ToInt32(versionTwo, record + 8);
                if (id == 84) { targetRecord = record; targetData = record + 12; targetLength = length; break; }
                record += 12 + length;
            }
            Assert.That(targetRecord, Is.GreaterThanOrEqualTo(0));
            Assert.That(targetLength, Is.EqualTo(10 * sizeof(int)));
            int removeAt = targetData + targetLength - sizeof(int);
            var versionOne = new byte[versionTwo.Length - sizeof(int)];
            Array.Copy(versionTwo, 0, versionOne, 0, removeAt);
            Array.Copy(versionTwo, removeAt + sizeof(int), versionOne, removeAt, versionTwo.Length - removeAt - sizeof(int));
            Array.Copy(BitConverter.GetBytes(sectionLength - sizeof(int)), 0, versionOne, markerOffset + 8, sizeof(int));
            Array.Copy(BitConverter.GetBytes(1), 0, versionOne, targetRecord + 4, sizeof(int));
            Array.Copy(BitConverter.GetBytes(9 * sizeof(int)), 0, versionOne, targetRecord + 8, sizeof(int));

            var decoded = ScenarioBinary.Decode(versionOne);
            Assert.That(decoded.Extensions.Single(x => x.Id == 84).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.GatherLowSharePermille, Is.EqualTo(1500));
            var upgraded = ScenarioBinary.Decode(ScenarioBinary.Encode(decoded));
            Assert.That(upgraded.Extensions.Single(x => x.Id == 84).Version, Is.EqualTo(2));
            Assert.That(upgraded.Economy.GatherLowSharePermille, Is.EqualTo(1500));
        }

        [Test]
        public void LowStoneUsesExpandedShareInsteadOfTakingEveryIdleVillager()
        {
            var scenario = Scenario();
            scenario.Economy.AiExpandShareMinVillagers = 0;
            scenario.Economy.AiExpandWoodWorkerPermille = 0;
            scenario.Economy.AiExpandTownStoneWorkerPermille = 0;
            scenario.Economy.AiExpandAge1TownCount = 0;
            scenario.Economy.AiExpandAge2TownCount = 0;
            scenario.Economy.AiExpandAge3TownCount = 0;
            scenario.Economy.TownStoneCost = 0;
            var simulation = new Battle(scenario);
            simulation.SetStockAwareWorkTestState(1, 3000, 1000, 199);
            Assert.That(simulation.TryStockAwareSwitchForTest(1, ResourceKind.Stone, false), Is.False);
            Assert.That(simulation.TryStockAwareSwitchForTest(2, ResourceKind.Stone, false), Is.False);
            Assert.That(simulation.SelectStockAwareWorkKindForTest(1), Is.Not.EqualTo(ResourceKind.Stone));
        }

        [Test]
        public void HighFoodVillagerSwitchesToUnderShareWoodAndRespectsCap()
        {
            var scenario = Scenario();
            scenario.Economy.AiExpandShareMinVillagers = 0;
            var simulation = new Battle(scenario);
            simulation.SetStockAwareWorkTestState(1, 3000, 1000, 1000);
            Assert.That(simulation.TryStockAwareSwitchForTest(1, ResourceKind.Food, false), Is.True);
            Assert.That(simulation.StockAwareCurrentWorkKindForTest(1), Is.EqualTo(ResourceKind.Wood));
            Assert.That(simulation.TryStockAwareSwitchForTest(2, ResourceKind.Food, false), Is.False);
        }

        [Test]
        public void StockAwareSwitchIsLimitedPerInterval()
        {
            var scenario = Scenario();
            scenario.Economy.AiExpandShareMinVillagers = 0;
            var simulation = new Battle(scenario);
            simulation.SetStockAwareWorkTestState(1, 3000, 399, 1000);
            // The share policy is active for this small fixture so the destination is an under-share resource.
            // (The production default remains the 20-villager floor.)
            
            Assert.That(simulation.TryStockAwareSwitchForTest(1, ResourceKind.Food, false), Is.True);
            Assert.That(simulation.TryStockAwareSwitchForTest(2, ResourceKind.Food, false), Is.False);
        }

        [Test]
        public void HeldVillagerIsNotStockAwareSwitched()
        {
            var simulation = new Battle(Scenario());
            simulation.SetStockAwareWorkTestState(1, 3000, 399, 1000);
            Assert.That(simulation.TryStockAwareSwitchForTest(1, ResourceKind.Food, true), Is.False);
        }

        [Test]
        public void HighFoodStopsNewFarmButFoodBelowThresholdStillPlacesOne()
        {
            var high = Scenario();
            high.Economy.GatherFoodHigh = 1000000;
            high.Economy.StartFood = high.Economy.GatherFoodHigh;
            high.Economy.StartWood = 10000;
            high.Economy.FarmWoodCost = 1;
            high.Economy.AutoVillagerTarget = 0;
            var highSimulation = new Battle(high);
            for (long tick = 1; tick <= 40; tick++) highSimulation.Step(tick, Array.Empty<ScheduledInput>());
            var highObservation = highSimulation.CaptureLargeEconomyObservation().Factions[0];
            Assert.That(highObservation.Farms, Is.EqualTo(0));
            Assert.That(highObservation.FoodHighFarmSkipped, Is.GreaterThan(0));

            var low = Scenario();
            low.Economy.GatherFoodHigh = 1000000;
            low.Economy.StartFood = low.Economy.GatherFoodHigh - 1;
            low.Economy.StartWood = 10000;
            low.Economy.FarmWoodCost = 1;
            low.Economy.AutoVillagerTarget = 0;
            var lowSimulation = new Battle(low);
            for (long tick = 1; tick <= 40; tick++) lowSimulation.Step(tick, Array.Empty<ScheduledInput>());
            Assert.That(lowSimulation.Capture(1).Economy.Buildings.Count(b => b.Kind == BuildingKind.Farm), Is.GreaterThan(0));
        }
    }
}
