using System;
using System.Linq;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        // Ten seconds is enough for a normal slot correction; a late unit then follows the advancing formation.
        private const long FormationWaitTicks = 200;

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
                int first = -1;
                foreach (uint id in a.SoldierIds) if (world.Soldiers[id - 1].Alive) { first = (int)id - 1; break; }
                if (first < 0) continue;
                var goal = ArmyGoal(a);
                var command = a.CommandId == 0 ? null : CommandById(a.CommandId);
                bool humanFocus = a.Policy == PolicyKind.Focus && command != null && command.Order.Source == CommandSource.Human;
                if (!a.HasPathGoal || !SamePoint(a.PathGoal, goal))
                {
                    a.PathGoal = goal; a.HasPathGoal = true; a.PathCursor = 0;
                    foreach (uint id in a.SoldierIds)
                    {
                        world.Soldiers[id - 1].Joining = false; world.Soldiers[id - 1].SlotWaitTimedOut = false;
                        world.Soldiers[id - 1].SlotWaitSinceTick = 0; world.Soldiers[id - 1].SlotWaitTarget = default;
                        world.Soldiers[id - 1].TacticalRoute = false; world.Soldiers[id - 1].LocalPath = null;
                    }
                    int startCell = world.Map.Cell(world.Soldiers[first].Position);
                    var soldierIds = a.SoldierIds;
                    uint factionId = a.Definition.FactionId;
                    bool direct = world.Config.Economy.DirectPathMovement && humanFocus && soldierIds
                        .Where(id => world.Soldiers[id - 1].Alive)
                        .All(id => world.Soldiers[id - 1].Initial.Kind == UnitKind.Scout
                            && Clear(world.Soldiers[id - 1].Position, goal, factionId));
                    a.Path = direct ? new[] { startCell, world.Map.Cell(goal) }
                        : world.Map.FindPath(startCell, goal, a.Definition.FactionId);
                    var radius = a.Policy == PolicyKind.Focus && a.Goal.Kind == GoalKind.Outpost ? world.Config.Rules.CaptureRadius
                        : a.Policy == PolicyKind.Focus && a.Goal.Kind == GoalKind.Core ? world.Config.Rules.CoreRadius + world.Soldiers[first].Parameters.Range
                        : Fix64.FromInt(4);
                    a.PathImpossible = !direct && (a.Path.Length == 0 || !InRange(world.Map.Center(a.Path[a.Path.Length - 1]), goal, radius));
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
                    { soldier.TacticalRoute = false; soldier.Joining = false; soldier.LocalPath = null; }
                    if (a.PathImpossible) { soldier.MoveGoal = soldier.Position; continue; }
                    var center = world.Map.Center(a.Path[a.PathCursor]);
                    if (!soldier.Joining && (!InRange(soldier.Position, center, Fix64.FromInt(8)) || !Clear(soldier.Position, center, a.Definition.FactionId)))
                    {
                        soldier.Joining = true; soldier.JoinCursor = a.PathCursor;
                        StartLocalRoute(ref soldier, center);
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
                        soldier.Joining = false; soldier.LocalPath = null;
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
                        soldier.LocalPath = null;
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
            soldier.LocalPath = world.Map.SharedRoute(world.Map.Cell(soldier.Position), goal, soldier.Initial.FactionId);
            if (soldier.LocalPath.Length > 1 && Clear(soldier.Position, world.Map.Center(soldier.LocalPath[1]), soldier.Initial.FactionId)) soldier.LocalCursor = 1;
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
