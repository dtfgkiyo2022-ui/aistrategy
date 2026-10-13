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

        [Test]
        public void StopVillagersCommandLeavesEveryoneIdleAndPreservesCarriedStock()
        {
            var scenario = LatePushScenario();
            var sim = new Battle(scenario);
            var gateway = new Rts.Application.CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            gateway.SubmitEconomy(EconomyCommand.StopVillagers(1, 2));
            gateway.Step();

            var stopped = sim.Capture(1);
            var stockFood = stopped.Economy.Food;
            var stockWood = stopped.Economy.Wood;
            var carried = stopped.Economy.Villagers.Where(v => v.IsOwn).ToDictionary(v => v.Id, v => v.Carry);
            Assert.That(stopped.Economy.Villagers.Where(v => v.IsOwn).All(v => v.Activity == VillagerActivity.Idle), Is.True);
            Assert.That(stopped.Economy.Villagers.Where(v => v.IsOwn).All(v => v.PlayerHeld), Is.True);

            for (int i = 0; i < 600; i++) gateway.Step();

            var after = sim.Capture(1);
            Assert.That(after.Economy.AutoEconomy, Is.False);
            Assert.That(after.Economy.Food, Is.EqualTo(stockFood));
            Assert.That(after.Economy.Wood, Is.EqualTo(stockWood));
            foreach (var villager in after.Economy.Villagers.Where(v => v.IsOwn))
            {
                Assert.That(villager.Activity, Is.EqualTo(VillagerActivity.Idle), "villager " + villager.Id);
                Assert.That(villager.Carry, Is.EqualTo(carried[villager.Id]), "carried stock " + villager.Id);
            }
        }

        [Test]
        public void ReturnToAutoHumanCommandStopsAnArmyAndIsReplayable()
        {
            var scenario = LatePushScenario();
            scenario.Economy.ManualControl = true;
            var sim = new Battle(scenario);
            var gateway = new Rts.Application.CommandGateway(sim);
            gateway.Submit(new UserPolicyIntent(1, new ScopeKey(1, ScopeKind.Army, 1), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, new SimPoint(Fix64.FromInt(120), Fix64.FromInt(64))), 50,
                new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 0,
                new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            gateway.Step();
            gateway.Submit(new UserPolicyIntent(2, new ScopeKey(1, ScopeKind.All, 0), PolicyKind.ReturnToAuto,
                default(PolicyGoal), 100, new LossBudget(1000), new EndCondition(EndKind.UntilReplaced, 0), 0,
                new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            gateway.SubmitEconomy(EconomyCommand.SetAutoAssault(1, 3, false));
            gateway.Step();
            var stopped = sim.Capture(1);
            var positions = stopped.Units.Where(u => u.IsOwn && u.Kind != UnitKind.Villager)
                .ToDictionary(u => u.Id, u => u.Position);
            Assert.That(stopped.Commands.Any(c => c.Source == CommandSource.Human && c.Kind == PolicyKind.Focus
                && c.Status == CommandStatus.Executing), Is.False);

            for (int i = 0; i < 600; i++) gateway.Step();
            var after = sim.Capture(1);
            foreach (var unit in after.Units.Where(u => u.IsOwn && u.Kind != UnitKind.Villager))
                Assert.That(unit.Position, Is.EqualTo(positions[unit.Id]), "unit " + unit.Id + " moved while stopped (expected "
                    + positions[unit.Id].X.Raw + "," + positions[unit.Id].Z.Raw + ", actual "
                    + unit.Position.X.Raw + "," + unit.Position.Z.Raw + ")");
            Assert.That(after.Economy.AutoAssaultOff, Is.True);

            using (var stream = new System.IO.MemoryStream())
            {
                var build = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, gateway.Inputs, after.Tick, build);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, build);
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
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
