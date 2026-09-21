using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rts.Contracts;

namespace Rts.Providers
{
    /// <summary>One question set's answers. Null / missing means "no answer": the game then issues no order.</summary>
    public sealed class JevAnswers
    {
        /// <summary>"north", "south" or "core"; null when Jev did not choose.</summary>
        public string Focus;
        public double FocusConfidence;
        /// <summary>Probability that the army should retreat, when the noul question was answered.</summary>
        public double? RetreatProbability;
    }

    /// <summary>The network side. Real HTTP lives behind this so tests and replays never touch the network.</summary>
    public interface IJevTransport
    {
        Task<JevAnswers> AskAsync(string stateJson, CancellationToken cancel);
    }

    /// <summary>Game-side decision table (design sketch section 4). The thresholds are calibrated, not guessed.</summary>
    public sealed class JevThresholds
    {
        public double MinFocusConfidence = 0.7;
        public double RetreatProbability = 0.7;
    }

    /// <summary>
    /// A policy provider backed by an external judgement model. It lives outside the deterministic assemblies: it
    /// uses tasks and queues, but it never waits, never reads a clock, and only hands back finished answers in
    /// RequestId order. What the model said never enters the replay; the gateway records the resulting orders.
    /// </summary>
    public sealed class JevPolicyProvider : IPolicyProvider, IDisposable
    {
        private sealed class Completed
        {
            internal ulong RequestId;
            internal JevAnswers Answers; // null when the call failed
        }

        private readonly IJevTransport transport;
        private readonly JevThresholds thresholds;
        private readonly ConcurrentQueue<Completed> done = new ConcurrentQueue<Completed>();
        private readonly Dictionary<ulong, PolicyRequest> open = new Dictionary<ulong, PolicyRequest>();
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();
        private int failures;

        public JevPolicyProvider(IJevTransport transport, JevThresholds thresholds = null)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.thresholds = thresholds ?? new JevThresholds();
        }

        /// <summary>Calls that failed or came back unusable since the start; for the "AI suggestions unavailable" display.</summary>
        public int FailureCount => Volatile.Read(ref failures);

        public void Request(PolicyRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            // The state is written out now, on the caller's thread, so nothing the simulation mutates later can leak in.
            string state = JevState.Build(request.Observation);
            open[request.RequestId] = request;
            ulong id = request.RequestId;
            Task<JevAnswers> call;
            try { call = Task.Run(() => transport.AskAsync(state, cancel.Token)); }
            catch (Exception) { Fail(id); return; }
            call.ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result != null) done.Enqueue(new Completed { RequestId = id, Answers = t.Result });
                else Fail(id);
            }, TaskScheduler.Default);
        }

        private void Fail(ulong id)
        {
            Interlocked.Increment(ref failures);
            done.Enqueue(new Completed { RequestId = id });
        }

        public IReadOnlyList<PolicyReply> Poll(long tick)
        {
            var finished = new List<Completed>();
            while (done.TryDequeue(out var c)) finished.Add(c);
            // Completion order is decided by the network. The gateway consumes command ids in the order replies arrive, and
            // command ids are part of the canonical state, so the order handed back must be RequestId, never arrival.
            finished.Sort((a, b) => a.RequestId.CompareTo(b.RequestId));
            var replies = new List<PolicyReply>();
            foreach (var c in finished)
            {
                if (!open.TryGetValue(c.RequestId, out var request)) continue;
                open.Remove(c.RequestId);
                var orders = c.Answers == null ? new List<PolicyOrder>() : Decide(request, c.Answers);
                if (c.Answers != null && orders.Count == 0) Interlocked.Increment(ref failures);
                // No usable answer is an empty reply: the gateway logs it as a rejection and the automatic AI carries on.
                replies.Add(new PolicyReply(c.RequestId, tick, orders, ReasonCode.None));
            }
            return replies;
        }

        private List<PolicyOrder> Decide(PolicyRequest request, JevAnswers answers)
        {
            var orders = new List<PolicyOrder>();
            if (answers.RetreatProbability.HasValue && answers.RetreatProbability.Value >= thresholds.RetreatProbability)
            {
                orders.Add(Order(request, PolicyKind.Retreat, default(PolicyGoal)));
                return orders;
            }
            if (answers.Focus != null && answers.FocusConfidence >= thresholds.MinFocusConfidence)
            {
                var goal = FocusGoal(request.Observation, answers.Focus);
                if (goal.HasValue) orders.Add(Order(request, PolicyKind.Focus, goal.Value));
            }
            return orders;
        }

        private static PolicyGoal? FocusGoal(FactionObservation o, string choice)
        {
            switch (choice)
            {
                case "north": return new PolicyGoal(GoalKind.Outpost, 1, default(SimPoint));
                case "south": return new PolicyGoal(GoalKind.Outpost, 2, default(SimPoint));
                case "core":
                    foreach (var obj in o.Objectives)
                        if (obj.Kind == GoalKind.Core && obj.OwnerFactionId != o.FactionId)
                            return new PolicyGoal(GoalKind.Core, obj.Id, default(SimPoint));
                    return null; // the enemy core has not been seen, so it cannot be named
                default: return null; // an unknown choice is not guessed at
            }
        }

        private static PolicyOrder Order(PolicyRequest r, PolicyKind kind, PolicyGoal goal) =>
            new PolicyOrder(0, 0, CommandSource.Ai, r.Scope, kind, goal, 50, new LossBudget(300),
                new EndCondition(EndKind.UntilReplaced, 0), 0, 0, Array.Empty<PolicyVersion>(), r.StartedTick,
                new Expiration(long.MaxValue, 0, ExpireFlags.None));

        public void Dispose() { cancel.Cancel(); cancel.Dispose(); }
    }
}
