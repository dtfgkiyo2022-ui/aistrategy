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
    public sealed class InterpretationReservationGatewayTests
    {
        private static BuildIdentity Build() => new BuildIdentity
        {
            Commit = "test", SourceHash = new string('a', 64), Backend = "test"
        };

        private static PolicyOrder AiOrder(Battle simulation, ScopeKey scope) => new PolicyOrder(
            0, 0, CommandSource.Ai, scope, PolicyKind.Retreat, default(PolicyGoal), 50, new LossBudget(300),
            new EndCondition(EndKind.UntilReplaced, 0), 0, simulation.Revision(scope),
            simulation.Versions(scope).Where(v => !v.Scope.Equals(scope)).ToArray(), 0,
            new Expiration(long.MaxValue, 240, ExpireFlags.ObservationTooOld));

        [Test]
        public void FixedReservationDropsOnlyOverlappingAutonomousProposal()
        {
            var simulation = new Battle(WeekTwoScenario.Create());
            var gateway = new CommandGateway(simulation);
            ulong reservation = gateway.BeginInterpretation(1, new ScopeKey(1, ScopeKind.Army, 1));
            Assert.That(gateway.Propose(1, 1, new[] { AiOrder(simulation, new ScopeKey(1, ScopeKind.Army, 1)) }, 1), Is.Zero);
            Assert.That(gateway.Propose(1, 2, new[] { AiOrder(simulation, new ScopeKey(1, ScopeKind.Army, 2)) }, 1), Is.Not.Zero);
            Assert.That(gateway.InterpretationRejections.Single().Reason, Is.EqualTo("人の解釈中"));
            gateway.EndInterpretation(reservation);
            Assert.That(gateway.Propose(1, 3, new[] { AiOrder(simulation, new ScopeKey(1, ScopeKind.Army, 1)) }, 1), Is.Not.Zero);
        }

        [Test]
        public void TargetlessReservationDropsEveryProposalFromThatFaction()
        {
            var simulation = new Battle(WeekTwoScenario.Create());
            var gateway = new CommandGateway(simulation);
            ulong reservation = gateway.BeginInterpretation(1);
            Assert.That(gateway.Propose(1, 1, new[] { AiOrder(simulation, new ScopeKey(1, ScopeKind.Army, 1)) }, 1), Is.Zero);
            Assert.That(gateway.Propose(1, 2, new[] { AiOrder(simulation, new ScopeKey(1, ScopeKind.Army, 2)) }, 1), Is.Zero);
            uint otherArmy = simulation.Capture(2).Observation.OwnArmies[0].Id;
            Assert.That(gateway.Propose(2, 3, new[] { AiOrder(simulation, new ScopeKey(2, ScopeKind.Army, otherArmy)) }, 1), Is.Not.Zero);
            gateway.EndInterpretation(reservation);
        }

        [Test]
        public void ReservationIsNotRecordedAndReplayStillMatches()
        {
            var scenario = WeekTwoScenario.Create();
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            ulong reservation = gateway.BeginInterpretation(1);
            gateway.EndInterpretation(reservation);
            gateway.Submit(new UserPolicyIntent(1, new ScopeKey(1, ScopeKind.Army, 1), PolicyKind.Retreat,
                default(PolicyGoal), 50, new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 0,
                new Expiration(long.MaxValue, 0, ExpireFlags.None)));
            var inputs = gateway.Step().ToArray();
            Assert.That(gateway.Inputs.Any(i => i.Kind == InputKind.Reserve || i.Kind == InputKind.Resolve), Is.True);
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, inputs, 3, Build());
                stream.Position = 0;
                Assert.That(ReplayRunner.Replay(stream, Build()).FirstMismatchTick, Is.Null);
            }
        }
    }
}
