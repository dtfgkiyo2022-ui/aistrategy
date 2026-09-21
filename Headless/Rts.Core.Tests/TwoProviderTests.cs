using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Simulation;

namespace Rts.Tests.Headless
{
    /// <summary>
    /// The gateway asks a provider both for the autonomous upper policy and for the player's interpreted orders. With
    /// an external model behind it, one provider means the model would also decide what the player just asked for, so
    /// the two paths can be given separate providers.
    /// </summary>
    public sealed class TwoProviderTests
    {
        /// <summary>Answers every request at the next poll, with the orders it was given, and records what it saw.</summary>
        private sealed class NamedProvider : IPolicyProvider
        {
            internal readonly List<ulong> Asked = new List<ulong>();
            internal readonly List<PolicyKind> AskedKinds = new List<PolicyKind>();
            internal Func<PolicyRequest, PolicyOrder[]> Answer = _ => Array.Empty<PolicyOrder>();
            private readonly List<PolicyRequest> open = new List<PolicyRequest>();
            public void Request(PolicyRequest request) { Asked.Add(request.RequestId); AskedKinds.Add(request.Kind); open.Add(request); }
            public IReadOnlyList<PolicyReply> Poll(long tick)
            {
                var replies = open.OrderBy(r => r.RequestId)
                    .Select(r => new PolicyReply(r.RequestId, tick, Answer(r), ReasonCode.None)).ToArray();
                open.Clear();
                return replies;
            }
        }

        private static UserPolicyIntent Intent(PolicyKind kind) =>
            new UserPolicyIntent(0, new ScopeKey(1, ScopeKind.All, 0), kind, default(PolicyGoal),
                50, new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 100, new Expiration(long.MaxValue, 0, ExpireFlags.None));

        private static PolicyOrder[] Keep(PolicyRequest r) => new[]
        {
            new PolicyOrder(0, 0, CommandSource.Ai, r.Scope, r.Kind, r.Goal, 50, new LossBudget(300),
                new EndCondition(EndKind.UntilReplaced, 0), 100, 0, Array.Empty<PolicyVersion>(), r.StartedTick,
                new Expiration(long.MaxValue, 0, ExpireFlags.None))
        };

        [Test]
        public void ThePlayersOrderGoesToTheHumanProviderAndTheAutonomousOneToTheOther()
        {
            var human = new NamedProvider { Answer = Keep };
            var autonomous = new NamedProvider { Answer = Keep };
            var sim = new Rts.Simulation.Simulation(WeekTwoScenario.Create());
            var gateway = new CommandGateway(sim, human, null, null, autonomous);
            gateway.EnableAutonomous(Intent(PolicyKind.MaintainReserve));
            gateway.Step();                                   // the opening autonomous ask
            gateway.SubmitInterpreted(Intent(PolicyKind.Retreat));
            for (int i = 0; i < 5; i++) gateway.Step();

            Assert.That(autonomous.AskedKinds, Is.All.EqualTo(PolicyKind.MaintainReserve));
            Assert.That(human.AskedKinds, Is.EqualTo(new[] { PolicyKind.Retreat }));
            Assert.That(human.Asked.Intersect(autonomous.Asked), Is.Empty, "a request belongs to one path only");
        }

        [Test]
        public void WithOneProviderNothingChanges()
        {
            var only = new NamedProvider { Answer = Keep };
            var sim = new Rts.Simulation.Simulation(WeekTwoScenario.Create());
            var gateway = new CommandGateway(sim, only);
            gateway.EnableAutonomous(Intent(PolicyKind.MaintainReserve));
            gateway.Step();
            gateway.SubmitInterpreted(Intent(PolicyKind.Retreat));
            for (int i = 0; i < 5; i++) gateway.Step();
            Assert.That(only.AskedKinds, Does.Contain(PolicyKind.MaintainReserve).And.Contain(PolicyKind.Retreat));
        }

        [Test]
        public void TheAutonomousPathAloneStillNeedsAProvider()
        {
            var sim = new Rts.Simulation.Simulation(WeekTwoScenario.Create());
            var gateway = new CommandGateway(sim, null, null, null, new NamedProvider());
            Assert.DoesNotThrow(() => gateway.EnableAutonomous(Intent(PolicyKind.MaintainReserve)));
            Assert.Throws<InvalidOperationException>(() => new CommandGateway(sim).EnableAutonomous(Intent(PolicyKind.MaintainReserve)));
        }

        [Test]
        public void RepliesAreTakenInRequestIdOrderWhicheverProviderAnsweredFirst()
        {
            // Command ids are handed out as replies are received and are part of the canonical state, so two runs that
            // differ only in which provider happened to answer first must still reach the same state.
            string Play(bool humanFirst)
            {
                var human = new NamedProvider { Answer = Keep };
                var autonomous = new NamedProvider { Answer = Keep };
                var sim = new Rts.Simulation.Simulation(WeekTwoScenario.Create());
                var gateway = new CommandGateway(sim, humanFirst ? human : autonomous, null, null, humanFirst ? autonomous : human);
                gateway.EnableAutonomous(Intent(PolicyKind.MaintainReserve));
                for (int i = 0; i < 40; i++)
                {
                    if (i == 3) gateway.SubmitInterpreted(Intent(PolicyKind.Retreat));
                    gateway.Step();
                }
                return Convert.ToHexString(sim.CaptureDiagnostic().CanonicalState.ToArray());
            }
            Assert.That(Play(humanFirst: true), Is.EqualTo(Play(humanFirst: false)));
        }
    }
}
