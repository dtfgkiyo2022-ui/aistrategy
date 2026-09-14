using System;
using System.Collections.Generic;

namespace Rts.Contracts
{
    public sealed class FactionFrame
    {
        public long Tick { get; }
        public uint FactionId { get; }
        public IReadOnlyList<RenderUnit> Units { get; }
        public FactionObservation Observation { get; }
        public IReadOnlyList<CommandView> Commands { get; }
        public IReadOnlyList<GameEvent> Events { get; }
        public FogView Fog { get; }
        public MatchResult Result { get; }

        public FactionFrame(
            long tick,
            uint factionId,
            IReadOnlyList<RenderUnit> units,
            FactionObservation observation,
            IReadOnlyList<CommandView> commands,
            IReadOnlyList<GameEvent> events,
            FogView fog,
            MatchResult result)
        {
            Tick = tick;
            FactionId = factionId;
            Units = ContractList.Copy(units);
            Observation = observation;
            Commands = ContractList.Copy(commands);
            Events = ContractList.Copy(events);
            Fog = fog;
            Result = result;
        }
    }

    public readonly struct GameEvent
    {
        public long Tick { get; }
        public uint Ordinal { get; }
        public EventKind Kind { get; }
        public byte AudienceMask { get; }
        public uint SubjectId { get; }
        public ulong CommandId { get; }
        public SimPoint Position { get; }
        public int Value { get; }
        public ReasonCode Reason { get; }

        public GameEvent(
            long tick,
            uint ordinal,
            EventKind kind,
            byte audienceMask,
            uint subjectId,
            ulong commandId,
            SimPoint position,
            int value,
            ReasonCode reason)
        {
            Tick = tick;
            Ordinal = ordinal;
            Kind = kind;
            AudienceMask = audienceMask;
            SubjectId = subjectId;
            CommandId = commandId;
            Position = position;
            Value = value;
            Reason = reason;
        }
    }

    /// <summary>Id is an own unit ID or a faction-local contact ID. Enemy units use HasHp=false and Hp=0.</summary>
    public readonly struct RenderUnit
    {
        public uint Id { get; }
        public bool IsOwn { get; }
        public UnitKind Kind { get; }
        public SimPoint Position { get; }
        public bool IsMoving { get; }
        public bool IsAttacking { get; }
        public bool IsRetreating { get; }
        public bool HasHp { get; }
        public int Hp { get; }

        public RenderUnit(
            uint id,
            bool isOwn,
            UnitKind kind,
            SimPoint position,
            bool isMoving,
            bool isAttacking,
            bool isRetreating,
            bool hasHp,
            int hp)
        {
            Id = id;
            IsOwn = isOwn;
            Kind = kind;
            Position = position;
            IsMoving = isMoving;
            IsAttacking = isAttacking;
            IsRetreating = isRetreating;
            HasHp = hasHp;
            Hp = hp;
        }
    }
    public readonly struct CommandView
    {
        public ulong CommandId { get; }
        public ScopeKey Target { get; }
        public PolicyKind Kind { get; }
        public CommandStatus Status { get; }
        public long AcceptedTick { get; }
        public long ApplyTick { get; }
        public ReasonCode Reason { get; }

        public CommandView(
            ulong commandId,
            ScopeKey target,
            PolicyKind kind,
            CommandStatus status,
            long acceptedTick,
            long applyTick,
            ReasonCode reason)
        {
            CommandId = commandId;
            Target = target;
            Kind = kind;
            Status = status;
            AcceptedTick = acceptedTick;
            ApplyTick = applyTick;
            Reason = reason;
        }
    }
    /// <summary>Cell index is z * map width + x.</summary>
    public sealed class FogView
    {
        public IReadOnlyList<bool> VisibleCells { get; }
        public IReadOnlyList<bool> ExploredCells { get; }

        public FogView(
            IReadOnlyList<bool> visibleCells,
            IReadOnlyList<bool> exploredCells)
        {
            VisibleCells = ContractList.Copy(visibleCells);
            ExploredCells = ContractList.Copy(exploredCells);
        }
    }
    /// <summary>WinnerFactionId is zero without a winner; a verification tick limit is undecided, not a draw.</summary>
    public readonly struct MatchResult
    {
        public bool HasEnded { get; }
        public uint WinnerFactionId { get; }
        public bool IsDraw { get; }
        public bool IsFault { get; }
        public bool IsUndecided { get; }

        public MatchResult(
            bool hasEnded,
            uint winnerFactionId,
            bool isDraw,
            bool isFault,
            bool isUndecided)
        {
            HasEnded = hasEnded;
            WinnerFactionId = winnerFactionId;
            IsDraw = isDraw;
            IsFault = isFault;
            IsUndecided = isUndecided;
        }
    }
    /// <summary>Development/replay only. Complete canonical state bytes; the serializer schema is defined by the replay implementation.</summary>
    public sealed class DiagnosticState
    {
        public long Tick { get; }
        public IReadOnlyList<byte> CanonicalState { get; }

        public DiagnosticState(
            long tick,
            IReadOnlyList<byte> canonicalState)
        {
            Tick = tick;
            CanonicalState = ContractList.Copy(canonicalState);
        }
    }

}
