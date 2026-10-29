using System;
using System.Collections.Generic;
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
            // These tests exercise expansion from the legacy three-villager opening.
            LiveGameRules.SetEarlyStart(scenario, false);
            scenario.Map.Terrain = new byte[scenario.Map.WidthCells * scenario.Map.HeightCells];
            scenario.Map.BlockedCellIds = Array.Empty<int>();
            var e = scenario.Economy;
            e.AiFactory = true; e.AiExpand = expand; e.Towns = true;
            // ArmsFlow has its own material-chain policy tests. Keep this fixture focused on AiExpand worker floors;
            // with ArmsFlow enabled, the chain's periodic material allocation legitimately changes the worker split.
            e.ArmsFlow = false;
            e.FreeTowns = false;
            // These tests assert expansion policy thresholds; keep the optional movement mode out of the fixture.
            e.FastMovement = false;
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
            scenario.Economy.AiExpandUnreachableWorkTimeoutTicks = 1201;
            scenario.Economy.AiExpandShareMinVillagers = 21;
            scenario.Economy.AiExpandFoodWorkerPermille = 451;
            scenario.Economy.AiExpandWoodWorkerPermille = 251;
            scenario.Economy.AiExpandStoneWorkerPermille = 101;
            scenario.Economy.AiExpandTownStoneWorkerPermille = 151;
            scenario.Economy.AiExpandPrimitiveFoodWorkerPermille = 401;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Any(x => x.Id == 68 && x.Version == 1), Is.True);
            Assert.That(decoded.Economy.AiExpand, Is.True);
            Assert.That(decoded.Economy.AiExpandDecisionIntervalTicks, Is.EqualTo(137));
            Assert.That(decoded.Economy.AiExpandUnreachableWorkTimeoutTicks, Is.EqualTo(1201));
            Assert.That(decoded.Economy.AiExpandShareMinVillagers, Is.EqualTo(21));
            Assert.That(decoded.Economy.AiExpandFoodWorkerPermille, Is.EqualTo(451));
            Assert.That(decoded.Economy.AiExpandWoodWorkerPermille, Is.EqualTo(251));
            Assert.That(decoded.Economy.AiExpandStoneWorkerPermille, Is.EqualTo(101));
            Assert.That(decoded.Economy.AiExpandTownStoneWorkerPermille, Is.EqualTo(151));
            Assert.That(decoded.Economy.AiExpandPrimitiveFoodWorkerPermille, Is.EqualTo(401));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void WorkRouteCacheInvalidatesWhenAClosedCellOpens()
        {
            var scenario = Scenario(true);
            var sim = new Battle(scenario);
            int width = scenario.Map.WidthCells;
            int origin = 0, resource = width * 2;
            for (int x = 0; x < width; x++) sim.SetPassableForTest(width + x, false);

            Assert.That(sim.ProbeAiExpandWorkRouteForTest(1, origin, resource), Is.False);
            sim.SetPassableForTest(width, true);
            Assert.That(sim.ProbeAiExpandWorkRouteForTest(1, origin, resource), Is.True);
        }

        [Test]
        public void AutomaticResearchKeepsWoodForPendingTown()
        {
            var scenario = Scenario(true, false);
            var e = scenario.Economy;
            e.AgeClock = false; e.AgeClockUx = false;
            e.AdvanceFoodCost = e.AdvanceWoodCost = 0; e.AdvanceTicks = 1;
            e.Age2FoodCost = e.Age2WoodCost = 1000000;
            e.TownWoodCost = 300; e.TownStoneCost = 1000000;
            e.StartFood = 100000; e.StartWood = e.TownWoodCost + e.TechWood[0] + 120 - 1; e.StartStone = 0; e.StartMetal = 100000;
            e.AutoVillagerTarget = 10; e.AutoInfantryQueue = 0;
            e.BarracksWoodCost = e.BlacksmithWoodCost = e.FarmWoodCost = e.HouseWoodCost = e.DropSiteWoodCost = e.TowerWoodCost = e.HutWoodCost = e.SawmillWoodCost = 0;
            e.BarracksWork = e.BlacksmithWork = e.FarmWork = 1;
            e.AiFactoryPrimitiveLineCount = e.AiFactoryAge1LineCount = e.AiFactoryAge2LineCount = e.AiFactoryAge3LineCount = 0;
            e.AiFactoryPrimitiveBarracksMaxBuildings = e.AiFactoryAge1BarracksMaxBuildings = 1;
            e.AiExpandDecisionIntervalTicks = 100000;
            e.AiExpandShareMinVillagers = 0; e.AiExpandFoodWorkerPermille = 1000; e.AiExpandWoodWorkerPermille = 0;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, true));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            bool blacksmithReady = false;
            for (int tick = 0; tick < 5000; tick++)
            {
                bool wasResearching = sim.Capture(1).Economy.Buildings.Any(b => b.Researching != 0);
                gateway.Step();
                var frame = sim.Capture(1);
                blacksmithReady |= frame.Economy.Buildings.Any(b => b.Kind == BuildingKind.Blacksmith && b.Complete);
                if (blacksmithReady && !wasResearching && frame.Economy.Buildings.Any(b => b.Researching != 0))
                    Assert.That(frame.Economy.Wood, Is.GreaterThanOrEqualTo(e.TownWoodCost), "tick " + tick);
            }
            var economy = sim.Capture(1).Economy;
            Assert.That(blacksmithReady, Is.True);
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.Town), Is.False);
        }

        [Test]
        public void VillagerShareFloorsFundWoodAndStoneAfterAgeOne()
        {
            var scenario = Scenario(true);
            var e = scenario.Economy;
            e.StartFood = e.StartWood = e.StartStone = e.StartMetal = 0;
            e.PopulationCap = 1000;
            e.AutoVillagerTarget = 0; e.AutoInfantryQueue = 0;
            e.AgeClockTicks1 = 1; e.AdvanceFoodCost = e.AdvanceWoodCost = e.Age2FoodCost = e.Age2WoodCost = 0;
            e.AdvanceTicks = e.Age2Ticks = 1;
            e.AiFactoryPrimitiveLineCount = e.AiFactoryAge1LineCount = e.AiFactoryAge2LineCount = e.AiFactoryAge3LineCount = 0;
            e.AiFactoryPrimitiveBarracksMaxBuildings = e.AiFactoryAge1BarracksMaxBuildings = 0;
            e.AiExpandFoodWorkerPermille = 450; e.AiExpandWoodWorkerPermille = 250;
            // This test checks the expansion worker-share floors; belt drop-off transport is unrelated to that threshold.
            e.BeltDropOff = false;
            e.BeltDropOffAi = false;
            e.Towns = false;
            SetFactionOneVillagers(scenario, 40);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, true));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Steps(gateway, sim, 4000);

            var observation = sim.CaptureLargeEconomyObservation().Factions[0];
            TestContext.Progress.WriteLine("VillagerShareFloors: BeltDropped=" + observation.BeltDropped + ", foodWorkers=" + observation.Workers[0] + ", woodWorkers=" + observation.Workers[1] + ", stoneWorkers=" + observation.Workers[2]);
            Assert.That(observation.Villagers, Is.EqualTo(40));
            Assert.That(observation.Workers[0], Is.GreaterThanOrEqualTo(15));
            Assert.That(observation.Workers[1], Is.GreaterThanOrEqualTo(10));
            Assert.That(observation.Workers[2], Is.GreaterThanOrEqualTo(4));
            Assert.That(observation.Income[1], Is.GreaterThan(0), "wood actually delivered");
            Assert.That(observation.Income[2], Is.GreaterThan(0), "stone actually delivered");
        }

        [Test]
        public void VillagerShareFloorsAreInactiveBelowMinimumPopulation()
        {
            // 19 villagers: the floors must not act, so the run matches one whose floors can never act.
            var (left, leftGateway) = ShareFloorMatch(19, 20);
            var (right, rightGateway) = ShareFloorMatch(19, 1000);
            for (int tick = 0; tick < 1600; tick++)
            {
                leftGateway.Step();
                rightGateway.Step();
                var l = left.CaptureLargeEconomyObservation().Factions[0];
                var r = right.CaptureLargeEconomyObservation().Factions[0];
                Assert.That(l.Workers, Is.EqualTo(r.Workers), "tick " + tick);
                var le = left.Capture(1).Economy; var re = right.Capture(1).Economy;
                Assert.That((le.Food, le.Wood, le.Stone), Is.EqualTo((re.Food, re.Wood, re.Stone)), "tick " + tick);
            }
            Assert.That(left.CaptureLargeEconomyObservation().Factions[0].Villagers, Is.EqualTo(19));
        }

        [Test]
        public void TownFundingKeepsWoodForTheTownDuringAutomaticSpending()
        {
            var scenario = Scenario(true);
            var e = scenario.Economy;
            // Wood for one barracks (150) above the town's wood, too little for a siege workshop (200); houses, farms and drop sites are free here because
            // they may still be built while the town is funded. Every automatic soldier would then cut into the reserve.
            e.TownWoodCost = 300; e.StartWood = e.TownWoodCost + e.BarracksWoodCost + 40;
            e.StartFood = 100000; e.StartStone = 0; e.TownStoneCost = 1000000;
            e.StartMetal = 100000; e.AutoVillagerTarget = 0; e.AutoInfantryQueue = 20;
            e.HouseWoodCost = e.FarmWoodCost = e.DropSiteWoodCost = 0;
            e.AiFactoryPrimitiveLineCount = e.AiFactoryAge1LineCount = e.AiFactoryAge2LineCount = e.AiFactoryAge3LineCount = 0;
            e.AiFactoryPrimitiveBarracksMaxBuildings = e.AiFactoryAge1BarracksMaxBuildings = e.AiFactoryAge2BarracksMaxBuildings = 1;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, true));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            // Processing intake and belts may still take single pieces of wood; the reserve is about soldiers and buildings.
            int startSoldiers = sim.Capture(1).Units.Count(u => u.IsOwn), soldiers = startSoldiers;
            int checkedTicks = 0;
            for (int tick = 0; tick < 2000; tick++)
            {
                gateway.Step();
                var frame = sim.Capture(1);
                int now = frame.Units.Count(u => u.IsOwn);
                if (frame.Economy.Age >= 1)
                {
                    checkedTicks++;
                    if (now > soldiers)
                        Assert.That(frame.Economy.Wood, Is.GreaterThanOrEqualTo(e.TownWoodCost), "a soldier left the town's wood, tick " + tick);
                    // Processing intake may take single pieces; no building or soldier payment goes below the reserve.
                    Assert.That(frame.Economy.Wood, Is.GreaterThanOrEqualTo(e.TownWoodCost - 20), "tick " + tick);
                }
                soldiers = now;
            }
            // 40 spare wood pays at most two soldiers at 20 wood; afterwards training waits for the town.
            Assert.That(soldiers - startSoldiers, Is.LessThanOrEqualTo(2));
            Assert.That(checkedTicks, Is.GreaterThan(1900), "the side reached age 1 and the reserve was checked");
        }

        [Test]
        public void PrimitiveFoodFloorGrowsVillagersBeforePrimitiveCraftWood()
        {
            var scenario = Scenario(true);
            var e = scenario.Economy;
            e.StartFood = 0; e.StartWood = e.StartStone = e.StartMetal = 0;
            e.AgeClockTicks1 = 100000; e.AgeClockTicks2 = 200000; e.AgeClockTicks3 = 300000; e.AgeClockTicks4 = 400000;
            e.PopulationCap = 100; e.AutoVillagerTarget = 12; e.AutoInfantryQueue = 0;
            e.AiExpandPrimitiveFoodWorkerPermille = 400; e.AiExpandShareMinVillagers = 20;
            e.Towns = false; e.AiFactoryPrimitiveLineCount = e.AiFactoryAge1LineCount = e.AiFactoryAge2LineCount = e.AiFactoryAge3LineCount = 0;
            e.AiFactoryPrimitiveBarracksMaxBuildings = e.AiFactoryAge1BarracksMaxBuildings = 0;
            SetFactionOneVillagers(scenario, 7);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, true));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            Steps(gateway, sim, 2000);
            var observation = sim.CaptureLargeEconomyObservation().Factions[0];
            Assert.That(sim.Capture(1).Economy.Age, Is.EqualTo(0), "still the primitive age");
            Assert.That(observation.Workers[0], Is.GreaterThanOrEqualTo(3));
            Assert.That(observation.Villagers, Is.GreaterThan(7));
        }

        private static (Battle sim, CommandGateway gateway) ShareFloorMatch(int villagers, int shareMinVillagers)
        {
            var scenario = Scenario(true);
            var e = scenario.Economy;
            e.StartFood = e.StartWood = e.StartStone = e.StartMetal = 0;
            e.PopulationCap = 1000;
            e.AutoVillagerTarget = 0; e.AutoInfantryQueue = 0;
            e.AgeClockTicks1 = 1; e.AdvanceFoodCost = e.AdvanceWoodCost = e.Age2FoodCost = e.Age2WoodCost = 0;
            e.AdvanceTicks = e.Age2Ticks = 1;
            e.AiFactoryPrimitiveLineCount = e.AiFactoryAge1LineCount = e.AiFactoryAge2LineCount = e.AiFactoryAge3LineCount = 0;
            e.AiFactoryPrimitiveBarracksMaxBuildings = e.AiFactoryAge1BarracksMaxBuildings = 0;
            e.Towns = false;
            e.AiExpandShareMinVillagers = shareMinVillagers;
            SetFactionOneVillagers(scenario, villagers);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, true));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            return (sim, gateway);
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
            // This legacy expansion-only fixture predates CraftRequired's age-up material costs.
            // Keep it focused on town targets; CraftRequiredTests covers the new advance gate.
            scenario.Economy.CraftRequired = false;
            // BeltCraft requires CraftRequired.
            scenario.Economy.BeltCraft = false;
            scenario.Economy.ArmsScale = false;
            scenario.Economy.CraftDepth = false;
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

        [Test]
        public void UnreachableLargeWorkPointIsNotKeptAsTheWoodAssignment()
        {
            var scenario = LiveGameRules.Create(3UL, true, true, false, false);
            scenario.Economy.AiExpandUnreachableWorkTimeoutTicks = 20;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, true));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, true));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            for (int tick = 0; tick < 15000 && !sim.Capture(1).Result.HasEnded; tick++) gateway.Step();
            var fields = DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(x => x.Key, x => x.Value);
            foreach (var field in fields.Keys.Where(x => x.StartsWith("Villagers[", StringComparison.Ordinal) && x.EndsWith(".FactionId", StringComparison.Ordinal)))
            {
                string prefix = field.Substring(0, field.Length - ".FactionId".Length);
                if (fields[field] == "1" && fields[prefix + ".Alive"] == "1" && fields[prefix + ".Task"] == "1")
                    Assert.That(fields[prefix + ".NodeId"], Is.Not.EqualTo("3"), prefix + " stayed on unreachable wood node 3");
            }
            Assert.That(sim.CaptureLargeEconomyObservation().Factions[0].Income[1], Is.GreaterThan(200), "wood must continue after the unreachable point");
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

        private static void SetFactionOneVillagers(ScenarioDefinition scenario, int count)
        {
            var villagers = scenario.Villagers.Where(v => v.FactionId != 1).ToList();
            var factionOne = scenario.Villagers.Where(v => v.FactionId == 1).Take(count).ToList();
            uint nextId = scenario.Villagers.Length == 0 ? 1 : scenario.Villagers.Max(v => v.Id) + 1;
            var core = scenario.Cores[0].Position;
            long one = Fix64.FromInt(1).Raw;
            while (factionOne.Count < count)
            {
                int index = factionOne.Count;
                factionOne.Add(new VillagerDefinition
                {
                    Id = nextId++, FactionId = 1,
                    Position = new SimPoint(Fix64.FromRaw(core.X.Raw + (index % 8 - 4) * one),
                        Fix64.FromRaw(core.Z.Raw + (index / 8 - 2) * one))
                });
            }
            villagers.AddRange(factionOne);
            scenario.Villagers = villagers.OrderBy(v => v.Id).ToArray();
        }
    }
}
