using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Net.Http;
using System.Threading.Tasks;
using Rts.Contracts;

namespace Rts.Providers
{
    /// <summary>
    /// A gateway answer that was not a success, carrying its status. HttpRequestException only carries one from .NET 5,
    /// and Unity's runtime does not have that, so the status travels on this instead.
    /// </summary>
    public sealed class JevHttpException : Exception
    {
        public JevHttpException(int statusCode, string message) : base(message) { StatusCode = statusCode; }
        public int StatusCode { get; }
    }

    /// <summary>
    /// Short reasons a call produced nothing, for the diagnostic log. They are deliberately coarse and fixed: a raw
    /// exception message could repeat the request, and the key must never reach a log.
    /// </summary>
    public static class JevFailure
    {
        public const string Paused = "paused";
        public const string Timeout = "timeout";
        public const string NoKey = "no-key";
        public const string BadReply = "bad-reply";
        public const string Unknown = "unknown";

        public static string Describe(Exception e)
        {
            while (e is AggregateException aggregate && aggregate.InnerException != null) e = aggregate.InnerException;
            switch (e)
            {
                case null: return Unknown;
                case TaskCanceledException _:
                case OperationCanceledException _: return Timeout;
                case InvalidOperationException _: return NoKey;
                case FormatException _: return BadReply;
                case JevHttpException gateway: return "http-" + gateway.StatusCode;
                case HttpRequestException _: return "http";
                default: return Unknown;
            }
        }
    }

    /// <summary>The game's names for the choices, so the transport and the order table cannot drift apart.</summary>
    public static class JevChoice
    {
        public const string NorthOutpost = "north-outpost";
        public const string SouthOutpost = "south-outpost";
        public const string MyCore = "my-core";
        public const string EnemyCore = "enemy-core";
    }

    /// <summary>One question set's answers. Null / missing means "no answer": the game then issues no order.</summary>
    public sealed class JevAnswers
    {
        /// <summary>A <see cref="JevChoice"/> value, or null when the model did not choose one of them.</summary>
        public string Choice;
        public double ChoiceConfidence;
        /// <summary>Each factual statement's probability, by <see cref="JevFacts"/> name. Missing means unanswered.</summary>
        public Dictionary<string, double> Facts = new Dictionary<string, double>();
    }

    /// <summary>The network side. Real HTTP lives behind this so tests and replays never touch the network.</summary>
    public interface IJevTransport
    {
        Task<JevAnswers> AskAsync(string stateJson, CancellationToken cancel);
    }

    /// <summary>Game-side decision table (design sketch section 4). The thresholds are calibrated, not guessed.</summary>
    public sealed class JevThresholds
    {
        public double MinChoiceConfidence = 0.7;

        /// <summary>
        /// How many of the last factual answers must be right before an order is issued at all, per thousand. The
        /// statements are all things this code works out for itself, so getting them wrong means the model is not
        /// reading the state - and then its one real judgement, where the match is being decided, is worth nothing.
        /// 0 never gates.
        /// </summary>
        public int MinComprehensionPermille = 700;

        /// <summary>How many recent factual answers the check looks at, and the fewest it will judge on.</summary>
        public int ComprehensionWindow = 12;
        public int ComprehensionMinimumAnswers = 6;

        /// <summary>
        /// How sure a factual statement has to be before the order table treats it as so. Measured answers to this
        /// shape of question sat at 0.98 when true and 0.04 when false, so anything in the middle is unusual and is
        /// treated as "not so".
        /// </summary>
        public double MinFactProbability = 0.7;
        /// <summary>Consecutive failures that stop the calls; 0 never stops them.</summary>
        public int FailuresBeforeStopping = 3;
        /// <summary>How long the calls stay stopped, in ticks. 600 = 30 seconds at 20 Hz.</summary>
        public long StoppedTicks = 600;

        /// <summary>
        /// An order the same as the one just issued is not issued again until this many ticks have passed. Repeating it
        /// changes nothing in the match - the order runs until replaced - but each repeat costs a command id and a
        /// supersede in the log. It is a delay rather than a ban because the provider is not told whether the gateway
        /// accepted the order, so a suppressed order must eventually be tried again. 1200 = one minute at 20 Hz.
        /// 0 never suppresses.
        /// </summary>
        public long RepeatSameOrderAfterTicks = 1200;
    }

