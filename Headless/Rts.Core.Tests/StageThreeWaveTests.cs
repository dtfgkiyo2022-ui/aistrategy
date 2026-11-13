using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class StageThreeWaveTests
    {
        [Test]
        public void StageThreeStagesAtTheFirstBreachWhileEarlierStagesKeepTheCoreRally()
        {
            var stageTwo = RunToGathering(2);
            var stageThree = RunToGathering(3);

            var stageTwoCore = stageTwo.Scenario.Cores[0].Position;
            var stageTwoEvent = stageTwo.Events.Single(e => e.Value == 1);
            var stageThreeEvent = stageThree.Events.Single(e => e.Value == 1);

            Assert.That(stageTwoEvent.Position, Is.EqualTo(stageTwoCore));
            Assert.That(stageThreeEvent.Position, Is.Not.EqualTo(stageTwoCore));
            Assert.That(stageThree.Simulation.FortifyMetrics(2).RingGaps, Is.EqualTo(0));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void StageZeroThroughTwoKeepTheExistingCoreRally(int level)
        {
            var result = RunToGathering(level);
            var core = result.Scenario.Cores[0].Position;
            Assert.That(result.Events.Single(e => e.Value == 1).Position, Is.EqualTo(core));
        }

        private static (ScenarioDefinition Scenario, Battle Simulation, List<GameEvent> Events) RunToGathering(int level)
        {
            var scenario = LiveGameRules.Create(40041, true, true, false, false);
            scenario.Economy.AiLevelWest = level;
            scenario.Economy.AiLevelEast = 2;
            scenario.Economy.StartFood = 100000;
            scenario.Economy.StartWood = 100000;
            scenario.Economy.StartStone = 100000;
            scenario.Economy.LatePushAfterTicks = 1000;
            scenario.Economy.LatePushAdvantageAfterTicks = 100000;
            scenario.VerificationTickLimit = 1400;
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            gateway.Step();
            var state = (WorldState)typeof(Battle).GetField("world", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(simulation);
            ulong sequence = 100;
            var ring = Ring(state, 2, 6);
            int width = state.Config.Map.WidthCells;
            int core = state.Map.Cell(state.Cores[state.Factions[1].CoreId - 1].Definition.Position);
            int gate = ring.First(cell => cell % width == core % width && state.Map.IsPassable(cell));
            gateway.SubmitEconomy(EconomyCommand.PlaceGate(2, ++sequence, gate));
            gateway.Step();
            gateway.SubmitEconomy(EconomyCommand.PlaceWall(2, ++sequence, ring.Where(cell => cell != gate).ToList()));
            gateway.Step();

            var events = new List<GameEvent>();
            for (long tick = simulation.Capture(1).Tick + 1; tick <= 1200; tick++)
            {
                simulation.Step(tick, Array.Empty<ScheduledInput>());
                events.AddRange(simulation.Capture(1).Events.Where(e => e.Kind == EventKind.AiReport && e.Value == 1));
            }
            return (scenario, simulation, events.Distinct().ToList());
        }

        private static List<int> Ring(WorldState state, uint faction, int radius)
        {
            int width = state.Config.Map.WidthCells, height = state.Config.Map.HeightCells;
            int core = state.Map.Cell(state.Cores[state.Factions[faction - 1].CoreId - 1].Definition.Position);
            int cx = core % width, cz = core / width;
            var cells = new List<int>();
            for (int z = Math.Max(0, cz - radius); z <= Math.Min(height - 1, cz + radius); z++)
                for (int x = Math.Max(0, cx - radius); x <= Math.Min(width - 1, cx + radius); x++)
                    if (Math.Max(Math.Abs(x - cx), Math.Abs(z - cz)) == radius) cells.Add(z * width + x);
            return cells;
        }
    }
}
