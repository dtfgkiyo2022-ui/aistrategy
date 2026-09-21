using System;
using System.Collections.Generic;
using Rts.Contracts;
using Rts.Simulation;

namespace Rts.Application
{
    /// <summary>Success predicate of chapter 11 (judgement grace). One measurement uses exactly one.</summary>
    public enum GraceCriterion : byte
    {
        /// <summary>Core defence: the measured faction's core is never destroyed within the horizon.</summary>
        CoreDefense = 1,
        /// <summary>Retreat: the designated army keeps at least 50% of its starting strength.</summary>
        Retreat = 2,
        /// <summary>Reinforcement: defenders reach the designated outpost before it falls.</summary>
        Reinforcement = 3,
        /// <summary>Diversion response: the designated outpost or the core is still held at the end.</summary>
        DiversionResponse = 4,
        /// <summary>Outpost held: the designated outpost belongs to the measured faction at the end of the run.</summary>
        OutpostHeld = 5,
        /// <summary>Core and outpost held: the core survives and the designated outpost is owned at the end.</summary>
        CoreAndOutpostHeld = 6
    }

    /// <summary>Outcome of one candidate acceptance tick. NotApplied means the order never entered the run.</summary>
    public enum GraceOutcome : byte { Failure = 0, Success = 1, Undetermined = 2, NotApplied = 3 }

    /// <summary>Grace = a last success exists; NoGrace = every applied candidate failed; Unevaluated = nothing decided.</summary>
    public enum GraceVerdict : byte { Grace = 1, NoGrace = 2, Unevaluated = 3 }

    /// <summary>Inputs of one grace measurement. Public fields follow ScenarioDefinition's authoring style.</summary>
    public sealed class GraceRequest
    {
        public ScenarioDefinition Scenario;
        /// <summary>Template orders. CommandId/TargetRevision/ObservedTick are rewritten per run.</summary>
        public IReadOnlyList<PolicyOrder> Orders = Array.Empty<PolicyOrder>();
        public GraceCriterion Criterion = GraceCriterion.CoreDefense;
        /// <summary>The faction whose success is measured.</summary>
        public uint FactionId = 1;
        /// <summary>Retreat only.</summary>
        public uint ArmyId;
        /// <summary>Reinforcement and DiversionResponse only.</summary>
        public uint OutpostId;
        /// <summary>The tick at which the triggering event was first observed. Supplied by the caller.</summary>
        public long FirstObservedTick;
        /// <summary>Upper bound of simulated ticks per run; defaults to the scenario limit when zero or negative.</summary>
        public long TickLimit;
        /// <summary>First candidate acceptance tick. The order is applied during Step(tick), so it is at least 1.</summary>
        public long MinAcceptTick = 1;
        /// <summary>Last candidate acceptance tick; when negative the tick limit is used.</summary>
        public long MaxAcceptTick = -1;
        /// <summary>Candidate stride. The specification asks for 1; larger strides are a cost escape hatch.</summary>
        public long AcceptStep = 1;
        /// <summary>Comprehension and input time added to the compared case. The specification uses 60.</summary>
        public long InputDelayTicks = 60;
        /// <summary>How many equal-width bands the swept range is split into for the success rate.</summary>
        public int RateBandCount = 10;
    }

    /// <summary>A maximal run of consecutive candidates that all succeeded, in sweep order.</summary>
    public sealed class GraceRun
    {
        public long FromTick;
        public long ToTick;
        public int Count;
    }

    /// <summary>
    /// One equal-width slice of the swept range. SuccessPermille is -1 when the band decided nothing, so an
    /// untested band is never read as a total failure.
    /// </summary>
    public sealed class GraceBand
    {
        public long FromTick;
        public long ToTick;
        public int Decided;
        public int Success;
        public int SuccessPermille = -1;
    }

