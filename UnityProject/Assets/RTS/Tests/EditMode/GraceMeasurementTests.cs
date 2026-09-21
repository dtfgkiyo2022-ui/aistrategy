using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Tests.EditMode
{
    /// <summary>
    /// Chapter 11 judgement grace. The sweep must not assume that success is monotone in the acceptance tick, and it
    /// must keep "no grace" (every candidate decided and failed) apart from "unevaluated" (something stayed open).
    /// </summary>
    public sealed class GraceMeasurementTests
    {
        private static Func<long, GraceOutcome> Succeeding(params long[] applyTicks)
        {
            var successes = new List<long>(applyTicks);
            return applyTick => successes.Contains(applyTick) ? GraceOutcome.Success : GraceOutcome.Failure;
        }

        [Test]
        public void ScanFindsEveryIslandOfSuccess()
        {
            // Success at 2 and 7 but not at 3..6: a sweep that stopped at the first failure would report 2.
            var result = GraceMeasurement.Scan(1, 10, 1, 0, 0, Succeeding(2, 7));
            Assert.That(result.SuccessTicks, Is.EqualTo(new long[] { 2, 7 }));
            Assert.That(result.FailureTicks, Is.EqualTo(new long[] { 1, 3, 4, 5, 6, 8, 9, 10 }));
            Assert.That(result.LastSuccessTick, Is.EqualTo(7));
            Assert.That(result.Verdict, Is.EqualTo(GraceVerdict.Grace));
        }

        [Test]
        public void IslandsAreReportedAsRunsAndARate()
        {
            // The same 2-and-7 sweep: LastSuccessTick is 7, but only 2 of 10 candidates work and neither run is wide.
            var result = GraceMeasurement.Scan(1, 10, 1, 0, 0, Succeeding(2, 7));
            Assert.That(result.SuccessRuns.Select(r => r.FromTick + "-" + r.ToTick + "x" + r.Count),
                Is.EqualTo(new[] { "2-2x1", "7-7x1" }));
            Assert.That(result.DecidedCount, Is.EqualTo(10));
            Assert.That(result.SuccessCount, Is.EqualTo(2));
            Assert.That(result.SuccessPermille, Is.EqualTo(200));
            Assert.That(result.LongestSuccessRun.FromTick, Is.EqualTo(2), "ties go to the earliest run");
        }

        [Test]
        public void BandsSayWhereTheSweepStopsBeingDependable()
        {
            var result = GraceMeasurement.Scan(1, 10, 1, 0, 0, Succeeding(1, 2, 3, 4, 5, 6, 7, 9), bandCount: 5);
            Assert.That(result.Bands.Select(b => b.FromTick + "-" + b.ToTick + ":" + b.SuccessPermille),
                Is.EqualTo(new[] { "1-2:1000", "3-4:1000", "5-6:1000", "7-8:500", "9-10:500" }));
            Assert.That(result.FirstBandAtLeast900Tick, Is.EqualTo(1));
            Assert.That(result.FirstBandBelow900Tick, Is.EqualTo(7));
            Assert.That(result.FirstBandBelow500Tick, Is.Null, "500 permille is not below 500");
            Assert.That(result.LastSuccessTick, Is.EqualTo(9), "the last success sits inside a half-failing band");
        }

        [Test]
        public void BandsBeforeTheDependableStretchAreNotReportedAsTheEndOfIt()
        {
            // A "window" predicate like retreat: too early fails, the middle works, then it decays. Reporting the
            // very first band as "fell under 90%" told us nothing; the answer must be where the window closes.
            var result = GraceMeasurement.Scan(1, 10, 1, 0, 0, Succeeding(3, 4, 5, 6, 7), bandCount: 5);
            Assert.That(result.Bands.Select(b => b.SuccessPermille), Is.EqualTo(new[] { 0, 1000, 1000, 500, 0 }));
            Assert.That(result.FirstBandAtLeast900Tick, Is.EqualTo(3), "the window opens in the second band");
            Assert.That(result.FirstBandBelow900Tick, Is.EqualTo(7), "and closes in the fourth, not at tick 1");
            Assert.That(result.FirstBandBelow500Tick, Is.EqualTo(9));
        }

        [Test]
        public void ASweepThatNeverBecomesDependableReportsNoBandAtAll()
        {
            var result = GraceMeasurement.Scan(1, 10, 1, 0, 0, Succeeding(1, 4, 7), bandCount: 5);
            Assert.That(result.Bands.Select(b => b.SuccessPermille), Is.EqualTo(new[] { 500, 500, 0, 500, 0 }));
            Assert.That(result.FirstBandAtLeast900Tick, Is.Null);
            Assert.That(result.FirstBandBelow900Tick, Is.Null, "there was no dependable stretch to fall out of");
            Assert.That(result.FirstBandBelow500Tick, Is.Null);
            Assert.That(result.SuccessPermille, Is.EqualTo(300), "the overall rate still says it is unreliable");
        }

        [Test]
        public void EveryCandidateIsCountedInTheBandThatPrintsItsTick()
        {
            // 4280..4400 by 10 into 6 bands: integer division put 4380 in the band printed as 4360-4379, so the last
            // success landed in a band shown as 0%. Every decided candidate must fall inside its own band's range.
            var result = GraceMeasurement.Scan(4280, 4400, 10, 0, 0, _ => GraceOutcome.Success, bandCount: 6);
            Assert.That(result.Bands.Sum(b => b.Decided), Is.EqualTo(13), "every candidate is counted exactly once");
            Assert.That(result.Bands.Where(b => b.Decided > 0).Select(b => b.SuccessPermille).Distinct(),
                Is.EqualTo(new[] { 1000 }), "an all-success sweep has no band under 100%");
            foreach (var band in result.Bands)
                Assert.That(result.SuccessTicks.Count(t => t >= band.FromTick && t <= band.ToTick), Is.EqualTo(band.Decided),
                    "band " + band.FromTick + "-" + band.ToTick + " counts ticks it does not print");
        }

        [Test]
        public void UndecidedCandidatesBreakARunButLeaveTheRateAlone()
        {
            // Candidate 3 is undetermined: it says nothing about acceptance tick 3, so it is outside every rate.
            var result = GraceMeasurement.Scan(1, 4, 1, 0, 0,
                applyTick => applyTick == 3 ? GraceOutcome.Undetermined : GraceOutcome.Success, bandCount: 1);
            Assert.That(result.SuccessRuns.Select(r => r.FromTick + "-" + r.ToTick), Is.EqualTo(new[] { "1-2", "4-4" }));
            Assert.That(result.DecidedCount, Is.EqualTo(3));
            Assert.That(result.SuccessPermille, Is.EqualTo(1000));
            Assert.That(result.Bands.Single().Decided, Is.EqualTo(3));
        }

        [Test]
        public void ABandThatDecidedNothingIsNotReadAsAFailure()
        {
            var result = GraceMeasurement.Scan(1, 2, 1, 0, 0, _ => GraceOutcome.NotApplied, bandCount: 2);
            Assert.That(result.Bands.Select(b => b.SuccessPermille), Is.EqualTo(new[] { -1, -1 }));
            Assert.That(result.FirstBandBelow900Tick, Is.Null);
            Assert.That(result.SuccessPermille, Is.EqualTo(-1));
        }

        [Test]
        public void GraceCountsFromTheFirstObservationTick()
        {
            var result = GraceMeasurement.Scan(1, 10, 1, 4, 0, Succeeding(2, 7));
            Assert.That(result.GraceTicks, Is.EqualTo(3)); // 7 - 4
        }

        [Test]
        public void NoSuccessAnywhereIsNoGrace()
        {
            var result = GraceMeasurement.Scan(1, 5, 1, 0, 0, _ => GraceOutcome.Failure);
            Assert.That(result.SuccessTicks, Is.Empty);
            Assert.That(result.LastSuccessTick, Is.Null);
            Assert.That(result.GraceTicks, Is.Null);
            Assert.That(result.Verdict, Is.EqualTo(GraceVerdict.NoGrace));
        }

        [Test]
        public void UndecidedCandidatesBlockTheNoGraceClaim()
        {
            var result = GraceMeasurement.Scan(1, 4, 1, 0,
                0, applyTick => applyTick >= 3 ? GraceOutcome.Undetermined : GraceOutcome.Failure);
            Assert.That(result.FailureTicks, Is.EqualTo(new long[] { 1, 2 }));
            Assert.That(result.UndeterminedTicks, Is.EqualTo(new long[] { 3, 4 }));
            Assert.That(result.Verdict, Is.EqualTo(GraceVerdict.Unevaluated));
        }

        [Test]
        public void SuccessOutranksRemainingUndecidedCandidates()
        {
            var result = GraceMeasurement.Scan(1, 4, 1, 0,
                0, applyTick => applyTick == 2 ? GraceOutcome.Success : GraceOutcome.Undetermined);
            Assert.That(result.Verdict, Is.EqualTo(GraceVerdict.Grace));
            Assert.That(result.LastSuccessTick, Is.EqualTo(2));
        }

        [Test]
        public void CandidatesWhoseOrderNeverEnteredAreNeitherSuccessNorFailure()
        {
            var result = GraceMeasurement.Scan(1, 3, 1, 0, 0, _ => GraceOutcome.NotApplied);
            Assert.That(result.NotAppliedTicks, Is.EqualTo(new long[] { 1, 2, 3 }));
            Assert.That(result.FailureTicks, Is.Empty);
            Assert.That(result.Verdict, Is.EqualTo(GraceVerdict.Unevaluated));
        }

        [Test]
        public void DelayedCaseShiftsTheAppliedTickButReportsTheOperatorTick()
        {
            var applied = new List<long>();
            var result = GraceMeasurement.Scan(1, 20, 1, 0, 60, applyTick =>
            {
                applied.Add(applyTick);
                // The order only works when it lands in 61..80, that is when the operator acts at 1..20.
                return applyTick >= 61 && applyTick <= 80 ? GraceOutcome.Success : GraceOutcome.Failure;
            });
            Assert.That(applied.First(), Is.EqualTo(61));
            Assert.That(applied.Last(), Is.EqualTo(80));
            Assert.That(result.SuccessTicks.Count, Is.EqualTo(20));
            Assert.That(result.LastSuccessTick, Is.EqualTo(20), "candidates stay on the operator's clock");
            Assert.That(result.GraceTicks, Is.EqualTo(20));
            Assert.That(result.InputDelayTicks, Is.EqualTo(60));
        }

        [Test]
        public void DelayedCaseLosesTheLateCandidatesTheImmediateCaseKeeps()
        {
            // The order is only effective when it lands no later than tick 30.
            Func<long, GraceOutcome> evaluate = applyTick => applyTick <= 30 ? GraceOutcome.Success : GraceOutcome.Failure;
            var immediate = GraceMeasurement.Scan(1, 40, 1, 0, 0, evaluate);
            var delayed = GraceMeasurement.Scan(1, 40, 1, 0, 60, evaluate);
            Assert.That(immediate.GraceTicks, Is.EqualTo(30));
            Assert.That(delayed.Verdict, Is.EqualTo(GraceVerdict.NoGrace), "60 ticks of comprehension already exhaust the grace");
        }

        [Test]
        public void StrideVisitsOnlyTheRequestedCandidates()
        {
            var visited = new List<long>();
            GraceMeasurement.Scan(1, 10, 4, 0, 0, applyTick => { visited.Add(applyTick); return GraceOutcome.Failure; });
            Assert.That(visited, Is.EqualTo(new long[] { 1, 5, 9 }));
        }

        [Test]
        public void ComposedOrderReachesTheSimulation()
        {
            var scenario = WeekTwoScenario.Create();
            var sim = new Battle(scenario);
            var order = RetreatOrder(1);
            for (long tick = 1; tick <= 6; tick++)
                sim.Step(tick, tick == 5 ? GraceMeasurement.ComposeInputs(sim, tick, new[] { order }) : Array.Empty<ScheduledInput>());
            var accepted = sim.Capture(1).Commands.Where(c => c.Kind == PolicyKind.Retreat).ToArray();
            Assert.That(accepted.Length, Is.GreaterThan(0), "the retreat order never reached the command pipeline");
            Assert.That(accepted.Any(c => c.Reason == ReasonCode.None), "the composed order was rejected: "
                + string.Join(",", accepted.Select(c => c.Status + "/" + c.Reason)));
        }

        [Test]
        public void RetreatCriterionMeasuresTheDesignatedArmyOnAShortHorizon()
        {
            var report = GraceMeasurement.Measure(Request(GraceCriterion.Retreat, armyId: 1, ticks: 120, maxR: 3, step: 2));
            Assert.That(report.Immediate.SuccessTicks, Is.EqualTo(new long[] { 1, 3 }),
                "army 1 is untouched after 120 ticks, so every acceptance tick keeps at least half of it");
            Assert.That(report.Immediate.Verdict, Is.EqualTo(GraceVerdict.Grace));
            Assert.That(report.Delayed.InputDelayTicks, Is.EqualTo(60));
            Assert.That(report.Delayed.Verdict, Is.EqualTo(GraceVerdict.Grace));
        }

        [Test]
        public void ReinforcementStaysUnevaluatedWhileNobodyReachesTheOutpost()
        {
            // Outpost 1 sits at the middle of the map; within 60 ticks no defender arrives and nobody takes it.
            var report = GraceMeasurement.Measure(Request(GraceCriterion.Reinforcement, outpostId: 1, ticks: 60, maxR: 3, step: 2));
            Assert.That(report.Immediate.UndeterminedTicks, Is.EqualTo(new long[] { 1, 3 }));
            Assert.That(report.Immediate.Verdict, Is.EqualTo(GraceVerdict.Unevaluated));
        }

        [Test]
        public void OutpostHeldFailsWhenTheFactionDoesNotOwnTheOutpostAtTheEnd()
        {
            // The week two outposts start neutral and nobody reaches outpost 1 within 60 ticks, so it is not held.
            // Unlike reinforcement, every applied candidate is judged: nothing stays undetermined.
            var report = GraceMeasurement.Measure(Request(GraceCriterion.OutpostHeld, outpostId: 1, ticks: 60, maxR: 3, step: 2));
            Assert.That(report.Immediate.FailureTicks, Is.EqualTo(new long[] { 1, 3 }));
            Assert.That(report.Immediate.UndeterminedTicks, Is.Empty);
            Assert.That(report.Immediate.Verdict, Is.EqualTo(GraceVerdict.NoGrace));
        }

        [Test]
        public void CoreAndOutpostHeldNeedsBothAndTakesTwoOrdersAtTheSameTick()
        {
            // The core survives 60 ticks, but the neutral outpost is not owned, so holding both fails.
            var request = Request(GraceCriterion.CoreAndOutpostHeld, outpostId: 1, ticks: 60, maxR: 3, step: 2);
            request.Orders = new[] { RetreatOrder(1), RetreatOrder(2) };
            var report = GraceMeasurement.Measure(request);
            Assert.That(report.Immediate.FailureTicks, Is.EqualTo(new long[] { 1, 3 }));
            Assert.That(report.Immediate.Verdict, Is.EqualTo(GraceVerdict.NoGrace));
            var sim = new Battle(WeekTwoScenario.Create());
            var inputs = GraceMeasurement.ComposeInputs(sim, 5, request.Orders);
            Assert.That(inputs.Count, Is.EqualTo(4), "each order becomes a Reserve and a Resolve input at the same tick");
        }

        [Test]
        public void OutpostHeldIsNotDecidedBeforeTheHorizon()
        {
            // The same neutral outpost is a failure at the horizon for a candidate applied late in the run,
            // and a candidate beyond the horizon is not applied at all.
            var request = Request(GraceCriterion.OutpostHeld, outpostId: 1, ticks: 60, maxR: 3, step: 2);
            request.InputDelayTicks = 58;
            var report = GraceMeasurement.Measure(request);
            Assert.That(report.Delayed.FailureTicks, Is.EqualTo(new long[] { 1 }), "1+58 is inside the 60 tick horizon");
            Assert.That(report.Delayed.NotAppliedTicks, Is.EqualTo(new long[] { 3 }), "3+58 is beyond it");
        }

        [Test]
        public void AcceptanceTicksBeyondTheHorizonAreNotApplied()
        {
            var request = Request(GraceCriterion.CoreDefense, ticks: 20, maxR: 2, step: 1);
            request.InputDelayTicks = 60; // 1+60 and 2+60 both exceed the 20 tick horizon
            var report = GraceMeasurement.Measure(request);
            Assert.That(report.Immediate.Verdict, Is.EqualTo(GraceVerdict.Grace), "the core survives 20 ticks");
            Assert.That(report.Delayed.NotAppliedTicks, Is.EqualTo(new long[] { 1, 2 }));
            Assert.That(report.Delayed.Verdict, Is.EqualTo(GraceVerdict.Unevaluated));
        }

        [Test]
        public void RepeatedMeasurementsAgree()
        {
            var first = GraceMeasurement.Measure(Request(GraceCriterion.Retreat, armyId: 1, ticks: 80, maxR: 2, step: 1));
            var second = GraceMeasurement.Measure(Request(GraceCriterion.Retreat, armyId: 1, ticks: 80, maxR: 2, step: 1));
            Assert.That(second.Immediate.SuccessTicks, Is.EqualTo(first.Immediate.SuccessTicks));
            Assert.That(second.Delayed.SuccessTicks, Is.EqualTo(first.Delayed.SuccessTicks));
            Assert.That(second.Immediate.GraceTicks, Is.EqualTo(first.Immediate.GraceTicks));
        }

        [Test]
        public void ArmyAliveReadsTheOwnArmyView()
        {
            var sim = new Battle(WeekTwoScenario.Create());
            Assert.That(GraceMeasurement.ArmyAlive(sim, 1, 1), Is.EqualTo(8));
            Assert.That(GraceMeasurement.CoreLost(sim, 1), Is.False);
            Assert.That(GraceMeasurement.OutpostOwner(sim, 1), Is.EqualTo(0U), "the week two outposts start neutral");
        }

        private static GraceRequest Request(GraceCriterion criterion, long ticks, long maxR, long step,
            uint armyId = 0, uint outpostId = 0) => new GraceRequest
        {
            Scenario = WeekTwoScenario.Create(),
            Orders = new[] { RetreatOrder(1) },
            Criterion = criterion,
            FactionId = 1,
            ArmyId = armyId,
            OutpostId = outpostId,
            FirstObservedTick = 0,
            TickLimit = ticks,
            MinAcceptTick = 1,
            MaxAcceptTick = maxR,
            AcceptStep = step,
            InputDelayTicks = 60
        };

        private static PolicyOrder RetreatOrder(uint armyId) => new PolicyOrder(1, 0, CommandSource.Human,
            new ScopeKey(1, ScopeKind.Army, armyId), PolicyKind.Retreat, default, 50, new LossBudget(300),
            new EndCondition(EndKind.UntilReplaced, 0), 0, 0, Array.Empty<PolicyVersion>(), 0,
            new Expiration(long.MaxValue, 0, ExpireFlags.None));
    }
}
