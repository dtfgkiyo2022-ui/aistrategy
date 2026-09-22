using System;
using System.Collections.Generic;

namespace Rts.Contracts
{
    public enum ScopeKind : byte
    {
        All = 1, Army = 2, Outpost = 3
    }

    public enum PolicyKind : byte
    {
        Focus = 1, AllowAbandon = 2, Retreat = 3, MaintainReserve = 4,
    Defend = 5, Scout = 6, ReturnToAuto = 7
    }

    public enum CommandSource : byte
    {
        Human = 1, Doctrine = 2, Ai = 3
    }

    public enum CommandStatus : byte
    {
        Interpreting = 1, Pending = 2, Executing = 3, Completed = 4,
    Cancelled = 5, Expired = 6, Impossible = 7
    }

    public enum GoalKind : byte
    {
        None = 0, Point = 1, Outpost = 2, Core = 3
    }

    public enum EndKind : byte
    {
        UntilReplaced = 1, Arrived = 2, ObjectiveOwned = 3, AtTick = 4, LossReached = 5
    }

    [Flags]
    public enum ExpireFlags : byte
    {
        None = 0, SubjectGone = 1, OwnershipChanged = 2, ObservationTooOld = 4
    }

    public enum InputKind : byte
    {
        Reserve = 1, Resolve = 2, Cancel = 3, Proposal = 4,
        /// <summary>Ver.3 direct economy operation; carries <see cref="ScheduledInput.Economy"/> and no orders.</summary>
        Economy = 5
    }

    // UnitKind has no assigned numbers in chapter 5; these are the initial contract values.
    // 3 is left for the sentry of the held PR #62. Villager is Ver.3 (technical-design-v3 3.3).
    public enum UnitKind : byte { Infantry = 1, Scout = 2, Villager = 4 }

    /// <summary>Ver.3 resources (technical-design-v3 2.3). Ore and Metal are V3-2 (12.1); Metal is made, never found.</summary>
    public enum ResourceKind : byte { Food = 1, Wood = 2, Ore = 3, Metal = 4 }

    /// <summary>Ver.3 direction of a belt or a building's output (technical-design-v3 11.2). North is +z, east is +x.</summary>
    public enum Facing : byte { North = 0, East = 1, South = 2, West = 3 }

    /// <summary>Ver.3 buildings (technical-design-v3 3.3). The core is not a building.</summary>
    public enum BuildingKind : byte { Barracks = 1 }

    public enum EventKind : byte
    {
        CommandChanged = 1, MoveStarted = 2, Attack = 3, Death = 4,
        Capture = 5, Reinforcement = 6, ContactChanged = 7, MatchEnded = 8, Fault = 9, AiReport = 10
    }

    public enum ReasonCode : byte
    {
        None = 0, Superseded = 1, UserCancelled = 2, Deadline = 3,
        StaleVersion = 4, InvalidPayload = 5, SubjectGone = 6, OwnershipChanged = 7,
        NoPath = 8, EmptyArmy = 9, ObservationTooOld = 10, LossLimit = 11, ReserveShortfall = 12
    }

}