    /// <summary>Result of one sweep. Candidate ticks are the operator-side acceptance ticks R, never R + delay.</summary>
    public sealed class GraceResult
    {
        public long InputDelayTicks;
        public long FirstObservedTick;
        public List<long> SuccessTicks = new List<long>();
        public List<long> FailureTicks = new List<long>();
        public List<long> UndeterminedTicks = new List<long>();
        public List<long> NotAppliedTicks = new List<long>();
        public long? LastSuccessTick;
        /// <summary>LastSuccessTick - FirstObservedTick. Null unless the verdict is Grace.</summary>
        public long? GraceTicks;
        public GraceVerdict Verdict = GraceVerdict.Unevaluated;

        // Success is not monotone in R, so the last success alone overstates the grace. The fields below describe
        // how densely the successes sit, which is what tells a late island apart from a dependable window.
        public int DecidedCount;
        public int SuccessCount;
        /// <summary>Successes per 1000 decided candidates; -1 when nothing was decided.</summary>
        public int SuccessPermille = -1;
        /// <summary>Maximal runs of consecutive successful candidates, in sweep order.</summary>
        public List<GraceRun> SuccessRuns = new List<GraceRun>();
        /// <summary>The longest such run; on a tie the earliest. Null when there is no success.</summary>
        public GraceRun LongestSuccessRun;
        public List<GraceBand> Bands = new List<GraceBand>();
        /// <summary>First band that reached 90%; null when no band does. The start of the dependable stretch.</summary>
        public long? FirstBandAtLeast900Tick;
        /// <summary>
        /// First band under 90%/50% *after* FirstBandAtLeast900Tick, that is where the dependable stretch ends.
        /// Bands before the first dependable one are skipped: a predicate whose early candidates all fail (retreat)
        /// would otherwise always report its very first band, which says nothing about when help stops arriving.
        /// Null when no band ever reached 90%, or when none falls back below.
        /// </summary>
        public long? FirstBandBelow900Tick;
        public long? FirstBandBelow500Tick;
    }

    /// <summary>Both compared cases: the order applied at R, and the order applied at R + InputDelayTicks.</summary>
    public sealed class GraceReport
    {
        public GraceCriterion Criterion;
        public uint FactionId;
        public uint ArmyId;
        public uint OutpostId;
        public long FirstObservedTick;
        public long TickLimit;
        public long MinAcceptTick;
        public long MaxAcceptTick;
        public long AcceptStep;
        public GraceResult Immediate = new GraceResult();
        public GraceResult Delayed = new GraceResult();
    }

    /// <summary>
    /// Chapter 11 judgement-grace measurement. The same order is replayed from tick 0 once per candidate
    /// acceptance tick R; success is never assumed to be monotone in R, so every candidate is simulated.
    /// </summary>
    public static class GraceMeasurement
    {
        /// <summary>Runs both the immediate and the delayed sweep for one request.</summary>
        public static GraceReport Measure(GraceRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Scenario == null) throw new ArgumentException("Scenario is required.", nameof(request));
            if (request.Orders == null || request.Orders.Count == 0) throw new ArgumentException("At least one order is required.", nameof(request));
            if (request.FactionId < 1 || request.FactionId > 2) throw new ArgumentOutOfRangeException(nameof(request));
            if (request.FirstObservedTick < 0) throw new ArgumentOutOfRangeException(nameof(request));
            if (request.AcceptStep < 1) throw new ArgumentOutOfRangeException(nameof(request));
            if (request.InputDelayTicks < 0) throw new ArgumentOutOfRangeException(nameof(request));
            if (request.RateBandCount < 1) throw new ArgumentOutOfRangeException(nameof(request));
            long limit = request.TickLimit > 0 ? request.TickLimit : request.Scenario.VerificationTickLimit;
            if (limit < 1) throw new ArgumentOutOfRangeException(nameof(request));
            long minAccept = request.MinAcceptTick < 1 ? 1 : request.MinAcceptTick;
            long maxAccept = request.MaxAcceptTick < 0 ? limit : request.MaxAcceptTick;

