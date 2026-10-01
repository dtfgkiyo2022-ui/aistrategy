using System;
using System.Collections.Generic;
using System.Globalization;
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
    public sealed class CultTests
    {
        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static ScenarioDefinition CultScenario(bool monks = true)
        {
            var s = MapGenerator.GenerateTerrain(1);
            s.Economy.Cult = true;
            s.Economy.MonksEnabled = monks;
            s.Economy.StartFood = 5000;
            s.Economy.StartWood = 5000;
            s.Economy.AutoVillagerTarget = 3;
            s.Economy.AutoInfantryQueue = 0;
            s.Cores[0].Hp = s.Cores[1].Hp = 100000;
            return s;
        }

        private static (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) AtCultAge()
        {
            var scenario = CultScenario();
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Cult));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 2);
            return (scenario, sim, gateway, sequence);
        }

        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static int Number(Dictionary<string, string> fields, string name)
            => int.Parse(fields[name], CultureInfo.InvariantCulture);

        private static uint PlaceAndBuild(ScenarioDefinition s, Battle sim, CommandGateway gateway, ref ulong sequence)
        {
            int before = sim.Capture(1).Economy.Buildings.Count;
            int width = s.Map.WidthCells;
            int core = width * ((int)(s.Cores[0].Position.Z.Raw / 65536) / s.Map.CellSizeMeters)
                + (int)(s.Cores[0].Position.X.Raw / 65536) / s.Map.CellSizeMeters;
            int cx = core % width, cz = core / width;
            for (int radius = 5; radius <= 14; radius++)
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius || cx + dx < 0 || cz + dz < 0
                            || cx + dx >= width - s.Economy.MonasterySizeCells
                            || cz + dz >= s.Map.HeightCells - s.Economy.MonasterySizeCells) continue;
                        gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Monastery,
                            (cz + dz) * width + cx + dx, Facing.North));
                        Steps(gateway, sim, 1);
                        var buildings = sim.Capture(1).Economy.Buildings;
                        if (buildings.Count <= before || buildings[before].Kind != BuildingKind.Monastery) continue;
                        uint id = buildings[before].Id;
                        gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new uint[] { 1, 2, 3 },
                            EconomyTargetKind.Building, id));
                        Steps(gateway, sim, 900);
                        return id;
                    }
            return 0;
        }

        [Test]
        public void CultOffKeepsTheOldBytesAndState()
        {
            var old = MapGenerator.GenerateTerrain(1);
            var off = MapGenerator.GenerateTerrain(1);
            off.Economy.Cult = false;
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(old)));
            var left = new Battle(old); var right = new Battle(off);
            for (long tick = 1; tick <= 300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void CultRequiresMonksAndCanBeSelectedWithItsOwnFlag()
        {
            var disabled = CultScenario(false);
            var noMonks = new Battle(disabled);
            var noMonksGateway = new CommandGateway(noMonks);
            noMonksGateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Cult));
            Steps(noMonksGateway, noMonks, disabled.Economy.AdvanceTicks + 2);
            Assert.That(noMonks.Capture(1).Economy.Civ, Is.Not.EqualTo(CivKind.Cult));

            var selected = AtCultAge();
            Assert.That(selected.sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Cult));
        }

        [Test]
        public void MonasteryUsesCultFootprintAndOnlyCultCanTrainItsMonk()
        {
            var state = AtCultAge();
            uint monastery = PlaceAndBuild(state.scenario, state.sim, state.gateway, ref state.sequence);
            Assert.That(monastery, Is.Not.EqualTo(0u));
            var building = state.sim.Capture(1).Economy.Buildings.First(b => b.Id == monastery);
            Assert.That((building.SizeMeters, building.Hp), Is.EqualTo((state.scenario.Economy.MonasterySizeCells * state.scenario.Map.CellSizeMeters,
                state.scenario.Economy.MonasteryHp)));

            var before = Fields(state.sim);
            int food = Number(before, "Economy[1].Food"), wood = Number(before, "Economy[1].Wood");
            state.gateway.SubmitEconomy(EconomyCommand.Train(1, ++state.sequence, monastery, UnitKind.Monk));
            Steps(state.gateway, state.sim, 1);
            var queued = Fields(state.sim);
            Assert.That(Number(queued, "Buildings[" + monastery + "].Queued"), Is.EqualTo(1));
            Assert.That(Number(queued, "Economy[1].Food"), Is.EqualTo(food - state.scenario.Economy.MonasteryMonkFoodCost));
            Assert.That(Number(queued, "Economy[1].Wood"), Is.EqualTo(wood - state.scenario.Economy.MonasteryMonkWoodCost));

            state.gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, ++state.sequence, monastery));
            Steps(state.gateway, state.sim, 1);
            var cancelled = Fields(state.sim);
            Assert.That(Number(cancelled, "Economy[1].Food"), Is.EqualTo(food));
            Assert.That(Number(cancelled, "Economy[1].Wood"), Is.EqualTo(wood));
        }

        [Test]
        public void CultExtensionRoundTripsAndKeepsOtherExtensions()
        {
            var scenario = CultScenario();
            scenario.Economy.Academy = true;
            scenario.Economy.GoldEnabled = true;
            scenario.Economy.Forestry = true;
            scenario.Economy.Masonry = true;
            scenario.Economy.Caravan = true;
            scenario.Economy.Bridge = true;
            byte[] bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.Cult, Is.True);
            Assert.That(decoded.Economy.MonasteryMonkWoodCost, Is.EqualTo(scenario.Economy.MonasteryMonkWoodCost));
            Assert.That(decoded.Extensions.Any(e => e.Id == 3), Is.True);
            Assert.That(decoded.Extensions.Any(e => e.Id == 2), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void CultReplayIsDeterministicForTwentyThousandTicks()
        {
            var scenario = CultScenario();
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
    }
}
