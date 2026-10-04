using System;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Providers;
using Rts.Simulation;
using Rts.UnityHost;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Tests.EditMode
{
    public sealed class InterpretationReservationTests
    {
        private static PolicyOrder AiOrder(Battle simulation, ScopeKey scope)
        {
            return new PolicyOrder(0, 0, CommandSource.Ai, scope, PolicyKind.Retreat, default(PolicyGoal), 50,
                new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 0, simulation.Revision(scope),
                simulation.Versions(scope).Where(v => !v.Scope.Equals(scope)).ToArray(), 0,
                new Expiration(long.MaxValue, 240, ExpireFlags.ObservationTooOld));
        }

        [Test]
        public void OpenInterpretationFiltersOnlyOverlappingAutonomousProposalsAndEndReleasesThem()
        {
            var simulation = new Battle(WeekTwoScenario.Create());
            var gateway = new CommandGateway(simulation);
            var north = new ScopeKey(1, ScopeKind.Army, 1);
            var south = new ScopeKey(1, ScopeKind.Army, 2);
            ulong reservation = gateway.BeginInterpretation(1, north);

            Assert.That(gateway.Propose(1, 1, new[] { AiOrder(simulation, north) }, 1), Is.Zero);
            Assert.That(gateway.Propose(1, 2, new[] { AiOrder(simulation, south) }, 1), Is.Not.Zero);
            Assert.That(gateway.InterpretationRejections.Single().Reason, Is.EqualTo("人の解釈中"));

            gateway.EndInterpretation(reservation);
            Assert.That(gateway.Propose(1, 3, new[] { AiOrder(simulation, north) }, 1), Is.Not.Zero);
        }

        [Test]
        public void TargetlessInterpretationFiltersTheWholeFactionButNotTheOtherFaction()
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
        public void FakeInterpreterSubmitsAHumanOrderAndClosesTheReservation()
        {
            var simulation = new Battle(WeekTwoScenario.Create());
            var gateway = new CommandGateway(simulation);
            var frame = simulation.Capture(1);
            var target = new ScopeKey(1, ScopeKind.Army, frame.Observation.OwnArmies[0].Id);
            var goal = AiSituationSummary.From(frame).NameTable.First(e => e.HasGoal && e.IsOwn);
            string answer = "{\"say\":\"防衛します\",\"commands\":[{\"type\":\"policy\",\"kind\":\"Defend\",\"goal\":\"" + goal.Name + "\"}]}";
            var fake = new FakeCommandInterpreter(0, _ => answer);
            var port = new LiveAiCommandPort(gateway, fake, () => simulation.Capture(1));

            ulong request = port.BeginInterpretation("守れ", target, "gpt-6-luna");
            Assert.That(port.Instructions.Single(i => i.RequestId == request).State, Is.EqualTo(AiInstructionState.Interpreting));
            port.Poll(0);

            var status = port.Instructions.Single(i => i.RequestId == request);
            Assert.That(status.State, Is.EqualTo(AiInstructionState.Executing));
            Assert.That(gateway.IsInterpretationOpen(status.ReservationId), Is.False);
            Assert.That(status.Say, Is.EqualTo("防衛します"));
            Assert.That(status.ActualCostYen, Is.GreaterThanOrEqualTo(0m));
            port.Dispose();
        }
    }
}