            var report = new GraceReport
            {
                Criterion = request.Criterion,
                FactionId = request.FactionId,
                ArmyId = request.ArmyId,
                OutpostId = request.OutpostId,
                FirstObservedTick = request.FirstObservedTick,
                TickLimit = limit,
                MinAcceptTick = minAccept,
                MaxAcceptTick = maxAccept,
                AcceptStep = request.AcceptStep
            };
            // The two sweeps overlap wherever R + delay is also an immediate candidate; one run per applied tick is
            // enough. The cache is only ever probed by key, never enumerated, so it cannot reorder anything.
            var cache = new Dictionary<long, GraceOutcome>();
            Func<long, GraceOutcome> evaluate = applyTick =>
            {
                GraceOutcome cached;
                if (cache.TryGetValue(applyTick, out cached)) return cached;
                var outcome = RunOnce(request, limit, applyTick);
                cache.Add(applyTick, outcome);
                return outcome;
            };
            report.Immediate = Scan(minAccept, maxAccept, request.AcceptStep, request.FirstObservedTick, 0, evaluate, request.RateBandCount);
            report.Delayed = Scan(minAccept, maxAccept, request.AcceptStep, request.FirstObservedTick, request.InputDelayTicks, evaluate, request.RateBandCount);
            return report;
        }

        /// <summary>
        /// Candidate sweep over the acceptance tick R. <paramref name="evaluate"/> receives the tick at which the
        /// order actually reaches the simulation, that is R + <paramref name="inputDelayTicks"/>; the recorded
        /// candidate stays R so both cases are comparable on the operator's clock.
        /// </summary>
        public static GraceResult Scan(long minAccept, long maxAccept, long step, long firstObservedTick,
            long inputDelayTicks, Func<long, GraceOutcome> evaluate, int bandCount = 10)
        {
            if (evaluate == null) throw new ArgumentNullException(nameof(evaluate));
            if (step < 1) throw new ArgumentOutOfRangeException(nameof(step));
            if (inputDelayTicks < 0) throw new ArgumentOutOfRangeException(nameof(inputDelayTicks));
            if (bandCount < 1) throw new ArgumentOutOfRangeException(nameof(bandCount));
            var result = new GraceResult { InputDelayTicks = inputDelayTicks, FirstObservedTick = firstObservedTick };
            var outcomes = new List<KeyValuePair<long, GraceOutcome>>();
            for (long candidate = minAccept; candidate <= maxAccept; candidate = checked(candidate + step))
            {
                var outcome = evaluate(checked(candidate + inputDelayTicks));
                outcomes.Add(new KeyValuePair<long, GraceOutcome>(candidate, outcome));
                switch (outcome)
                {
                    case GraceOutcome.Success: result.SuccessTicks.Add(candidate); break;
                    case GraceOutcome.Failure: result.FailureTicks.Add(candidate); break;
                    case GraceOutcome.Undetermined: result.UndeterminedTicks.Add(candidate); break;
                    default: result.NotAppliedTicks.Add(candidate); break;
                }
            }
            Describe(result, outcomes, minAccept, maxAccept, bandCount);
            if (result.SuccessTicks.Count != 0)
            {
                long last = result.SuccessTicks[result.SuccessTicks.Count - 1];
                result.LastSuccessTick = last;
                result.GraceTicks = checked(last - firstObservedTick);
                result.Verdict = GraceVerdict.Grace;
            }
            // "No grace" claims that no acceptance tick works, so it needs at least one decided failure and no
            // undecided candidate. Everything else (only undecided, or nothing applied at all) stays unevaluated.
            else if (result.UndeterminedTicks.Count == 0 && result.FailureTicks.Count != 0) result.Verdict = GraceVerdict.NoGrace;
            else result.Verdict = GraceVerdict.Unevaluated;
            return result;
        }

