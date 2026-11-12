using System;
using System.Linq;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        // Ten seconds is enough for a normal slot correction; a late unit then follows the advancing formation.
        private const long FormationWaitTicks = 200;
        private bool ArmyFlowOn => world.Config.Economy.Enabled && world.Config.Economy.ArmyFlow;
        private bool ArmyFinalApproachOn => world.Config.Economy.Enabled && world.Config.Economy.ArmyFinalApproach;
        private bool HumanOrderFlowOn => world.Config.Economy.Enabled && world.Config.Economy.HumanOrderFlow;
        public bool HumanOrderFlowEnabled => HumanOrderFlowOn;
        // 45: diagnostics only (tactic-match output); never saved, hashed or read by a decision.
        private readonly int[] armyFinalApproachSkippedJoins = new int[2];
        internal int ArmyFinalApproachSkippedJoins(uint faction) => armyFinalApproachSkippedJoins[faction - 1];

        private SimPoint GoalPosition(PolicyGoal goal) => goal.Kind == GoalKind.Point ? goal.Point
            : goal.Kind == GoalKind.Outpost ? world.Outposts[goal.Id - 1].Definition.Position
            : world.Cores[goal.Id - 1].Definition.Position;

        private SimPoint ArmyGoal(ArmyState a)
        {
            uint faction = a.Definition.FactionId;
            var own = world.Cores[world.Factions[faction - 1].CoreId - 1].Definition.Position;
            // A player's right-click comes first: the automatic decision may still mark the army as returning to the
            // core after losses, which kept a selected army standing by the castle (10-17 test play).
            var command = a.CommandId == 0 ? null : CommandById(a.CommandId);
            if (world.Config.Economy.DirectPathMovement && a.Policy == PolicyKind.Focus && command != null
                && command.Order.Source == CommandSource.Human)
                return GoalPosition(a.Goal);
            if (IsPushWaveStagingPathArmy(a.Definition.Id)) return pushWaveStates[faction - 1].StagingPoint;
            if (a.Policy == PolicyKind.Retreat) return a.Goal.Kind == GoalKind.None ? own : GoalPosition(a.Goal);
            if (a.Decision.Returning || a.Policy == 0 && world.Tick < a.Decision.HoldUntilTick) return own;
            if (a.ExplorePatrolActive) return a.ExploreHasGoal ? a.ExploreGoal : own;
            if (a.Policy == PolicyKind.Defend || a.Policy == PolicyKind.Scout) return GoalPosition(a.Goal);
            if (a.Decision.Assignment != AssignmentKind.Advance && a.Decision.Goal.Kind != GoalKind.None) return GoalPosition(a.Decision.Goal);
            if (a.Policy == PolicyKind.Focus) return GoalPosition(a.Goal);
            if (a.Decision.Goal.Kind != GoalKind.None) return GoalPosition(a.Decision.Goal);
            return world.Cores[world.Factions[2 - faction].CoreId - 1].Definition.Position;
        }

        private void PrepareArmyPaths()
        {
            foreach (int index in world.ArmyTraversal)
            {
                ref var a = ref world.Armies[index];
                var goal = ArmyGoal(a);
                var command = a.CommandId == 0 ? null : CommandById(a.CommandId);
                bool humanFocus = a.Policy == PolicyKind.Focus && command != null && command.Order.Source == CommandSource.Human;
                int first = -1;
                foreach (uint id in a.SoldierIds)
                {
                    if (!world.Soldiers[id - 1].Alive) continue;
                    if (first < 0 || HumanOrderFlowOn && humanFocus && (DistanceSquared(world.Soldiers[id - 1].Position, goal)
                        < DistanceSquared(world.Soldiers[first].Position, goal)
                        || DistanceSquared(world.Soldiers[id - 1].Position, goal) == DistanceSquared(world.Soldiers[first].Position, goal)
                        && id < (uint)(first + 1))) first = (int)id - 1;
                }
                if (first < 0) continue;
                if (!a.HasPathGoal || !SamePoint(a.PathGoal, goal))
                {
                    a.PathGoal = goal; a.HasPathGoal = true; a.PathCursor = 0;
                    foreach (uint id in a.SoldierIds)
                    {
                        ref var reset = ref world.Soldiers[id - 1];
                        reset.Joining = false; reset.SlotWaitTimedOut = false;
                        reset.SlotWaitSinceTick = 0; reset.SlotWaitTarget = default;
                        reset.TacticalRoute = false; ReleaseLocalPath(ref reset);
                    }
                    int startCell = world.Map.Cell(world.Soldiers[first].Position);
                    var soldierIds = a.SoldierIds;
                    uint factionId = a.Definition.FactionId;
                    bool direct = world.Config.Economy.DirectPathMovement && humanFocus && soldierIds
                        .Where(id => world.Soldiers[id - 1].Alive)
                        .All(id => world.Soldiers[id - 1].Initial.Kind == UnitKind.Scout
                            && Clear(world.Soldiers[id - 1].Position, goal, factionId));
                    a.Path = direct ? new[] { startCell, world.Map.Cell(goal) }
                        : ArmyFlowOn ? world.Map.FindArmyFlowPath(startCell, goal, a.Definition.FactionId, world.Config.Economy.ArmyFlowLookahead)
                        : world.Map.FindPath(startCell, goal, a.Definition.FactionId);
                    a.BreakingPath = false;
                    a.BreachCell = -1;
                    var radius = a.Policy == PolicyKind.Focus && a.Goal.Kind == GoalKind.Outpost ? world.Config.Rules.CaptureRadius
                        : a.Policy == PolicyKind.Focus && a.Goal.Kind == GoalKind.Core ? world.Config.Rules.CoreRadius + world.Soldiers[first].Parameters.Range
                        : Fix64.FromInt(4);
                    uint buildingTarget = BuildingAttackOrderOn && a.Definition.Id <= buildingAttackTargets.Length
                        ? buildingAttackTargets[a.Definition.Id - 1] : 0;
                    bool reached = a.Path.Length != 0 && InRange(world.Map.Center(a.Path[a.Path.Length - 1]), goal, radius);
                    if (BuildingAttackOrderOn && buildingTarget != 0)
                        reached = a.Path.Length != 0 && BuildingTargetInRange(buildingTarget,
                            world.Map.Center(a.Path[a.Path.Length - 1]), world.Soldiers[first].Parameters.Range);
                    a.PathImpossible = !direct && !reached;
                    if (FortifyOn && a.PathImpossible)
                    {
                        int[] breachRoute = world.Map.FindBreakablePath(startCell, goal, a.Definition.FactionId);
                        int breachIndex = -1;
                        for (int r = 1; r < breachRoute.Length; r++)
                            if (IsBreakableForFaction(breachRoute[r], a.Definition.FactionId)) { breachIndex = r; break; }
                        if (breachIndex > 0)
                        {
                            a.Path = new int[breachIndex];
                            System.Array.Copy(breachRoute, a.Path, breachIndex);
                            a.PathImpossible = false;
                            a.BreakingPath = true;
                            a.BreachCell = breachRoute[breachIndex];
                            world.Economies[a.Definition.FactionId - 1].FortifyBreachUses = checked(world.Economies[a.Definition.FactionId - 1].FortifyBreachUses + 1);
                        }
                    }
                }
                if (ArmyFlowOn)
                {
                    PrepareArmyFlowFormation(ref a, goal);
                    continue;
                }
                bool arrived = true, participants = false;
                bool directAssault = IsLatePushAttack(a.Definition.Id);
                for (int i = 0; i < a.SoldierIds.Length; i++)
                {
                    ref var soldier = ref world.Soldiers[a.SoldierIds[i] - 1];
                    if (!soldier.Alive) continue;
                    var intended = soldier.MoveGoal;
                    bool splitScout = world.Config.Economy.LargeEconomy && a.ExplorePatrolAuto
                        && soldier.Initial.Kind == UnitKind.Scout && !soldier.Pursuit.Returning && soldier.ExploreHasGoal;
                    if (splitScout) intended = soldier.ExploreGoal;
                    if (!SamePoint(soldier.MoveGoal, intended)) soldier.MoveGoal = intended;
                    bool returning = !(world.Config.Economy.DirectPathMovement && humanFocus)
                        && (a.Policy == PolicyKind.Retreat || a.Decision.Returning);
                    bool atPosition = SamePoint(intended, soldier.Position);
                    if (atPosition && !returning) continue;
                    if (!SamePoint(intended, goal) && !(returning && atPosition))
                    {
                        // Tactical destinations can cross walls too. Reuse the route while its cell is unchanged.
                        if (!soldier.TacticalRoute || world.Map.Cell(soldier.LocalGoal) != world.Map.Cell(intended))
                        StartLocalRoute(ref soldier, intended);
                        soldier.LocalGoal = intended;
                        soldier.TacticalRoute = true; soldier.Joining = false;
                        FollowLocalRoute(ref soldier);
                        continue;
                    }
                    if (soldier.TacticalRoute)
                    { soldier.TacticalRoute = false; soldier.Joining = false; ReleaseLocalPath(ref soldier); }
                    if (a.PathImpossible) { soldier.MoveGoal = soldier.Position; continue; }
                    var center = world.Map.Center(a.Path[a.PathCursor]);
                    bool skipFinalApproachJoin = ArmyFinalApproachOn && a.PathCursor == a.Path.Length - 1
                        && !soldier.Joining && Clear(soldier.Position, goal, a.Definition.FactionId);
                    if (skipFinalApproachJoin)
                        armyFinalApproachSkippedJoins[a.Definition.FactionId - 1]++;
                    else
                    {
                        int joinCursor = HumanOrderFlowOn && humanFocus && !soldier.Joining ? HumanJoinCursor(a, soldier.Position) : a.PathCursor;
                        var joinCenter = world.Map.Center(a.Path[joinCursor]);
                        if (!soldier.Joining && (!InRange(soldier.Position, joinCenter, Fix64.FromInt(8)) || !Clear(soldier.Position, joinCenter, a.Definition.FactionId)))
                        {
                            soldier.Joining = true; soldier.JoinCursor = joinCursor;
                            StartLocalRoute(ref soldier, joinCenter);
                        }
                    }
                    if (soldier.Joining)
                    {
                        FollowLocalRoute(ref soldier);
                        if (!SamePoint(soldier.Position, soldier.LocalGoal)) continue;
                        if (soldier.JoinCursor < a.PathCursor)
                        {
                            soldier.JoinCursor++;
                            StartLocalRoute(ref soldier, world.Map.Center(a.Path[soldier.JoinCursor]));
                            FollowLocalRoute(ref soldier);
                            continue;
                        }
                        soldier.Joining = false; ReleaseLocalPath(ref soldier);
                    }
                    // Once a human scout was given a clear two-cell direct route, do not re-evaluate the line from the
                    // clipped position every tick.  Re-evaluation can reject the same route at a cell boundary and send
                    // the scout back to the route's start centre, producing a small loop around an obstacle corner.
                    bool directScoutPath = world.Config.Economy.DirectPathMovement && humanFocus
                        && soldier.Initial.Kind == UnitKind.Scout && !a.PathImpossible && a.Path.Length == 2;
                    if (directScoutPath)
                    {
                        participants = true;
                        soldier.Joining = false;
                        ReleaseLocalPath(ref soldier);
                        soldier.MoveGoal = goal;
                        if (!InRange(soldier.Position, goal, soldier.StepDistance)) arrived = false;
                        continue;
                    }
                    participants = true;
                    // Collapse at bends; keep slots only when the next center is reachable from the slot.
                    // Returning armies use the route centre instead of a one-metre formation offset, which can press
                    // a late scout against an obstacle while the rest of the army waits for its slot.
                    var target = returning || directAssault ? center : new SimPoint(center.X + Fix64.FromInt((a.Definition.FactionId == 1 ? 1 : -1) * (i % 4)), center.Z + Fix64.FromInt(i / 4));
                    if (!Clear(center, target, a.Definition.FactionId) || !Clear(soldier.Position, target, a.Definition.FactionId)
                        || a.PathCursor + 1 < a.Path.Length && !Clear(target, world.Map.Center(a.Path[a.PathCursor + 1]), a.Definition.FactionId)) target = center;
                    if (a.PathCursor == a.Path.Length - 1) target = Clear(soldier.Position, goal, a.Definition.FactionId) ? goal : center;
                    soldier.MoveGoal = target;
                    if (!SamePoint(soldier.SlotWaitTarget, target))
                    {
                        soldier.SlotWaitTarget = target; soldier.SlotWaitSinceTick = world.Tick; soldier.SlotWaitTimedOut = false;
                    }
                    var arrivalRadius = world.Config.Economy.DirectPathMovement ? soldier.StepDistance : Fix64.FromRatio(1, 10);
                    if (!InRange(soldier.Position, target, arrivalRadius))
                    {
                        if (!soldier.SlotWaitTimedOut && world.Tick - soldier.SlotWaitSinceTick >= FormationWaitTicks)
                            soldier.SlotWaitTimedOut = true;
                        if (!directAssault && !soldier.SlotWaitTimedOut) arrived = false;
                    }
                    else { soldier.SlotWaitSinceTick = 0; soldier.SlotWaitTimedOut = false; }
                }
                if (participants && arrived && a.PathCursor + 1 < a.Path.Length) a.PathCursor++;
            }
        }

        private bool Clear(SimPoint from, SimPoint to) => Clear(from, to, 0);

        private bool Clear(SimPoint from, SimPoint to, uint faction)
            => world.Map.IsPassableFor(world.Map.Cell(to), faction) && SamePoint(world.Map.ClipMove(from, to, faction), to);

        private void StartLocalRoute(ref SoldierState soldier, SimPoint goal)
        {
            soldier.LocalGoal = goal; soldier.LocalCursor = 0;
            ReleaseLocalPath(ref soldier);
            soldier.LocalPath = ArmyFlowOn
                ? world.Map.SharedArmyFlowRoute(world.Map.Cell(soldier.Position), goal, soldier.Initial.FactionId, world.Config.Economy.ArmyFlowLookahead)
                : world.Map.SharedRoute(world.Map.Cell(soldier.Position), goal, soldier.Initial.FactionId);
            if (soldier.LocalPath.Length == 0 && FortifyOn)
            {
                int[] breachRoute = world.Map.SharedBreakableRoute(world.Map.Cell(soldier.Position), goal, soldier.Initial.FactionId);
                soldier.LocalPath = TrimBreachRoute(breachRoute, soldier.Initial.FactionId);
            }
            if (soldier.LocalPath.Length > 1 && Clear(soldier.Position, world.Map.Center(soldier.LocalPath[1]), soldier.Initial.FactionId)) soldier.LocalCursor = 1;
        }

        private int[] TrimBreachRoute(int[] route, uint faction)
        {
            int breach = -1;
            for (int i = 1; i < route.Length; i++)
                if (IsBreakableForFaction(route[i], faction)) { breach = i; break; }
            if (breach < 0) return route;
            if (breach == 0) return System.Array.Empty<int>();
            if (breach == route.Length) return route;
            var prefix = new int[breach];
            System.Array.Copy(route, prefix, breach);
            return prefix;
        }

        private void ReleaseLocalPath(ref SoldierState soldier)
        {
            if (ArmyFlowOn) world.Map.ReleaseArmyFlowRoute(soldier.LocalPath);
            else world.Map.ReleaseSharedRoute(soldier.LocalPath);
            soldier.LocalPath = null;
        }

        private void PrepareArmyFlowFormation(ref ArmyState a, SimPoint goal)
        {
            if (a.PathImpossible) return;
            int living = 0;
            foreach (uint id in a.SoldierIds) if (world.Soldiers[id - 1].Alive) living++;
            if (living == 0 || a.Path.Length == 0) return;
            int width = ArmyFlowWidth(living, world.Config.Economy.ArmyFlowMaxWidth);
            var direction = ArmyFlowDirection(a);
            var lateral = new SimPoint(Fix64.FromInt(-direction.Z), Fix64.FromInt(direction.X));
            var forward = new SimPoint(Fix64.FromInt(direction.X), Fix64.FromInt(direction.Z));
            var center = world.Map.Center(a.Path[a.PathCursor]);
            bool returning = a.Policy == PolicyKind.Retreat || a.Decision.Returning;
            // Stage 2 late-push assaults must not let a blocked joiner hold the shared path cursor.
            bool directAssault = world.Config.Economy.AiLevelAtLeast(a.Definition.FactionId, 2)
                && IsLatePushAttack(a.Definition.Id) && a.Policy == PolicyKind.Focus && a.Goal.Kind == GoalKind.Core;
            bool humanArmy = HumanOrderFlowOn && HumanOrderCommand(a);
            int quorum = humanArmy ? world.Config.Economy.HumanOrderQuorumPermille
                : world.Config.Economy.ArmyFlowQuorumPermille;
            long waitTicks = humanArmy ? world.Config.Economy.HumanOrderWaitTicks
                : world.Config.Economy.ArmyFlowWaitTicks;
            int participants = 0, ready = 0, rank = 0;
            for (int i = 0; i < a.SoldierIds.Length; i++)
            {
                ref var soldier = ref world.Soldiers[a.SoldierIds[i] - 1];
                if (!soldier.Alive) continue;
                bool directSoldier = directAssault && soldier.TargetKind == 0 && !SoldierHasNearbyCombat(soldier);
                var intended = soldier.MoveGoal;
                bool splitScout = world.Config.Economy.LargeEconomy && a.ExplorePatrolAuto
                    && soldier.Initial.Kind == UnitKind.Scout && !soldier.Pursuit.Returning && soldier.ExploreHasGoal;
                if (splitScout) intended = soldier.ExploreGoal;
                if (!SamePoint(soldier.MoveGoal, intended)) soldier.MoveGoal = intended;
                bool atPosition = SamePoint(intended, soldier.Position);
                if (atPosition && !returning) { participants++; ready++; rank++; continue; }
                if (!SamePoint(intended, goal) && !(returning && atPosition))
                {
                    if (!soldier.TacticalRoute || world.Map.Cell(soldier.LocalGoal) != world.Map.Cell(intended))
                        StartLocalRoute(ref soldier, intended);
                    soldier.LocalGoal = intended; soldier.TacticalRoute = true; soldier.Joining = false;
                    FollowLocalRoute(ref soldier); rank++; continue;
                }
                if (soldier.TacticalRoute)
                { soldier.TacticalRoute = false; soldier.Joining = false; ReleaseLocalPath(ref soldier); }
                var pathCenter = center;
                bool skipFinalApproachJoin = ArmyFinalApproachOn && a.PathCursor == a.Path.Length - 1
                    && !soldier.Joining && Clear(soldier.Position, goal, a.Definition.FactionId);
                if (skipFinalApproachJoin)
                    armyFinalApproachSkippedJoins[a.Definition.FactionId - 1]++;
                else
                {
                    int joinCursor = humanArmy && !soldier.Joining ? HumanJoinCursor(a, soldier.Position) : a.PathCursor;
                    var joinCenter = world.Map.Center(a.Path[joinCursor]);
                    if (!soldier.Joining && (!InRange(soldier.Position, joinCenter, Fix64.FromInt(8))
                        || !Clear(soldier.Position, joinCenter, a.Definition.FactionId)))
                    {
                        soldier.Joining = true; soldier.JoinCursor = joinCursor; StartLocalRoute(ref soldier, joinCenter);
                        if (directSoldier) soldier.SlotWaitSinceTick = world.Tick;
                    }
                }
                if (soldier.Joining)
                {
                    FollowLocalRoute(ref soldier);
                    bool joinTimedOut = directSoldier && world.Tick - soldier.SlotWaitSinceTick >= waitTicks;
                    if (joinTimedOut)
                    {
                        soldier.Joining = false; soldier.JoinCursor = a.PathCursor; soldier.LocalGoal = soldier.Position;
                        soldier.SlotWaitTimedOut = true; soldier.SlotWaitTarget = pathCenter; ReleaseLocalPath(ref soldier);
                    }
                    if (!SamePoint(soldier.Position, soldier.LocalGoal)) { rank++; continue; }
                    else if (soldier.JoinCursor < a.PathCursor)
                    {
                        soldier.JoinCursor++; StartLocalRoute(ref soldier, world.Map.Center(a.Path[soldier.JoinCursor]));
                        FollowLocalRoute(ref soldier); rank++; continue;
                    }
                    soldier.Joining = false; ReleaseLocalPath(ref soldier);
                }
                participants++;
                var target = returning ? pathCenter : new SimPoint(
                    center.X + lateral.X * Fix64.FromInt(rank % width - width / 2) - forward.X * Fix64.FromInt(rank / width),
                    center.Z + lateral.Z * Fix64.FromInt(rank % width - width / 2) - forward.Z * Fix64.FromInt(rank / width));
                if (directSoldier) target = pathCenter;
                if (!Clear(center, target, a.Definition.FactionId) || !Clear(soldier.Position, target, a.Definition.FactionId)
                    || a.PathCursor + 1 < a.Path.Length && !Clear(target, world.Map.Center(a.Path[a.PathCursor + 1]), a.Definition.FactionId)) target = center;
                if (a.PathCursor == a.Path.Length - 1) target = Clear(soldier.Position, goal, a.Definition.FactionId) ? goal : center;
                soldier.MoveGoal = target;
                if (!SamePoint(soldier.SlotWaitTarget, target))
                { soldier.SlotWaitTarget = target; soldier.SlotWaitSinceTick = world.Tick; soldier.SlotWaitTimedOut = false; }
                if (!InRange(soldier.Position, target, Fix64.FromRatio(1, 10)))
                {
                    if (!soldier.SlotWaitTimedOut && world.Tick - soldier.SlotWaitSinceTick >= waitTicks)
                        soldier.SlotWaitTimedOut = true;
                    if (directSoldier) ready++;
                    if (soldier.SlotWaitTimedOut) ready++;
                }
                else { soldier.SlotWaitSinceTick = 0; soldier.SlotWaitTimedOut = false; ready++; }
                rank++;
            }
            int required = (participants * quorum + 999) / 1000;
            if (participants > 0 && ready >= required && a.PathCursor + 1 < a.Path.Length) a.PathCursor++;
        }

        private bool HumanOrderCommand(ArmyState a)
        {
            var command = a.CommandId == 0 ? null : CommandById(a.CommandId);
            return a.Policy == PolicyKind.Focus && command != null && command.Order.Source == CommandSource.Human;
        }

        private int HumanJoinCursor(ArmyState a, SimPoint position)
        {
            int best = a.PathCursor;
            var bestDistance = DistanceSquared(position, position);
            bool found = false;
            for (int i = a.PathCursor; i < a.Path.Length; i++)
            {
                var center = world.Map.Center(a.Path[i]);
                if (!Clear(position, center, a.Definition.FactionId)) continue;
                var distance = DistanceSquared(position, center);
                if (!found || distance < bestDistance) { bestDistance = distance; best = i; found = true; }
            }
            return best;
        }

        private static int ArmyFlowWidth(int count, int maxWidth)
        {
            int root = 0;
            while ((long)root * root < count) root++;
            return Math.Max(4, Math.Min(maxWidth, root));
        }

        private (int X, int Z) ArmyFlowDirection(ArmyState a)
        {
            int current = a.Path[a.PathCursor];
            int next = a.PathCursor + 1 < a.Path.Length ? a.Path[a.PathCursor + 1]
                : a.PathCursor > 0 ? a.Path[a.PathCursor - 1] : current;
            int dx = Math.Sign(next % world.Config.Map.WidthCells - current % world.Config.Map.WidthCells);
            int dz = Math.Sign(next / world.Config.Map.WidthCells - current / world.Config.Map.WidthCells);
            if (dx == 0 && dz == 0) dx = 1;
            return (dx, dz);
        }

        private void FollowLocalRoute(ref SoldierState soldier)
        {
            var path = soldier.LocalPath;
            if (path == null || path.Length == 0) { soldier.MoveGoal = soldier.Position; return; }
            // Center-to-center segments cannot cut a blocked corner. An off-center start first recenters.
            while (soldier.LocalCursor + 1 < path.Length && SamePoint(soldier.Position, world.Map.Center(path[soldier.LocalCursor]))) soldier.LocalCursor++;
            var target = world.Map.Center(path[soldier.LocalCursor]);
            if (soldier.LocalCursor == path.Length - 1 && Clear(soldier.Position, soldier.LocalGoal, soldier.Initial.FactionId)) target = soldier.LocalGoal;
            soldier.MoveGoal = Clear(soldier.Position, target, soldier.Initial.FactionId) ? target : world.Map.Center(world.Map.Cell(soldier.Position));
        }
    }
}
