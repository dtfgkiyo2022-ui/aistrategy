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
    public sealed class ArmySplitTests
    {
        private static ScenarioDefinition Scenario(bool enabled)
        {
            var scenario = WeekOneScenario.Create();
            scenario.Outposts = Array.Empty<OutpostDefinition>();
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 1000000;
            scenario.Economy.Enabled = true;
            scenario.Economy.ArmySplit = enabled;
            return scenario;
        }

        private static (uint army, uint soldier) SplitSelection(Battle simulation)
        {
            var frame = simulation.Capture(1);
            var army = frame.Observation.OwnArmies.First(a => a.AliveCount >= 2 && a.Kind != UnitKind.Scout);
            var soldier = frame.Units.First(u => u.IsOwn && u.Kind != UnitKind.Villager && u.ArmyId == army.Id).Id;
            return (army.Id, soldier);
        }

        private static Dictionary<string, string> Fields(Battle simulation)
            => DiagnosticComparison.Fields(simulation.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        [Test]
        public void FlagOffLeavesTheStateUnchangedWhenSplitInputArrives()
        {
            var left = new Battle(Scenario(false));
            var right = new Battle(Scenario(false));
            var selected = SplitSelection(left);
            var gateway = new CommandGateway(left);
            gateway.SubmitEconomy(EconomyCommand.SplitArmy(1, 2, selected.army, new[] { selected.soldier }));
            for (long tick = 1; tick <= 20; tick++)
            {
                left.Step(tick, tick == 1 ? gateway.Inputs.ToArray() : Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
            }
            Assert.That(left.CaptureDiagnostic().CanonicalState, Is.EqualTo(right.CaptureDiagnostic().CanonicalState));
        }

        [Test]
        public void SplitCopiesPolicyAndMovesSelectedSoldierInOrder()
        {
            var scenario = Scenario(true);
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            var selected = SplitSelection(simulation);
            gateway.Submit(new UserPolicyIntent(1, new ScopeKey(1, ScopeKind.Army, selected.army), PolicyKind.Focus,
                new PolicyGoal(GoalKind.Point, 0, new SimPoint(Fix64.FromInt(100), Fix64.FromInt(64))), 50,
                new LossBudget(1000), new EndCondition(EndKind.UntilReplaced, 0), 0,
                new Expiration(long.MaxValue, 0, ExpireFlags.SubjectGone)));
            gateway.Step();
            var before = Fields(simulation);
            string policy = before["Commands[Army=" + selected.army + "].Policy"];
            gateway.SubmitEconomy(EconomyCommand.SplitArmy(1, 2, selected.army, new[] { selected.soldier }));
            gateway.Step();

            var frame = simulation.Capture(1);
            var moved = frame.Units.Single(u => u.IsOwn && u.Id == selected.soldier).ArmyId;
            Assert.That(moved, Is.Not.EqualTo(selected.army));
            Assert.That(frame.Observation.OwnArmies.Single(a => a.Id == moved).AliveCount, Is.EqualTo(1));
            var after = Fields(simulation);
            Assert.That(after["Commands[Army=" + moved + "].Policy"], Is.EqualTo(policy));

            for (int i = 0; i < 600; i++) gateway.Step();
            var progressed = simulation.Capture(1).Observation.OwnArmies;
            Assert.That(progressed.Any(a => a.Id == selected.army && a.AliveCount > 0), Is.True);
            Assert.That(progressed.Any(a => a.Id == moved && a.AliveCount > 0), Is.True);
            Assert.That(progressed.Single(a => a.Id == moved).Position, Is.Not.EqualTo(default(SimPoint)));
        }

        [Test]
        public void SplitReplayMatchesEveryTick()
        {
            var scenario = Scenario(true);
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            var selected = SplitSelection(simulation);
            gateway.SubmitEconomy(EconomyCommand.SplitArmy(1, 1, selected.army, new[] { selected.soldier }));
            for (int i = 0; i < 600; i++) gateway.Step();
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, gateway.Inputs, simulation.Capture(1).Tick, new BuildIdentity());
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
                Assert.That(result.LastTick, Is.GreaterThanOrEqualTo(600));
            }
        }

        [Test]
        public void MergeMovesLivingSourceSoldiersInIdOrderAndLeavesTheSourceArmy()
        {
            var simulation = new Battle(Scenario(true));
            var gateway = new CommandGateway(simulation);
            gateway.SubmitEconomy(EconomyCommand.MergeArmies(1, 1, 1, new[] { 2u }));
            gateway.Step();
            var frame = simulation.Capture(1);
            Assert.That(frame.Observation.OwnArmies.Single(a => a.Id == 1).AliveCount, Is.EqualTo(8));
            Assert.That(frame.Observation.OwnArmies.Single(a => a.Id == 2).AliveCount, Is.EqualTo(0));
            var merged = frame.Units.Where(u => u.IsOwn && u.Kind != UnitKind.Villager && u.ArmyId == 1).Select(u => u.Id).ToArray();
            Assert.That(merged, Is.EqualTo(new uint[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        }

        [Test]
        public void ArmySplitExtensionRoundTripsAndIsAbsentWhenOff()
        {
            var on = Scenario(true);
            var bytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.ArmySplit, Is.True);
            Assert.That(decoded.Extensions.Single(e => e.Id == 98).Version, Is.EqualTo(1));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var off = Scenario(false);
            off.Extensions = new[] { new ScenarioExtensionData { Id = 98, Version = 1, Data = BitConverter.GetBytes(1) } };
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(off)).Extensions.Any(e => e.Id == 98), Is.False);
        }
    }
}
