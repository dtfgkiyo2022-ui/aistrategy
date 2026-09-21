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
    /// Ver.2: with an external model behind the provider, how often the gateway asks is the cost. The default stays
    /// the Ver.1 cadence, because every recorded baseline was made with it; the trigger-based schedule is opt-in.
    /// </summary>
    public sealed class AutonomousPollScheduleTests
    {
        /// <summary>
        /// Counts what was asked for and answers straight away with no orders, so nothing reaches the simulation but
        /// the request does finish. A provider that never answers would be re-asked every deadline, which would hide
        /// the schedule behind the deadline.
        /// </summary>
        private sealed class CountingProvider : IPolicyProvider
        {
            internal readonly List<long> AskedTicks = new List<long>();
            private readonly List<ulong> open = new List<ulong>();
            public void Request(PolicyRequest request) { AskedTicks.Add(request.StartedTick); open.Add(request.RequestId); }
            public IReadOnlyList<PolicyReply> Poll(long tick)
            {
                var replies = open.OrderBy(id => id).Select(id => new PolicyReply(id, tick, Array.Empty<PolicyOrder>(), ReasonCode.None)).ToArray();
                open.Clear();
                return replies;
            }
        }

        private static UserPolicyIntent Intent(uint faction) =>
            new UserPolicyIntent(0, new ScopeKey(faction, ScopeKind.All, 0), PolicyKind.MaintainReserve, default(PolicyGoal),
                50, new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 100, new Expiration(long.MaxValue, 0, ExpireFlags.None));

        private static CountingProvider Run(AutonomousPollSchedule schedule, long ticks, uint factions = 1)
        {
            var provider = new CountingProvider();
            var sim = new Rts.Simulation.Simulation(WeekTwoScenario.Create());
            var gateway = new CommandGateway(sim, provider, null, schedule);
            for (uint f = 1; f <= factions; f++) gateway.EnableAutonomous(Intent(f));
            for (long i = 0; i < ticks && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
            return provider;
        }

        [Test]
        public void TheDefaultIsUnchangedVerOneCadence()
        {
            var gateway = new CommandGateway(new Rts.Simulation.Simulation(WeekTwoScenario.Create()));
            Assert.That(gateway.PollSchedule, Is.SameAs(AutonomousPollSchedule.EveryCycle));
            Assert.That(gateway.PollSchedule.AsksEveryCycle, Is.True);
            // 600 ticks with nothing outstanding is one ask per allocation cycle, starting at tick 0.
            var provider = Run(AutonomousPollSchedule.EveryCycle, 600);
            Assert.That(provider.AskedTicks.Count, Is.EqualTo(30));
            Assert.That(provider.AskedTicks.Take(3), Is.EqualTo(new long[] { 0, 20, 40 }));
        }

        [Test]
        public void OnChangeAsksFarLessOftenOverTheSameMatch()
        {
            var every = Run(AutonomousPollSchedule.EveryCycle, 3000);
            var onChange = Run(AutonomousPollSchedule.OnChange(600), 3000);
            Assert.That(onChange.AskedTicks.Count, Is.LessThan(every.AskedTicks.Count / 4),
                "the whole point is that a call costs money");
            Assert.That(onChange.AskedTicks.First(), Is.EqualTo(0), "the match still opens with one ask");
        }

        [Test]
        public void AQuietStretchIsStillLookedAtOnceEveryHeartbeat()
        {
            var provider = Run(AutonomousPollSchedule.OnChange(600), 1200);
            // Before any contact nothing changes, so only the opening ask and the heartbeats are left.
            var gaps = provider.AskedTicks.Zip(provider.AskedTicks.Skip(1), (a, b) => b - a).ToArray();
            Assert.That(provider.AskedTicks, Is.Not.Empty);
            Assert.That(gaps, Is.All.LessThanOrEqualTo(600));
        }

        [Test]
        public void EveryAskLandsOnAnAllocationCycle()
        {
            foreach (var schedule in new[] { AutonomousPollSchedule.EveryCycle, AutonomousPollSchedule.OnChange(600) })
                Assert.That(Run(schedule, 3000).AskedTicks.Select(t => t % 20), Is.All.EqualTo(0), schedule.ToString());
        }

        [Test]
        public void TheFirstEnemyContactIsAskedAboutWithoutWaitingForTheHeartbeat()
        {
            var provider = new CountingProvider();
            var sim = new Rts.Simulation.Simulation(WeekTwoScenario.Create());
            var gateway = new CommandGateway(sim, provider, null, AutonomousPollSchedule.OnChange(6000));
            gateway.EnableAutonomous(Intent(1));
            long firstContactTick = -1;
            for (long i = 0; i < 6000 && !sim.Capture(1).Result.HasEnded; i++)
            {
                gateway.Step();
                if (firstContactTick < 0 && sim.Capture(1).Observation.Contacts.Count > 0) firstContactTick = sim.Capture(1).Tick;
            }
            Assert.That(firstContactTick, Is.GreaterThan(0), "this scenario must reach contact for the test to mean anything");
            // The heartbeat is 6000 ticks, so anything after the opening ask can only be a change.
            Assert.That(provider.AskedTicks.Count, Is.GreaterThan(1));
            Assert.That(provider.AskedTicks.Skip(1).First(), Is.LessThanOrEqualTo(firstContactTick + 20));
        }

        [Test]
        public void TheScheduleChangesOnlyTheInputBookkeepingNeverTheBattle()
        {
            // Every ask that produces no order is still logged as a rejected proposal, so asking less often leaves
            // fewer inputs in the log. That is a change of canonical state: the cadence must not be changed under a
            // recorded baseline. What it must never touch is the battle - soldiers, cores, outposts, armies.
            var bookkeeping = new[] { "Inputs.Cursor", "Gateway.NextRequestId", "Gateway.NextLogIndex" };
            Dictionary<string, string> Play(AutonomousPollSchedule schedule)
            {
                var sim = new Rts.Simulation.Simulation(WeekTwoScenario.Create());
                var gateway = new CommandGateway(sim, new CountingProvider(), null, schedule);
                gateway.EnableAutonomous(Intent(1));
                for (int i = 0; i < 1000; i++) gateway.Step();
                return Rts.Replay.DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);
            }
            var every = Play(AutonomousPollSchedule.EveryCycle);
            var onChange = Play(AutonomousPollSchedule.OnChange(600));
            var differing = every.Keys.Where(k => every[k] != onChange[k]).ToArray();
            Assert.That(every.Count, Is.GreaterThan(1000), "this only means something if the whole state is compared");
            Assert.That(differing, Is.EquivalentTo(bookkeeping));
        }

        [Test]
        public void TheSameScheduleTwiceGivesTheSameAskTicks()
        {
            Assert.That(Run(AutonomousPollSchedule.OnChange(600), 3000, 2).AskedTicks,
                Is.EqualTo(Run(AutonomousPollSchedule.OnChange(600), 3000, 2).AskedTicks));
        }

        [Test]
        public void AHeartbeatShorterThanOneAllocationCycleIsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => AutonomousPollSchedule.OnChange(19));
            Assert.That(AutonomousPollSchedule.OnChange(20).MaxIntervalTicks, Is.EqualTo(20));
        }

        [Test]
        public void BothFactionsAreWatchedSeparately()
        {
            var provider = Run(AutonomousPollSchedule.OnChange(600), 2000, 2);
            Assert.That(provider.AskedTicks.Count(t => t == 0), Is.EqualTo(2), "each registered target opens with its own ask");
        }
    }
}
