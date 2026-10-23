using System;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class AiFactoryTests
    {
        private static ScenarioDefinition Scenario(bool factory)
        {
            var scenario = LiveGameRules.Create(67001UL, false, true, false, false);
            var e = scenario.Economy;
            e.Ages = true; e.Industry = true; e.CraftTree1 = true; e.PrimitiveCraft = true; e.AiFactory = factory;
            e.CommonDrill = true; e.Forestry = true; e.Masonry = true; e.Inserters = true; e.BeltComponents = true;
            e.HutWork = 1; e.LumberCampWork = 1; e.SawmillWork = 1; e.DrillWork = 1; e.StonecutterWork = 1;
            e.PrimitiveCraftTicks = 4; e.CraftTreePlankTicks = 4; e.CraftTreeStonecutterTicks = 4;
            e.AiFactoryBarracksIdleWindowTicks = 20;
            return scenario;
        }

        [Test]
        public void FlagOffIsHashStable()
        {
            var left = new Battle(Scenario(false));
            var right = new Battle(ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario(false))));
            for (long tick = 1; tick <= 300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void Extension67RoundTripsAllAiFactoryRules()
        {
            var source = Scenario(true);
            source.Economy.AiFactoryPrimitiveLineCount = 7;
            source.Economy.AiFactoryBarracksIdlePermille = 125;
            source.Economy.AiFactoryAge4BarracksMaxBuildings = 11;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Any(x => x.Id == 67 && x.Version == 1), Is.True,
                "extensions=" + string.Join(",", decoded.Extensions.Select(x => x.Id.ToString())));
            Assert.That(decoded.Economy.AiFactory, Is.True);
            Assert.That(decoded.Economy.AiFactoryPrimitiveLineCount, Is.EqualTo(7));
            Assert.That(decoded.Economy.AiFactoryBarracksIdlePermille, Is.EqualTo(125));
            Assert.That(decoded.Economy.AiFactoryAge4BarracksMaxBuildings, Is.EqualTo(11));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void PrimitiveFactoryBuildsLinesAndProcessedGoods()
        {
            var sim = new Battle(Scenario(true));
            for (long tick = 1; tick <= 6000; tick++) sim.Step(tick, Array.Empty<ScheduledInput>());
            var economy = sim.Capture(1).Economy;
            Assert.That(economy.AiFactoryEnabled, Is.True);
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.Hut), Is.True);
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.Sawmill), Is.True,
                string.Join(",", economy.Buildings.Select(b => b.Kind.ToString())));
            Assert.That(economy.Belts.Count, Is.GreaterThan(0));
            Assert.That(economy.StoneTools + economy.Jerky + economy.Planks + economy.CutStone, Is.GreaterThan(0));
        }

        [Test]
        public void PrimitiveAgeBarracksStayAtConfiguredCap()
        {
            var scenario = Scenario(true);
            scenario.Economy.AiFactoryPrimitiveBarracksMaxBuildings = 1;
            var sim = new Battle(scenario);
            for (long tick = 1; tick <= 3000; tick++) sim.Step(tick, Array.Empty<ScheduledInput>());
            Assert.That(sim.Capture(1).Economy.Buildings.Count(b => b.Kind == BuildingKind.Barracks), Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void UnavailableFactoryTemplateDoesNotPreventOtherTemplates()
        {
            var scenario = Scenario(true);
            scenario.Economy.AiFactoryPrimitiveLineCount = 1;
            var sim = new Battle(scenario);
            for (long tick = 1; tick <= 2500; tick++) sim.Step(tick, Array.Empty<ScheduledInput>());
            var economy = sim.Capture(1).Economy;
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.Hut), Is.True);
            Assert.That(economy.Belts.Count, Is.GreaterThan(0));
        }

        [Test]
        public void FactoryReplayHashesMatchEveryTick()
        {
            var left = new Battle(Scenario(true));
            var right = new Battle(Scenario(true));
            for (long tick = 1; tick <= 1000; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void Era2FactoryBuildsMaterialAndMetalTemplates()
        {
            var scenario = MapGenerator.GenerateTerrain(67001);
            var e = scenario.Economy;
            e.Ages = true; e.Industry = true; e.CraftTree1 = true; e.CraftTree2 = true; e.CraftTree3 = true;
            e.PrimitiveCraft = true; e.AiFactory = true; e.CommonDrill = true; e.Forestry = true; e.Masonry = true; e.Inserters = true; e.BeltComponents = true;
            e.HutWork = 1; e.LumberCampWork = 1; e.SawmillWork = 1; e.DrillWork = 1; e.StonecutterWork = 1;
            e.PrimitiveCraftTicks = 4; e.CraftTreePlankTicks = 4; e.CraftTreeStonecutterTicks = 4; e.AiFactoryBarracksIdleWindowTicks = 20;
            e.StartFood = 50000; e.StartWood = 50000; e.StartStone = 50000; e.StartMetal = 50000;
            e.AdvanceFoodCost = e.AdvanceWoodCost = e.Age2FoodCost = e.Age2WoodCost = e.Age3FoodCost = e.Age3WoodCost = 0;
            e.AdvanceTicks = e.Age2Ticks = e.Age3Ticks = 1;
            e.AutoVillagerTarget = 8; e.AutoInfantryQueue = 0;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 1000000;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Metallurgy));
            Steps(gateway, sim, 5);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Metallurgy));
            Steps(gateway, sim, 5);
            Assert.That(sim.Capture(1).Economy.Age, Is.GreaterThanOrEqualTo(2));
            Steps(gateway, sim, 12000);

            var economy = sim.Capture(1).Economy;
            var observation = sim.CaptureLargeEconomyObservation().Factions[0];
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.Joinery), Is.True);
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.Mine), Is.True);
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.Smelter), Is.True);
            Assert.That(observation.AiFactoryAge2MaterialConnectedLines, Is.GreaterThan(0));
            Assert.That(observation.AiFactoryConnectedLines, Is.GreaterThanOrEqualTo(observation.AiFactoryAge2MaterialConnectedLines));
            Assert.That(observation.FramesProduced + observation.MetalProduced, Is.GreaterThan(0));
        }

        [Test]
        public void SecondFactoryTownUsesASeparateResourceCluster()
        {
            var scenario = MapGenerator.GenerateLarge(3);
            scenario.Map.Terrain = new byte[scenario.Map.WidthCells * scenario.Map.HeightCells];
            scenario.Map.BlockedCellIds = Array.Empty<int>();
            var e = scenario.Economy;
            e.Ages = true; e.Industry = true; e.CraftTree1 = true; e.CraftTree2 = true;
            e.PrimitiveCraft = true; e.AiFactory = true; e.Towns = true; e.LargeEconomy = true;
            e.StartFood = 100000; e.StartWood = 100000; e.StartStone = 100000; e.StartMetal = 100000;
            e.AdvanceFoodCost = e.AdvanceWoodCost = e.Age2FoodCost = e.Age2WoodCost = 0;
            e.AdvanceTicks = e.Age2Ticks = 1; e.TownWork = 1; e.TownMaxBuildings = 2;
            e.AgeClock = true; e.AgeClockTicks1 = 1; e.AgeClockTicks2 = 2; e.AgeClockTicks3 = 100000;
            e.LargeTownDecisionIntervalTicks = 1; e.AutoVillagerTarget = 0; e.AutoInfantryQueue = 0;
            e.BarracksWoodCost = e.HouseWoodCost = e.DropSiteWoodCost = 100000000;
            e.TowerWoodCost = e.FarmWoodCost = e.BlacksmithWoodCost = 100000000;
            e.MarketWoodCost = e.WorkshopWoodCost = e.RangeWoodCost = e.StableWoodCost = 100000000;
            e.AiFactoryPrimitiveBarracksMaxBuildings = 0;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000000;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, 5);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, 20000);

            var towns = sim.Capture(1).Economy.Buildings.Where(b => b.Kind == BuildingKind.Town && b.Complete).ToArray();
            Assert.That(towns.Length, Is.GreaterThanOrEqualTo(2));
            long dx = (towns[1].Center.X.Raw - towns[0].Center.X.Raw) / 65536;
            long dz = (towns[1].Center.Z.Raw - towns[0].Center.Z.Raw) / 65536;
            Assert.That(dx * dx + dz * dz, Is.GreaterThan((long)(scenario.Economy.TownResourceReach * 2) * (scenario.Economy.TownResourceReach * 2)));
        }

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }
    }
}
