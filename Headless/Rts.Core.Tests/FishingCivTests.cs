using System;
using System.Collections.Generic;
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
    public sealed class FishingCivTests
    {
        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static ScenarioDefinition Scenario(bool fishingCiv = true)
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.FishingEnabled = true;
            scenario.Economy.FishingCiv = fishingCiv;
            scenario.Economy.StartFood = 5000;
            scenario.Economy.StartWood = 5000;
            scenario.Economy.AutoVillagerTarget = 3;
            scenario.Economy.AutoInfantryQueue = 0;
            scenario.Economy.AdvanceTicks = 1;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000;
            return scenario;
        }

        private static (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) AtFishingAge(bool auto = false)
        {
            var scenario = Scenario();
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, auto));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Fishing));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 3);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Fishing));
            return (scenario, sim, gateway, sequence);
        }

        private static bool RiverAdjacent(ScenarioDefinition scenario, int origin)
        {
            int width = scenario.Map.WidthCells, size = scenario.Economy.HarborSizeCells;
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                {
                    int cell = origin + z * width + x;
                    if (x == 0 && origin % width > 0 && scenario.Map.Terrain[cell - 1] == (byte)TerrainKind.River
                        || x + 1 == size && origin % width + size < width && scenario.Map.Terrain[cell + 1] == (byte)TerrainKind.River
                        || z == 0 && origin / width > 0 && scenario.Map.Terrain[cell - width] == (byte)TerrainKind.River
                        || z + 1 == size && origin / width + size < scenario.Map.HeightCells && scenario.Map.Terrain[cell + width] == (byte)TerrainKind.River)
                        return true;
                }
            return false;
        }

        private static uint PlaceHarbor((ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) state)
        {
            int width = state.scenario.Map.WidthCells, cells = width * state.scenario.Map.HeightCells;
            int size = state.scenario.Economy.HarborSizeCells;
            ulong sequence = state.sequence;
            for (int origin = 0; origin < cells; origin++)
            {
                if (origin % width + size > width || origin / width + size > state.scenario.Map.HeightCells || !RiverAdjacent(state.scenario, origin)) continue;
                sequence++;
                state.gateway.SubmitEconomy(EconomyCommand.Place(1, sequence, BuildingKind.Harbor, origin, Facing.North));
                Steps(state.gateway, state.sim, 1);
                var harbor = state.sim.Capture(1).Economy.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Harbor);
                if (harbor.Id != 0) return harbor.Id;
            }
            return 0;
        }

        [Test]
        public void FishingCivOffKeepsOldBytesAndState()
        {
            var old = MapGenerator.GenerateTerrain(1);
            var off = MapGenerator.GenerateTerrain(1);
            off.Economy.FishingEnabled = false;
            off.Economy.FishingCiv = false;
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
        public void FishingCivCannotBeChosenWithoutFishingEnabled()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.FishingEnabled = false;
            scenario.Economy.FishingCiv = false;
            scenario.Economy.StartFood = 5000; scenario.Economy.StartWood = 5000; scenario.Economy.AdvanceTicks = 1;
            var sim = new Battle(scenario); var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Fishing));
            Steps(gateway, sim, 3);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Primitive));
        }

        [Test]
        public void FishingCivCanPlaceHarborOnlyOnRiverBank()
        {
            var state = AtFishingAge();
            int before = state.sim.Capture(1).Economy.Buildings.Count;
            state.gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.sequence, BuildingKind.Harbor, 0, Facing.North));
            Steps(state.gateway, state.sim, 1);
            Assert.That(state.sim.Capture(1).Economy.Buildings.Count, Is.EqualTo(before));
            Assert.That(PlaceHarbor(state), Is.Not.EqualTo(0u));
        }

        [Test]
        public void FishingExtensionRoundTripsWithAcademyAndExistingExtension()
        {
            var scenario = Scenario();
            scenario.Economy.Academy = true;
            scenario.Extensions = new[] { new ScenarioExtensionData { Id = 1, Version = 1, Data = BitConverter.GetBytes(1234) } };
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.FishingCiv, Is.True);
            Assert.That(decoded.Extensions.Select(e => e.Id), Is.EquivalentTo(new[] { 1, 2, 4 }));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void FishingCivAutomaticallyBuildsHarborAndGathersFish()
        {
            var state = AtFishingAge(true);
            Steps(state.gateway, state.sim, 5000);
            var economy = state.sim.Capture(1).Economy;
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.Harbor && b.Complete), Is.True);
            Assert.That(economy.Villagers.Any(v => v.Activity == VillagerActivity.Gathering || v.Activity == VillagerActivity.ToResource), Is.True);
        }

        [Test]
        public void FishingCivReplayIsDeterministicForTwentyThousandTicks()
        {
            var scenario = Scenario();
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
