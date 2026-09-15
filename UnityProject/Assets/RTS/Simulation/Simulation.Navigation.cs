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
            var enemy = world.Cores[world.Factions[2 - faction].CoreId - 1].Definition.Position;
            if (a.Policy == PolicyKind.Focus) return GoalPosition(a.Goal);
            if (a.Policy == PolicyKind.Retreat || a.Definition.Role == "reserve") return own;
            bool scout = a.Definition.Role == "scout";
            if (scout && a.AutoStage == 0) return new SimPoint(own.X, Fix64.FromInt(96));
            if (a.AutoStage < (scout ? 2 : 1)) return new SimPoint(enemy.X, Fix64.FromInt(a.Definition.Role == "south" ? 32 : 96));
            return enemy;
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
                    a.Path = world.Map.FindPath(world.Map.Cell(world.Soldiers[first].Position), goal);
                    var radius = a.Policy == PolicyKind.Focus && a.Goal.Kind == GoalKind.Outpost ? world.Config.Rules.CaptureRadius
                        : a.Policy == PolicyKind.Focus && a.Goal.Kind == GoalKind.Core ? world.Config.Rules.CoreRadius + world.Soldiers[first].Parameters.Range
                        : Fix64.FromInt(4);
                    a.PathImpossible = a.Path.Length == 0 || !InRange(world.Map.Center(a.Path[a.Path.Length - 1]), goal, radius);
                }
                if (a.PathImpossible)
                {
                    foreach (uint id in a.SoldierIds) world.Soldiers[id - 1].MoveGoal = world.Soldiers[id - 1].Position;
                    continue;
                }
                bool arrived = true;
                for (int i = 0; i < a.SoldierIds.Length; i++)
                {
                    ref var soldier = ref world.Soldiers[a.SoldierIds[i] - 1];
                    if (!soldier.Alive) continue;
                    var center = world.Map.Center(a.Path[a.PathCursor]);
                    // Formation slots are fixed by initial soldier ID, including tombstones.
                    var target = new SimPoint(center.X + Fix64.FromInt((a.Definition.FactionId == 1 ? 1 : -1) * (i % 4)), center.Z + Fix64.FromInt(i / 4));
                    if (!world.Map.IsPassable(world.Map.Cell(target)) || !SamePoint(world.Map.ClipMove(soldier.Position, target), target)) target = center;
                    soldier.MoveGoal = target;
                    if (!InRange(soldier.Position, target, Fix64.FromRatio(1, 10))) arrived = false;
                }
                if (arrived)
                {
                    if (a.PathCursor + 1 < a.Path.Length) a.PathCursor++;
                    else if (a.Policy == 0 && a.Definition.Role != "reserve" && a.AutoStage < (a.Definition.Role == "scout" ? 2 : 1)) a.AutoStage++;
                }
            }
        }
    }
}
