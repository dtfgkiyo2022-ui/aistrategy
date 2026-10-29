using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class ArmsScaleTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(bool enabled)
        {
            var scenario = LiveGameRules.Create(78001UL, true, true, false, false);
            var e = scenario.Economy;
            e.CraftRequired = true;
            e.ArmsScale = enabled;
            e.AutoInfantryQueue = 0;
            e.AutoVillagerTarget = 0;
            e.StartFood = e.StartWood = e.StartStone = e.StartMetal = 100000;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000000;
            return scenario;
        }

        [Test]
        public void FlagOffIsHashStableAndExtension78IsAbsent()
        {
            var source = Scenario(false);
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Any(x => x.Id == 78), Is.False);
            var left = new Battle(source);
            var right = new Battle(decoded);
            for (long tick = 1; tick <= 300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void Extension78RoundTripsAllArmsScaleRules()
        {
            var source = Scenario(true);
            var e = source.Economy;
            e.ArmsScaleDecisionIntervalTicks = 601;
            e.ArmsScaleArmsStockLow = 3;
            e.ArmsScaleFoodReserve = 1501;
            e.ArmsScaleWoodReserve = 1001;
            e.ArmsScaleMaxArmsAssemblersAge2 = 7;
            e.ArmsScaleMaxArmsAssemblersAge3 = 11;
            e.ArmsScaleMaxArmsAssemblersAge4 = 13;
            e.ArmsScalePlankLow = 41;
            e.ArmsScaleExtraSawmillsAge2 = 5;
            e.ArmsScaleExtraSawmillsAge3 = 7;
            e.ArmsScaleExtraSawmillsAge4 = 9;
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            Assert.That(decoded.Extensions.Single(x => x.Id == 78).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.ArmsScale, Is.True);
            Assert.That(decoded.Economy.ArmsScaleDecisionIntervalTicks, Is.EqualTo(601));
            Assert.That(decoded.Economy.ArmsScaleMaxArmsAssemblersAge4, Is.EqualTo(13));
            Assert.That(decoded.Economy.ArmsScalePlankLow, Is.EqualTo(41));
            Assert.That(decoded.Economy.ArmsScaleExtraSawmillsAge4, Is.EqualTo(9));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(ScenarioBinary.Encode(source)));
        }

        [Test]
        public void AgeTwoArmsScaleAddsAnAssemblerAndKeepsItsArmsRecipe()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, 0, "Age", 2);
            SetEconomy(simulation, 0, "Food", 10000);
            SetEconomy(simulation, 0, "Wood", 10000);
            SetEconomy(simulation, 0, "Stone", 10000);
            SetEconomy(simulation, 0, "Planks", 10000);
            SetEconomy(simulation, 0, "Frames", 10000);
            SetEconomy(simulation, 0, "Fittings", 10000);
            SetEconomy(simulation, 0, "Rope", 10000);
            SetEconomy(simulation, 0, "ArmsKit", 0);
            SetEconomy(simulation, 0, "FineArmsKit", 0);
            for (long tick = 1; tick <= 2000; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            var assemblers = simulation.Capture(1).Economy.Buildings
                .Where(b => b.FactionId == 1 && b.Kind == BuildingKind.Assembler).ToArray();
            Assert.That(assemblers.Length, Is.GreaterThan(0));
            Assert.That(assemblers.Any(b => b.Recipe == RecipeKind.ArmsKit), Is.True);
        }

        [Test]
        public void FoodReserveAndAgeTwoCapBlockFurtherScaling()
        {
            var blocked = new Battle(Scenario(true));
            SetEconomy(blocked, 0, "Age", 2);
            SetEconomy(blocked, 0, "Food", 1499);
            Invoke(blocked, "EnsureArmsScaleCapacity", 1u);
            Assert.That(blocked.Capture(1).Economy.Buildings.Count(b => b.Kind == BuildingKind.Assembler), Is.EqualTo(0));

            var cappedScenario = Scenario(true);
            cappedScenario.Economy.ArmsScaleMaxArmsAssemblersAge2 = 1;
            var capped = new Battle(cappedScenario);
            SetEconomy(capped, 0, "Age", 2);
            SetEconomy(capped, 0, "Food", 10000);
            SetEconomy(capped, 0, "Wood", 10000);
            SetEconomy(capped, 0, "Stone", 10000);
            SetEconomy(capped, 0, "Planks", 10000);
            SetEconomy(capped, 0, "Fittings", 10000);
            SetEconomy(capped, 0, "Rope", 10000);
            Invoke(capped, "EnsureArmsScaleCapacity", 1u);
            Invoke(capped, "EnsureArmsScaleCapacity", 1u);
            Assert.That(capped.Capture(1).Economy.Buildings.Count(b => b.Kind == BuildingKind.Assembler), Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void AgeThreeScalingStillUsesArmsRecipe()
        {
            var simulation = new Battle(Scenario(true));
            SetEconomy(simulation, 0, "Age", 3);
            SetEconomy(simulation, 0, "Food", 10000);
            SetEconomy(simulation, 0, "Wood", 10000);
            SetEconomy(simulation, 0, "Stone", 10000);
            SetEconomy(simulation, 0, "Planks", 10000);
            SetEconomy(simulation, 0, "Fittings", 10000);
            SetEconomy(simulation, 0, "Rope", 10000);
            SetEconomy(simulation, 0, "ArmsKit", 0);
            SetEconomy(simulation, 0, "FineArmsKit", 0);
            Invoke(simulation, "EnsureArmsScaleCapacity", 1u);
            simulation.Step(1, Array.Empty<ScheduledInput>());
            var buildings = simulation.Capture(1).Economy.Buildings.Where(b => b.Kind == BuildingKind.Assembler).ToArray();
            Assert.That(buildings.Any(b => b.Recipe == RecipeKind.ArmsKit), Is.True,
                "assemblers=" + string.Join(",", buildings.Select(b => b.Recipe + ":" + b.FactionId)));
        }

        [Test]
        public void AgeTwoLowPlanksAddsSawmillsWithoutExceedingTheAgeCap()
        {
            var scenario = Scenario(true);
            var simulation = new Battle(scenario);
            SetEconomy(simulation, 0, "Age", 2);
            SetEconomy(simulation, 0, "Food", 10000);
            SetEconomy(simulation, 0, "Wood", 10000);
            SetEconomy(simulation, 0, "Planks", 0);
            SetEconomy(simulation, 0, "ArmsKit", 0);
            SetEconomy(simulation, 0, "FineArmsKit", 0);
            int before = simulation.Capture(1).Economy.Buildings.Count(b => b.FactionId == 1 && b.Kind == BuildingKind.Sawmill);
            for (long tick = 1; tick <= 2000; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            var economy = simulation.Capture(1).Economy;
            int sawmills = economy.Buildings.Count(b => b.FactionId == 1 && b.Kind == BuildingKind.Sawmill);
            Assert.That(sawmills, Is.GreaterThan(before));
            int materialTarget = (int)Invoke(simulation, "CraftRequiredAiMaterialTarget", 1u) + scenario.Economy.ArmsScaleExtraSawmillsAge2;
            Assert.That(sawmills, Is.LessThanOrEqualTo(materialTarget));
        }

        [Test]
        public void PlankOnlyShortageReservesAssemblerAndPreservesArmsRecipeUntilItBuilds()
        {
            var scenario = Scenario(true);
            scenario.Economy.ArmsScalePlankLow = 0;
            var simulation = new Battle(scenario);
            SetEconomy(simulation, 0, "Age", 2);
            SetEconomy(simulation, 0, "Food", 10000);
            SetEconomy(simulation, 0, "Wood", 10000);
            SetEconomy(simulation, 0, "Stone", 10000);
            SetEconomy(simulation, 0, "Planks", 0);
            SetEconomy(simulation, 0, "Frames", 10000);
            SetEconomy(simulation, 0, "Bricks", 10000);
            SetEconomy(simulation, 0, "SiegeParts", 10000);
            SetEconomy(simulation, 0, "Fittings", 10000);
            SetEconomy(simulation, 0, "Rope", 10000);
            SetEconomy(simulation, 0, "ArmsKit", 0);
            SetEconomy(simulation, 0, "FineArmsKit", 0);
            Invoke(simulation, "EnsureArmsScaleCapacity", 1u);
            simulation.Step(1, Array.Empty<ScheduledInput>());
            var candidates = simulation.Capture(1).Economy.Buildings.Where(b => b.FactionId == 1 && b.Kind == BuildingKind.Assembler).ToArray();
            Assert.That(candidates.Any(b => b.Reserved), Is.True);
            var reserved = candidates.Single(b => b.Reserved);
            SetEconomy(simulation, 0, "Planks", 10000);
            for (long tick = 2; tick <= 4000; tick++)
            {
                simulation.Step(tick, Array.Empty<ScheduledInput>());
                var building = simulation.Capture(1).Economy.Buildings.SingleOrDefault(b => b.Id == reserved.Id);
                if (building.Id == reserved.Id && building.Complete)
                {
                    Assert.That(building.Recipe, Is.EqualTo(RecipeKind.ArmsKit));
                    return;
                }
            }
            Assert.Fail("板が集まった予約組立所が完成しませんでした。");
        }

        private static object World(Battle simulation) => typeof(Battle).GetField("world", Private).GetValue(simulation);

        private static void SetEconomy(Battle simulation, int index, string name, object value)
        {
            var world = World(simulation);
            var field = world.GetType().GetField("Economies", Private);
            var economies = (Array)field.GetValue(world);
            var economy = economies.GetValue(index);
            var member = economy.GetType().GetField(name, Private);
            member.SetValue(economy, Convert.ChangeType(value, member.FieldType));
            economies.SetValue(economy, index);
        }

        private static object Invoke(Battle simulation, string name, params object[] args)
            => typeof(Battle).GetMethod(name, Private).Invoke(simulation, args);
    }
}
