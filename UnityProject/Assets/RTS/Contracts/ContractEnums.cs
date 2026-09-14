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
        Reserve = 1, Resolve = 2, Cancel = 3, Proposal = 4
    }

    // UnitKind has no assigned numbers in chapter 5; these are the initial contract values.
    public enum UnitKind : byte { Infantry = 1, Scout = 2 }

    public enum EventKind : byte
    {
        CommandChanged = 1, MoveStarted = 2, Attack = 3, Death = 4,
        Capture = 5, Reinforcement = 6, ContactChanged = 7, MatchEnded = 8, Fault = 9
    }

    public enum ReasonCode : byte
    {
        None = 0, Superseded = 1, UserCancelled = 2, Deadline = 3,
        StaleVersion = 4, InvalidPayload = 5, SubjectGone = 6, OwnershipChanged = 7,
        NoPath = 8, EmptyArmy = 9, ObservationTooOld = 10, LossLimit = 11
    }

}
