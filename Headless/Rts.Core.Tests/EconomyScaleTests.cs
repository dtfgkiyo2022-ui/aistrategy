using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Decision;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class EconomyScaleTests
    {
        private static ScenarioDefinition Scenario(bool scale, bool armyGrowth = false)
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.EconomyScale = scale;
            scenario.Economy.ArmyGrowth = armyGrowth;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 1000000;
            for (int i = 0; i < scenario.UnitParameters.Length; i++)
            {
                var parameters = scenario.UnitParameters[i];
                parameters.Damage = 0;
                scenario.UnitParameters[i] = parameters;
            }
            scenario.Economy.AdvanceFoodCost = 1;
            scenario.Economy.AdvanceWoodCost = 1;
            scenario.Economy.AdvanceTicks = 1;
            scenario.Economy.Age2FoodCost = 1;
            scenario.Economy.Age2WoodCost = 1;
            scenario.Economy.Age2Ticks = 1000;
            scenario.Economy.Age3FoodCost = 1;
            scenario.Economy.Age3WoodCost = 1;
            scenario.Economy.Age3Ticks = 1000;
            return scenario;
        }

        private static void Run(Battle simulation, long lastTick)
        {
            for (long tick = 1; tick <= lastTick && !simulation.Capture(1).Result.HasEnded; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());
        }

        private static void ConfigureFastEconomy(EconomyRules rules)
        {
            rules.StartFood = 100000;
            rules.StartWood = 100000;
            rules.VillagerFoodCost = 1;
            rules.VillagerTrainTicks = 1;
            rules.BarracksWoodCost = 1;
            rules.BarracksWork = 1;
            rules.Builders = 60;
            rules.HouseWoodCost = 1;
            rules.HouseWork = 1;
            rules.InfantryFoodCost = 1;
            rules.InfantryWoodCost = 1;
            rules.InfantryTrainTicks = 1;
            rules.AutoInfantryQueue = 2;
        }

        private static void AddWestVillagers(ScenarioDefinition scenario, int total)
        {
            if (total <= scenario.Villagers.Count(value => value.FactionId == 1)) return;
            var villagers = new VillagerDefinition[scenario.Villagers.Length + total - scenario.Villagers.Count(value => value.FactionId == 1)];
            Array.Copy(scenario.Villagers, villagers, scenario.Villagers.Length);
            uint id = (uint)scenario.Villagers.Length + 1;
            int count = scenario.Villagers.Count(value => value.FactionId == 1);
            while (count < total)
            {
                villagers[id - 1] = new VillagerDefinition { Id = id, FactionId = 1, Position = scenario.Cores[0].Position };
                id++; count++;
            }
            scenario.Villagers = villagers;
        }

        [TestCase(false, 0, 20)]
        [TestCase(false, 1, 20)]
        [TestCase(false, 2, 20)]
        [TestCase(true, 0, 20)]
        [TestCase(true, 1, 40)]
        [TestCase(true, 2, 60)]
        public void VillagerTargetCurveIsStable(bool scale, int age, int expected)
        {
            Assert.That(EconomyDecision.VillagerTarget(scale, age, 20, false), Is.EqualTo(expected));
            Assert.That(EconomyDecision.VillagerTarget(scale, age, 20, true), Is.EqualTo(expected * 3 / 2));
        }

        [Test]
        public void EconomyScaleRaisesFoodGatheringRatioOnlyWhenEnabled()
        {
            Assert.That(EconomyDecision.FoodPerWood(false, 2), Is.EqualTo(2));
            Assert.That(EconomyDecision.FoodPerWood(true, 2), Is.EqualTo(3));
            Assert.That(EconomyDecision.FoodPerWood(true, 3), Is.EqualTo(3));
            Assert.That(EconomyDecision.FoodSourceTarget(false, 2), Is.EqualTo(2));
            Assert.That(EconomyDecision.FoodSourceTarget(true, 2), Is.EqualTo(4));
            Assert.That(EconomyDecision.KindToGather(3, 1, EconomyDecision.FoodPerWood(false, 2)), Is.EqualTo(ResourceKind.Wood));
            Assert.That(EconomyDecision.KindToGather(3, 1, EconomyDecision.FoodPerWood(true, 2)), Is.EqualTo(ResourceKind.Food));
        }

        [Test]
        public void FlagOffKeepsBytesHashesVillagerTargetAndOneBarracks()
        {
            var leftScenario = Scenario(false);
            var rightScenario = Scenario(false);
            Assert.That(ScenarioBinary.Encode(leftScenario), Is.EqualTo(ScenarioBinary.Encode(rightScenario)));
            var left = new Battle(leftScenario);
            var right = new Battle(rightScenario);
            for (long tick = 1; tick <= 6000; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
                if (left.Capture(1).Result.HasEnded) break;
            }
            var economy = left.Capture(1).Economy;
            Assert.That(economy.Villagers.Count(v => v.IsOwn), Is.LessThanOrEqualTo(leftScenario.Economy.AutoVillagerTarget));
            Assert.That(economy.Buildings.Count(v => v.FactionId == 1 && v.Kind == BuildingKind.Barracks), Is.EqualTo(1));
        }

        [Test]
        public void EconomyScaleExtensionRoundTripsAndIsAbsentWhenOff()
        {
            var on = Scenario(true);
            var bytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.EconomyScale, Is.True);
            Assert.That(decoded.Extensions.Single(value => value.Id == 12).Version, Is.EqualTo(1));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var off = Scenario(false);
            off.Extensions = new[] { new ScenarioExtensionData { Id = 12, Version = 1, Data = BitConverter.GetBytes(1) } };
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(off)).Extensions.Any(value => value.Id == 12), Is.False);
        }

        [Test]
        public void EconomyScaleRaisesVillagerTargetByAgeAndUsesFourBarracks()
        {
            var scenario = Scenario(true, true);
            ConfigureFastEconomy(scenario.Economy);
            AddWestVillagers(scenario, 60);
            scenario.Economy.InfantryTrainTicks = 200;
            var simulation = new Battle(scenario);
            bool sawMultipleQueues = false;
            for (long tick = 1; tick <= 12000 && !simulation.Capture(1).Result.HasEnded; tick++)
            {
                simulation.Step(tick, Array.Empty<ScheduledInput>());
                var economy = simulation.Capture(1).Economy;
                var barracks = economy.Buildings.Where(value => value.FactionId == 1 && value.Kind == BuildingKind.Barracks && value.Complete).ToArray();
                if (barracks.Length >= 4 && barracks.Count(value => value.Queued > 0) >= 2) sawMultipleQueues = true;
            }
            var finalEconomy = simulation.Capture(1).Economy;
            Assert.That(finalEconomy.Buildings.Count(value => value.FactionId == 1 && value.Kind == BuildingKind.Barracks), Is.EqualTo(4));
            Assert.That(sawMultipleQueues, Is.True, "more than one barracks trained");
        }

        [Test]
        public void EconomyScaleBuildsHousingToTheTwoHundredPopulationCap()
        {
            var scenario = Scenario(true, true);
            ConfigureFastEconomy(scenario.Economy);
            scenario.Economy.HousePopulation = 170;
            AddWestVillagers(scenario, 60);
            var simulation = new Battle(scenario);
            Run(simulation, 20000);
            var economy = simulation.Capture(1).Economy;
            Assert.That(economy.PopulationCap, Is.EqualTo(200), "age=" + economy.Age + ", population=" + economy.Population + ", wood=" + economy.Wood + ", houses=" + economy.Buildings.Count(value => value.FactionId == 1 && value.Kind == BuildingKind.House));
            Assert.That(economy.Buildings.Count(value => value.FactionId == 1 && value.Kind == BuildingKind.House && value.Complete), Is.GreaterThan(0));
        }

        [Test]
        public void EconomyScaleReplayMatchesEveryTickForTwentyThousandTicks()
        {
            var scenario = Scenario(true, true);
            var inputs = PolicyPresets.RecordedInputs(scenario, "maintain", "maintain", 20000);
            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, inputs, 20000, build);
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, build);
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
                Assert.That(result.LastTick, Is.EqualTo(20000));
            }
        }
    }
}
