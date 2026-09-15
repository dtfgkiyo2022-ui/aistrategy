using System.Collections.Generic;

namespace Rts.Contracts
{
    public enum AssignmentKind : byte { Advance = 0, Reserve = 1, Guard = 2, CoreDefense = 3 }

    // Value snapshots: Simulation owns persistent copies; Decision returns replacements.
    public struct ArmyDecisionMemory
    {
        public AssignmentKind Assignment;
        public PolicyGoal Goal;
        public bool Returning;
        public long InferiorSince, HoldUntilTick;
        public ArmyDecisionMemory(AssignmentKind assignment, PolicyGoal goal, bool returning, long inferiorSince, long holdUntilTick)
        { Assignment = assignment; Goal = goal; Returning = returning; InferiorSince = inferiorSince; HoldUntilTick = holdUntilTick; }
    }

    public struct PursuitMemory
    {
        public bool Active, Returning;
        public SimPoint Start, Mission;
        public long StartTick;
    }

    public struct AttackMemory
    {
        public uint OutpostId;
        public long LastAttackTick, LastSeenTick, AlertUntilTick;
        public bool Visible, HasAttack;
    }

    public readonly struct ObjectiveRoute
    {
        public PolicyGoal Goal { get; }
        public int Distance { get; }
        public ObjectiveRoute(PolicyGoal goal, int distance) { Goal = goal; Distance = distance; }
    }

    public sealed class ArmyDecisionInput
    {
        public OwnArmyView Army { get; }
        public PolicyView Policy { get; }
        public bool IsReserveRole { get; }
        public ArmyDecisionMemory Memory { get; }
        public IReadOnlyList<ObjectiveRoute> Routes { get; }
        public ArmyDecisionInput(OwnArmyView army, PolicyView policy, bool isReserveRole,
            ArmyDecisionMemory memory, IReadOnlyList<ObjectiveRoute> routes)
        { Army = army; Policy = policy; IsReserveRole = isReserveRole; Memory = memory; Routes = ContractList.Copy(routes); }
    }

    public readonly struct TacticalInput
    {
        public uint ArmyId { get; }
        public long Tick { get; }
        public SimPoint Position { get; }
        public SimPoint Mission { get; }
        public SimPoint Home { get; }
        public Fix64 Range { get; }
        public Fix64 CoreRadius { get; }
        public PolicyKind Policy { get; }
        public AssignmentKind Assignment { get; }
        public bool Returning { get; }
        public TacticalInput(uint armyId, long tick, SimPoint position, SimPoint mission, SimPoint home,
            Fix64 range, Fix64 coreRadius, PolicyKind policy, AssignmentKind assignment, bool returning)
        { ArmyId = armyId; Tick = tick; Position = position; Mission = mission; Home = home;
            Range = range; CoreRadius = coreRadius; Policy = policy; Assignment = assignment; Returning = returning; }
    }
}
