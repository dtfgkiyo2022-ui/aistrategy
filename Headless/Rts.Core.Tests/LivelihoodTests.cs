using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class LivelihoodTests
    {
        private static ScenarioDefinition Scenario(bool livelihood)
        {
            var s = MapGenerator.GenerateTerrain(9017UL, true, true);
            s.Economy.Ages = true; s.Economy.Industry = true;
            s.Economy.AgeClock = false;
            s.Economy.Preserves = livelihood; s.Economy.Tools = livelihood;
            s.Economy.TradeGoods = livelihood; s.Economy.Incense = livelihood;
            s.Economy.Livelihood = livelihood;
            s.Economy.StartFood = 100; s.Economy.StartWood = 1000;
            s.Economy.AdvanceFoodCost = 0; s.Economy.AdvanceWoodCost = 0; s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0; s.Economy.Age2WoodCost = 0; s.Economy.Age2Ticks = 1;
            s.Economy.Age3FoodCost = 0; s.Economy.Age3WoodCost = 0; s.Economy.Age3Ticks = 1;
            s.Economy.BarracksWoodCost = 1; s.Economy.BarracksWork = 1;
            s.Economy.VillagerFoodCost = 0;
            s.Cores[0].Hp = s.Cores[1].Hp = 1000000;
            return s;
        }

        private static Dictionary<string, string> Fields(Battle simulation)
            => DiagnosticComparison.Fields(simulation.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static object World(Battle simulation)
            => typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);

        private static void SetEconomy(Battle simulation, string name, object value)
        {
            var world = World(simulation);
            var economies = (Array)world.GetType().GetField("Economies", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(world);
            var economy = economies.GetValue(0);
            economy.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(economy, value);
            economies.SetValue(economy, 0);
        }

        [Test]
        public void Extension89RoundTripsAllTenIntegers()
        {
            var scenario = Scenario(true);
            scenario.Economy.LivelihoodPreservesPer100 = 13;
            scenario.Economy.LivelihoodToolsPer100 = 7;
            scenario.Economy.LivelihoodTradeGoodsPer100 = 9;
            scenario.Economy.LivelihoodIncensePer100 = 5;
            scenario.Economy.LivelihoodDecisionIntervalTicks = 1200;
            scenario.Economy.LivelihoodTrainingTimePermille = 750;
            scenario.Economy.LivelihoodUnmetHousePermille = 500;
            scenario.Economy.LivelihoodAiMarginPermille = 1250;
            scenario.Economy.LivelihoodAiDecisionIntervalTicks = 600;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(x => x.Id == 89).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.LivelihoodPreservesPer100, Is.EqualTo(13));
            Assert.That(decoded.Economy.LivelihoodToolsPer100, Is.EqualTo(7));
            Assert.That(decoded.Economy.LivelihoodTradeGoodsPer100, Is.EqualTo(9));
            Assert.That(decoded.Economy.LivelihoodIncensePer100, Is.EqualTo(5));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void OffDoesNotAddLivelihoodDiagnosticFields()
        {
            var fields = Fields(new Battle(Scenario(false)));
            Assert.That(fields.Keys.Any(k => k.Contains("Livelihood", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void AgeTwoPaysPreservesAndToolsAtTheMinuteBoundary()
        {
            var scenario = Scenario(true);
            var simulation = new Battle(scenario);
            SetEconomy(simulation, "Age", (byte)2);
            SetEconomy(simulation, "Civ", CivKind.Metropolis);
            SetEconomy(simulation, "AutoOff", true);
            SetEconomy(simulation, "LivelihoodObservedAge", (byte)2);
            SetEconomy(simulation, "Preserves", 1);
            SetEconomy(simulation, "Tools", 1);
            for (long tick = 1; tick <= 1200; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            var fields = Fields(simulation);
            Assert.That(fields["Economy[1].LivelihoodMet"], Is.EqualTo("2"));
            Assert.That(fields["Economy[1].LivelihoodRequired"], Is.EqualTo("2"));
            Assert.That(fields["Economy[1].LivelihoodShortGoods"], Is.EqualTo("0"));
            Assert.That(fields["Economy[1].LivelihoodPreservesPaid"], Is.EqualTo("1"));
            Assert.That(fields["Economy[1].LivelihoodToolsPaid"], Is.EqualTo("1"));
        }

        private static T Call<T>(Battle simulation, string name, params object[] args)
        {
            var method = typeof(Battle).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(m => m.Name == name && m.GetParameters().Length == args.Length);
            return (T)method.Invoke(simulation, args);
        }

        [Test]
        public void UnmetNeedsShrinkHousesByTheDesignFormulaAndMetNeedsRestoreThem()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, "Age", (byte)2);
            SetEconomy(simulation, "LivelihoodMet", true);
            SetEconomy(simulation, "LivelihoodRequiredCount", 2);
            SetEconomy(simulation, "LivelihoodMetCount", 2);
            int full = Call<int>(simulation, "LivelihoodHousePopulation", 1u, 100);
            SetEconomy(simulation, "LivelihoodMet", false);
            SetEconomy(simulation, "LivelihoodMetCount", 1);
            int half = Call<int>(simulation, "LivelihoodHousePopulation", 1u, 100);
            SetEconomy(simulation, "LivelihoodMetCount", 0);
            int none = Call<int>(simulation, "LivelihoodHousePopulation", 1u, 100);
            Assert.That(full, Is.EqualTo(100));
            Assert.That(half, Is.EqualTo(75), "(500 + 500 * 1 / 2) / 1000");
            Assert.That(none, Is.EqualTo(50));
            SetEconomy(simulation, "Age", (byte)1);
            Assert.That(Call<int>(simulation, "LivelihoodHousePopulation", 1u, 100), Is.EqualTo(100), "age 1 has no needs");
        }

        [Test]
        public void MetNeedsCutVillagerTrainingToThreeQuartersRoundedUp()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, "Age", (byte)2);
            SetEconomy(simulation, "Civ", CivKind.Agrarian);
            SetEconomy(simulation, "LivelihoodMet", false);
            int normal = Call<int>(simulation, "VillagerTrainTicksFor", 1u, false);
            SetEconomy(simulation, "LivelihoodMet", true);
            int met = Call<int>(simulation, "VillagerTrainTicksFor", 1u, false);
            Assert.That(met, Is.EqualTo((normal * 750 + 999) / 1000));
        }

        [Test]
        public void EveryCivMayBuildTheFourWorkshopsOneAgeEarlyOnlyWithTheFlag()
        {
            foreach (bool on in new[] { true, false })
            {
                var simulation = new Battle(Scenario(on));
                SetEconomy(simulation, "Civ", CivKind.Agrarian);
                SetEconomy(simulation, "Age", (byte)1);
                Assert.That(Call<bool>(simulation, "PreservesAllowed", 1u), Is.EqualTo(on));
                Assert.That(Call<bool>(simulation, "ToolsAllowed", 1u), Is.EqualTo(on));
                Assert.That(Call<bool>(simulation, "TradeGoodsAllowed", 1u), Is.False, "weaver from age 2");
                SetEconomy(simulation, "Age", (byte)2);
                Assert.That(Call<bool>(simulation, "TradeGoodsAllowed", 1u), Is.EqualTo(on));
                Assert.That(Call<bool>(simulation, "IncenseAllowed", 1u), Is.False, "incense house from age 3");
                SetEconomy(simulation, "Age", (byte)3);
                Assert.That(Call<bool>(simulation, "IncenseAllowed", 1u), Is.EqualTo(on));
            }
        }

        [Test]
        public void KeptPreservesAndTradeGoodsStopAtThreeMinutesOfNeed()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, "Civ", CivKind.Agrarian);
            SetEconomy(simulation, "LivelihoodVillagers", 200);
            // 200 villagers: preserves 20 a minute, trade goods 12 a minute.
            SetEconomy(simulation, "Preserves", 59);
            Assert.That(Call<bool>(simulation, "LivelihoodGoodFull", 1u, ResourceKind.Preserves), Is.False);
            SetEconomy(simulation, "Preserves", 60);
            Assert.That(Call<bool>(simulation, "LivelihoodGoodFull", 1u, ResourceKind.Preserves), Is.True);
            SetEconomy(simulation, "TradeGoods", 36);
            Assert.That(Call<bool>(simulation, "LivelihoodGoodFull", 1u, ResourceKind.TradeGoods), Is.True);
            SetEconomy(simulation, "Civ", CivKind.Fishing);
            Assert.That(Call<bool>(simulation, "LivelihoodGoodFull", 1u, ResourceKind.Preserves), Is.False, "fishing turns the surplus into food");
            var off = new Battle(Scenario(false));
            SetEconomy(off, "Preserves", 1000);
            Assert.That(Call<bool>(off, "LivelihoodGoodFull", 1u, ResourceKind.Preserves), Is.False);
        }

        [Test]
        public void FishingNeedsHalfThePreserves()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, "Civ", CivKind.Fishing);
            Assert.That(Call<int>(simulation, "LivelihoodNeed", 1u, ResourceKind.Preserves, 200), Is.EqualTo(10));
            SetEconomy(simulation, "Civ", CivKind.Agrarian);
            Assert.That(Call<int>(simulation, "LivelihoodNeed", 1u, ResourceKind.Preserves, 200), Is.EqualTo(20));
        }

        [Test]
        public void FirstMinuteAfterAgeTransitionKeepsPreviousResult()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, "Age", (byte)2);
            SetEconomy(simulation, "Civ", CivKind.Metropolis);
            SetEconomy(simulation, "AutoOff", true);
            SetEconomy(simulation, "LivelihoodObservedAge", (byte)1);
            SetEconomy(simulation, "Preserves", 1);
            SetEconomy(simulation, "Tools", 1);
            for (long tick = 1; tick <= 1200; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            var fields = Fields(simulation);
            Assert.That(fields["Economy[1].LivelihoodPreservesPaid"], Is.EqualTo("0"));
            Assert.That(fields["Economy[1].LivelihoodToolsPaid"], Is.EqualTo("0"));
        }
    }
}
