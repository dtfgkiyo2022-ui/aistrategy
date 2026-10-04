using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class LatePushTests
    {
        [Test]
        public void DisabledLatePushKeepsBytesAndStateUnchanged()
        {
            var baseline = MapGenerator.GenerateTerrain(1);
            var disabled = MapGenerator.GenerateTerrain(1);
            disabled.Economy.LatePush = false;
            Assert.That(ScenarioBinary.Encode(disabled), Is.EqualTo(ScenarioBinary.Encode(baseline)));
            var left = new Battle(disabled); var right = new Battle(baseline);
            for (long tick = 1; tick <= 200; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void EachLatePushConditionHasItsBoundary()
        {
            var rules = new EconomyRules { LatePush = true, LatePushAfterTicks = 14400, LatePushAdvantageAfterTicks = 9600,
                LatePushEnemyMultiplierPermille = 1500 };
            Assert.That(Battle.LatePushCondition(Observation(14399, 2, 2, 1), rules), Is.False);
            Assert.That(Battle.LatePushCondition(Observation(14400, 0, 0, 0), rules), Is.True);
            Assert.That(Battle.LatePushCondition(Observation(9599, 3, 1, 1), rules), Is.False);
            // No visible enemy is not an advantage (the fog hides them).
            Assert.That(Battle.LatePushCondition(Observation(9600, 3, 0, 0), rules), Is.False);
            Assert.That(Battle.LatePushCondition(Observation(9599, 3, 0, 0), rules), Is.False);
            Assert.That(Battle.LatePushCondition(Observation(9600, 3, 2, 1), rules), Is.True);
            Assert.That(Battle.LatePushCondition(Observation(9600, 2, 2, 0), rules), Is.False);
            Assert.That(Battle.LatePushCondition(Observation(9600, 3, 2, 0), rules), Is.True);
        }

        [Test]
        public void LatePushCreatesCoreFocusAndHumanWins()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.LatePush = true;
            scenario.Economy.LatePushAfterTicks = 1;
            scenario.Economy.LatePushAdvantageAfterTicks = 100000;
            var sim = new Battle(scenario);
            sim.Step(1, Array.Empty<ScheduledInput>());
            sim.Step(2, Array.Empty<ScheduledInput>());
            var automatic = sim.Capture(1).Commands.FirstOrDefault(c => c.Source == CommandSource.Doctrine
                && c.Kind == PolicyKind.Focus && c.Goal.Kind == GoalKind.Core && c.Status == CommandStatus.Executing);
            Assert.That(automatic.CommandId, Is.Not.Zero);

            var target = new ScopeKey(1, ScopeKind.All, 0);
            var humanOrder = new PolicyOrder(1, 0, CommandSource.Human, target, PolicyKind.Focus,
                new PolicyGoal(GoalKind.Core, 2, default), 1, new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 0,
                0, Array.Empty<PolicyVersion>(), 2, new Expiration(long.MaxValue, 0, ExpireFlags.None));
            Rts.Tests.EditMode.CommandTestInput.Step(sim, 3, new[] { new ScheduledInput(1, InputKind.Proposal, 2, 3, 1, 1, new[] { humanOrder }) });
            Assert.That(sim.Capture(1).Commands.Any(c => c.Source == CommandSource.Human && c.Kind == PolicyKind.Focus
                && c.Status == CommandStatus.Executing), Is.True);
        }

        [Test]
        public void LatePushExtensionRoundTripsAndReplayIsDeterministic()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.LatePush = true;
            scenario.Economy.LatePushAfterTicks = 12000;
            scenario.Economy.LatePushAdvantageAfterTicks = 9000;
            scenario.Economy.LatePushEnemyMultiplierPermille = 1500;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.LatePush, Is.True);
            Assert.That(decoded.Economy.LatePushAfterTicks, Is.EqualTo(12000));
            Assert.That(decoded.Economy.LatePushAdvantageAfterTicks, Is.EqualTo(9000));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var left = new Battle(decoded); var right = new Battle(ScenarioBinary.Decode(bytes));
            for (long tick = 1; tick <= 20000; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        private static FactionObservation Observation(long tick, int own, int enemy, int ownedOutposts)
        {
            var armies = Enumerable.Range(0, own == 0 ? 0 : 1)
                .Select(i => new OwnArmyView((uint)(i + 1), 1, UnitKind.Infantry, default, own, default)).ToArray();
            var visible = Enumerable.Range(0, enemy).Select(i => new VisibleEnemy((uint)(i + 1), default, (byte)UnitKind.Infantry)).ToArray();
            var objectives = Enumerable.Range(0, 2).Select(i => new KnownObjective(GoalKind.Outpost, (uint)(i + 1), default,
                true, i < ownedOutposts ? 1U : 2U, false, 0, tick)).ToArray();
            return new FactionObservation(1, tick, armies, visible, Array.Empty<EnemyContact>(), objectives);
        }
    }
}