        /// <summary>
        /// Fills the density fields from the outcomes in sweep order. Runs need no parameter and show the islands
        /// directly; the bands answer "from where does it stop being dependable" at 90% and 50%. Undetermined and
        /// not-applied candidates say nothing about the acceptance tick, so they break a run but are left out of
        /// every rate.
        /// </summary>
        private static void Describe(GraceResult result, List<KeyValuePair<long, GraceOutcome>> outcomes,
            long minAccept, long maxAccept, int bandCount)
        {
            GraceRun open = null;
            foreach (var pair in outcomes)
            {
                if (pair.Value == GraceOutcome.Success)
                {
                    if (open == null) { open = new GraceRun { FromTick = pair.Key, ToTick = pair.Key, Count = 1 }; result.SuccessRuns.Add(open); }
                    else { open.ToTick = pair.Key; open.Count++; }
                }
                else open = null;
                if (pair.Value == GraceOutcome.Success || pair.Value == GraceOutcome.Failure) result.DecidedCount++;
                if (pair.Value == GraceOutcome.Success) result.SuccessCount++;
            }
            if (result.DecidedCount > 0) result.SuccessPermille = (int)(1000L * result.SuccessCount / result.DecidedCount);
            foreach (var run in result.SuccessRuns)
                if (result.LongestSuccessRun == null || run.Count > result.LongestSuccessRun.Count) result.LongestSuccessRun = run;

            long span = checked(maxAccept - minAccept + 1);
            if (span < 1) return;
            for (int i = 0; i < bandCount; i++)
                result.Bands.Add(new GraceBand
                {
                    FromTick = checked(minAccept + span * i / bandCount),
                    ToTick = i == bandCount - 1 ? maxAccept : checked(minAccept + span * (i + 1) / bandCount - 1)
                });
            foreach (var pair in outcomes)
            {
                if (pair.Value != GraceOutcome.Success && pair.Value != GraceOutcome.Failure) continue;
                // The band is found from the printed boundaries, never from a second formula: integer division makes
                // "which band starts here" and "which band is this tick in" disagree, which mislabels the edges.
                int index = bandCount - 1;
                while (index > 0 && result.Bands[index].FromTick > pair.Key) index--;
                var band = result.Bands[index];
                band.Decided++;
                if (pair.Value == GraceOutcome.Success) band.Success++;
            }
            foreach (var band in result.Bands)
            {
                if (band.Decided == 0) continue;
                band.SuccessPermille = (int)(1000L * band.Success / band.Decided);
                if (band.SuccessPermille >= 900)
                {
                    if (result.FirstBandAtLeast900Tick == null) result.FirstBandAtLeast900Tick = band.FromTick;
                    continue;
                }
                if (result.FirstBandAtLeast900Tick == null) continue;
                if (result.FirstBandBelow900Tick == null) result.FirstBandBelow900Tick = band.FromTick;
                if (band.SuccessPermille < 500 && result.FirstBandBelow500Tick == null) result.FirstBandBelow500Tick = band.FromTick;
            }
        }

        /// <summary>One full run from tick 0 with the order applied during Step(applyTick).</summary>
        private static GraceOutcome RunOnce(GraceRequest request, long limit, long applyTick)
        {
            var sim = new Simulation.Simulation(request.Scenario);
            var evaluator = CreateEvaluator(request);
            evaluator.Begin(sim);
            bool applied = false;
            var empty = Array.Empty<ScheduledInput>();
            for (long tick = 1; tick <= limit; tick++)
            {
                if (sim.Capture(request.FactionId).Result.HasEnded) break;
                if (tick == applyTick)
                {
                    sim.Step(tick, ComposeInputs(sim, tick, request.Orders));
                    applied = true;
                }
                else sim.Step(tick, empty);
                if (sim.Capture(request.FactionId).Result.IsFault) throw new InvalidOperationException("Simulation fault during grace measurement at tick " + tick + ".");
                evaluator.Observe(sim);
                if (evaluator.IsDecided) break;
            }
            // A candidate whose order never entered the run says nothing about that acceptance tick.
            if (!applied) return GraceOutcome.NotApplied;
            return evaluator.Conclude(sim);
        }

