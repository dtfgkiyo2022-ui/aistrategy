using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Decision;
using Rts.Simulation;

namespace Rts.Tests.EditMode
{
    public sealed class CoordinatedOffenseTests
    {
        private static SimPoint P(int x, int z = 0) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));
        private static PolicyGoal Goal => new PolicyGoal(GoalKind.Outpost, 1, default);
        private static PolicyGoal Home => new PolicyGoal(GoalKind.Core, 1, default);
        private static KnownObjective[] Objectives => new[] {
            new KnownObjective(GoalKind.Core, 1, P(0), true, 1, true, 3000, 0),
            new KnownObjective(GoalKind.Core, 2, P(200), true, 2, true, 3000, 0),
            new KnownObjective(GoalKind.Outpost, 1, P(100), true, 0, false, 0, 0) };
        private static FactionObservation O(long tick = 20, int enemies = 0, SimPoint? enemy = null, bool visible = false) =>
            new FactionObservation(1, tick, Array.Empty<OwnArmyView>(), visible ? new[] { new VisibleEnemy(1, enemy ?? P(100), 1) } : Array.Empty<VisibleEnemy>(),
                enemies == 0 ? Array.Empty<EnemyContact>() : new[] { new EnemyContact(1, enemy ?? P(100), tick, enemies, enemies, visible) }, Objectives);
        private static OffenseArmyInput Army(uint id, int n, int x, bool loss = false) => new OffenseArmyInput(id, Enumerable.Repeat(P(x), n).ToArray(), Fix64.FromInt(1).Raw, loss, false);
        private static ArmyDecisionInput[] Inputs(OffenseArmyInput[] own, ArmyDecisionMemory[] memory = null) => own.Select((a, i) => new ArmyDecisionInput(
            new OwnArmyView(a.ArmyId, 1, UnitKind.Infantry, a.Soldiers.FirstOrDefault(), a.Soldiers.Count, default), default, false,
            memory == null ? default : memory[i], new[] { new ObjectiveRoute(Home, 1, new[] { P(0) }) })).ToArray();
        private static OffenseRouteInput Route(OffenseArmyInput[] own, int rally = 60) => new OffenseRouteInput(Goal, P(rally), new[] { P(rally), P(100) },
            own.Select(a => new ObjectiveRoute(new PolicyGoal(GoalKind.Point, a.ArmyId, default), 1, new[] { a.Soldiers.FirstOrDefault(), P(rally) })).ToArray());
        private static FactionOffenseMemory Gathering(params uint[] ids) => new FactionOffenseMemory { Id = 1, Goal = Goal, RallyPoint = P(60), Phase = OffensivePhase.Gathering,
            StartedTick = 20, MoveDeadlineTick = 1200, MaintainedSinceTick = 20, GatheredTick = -1, PlannedArmyIds = ids };
        private static void Update(FactionOffenseMemory s, OffenseArmyInput[] own, ArmyDecisionMemory[] m, long tick, int enemies = 0, bool allocation = false, FactionObservation observation = null, bool human = false)
            => OffenseDecision.Update(observation ?? O(tick, enemies), tick, s, Inputs(own, m), own, m, new[] { Route(own) }, allocation, human, Home);

        [Test]
        public void UnionCountsCrossingAndOverlappingPathsOnceAndDeduplicatesAggregate()
        {
            var own = new[] { Army(1, 4, 0), Army(2, 4, 0) };
            Assert.That(OffenseDecision.Estimate(O(20, 7, P(40)), Route(own), new uint[] { 1, 2 }), Is.EqualTo(7));
            var union = new OffenseRouteInput(Goal, P(60), new[] { P(60), P(100) }, new[] {
                new ObjectiveRoute(new PolicyGoal(GoalKind.Point, 1, default), 1, new[] { P(0), P(60) }),
                new ObjectiveRoute(new PolicyGoal(GoalKind.Point, 2, default), 1, new[] { P(0, 80), P(60) }) });
            Assert.That(OffenseDecision.Estimate(O(20, 9, P(0, 80)), union, new uint[] { 1 }), Is.Zero);
            Assert.That(OffenseDecision.Estimate(O(20, 9, P(0, 80)), union, new uint[] { 1, 2 }), Is.EqualTo(9));
            var o = new FactionObservation(1, 20, Array.Empty<OwnArmyView>(), Array.Empty<VisibleEnemy>(), new[] {
                new EnemyContact(1, P(40), 20, 1, 1, true), new EnemyContact(9, P(40), 20, 1, 5, true, new uint[] { 1 }) }, Objectives);
            Assert.That(OffenseDecision.Estimate(o, Route(own), new uint[] { 1, 2 }), Is.EqualTo(5));
            Assert.That(OffenseDecision.Estimate(O(20, 7, P(-30)), Route(own), new uint[] { 1, 2 }), Is.Zero, "Endpoint distance is bounded too");
        }
        [Test]
        public void RallyUsesCumulativeMetersAndBacksAwayFromVisibleEnemiesOnly()
        {
            var path = Enumerable.Range(0, 26).Select(i => P(i * 4)).ToArray();
            Assert.That(OffenseDecision.Rally(O(), path), Is.EqualTo(P(60)));
            Assert.That(OffenseDecision.Rally(O(20, 1, P(60), true), path), Is.EqualTo(P(32)));
            Assert.That(OffenseDecision.Rally(O(20, 1, P(60), false), path), Is.EqualTo(P(60)));
            Assert.That(OffenseDecision.Rally(O(), new[] { P(0), P(20) }), Is.EqualTo(P(0)));
            Assert.That(OffenseDecision.TryRally(O(20, 1, P(0), true), new[] { P(0), P(20) }, out _), Is.False);
            Assert.That(OffenseDecision.Rally(O(), new[] { P(0), P(30), P(30, 30), P(60, 30) }), Is.EqualTo(P(30)));
        }
        [Test]
        public void TravelDeadlineUsesRallyPathAndActualSpeedThenWaitStartsAtArrival()
        {
            var own = new[] { Army(1, 3, 0), Army(2, 3, 20) }; var m = new ArmyDecisionMemory[2]; var s = new FactionOffenseMemory();
            Update(s, own, m, 20, 100, true);
            Assert.That(s.MoveDeadlineTick, Is.EqualTo(280));
            Update(s, own, m, 260, 100);
            Assert.That(s.Phase, Is.EqualTo(OffensivePhase.Gathering)); Assert.That(s.GatheredTick, Is.EqualTo(-1));
            own = new[] { Army(1, 3, 60), Army(2, 3, 60) };
            Update(s, own, m, 270, 100);
            Assert.That(s.GatheredTick, Is.EqualTo(270));
            Update(s, own, m, 869, 100); Assert.That(s.Phase, Is.EqualTo(OffensivePhase.WaitingToAdvance));
            Update(s, own, m, 870, 100); Assert.That(s.Phase, Is.EqualTo(OffensivePhase.Idle));
            Assert.That(s.SuppressedGoals.Single().UntilTick, Is.EqualTo(1470));
        }
        [Test]
        public void TravelOverSixHundredTicksDoesNotConsumeGatheringWait()
        {
            var own = new[] { Army(1, 3, 0) }; var m = new ArmyDecisionMemory[1]; var s = Gathering(1);
            Update(s, own, m, 900, 100); Assert.That(s.Phase, Is.EqualTo(OffensivePhase.Gathering)); Assert.That(s.GatheredTick, Is.EqualTo(-1));
        }
        [Test]
        public void DeadlineRemovesOnlyNonArrivalsAndTimeAloneNeverCompletesGathering()
        {
            var own = new[] { Army(1, 3, 60), Army(2, 3, 0) }; var m = new ArmyDecisionMemory[2]; var s = Gathering(1, 2);
            Update(s, own, m, 1200, 100); Assert.That(s.Phase, Is.EqualTo(OffensivePhase.Gathering));
            Update(s, own, m, 1201, 100); Assert.That(s.PlannedArmyIds, Is.EqualTo(new uint[] { 1 })); Assert.That(s.GatheredTick, Is.EqualTo(1201));
            var empty = Gathering(2); var one = new[] { Army(2, 3, 0) };
            Update(empty, one, new ArmyDecisionMemory[1], 1201, 100); Assert.That(empty.Phase, Is.EqualTo(OffensivePhase.Idle)); Assert.That(empty.GatheredTick, Is.EqualTo(-1));
        }
        [Test]
        public void GatheringRequiresMajorityInEveryArmyAndAdvanceCountsSoldiers()
        {
            var own = new[] { new OffenseArmyInput(1, new[] { P(60), P(60), P(0) }, 1, false, false), Army(2, 2, 0) };
            var m = new ArmyDecisionMemory[2]; var s = Gathering(1, 2);
            Update(s, own, m, 21, 4); Assert.That(s.Phase, Is.EqualTo(OffensivePhase.Gathering));
            s.PlannedArmyIds = new uint[] { 1 }; Update(s, own, m, 22, 5); Assert.That(s.Phase, Is.EqualTo(OffensivePhase.WaitingToAdvance));
            Update(s, own, m, 40, 4, true); Assert.That(s.Phase, Is.EqualTo(OffensivePhase.Advancing)); Assert.That(s.AdvancingArmyIds, Is.EqualTo(new uint[] { 1 }));
        }
        [TestCase(false, false, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        public void ReserveCommitsOnlyIfSufficientAndCoreSafeAndNotHuman(bool coreEnemy, bool human, bool commits)
        {
            var own = new[] { Army(1, 2, 60), Army(2, 3, 0) }; var m = new ArmyDecisionMemory[2]; m[1].Assignment = AssignmentKind.Reserve;
            var s = Gathering(1); var o = O(20, 8, coreEnemy ? P(0) : P(100), coreEnemy);
            Update(s, own, m, 20, observation: o, human: human);
            Assert.That(s.CommittedReserveArmyIds.Contains(2U), Is.EqualTo(commits));
            if (commits) { Assert.That(s.JoiningArmyIds, Does.Contain(2U)); Assert.That(m[1].Goal.Point, Is.EqualTo(P(60))); }
        }
        [Test]
        public void InsufficientReserveStaysHomeAndCommitmentEndsWithOffense()
        {
            var own = new[] { Army(1, 2, 60), Army(2, 3, 0) }; var m = new ArmyDecisionMemory[2]; m[1].Assignment = AssignmentKind.Reserve; var s = Gathering(1);
            Update(s, own, m, 20, 20); Assert.That(s.CommittedReserveArmyIds, Is.Empty);
            Update(s, own, m, 40, 8, true); Assert.That(s.CommittedReserveArmyIds, Does.Contain(2U));
            var inputs = Inputs(own, m);
            var kept = PolicyDecision.Allocate(O(), 40, inputs, 800, Array.Empty<uint>(), Array.Empty<AttackMemory>(), Array.Empty<PolicyOrder>(), out _, null, s.CommittedReserveArmyIds, true);
            Assert.That(kept[1].Assignment, Is.EqualTo(AssignmentKind.Advance));
            OffenseDecision.End(s, 41, false);
            var released = PolicyDecision.Allocate(O(), 60, inputs, 800, Array.Empty<uint>(), Array.Empty<AttackMemory>(), Array.Empty<PolicyOrder>(), out _, null, s.CommittedReserveArmyIds, true);
            Assert.That(released[1].Assignment, Is.EqualTo(AssignmentKind.Reserve));
        }
        [Test]
        public void PerArmyStreakResetsAndFortiethTickRetreatsEntireSquadSimultaneously()
        {
            var own = new[] { Army(1, 2, 60), Army(2, 2, 160) }; var m = new ArmyDecisionMemory[2]; var s = Gathering(1, 2); s.Phase = OffensivePhase.Advancing; s.AdvancingArmyIds = new uint[] { 1, 2 };
            m[1].InferiorTicks = 10;
            for (int t = 1; t <= 40; t++) {
                OffenseDecision.Retreat(O(t, 5, P(60)), t, s, Inputs(own, m), own, m, Route(own));
                Assert.That(m[1].InferiorTicks, Is.Zero);
                Assert.That(m.All(a => a.Returning), Is.EqualTo(t == 40));
            }
            Assert.That(m[0].InferiorTicks, Is.EqualTo(40)); Assert.That(s.SuppressedGoals.Single().UntilTick, Is.EqualTo(640));
        }
        [Test]
        public void LocalFriendlyCountUsesNearbySoldiersOfSameAdvanceSquad()
        {
            var own = new[] { Army(1, 2, 60), new OffenseArmyInput(2, new[] { P(60), P(150), P(150) }, 1, false, false) };
            var s = Gathering(1, 2); s.AdvancingArmyIds = new uint[] { 1, 2 }; var m = new ArmyDecisionMemory[2];
            OffenseDecision.Retreat(O(1, 7, P(60)), 1, s, Inputs(own), own, m, Route(own)); Assert.That(m[0].InferiorTicks, Is.EqualTo(1));
            OffenseDecision.Retreat(O(2, 6, P(60)), 2, s, Inputs(own), own, m, Route(own)); Assert.That(m[0].InferiorTicks, Is.Zero);
        }
        [TestCase(4, false)]
        [TestCase(5, true)]
        public void LossRetreatIsIndividualThenReevaluatesRemainingSquad(int enemies, bool all)
        {
            var own = new[] { Army(1, 2, 60, true), Army(2, 2, 60) }; var m = new ArmyDecisionMemory[2]; var s = Gathering(1, 2); s.Phase = OffensivePhase.Advancing; s.AdvancingArmyIds = new uint[] { 1, 2 };
            OffenseDecision.Retreat(O(20, enemies), 20, s, Inputs(own), own, m, Route(own));
            Assert.That(m[0].Returning, Is.True); Assert.That(m[1].Returning, Is.EqualTo(all));
            Assert.That(s.AdvancingArmyIds, Is.EqualTo(all ? Array.Empty<uint>() : new uint[] { 2 }));
        }
        [Test]
        public void JoiningDoesNotExtendDeadlineAndMustPassRallyBeforeJoiningAdvance()
        {
            var own = new[] { Army(1, 4, 100), Army(2, 4, 0) }; var m = new ArmyDecisionMemory[2]; var s = Gathering(1); s.Phase = OffensivePhase.Advancing; s.AdvancingArmyIds = new uint[] { 1 };
            Update(s, own, m, 21); Assert.That(s.JoiningArmyIds, Does.Contain(2U)); Assert.That(m[1].Goal.Point, Is.EqualTo(P(60)));
            own[1] = Army(2, 4, 60); Update(s, own, m, 22); Assert.That(s.AdvancingArmyIds, Is.EqualTo(new uint[] { 1 }));
            own[1] = Army(2, 4, 80); Update(s, own, m, 23); Assert.That(s.AdvancingArmyIds, Is.EqualTo(new uint[] { 1, 2 })); Assert.That(s.MoveDeadlineTick, Is.EqualTo(1200));
        }
        [Test]
        public void OwnershipReleaseIsEveryTickAndNewTargetWaitsForAllocation()
        {
            var own = new[] { Army(1, 4, 60) }; var m = new ArmyDecisionMemory[1]; var s = Gathering(1);
            var goals = Objectives; goals[2] = new KnownObjective(GoalKind.Outpost, 1, P(100), true, 1, false, 0, 21);
            var o = new FactionObservation(1, 21, Array.Empty<OwnArmyView>(), Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(), goals);
            Update(s, own, m, 21, observation: o); Assert.That(s.Phase, Is.EqualTo(OffensivePhase.Idle)); Assert.That(s.ReleasedTick, Is.EqualTo(21));
            Update(s, own, m, 22); Assert.That(s.Phase, Is.EqualTo(OffensivePhase.Idle));
            Update(s, own, m, 40, allocation: true); Assert.That(s.Phase, Is.Not.EqualTo(OffensivePhase.Idle));
        }
        [Test]
        public void SameTargetAfterMinimumPreservesIdentityAndTimersAndSuppressionWaits()
        {
            var own = new[] { Army(1, 4, 60) }; var m = new ArmyDecisionMemory[1]; var s = Gathering(1); s.Phase = OffensivePhase.Advancing; s.AdvancingArmyIds = new uint[] { 1 };
            Update(s, own, m, 620, allocation: true); Assert.That(s.Id, Is.EqualTo(1)); Assert.That(s.StartedTick, Is.EqualTo(20));
            OffenseDecision.End(s, 621, true); Update(s, own, m, 640, allocation: true); Assert.That(s.Phase, Is.EqualTo(OffensivePhase.Idle)); Assert.That(m[0].Goal, Is.EqualTo(Home));
            Update(s, own, m, 1240, allocation: true); Assert.That(s.Id, Is.EqualTo(2));
        }

        [Test]
        public void DiversionReassemblesRemainingArmiesAndCommittedReserveReturnsForCoreDefense()
        {
            var own = new[] { Army(1, 2, 60), Army(2, 4, 60) }; var m = new ArmyDecisionMemory[2];
            var s = Gathering(); s.Phase = OffensivePhase.Advancing; s.AdvancingArmyIds = new uint[] { 1, 2 }; s.CommittedReserveArmyIds = new uint[] { 2 };
            m[1].Assignment = AssignmentKind.Guard;
            Update(s, own, m, 40, 8);
            Assert.That(s.Phase, Is.EqualTo(OffensivePhase.Gathering).Or.EqualTo(OffensivePhase.WaitingToAdvance));
            Assert.That(s.AdvancingArmyIds, Is.Empty); Assert.That(s.CommittedReserveArmyIds, Does.Contain(2U));
            var defended = PolicyDecision.Allocate(O(60, 1, P(0), true), 60, Inputs(own, m), 0, Array.Empty<uint>(), Array.Empty<AttackMemory>(), Array.Empty<PolicyOrder>(), out _, null, s.CommittedReserveArmyIds, true);
            Assert.That(defended[1].Assignment, Is.EqualTo(AssignmentKind.CoreDefense));
            Update(s, own, defended, 60, observation: O(60, 1, P(0), true));
            Assert.That(s.CommittedReserveArmyIds, Is.Empty);
        }
        [Test]
        public void ReinforcementAndTargetUpdateNeverResetLossBaseline()
        {
            var sim = new Rts.Simulation.Simulation(WeekTwoScenario.Create());
            string[] Baseline() => Rts.Replay.DiagnosticComparison.Fields(sim.CaptureDiagnostic()).Where(f => f.Key.StartsWith("Ai.Armies[1].StartIds")).Select(f => f.Key + "=" + f.Value).ToArray();
            var before = Baseline(); Assert.That(before, Is.Not.Empty);
            for (int tick = 1; tick <= 100; tick++) sim.Step(tick, Array.Empty<ScheduledInput>());
            Assert.That(Baseline(), Is.EqualTo(before));
            var own = new[] { new OffenseArmyInput(1, new[] { P(60), P(60), P(60), P(60), P(60) }, 1, false, false, 10, 3) };
            var m = new ArmyDecisionMemory[1]; var s = Gathering(1);
            Update(s, own, m, 20, allocation: true);
            OffenseDecision.Retreat(O(21), 21, s, Inputs(own, m), own, m, Route(own));
            Assert.That(m[0].Returning, Is.True, "Reinforcements do not dilute the initial-ID loss budget");
        }
        [Test]
        public void CanonicalStateIncludesEveryOffenseCollectionAndArmyCounter()
        {
            var sim = new Rts.Simulation.Simulation(WeekTwoScenario.Create()); sim.Step(1, Array.Empty<ScheduledInput>());
            var names = Rts.Replay.DiagnosticComparison.Fields(sim.CaptureDiagnostic()).Select(f => f.Key).ToArray();
            foreach (var suffix in new[] { "Id", "Goal.Kind", "Phase", "RallyPoint.X.Raw", "StartedTick", "MoveDeadlineTick", "GatheredTick", "MaintainedSinceTick", "ReleasedTick", "SuppressedCount", "Planned.Count", "Joining.Count", "Advancing.Count", "CommittedReserve.Count" })
                Assert.That(names, Does.Contain("Ai.Factions[1].Offense." + suffix));
            foreach (var suffix in new[] { "InferiorTicks", "InferiorSince", "StartIds.Count", "HoldUntilTick" }) Assert.That(names, Does.Contain("Ai.Armies[1]." + suffix));
        }
        [Test]
        public void ReturnArrivalHoldsSixtyTicksAndZeroAttackersPause()
        {
            var own = new[] { new OffenseArmyInput(1, new[] { P(0) }, 1, false, true) }; var m = new[] { new ArmyDecisionMemory { Returning = true } }; var s = Gathering(1);
            OffenseDecision.Retreat(O(50), 50, s, Inputs(own, m), own, m, Route(own));
            Assert.That(m[0].Returning, Is.False); Assert.That(m[0].HoldUntilTick, Is.EqualTo(110));
            Update(s, own, m, 50); Assert.That(s.Phase, Is.EqualTo(OffensivePhase.Idle));
        }
        [Test]
        public void HumanCommandAndUnreachableTargetReleaseWithoutImmediateReselection()
        {
            var own = new[] { Army(1, 4, 60) }; var m = new ArmyDecisionMemory[1]; var s = Gathering(1); var a = Inputs(own)[0];
            var human = new ArmyDecisionInput(a.Army, new PolicyView(1, CommandSource.Human, PolicyKind.Focus, Goal, default, 0), false, default, a.Routes);
            OffenseDecision.Update(O(21), 21, s, new[] { human }, own, m, new[] { Route(own) }, false, false, Home);
            Assert.That(s.Phase, Is.EqualTo(OffensivePhase.Idle));
            s = Gathering(1); OffenseDecision.Update(O(40), 40, s, Inputs(own), own, m, Array.Empty<OffenseRouteInput>(), true, false, Home);
            Assert.That(s.Phase, Is.EqualTo(OffensivePhase.Idle)); Assert.That(s.SuppressedGoals, Is.Empty);
        }
        [Test]
        public void TargetSelectionUsesUnionThreatBeforeMaximumRallyDistance()
        {
            var own = new[] { Army(1, 4, 0), Army(2, 4, 0) }; var m = new ArmyDecisionMemory[2];
            var goals = Objectives.Concat(new[] { new KnownObjective(GoalKind.Outpost, 2, P(100, 100), true, 0, false, 0, 20) }).ToArray();
            var o = new FactionObservation(1, 20, Array.Empty<OwnArmyView>(), Array.Empty<VisibleEnemy>(), new[] { new EnemyContact(1, P(40), 20, 20, 20, false) }, goals);
            var second = new OffenseRouteInput(new PolicyGoal(GoalKind.Outpost, 2, default), P(60, 100), new[] { P(60, 100), P(100, 100) }, own.Select(a => new ObjectiveRoute(new PolicyGoal(GoalKind.Point, a.ArmyId, default), 1, new[] { P(0), P(0, 100), P(60, 100) })).ToArray());
            var s = new FactionOffenseMemory(); OffenseDecision.Update(o, 20, s, Inputs(own), own, m, new[] { Route(own), second }, true, false, Home);
            Assert.That(s.Goal.Id, Is.EqualTo(2), "The longer but safer union wins");
        }
        [Test]
        public void DifferentHiddenWorldsWithIdenticalObservationHaveIdenticalOffense()
        {
            var a = WeekTwoScenario.Create(); var b = WeekTwoScenario.Create(); b.Soldiers[20].Position = P(230, 96); b.Soldiers[20].Hp = 50;
            var left = new Rts.Simulation.Simulation(a); var right = new Rts.Simulation.Simulation(b);
            Assert.That(left.CaptureDiagnostic().CanonicalState, Is.Not.EqualTo(right.CaptureDiagnostic().CanonicalState));
            for (int tick = 1; tick <= 60; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                var x = Rts.Replay.DiagnosticComparison.Fields(left.CaptureDiagnostic()).Where(f => f.Key.StartsWith("Ai.Factions[1].Offense.")).Select(f => f.Key + "=" + f.Value).ToArray();
                var y = Rts.Replay.DiagnosticComparison.Fields(right.CaptureDiagnostic()).Where(f => f.Key.StartsWith("Ai.Factions[1].Offense.")).Select(f => f.Key + "=" + f.Value).ToArray();
                Assert.That(x, Is.EqualTo(y), "tick " + tick);
                Assert.That(left.Capture(1).Result.IsFault || right.Capture(1).Result.IsFault, Is.False);
            }

        }
    }
}
