using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Providers;

namespace Rts.Tests.Headless
{
    /// <summary>
    /// Design sketch section 5: after several failures in a row the provider stops calling for a while, so a gateway
    /// that is down is not called once per request for the rest of the match. The window is counted in ticks, never
    /// in wall-clock time.
    /// </summary>
    public sealed class JevPauseTests
    {
        private sealed class CountingTransport : IJevTransport
        {
            internal int Calls;
            internal bool Fail = true;
            public Task<JevAnswers> AskAsync(string stateJson, CancellationToken cancel)
            {
                Interlocked.Increment(ref Calls);
                return Fail
                    ? Task.FromException<JevAnswers>(new InvalidOperationException("offline"))
                    : Task.FromResult(new JevAnswers { Focus = "north", FocusConfidence = 0.9 });
            }
        }

        private static PolicyRequest Request(ulong id, long tick)
        {
            var observation = new FactionObservation(1, tick, Array.Empty<OwnArmyView>(), Array.Empty<VisibleEnemy>(),
                Array.Empty<EnemyContact>(), Array.Empty<KnownObjective>());
            return new PolicyRequest(id, 1, new ScopeKey(1, ScopeKind.All, 0), tick, observation,
                new[] { new PolicyVersion(new ScopeKey(1, ScopeKind.All, 0), 0) }, tick + 240, PolicyKind.MaintainReserve, default(PolicyGoal));
        }

        /// <summary>One request, then poll until its reply comes back. Returns the reply.</summary>
        private static PolicyReply Round(JevPolicyProvider provider, ulong id, long tick)
        {
            provider.Request(Request(id, tick));
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 5000)
            {
                var replies = provider.Poll(tick);
                if (replies.Count != 0) return replies.Single();
                Thread.Sleep(2);
            }
            throw new TimeoutException("No reply for request " + id);
        }

        [Test]
        public void ThreeFailuresInARowPauseTheCallsAndTheWindowIsCountedInTicks()
        {
            var transport = new CountingTransport();
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { FailuresBeforeStopping = 3, StoppedTicks = 600 }))
            {
                for (ulong i = 1; i <= 3; i++) Round(provider, i, 20L * (long)i);
                Assert.That(transport.Calls, Is.EqualTo(3));
                Assert.That(provider.Availability, Is.EqualTo(JevAvailability.Paused));
                Assert.That(provider.ResumeTick, Is.EqualTo(660), "the third reply arrived at tick 60");

                // Inside the window nothing is sent, but the request is still answered so nothing waits on it.
                var reply = Round(provider, 4, 100);
                Assert.That(transport.Calls, Is.EqualTo(3), "no call was made while paused");
                Assert.That(reply.Orders, Is.Empty);
                Assert.That(provider.Availability, Is.EqualTo(JevAvailability.Paused));
            }
        }

        [Test]
        public void CallsResumeOnceTheWindowHasPassed()
        {
            var transport = new CountingTransport();
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { FailuresBeforeStopping = 2, StoppedTicks = 600 }))
            {
                Round(provider, 1, 20);
                Round(provider, 2, 40);
                Assert.That(provider.Availability, Is.EqualTo(JevAvailability.Paused));
                Assert.That(provider.ResumeTick, Is.EqualTo(640));

                transport.Fail = false;
                var order = Round(provider, 3, 640).Orders.Single();
                Assert.That(transport.Calls, Is.EqualTo(3), "the call resumes exactly at the resume tick");
                Assert.That(order.Kind, Is.EqualTo(PolicyKind.Focus));
                Assert.That(provider.Availability, Is.EqualTo(JevAvailability.Calling));
                Assert.That(provider.ResumeTick, Is.EqualTo(0));
            }
        }

        [Test]
        public void OneGoodAnswerClearsTheRunOfFailures()
        {
            var transport = new CountingTransport();
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { FailuresBeforeStopping = 3, StoppedTicks = 600 }))
            {
                Round(provider, 1, 20);
                Round(provider, 2, 40);
                transport.Fail = false;
                Round(provider, 3, 60);
                transport.Fail = true;
                Round(provider, 4, 80);
                Round(provider, 5, 100);
                Assert.That(provider.Availability, Is.EqualTo(JevAvailability.Calling), "two failures since the good answer is not three");
                Round(provider, 6, 120);
                Assert.That(provider.Availability, Is.EqualTo(JevAvailability.Paused));
            }
        }

        [Test]
        public void AnAnswerThatYieldsNoOrderIsNotTreatedAsAFailedCall()
        {
            // The model answered; it simply was not confident. That is the thresholds working, not the gateway being down.
            var transport = new CountingTransport { Fail = false };
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { MinFocusConfidence = 0.95, FailuresBeforeStopping = 2 }))
            {
                Round(provider, 1, 20);
                Round(provider, 2, 40);
                Round(provider, 3, 60);
                Assert.That(provider.Availability, Is.EqualTo(JevAvailability.Calling));
                Assert.That(transport.Calls, Is.EqualTo(3));
            }
        }

        [Test]
        public void PausingCanBeTurnedOff()
        {
            var transport = new CountingTransport();
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { FailuresBeforeStopping = 0 }))
            {
                for (ulong i = 1; i <= 5; i++) Round(provider, i, 20L * (long)i);
                Assert.That(provider.Availability, Is.EqualTo(JevAvailability.Calling));
                Assert.That(transport.Calls, Is.EqualTo(5));
            }
        }
    }
}
