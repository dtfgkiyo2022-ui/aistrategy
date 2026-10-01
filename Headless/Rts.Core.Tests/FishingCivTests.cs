using System;
using System.Collections.Generic;
using System.IO;
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
    public sealed class FishingCivTests
    {
        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static ScenarioDefinition Scenario(bool fishingCiv = true, bool emptyFood = false)
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
            if (emptyFood)
            {
                for (int i = 0; i < scenario.ResourceNodes.Length; i++)
                    if (scenario.ResourceNodes[i].Kind == ResourceKind.Food) scenario.ResourceNodes[i].Amount = 1;
                scenario.Economy.FishRegrowTicks = 100000;
            }
            return scenario;
        }

        private static (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) AtFishingAge(bool auto = false, bool emptyFood = false)
        {
            var scenario = Scenario(true, emptyFood);
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

        private static uint PlaceHarbor(ref (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) state)
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
                if (harbor.Id != 0) { state.sequence = sequence; return harbor.Id; }
            }
            state.sequence = sequence;
            return 0;
        }

        private static uint PlaceAndBuildHarbor(ref (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) state)
        {
            uint harbor = PlaceHarbor(ref state);
            Assert.That(harbor, Is.Not.EqualTo(0u));
            state.gateway.SubmitEconomy(EconomyCommand.Assign(1, ++state.sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, harbor));
            Steps(state.gateway, state.sim, 1500);
            Assert.That(state.sim.Capture(1).Economy.Buildings.First(b => b.Id == harbor).Complete, Is.True);
            return harbor;
        }

        private static void AdvanceFishingAge(ref (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) state)
        {
            state.gateway.SubmitEconomy(EconomyCommand.Advance(1, ++state.sequence, CivKind.Fishing));
            Steps(state.gateway, state.sim, state.scenario.Economy.Age2Ticks + 3);
            Assert.That(state.sim.Capture(1).Economy.Age, Is.EqualTo(2));
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
            Assert.That(PlaceHarbor(ref state), Is.Not.EqualTo(0u));
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

        [Test]
        public void FishingNetResearchRunsAtHarborAndOnlyRaisesFishCarry()
        {
            var state = AtFishingAge();
            uint harbor = PlaceAndBuildHarbor(ref state);
            AdvanceFishingAge(ref state);
            var before = state.sim.Capture(1).Economy;
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, harbor, FishingTech.FishingNet));
            Steps(state.gateway, state.sim, state.scenario.Economy.FishingNetTicks + 2);
            var after = state.sim.Capture(1).Economy;
            Assert.That(after.Techs & (1UL << 15), Is.Not.EqualTo(0));
            var carry = typeof(Battle).GetMethod("CarryFor", BindingFlags.Instance | BindingFlags.NonPublic);
            int normalBefore = (int)carry.Invoke(state.sim, new object[] { 1u, false });
            int fishAfter = (int)carry.Invoke(state.sim, new object[] { 1u, true });
            Assert.That(fishAfter, Is.GreaterThan(normalBefore));
            Assert.That(after.Techs & (1UL << ((int)TechKind.Tools - 1)), Is.EqualTo(before.Techs & (1UL << ((int)TechKind.Tools - 1))));
        }

        [Test]
        public void DriedFishResearchRunsAtTheThirdAgeAndStoresTheFasterRegrowEffect()
        {
            var state = AtFishingAge();
            uint harbor = PlaceAndBuildHarbor(ref state);
            AdvanceFishingAge(ref state);
            state.gateway.SubmitEconomy(EconomyCommand.Advance(1, ++state.sequence, CivKind.Fishing));
            Steps(state.gateway, state.sim, state.scenario.Economy.Age3Ticks + 3);
            Assert.That(state.sim.Capture(1).Economy.Age, Is.EqualTo(3));
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, harbor, FishingTech.DriedFish));
            Steps(state.gateway, state.sim, 1);
            var building = state.sim.Capture(1).Economy.Buildings.First(b => b.Id == harbor);
            Assert.That(building.Researching, Is.EqualTo(FishingTech.DriedFish));
            Steps(state.gateway, state.sim, state.scenario.Economy.DriedFishTicks + 2);
            Assert.That(state.sim.Capture(1).Economy.Techs & (1UL << 16), Is.Not.EqualTo(0));
            Assert.That(state.scenario.Economy.DriedFishRegrowIntervalPermille, Is.EqualTo(333));
        }

        [Test]
        public void FishingCivOpensFoodMarketAfterCoveredFishIsGone()
        {
            var state = AtFishingAge(false, true);
            PlaceAndBuildHarbor(ref state);
            state.gateway.SubmitEconomy(EconomyCommand.Auto(1, ++state.sequence, true));
            Steps(state.gateway, state.sim, 600);
            Assert.That(state.sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.Market), Is.True);
        }

        [Test]
        public void FishingExtensionVersionOneIsReadableAndVersionTwoRoundTrips()
        {
            var scenario = Scenario();
            byte[] current = ScenarioBinary.Encode(scenario);
            int marker = FindInt32(current, 0x4E545845);
            Assert.That(marker, Is.GreaterThanOrEqualTo(0));
            int record = marker + 12;
            Assert.That(BitConverter.ToInt32(current, record), Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(current, record + 4), Is.EqualTo(2));
            Assert.That(BitConverter.ToInt32(current, record + 8), Is.EqualTo(52));
            byte[] old = new byte[current.Length - 32];
            Buffer.BlockCopy(current, 0, old, 0, record + 12 + 20);
            Buffer.BlockCopy(current, record + 12 + 52, old, record + 12 + 20, current.Length - (record + 12 + 52));
            Array.Copy(BitConverter.GetBytes(BitConverter.ToInt32(current, marker + 8) - 32), 0, old, marker + 8, 4);
            Array.Copy(BitConverter.GetBytes(1), 0, old, record + 4, 4);
            Array.Copy(BitConverter.GetBytes(20), 0, old, record + 8, 4);
            var decoded = ScenarioBinary.Decode(old);
            Assert.That(decoded.Economy.FishingCiv, Is.True);
            byte[] upgraded = ScenarioBinary.Encode(decoded);
            Assert.That(BitConverter.ToInt32(upgraded, FindInt32(upgraded, 0x4E545845) + 20), Is.EqualTo(52));
            Assert.That(ScenarioBinary.Encode(ScenarioBinary.Decode(upgraded)), Is.EqualTo(upgraded));
        }

        private static int FindInt32(byte[] bytes, int value)
        {
            byte[] pattern = BitConverter.GetBytes(value);
            for (int i = 0; i <= bytes.Length - pattern.Length; i++)
                if (bytes[i] == pattern[0] && bytes[i + 1] == pattern[1] && bytes[i + 2] == pattern[2] && bytes[i + 3] == pattern[3]) return i;
            return -1;
        }
    }
}