    /// <summary>
    /// One answer, for the diagnostic log only. The model's own words never enter the replay: what the match records
    /// is the order the game derived, if any.
    /// </summary>
    public sealed class JevAnswerRecord
    {
        public ulong RequestId;
        public long Tick;
        /// <summary>False when the call did not come back at all.</summary>
        public bool Answered;
        /// <summary>Why it did not, when it did not: a <see cref="JevFailure"/> value. Never carries the key.</summary>
        public string Failure;
        public string Choice;
        public double ChoiceConfidence;
        /// <summary>What was answered for each statement, and what the statement actually was.</summary>
        public List<JevFactAnswer> Facts = new List<JevFactAnswer>();
        public int OrderCount;
        /// <summary>True when an order was derived but not issued, because it repeated the one already in force.</summary>
        public bool Suppressed;
        /// <summary>False when the recent factual answers were too often wrong, so no order was derived at all.</summary>
        public bool Understood = true;
    }

    /// <summary>One statement: what the model said, and what it actually was.</summary>
    public sealed class JevFactAnswer
    {
        public string Name;
        /// <summary>-1 when the model did not answer this one.</summary>
        public double Probability = -1;
        public bool Truth;
    }

    /// <summary>What the display shows about the external AI.</summary>
    public enum JevAvailability
    {
        /// <summary>Calls are being made.</summary>
        Calling = 0,
        /// <summary>Too many failed in a row, so calls are paused; the automatic AI is running the match alone.</summary>
        Paused = 1
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
            internal string Failure;     // why, when Answers is null
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

        /// <summary>Calls that did not come back at all; for the "AI suggestions unavailable" display.</summary>
        public int FailureCount => Volatile.Read(ref failures);

        /// <summary>
        /// Calls the model answered but that produced no order, because it was not confident enough or named something
        /// that cannot be turned into an order. This is the thresholds working, not the gateway being down, so it is
        /// counted apart from <see cref="FailureCount"/>.
        /// </summary>
        public int DeclinedCount { get; private set; }

        /// <summary>Answers that would have repeated the order already in force, and so were not turned into one.</summary>
        public int SuppressedCount { get; private set; }

        /// <summary>Replies held back because the recent factual answers were too often wrong.</summary>
        public int NotUnderstoodCount { get; private set; }

        /// <summary>Right answers, and answers, among the statements in the current window.</summary>
        public int ComprehensionCorrect { get; private set; }
        public int ComprehensionAnswered { get; private set; }

        // Whether each of the last few factual answers was right. Oldest first.
        private readonly Queue<bool> comprehension = new Queue<bool>();

        /// <summary>
        /// Receives what the model answered, for the diagnostic log. The design keeps raw answers out of the replay, so
        /// nothing here is fed back into the match. Called on the game thread from Poll.
        /// </summary>
        public Action<JevAnswerRecord> Observe { get; set; }

        /// <summary>Whether calls are being made right now. Read on the game thread, after Request/Poll.</summary>
        public JevAvailability Availability { get; private set; } = JevAvailability.Calling;

        /// <summary>The tick calls resume at while <see cref="Availability"/> is Paused; 0 when they are not paused.</summary>
        public long ResumeTick { get; private set; }

        // Failures in a row on the game thread. A single good answer clears it: the gateway is what decides whether the
        // orders are useful, and a model that answers at all is not the failure this is guarding against.
        private int consecutiveFailures;

