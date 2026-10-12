using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class ManualControlTests
    {
        [Test]
        public void ManualControlExtensionAndCommandRoundTrip()
        {
            var scenario = LatePushScenario();
            scenario.Economy.ManualControl = true;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);

            Assert.That(decoded.Economy.ManualControl, Is.True);
            Assert.That(decoded.Extensions.Any(x => x.Id == 19), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var input = new ScheduledInput(1, 0, 1, EconomyCommand.SetAutoAssault(1, 7, false));
            var copy = InputBinary.Decode(InputBinary.Encode(input));
            Assert.That(copy.Economy.Kind, Is.EqualTo(EconomyCommandKind.SetAutoAssault));
            Assert.That(copy.Economy.Enabled, Is.False);
            Assert.That(InputBinary.Encode(copy), Is.EqualTo(InputBinary.Encode(input)));
        }

        [Test]
        public void ManualControlStopsAndResumesLatePush()
        {
            var scenario = LatePushScenario();
            scenario.Economy.ManualControl = true;
            var sim = new Battle(scenario);
            sim.Step(1, Array.Empty<ScheduledInput>());
            sim.Step(2, Array.Empty<ScheduledInput>());
            Assert.That(sim.Capture(1).Commands.Any(c => c.Source == CommandSource.Doctrine
                && c.Kind == PolicyKind.Focus && c.Goal.Kind == GoalKind.Core
                && c.Status == CommandStatus.Executing), Is.True);

            sim.Step(3, new[] { new ScheduledInput(3, 2, 3,
                EconomyCommand.SetAutoAssault(1, 1, false)) });
            Assert.That(sim.Capture(1).Economy.AutoAssaultOff, Is.True);
            Assert.That(sim.Capture(1).Commands.Any(c => c.Source == CommandSource.Doctrine
                && c.Kind == PolicyKind.Focus && c.Status == CommandStatus.Executing), Is.False);

            sim.Step(4, new[] { new ScheduledInput(4, 3, 4,
                EconomyCommand.SetAutoAssault(1, 2, true)) });
            Assert.That(sim.Capture(1).Economy.AutoAssaultOff, Is.False);
            Assert.That(sim.Capture(1).Commands.Any(c => c.Source == CommandSource.Doctrine
                && c.Kind == PolicyKind.Focus && c.Goal.Kind == GoalKind.Core
                && c.Status == CommandStatus.Executing), Is.True);
        }

        [Test]
        public void DisabledManualControlCommandHasNoStateOrDiagnosticEffect()
        {
            var scenario = LatePushScenario();
            scenario.Economy.ManualControl = false;
            var withIgnoredCommand = new Battle(ScenarioBinary.Decode(ScenarioBinary.Encode(scenario)));
            var baseline = new Battle(ScenarioBinary.Decode(ScenarioBinary.Encode(scenario)));
            var command = EconomyCommand.SetAutoAssault(1, 1, false);

            for (long tick = 1; tick <= 4; tick++)
            {
                var inputs = tick == 1
                    ? new[] { new ScheduledInput(1, 0, 1, command) }
                    : Array.Empty<ScheduledInput>();
                withIgnoredCommand.Step(tick, inputs);
                baseline.Step(tick, Array.Empty<ScheduledInput>());
                var actual = DiagnosticComparison.Fields(withIgnoredCommand.CaptureDiagnostic())
                    .Where(x => x.Key != "Gateway.NextLogIndex" && x.Key != "Inputs.Cursor").ToArray();
                var expected = DiagnosticComparison.Fields(baseline.CaptureDiagnostic())
                    .Where(x => x.Key != "Gateway.NextLogIndex" && x.Key != "Inputs.Cursor").ToArray();
                Assert.That(actual, Is.EqualTo(expected), "tick " + tick);
            }

            Assert.That(withIgnoredCommand.Capture(1).Economy.AutoAssaultOff, Is.False);
            Assert.That(DiagnosticComparison.Fields(withIgnoredCommand.CaptureDiagnostic())
                .Any(x => x.Key.StartsWith("ManualControl.", StringComparison.Ordinal)), Is.False);
            Assert.That(withIgnoredCommand.Capture(1).Commands.Any(c => c.Source == CommandSource.Doctrine
                && c.Kind == PolicyKind.Focus && c.Goal.Kind == GoalKind.Core
                && c.Status == CommandStatus.Executing), Is.True);
        }

        [Test]
        public void ManualControlStopsPushWavesUntilReenabled()
        {
            var scenario = WaveScenario();
            scenario.Economy.ManualControl = true;
            var sim = new Battle(scenario);
            var events = new List<GameEvent>();
            sim.Step(1, new[] { new ScheduledInput(1, 0, 1,
                EconomyCommand.SetAutoAssault(1, 1, false)) });
            for (long tick = 2; tick <= 4; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                events.AddRange(WaveEvents(sim));
            }
            Assert.That(events, Is.Empty);
            Assert.That(DiagnosticComparison.Fields(sim.CaptureDiagnostic())
                .Any(x => x.Key == "PushWaves.Faction1.Phase" && x.Value != "0"), Is.False);

            sim.Step(5, new[] { new ScheduledInput(5, 4, 5,
                EconomyCommand.SetAutoAssault(1, 2, true)) });
            Assert.That(WaveEvents(sim).Any(e => e.Value == 1), Is.True);
            Assert.That(sim.Capture(1).Economy.AutoAssaultOff, Is.False);
        }

        private static ScenarioDefinition LatePushScenario()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.LatePush = true;
            scenario.Economy.LatePushAfterTicks = 1;
            scenario.Economy.LatePushAdvantageAfterTicks = 100000;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 1000000;
            return scenario;
        }

        private static ScenarioDefinition WaveScenario()
        {
            var scenario = LatePushScenario();
            scenario.Economy.SiegeByAge = true;
            scenario.Economy.PushWaves = true;
            scenario.Economy.AgeClock = true;
            scenario.Economy.AgeClockTicks1 = 1;
            scenario.Economy.AgeClockTicks2 = 2;
            scenario.Economy.AgeClockTicks3 = 3;
            scenario.Economy.WaveSize = 1000;
            return scenario;
        }

        private static IEnumerable<GameEvent> WaveEvents(Battle sim)
            => sim.Capture(1).Events.Where(e => e.Kind == EventKind.AiReport && e.Reason == ReasonCode.None
                && (e.Value == 1 || e.Value == 2 || e.Value == 3));
    }
}
