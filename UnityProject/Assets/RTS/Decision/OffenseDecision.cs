using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Contracts;

namespace Rts.Decision
{
    public static class OffenseDecision
    {
        private static bool Same(PolicyGoal a, PolicyGoal b) => a.Kind == b.Kind && a.Id == b.Id;
        public static long Length(IReadOnlyList<SimPoint> cells)
        {
            long distance = 0;
            for (int i = 1; i < cells.Count; i++) distance = checked(distance + (long)FixMath.IntegerSqrt(PolicyDecision.Distance(cells[i - 1], cells[i])));
            return distance;
        }
        public static SimPoint Rally(FactionObservation o, IReadOnlyList<SimPoint> corePath)
        {
            if (!TryRally(o, corePath, out var rally)) throw new ArgumentException("No safe reachable rally cell", nameof(corePath));
            return rally;
        }
        public static bool TryRally(FactionObservation o, IReadOnlyList<SimPoint> corePath, out SimPoint rally)
        {
            rally = default;
            if (corePath.Count == 0) return false;
            int cursor = corePath.Count - 1; long distance = 0;
            while (cursor > 0 && distance < Fix64.FromInt(40).Raw)
            { distance += (long)FixMath.IntegerSqrt(PolicyDecision.Distance(corePath[cursor], corePath[cursor - 1])); cursor--; }
            while (cursor > 0 && o.VisibleEnemies.Any(e => PolicyDecision.Within(e.Position, corePath[cursor], 24))) cursor--;
            rally = corePath[cursor];
            var selected = rally;
            return !o.VisibleEnemies.Any(e => PolicyDecision.Within(e.Position, selected, 24));
        }
        public static int Estimate(FactionObservation o, OffenseRouteInput route, IEnumerable<uint> armies)
        {
            var ids = new HashSet<uint>(armies);
            var paths = route.ToRally.Where(r => ids.Contains(r.Goal.Id)).Select(r => r.Cells).Concat(new[] { route.ToTarget }).ToArray();
            int count = PolicyDecision.CountableContacts(o)
                .Where(c => paths.Any(p => PolicyDecision.NearRoute(c.LastPosition, p, 24)))
                .Sum(c => c.EstimateMax < 0 || o.Tick - c.LastSeenTick >= 600 ? 10 : c.EstimateMax);
            var goal = o.Objectives.FirstOrDefault(g => Same(new PolicyGoal(g.Kind, g.Id, default), route.Goal));
            bool unknown = (goal.Kind == GoalKind.Outpost ? !goal.IsOwnerKnown : !goal.IsHpKnown) || o.Tick - goal.LastSeenTick >= 600;
            if (unknown && !o.Contacts.Any(c => PolicyDecision.Within(c.LastPosition, goal.Position, 24))) count += 10;
            return count;
        }
        private static int Near(OffenseArmyInput a, SimPoint point, int radius) => a.Soldiers.Count(p => PolicyDecision.Within(p, point, radius));
        private static bool Arrived(OffenseArmyInput a, SimPoint point) => a.Soldiers.Count > 0 && Near(a, point, 12) * 2 > a.Soldiers.Count;
        private static uint[] Ordered(IEnumerable<uint> ids) => ids.Distinct().OrderBy(id => id).ToArray();
        public static PolicyGoal WaitGoal(FactionObservation o, IReadOnlyList<OffenseRouteInput> routes)
            => routes.Where(r => r.Goal.Kind == GoalKind.Outpost && o.Objectives.Any(g => g.Kind == GoalKind.Outpost && g.Id == r.Goal.Id && g.IsOwnerKnown && g.OwnerFactionId == o.FactionId))
                .OrderBy(r => r.HomeDistanceRaw).ThenBy(r => r.Goal.Id).Select(r => r.Goal).DefaultIfEmpty(PolicyDecision.Core(o, true)).First();
        public static void End(FactionOffenseMemory s, long tick, bool suppress)
        {
            if (suppress && s.Goal.Kind != GoalKind.None)
                s.SuppressedGoals = s.SuppressedGoals.Where(g => !Same(g.Goal, s.Goal)).Concat(new[] { new SuppressedGoalMemory { Goal = s.Goal, UntilTick = checked(tick + 600) } }).OrderBy(g => g.Goal.Kind).ThenBy(g => g.Goal.Id).ToArray();
            s.ReleasedTick = tick; s.Phase = OffensivePhase.Idle; s.Goal = default;
            s.PlannedArmyIds = s.JoiningArmyIds = s.AdvancingArmyIds = s.CommittedReserveArmyIds = Array.Empty<uint>();
        }
        // All retreat predicates are frozen before any army's result is changed.
        public static void Retreat(FactionObservation o, long tick, FactionOffenseMemory s,
            IReadOnlyList<ArmyDecisionInput> inputs, IReadOnlyList<OffenseArmyInput> own, ArmyDecisionMemory[] result, OffenseRouteInput route)
        {
            var advance = new HashSet<uint>(s.AdvancingArmyIds);
            var losses = new HashSet<uint>(); bool whole = false;
            for (int i = 0; i < inputs.Count; i++)
            {
                var a = own[i]; var m = result[i];
                if (m.Returning)
                {
                    if (a.HomeArrived) { m.Returning = false; m.HoldUntilTick = tick + 60; m.InferiorTicks = 0; m.InferiorSince = 0; m.Assignment = AssignmentKind.Reserve; m.Goal = PolicyDecision.Core(o, true); }
                }
                else if (inputs[i].Policy.CommandId != 0 || tick < m.HoldUntilTick || a.Soldiers.Count == 0) { m.InferiorTicks = 0; m.InferiorSince = 0; }
                else
                {
                    var center = a.Soldiers[0];
                    int count = own.Where(x => x.ArmyId == a.ArmyId || advance.Contains(a.ArmyId) && advance.Contains(x.ArmyId)).Sum(x => Near(x, center, 24));
                    bool inferior = count * 2L < PolicyDecision.Estimate(o, center);
                    m.InferiorTicks = inferior ? checked(m.InferiorTicks + 1) : 0;
                    m.InferiorSince = inferior ? (m.InferiorTicks == 1 ? tick : m.InferiorSince) : 0;
                    if (a.LossReached || a.BaselineCount > 0 && 1000L * a.LostCount >= 300L * a.BaselineCount) losses.Add(a.ArmyId);
                    if (m.InferiorTicks >= 40) { if (advance.Contains(a.ArmyId)) whole = true; else losses.Add(a.ArmyId); }
                }
                result[i] = m;
            }
            var remaining = advance.Except(losses).ToArray();
            if (losses.Overlaps(advance) && remaining.Length > 0 && (route == null || own.Where(a => remaining.Contains(a.ArmyId)).Sum(a => Near(a, s.RallyPoint, 24)) * 2L < Estimate(o, route, remaining))) whole = true;
            for (int i = 0; i < inputs.Count; i++)
                if (losses.Contains(inputs[i].Army.Id) || whole && advance.Contains(inputs[i].Army.Id)) result[i].Returning = true;
            s.AdvancingArmyIds = Ordered(remaining);
            if (whole || advance.Count > 0 && remaining.Length == 0) End(s, tick, true);
        }
        private static bool Eligible(ArmyDecisionInput a, ArmyDecisionMemory m, long tick) => a.Army.Kind == UnitKind.Infantry && a.Army.AliveCount > 0 && a.Policy.Kind == 0 && !m.Returning && tick >= m.HoldUntilTick && m.Assignment == AssignmentKind.Advance;
        private static bool Reachable(OffenseRouteInput r, IEnumerable<uint> ids) => r != null && r.ToTarget.Count > 0 && ids.All(id => r.ToRally.Any(p => p.Goal.Id == id && p.Cells.Count > 0));
        private static long MaxDistance(OffenseRouteInput r, IEnumerable<uint> ids) => ids.Select(id => Length(r.ToRally.First(p => p.Goal.Id == id).Cells)).DefaultIfEmpty(0).Max();
        private static void Start(FactionOffenseMemory s, OffenseRouteInput r, uint[] ids, IReadOnlyList<OffenseArmyInput> own, long tick, bool newTarget)
        {
            if (newTarget) { s.Id++; s.Goal = r.Goal; s.MaintainedSinceTick = tick; s.CommittedReserveArmyIds = Array.Empty<uint>(); }
            s.RallyPoint = r.Rally; s.Phase = OffensivePhase.Gathering; s.StartedTick = tick; s.GatheredTick = -1;
            s.PlannedArmyIds = ids; s.JoiningArmyIds = s.AdvancingArmyIds = Array.Empty<uint>();
            s.MoveDeadlineTick = checked(tick + ids.Select(id => {
                long step = own.First(a => a.ArmyId == id).StepRaw, distance = Length(r.ToRally.First(p => p.Goal.Id == id).Cells);
                return step <= 0 ? 0 : (distance + step - 1) / step;
            }).DefaultIfEmpty(0).Max() + 200);
        }
        public static void Update(FactionObservation o, long tick, FactionOffenseMemory s,
            IReadOnlyList<ArmyDecisionInput> inputs, IReadOnlyList<OffenseArmyInput> own,
            ArmyDecisionMemory[] result, IReadOnlyList<OffenseRouteInput> routes, bool allocation, bool humanReserve, PolicyGoal waitGoal)
        {
            s.SuppressedGoals = s.SuppressedGoals.Where(g => tick < g.UntilTick).ToArray();
            uint[] ids = Ordered(Enumerable.Range(0, inputs.Count).Where(i => Eligible(inputs[i], result[i], tick)).Select(i => inputs[i].Army.Id));
            var route = routes.FirstOrDefault(r => Same(r.Goal, s.Goal));
            bool released = false;
            if (s.Phase != OffensivePhase.Idle)
            {
                bool invalid = o.Objectives.Any(g => g.Kind == GoalKind.Outpost && Same(new PolicyGoal(g.Kind, g.Id, default), s.Goal) && g.IsOwnerKnown && g.OwnerFactionId == o.FactionId)
                    || !Reachable(route, ids) || inputs.Any(a => (s.PlannedArmyIds.Contains(a.Army.Id) || s.JoiningArmyIds.Contains(a.Army.Id) || s.AdvancingArmyIds.Contains(a.Army.Id)) && a.Policy.CommandId != 0);
                bool failed = s.Phase == OffensivePhase.WaitingToAdvance && tick - s.GatheredTick >= 600;
                if (invalid || failed || ids.Length == 0) { End(s, tick, failed); released = true; }
            }
            if (allocation && !released && s.ReleasedTick != tick && ids.Length > 0 && (s.Phase == OffensivePhase.Idle || tick - s.MaintainedSinceTick >= 600))
            {
                bool coreAllowed = o.Objectives.Any(g => g.Kind == GoalKind.Outpost && g.IsOwnerKnown && g.OwnerFactionId == o.FactionId);
                var choice = routes.Where(r => !s.SuppressedGoals.Any(g => Same(g.Goal, r.Goal)) && Reachable(r, ids))
                    .Where(r => r.Goal.Kind == GoalKind.Core ? coreAllowed && !Same(r.Goal, PolicyDecision.Core(o, true)) : o.Objectives.Any(g => g.Kind == GoalKind.Outpost && g.Id == r.Goal.Id && (!g.IsOwnerKnown || g.OwnerFactionId != o.FactionId)))
                    .OrderBy(r => Estimate(o, r, ids) * 1000L / own.Where(a => ids.Contains(a.ArmyId)).Sum(a => a.Soldiers.Count))
                    .ThenBy(r => MaxDistance(r, ids)).ThenBy(r => r.Goal.Kind == GoalKind.Outpost ? 0 : 1).ThenBy(r => r.Goal.Id).FirstOrDefault();
                if (choice != null && (s.Phase == OffensivePhase.Idle || !Same(choice.Goal, s.Goal))) { Start(s, choice, ids, own, tick, true); route = choice; }
            }
            if (s.Phase != OffensivePhase.Idle)
            {
                var previousAdvance = s.AdvancingArmyIds;
                s.PlannedArmyIds = Ordered(s.PlannedArmyIds.Intersect(ids)); s.AdvancingArmyIds = Ordered(s.AdvancingArmyIds.Intersect(ids));
                s.CommittedReserveArmyIds = Ordered(s.CommittedReserveArmyIds.Where(id => !Enumerable.Range(0, inputs.Count).Any(i => inputs[i].Army.Id == id && result[i].Assignment == AssignmentKind.CoreDefense)));
                s.JoiningArmyIds = Ordered(s.JoiningArmyIds.Intersect(ids).Concat(ids.Except(s.PlannedArmyIds).Except(s.AdvancingArmyIds)));
                if (previousAdvance.Except(ids).Any() && own.Where(a => s.AdvancingArmyIds.Contains(a.ArmyId)).Sum(a => Near(a, s.RallyPoint, 24)) * 2L < Estimate(o, route, s.AdvancingArmyIds))
                    Start(s, route, ids, own, tick, false);
                // Joining armies must spend at least one judgement as arrivals before being counted.
                var readyJoin = s.JoiningArmyIds.Where(id => own.First(a => a.ArmyId == id).Soldiers.Count > 0 &&
                    Arrived(own.First(a => a.ArmyId == id), s.RallyPoint)).ToArray();
                if (s.Phase == OffensivePhase.Gathering)
                {
                    if (tick > s.MoveDeadlineTick) s.PlannedArmyIds = s.PlannedArmyIds.Where(id => Arrived(own.First(a => a.ArmyId == id), s.RallyPoint)).ToArray();
                    if (s.PlannedArmyIds.Count == 0 && tick > s.MoveDeadlineTick) End(s, tick, true);
                    if (s.PlannedArmyIds.Count > 0 && s.PlannedArmyIds.All(id => Arrived(own.First(a => a.ArmyId == id), s.RallyPoint)))
                    { s.Phase = OffensivePhase.WaitingToAdvance; s.GatheredTick = tick; }
                }
                if (s.Phase == OffensivePhase.WaitingToAdvance && (allocation || tick == s.GatheredTick))
                {
                    int near = own.Where(a => ids.Contains(a.ArmyId) && !s.JoiningArmyIds.Contains(a.ArmyId)).Sum(a => Near(a, s.RallyPoint, 24));
                    int enemies = Estimate(o, route, ids);
                    if (near > 0 && near * 2L >= enemies)
                    { s.Phase = OffensivePhase.Advancing; s.AdvancingArmyIds = Ordered(own.Where(a => ids.Contains(a.ArmyId) && !s.JoiningArmyIds.Contains(a.ArmyId) && Near(a, s.RallyPoint, 24) > 0).Select(a => a.ArmyId)); s.PlannedArmyIds = Ordered(s.PlannedArmyIds.Except(s.AdvancingArmyIds)); }
                    else if (!humanReserve && !o.VisibleEnemies.Any(e => PolicyDecision.Within(e.Position, PolicyDecision.Position(o, PolicyDecision.Core(o, true)), 24)))
                    {
                        var reserves = Enumerable.Range(0, inputs.Count).Where(i => inputs[i].Policy.Kind == 0 && inputs[i].Army.Kind == UnitKind.Infantry && !result[i].Returning && tick >= result[i].HoldUntilTick && result[i].Assignment == AssignmentKind.Reserve && own[i].Soldiers.Count > 0 && Reachable(route, new[] { inputs[i].Army.Id })).ToArray();
                        var committed = new List<int>(); int potential = near;
                        foreach (int i in reserves) { committed.Add(i); potential += own[i].Soldiers.Count; if (potential * 2L >= Estimate(o, route, ids.Concat(committed.Select(j => inputs[j].Army.Id)))) break; }
                        if (committed.Count > 0 && potential * 2L >= Estimate(o, route, ids.Concat(committed.Select(j => inputs[j].Army.Id))))
                        {
                            foreach (int i in committed) { result[i].Assignment = AssignmentKind.Advance; result[i].Goal = new PolicyGoal(GoalKind.Point, 0, s.RallyPoint); }
                            var added = committed.Select(i => inputs[i].Army.Id);
                            s.CommittedReserveArmyIds = Ordered(s.CommittedReserveArmyIds.Concat(added)); s.JoiningArmyIds = Ordered(s.JoiningArmyIds.Concat(added)); ids = Ordered(ids.Concat(added));
                        }
                    }
                }
                if (s.Phase == OffensivePhase.Advancing)
                {
                    var joined = s.PlannedArmyIds.Where(id => !s.AdvancingArmyIds.Contains(id) && own.First(a => a.ArmyId == id).Soldiers.Any(p => own.Where(b => s.AdvancingArmyIds.Contains(b.ArmyId)).Any(b => b.Soldiers.Any(q => PolicyDecision.Within(p, q, 24))))).ToArray();
                    s.AdvancingArmyIds = Ordered(s.AdvancingArmyIds.Concat(joined));
                    s.PlannedArmyIds = Ordered(s.PlannedArmyIds.Except(s.AdvancingArmyIds));
                }
                if (s.Phase != OffensivePhase.Idle)
                { s.PlannedArmyIds = Ordered(s.PlannedArmyIds.Concat(readyJoin)); s.JoiningArmyIds = Ordered(s.JoiningArmyIds.Except(readyJoin)); }
            }
            for (int i = 0; i < inputs.Count; i++)
                if (ids.Contains(inputs[i].Army.Id))
                {
                    result[i].Assignment = AssignmentKind.Advance;
                    result[i].Goal = s.Phase == OffensivePhase.Idle ? waitGoal : s.Phase == OffensivePhase.Advancing && (s.AdvancingArmyIds.Contains(inputs[i].Army.Id) || s.PlannedArmyIds.Contains(inputs[i].Army.Id)) ? s.Goal : new PolicyGoal(GoalKind.Point, 0, s.RallyPoint);
                }
        }
    }
}
