using System;
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
    public sealed class FreeTownsTests
    {
        private static ScenarioDefinition Scenario(bool free)
        {
            var scenario = MapGenerator.GenerateTerrain(1, processingChain: true);
            var e = scenario.Economy;
            e.Towns = true;
            e.FreeTowns = free;
            e.FreeTownsPerAge = 2;
            e.StartFood = e.StartWood = e.StartStone = e.StartMetal = 0;
            e.AutoVillagerTarget = 0;
            e.AutoInfantryQueue = 0;
            e.TownWork = 1;
            e.TownMaxBuildings = 10;
            e.TownCoreDistance = 1;
            e.TownResourceReach = 16;
            e.Ages = true;
            e.AgeClock = false;
            e.AgeClockUx = false;
            e.AdvanceFoodCost = e.AdvanceWoodCost = 0;
            e.AdvanceTicks = 1;
            e.Age2FoodCost = e.Age2WoodCost = 0;
            e.Age2Ticks = 1;
            e.BuildingReservations = false;
            e.NoNegativeStock = true;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000000;
            return scenario;
        }

        [Test]
        public void FlagOffPreservesEveryTickHash()
        {
            var left = new Battle(Scenario(false));
            var right = new Battle(ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario(false))));
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void Extension70RoundTripsFreeTownRules()
        {
            var source = Scenario(true);
            source.Economy.FreeTownsPerAge = 1;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Any(x => x.Id == 70 && x.Version == 1), Is.True);
            Assert.That(decoded.Economy.FreeTowns, Is.True);
            Assert.That(decoded.Economy.FreeTownsPerAge, Is.EqualTo(1));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void HumanPrimitiveTownsAreFreeAndAgeRaisesTheLimit()
        {
            var scenario = Scenario(true);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            uint first = PlaceTown(scenario, sim, gateway, ref sequence);
            uint second = PlaceTown(scenario, sim, gateway, ref sequence);
            Assert.That(first, Is.Not.EqualTo(0u));
            Assert.That(second, Is.Not.EqualTo(0u));
            Assert.That(PlaceTown(scenario, sim, gateway, ref sequence), Is.EqualTo(0u));
            Run(gateway, sim, 300);
            Assert.That(sim.Capture(1).Economy.Buildings.Count(b => b.Kind == BuildingKind.Town && b.Complete), Is.EqualTo(2));

            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Agrarian));
            Run(gateway, sim, 3);
            Assert.That(sim.Capture(1).Economy.Age, Is.EqualTo(1));
            Assert.That(PlaceTown(scenario, sim, gateway, ref sequence), Is.Not.EqualTo(0u));
            Assert.That(PlaceTown(scenario, sim, gateway, ref sequence), Is.Not.EqualTo(0u));
            Assert.That(PlaceTown(scenario, sim, gateway, ref sequence), Is.EqualTo(0u));
        }

        [Test]
        public void AutomaticEconomyBuildsACompletePrimitiveTown()
        {
            var scenario = LiveGameRules.Create(68003UL, true, true, false, false);
            var e = scenario.Economy;
            e.FreeTowns = true;
            e.FreeTownsPerAge = 2;
            e.AgeClock = false;
            e.AgeClockUx = false;
            e.StartFood = e.StartWood = e.StartStone = e.StartMetal = 100000;
            e.TownWork = 1;
            e.AutoVillagerTarget = 80;
            e.AutoInfantryQueue = 0;
            e.AiExpandDecisionIntervalTicks = 1;
            e.TownCoreDistance = 1;
            e.TownResourceReach = 16;
            var sim = new Battle(scenario);
            int ageAtFirstCompletedTown = -1;
            for (long tick = 1; tick <= 20000 && ageAtFirstCompletedTown < 0 && !sim.Capture(1).Result.HasEnded; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                var frame = sim.Capture(1);
                if (frame.Economy.Buildings.Any(b => b.Kind == BuildingKind.Town && b.Complete))
                    ageAtFirstCompletedTown = frame.Economy.Age;
            }
            Assert.That(ageAtFirstCompletedTown, Is.EqualTo(0));
        }

        [Test]
        public void DestroyedTownCanBeRebuiltWithinTheSameLimit()
        {
            var scenario = Scenario(true);
            scenario.Economy.FreeTownsPerAge = 1;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            uint first = PlaceTown(scenario, sim, gateway, ref sequence);
            Assert.That(first, Is.Not.EqualTo(0u));
            Run(gateway, sim, 2000);
            Assert.That(sim.Capture(1).Economy.Buildings.Any(b => b.Id == first && b.Complete), Is.True);
            DestroyBuildingForTest(sim, first);
            Run(gateway, sim, 3);
            Assert.That(PlaceTown(scenario, sim, gateway, ref sequence), Is.Not.EqualTo(0u));
        }

        [Test]
        public void FreeTownReplayMatchesEveryTickHash()
        {
            var identity = new BuildIdentity();
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, Scenario(true), Array.Empty<ScheduledInput>(), 3000, identity);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, identity);
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
        }

        private static uint PlaceTown(ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ref ulong sequence)
        {
            int width = scenario.Map.WidthCells, size = scenario.Economy.TownSizeCells;
            int before = sim.Capture(1).Economy.Buildings.Count;
            for (int cell = 0; cell < scenario.Map.WidthCells * scenario.Map.HeightCells; cell++)
            {
                int x = cell % width, z = cell / width;
                if (x + size > width || z + size > scenario.Map.HeightCells) continue;
                gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Town, cell, Facing.North));
                gateway.Step();
                var added = sim.Capture(1).Economy.Buildings.Skip(before).FirstOrDefault(b => b.Kind == BuildingKind.Town);
                if (added.Id != 0) return added.Id;
            }
            return 0;
        }

        private static void Run(CommandGateway gateway, Battle sim, int ticks)
        {
            for (int i = 0; i < ticks && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static void Run(Battle sim, int ticks)
        {
            for (long tick = 1; tick <= ticks && !sim.Capture(1).Result.HasEnded; tick++)
                sim.Step(tick, Array.Empty<ScheduledInput>());
        }

        private static void DestroyBuildingForTest(Battle sim, uint building)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var world = typeof(Battle).GetField("world", flags).GetValue(sim);
            var states = (Array)world.GetType().GetField("Buildings", flags).GetValue(world);
            object state = states.GetValue((int)building - 1);
            state.GetType().GetField("Hp", flags).SetValue(state, 0);
            states.SetValue(state, (int)building - 1);
        }
    }
}
