using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
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
                    : Task.FromResult(Answer(JevChoice.NorthOutpost, 0.9));
            }
        }

        private static JevAnswers Answer(string choice, double confidence)
        {
            var answers = new JevAnswers { Choice = choice, ChoiceConfidence = confidence };
            answers.Facts[JevFacts.Outnumbering] = 0.95;
            return answers;
        }

        private static PolicyRequest Request(ulong id, long tick)
        {
            // Two outposts are in view so that a "north" answer has an objective to name.
            var observation = new FactionObservation(1, tick, Array.Empty<OwnArmyView>(), Array.Empty<VisibleEnemy>(),
                Array.Empty<EnemyContact>(), new[]
                {
                    new KnownObjective(GoalKind.Outpost, 1, new SimPoint(Fix64.FromInt(128), Fix64.FromInt(96)), true, 1, false, 0, 0),
                    new KnownObjective(GoalKind.Outpost, 2, new SimPoint(Fix64.FromInt(128), Fix64.FromInt(32)), true, 0, false, 0, 0)
                });
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
                Assert.That(order.Kind, Is.EqualTo(PolicyKind.Defend), "the north outpost is ours in this observation");
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
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { MinChoiceConfidence = 0.95, FailuresBeforeStopping = 2 }))
            {
                Round(provider, 1, 20);
                Round(provider, 2, 40);
                Round(provider, 3, 60);
                Assert.That(provider.Availability, Is.EqualTo(JevAvailability.Calling));
                Assert.That(transport.Calls, Is.EqualTo(3));
            }
        }

        [Test]
        public void DecliningIsCountedApartFromFailingAndIsReportedToTheDiagnosticLog()
        {
            var transport = new CountingTransport { Fail = false };
            var records = new List<JevAnswerRecord>();
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { MinChoiceConfidence = 0.95 }))
            {
                provider.Observe = records.Add;
                Round(provider, 1, 20);
                transport.Fail = true;
                Round(provider, 2, 40);
                Assert.That(provider.DeclinedCount, Is.EqualTo(1), "answered but not confident enough");
                Assert.That(provider.FailureCount, Is.EqualTo(1), "did not come back at all");
                Assert.That(records.Select(r => r.Answered), Is.EqualTo(new[] { true, false }));
                Assert.That(records[0].Choice, Is.EqualTo(JevChoice.NorthOutpost));
                Assert.That(records[0].ChoiceConfidence, Is.EqualTo(0.9).Within(1e-9));
                Assert.That(records[0].OrderCount, Is.EqualTo(0));
            }
        }

        private sealed class ThrowingTransport : IJevTransport
        {
            internal Func<Exception> Throw = () => new InvalidOperationException("offline");
            public Task<JevAnswers> AskAsync(string stateJson, CancellationToken cancel) => Task.FromException<JevAnswers>(Throw());
        }

        [TestCase("timeout")]
        [TestCase("no-key")]
        [TestCase("bad-reply")]
        [TestCase("http-429")]
        [TestCase("http-401")]
        [TestCase("unknown")]
        public void WhyACallProducedNothingIsRecorded(string expected)
        {
            const string key = "sk-secret-value-that-must-never-be-logged";
            var transport = new ThrowingTransport
            {
                Throw = () => expected switch
                {
                    "timeout" => new TaskCanceledException("The request was canceled due to timeout."),
                    "no-key" => new InvalidOperationException("No API key is set."),
                    "bad-reply" => new FormatException("The reply has no answers."),
                    "http-429" => new JevHttpException(429, "The AI gateway answered 429."),
                    "http-401" => new JevHttpException(401, "The AI gateway answered 401."),
                    _ => new NotSupportedException(key)
                }
            };
            var records = new List<JevAnswerRecord>();
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { FailuresBeforeStopping = 0 }))
            {
                provider.Observe = records.Add;
                Round(provider, 1, 20);
                Assert.That(records.Single().Failure, Is.EqualTo(expected));
                // A raw message could repeat the request or the key, so only fixed words are recorded.
                Assert.That(records.Single().Failure, Does.Not.Contain(key));
            }
        }

        [Test]
        public void ARequestSkippedWhilePausedIsMarkedAsSuchAndDoesNotCountAsAnAttempt()
        {
            var transport = new CountingTransport();
            var records = new List<JevAnswerRecord>();
            using (var provider = new JevPolicyProvider(transport, new JevThresholds { FailuresBeforeStopping = 2, StoppedTicks = 600 }))
            {
                provider.Observe = records.Add;
                Round(provider, 1, 20);
                Round(provider, 2, 40);
                Assert.That(provider.Availability, Is.EqualTo(JevAvailability.Paused));
                Round(provider, 3, 100);
                Assert.That(records.Last().Failure, Is.EqualTo("paused"));
                Assert.That(provider.FailureCount, Is.EqualTo(2), "a call that was never made is not a failed call");
                // The window must not be pushed further out by requests that never went anywhere.
                Assert.That(provider.ResumeTick, Is.EqualTo(640));
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
