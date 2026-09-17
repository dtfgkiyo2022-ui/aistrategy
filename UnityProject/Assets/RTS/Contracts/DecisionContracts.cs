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
        public int InferiorTicks;
        // Auto-offense is stateful: keeping a target must not silently refresh its timer.
        public long OffensiveSince, SuppressUntilTick;
        public PolicyGoal SuppressedGoal;
        public ArmyDecisionMemory(AssignmentKind assignment, PolicyGoal goal, bool returning, long inferiorSince, long holdUntilTick)
        { Assignment = assignment; Goal = goal; Returning = returning; InferiorSince = inferiorSince; HoldUntilTick = holdUntilTick; InferiorTicks = 0;
            OffensiveSince = 0; SuppressUntilTick = 0; SuppressedGoal = default; }
    }

    /// <summary>Observation-derived approach state.  It deliberately contains no enemy world state.</summary>
    public struct ContactApproachMemory
    {
        public uint ContactId;
        public uint ObjectiveId;
        public SimPoint PreviousPosition;
        public long PreviousDistance;
        public long LastSeenTick;
        public long ThreatUntilTick;
        public bool HasPrevious;
    }

    public enum OffensivePhase : byte { Idle = 0, Gathering = 1, WaitingToAdvance = 2, Advancing = 3 }

    /// <summary>Faction-owned, observation-only state for one coordinated auto offensive.</summary>
    public sealed class FactionOffenseMemory
    {
        public ulong Id;
        public PolicyGoal Goal;
        public OffensivePhase Phase;
        public SimPoint RallyPoint;
        public long StartedTick, MoveDeadlineTick, GatheredTick, MaintainedSinceTick;
        public long ReleasedTick = -1;
        public IReadOnlyList<uint> PlannedArmyIds = System.Array.Empty<uint>();
        public IReadOnlyList<uint> JoiningArmyIds = System.Array.Empty<uint>();
        public IReadOnlyList<uint> AdvancingArmyIds = System.Array.Empty<uint>();
        public IReadOnlyList<uint> CommittedReserveArmyIds = System.Array.Empty<uint>();
        public IReadOnlyList<SuppressedGoalMemory> SuppressedGoals = System.Array.Empty<SuppressedGoalMemory>();
    }
    public struct SuppressedGoalMemory { public PolicyGoal Goal; public long UntilTick; }

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
        /// <summary>Centres of the A* cells, in travel order.  Empty means unreachable.</summary>
        public IReadOnlyList<SimPoint> Cells { get; }
        public ObjectiveRoute(PolicyGoal goal, int distance, IReadOnlyList<SimPoint> cells = null)
        { Goal = goal; Distance = distance; Cells = ContractList.Copy(cells ?? System.Array.Empty<SimPoint>()); }
    }

    /// <summary>A bounded, observation-only A* measurement for an approaching visible contact.</summary>
    public readonly struct ContactApproachRoute
    {
        public uint ContactId { get; }
        public uint ObjectiveId { get; }
        public int Distance { get; }
        public ContactApproachRoute(uint contactId, uint objectiveId, int distance)
        { ContactId = contactId; ObjectiveId = objectiveId; Distance = distance; }
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
        /// <summary>
        /// The soldier cannot move at all (speed 0). Such a unit must never join the pursuit
        /// cycle: it could never travel back to its mission, so it would stop selecting targets
        /// for the rest of the match.
        /// </summary>
        public bool Immobile { get; }
        public TacticalInput(uint armyId, long tick, SimPoint position, SimPoint mission, SimPoint home,
            Fix64 range, Fix64 coreRadius, PolicyKind policy, AssignmentKind assignment, bool returning,
            bool immobile = false)
        { ArmyId = armyId; Tick = tick; Position = position; Mission = mission; Home = home;
            Range = range; CoreRadius = coreRadius; Policy = policy; Assignment = assignment; Returning = returning;
            Immobile = immobile; }
    }
}
