using System;
using System.Collections.Generic;

namespace Rts.Contracts
{
    /// <summary>Ver.3 direct economy operations (technical-design-v3 6).</summary>
    public enum EconomyCommandKind : byte
    {
        PlaceBuilding = 1, Train = 2, CancelTrain = 3, AssignVillagers = 4,
        /// <summary>Turns the automatic economy of the faction on or off.</summary>
        SetAutoEconomy = 5
    }

    public enum EconomyTargetKind : byte { None = 0, ResourceNode = 1, Building = 2 }

    /// <summary>
    /// One direct economy operation from a player (or a script). Unlike a policy it is not interpreted and has no reply
    /// delay: the gateway logs it and the simulation applies it on the next tick, checking cost, ownership and room
    /// there. A rejected operation changes nothing, and replay reaches the same rejection from the same log.
    /// </summary>
    public sealed class EconomyCommand
    {
        public uint FactionId { get; }
        public ulong IssuerSequence { get; }
        public EconomyCommandKind Kind { get; }
        public BuildingKind Building { get; }
        /// <summary>PlaceBuilding: lower-left cell of the footprint.</summary>
        public int Cell { get; }
        /// <summary>Train and CancelTrain: 0 for the core, otherwise a building id.</summary>
        public uint ProducerId { get; }
        public UnitKind Unit { get; }
        public IReadOnlyList<uint> VillagerIds { get; }
        public EconomyTargetKind TargetKind { get; }
        public uint TargetId { get; }
        /// <summary>SetAutoEconomy: the new state.</summary>
        public bool Enabled { get; }

        public EconomyCommand(uint factionId, ulong issuerSequence, EconomyCommandKind kind, BuildingKind building, int cell,
            uint producerId, UnitKind unit, IReadOnlyList<uint> villagerIds, EconomyTargetKind targetKind, uint targetId, bool enabled)
        {
            FactionId = factionId;
            IssuerSequence = issuerSequence;
            Kind = kind;
            Building = building;
            Cell = cell;
            ProducerId = producerId;
            Unit = unit;
            VillagerIds = ContractList.Copy(villagerIds ?? Array.Empty<uint>());
            TargetKind = targetKind;
            TargetId = targetId;
            Enabled = enabled;
        }

        public static EconomyCommand Place(uint faction, ulong sequence, BuildingKind building, int cell)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.PlaceBuilding, building, cell, 0, 0, null, EconomyTargetKind.None, 0, false);

        public static EconomyCommand Train(uint faction, ulong sequence, uint producerId, UnitKind unit)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.Train, 0, 0, producerId, unit, null, EconomyTargetKind.None, 0, false);

        public static EconomyCommand CancelTrain(uint faction, ulong sequence, uint producerId)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.CancelTrain, 0, 0, producerId, 0, null, EconomyTargetKind.None, 0, false);

        public static EconomyCommand Assign(uint faction, ulong sequence, IReadOnlyList<uint> villagers, EconomyTargetKind target, uint targetId)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.AssignVillagers, 0, 0, 0, 0, villagers, target, targetId, false);

        public static EconomyCommand Auto(uint faction, ulong sequence, bool enabled)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.SetAutoEconomy, 0, 0, 0, 0, null, EconomyTargetKind.None, 0, enabled);
    }

    /// <summary>Where the display sends direct economy operations. Separate from ICommandPort so its implementers are untouched.</summary>
    public interface IEconomyPort
    {
        void SubmitEconomy(EconomyCommand command);
    }
}
