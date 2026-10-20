using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Simulation;
using Rts.UnityHost;

namespace Rts.Core.Tests
{
    [TestFixture]
    public sealed class SimulationThreadingTests
    {
        private static UserPolicyIntent Intent(uint faction, ulong sequence = 1) =>
            new UserPolicyIntent(sequence, new ScopeKey(faction, ScopeKind.All, 0), PolicyKind.MaintainReserve,
                default(PolicyGoal), 50, new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 100,
                new Expiration(long.MaxValue, 0, ExpireFlags.None));

        [Test]
        public void PublicationKeepsPreviousAndCurrentTogether()
        {
            var simulation = new Rts.Simulation.Simulation(WeekTwoScenario.Create());
            var store = new PublishedMatchFrameStore();
            store.Publish(7, simulation.Capture(1), simulation.Capture(2), null, default(SimulationThreadPerf));
            var first = store.Read();
            simulation.Step(1, Array.Empty<ScheduledInput>());
            store.Publish(7, simulation.Capture(1), simulation.Capture(2), null, default(SimulationThreadPerf));

            var second = store.Read();
            Assert.That(second.Generation, Is.EqualTo(7));
            Assert.That(second.PreviousFaction1.Tick, Is.EqualTo(first.CurrentFaction1.Tick));
            Assert.That(second.CurrentFaction1.Tick, Is.EqualTo(1));
            Assert.That(second.PreviousFaction2.Tick, Is.EqualTo(first.CurrentFaction2.Tick));
        }

        [Test]
        public void QueuedSubmitAndCancelUseTheGatewayIdOnTheWorkerInOrder()
        {
            var commands = new SimulationCommandPort(0);
            var receipt = commands.Submit(Intent(1));
            commands.Cancel(receipt);
            var store = new PublishedMatchFrameStore();
            var events = new SimulationEventQueue();
            using (var worker = new SimulationThreadWorker(() =>
            {
                var simulation = new Rts.Simulation.Simulation(WeekTwoScenario.Create());
                return new SimulationThreadMatch(simulation, new CommandGateway(simulation));
            }, commands, store, events, 11, TimeSpan.FromMilliseconds(1)))
            {
                worker.Start();
                var watch = Stopwatch.StartNew();
                while (!commands.TryGetGatewayId(receipt, out _) && watch.ElapsedMilliseconds < 3000)
                    Thread.Sleep(1);
                Assert.That(commands.TryGetGatewayId(receipt, out var gatewayId), Is.True);
                Assert.That(gatewayId, Is.GreaterThan(0));
                Assert.That(worker.StopAndJoin(TimeSpan.FromSeconds(3)), Is.True);
                Assert.That(worker.Failure, Is.Null);
            }
        }

        [Test]
        public void StopIsAJoinAndOldGenerationCanBeDiscarded()
        {
            var commands = new SimulationCommandPort(0);
            var store = new PublishedMatchFrameStore();
            var events = new SimulationEventQueue();
            using (var worker = new SimulationThreadWorker(() =>
            {
                var simulation = new Rts.Simulation.Simulation(WeekTwoScenario.Create());
                return new SimulationThreadMatch(simulation, new CommandGateway(simulation));
            }, commands, store, events, 22, TimeSpan.FromMilliseconds(1)))
            {
                worker.Start();
                var watch = Stopwatch.StartNew();
                while ((store.Read() == null || store.Read().Tick == 0) && watch.ElapsedMilliseconds < 3000)
                    Thread.Sleep(1);
                Assert.That(worker.StopAndJoin(TimeSpan.FromSeconds(3)), Is.True);
                Assert.That(worker.IsRunning, Is.False);
                Assert.That(store.Read().Generation, Is.EqualTo(22));
            }
        }

        [Test]
        public void WorkerPathKeepsCanonicalStateDeterministic()
        {
            var commands = new SimulationCommandPort(0);
            var store = new PublishedMatchFrameStore();
            var events = new SimulationEventQueue();
            var workerStates = new List<byte[]>();
            using (var worker = new SimulationThreadWorker(() =>
            {
                var simulation = new Rts.Simulation.Simulation(WeekTwoScenario.Create());
                return new SimulationThreadMatch(simulation, new CommandGateway(simulation));
            }, commands, store, events, 31, TimeSpan.FromMilliseconds(1))
            { TickObserver = (tick, state) => workerStates.Add(state.ToArray()) })
            {
                worker.Start();
                var watch = Stopwatch.StartNew();
                while ((store.Read() == null || store.Read().Tick < 8) && watch.ElapsedMilliseconds < 3000)
                    Thread.Sleep(1);
                Assert.That(store.Read(), Is.Not.Null);
                Assert.That(store.Read().Tick, Is.GreaterThanOrEqualTo(8));
                Assert.That(worker.StopAndJoin(TimeSpan.FromSeconds(3)), Is.True);
                Assert.That(worker.Failure, Is.Null);
                var workerState = worker.FinalCanonicalState.ToArray();
                var workerTick = store.Read().Tick;
                var replay = new Rts.Simulation.Simulation(WeekTwoScenario.Create());
                var replayGateway = new CommandGateway(replay);
                Assert.That(workerStates.Count, Is.EqualTo(workerTick + 1));
                for (long tick = 0; tick < workerTick; tick++)
                {
                    replayGateway.Step();
                    Assert.That(workerStates[(int)tick + 1], Is.EqualTo(replay.CaptureDiagnostic().CanonicalState.ToArray()));
                }
                Assert.That(workerState, Is.EqualTo(replay.CaptureDiagnostic().CanonicalState.ToArray()));
            }
        }
    }
}