        /// <summary>
        /// Same Reserve/Resolve pair the human command path produces. Duplicated from the EditMode test helper on
        /// purpose: Application must not depend on the test assembly.
        /// </summary>
        public static IReadOnlyList<ScheduledInput> ComposeInputs(Simulation.Simulation sim, long tick, IReadOnlyList<PolicyOrder> orders)
        {
            var inputs = new List<ScheduledInput>();
            var scopes = new List<ScopeKey>();
            var revisions = new List<ulong>();
            ulong index = checked((ulong)tick * 100);
            foreach (var o in orders)
            {
                int slot = scopes.IndexOf(o.Target);
                ulong revision;
                if (slot < 0) { revision = sim.Revision(o.Target); scopes.Add(o.Target); revisions.Add(revision + 1); }
                else { revision = revisions[slot]; revisions[slot] = revision + 1; }
                ulong id = checked(++index);
                inputs.Add(new ScheduledInput(index, InputKind.Reserve, tick - 1, tick, id, id, new[] { Order(o, id, revision, tick) }));
                inputs.Add(new ScheduledInput(checked(++index), InputKind.Resolve, tick - 1, tick, id, id, new[] { Order(o, id, revision + 1, tick) }));
            }
            return inputs;
        }

        private static PolicyOrder Order(PolicyOrder o, ulong id, ulong revision, long tick) =>
            new PolicyOrder(id, 0, o.Source, o.Target, o.Kind, o.Goal, o.Priority, o.AllowedLoss, o.End,
                o.ReservePermille, revision, Array.Empty<PolicyVersion>(), tick - 1, o.Expiration);

        private static IGraceEvaluator CreateEvaluator(GraceRequest request)
        {
            switch (request.Criterion)
            {
                case GraceCriterion.CoreDefense: return new CoreDefenseEvaluator(request.FactionId);
                case GraceCriterion.Retreat: return new RetreatEvaluator(request.FactionId, request.ArmyId);
                case GraceCriterion.Reinforcement: return new ReinforcementEvaluator(request.FactionId, request.OutpostId, request.Scenario);
                case GraceCriterion.DiversionResponse: return new DiversionEvaluator(request.FactionId, request.OutpostId);
                case GraceCriterion.OutpostHeld: return new OutpostHeldEvaluator(request.FactionId, request.OutpostId);
                case GraceCriterion.CoreAndOutpostHeld: return new CoreAndOutpostHeldEvaluator(request.FactionId, request.OutpostId);
                default: throw new ArgumentOutOfRangeException(nameof(request));
            }
        }

        /// <summary>
        /// Ground-truth outpost ownership from the public frames. The owning faction always sees its own outposts,
        /// so its frame reports the truth; the loser's frame may still hold a stale memory, which is discarded here.
        /// </summary>
        public static uint OutpostOwner(Simulation.Simulation sim, uint outpostId)
        {
            for (uint f = 1; f <= 2; f++)
                foreach (var objective in sim.Capture(f).Objectives)
                    if (objective.Kind == GoalKind.Outpost && objective.Id == outpostId && objective.IsOwnerKnown && objective.OwnerFactionId == f)
                        return f;
            return 0;
        }

        /// <summary>True when the faction's own core has been destroyed (the match then has ended).</summary>
        public static bool CoreLost(Simulation.Simulation sim, uint faction)
        {
            var result = sim.Capture(faction).Result;
            if (!result.HasEnded || result.IsFault) return false;
            return result.IsDraw || result.WinnerFactionId != 0 && result.WinnerFactionId != faction;
        }

