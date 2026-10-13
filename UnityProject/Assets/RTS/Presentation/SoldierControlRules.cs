using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>Unity-free rules for translating a right-click target into an existing army policy.</summary>
    public static class SoldierControlRules
    {
        public enum TargetKind : byte
        {
            Ground = 0,
            EnemyCore = 1,
            EnemyOutpost = 2,
            EnemyArmy = 3,
            EnemySoldier = 4,
            EnemyBuilding = 5,
            Ui = 6,
            VillagerSelection = 7,
            Nothing = 8
        }

        public readonly struct Target
        {
            public TargetKind Kind { get; }
            public PolicyGoal Goal { get; }

            public Target(TargetKind kind, PolicyGoal goal)
            {
                Kind = kind;
                Goal = goal;
            }
        }

        /// <summary>Returns an order only when an army is selected and the click is not owned by another input mode.</summary>
        public static bool TryResolve(bool armySelected, bool villagerSelected, Target target,
            out PolicyKind kind, out PolicyGoal goal)
        {
            kind = default(PolicyKind);
            goal = default(PolicyGoal);
            if (!armySelected || villagerSelected) return false;

            switch (target.Kind)
            {
                case TargetKind.Ground:
                case TargetKind.EnemyArmy:
                case TargetKind.EnemySoldier:
                case TargetKind.EnemyBuilding:
                    if (target.Goal.Kind != GoalKind.Point) return false;
                    kind = PolicyKind.Focus;
                    goal = target.Goal;
                    return true;
                case TargetKind.EnemyCore:
                    if (target.Goal.Kind != GoalKind.Core || target.Goal.Id == 0) return false;
                    kind = PolicyKind.Focus;
                    goal = target.Goal;
                    return true;
                case TargetKind.EnemyOutpost:
                    if (target.Goal.Kind != GoalKind.Outpost || target.Goal.Id == 0) return false;
                    kind = PolicyKind.Focus;
                    goal = target.Goal;
                    return true;
                default:
                    return false;
            }
        }
    }
}
