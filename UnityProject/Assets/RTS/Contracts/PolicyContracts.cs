using System;
using System.Collections.Generic;

namespace Rts.Contracts
{
    public readonly struct ScopeKey
    {
        public uint FactionId { get; }
        public ScopeKind Kind { get; }
        public uint Id { get; }

        public ScopeKey(
            uint factionId,
            ScopeKind kind,
            uint id)
        {
            FactionId = factionId;
            Kind = kind;
            Id = id;
        }
    }

    public readonly struct PolicyVersion
    {
        public ScopeKey Scope { get; }
        public ulong Revision { get; }

        public PolicyVersion(
            ScopeKey scope,
            ulong revision)
        {
            Scope = scope;
            Revision = revision;
        }
    }

    public readonly struct PolicyGoal
    {
        public GoalKind Kind { get; }
        public uint Id { get; }
        public SimPoint Point { get; }

        public PolicyGoal(
            GoalKind kind,
            uint id,
            SimPoint point)
        {
            Kind = kind;
            Id = id;
            Point = point;
        }
    }

    public readonly struct LossBudget
    {
        public ushort Permille { get; }

        public LossBudget(
            ushort permille)
        {
            Permille = permille;
        }
    }

    public readonly struct EndCondition
    {
        public EndKind Kind { get; }
        public long Tick { get; }

        public EndCondition(
            EndKind kind,
            long tick)
        {
            Kind = kind;
            Tick = tick;
        }
    }

    public readonly struct Expiration
    {
        public long ValidUntilTick { get; }
        public int MaxObservationAgeTicks { get; }
        public ExpireFlags Flags { get; }

        public Expiration(
            long validUntilTick,
            int maxObservationAgeTicks,
            ExpireFlags flags)
        {
            ValidUntilTick = validUntilTick;
            MaxObservationAgeTicks = maxObservationAgeTicks;
            Flags = flags;
        }
    }

    public sealed class PolicyOrder
    {
        public ulong CommandId { get; }
        public ulong BatchId { get; }
        public CommandSource Source { get; }
        public ScopeKey Target { get; }
        public PolicyKind Kind { get; }
        public PolicyGoal Goal { get; }
        public byte Priority { get; }
        public LossBudget AllowedLoss { get; }
        public EndCondition End { get; }
        public ushort ReservePermille { get; }
        public ulong TargetRevision { get; }
        public IReadOnlyList<PolicyVersion> Parents { get; }
        public long ObservedTick { get; }
        public Expiration Expiration { get; }

        public PolicyOrder(
            ulong commandId,
            ulong batchId,
            CommandSource source,
            ScopeKey target,
            PolicyKind kind,
            PolicyGoal goal,
            byte priority,
            LossBudget allowedLoss,
            EndCondition end,
            ushort reservePermille,
            ulong targetRevision,
            IReadOnlyList<PolicyVersion> parents,
            long observedTick,
            Expiration expiration)
        {
            CommandId = commandId;
            BatchId = batchId;
            Source = source;
            Target = target;
            Kind = kind;
            Goal = goal;
            Priority = priority;
            AllowedLoss = allowedLoss;
            End = end;
            ReservePermille = reservePermille;
            TargetRevision = targetRevision;
            Parents = ContractList.Copy(parents);
            ObservedTick = observedTick;
            Expiration = expiration;
        }
    }

    public sealed class ScheduledInput
    {
        public ulong LogIndex { get; }
        public InputKind Kind { get; }
        public long AcceptedTick { get; }
        public long ApplyTick { get; }
        public ulong RequestId { get; }
        public ulong IssuerSequence { get; }
        public IReadOnlyList<PolicyOrder> Orders { get; }

        public ScheduledInput(
            ulong logIndex,
            InputKind kind,
            long acceptedTick,
            long applyTick,
            ulong requestId,
            ulong issuerSequence,
            IReadOnlyList<PolicyOrder> orders)
        {
            LogIndex = logIndex;
            Kind = kind;
            AcceptedTick = acceptedTick;
            ApplyTick = applyTick;
            RequestId = requestId;
            IssuerSequence = issuerSequence;
            Orders = ContractList.Copy(orders);
        }
    }

    public sealed class PolicyRequest
    {
        public ulong RequestId { get; }
        public uint FactionId { get; }
        public ScopeKey Scope { get; }
        public long StartedTick { get; }
        public FactionObservation Observation { get; }
        public IReadOnlyList<PolicyVersion> Versions { get; }
        public long DeadlineTick { get; }
        public PolicyKind Kind { get; }
        public PolicyGoal Goal { get; }

        public PolicyRequest(
            ulong requestId,
            uint factionId,
            ScopeKey scope,
            long startedTick,
            FactionObservation observation,
            IReadOnlyList<PolicyVersion> versions,
            long deadlineTick,
            PolicyKind kind,
            PolicyGoal goal)
        {
            RequestId = requestId;
            FactionId = factionId;
            Scope = scope;
            StartedTick = startedTick;
            Observation = observation;
            Versions = ContractList.Copy(versions);
            DeadlineTick = deadlineTick;
            Kind = kind;
            Goal = goal;
        }
    }
    public sealed class PolicyReply
    {
        public ulong RequestId { get; }
        public long ReturnedTick { get; }
        public IReadOnlyList<PolicyOrder> Orders { get; }
        public ReasonCode Reason { get; }

        public PolicyReply(
            ulong requestId,
            long returnedTick,
            IReadOnlyList<PolicyOrder> orders,
            ReasonCode reason)
        {
            RequestId = requestId;
            ReturnedTick = returnedTick;
            Orders = ContractList.Copy(orders);
            Reason = reason;
        }
    }
    public readonly struct UserPolicyIntent
    {
        public ulong IssuerSequence { get; }
        public ScopeKey Target { get; }
        public PolicyKind Kind { get; }
        public PolicyGoal Goal { get; }
        public byte Priority { get; }
        public LossBudget AllowedLoss { get; }
        public EndCondition End { get; }
        public ushort ReservePermille { get; }
        public Expiration Expiration { get; }

        public UserPolicyIntent(
            ulong issuerSequence,
            ScopeKey target,
            PolicyKind kind,
            PolicyGoal goal,
            byte priority,
            LossBudget allowedLoss,
            EndCondition end,
            ushort reservePermille,
            Expiration expiration)
        {
            IssuerSequence = issuerSequence;
            Target = target;
            Kind = kind;
            Goal = goal;
            Priority = priority;
            AllowedLoss = allowedLoss;
            End = end;
            ReservePermille = reservePermille;
            Expiration = expiration;
        }
    }

}
