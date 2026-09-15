using System;
using System.Collections.Generic;

namespace Rts.Contracts
{
    public sealed class FactionObservation
    {
        public uint FactionId { get; }
        public long Tick { get; }
        public IReadOnlyList<OwnArmyView> OwnArmies { get; }
        public IReadOnlyList<VisibleEnemy> VisibleEnemies { get; }
        public IReadOnlyList<EnemyContact> Contacts { get; }
        public IReadOnlyList<KnownObjective> Objectives { get; }

        public FactionObservation(
            uint factionId,
            long tick,
            IReadOnlyList<OwnArmyView> ownArmies,
            IReadOnlyList<VisibleEnemy> visibleEnemies,
            IReadOnlyList<EnemyContact> contacts,
            IReadOnlyList<KnownObjective> objectives)
        {
            FactionId = factionId;
            Tick = tick;
            OwnArmies = ContractList.Copy(ownArmies);
            VisibleEnemies = ContractList.Copy(visibleEnemies);
            Contacts = ContractList.Copy(contacts);
            Objectives = ContractList.Copy(objectives);
        }
    }

    public readonly struct VisibleEnemy
    {
        public uint ContactId { get; }
        public SimPoint Position { get; }
        public byte Kind { get; }

        public VisibleEnemy(
            uint contactId,
            SimPoint position,
            byte kind)
        {
            ContactId = contactId;
            Position = position;
            Kind = kind;
        }
    }

    public readonly struct EnemyContact
    {
        public uint ContactId { get; }
        public SimPoint LastPosition { get; }
        public long LastSeenTick { get; }
        public int EstimateMin { get; }
        public int EstimateMax { get; }
        public bool IsCurrentlyVisible { get; }

        public EnemyContact(
            uint contactId,
            SimPoint lastPosition,
            long lastSeenTick,
            int estimateMin,
            int estimateMax,
            bool isCurrentlyVisible)
        {
            ContactId = contactId;
            LastPosition = lastPosition;
            LastSeenTick = lastSeenTick;
            EstimateMin = estimateMin;
            EstimateMax = estimateMax;
            IsCurrentlyVisible = isCurrentlyVisible;
        }
    }

    /// <summary>Own army information only; Position is the army reference position.</summary>
    public readonly struct OwnArmyView
    {
        public uint Id { get; }
        public uint FactionId { get; }
        public UnitKind Kind { get; }
        public SimPoint Position { get; }
        public int AliveCount { get; }
        public PolicyGoal HomeObjective { get; }

        public OwnArmyView(
            uint id,
            uint factionId,
            UnitKind kind,
            SimPoint position,
            int aliveCount,
            PolicyGoal homeObjective)
        {
            Id = id;
            FactionId = factionId;
            Kind = kind;
            Position = position;
            AliveCount = aliveCount;
            HomeObjective = homeObjective;
        }
    }
    /// <summary>Known map location and observed asset information. Unknown owner/HP values are zero.</summary>
    public readonly struct KnownObjective
    {
        public GoalKind Kind { get; }
        public uint Id { get; }
        public SimPoint Position { get; }
        public bool IsOwnerKnown { get; }
        public uint OwnerFactionId { get; }
        public bool IsHpKnown { get; }
        public int Hp { get; }
        public long LastSeenTick { get; }

        public uint CapturingFactionId { get; }
        public int CaptureTicks { get; }
        public int CaptureDurationTicks { get; }

        public KnownObjective(
            GoalKind kind,
            uint id,
            SimPoint position,
            bool isOwnerKnown,
            uint ownerFactionId,
            bool isHpKnown,
            int hp,
            long lastSeenTick)
            : this(kind, id, position, isOwnerKnown, ownerFactionId, isHpKnown, hp, lastSeenTick, 0, 0, 0) { }

        public KnownObjective(GoalKind kind, uint id, SimPoint position, bool isOwnerKnown,
            uint ownerFactionId, bool isHpKnown, int hp, long lastSeenTick,
            uint capturingFactionId, int captureTicks, int captureDurationTicks)
        {
            CapturingFactionId = capturingFactionId;
            CaptureTicks = captureTicks;
            CaptureDurationTicks = captureDurationTicks;
            Kind = kind;
            Id = id;
            Position = position;
            IsOwnerKnown = isOwnerKnown;
            OwnerFactionId = ownerFactionId;
            IsHpKnown = isHpKnown;
            Hp = hp;
            LastSeenTick = lastSeenTick;
        }
    }
    public readonly struct PolicyView
    {
        public ulong CommandId { get; }
        public CommandSource Source { get; }
        public PolicyKind Kind { get; }
        public PolicyGoal Goal { get; }
        public LossBudget AllowedLoss { get; }
        public ushort ReservePermille { get; }

        public PolicyView(
            ulong commandId,
            CommandSource source,
            PolicyKind kind,
            PolicyGoal goal,
            LossBudget allowedLoss,
            ushort reservePermille)
        {
            CommandId = commandId;
            Source = source;
            Kind = kind;
            Goal = goal;
            AllowedLoss = allowedLoss;
            ReservePermille = reservePermille;
        }
    }
    /// <summary>Enemy soldiers are addressed by faction-local ContactId, never internal enemy IDs.</summary>
    public readonly struct ArmyIntent
    {
        public uint ArmyId { get; }
        public SimPoint MoveGoal { get; }
        public uint TargetContactId { get; }
        public PolicyGoal TargetObjective { get; }
        public bool IsRetreating { get; }

        public ArmyIntent(
            uint armyId,
            SimPoint moveGoal,
            uint targetContactId,
            PolicyGoal targetObjective,
            bool isRetreating)
        {
            ArmyId = armyId;
            MoveGoal = moveGoal;
            TargetContactId = targetContactId;
            TargetObjective = targetObjective;
            IsRetreating = isRetreating;
        }
    }

}
