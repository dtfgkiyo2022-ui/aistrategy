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
    public sealed class AiExpandTests
    {
        private static ScenarioDefinition Scenario(bool expand, bool large = true)
        {
            var scenario = LiveGameRules.Create(68003UL, large, true, false, false);
            scenario.Map.Terrain = new byte[scenario.Map.WidthCells * scenario.Map.HeightCells];
            scenario.Map.BlockedCellIds = Array.Empty<int>();
            var e = scenario.Economy;
            e.AiFactory = true; e.AiExpand = expand; e.Towns = true;
            e.ResourceFog = false; e.ExploreScouting = false;
            e.StartFood = e.StartWood = e.StartStone = e.StartMetal = 100000;
            e.AutoVillagerTarget = 80; e.AutoInfantryQueue = 0;
            e.TownWork = 1; e.AiExpandDecisionIntervalTicks = 1;
            e.TownCoreDistance = 1; e.TownResourceReach = 16;
            e.BuildingReservations = false;
            e.AiFactoryBarracksIdleWindowTicks = 20;
            e.AiFactoryPrimitiveBarracksMaxBuildings = 1;
            e.AiFactoryAge1BarracksMaxBuildings = 2;
            e.AgeClockTicks1 = 1; e.AgeClockTicks2 = 2; e.AgeClockTicks3 = 100000; e.AgeClockTicks4 = 200000;
            e.AdvanceFoodCost = e.AdvanceWoodCost = e.Age2FoodCost = e.Age2WoodCost = 0;
            e.AdvanceTicks = e.Age2Ticks = 1;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000000;
            return scenario;
        }

        [Test]
        public void FlagOffPreservesEveryTickHash()
        {
            var left = new Battle(Scenario(false));
            var right = new Battle(ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario(false))));
            for (long tick = 1; tick <= 3000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void Extension68RoundTripsExpansionRules()
        {
            var scenario = Scenario(true);
            scenario.Economy.AiExpandAge1TownCount = 1;
            scenario.Economy.AiExpandAge2TownCount = 2;
            scenario.Economy.AiExpandAge3TownCount = 3;
            scenario.Economy.AiExpandDecisionIntervalTicks = 137;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Any(x => x.Id == 68 && x.Version == 1), Is.True);
            Assert.That(decoded.Economy.AiExpand, Is.True);
            Assert.That(decoded.Economy.AiExpandDecisionIntervalTicks, Is.EqualTo(137));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void TownCreatesRemoteFactoryAndDeliversByBelt()
        {
            var scenario = Scenario(true);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, true));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, true));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, 10);
            for (int i = 0; i < 20000 && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
            var economy = sim.Capture(1).Economy;
            var towns = economy.Buildings.Where(b => b.Kind == BuildingKind.Town && b.Complete).ToArray();
            Assert.That(towns.Length, Is.GreaterThanOrEqualTo(1),
                $"age={economy.Age} food={economy.Food} wood={economy.Wood} stone={economy.Stone} buildings={economy.Buildings.Count}");
            var sawmills = economy.Buildings.Where(b => b.Kind == BuildingKind.Sawmill && b.Complete).ToArray();
            long nearestTownSawmill = sawmills.Length == 0 ? -1 : sawmills.Min(s => towns.Min(t => DistanceSquared(s.Center, t.Center)));
            Assert.That(nearestTownSawmill, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(64 * 64),
                $"towns={towns.Length} sawmills={sawmills.Length} nearest={nearestTownSawmill} belts={economy.Belts.Count} planks={economy.Planks}");
            Assert.That(economy.Belts.Count, Is.GreaterThan(0));
            Assert.That(economy.Planks, Is.GreaterThan(0));
        }

        [Test]
        public void AgeOneAndAgeTwoReachExpansionTownTargets()
        {
            var scenario = Scenario(true);
            var e = scenario.Economy;
            e.AiExpandAge1TownCount = 1;
            e.AiExpandAge2TownCount = 2;
            e.TownMaxBuildings = 2;
            e.TownWoodCost = e.TownStoneCost = 1;
            e.AgeClock = false;
            e.AgeClockUx = false;
            e.AutoVillagerTarget = 0; e.AutoInfantryQueue = 0;
            e.AiFactoryPrimitiveBarracksMaxBuildings = 0;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, 20000);
            var ageOne = sim.Capture(1).Economy;
            Assert.That(ageOne.Age, Is.GreaterThanOrEqualTo(1));
            Assert.That(ageOne.Buildings.Count(b => b.Kind == BuildingKind.Town && b.Complete), Is.GreaterThanOrEqualTo(1));

            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, 20000);
            var ageTwo = sim.Capture(1).Economy;
            Assert.That(ageTwo.Age, Is.GreaterThanOrEqualTo(2));
            Assert.That(ageTwo.Buildings.Count(b => b.Kind == BuildingKind.Town && b.Complete), Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void SecondBarracksUsesEnemyFacingBase()
        {
            var scenario = Scenario(true);
            scenario.Economy.AiFactoryPrimitiveLineCount = 0;
            scenario.Economy.AiFactoryAge1LineCount = 0;
            scenario.Economy.AiFactoryAge1BarracksMaxBuildings = 3;
            scenario.Economy.AgeClockTicks2 = 10000;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, true));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, 20000);
            var buildings = sim.Capture(1).Economy.Buildings.Where(b => b.Kind == BuildingKind.Barracks && b.Complete).ToArray();
            Assert.That(buildings.Length, Is.GreaterThanOrEqualTo(2));
            var town = sim.Capture(1).Economy.Buildings.Where(b => b.Kind == BuildingKind.Town && b.Complete)
                .OrderBy(b => DistanceSquared(b.Center, scenario.Cores[1].Position)).FirstOrDefault();
            Assert.That(town.Id, Is.Not.EqualTo(0u));
            var distances = string.Join(",", buildings.Select(b => DistanceSquared(b.Center, town.Center)));
            Assert.That(buildings.Skip(1).Any(b => DistanceSquared(b.Center, town.Center) < 128 * 128), Is.True,
                $"barracks={buildings.Length} town={town.Center.X.Raw / 65536},{town.Center.Z.Raw / 65536} distances={distances}");
        }

        [Test]
        public void ExpansionReplayHashesMatchEveryTick()
        {
            var left = new Battle(Scenario(true));
            var right = new Battle(Scenario(true));
            for (long tick = 1; tick <= 2000; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static long DistanceSquared(SimPoint left, SimPoint right)
        {
            long dx = (left.X.Raw - right.X.Raw) / 65536;
            long dz = (left.Z.Raw - right.Z.Raw) / 65536;
            return dx * dx + dz * dz;
        }
    }
}
