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
            Assert.That(scenario.Rules.FactionCap, Is.EqualTo(1400));
            Assert.That((rules.VillagerFoodCost, rules.InfantryFoodCost, rules.InfantryWoodCost,
                rules.BarracksWoodCost, rules.TownWoodCost, rules.TownStoneCost, rules.TownWork,
                rules.HouseWoodCost, rules.HouseWork),
                Is.EqualTo((ordinary.VillagerFoodCost, ordinary.InfantryFoodCost, ordinary.InfantryWoodCost,
                    ordinary.BarracksWoodCost, ordinary.TownWoodCost, ordinary.TownStoneCost, ordinary.TownWork,
                    ordinary.HouseWoodCost, ordinary.HouseWork)));
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
