using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class LargeEconomyTests
    {
        [Test]
        public void LargeEconomyUsesTheLargeMapProfileAndExtension59()
        {
            var normal = MapGenerator.GenerateLarge(1);
            var large = MapGenerator.GenerateLarge(1, processingChain: true, largeEconomy: true);

            Assert.That(normal.Economy.LargeEconomy, Is.False);
            Assert.That(large.Economy.LargeEconomy, Is.True);
            Assert.That(large.Economy.PopulationCap, Is.EqualTo(1400));
            Assert.That(large.Economy.LargeFactionCap, Is.EqualTo(1400));
            Assert.That(large.Economy.LargeVillagerBatch, Is.GreaterThan(1));
            Assert.That(large.Economy.LargeInfantryBatch, Is.GreaterThan(1));
            Assert.That(large.Armies.All(army => army.Capacity == large.Economy.LargeArmyCapacity), Is.True);
            Assert.That(large.ResourceNodes.Sum(node => node.Amount), Is.GreaterThan(normal.ResourceNodes.Sum(node => node.Amount)));

            var bytes = ScenarioBinary.Encode(large);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.LargeEconomy, Is.True);
            Assert.That(decoded.Economy.LargeVillagerTrainTicks, Is.EqualTo(large.Economy.LargeVillagerTrainTicks));
            Assert.That(decoded.Economy.LargeArmyCapacity, Is.EqualTo(large.Economy.LargeArmyCapacity));
            Assert.That(decoded.Economy.LargeTownVillagersPerTown, Is.EqualTo(large.Economy.LargeTownVillagersPerTown));
            Assert.That(decoded.Economy.LargeBarracksVillagersPerBuilding, Is.EqualTo(large.Economy.LargeBarracksVillagersPerBuilding));
            Assert.That(decoded.Economy.LargeTownAutoWorkers, Is.EqualTo(large.Economy.LargeTownAutoWorkers));
            Assert.That(decoded.Economy.LargeTargetRoundTripTicks, Is.EqualTo(large.Economy.LargeTargetRoundTripTicks));
            Assert.That(decoded.Economy.LargeResourceClusterRadiusMeters, Is.EqualTo(large.Economy.LargeResourceClusterRadiusMeters));
            Assert.That(decoded.Economy.LargeClusterDecisionIntervalTicks, Is.EqualTo(large.Economy.LargeClusterDecisionIntervalTicks));
            Assert.That(decoded.Economy.LargeResourceClusterMaxDropSites, Is.EqualTo(large.Economy.LargeResourceClusterMaxDropSites));
            Assert.That(decoded.Economy.LargeDropSiteMax, Is.EqualTo(large.Economy.LargeDropSiteMax));
            Assert.That(decoded.Economy.LargeClusterMinGatherers, Is.EqualTo(large.Economy.LargeClusterMinGatherers));
            Assert.That(decoded.Extensions.Count(extension => extension.Id == 59), Is.EqualTo(1));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var normalBytes = ScenarioBinary.Encode(normal);
            Assert.That(ScenarioBinary.Decode(normalBytes).Extensions.Any(extension => extension.Id == 59), Is.False);
        }

        [Test]
        public void LiveLargeMapEnablesCapacityTownsAndBatchProductionRules()
        {
            var scenario = LiveGameRules.Create(7, true, true, false, false);
            var rules = scenario.Economy;
            var ordinary = LiveGameRules.Create(7, false, true, false, false).Economy;

            Assert.That(rules.LargeEconomy, Is.True);
            Assert.That(rules.Towns && rules.ArmyGrowth && rules.EconomyScale, Is.True);
            Assert.That(rules.PopulationCap, Is.EqualTo(1400));
            Assert.That(rules.LargeBarracksMaxBuildings, Is.EqualTo(16));
            Assert.That(rules.LargeArmyCapacity, Is.EqualTo(300));
            Assert.That(rules.LargeTownDecisionIntervalTicks, Is.GreaterThan(0));
            Assert.That(rules.LargeTownVillagersPerTown, Is.EqualTo(80));
            Assert.That(rules.LargeBarracksVillagersPerBuilding, Is.EqualTo(20));
            Assert.That(rules.LargeTownAutoWorkers, Is.EqualTo(4));
            Assert.That(rules.LargeTargetRoundTripTicks, Is.EqualTo(300));
            Assert.That(rules.LargeResourceClusterRadiusMeters, Is.EqualTo(24));
            Assert.That(rules.LargeClusterDecisionIntervalTicks, Is.EqualTo(100));
            Assert.That(rules.LargeResourceClusterMaxDropSites, Is.EqualTo(8));
            Assert.That(rules.LargeDropSiteMax, Is.EqualTo(8));
            Assert.That(rules.DropSiteMax, Is.EqualTo(8));
            Assert.That(rules.LargeClusterMinGatherers, Is.EqualTo(2));
            Assert.That(scenario.Rules.FactionCap, Is.EqualTo(1400));
            Assert.That((rules.VillagerFoodCost, rules.InfantryFoodCost, rules.InfantryWoodCost,
                rules.BarracksWoodCost, rules.TownWoodCost, rules.TownStoneCost, rules.TownWork,
                rules.HouseWoodCost, rules.HouseWork),
                Is.EqualTo((ordinary.VillagerFoodCost, ordinary.InfantryFoodCost, ordinary.InfantryWoodCost,
                    ordinary.BarracksWoodCost, ordinary.TownWoodCost, ordinary.TownStoneCost, ordinary.TownWork,
                    ordinary.HouseWoodCost, ordinary.HouseWork)));
            var decodedEconomy = ScenarioBinary.Decode(ScenarioBinary.Encode(scenario)).Economy;
            Assert.That(decodedEconomy.LargeDropSiteMax, Is.EqualTo(rules.LargeDropSiteMax));
            Assert.That((decodedEconomy.LargeTownVillagersPerTown, decodedEconomy.LargeBarracksVillagersPerBuilding,
                decodedEconomy.LargeTownAutoWorkers, decodedEconomy.LargeTargetRoundTripTicks,
                decodedEconomy.LargeResourceClusterRadiusMeters, decodedEconomy.LargeClusterDecisionIntervalTicks,
                decodedEconomy.LargeResourceClusterMaxDropSites, decodedEconomy.LargeClusterMinGatherers),
                Is.EqualTo((rules.LargeTownVillagersPerTown, rules.LargeBarracksVillagersPerBuilding, rules.LargeTownAutoWorkers,
                    rules.LargeTargetRoundTripTicks, rules.LargeResourceClusterRadiusMeters, rules.LargeClusterDecisionIntervalTicks,
                    rules.LargeResourceClusterMaxDropSites, rules.LargeClusterMinGatherers)));
        }

        [Test]
        public void LargeEconomyObservationIsReadOnly()
        {
            var simulation = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var before = ReplayBinary.Hash(simulation.CaptureDiagnostic().CanonicalState);
            var observation = simulation.CaptureLargeEconomyObservation();
            var after = ReplayBinary.Hash(simulation.CaptureDiagnostic().CanonicalState);
            Assert.That(after, Is.EqualTo(before));
            Assert.That(observation.Factions.Length, Is.EqualTo(2));
            Assert.That(observation.Factions[0].Workers.Length, Is.EqualTo(6));
            Assert.That(observation.Factions[0].Income.Length, Is.EqualTo(6));
        }

        [Test]
        public void LargeEconomySimulationIsDeterministic()
        {
            var left = new Battle(LiveGameRules.Create(11, true, false, false, false));
            var right = new Battle(LiveGameRules.Create(11, true, false, false, false));
            for (long tick = 1; tick <= 2400; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                if (tick % 200 == 0)
                    Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                        Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }
    }
}
