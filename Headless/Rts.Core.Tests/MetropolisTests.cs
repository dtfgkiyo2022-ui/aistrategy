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
    public sealed class MetropolisTests
    {
        private static ScenarioDefinition Scenario(bool metropolis)
        {
            var scenario = MapGenerator.GenerateTerrain(1, gold: false);
            scenario.Economy.Metropolis = metropolis;
            scenario.Economy.StartFood = 5000;
            scenario.Economy.StartWood = 5000;
            scenario.Economy.AutoVillagerTarget = 4;
            scenario.Economy.AutoInfantryQueue = 0;
            scenario.Economy.AdvanceFoodCost = 0;
            scenario.Economy.AdvanceWoodCost = 0;
            scenario.Economy.AdvanceTicks = 1;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000;
            return scenario;
        }

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) AtAge(CivKind civ)
            => AtAge(Scenario(true), civ);

        private static (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) AtAge(ScenarioDefinition scenario, CivKind civ)
        {
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, civ));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 2);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(civ));
            return (scenario, sim, gateway, sequence);
        }

        private static void AdvanceSameCiv(ScenarioDefinition scenario, Battle sim, CommandGateway gateway,
            CivKind civ, ref ulong sequence, int targetAge)
        {
            while (sim.Capture(1).Economy.Age < targetAge)
            {
                for (int i = 0; i < 5; i++) gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, ++sequence, 0));
                gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, civ));
                int ticks = sim.Capture(1).Economy.Age == 1 ? scenario.Economy.Age2Ticks : scenario.Economy.Age3Ticks;
                Steps(gateway, sim, ticks + 2);
            }
            Assert.That(sim.Capture(1).Economy.Age, Is.EqualTo(targetAge));
        }

        private static ScenarioDefinition ScenarioWithVillagers(int factionOneVillagers)
        {
            var scenario = Scenario(true);
            scenario.Economy.AutoVillagerTarget = 0;
            scenario.Economy.PopulationCap = 200;
            var original = scenario.Villagers;
            var villagers = new System.Collections.Generic.List<VillagerDefinition>();
            for (int i = 0; i < factionOneVillagers; i++)
            {
                var source = original[i % 3];
                villagers.Add(new VillagerDefinition { Id = (uint)villagers.Count + 1, FactionId = 1, Position = source.Position });
            }
            foreach (var source in original.Where(v => v.FactionId == 2))
                villagers.Add(new VillagerDefinition { Id = (uint)villagers.Count + 1, FactionId = 2, Position = source.Position });
            scenario.Villagers = villagers.ToArray();
            return scenario;
        }

        private static long PlaceAndBuildUntilComplete(ScenarioDefinition scenario, Battle sim, CommandGateway gateway,
            BuildingKind kind, ref ulong sequence)
        {
            int before = sim.Capture(1).Economy.Buildings.Count;
            int width = scenario.Map.WidthCells;
            int core = Cell(scenario, scenario.Cores[0].Position);
            int cx = core % width, cz = core / width;
            int size = kind == BuildingKind.GrandHouse ? scenario.Economy.GrandHouseSizeCells : scenario.Economy.HouseSizeCells;
            for (int radius = 5; radius <= 14; radius++)
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius || cx + dx < 0 || cz + dz < 0
                            || cx + dx >= width - size || cz + dz >= scenario.Map.HeightCells - size) continue;
                        gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, kind,
                            (cz + dz) * width + cx + dx, Facing.North));
                        Steps(gateway, sim, 1);
                        var buildings = sim.Capture(1).Economy.Buildings;
                        if (buildings.Count <= before || buildings[before].Kind != kind) continue;
                        uint id = buildings[before].Id;
                        for (int tick = 0; tick < 5000; tick++)
                        {
                            if (sim.Capture(1).Economy.Buildings.Any(b => b.Id == id && b.Complete)) return sim.Capture(1).Tick;
                            Steps(gateway, sim, 1);
                        }
                        Assert.Fail("building did not complete: " + kind);
                    }
            Assert.Fail("could not place: " + kind);
            return -1;
        }

        private static object World(Battle sim)
            => typeof(Battle).GetField("world", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(sim);

        private static void SetEconomy(Battle sim, uint faction, int food, int wood, int stone)
        {
            var world = World(sim);
            var field = world.GetType().GetField("Economies", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var economies = (Array)field.GetValue(world);
            object economy = economies.GetValue((int)faction - 1);
            economy.GetType().GetField("Food", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(economy, food);
            economy.GetType().GetField("Wood", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(economy, wood);
            economy.GetType().GetField("Stone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(economy, stone);
            economies.SetValue(economy, (int)faction - 1);
        }

        private static void SetTech(Battle sim, uint faction, TechKind tech, bool enabled)
        {
            var world = World(sim);
            var field = world.GetType().GetField("Economies", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var economies = (Array)field.GetValue(world);
            object economy = economies.GetValue((int)faction - 1);
            var techs = economy.GetType().GetField("Techs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            ulong bits = (ulong)techs.GetValue(economy);
            ulong mask = 1UL << ((int)tech - 1);
            techs.SetValue(economy, enabled ? bits | mask : bits & ~mask);
            economies.SetValue(economy, (int)faction - 1);
        }

        private static void SetVillagersAlive(Battle sim, int factionOneCount)
        {
            var world = World(sim);
            var field = world.GetType().GetField("Villagers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var villagers = (Array)field.GetValue(world);
            int seen = 0;
            for (int i = 0; i < villagers.Length; i++)
            {
                object villager = villagers.GetValue(i);
                var faction = (uint)villager.GetType().GetField("FactionId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(villager);
                if (faction != 1) continue;
                villager.GetType().GetField("Alive", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(villager, seen++ < factionOneCount);
                villagers.SetValue(villager, i);
            }
        }

        private static long TradeReturn(Battle sim)
        {
            var method = typeof(Battle).GetMethod("TradeAtMarket", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            method.Invoke(sim, new object[] { 1u, ResourceKind.Food, ResourceKind.Stone });
            var world = World(sim);
            var economies = (Array)world.GetType().GetField("Economies", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(world);
            object economy = economies.GetValue(0);
            return (int)economy.GetType().GetField("Stone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(economy);
        }

        private static uint PlaceAndBuild(ScenarioDefinition scenario, Battle sim, CommandGateway gateway,
            BuildingKind kind, ref ulong sequence)
        {
            int before = sim.Capture(1).Economy.Buildings.Count;
            int width = scenario.Map.WidthCells;
            int core = Cell(scenario, scenario.Cores[0].Position);
            int cx = core % width, cz = core / width;
            int size = kind == BuildingKind.GrandHouse ? scenario.Economy.GrandHouseSizeCells : scenario.Economy.HouseSizeCells;
            for (int radius = 5; radius <= 14; radius++)
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius || cx + dx < 0 || cz + dz < 0
                            || cx + dx >= width - size || cz + dz >= scenario.Map.HeightCells - size) continue;
                        gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, kind,
                            (cz + dz) * width + cx + dx, Facing.North));
                        Steps(gateway, sim, 1);
                        var buildings = sim.Capture(1).Economy.Buildings;
                        if (buildings.Count <= before || buildings[before].Kind != kind) continue;
                        uint id = buildings[before].Id;
                        gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new uint[] { 1, 2, 3 },
                            EconomyTargetKind.Building, id));
                        int work = kind == BuildingKind.GrandHouse ? scenario.Economy.GrandHouseWork : scenario.Economy.HouseWork;
                        Steps(gateway, sim, work + 500);
                        return id;
                    }
            return 0;
        }

        private static int Cell(ScenarioDefinition scenario, SimPoint point)
            => (int)(point.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * scenario.Map.WidthCells
                + (int)(point.X.Raw / 65536 / scenario.Map.CellSizeMeters);

        [Test]
        public void MetropolisOffKeepsBytesAndStateStable()
        {
            var leftScenario = MapGenerator.GenerateTerrain(1, gold: false);
            var rightScenario = MapGenerator.GenerateTerrain(1, gold: false);
            rightScenario.Economy.Metropolis = false;
            Assert.That(ScenarioBinary.Encode(rightScenario), Is.EqualTo(ScenarioBinary.Encode(leftScenario)));
            var left = new Battle(leftScenario);
            var right = new Battle(rightScenario);
            for (long tick = 1; tick <= 300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void GrandHouseIsMetropolisOnlyAndTriplesHousePopulation()
        {
            var metropolis = AtAge(CivKind.Metropolis);
            uint grandHouse = PlaceAndBuild(metropolis.scenario, metropolis.sim, metropolis.gateway,
                BuildingKind.GrandHouse, ref metropolis.sequence);
            Assert.That(grandHouse, Is.Not.EqualTo(0u));
            Assert.That(metropolis.sim.Capture(1).Economy.Buildings.Any(b => b.Id == grandHouse && b.Complete), Is.True);
            Assert.That(metropolis.sim.Capture(1).Economy.PopulationCap,
                Is.EqualTo(Math.Min(metropolis.scenario.Economy.PopulationCap,
                    metropolis.scenario.Economy.BasePopulation + metropolis.scenario.Economy.HousePopulation * 3)));

            var agrarian = AtAge(CivKind.Agrarian);
            var before = agrarian.sim.Capture(1).Economy.Buildings.Count;
            agrarian.gateway.SubmitEconomy(EconomyCommand.Place(1, ++agrarian.sequence, BuildingKind.GrandHouse, 0, Facing.North));
            Steps(agrarian.gateway, agrarian.sim, 1);
            Assert.That(agrarian.sim.Capture(1).Economy.Buildings.Count, Is.EqualTo(before));
        }

        [Test]
        public void MetropolisVillagersAreCheaperAndFasterOnlyForThatCiv()
        {
            var city = AtAge(ScenarioWithVillagers(4), CivKind.Metropolis);
            var other = AtAge(ScenarioWithVillagers(4), CivKind.Agrarian);
            int cityFood = city.sim.Capture(1).Economy.Food;
            int otherFood = other.sim.Capture(1).Economy.Food;
            city.gateway.SubmitEconomy(EconomyCommand.Train(1, ++city.sequence, 0, UnitKind.Villager));
            other.gateway.SubmitEconomy(EconomyCommand.Train(1, ++other.sequence, 0, UnitKind.Villager));
            Steps(city.gateway, city.sim, 1);
            Steps(other.gateway, other.sim, 1);
            var cityEconomy = city.sim.Capture(1).Economy;
            var otherEconomy = other.sim.Capture(1).Economy;
            Assert.That(cityFood - cityEconomy.Food, Is.EqualTo(city.scenario.Economy.VillagerFoodCost * 2 / 3));
            Assert.That(otherFood - otherEconomy.Food, Is.EqualTo(other.scenario.Economy.VillagerFoodCost));
            Assert.That(TrainRemaining(city.sim), Is.LessThan(TrainRemaining(other.sim)));
        }

        [Test]
        public void MetropolisUsesTwoAdditionalBuildersOnly()
        {
            var city = AtAge(CivKind.Metropolis);
            var other = AtAge(CivKind.Agrarian);
            var method = typeof(Battle).GetMethod("BuildersFor",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            Assert.That(method.Invoke(city.sim, new object[] { 1u }), Is.EqualTo(city.scenario.Economy.Builders + 2));
            Assert.That(method.Invoke(other.sim, new object[] { 1u }), Is.EqualTo(other.scenario.Economy.Builders));
        }

        [Test]
        public void MetropolisActuallyFinishesTheSameBuildingEarlierWithoutChangingOtherCivs()
        {
            var city = AtAge(ScenarioWithVillagers(4), CivKind.Metropolis);
            var other = AtAge(ScenarioWithVillagers(4), CivKind.Agrarian);
            long cityTick = PlaceAndBuildUntilComplete(city.scenario, city.sim, city.gateway, BuildingKind.House, ref city.sequence);
            long otherTick = PlaceAndBuildUntilComplete(other.scenario, other.sim, other.gateway, BuildingKind.House, ref other.sequence);
            Assert.That(cityTick, Is.LessThan(otherTick));
            var builders = typeof(Battle).GetMethod("BuildersFor",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(builders.Invoke(other.sim, new object[] { 1u }), Is.EqualTo(other.scenario.Economy.Builders));
        }

        [Test]
        public void MarketFestivityUsesOnlyResearchedMetropolisVillagersAndCapsAtTenPercent()
        {
            var city = AtAge(ScenarioWithVillagers(60), CivKind.Metropolis);
            AdvanceSameCiv(city.scenario, city.sim, city.gateway, CivKind.Metropolis, ref city.sequence, 2);
            uint market = PlaceAndBuild(city.scenario, city.sim, city.gateway, BuildingKind.Market, ref city.sequence);
            uint grandHouse = PlaceAndBuild(city.scenario, city.sim, city.gateway, BuildingKind.GrandHouse, ref city.sequence);
            Assert.That(market, Is.Not.EqualTo(0u));
            Assert.That(grandHouse, Is.Not.EqualTo(0u));
            Assert.That(city.sim.Capture(1).Economy.Buildings.First(b => b.Id == market).Complete, Is.True);
            city.gateway.SubmitEconomy(EconomyCommand.Research(1, ++city.sequence, grandHouse, MetropolisTech.MarketFestivity));
            Steps(city.gateway, city.sim, city.scenario.Economy.MetropolisMarketTicks + 2);
            Assert.That(city.sim.Capture(1).Economy.Techs & (1UL << ((int)MetropolisTech.MarketFestivity - 1)), Is.Not.EqualTo(0UL));

            for (int count = 0; count <= 9; count++)
            {
                SetVillagersAlive(city.sim, count);
                SetEconomy(city.sim, 1, 1000, 0, 0);
                Assert.That(TradeReturn(city.sim), Is.EqualTo(city.scenario.Economy.TradeReturn), "villagers=" + count);
            }
            SetVillagersAlive(city.sim, 10);
            SetEconomy(city.sim, 1, 1000, 0, 0);
            Assert.That(TradeReturn(city.sim), Is.EqualTo(city.scenario.Economy.TradeReturn * 102 / 100));
            SetVillagersAlive(city.sim, 60);
            SetEconomy(city.sim, 1, 1000, 0, 0);
            Assert.That(TradeReturn(city.sim), Is.EqualTo(city.scenario.Economy.TradeReturn * 110 / 100));

            SetTech(city.sim, 1, MetropolisTech.MarketFestivity, false);
            SetEconomy(city.sim, 1, 1000, 0, 0);
            Assert.That(TradeReturn(city.sim), Is.EqualTo(city.scenario.Economy.TradeReturn), "未研究の都市");

            var other = AtAge(ScenarioWithVillagers(10), CivKind.Agrarian);
            AdvanceSameCiv(other.scenario, other.sim, other.gateway, CivKind.Agrarian, ref other.sequence, 2);
            uint otherMarket = PlaceAndBuild(other.scenario, other.sim, other.gateway, BuildingKind.Market, ref other.sequence);
            Assert.That(otherMarket, Is.Not.EqualTo(0u));
            SetEconomy(other.sim, 1, 1000, 0, 0);
            Assert.That(TradeReturn(other.sim), Is.EqualTo(other.scenario.Economy.TradeReturn), "都市でない文明");
        }

        [Test]
        public void CitizenMilitiaReturnsDamageOnlyNearTheCoreAfterResearch()
        {
            var city = AtAge(CivKind.Metropolis);
            var world = World(city.sim);
            var soldiers = (Array)world.GetType().GetField("Soldiers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(world);
            int attacker = -1;
            for (int i = 0; i < soldiers.Length; i++)
            {
                object soldier = soldiers.GetValue(i);
                object initial = soldier.GetType().GetField("Initial", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(soldier);
                uint faction = (uint)initial.GetType().GetField("FactionId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).GetValue(initial);
                if (faction == 2) { attacker = i; break; }
            }
            Assert.That(attacker, Is.GreaterThanOrEqualTo(0));
            var counterattack = typeof(Battle).GetMethod("MetropolisVillagerCounterattack", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var damageField = typeof(Battle).GetField("soldierDamage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var villagerField = world.GetType().GetField("Villagers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var villagers = (Array)villagerField.GetValue(world);
            object first = villagers.GetValue(0);
            first.GetType().GetField("Position", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(first, city.scenario.Cores[0].Position);
            villagers.SetValue(first, 0);
            SetTech(city.sim, 1, MetropolisTech.CitizenMilitia, true);
            long[] damage = (long[])damageField.GetValue(city.sim);
            Array.Clear(damage, 0, damage.Length);
            counterattack.Invoke(city.sim, new object[] { 1u, 0, attacker });
            Assert.That(damage[attacker], Is.EqualTo(city.scenario.Economy.MetropolisMilitiaDamage));

            first = villagers.GetValue(0);
            first.GetType().GetField("Position", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(first, city.scenario.Cores[1].Position);
            villagers.SetValue(first, 0);
            Array.Clear(damage, 0, damage.Length);
            counterattack.Invoke(city.sim, new object[] { 1u, 0, attacker });
            Assert.That(damage[attacker], Is.EqualTo(0), "コアから遠い村人");

            var other = AtAge(CivKind.Agrarian);
            var otherWorld = World(other.sim);
            var otherVillagers = (Array)otherWorld.GetType().GetField("Villagers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(otherWorld);
            object otherFirst = otherVillagers.GetValue(0);
            otherFirst.GetType().GetField("Position", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(otherFirst, other.scenario.Cores[0].Position);
            otherVillagers.SetValue(otherFirst, 0);
            var otherDamage = (long[])damageField.GetValue(other.sim);
            Array.Clear(otherDamage, 0, otherDamage.Length);
            counterattack.Invoke(other.sim, new object[] { 1u, 0, attacker });
            Assert.That(otherDamage[attacker], Is.EqualTo(0), "研究なし・他文明");
        }

        [Test]
        public void MetropolisResearchIsGrandHouseOnlyAgeGatedCostGatedAndReplaysBothCommands()
        {
            var state = AtAge(CivKind.Metropolis);
            uint house = PlaceAndBuild(state.scenario, state.sim, state.gateway, BuildingKind.House, ref state.sequence);
            uint grandHouse = PlaceAndBuild(state.scenario, state.sim, state.gateway, BuildingKind.GrandHouse, ref state.sequence);
            Assert.That(house, Is.Not.EqualTo(0u));
            Assert.That(grandHouse, Is.Not.EqualTo(0u));

            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, house, MetropolisTech.MarketFestivity));
            Steps(state.gateway, state.sim, 1);
            Assert.That(state.sim.Capture(1).Economy.Buildings.First(b => b.Id == house).Researching, Is.EqualTo((TechKind)0));
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, grandHouse, MetropolisTech.MarketFestivity));
            Steps(state.gateway, state.sim, 1);
            Assert.That(state.sim.Capture(1).Economy.Buildings.First(b => b.Id == grandHouse).Researching, Is.EqualTo((TechKind)0));

            AdvanceSameCiv(state.scenario, state.sim, state.gateway, CivKind.Metropolis, ref state.sequence, 2);
            SetEconomy(state.sim, 1, state.scenario.Economy.MetropolisMarketFoodCost - 1, 1000, 0);
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, grandHouse, MetropolisTech.MarketFestivity));
            Steps(state.gateway, state.sim, 1);
            Assert.That(state.sim.Capture(1).Economy.Buildings.First(b => b.Id == grandHouse).Researching, Is.EqualTo((TechKind)0));
            SetEconomy(state.sim, 1, 1000, 1000, 0);
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, grandHouse, MetropolisTech.MarketFestivity));
            Steps(state.gateway, state.sim, 1);
            Assert.That(state.sim.Capture(1).Economy.Buildings.First(b => b.Id == grandHouse).Researching, Is.EqualTo(MetropolisTech.MarketFestivity));
            Steps(state.gateway, state.sim, state.scenario.Economy.MetropolisMarketTicks + 1);
            Assert.That(state.sim.Capture(1).Economy.Techs & (1UL << ((int)MetropolisTech.MarketFestivity - 1)), Is.Not.EqualTo(0UL));

            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, grandHouse, MetropolisTech.CitizenMilitia));
            Steps(state.gateway, state.sim, 1);
            Assert.That(state.sim.Capture(1).Economy.Buildings.First(b => b.Id == grandHouse).Researching, Is.EqualTo((TechKind)0), "第2時代では市民兵は閉じる");
            AdvanceSameCiv(state.scenario, state.sim, state.gateway, CivKind.Metropolis, ref state.sequence, 3);
            SetEconomy(state.sim, 1, 1000, 1000, 0);
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, grandHouse, MetropolisTech.CitizenMilitia));
            Steps(state.gateway, state.sim, 1);
            Assert.That(state.sim.Capture(1).Economy.Buildings.First(b => b.Id == grandHouse).Researching, Is.EqualTo(MetropolisTech.CitizenMilitia));

            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                var record = ReplayRunner.Record(stream, state.scenario, state.gateway.Inputs, state.sim.Capture(1).Tick, build);
                Assert.That(record.IsFault, Is.False);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, build);
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
        }

        [Test]
        public void MetropolisExtensionRoundTripsAndReplayIsDeterministic()
        {
            var scenario = Scenario(true);
            byte[] bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.Metropolis, Is.True);
            Assert.That(decoded.Extensions.Any(e => e.Id == 7), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 20000, identity);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, identity);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        [Test]
        public void MetropolisExtensionV1IsReadableAndRewrittenAsV2()
        {
            var scenario = Scenario(true);
            byte[] current = ScenarioBinary.Encode(scenario);
            const int extensionHeaderBytes = 3 * sizeof(int);
            const int recordHeaderBytes = 3 * sizeof(int);
            const int currentDataBytes = 16 * sizeof(int);
            const int oldDataBytes = 5 * sizeof(int);
            int extensionStart = current.Length - extensionHeaderBytes - recordHeaderBytes - currentDataBytes;
            Assert.That(BitConverter.ToInt32(current, extensionStart), Is.EqualTo(0x4E545845));

            byte[] old = current.Take(current.Length - (currentDataBytes - oldDataBytes)).ToArray();
            Buffer.BlockCopy(BitConverter.GetBytes(recordHeaderBytes + oldDataBytes), 0, old,
                extensionStart + sizeof(int) * 2, sizeof(int));
            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, old,
                extensionStart + extensionHeaderBytes + sizeof(int), sizeof(int));
            Buffer.BlockCopy(BitConverter.GetBytes(oldDataBytes), 0, old,
                extensionStart + extensionHeaderBytes + sizeof(int) * 2, sizeof(int));

            var decoded = ScenarioBinary.Decode(old);
            Assert.That(decoded.Economy.Metropolis, Is.True);
            Assert.That(decoded.Economy.GrandHouseSizeCells, Is.EqualTo(scenario.Economy.GrandHouseSizeCells));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(current));
        }

        private static int TrainRemaining(Battle sim)
            => int.Parse(DiagnosticComparison.Fields(sim.CaptureDiagnostic())
                .First(p => p.Key == "Economy[1].TrainRemaining").Value,
                System.Globalization.CultureInfo.InvariantCulture);

        [Test]
        public void MetropolisAutomaticEconomyBuildsGrandHouseAndGrowsFurther()
        {
            var scenario = Scenario(true);
            scenario.Economy.BasePopulation = 8;
            scenario.Economy.AutoVillagerTarget = 12;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Metropolis));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 2);
            Steps(gateway, sim, 9000);
            var economy = sim.Capture(1).Economy;
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.GrandHouse && b.Complete), Is.True);
            Assert.That(economy.Population, Is.GreaterThan(scenario.Economy.BasePopulation));
        }
    }
}