        public void Request(PolicyRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (Availability == JevAvailability.Paused)
            {
                if (request.StartedTick < ResumeTick)
                {
                    // Still paused: answer straight away with nothing, so the gateway records a rejection at the usual
                    // place and the match is never left waiting on a request that was never sent.
                    done.Enqueue(new Completed { RequestId = request.RequestId, Failure = JevFailure.Paused });
                    open[request.RequestId] = request;
                    return;
                }
                Availability = JevAvailability.Calling;
                ResumeTick = 0;
                consecutiveFailures = 0;
            }
            // The state is written out now, on the caller's thread, so nothing the simulation mutates later can leak in.
            string state = JevState.Build(request.Observation);
            open[request.RequestId] = request;
            ulong id = request.RequestId;
            Task<JevAnswers> call;
            try { call = Task.Run(() => transport.AskAsync(state, cancel.Token)); }
            catch (Exception e) { Fail(id, JevFailure.Describe(e)); return; }
            call.ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result != null) done.Enqueue(new Completed { RequestId = id, Answers = t.Result });
                else Fail(id, JevFailure.Describe(t.Exception));
            }, TaskScheduler.Default);
        }

        private void Fail(ulong id, string reason)
        {
            Interlocked.Increment(ref failures);
            done.Enqueue(new Completed { RequestId = id, Failure = reason });
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
                var facts = FactAnswers(c.Answers, request.Observation);
                bool understood = Score(facts);
                bool suppressed = false;
                var orders = c.Answers == null || !understood ? new List<PolicyOrder>() : Decide(request, c.Answers, tick, out suppressed);
                if (c.Answers != null && !understood) NotUnderstoodCount++;
                if (c.Answers != null && understood && orders.Count == 0 && !suppressed) DeclinedCount++;
                if (suppressed) SuppressedCount++;
                // A request short-circuited by the pause never reached the gateway, so it says nothing about whether
                // the gateway is back; only a real attempt moves the run of failures.
                if (c.Answers == null && c.Failure != JevFailure.Paused) consecutiveFailures++;
                else if (c.Answers != null) consecutiveFailures = 0;
                Observe?.Invoke(new JevAnswerRecord
                {
                    RequestId = c.RequestId,
                    Tick = tick,
                    Answered = c.Answers != null,
                    Failure = c.Failure,
                    Choice = c.Answers?.Choice,
                    ChoiceConfidence = c.Answers == null ? 0 : c.Answers.ChoiceConfidence,
                    Facts = facts,
                    Understood = understood,
                    OrderCount = orders.Count,
                    Suppressed = suppressed
                });
                // No usable answer is an empty reply: the gateway logs it as a rejection and the automatic AI carries on.
                replies.Add(new PolicyReply(c.RequestId, tick, orders, ReasonCode.None));
            }
            if (Availability == JevAvailability.Calling && thresholds.FailuresBeforeStopping > 0
                && consecutiveFailures >= thresholds.FailuresBeforeStopping)
            {
                // The tick is the game's clock, so a paused window is the same length however slow the machine is.
                Availability = JevAvailability.Paused;
                ResumeTick = checked(tick + thresholds.StoppedTicks);
            }
            return replies;
        }

        /// <summary>
        /// The answer table (design sketch section 4). One order per reply at most, and only from the choice question:
        /// the noul answer is recorded and scored, not acted on (see JevQuestions).
        /// </summary>
        private List<PolicyOrder> Decide(PolicyRequest request, JevAnswers answers, long tick, out bool suppressed)
        {
            suppressed = false;
            var orders = new List<PolicyOrder>();
            if (answers.Choice != null && answers.ChoiceConfidence >= thresholds.MinChoiceConfidence)
            {
                var observation = request.Observation;
                // The choice says where; the statements say whether this faction can afford to push there. Attacking
                // while the enemy is the larger force is the one combination worth ruling out, so a place that is not
                // already mine is only attacked when the model says we outnumber them.
                bool strong = Believes(answers, JevFacts.Outnumbering, thresholds.MinFactProbability);
                switch (answers.Choice)
                {
                    case JevChoice.NorthOutpost:
                    case JevChoice.SouthOutpost:
                    {
                        // The state names north and south by position, so the order has to resolve them the same way.
                        uint outpost = JevState.OutpostId(observation, answers.Choice == JevChoice.NorthOutpost);
                        if (outpost == 0) break;
                        bool mine = observation.Objectives.Any(b => b.Kind == GoalKind.Outpost && b.Id == outpost
                            && b.IsOwnerKnown && b.OwnerFactionId == observation.FactionId);
                        // Hold what is already ours when we are the smaller force; go and take it when we are not.
                        var kind = mine || !strong ? PolicyKind.Defend : PolicyKind.Focus;
                        orders.Add(Order(request, kind, new PolicyGoal(GoalKind.Outpost, outpost, default(SimPoint))));
                        break;
                    }
                    case JevChoice.MyCore:
                        uint mineCore = CoreId(observation, own: true);
                        if (mineCore != 0) orders.Add(Order(request, PolicyKind.Defend, new PolicyGoal(GoalKind.Core, mineCore, default(SimPoint))));
                        break;
                    case JevChoice.EnemyCore:
                        uint theirs = CoreId(observation, own: false);
                        // An enemy core that has not been seen cannot be named, and charging it while outnumbered is
                        // the mistake this table exists to avoid, so both conditions have to hold.
                        if (theirs != 0 && strong) orders.Add(Order(request, PolicyKind.Focus, new PolicyGoal(GoalKind.Core, theirs, default(SimPoint))));
                        break;
                }
            }
            if (orders.Count == 1)
            {
                string key = orders[0].Kind + "/" + orders[0].Goal.Kind + ":" + orders[0].Goal.Id;
                if (thresholds.RepeatSameOrderAfterTicks > 0 && key == lastOrderKey
                    && checked(tick - lastOrderTick) < thresholds.RepeatSameOrderAfterTicks)
                {
                    suppressed = true;
                    orders.Clear();
                }
                else { lastOrderKey = key; lastOrderTick = tick; }
            }
            return orders;
        }

        // The last order actually handed back, so an answer that only repeats it can be recognised.
        private string lastOrderKey;
        private long lastOrderTick;

        /// <summary>Folds this reply's factual answers into the window and says whether the model is still reading it.</summary>
        private bool Score(List<JevFactAnswer> facts)
        {
            foreach (var f in facts)
            {
                if (f.Probability < 0) continue;
                // A half is the coarsest possible reading, and the measured answers sit at 0.04 and 0.98, so nothing
                // real is near the line.
                comprehension.Enqueue(f.Truth == f.Probability >= 0.5);
                while (comprehension.Count > Math.Max(1, thresholds.ComprehensionWindow)) comprehension.Dequeue();
            }
            ComprehensionAnswered = comprehension.Count;
            int correct = 0;
            foreach (bool right in comprehension) if (right) correct++;
            ComprehensionCorrect = correct;
            if (thresholds.MinComprehensionPermille <= 0) return true;
            // Too few answers to judge on yet: the model gets the benefit of the doubt rather than the match losing it.
            if (comprehension.Count < thresholds.ComprehensionMinimumAnswers) return true;
            return 1000 * correct / comprehension.Count >= thresholds.MinComprehensionPermille;
        }

        private static List<JevFactAnswer> FactAnswers(JevAnswers answers, FactionObservation observation)
        {
            var list = new List<JevFactAnswer>();
            // Walked in a fixed order so the diagnostic log reads the same way every time.
            foreach (string name in JevFacts.All)
                list.Add(new JevFactAnswer
                {
                    Name = name,
                    Probability = answers != null && answers.Facts.TryGetValue(name, out var p) ? p : -1,
                    Truth = JevFacts.Truth(name, observation)
                });
            return list;
        }

        private static bool Believes(JevAnswers answers, string fact, double threshold) =>
            answers.Facts.TryGetValue(fact, out var p) && p >= threshold;

        private static uint CoreId(FactionObservation o, bool own)
        {
            foreach (var b in o.Objectives)
                if (b.Kind == GoalKind.Core && b.IsOwnerKnown && (b.OwnerFactionId == o.FactionId) == own) return b.Id;
            return 0;
        }

        private static PolicyOrder Order(PolicyRequest r, PolicyKind kind, PolicyGoal goal) =>
            new PolicyOrder(0, 0, CommandSource.Ai, r.Scope, kind, goal, 50, new LossBudget(300),
                new EndCondition(EndKind.UntilReplaced, 0), 0, 0, Array.Empty<PolicyVersion>(), r.StartedTick,
                new Expiration(long.MaxValue, 0, ExpireFlags.None));

        public void Dispose() { cancel.Cancel(); cancel.Dispose(); }
    }
}