        public static int ArmyAlive(Simulation.Simulation sim, uint faction, uint armyId)
        {
            foreach (var army in sim.Capture(faction).Observation.OwnArmies)
                if (army.Id == armyId) return army.AliveCount;
            throw new ArgumentException("Army " + armyId + " does not belong to faction " + faction + ".", nameof(armyId));
        }

        private interface IGraceEvaluator
        {
            void Begin(Simulation.Simulation sim);
            void Observe(Simulation.Simulation sim);
            /// <summary>True once no further tick can change the outcome.</summary>
            bool IsDecided { get; }
            GraceOutcome Conclude(Simulation.Simulation sim);
        }

        /// <summary>Core defence: destruction of the measured faction's core is the only failure.</summary>
        private sealed class CoreDefenseEvaluator : IGraceEvaluator
        {
            private readonly uint faction;
            private bool lost;
            internal CoreDefenseEvaluator(uint faction) { this.faction = faction; }
            public void Begin(Simulation.Simulation sim) { }
            public void Observe(Simulation.Simulation sim) { if (CoreLost(sim, faction)) lost = true; }
            public bool IsDecided => lost;
            // Surviving the measured horizon is the success; the criterion is destruction avoidance, not victory.
            public GraceOutcome Conclude(Simulation.Simulation sim) => lost ? GraceOutcome.Failure : GraceOutcome.Success;
        }

        /// <summary>
        /// Retreat: alive * 2 &gt;= starting strength, evaluated at the end of the run. The comparison avoids
        /// rounding entirely, so an army of 5 needs 3 survivors. Losses can still be replaced by reinforcements,
        /// so the verdict is never taken early.
        /// </summary>
        private sealed class RetreatEvaluator : IGraceEvaluator
        {
            private readonly uint faction, armyId;
            private int initial;
            internal RetreatEvaluator(uint faction, uint armyId) { this.faction = faction; this.armyId = armyId; }
            public void Begin(Simulation.Simulation sim)
            {
                initial = ArmyAlive(sim, faction, armyId);
                if (initial == 0) throw new ArgumentException("Army " + armyId + " starts empty; a 50% threshold is undefined.");
            }
            public void Observe(Simulation.Simulation sim) { }
            public bool IsDecided => false;
            public GraceOutcome Conclude(Simulation.Simulation sim) =>
                checked(ArmyAlive(sim, faction, armyId) * 2) >= initial ? GraceOutcome.Success : GraceOutcome.Failure;
        }

        /// <summary>
        /// Reinforcement: "the outpost has not fallen" means the enemy does not own it. Success once an own infantry
        /// soldier stands inside the capture radius while the enemy does not own the outpost (defenders arrived in
        /// time); failure once the enemy owns it before that. Neither within the horizon is undetermined, because an
        /// outpost that was simply never contested does not show that help would have arrived. The outpost may start
        /// neutral, which the shipped scenarios do; starting enemy-owned is rejected as an ill-posed measurement.
        /// </summary>
        private sealed class ReinforcementEvaluator : IGraceEvaluator
        {
            private readonly uint faction, enemy, outpostId;
            private readonly SimPoint position;
            private readonly long radiusRaw;
            private GraceOutcome outcome = GraceOutcome.Undetermined;
            internal ReinforcementEvaluator(uint faction, uint outpostId, ScenarioDefinition scenario)
            {
                this.faction = faction; this.outpostId = outpostId; enemy = faction == 1 ? 2U : 1U;
                bool found = false;
                foreach (var outpost in scenario.Outposts)
                    if (outpost.Id == outpostId)
                    {
                        if (outpost.OwnerFactionId == enemy)
                            throw new ArgumentException("Outpost " + outpostId + " starts owned by the enemy of faction " + faction + ".");
                        position = outpost.Position; found = true;
                    }
                if (!found) throw new ArgumentException("Outpost " + outpostId + " is not in the scenario.");
                radiusRaw = scenario.Rules.CaptureRadius.Raw;
            }
            public void Begin(Simulation.Simulation sim) { }
            public void Observe(Simulation.Simulation sim)
            {
                if (outcome != GraceOutcome.Undetermined) return;
                if (OutpostOwner(sim, outpostId) == enemy) { outcome = GraceOutcome.Failure; return; }
                foreach (var unit in sim.Capture(faction).Units)
                {
                    if (!unit.IsOwn || unit.Kind != UnitKind.Infantry) continue;
                    long dx = checked(unit.Position.X.Raw - position.X.Raw), dz = checked(unit.Position.Z.Raw - position.Z.Raw);
                    if (checked(dx * dx + dz * dz) <= checked(radiusRaw * radiusRaw)) { outcome = GraceOutcome.Success; return; }
                }
            }
            public bool IsDecided => outcome != GraceOutcome.Undetermined;
            public GraceOutcome Conclude(Simulation.Simulation sim) => outcome;
        }

