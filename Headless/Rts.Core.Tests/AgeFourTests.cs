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
    public sealed class AgeFourTests
    {
        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static uint PlaceAndBuildBlacksmith(ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ref ulong sequence)
        {
            const int width = 128;
            int core = (int)(scenario.Cores[0].Position.Z.Raw / 65536 / 2) * width
                + (int)(scenario.Cores[0].Position.X.Raw / 65536 / 2);
            int cx = core % width, cz = core / width;
            int before = sim.Capture(1).Economy.Buildings.Count;
            for (int radius = 5; radius <= 14; radius++)
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius || cx + dx < 0 || cz + dz < 0
                            || cx + dx >= width - scenario.Economy.BlacksmithSizeCells
                            || cz + dz >= scenario.Map.HeightCells - scenario.Economy.BlacksmithSizeCells) continue;
                        gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Blacksmith,
                            (cz + dz) * width + cx + dx, Facing.North));
                        Steps(gateway, sim, 1);
                        var buildings = sim.Capture(1).Economy.Buildings;
                        if (buildings.Count <= before || buildings[before].Kind != BuildingKind.Blacksmith) continue;
                        uint id = buildings[before].Id;
                        gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, id));
                        Steps(gateway, sim, scenario.Economy.BlacksmithWork * 10);
                        return id;
                    }
            return 0;
        }

        [Test]
        public void AgeFourExtensionRoundTripsItsFlagPricesClockAndResearchDefaults()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            var e = scenario.Economy;
            e.AgeFour = true;
            e.AgeClock = true;
            e.AgeClockTicks1 = 6000;
            e.AgeClockTicks2 = 12000;
            e.AgeClockTicks3 = 18000;
            e.AgeClockTicks4 = 24000;
            e.Age4FoodCost = 1080;
            e.Age4WoodCost = 720;
            e.Age4Ticks = 2400;
            e.Age4PopulationBonus = 20;
            e.SiegeAge4Permille = 4000;
            e.TotalWarFoodCost = 400;
            e.TotalWarWoodCost = 300;
            e.TotalWarMetalCost = 100;
            e.TotalWarTicks = 900;
            e.GrandWorkshopFoodCost = 300;
            e.GrandWorkshopWoodCost = 400;
            e.GrandWorkshopTicks = 900;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            var d = decoded.Economy;
            Assert.That(d.AgeFour, Is.True);
            Assert.That(d.AgeClockTicks4, Is.EqualTo(24000));
            Assert.That((d.Age4FoodCost, d.Age4WoodCost, d.Age4Ticks, d.Age4PopulationBonus),
                Is.EqualTo((1080, 720, 2400, 20)));
            Assert.That((d.TotalWarFoodCost, d.TotalWarWoodCost, d.TotalWarMetalCost, d.TotalWarTicks),
                Is.EqualTo((400, 300, 100, 900)));
            Assert.That((d.GrandWorkshopFoodCost, d.GrandWorkshopWoodCost, d.GrandWorkshopTicks),
                Is.EqualTo((300, 400, 900)));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void AgeClockReachesAgeFourOnlyWhenAgeFourIsEnabled()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            var e = scenario.Economy;
            e.AgeFour = true;
            e.AgeClock = true;
            e.AgeClockTicks1 = 20;
            e.AgeClockTicks2 = 40;
            e.AgeClockTicks3 = 60;
            e.AgeClockTicks4 = 80;
            e.StartFood = 0;
            e.StartWood = 0;

            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            for (int i = 0; i < 85 && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();

            Assert.That(sim.Capture(1).Economy.Age, Is.EqualTo(4));
            Assert.That(sim.Capture(2).Economy.Age, Is.EqualTo(4));
        }

        [Test]
        public void PlayedRulesExposeTheFourAgeClockAndAgeFourEconomy()
        {
            var scenario = LiveGameRules.Create(3, false, true, false, false);
            var e = scenario.Economy;
            Assert.That(e.AgeFour, Is.True);
            Assert.That((e.AgeClockTicks1, e.AgeClockTicks2, e.AgeClockTicks3, e.AgeClockTicks4),
                Is.EqualTo((6000, 12000, 18000, 24000)));
            Assert.That((e.Age4FoodCost, e.Age4WoodCost, e.Age4PopulationBonus), Is.EqualTo((2400, 1600, 20)));
            Assert.That((e.TotalWarFoodCost, e.TotalWarWoodCost, e.TotalWarMetalCost, e.TotalWarTicks),
                Is.EqualTo((400, 300, 100, 900)));
        }

        [Test]
        public void PayingEarlyUsesTheRemainingClockFractionAndInsufficientStockDoesNothing()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            var e = scenario.Economy;
            e.AgeFour = true; e.AgeClock = true; e.AgeClockUx = false;
            e.AgeClockTicks1 = 20; e.AgeClockTicks2 = 40; e.AgeClockTicks3 = 60; e.AgeClockTicks4 = 80;
            e.AdvanceFoodCost = 0; e.AdvanceWoodCost = 0; e.AdvanceTicks = 1;
            e.Age2FoodCost = 100; e.Age2WoodCost = 100; e.Age2Ticks = 1;
            e.StartFood = 1000; e.StartWood = 1000;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, 2, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 3, CivKind.Agrarian));
            Steps(gateway, sim, 1);
            Assert.That(sim.Capture(1).Economy.Age, Is.EqualTo(1));
            Steps(gateway, sim, 24); // tick 25: 15 of the 20 ticks remain to age 2
            long beforeFood = sim.Capture(1).Economy.Food;
            long beforeWood = sim.Capture(1).Economy.Wood;
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 4, CivKind.Agrarian));
            Steps(gateway, sim, 1);
            Assert.That(sim.Capture(1).Economy.Food, Is.EqualTo(beforeFood - 70));
            Assert.That(sim.Capture(1).Economy.Wood, Is.EqualTo(beforeWood - 70));

            var poor = MapGenerator.GenerateTerrain(1);
            poor.Economy.AgeFour = true; poor.Economy.AgeClock = true; poor.Economy.AgeClockUx = false;
            poor.Economy.AgeClockTicks1 = 20; poor.Economy.AgeClockTicks2 = 40; poor.Economy.AgeClockTicks3 = 60; poor.Economy.AgeClockTicks4 = 80;
            poor.Economy.AdvanceFoodCost = 0; poor.Economy.AdvanceWoodCost = 0; poor.Economy.AdvanceTicks = 1;
            poor.Economy.Age2FoodCost = 100; poor.Economy.Age2WoodCost = 100; poor.Economy.Age2Ticks = 1;
            poor.Economy.StartFood = 50; poor.Economy.StartWood = 1000;
            var poorSim = new Battle(poor);
            var poorGateway = new CommandGateway(poorSim);
            poorGateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            poorGateway.SubmitEconomy(EconomyCommand.Auto(2, 2, false));
            poorGateway.SubmitEconomy(EconomyCommand.Advance(1, 3, CivKind.Agrarian));
            Steps(poorGateway, poorSim, 1); Steps(poorGateway, poorSim, 24);
            var poorBefore = poorSim.Capture(1).Economy;
            poorGateway.SubmitEconomy(EconomyCommand.Advance(1, 4, CivKind.Agrarian));
            Steps(poorGateway, poorSim, 1);
            var poorAfter = poorSim.Capture(1).Economy;
            Assert.That(poorAfter.Age, Is.EqualTo(poorBefore.Age));
            Assert.That(poorAfter.Food, Is.EqualTo(poorBefore.Food));
            Assert.That(poorAfter.Wood, Is.EqualTo(poorBefore.Wood));
        }

        [Test]
        public void AdoptedSiegeMultipliersAreTheMeasuredAgeValues()
        {
            var e = LiveGameRules.Create(1, true, true, false, false).Economy;
            Assert.That((e.SiegeAge0Permille, e.SiegeAge1Permille, e.SiegeAge2Permille, e.SiegeAge3Permille, e.SiegeAge4Permille),
                Is.EqualTo((100, 250, 450, 600, 4000)));
        }

        [Test]
        public void LatePushAdvantageStartsOnlyAtTheConfiguredWindowAndAgeFourDeadline()
        {
            var e = LiveGameRules.Create(1, true, true, false, false).Economy;
            var armies = new[] { new OwnArmyView(1, 1, UnitKind.Infantry, default, 2, default) };
            var enemies = new[] { new VisibleEnemy(1, default, (byte)UnitKind.Infantry) };
            var beforeWindow = new FactionObservation(1, 23999, armies, enemies, Array.Empty<EnemyContact>(), Array.Empty<KnownObjective>());
            var inWindow = new FactionObservation(1, 24000, armies, enemies, Array.Empty<EnemyContact>(), Array.Empty<KnownObjective>());
            Assert.That(Battle.LatePushAdvantageCondition(beforeWindow, e), Is.False);
            Assert.That(Battle.LatePushAdvantageCondition(inWindow, e), Is.True);
            Assert.That(Battle.LatePushCondition(inWindow, e), Is.True);
        }

        [Test]
        public void AgeFourResearchOpensAfterTheFourthAdvanceAndTotalWarUpdatesExistingUnits()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            var e = scenario.Economy;
            e.AgeFour = true; e.AgeClock = false; e.StartFood = 50000; e.StartWood = 50000; e.StartMetal = 5000;
            e.AdvanceFoodCost = 0; e.AdvanceWoodCost = 0; e.AdvanceTicks = 1;
            e.Age2FoodCost = 0; e.Age2WoodCost = 0; e.Age2Ticks = 1;
            e.Age3FoodCost = 0; e.Age3WoodCost = 0; e.Age3Ticks = 1;
            e.Age4FoodCost = 0; e.Age4WoodCost = 0; e.Age4Ticks = 1;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 1000000;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, 2);
            for (int age = 2; age <= 4; age++)
            {
                gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
                Steps(gateway, sim, 2);
            }
            Assert.That(sim.Capture(1).Economy.Age, Is.EqualTo(4));
            uint smith = PlaceAndBuildBlacksmith(scenario, sim, gateway, ref sequence);
            Assume.That(smith, Is.Not.EqualTo(0u));
            var beforeHp = sim.Capture(1).Units.First(unit => unit.IsOwn && unit.Kind == UnitKind.Infantry).Hp;
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++sequence, smith, TechKind.TotalWar));
            Steps(gateway, sim, e.TotalWarTicks - 1);
            Assert.That(sim.Capture(1).Economy.Techs & (1UL << ((int)TechKind.TotalWar - 1)), Is.EqualTo(0));
            Steps(gateway, sim, 2);
            var after = sim.Capture(1).Economy;
            Assert.That(after.Techs & (1UL << ((int)TechKind.TotalWar - 1)), Is.Not.EqualTo(0));
            Assert.That(sim.Capture(1).Units.First(unit => unit.IsOwn && unit.Kind == UnitKind.Infantry).Hp,
                Is.EqualTo(beforeHp * 11 / 10));
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++sequence, smith, TechKind.GrandWorkshop));
            Steps(gateway, sim, e.GrandWorkshopTicks - 1);
            Assert.That(sim.Capture(1).Economy.Techs & (1UL << ((int)TechKind.GrandWorkshop - 1)), Is.EqualTo(0));
            Steps(gateway, sim, 2);
            Assert.That(sim.Capture(1).Economy.Techs & (1UL << ((int)TechKind.GrandWorkshop - 1)), Is.Not.EqualTo(0));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AgeFourSettingReplaysDeterministically(bool ageFour)
        {
            var scenario = MapGenerator.GenerateTerrain(2);
            scenario.Economy.AgeFour = ageFour;
            scenario.Economy.AgeClock = true;
            scenario.Economy.AgeClockTicks1 = 20; scenario.Economy.AgeClockTicks2 = 40;
            scenario.Economy.AgeClockTicks3 = 60; scenario.Economy.AgeClockTicks4 = 80;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, 2, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 3, CivKind.Agrarian));
            Steps(gateway, sim, 100);
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, gateway.Inputs, sim.Capture(1).Tick, identity);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, identity);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AgeFourOnAndOffMatchesItsPeerAtEveryTick(bool ageFour)
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.AgeFour = ageFour;
            scenario.Economy.AgeClock = true;
            scenario.Economy.AgeClockTicks1 = 20; scenario.Economy.AgeClockTicks2 = 40; scenario.Economy.AgeClockTicks3 = 60; scenario.Economy.AgeClockTicks4 = 80;
            var bytes = ScenarioBinary.Encode(scenario);
            var left = new Battle(ScenarioBinary.Decode(bytes));
            var right = new Battle(ScenarioBinary.Decode(bytes));
            for (long tick = 1; tick <= 100; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }
    }
}
