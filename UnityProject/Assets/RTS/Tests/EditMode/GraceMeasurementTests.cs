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
