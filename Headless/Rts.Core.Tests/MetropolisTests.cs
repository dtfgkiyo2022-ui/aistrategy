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
        {
            var scenario = Scenario(true);
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
            var city = AtAge(CivKind.Metropolis);
            var other = AtAge(CivKind.Agrarian);
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