        /// <summary>
        /// Outpost held: success only when the measured faction owns the outpost at the end of the run (the tick
        /// limit, or the end of the match). Reinforcement is decided the moment defenders arrive, which happens
        /// without any order in the shipped scenarios, so it cannot show whether an order came in time. Holding the
        /// outpost at the end depends on what the order changed, so every applied candidate is judged, never early.
        /// A neutral or enemy-owned outpost is a failure: the faction did not hold it.
        /// </summary>
        private sealed class OutpostHeldEvaluator : IGraceEvaluator
        {
            private readonly uint faction, outpostId;
            internal OutpostHeldEvaluator(uint faction, uint outpostId) { this.faction = faction; this.outpostId = outpostId; }
            public void Begin(Simulation.Simulation sim) { }
            public void Observe(Simulation.Simulation sim) { }
            public bool IsDecided => false;
            public GraceOutcome Conclude(Simulation.Simulation sim) =>
                OutpostOwner(sim, outpostId) == faction ? GraceOutcome.Success : GraceOutcome.Failure;
        }

        /// <summary>
        /// Core and outpost held: the diversion response the design asks for is to keep both, so success needs the
        /// core alive and the designated outpost owned at the end. The diversion criterion above is met by holding
        /// either, which for a core-saving order reduces to core defence; this one does not. A lost core ends the match,
        /// so it is a failure at once.
        /// </summary>
        private sealed class CoreAndOutpostHeldEvaluator : IGraceEvaluator
        {
            private readonly uint faction, outpostId;
            private bool coreLost;
            internal CoreAndOutpostHeldEvaluator(uint faction, uint outpostId) { this.faction = faction; this.outpostId = outpostId; }
            public void Begin(Simulation.Simulation sim) { }
            public void Observe(Simulation.Simulation sim) { if (CoreLost(sim, faction)) coreLost = true; }
            public bool IsDecided => coreLost;
            public GraceOutcome Conclude(Simulation.Simulation sim) =>
                !coreLost && OutpostOwner(sim, outpostId) == faction ? GraceOutcome.Success : GraceOutcome.Failure;
        }

        /// <summary>
        /// Diversion response: failure only when the designated outpost and the core are both lost. Holding either
        /// one at the end of the run is the success.
        /// </summary>
        private sealed class DiversionEvaluator : IGraceEvaluator
        {
            private readonly uint faction, outpostId;
            private bool failed;
            internal DiversionEvaluator(uint faction, uint outpostId) { this.faction = faction; this.outpostId = outpostId; }
            public void Begin(Simulation.Simulation sim) { }
            public void Observe(Simulation.Simulation sim)
            {
                if (CoreLost(sim, faction) && OutpostOwner(sim, outpostId) != faction) failed = true;
            }
            public bool IsDecided => failed;
            public GraceOutcome Conclude(Simulation.Simulation sim) => failed ? GraceOutcome.Failure : GraceOutcome.Success;
        }
    }
}
