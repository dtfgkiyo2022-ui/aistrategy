using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private SimPoint GoalPosition(PolicyGoal goal) => goal.Kind == GoalKind.Point ? goal.Point
            : goal.Kind == GoalKind.Outpost ? world.Outposts[goal.Id - 1].Definition.Position
            : world.Cores[goal.Id - 1].Definition.Position;

        private SimPoint ArmyGoal(ArmyState a)
        {
            uint faction = a.Definition.FactionId;
            var own = world.Cores[world.Factions[faction - 1].CoreId - 1].Definition.Position;
            if (a.Policy == PolicyKind.Retreat) return a.Goal.Kind == GoalKind.None ? own : GoalPosition(a.Goal);
            if (a.Decision.Returning || a.Policy == 0 && world.Tick < a.Decision.HoldUntilTick) return own;
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
                if (!a.HasPathGoal || !SamePoint(a.PathGoal, goal))
                {
                    a.PathGoal = goal; a.HasPathGoal = true; a.PathCursor = 0;
                    foreach (uint id in a.SoldierIds) { world.Soldiers[id - 1].Joining = false; world.Soldiers[id - 1].TacticalRoute = false; world.Soldiers[id - 1].LocalPath = null; }
                    a.Path = world.Map.FindPath(world.Map.Cell(world.Soldiers[first].Position), goal);
                    var radius = a.Policy == PolicyKind.Focus && a.Goal.Kind == GoalKind.Outpost ? world.Config.Rules.CaptureRadius
                        : a.Policy == PolicyKind.Focus && a.Goal.Kind == GoalKind.Core ? world.Config.Rules.CoreRadius + world.Soldiers[first].Parameters.Range
                        : Fix64.FromInt(4);
                    a.PathImpossible = a.Path.Length == 0 || !InRange(world.Map.Center(a.Path[a.Path.Length - 1]), goal, radius);
                }
                bool arrived = true, participants = false;
                for (int i = 0; i < a.SoldierIds.Length; i++)
                {
                    ref var soldier = ref world.Soldiers[a.SoldierIds[i] - 1];
                    if (!soldier.Alive) continue;
                    var intended = soldier.MoveGoal;
                    if (SamePoint(intended, soldier.Position)) continue;
                    if (!SamePoint(intended, goal))
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
                    if (!soldier.Joining && (!InRange(soldier.Position, center, Fix64.FromInt(8)) || !Clear(soldier.Position, center)))
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
                    participants = true;
                    // Collapse at bends; keep slots only when the next center is reachable from the slot.
                    var target = new SimPoint(center.X + Fix64.FromInt((a.Definition.FactionId == 1 ? 1 : -1) * (i % 4)), center.Z + Fix64.FromInt(i / 4));
                    if (!Clear(center, target) || !Clear(soldier.Position, target)
                        || a.PathCursor + 1 < a.Path.Length && !Clear(target, world.Map.Center(a.Path[a.PathCursor + 1]))) target = center;
                    if (a.PathCursor == a.Path.Length - 1) target = Clear(soldier.Position, goal) ? goal : center;
                    soldier.MoveGoal = target;
                    if (!InRange(soldier.Position, target, Fix64.FromRatio(1, 10))) arrived = false;
                }
                if (participants && arrived && a.PathCursor + 1 < a.Path.Length) a.PathCursor++;
            }
        }

        private bool Clear(SimPoint from, SimPoint to) => world.Map.IsPassable(world.Map.Cell(to)) && SamePoint(world.Map.ClipMove(from, to), to);

        private void StartLocalRoute(ref SoldierState soldier, SimPoint goal)
        {
            soldier.LocalGoal = goal; soldier.LocalCursor = 0;
            soldier.LocalPath = world.Map.SharedRoute(world.Map.Cell(soldier.Position), goal);
            if (soldier.LocalPath.Length > 1 && Clear(soldier.Position, world.Map.Center(soldier.LocalPath[1]))) soldier.LocalCursor = 1;
        }

        private void FollowLocalRoute(ref SoldierState soldier)
        {
            var path = soldier.LocalPath;
            if (path == null || path.Length == 0) { soldier.MoveGoal = soldier.Position; return; }
            // Center-to-center segments cannot cut a blocked corner. An off-center start first recenters.
            while (soldier.LocalCursor + 1 < path.Length && SamePoint(soldier.Position, world.Map.Center(path[soldier.LocalCursor]))) soldier.LocalCursor++;
            var target = world.Map.Center(path[soldier.LocalCursor]);
            if (soldier.LocalCursor == path.Length - 1 && Clear(soldier.Position, soldier.LocalGoal)) target = soldier.LocalGoal;
            soldier.MoveGoal = Clear(soldier.Position, target) ? target : world.Map.Center(world.Map.Cell(soldier.Position));
        }
    }
}
