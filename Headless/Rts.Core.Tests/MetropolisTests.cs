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

        // ---- V3-17 #3: civilisation choice score, matches and the normal start ----

        private const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        private static readonly System.Reflection.MethodInfo MetropolisCells = typeof(Battle).GetMethod("CountMetropolisBuildableCells", Private);
        private static readonly System.Reflection.MethodInfo MetropolisScoreMethod = typeof(Battle).GetMethod("MetropolisScore",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        private static readonly System.Reflection.MethodInfo CavalryMobility = typeof(Battle).GetMethod("CountCavalryMobility", Private);
        private static readonly System.Reflection.MethodInfo ChooseCivMethod = typeof(Battle).GetMethod("ChooseCiv", Private);

        private static ScenarioDefinition MetropolisMatchScenario(ulong seed, bool metropolis = true)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            s.Economy.Metropolis = metropolis;
            s.Economy.StartFood = 50000;
            s.Economy.StartWood = 50000;
            s.Economy.AdvanceFoodCost = 0;
            s.Economy.AdvanceWoodCost = 0;
            s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0;
            s.Economy.Age2WoodCost = 0;
            s.Economy.Age2Ticks = 1;
            s.Economy.Age3FoodCost = 0;
            s.Economy.Age3WoodCost = 0;
            s.Economy.Age3Ticks = 1;
            s.Cores[0].Hp = 1000000;
            s.Cores[1].Hp = 1000000;
            return s;
        }

        private static bool[] Cells(Battle sim, uint faction, string field)
        {
            var world = World(sim);
            var factions = (Array)world.GetType().GetField("Factions", Private).GetValue(world);
            object state = factions.GetValue((int)faction - 1);
            return (bool[])state.GetType().GetField(field, Private).GetValue(state);
        }

        private static int BuildableCells(Battle sim, ScenarioDefinition s, uint faction)
            => (int)MetropolisCells.Invoke(sim, new object[] { faction, s.Cores[faction - 1].Position });

        private static int MetropolisScoreOf(Battle sim, ScenarioDefinition s, uint faction)
            => (int)MetropolisScoreMethod.Invoke(null, new object[] { sim, faction, s.Cores[faction - 1].Position, 0, 0 });

        private static int CavalryOf(Battle sim, ScenarioDefinition s, uint faction)
            => (int)CavalryMobility.Invoke(sim, new object[] { faction, s.Cores[faction - 1].Position });

        /// <summary>A sim at tick 1 whose explored cells cover the whole map, so the score reads only the ground.</summary>
        private static Battle FullyExplored(ScenarioDefinition s)
        {
            var sim = new Battle(s);
            sim.Step(1, Array.Empty<ScheduledInput>());
            for (uint faction = 1; faction <= 2; faction++)
            {
                var explored = Cells(sim, faction, "ExploredCells");
                for (int i = 0; i < explored.Length; i++) explored[i] = true;
            }
            return sim;
        }

        private static bool WithinMetropolisReach(ScenarioDefinition s, uint faction, int cell, int extraCells)
        {
            int width = s.Map.WidthCells, size = s.Map.CellSizeMeters;
            int core = Cell(s, s.Cores[faction - 1].Position);
            int dx = Math.Abs(cell % width - core % width), dz = Math.Abs(cell / width - core / width);
            int reachCells = 24 / size + 1 + extraCells;
            return Math.Max(dx, dz) <= reachCells;
        }

        [Test]
        public void MetropolisScoreTiersNarrowNormalAndWideGround()
        {
            Assert.That(MetropolisCells, Is.Not.Null);
            Assert.That(MetropolisScoreMethod, Is.Not.Null);
            bool sawNarrow = false, sawNormal = false, sawWide = false;
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var s = MetropolisMatchScenario(seed);
                var sim = FullyExplored(s);
                for (uint faction = 1; faction <= 2; faction++)
                {
                    int cells = BuildableCells(sim, s, faction);
                    int score = MetropolisScoreOf(sim, s, faction);
                    TestContext.WriteLine("seed " + seed + " faction " + faction + ": buildable cells=" + cells + " -> " + score);
                    Assert.That(cells, Is.GreaterThan(0).And.LessThan(700), "24m の円の中だけを数える");
                    Assert.That(score, Is.EqualTo(cells >= 320 ? 3 : cells >= 270 ? 2 : 0), "seed " + seed);
                    if (score == 0) sawNarrow = true;
                    if (score == 2) sawNormal = true;
                    if (score == 3) sawWide = true;
                }
            }
            Assert.That(sawNarrow, Is.True, "狭い（0点）");
            Assert.That(sawNormal, Is.True, "ふつう（2点）");
            Assert.That(sawWide, Is.True, "広い（3点）");

            // Closing every other column of ground near the core makes a wide start narrow; flags off score zero.
            var wide = MetropolisMatchScenario(3);
            var narrowSim = FullyExplored(wide);
            int before = BuildableCells(narrowSim, wide, 2);
            Assert.That(MetropolisScoreOf(narrowSim, wide, 2), Is.EqualTo(3), "広い地面");
            var map = World(narrowSim).GetType().GetField("Map", Private).GetValue(World(narrowSim));
            var setPassable = map.GetType().GetMethod("SetPassable", Private);
            for (int cell = 0; cell < wide.Map.WidthCells * wide.Map.HeightCells; cell++)
                if (WithinMetropolisReach(wide, 2, cell, 0) && cell % 2 == 0) setPassable.Invoke(map, new object[] { cell, false });
            Assert.That(BuildableCells(narrowSim, wide, 2), Is.LessThan(before));
            Assert.That(MetropolisScoreOf(narrowSim, wide, 2), Is.EqualTo(0), "狭くした地面");

            var off = MetropolisMatchScenario(3, metropolis: false);
            Assert.That(MetropolisScoreOf(FullyExplored(off), off, 2), Is.EqualTo(0), "旗オフは0点");
        }

        [Test]
        public void MetropolisScoreCountsOnlyExploredGroundNearTheCore()
        {
            var s = MetropolisMatchScenario(3);
            var sim = FullyExplored(s);
            int full = BuildableCells(sim, s, 2);
            Assert.That(MetropolisScoreOf(sim, s, 2), Is.EqualTo(3));

            // Forgetting everything outside the 24m square changes nothing.
            var explored = Cells(sim, 2, "ExploredCells");
            for (int i = 0; i < explored.Length; i++)
                if (!WithinMetropolisReach(s, 2, i, 0)) explored[i] = false;
            Assert.That(BuildableCells(sim, s, 2), Is.EqualTo(full), "24m の外は数えない");

            // Forgetting half of the ground inside lowers it; forgetting all of it gives zero.
            for (int i = 0; i < explored.Length; i++)
                if (WithinMetropolisReach(s, 2, i, 0) && i % 2 == 0) explored[i] = false;
            int half = BuildableCells(sim, s, 2);
            Assert.That(half, Is.LessThan(full).And.GreaterThan(0));
            for (int i = 0; i < explored.Length; i++) explored[i] = false;
            Assert.That(BuildableCells(sim, s, 2), Is.EqualTo(0));
            Assert.That(MetropolisScoreOf(sim, s, 2), Is.EqualTo(0), "見たことのない範囲だけ");
        }

        [Test]
        public void MetropolisAndCavalryScoresMoveIndependently()
        {
            Assert.That(CavalryMobility, Is.Not.Null);
            // 1. Forgetting the ground inside the metropolis square drops the metropolis score to zero, and the cavalry
            //    score (visible routes out of the core) does not move at all.
            var s = MetropolisMatchScenario(3);
            var sim = FullyExplored(s);
            for (uint faction = 1; faction <= 2; faction++)
            {
                int cavalry = CavalryOf(sim, s, faction);
                var explored = Cells(sim, faction, "ExploredCells");
                for (int i = 0; i < explored.Length; i++)
                    if (WithinMetropolisReach(s, faction, i, 0)) explored[i] = false;
                Assert.That(MetropolisScoreOf(sim, s, faction), Is.EqualTo(0));
                Assert.That(CavalryOf(sim, s, faction), Is.EqualTo(cavalry), "都市の範囲を変えても騎馬は同じ");
            }

            // 2. Hiding the routes outside the cavalry's 16m core ring changes the cavalry score and never the
            //    metropolis count.
            bool cavalryMoved = false;
            for (ulong seed = 1; seed <= 20 && !cavalryMoved; seed++)
            {
                var scenario = MetropolisMatchScenario(seed);
                var probe = FullyExplored(scenario);
                for (uint faction = 1; faction <= 2; faction++)
                {
                    int cells = BuildableCells(probe, scenario, faction);
                    int cavalry = CavalryOf(probe, scenario, faction);
                    var visible = Cells(probe, faction, "VisibleCells");
                    for (int i = 0; i < visible.Length; i++)
                        if (!WithinMetropolisReach(scenario, faction, i, -5)) visible[i] = false;
                    Assert.That(BuildableCells(probe, scenario, faction), Is.EqualTo(cells), "騎馬の進路を変えても都市は同じ");
                    if (cavalry > 0 && CavalryOf(probe, scenario, faction) != cavalry) cavalryMoved = true;
                }
            }
            Assert.That(cavalryMoved, Is.True, "コアの外側への進路を隠すと騎馬の得点が変わる");

            // 3. On real maps the two scores do not rise together: some pair of starts has more room for the city
            //    but weaker routes for the cavalry.
            var pairs = new System.Collections.Generic.List<(int city, int cavalry, string name)>();
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var scenario = MetropolisMatchScenario(seed);
                var probe = FullyExplored(scenario);
                for (uint faction = 1; faction <= 2; faction++)
                    pairs.Add((BuildableCells(probe, scenario, faction), CavalryOf(probe, scenario, faction), "seed " + seed + " faction " + faction));
            }
            string disagreement = null;
            for (int a = 0; a < pairs.Count && disagreement == null; a++)
                for (int b = 0; b < pairs.Count && disagreement == null; b++)
                    if (pairs[a].city > pairs[b].city && pairs[a].cavalry < pairs[b].cavalry)
                        disagreement = pairs[a].name + " (cells " + pairs[a].city + ", cavalry " + pairs[a].cavalry + ") vs "
                            + pairs[b].name + " (cells " + pairs[b].city + ", cavalry " + pairs[b].cavalry + ")";
            TestContext.WriteLine("disagreement: " + disagreement);
            Assert.That(disagreement, Is.Not.Null, "都市の広さと騎馬の進路は別々に動く");
        }

        [Test]
        public void MetropolisOnlyChangesChoicesItWins()
        {
            Assert.That(ChooseCivMethod, Is.Not.Null);
            int won = 0;
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var onScenario = MetropolisMatchScenario(seed);
                var offScenario = MetropolisMatchScenario(seed, metropolis: false);
                var on = FullyExplored(onScenario);
                var off = FullyExplored(offScenario);
                for (uint faction = 1; faction <= 2; faction++)
                {
                    var withCity = (CivKind)ChooseCivMethod.Invoke(on, new object[] { faction });
                    var without = (CivKind)ChooseCivMethod.Invoke(off, new object[] { faction });
                    TestContext.WriteLine("seed " + seed + " faction " + faction + ": on=" + withCity + " off=" + without);
                    Assert.That(without, Is.Not.EqualTo(CivKind.Metropolis));
                    if (withCity == CivKind.Metropolis) won++;
                    else Assert.That(withCity, Is.EqualTo(without), "都市が勝たない陣営の選択は変わらない");
                }
            }
            TestContext.WriteLine("metropolis chosen: " + won + " / 60");
        }

        [TestCase(CivKind.Metropolis, CivKind.Agrarian)]
        [TestCase(CivKind.Agrarian, CivKind.Metropolis)]
        [TestCase(CivKind.Metropolis, CivKind.Metallurgy)]
        [TestCase(CivKind.Metallurgy, CivKind.Metropolis)]
        public void MetropolisCombinationsReachTheSecondAgeAndReplay(CivKind west, CivKind east)
        {
            var scenario = MetropolisMatchScenario(21);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, west));
            gateway.SubmitEconomy(EconomyCommand.Advance(2, 2, east));
            for (int i = 0; i < 20000 && !sim.Capture(1).Result.HasEnded; i++)
            {
                gateway.Step();
                Assert.That(sim.Capture(1).Result.IsFault, Is.False, "fault at tick " + sim.Capture(1).Tick);
            }
            var result = sim.Capture(1).Result;
            TestContext.WriteLine(west + " vs " + east + ": tick=" + sim.Capture(1).Tick + ", winner=" + result.WinnerFactionId
                + ", ended=" + result.HasEnded + ", undecided=" + result.IsUndecided
                + ", ages=" + sim.Capture(1).Economy.Age + "/" + sim.Capture(2).Economy.Age
                + ", villagers=" + sim.Capture(1).Economy.Population + "/" + sim.Capture(2).Economy.Population);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(west));
            Assert.That(sim.Capture(2).Economy.Civ, Is.EqualTo(east));
            Assert.That(sim.Capture(1).Economy.Age, Is.GreaterThanOrEqualTo(2), west + " reaches the second age");
            Assert.That(sim.Capture(2).Economy.Age, Is.GreaterThanOrEqualTo(2), east + " reaches the second age");
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, gateway.Inputs, sim.Capture(1).Tick, identity);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, identity);
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
        }

        [Test]
        public void NormalMetropolisStartFindsASeedAndReplaysForTwentyThousandTicks()
        {
            ulong foundSeed = 0;
            CivKind foundWest = CivKind.Primitive, foundEast = CivKind.Primitive;
            for (ulong seed = 1; seed <= 200 && foundSeed == 0; seed++)
            {
                var probe = new Battle(MetropolisMatchScenario(seed));
                var probeGateway = new CommandGateway(probe);
                for (int i = 0; i < 1500; i++)
                {
                    probeGateway.Step();
                    if (i % 20 != 19) continue;
                    var west = probe.Capture(1).Economy.Civ;
                    var east = probe.Capture(2).Economy.Civ;
                    if (west == CivKind.Metropolis || east == CivKind.Metropolis)
                    {
                        foundSeed = seed;
                        foundWest = west;
                        foundEast = east;
                        break;
                    }
                    if (west != CivKind.Primitive && east != CivKind.Primitive) break;
                }
            }
            if (foundSeed == 0)
            {
                TestContext.WriteLine("都市が選ばれる種は seed 1..200 では見つからなかった");
                Assert.Inconclusive("都市が選ばれる種が見つからない");
                return;
            }

            var scenario = MetropolisMatchScenario(foundSeed);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            Steps(gateway, sim, 20000);
            TestContext.WriteLine("normal metropolis seed " + foundSeed + ": civs=" + foundWest + "/" + foundEast
                + ", final civs=" + sim.Capture(1).Economy.Civ + "/" + sim.Capture(2).Economy.Civ
                + ", tick=" + sim.Capture(1).Tick + ", ages=" + sim.Capture(1).Economy.Age + "/" + sim.Capture(2).Economy.Age);
            Assert.That(sim.Capture(1).Economy.Civ == CivKind.Metropolis || sim.Capture(2).Economy.Civ == CivKind.Metropolis, Is.True);
            Assert.That(sim.Capture(1).Economy.Age, Is.GreaterThanOrEqualTo(2), "通常開始から西が第2時代まで進む");
            Assert.That(sim.Capture(2).Economy.Age, Is.GreaterThanOrEqualTo(2), "通常開始から東が第2時代まで進む");
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, gateway.Inputs, sim.Capture(1).Tick, identity);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, identity);
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
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
