using System;
using System.Collections.Generic;

namespace Rts.Contracts
{
    /// <summary>Ver.3 direct economy operations (technical-design-v3 6).</summary>
    public enum EconomyCommandKind : byte
    {
        PlaceBuilding = 1, Train = 2, CancelTrain = 3, AssignVillagers = 4,
        /// <summary>Turns the automatic economy of the faction on or off.</summary>
        SetAutoEconomy = 5,
        /// <summary>V3-2: one run of belts (Cells with their Facings), as one drag on screen.</summary>
        PlaceBelt = 6,
        /// <summary>V3-2: takes the own belt off Cell; what it carried is lost.</summary>
        RemoveBelt = 7
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
        /// <summary>PlaceBelt: the cells of the run, in the order they are placed. Empty for every other kind.</summary>
        public IReadOnlyList<int> Cells { get; }
        /// <summary>PlaceBelt: one direction per cell.</summary>
        public IReadOnlyList<Facing> Facings { get; }

        public EconomyCommand(uint factionId, ulong issuerSequence, EconomyCommandKind kind, BuildingKind building, int cell,
            uint producerId, UnitKind unit, IReadOnlyList<uint> villagerIds, EconomyTargetKind targetKind, uint targetId, bool enabled)
            : this(factionId, issuerSequence, kind, building, cell, producerId, unit, villagerIds, targetKind, targetId, enabled, null, null)
        {
        }

        public EconomyCommand(uint factionId, ulong issuerSequence, EconomyCommandKind kind, BuildingKind building, int cell,
            uint producerId, UnitKind unit, IReadOnlyList<uint> villagerIds, EconomyTargetKind targetKind, uint targetId, bool enabled,
            IReadOnlyList<int> cells, IReadOnlyList<Facing> facings)
        {
            Cells = ContractList.Copy(cells ?? Array.Empty<int>());
            Facings = ContractList.Copy(facings ?? Array.Empty<Facing>());
            if (Cells.Count != Facings.Count) throw new ArgumentException("A belt run needs one facing per cell.");
            if (Cells.Count > MaxBeltRun) throw new ArgumentException("A belt run is at most " + MaxBeltRun + " cells.");
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

        public static EconomyCommand PlaceBelt(uint faction, ulong sequence, IReadOnlyList<int> cells, IReadOnlyList<Facing> facings)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.PlaceBelt, 0, 0, 0, 0, null, EconomyTargetKind.None, 0, false, cells, facings);

        public static EconomyCommand RemoveBelt(uint faction, ulong sequence, int cell)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.RemoveBelt, 0, cell, 0, 0, null, EconomyTargetKind.None, 0, false);

        /// <summary>The longest run one PlaceBelt may carry (a drag across the whole map is 128 cells).</summary>
        public const int MaxBeltRun = 256;
    }

    public enum VillagerActivity : byte { Idle = 0, ToResource = 1, Gathering = 2, Returning = 3, ToBuild = 4, Building = 5 }

    /// <summary>A villager on screen. Enemy villagers carry only a position (id 0, no HP or load), like enemy soldiers.</summary>
    public readonly struct VillagerView
    {
        public uint Id { get; }
        public bool IsOwn { get; }
        public SimPoint Position { get; }
        public VillagerActivity Activity { get; }
        public ResourceKind CarryKind { get; }
        public int Carry { get; }
        public int Hp { get; }

        public VillagerView(uint id, bool isOwn, SimPoint position, VillagerActivity activity, ResourceKind carryKind, int carry, int hp)
        {
            Id = id; IsOwn = isOwn; Position = position; Activity = activity; CarryKind = carryKind; Carry = carry; Hp = hp;
        }
    }

    /// <summary>An own building, or an enemy building the faction can see now.</summary>
    public readonly struct BuildingView
    {
        public uint Id { get; }
        public uint FactionId { get; }
        public BuildingKind Kind { get; }
        public SimPoint Center { get; }
        public int SizeMeters { get; }
        public int Hp { get; }
        public int MaxHp { get; }
        public bool Complete { get; }
        public int Progress { get; }
        public int Work { get; }
        public int Queued { get; }
        public long TrainRemaining { get; }

        public BuildingView(uint id, uint factionId, BuildingKind kind, SimPoint center, int sizeMeters, int hp, int maxHp,
            bool complete, int progress, int work, int queued, long trainRemaining)
        {
            Id = id; FactionId = factionId; Kind = kind; Center = center; SizeMeters = sizeMeters; Hp = hp; MaxHp = maxHp;
            Complete = complete; Progress = progress; Work = work; Queued = queued; TrainRemaining = trainRemaining;
        }
    }

    /// <summary>A resource point with something left. V3-1 shows every point to both sides (technical-design-v3 5.4).</summary>
    public readonly struct ResourceView
    {
        public uint Id { get; }
        public ResourceKind Kind { get; }
        public SimPoint Position { get; }
        public int Remaining { get; }

        public ResourceView(uint id, ResourceKind kind, SimPoint position, int remaining)
        {
            Id = id; Kind = kind; Position = position; Remaining = remaining;
        }
    }

    /// <summary>
    /// V3-2: a belt cell, own or enemy on a cell the faction sees now. Item is 0 when the cell is empty; Progress counts the
    /// ticks since the item entered the cell (it moves on at TicksPerCell), so the display can slide it along.
    /// </summary>
    public readonly struct BeltView
    {
        public int Cell { get; }
        public uint FactionId { get; }
        public Facing Facing { get; }
        public ResourceKind Item { get; }
        public int Progress { get; }

        public BeltView(int cell, uint factionId, Facing facing, ResourceKind item, int progress)
        {
            Cell = cell; FactionId = factionId; Facing = facing; Item = item; Progress = progress;
        }
    }

    /// <summary>The economy part of a faction frame. Null in a match without an economy.</summary>
    public sealed class EconomyView
    {
        public int Food { get; }
        public int Wood { get; }
        public int Population { get; }
        public int PopulationCap { get; }
        public int VillagerQueued { get; }
        public long VillagerTrainRemaining { get; }
        public bool AutoEconomy { get; }
        public int BuildingSizeCells { get; }
        public int BarracksWoodCost { get; }
        public int VillagerFoodCost { get; }
        public int InfantryFoodCost { get; }
        public int InfantryWoodCost { get; }
        public IReadOnlyList<VillagerView> Villagers { get; }
        public IReadOnlyList<BuildingView> Buildings { get; }
        public IReadOnlyList<ResourceView> Resources { get; }
        /// <summary>V3-2: false on a map without lines (then Ore, Metal and Belts stay empty).</summary>
        public bool Industry { get; }
        public int Ore { get; }
        public int Metal { get; }
        public int BeltWoodCost { get; }
        public int BeltTicksPerCell { get; }
        public IReadOnlyList<BeltView> Belts { get; }

        public EconomyView(int food, int wood, int population, int populationCap, int villagerQueued, long villagerTrainRemaining,
            bool autoEconomy, int buildingSizeCells, int barracksWoodCost, int villagerFoodCost, int infantryFoodCost, int infantryWoodCost,
            IReadOnlyList<VillagerView> villagers, IReadOnlyList<BuildingView> buildings, IReadOnlyList<ResourceView> resources)
            : this(food, wood, population, populationCap, villagerQueued, villagerTrainRemaining, autoEconomy, buildingSizeCells,
                barracksWoodCost, villagerFoodCost, infantryFoodCost, infantryWoodCost, villagers, buildings, resources,
                false, 0, 0, 0, 0, null)
        {
        }

        public EconomyView(int food, int wood, int population, int populationCap, int villagerQueued, long villagerTrainRemaining,
            bool autoEconomy, int buildingSizeCells, int barracksWoodCost, int villagerFoodCost, int infantryFoodCost, int infantryWoodCost,
            IReadOnlyList<VillagerView> villagers, IReadOnlyList<BuildingView> buildings, IReadOnlyList<ResourceView> resources,
            bool industry, int ore, int metal, int beltWoodCost, int beltTicksPerCell, IReadOnlyList<BeltView> belts)
        {
            Industry = industry; Ore = ore; Metal = metal; BeltWoodCost = beltWoodCost; BeltTicksPerCell = beltTicksPerCell;
            Belts = ContractList.Copy(belts ?? Array.Empty<BeltView>());
            Food = food; Wood = wood; Population = population; PopulationCap = populationCap;
            VillagerQueued = villagerQueued; VillagerTrainRemaining = villagerTrainRemaining; AutoEconomy = autoEconomy;
            BuildingSizeCells = buildingSizeCells; BarracksWoodCost = barracksWoodCost; VillagerFoodCost = villagerFoodCost;
            InfantryFoodCost = infantryFoodCost; InfantryWoodCost = infantryWoodCost;
            Villagers = ContractList.Copy(villagers ?? Array.Empty<VillagerView>());
            Buildings = ContractList.Copy(buildings ?? Array.Empty<BuildingView>());
            Resources = ContractList.Copy(resources ?? Array.Empty<ResourceView>());
        }
    }

    /// <summary>Where the display sends direct economy operations. Separate from ICommandPort so its implementers are untouched.</summary>
    public interface IEconomyPort
    {
        void SubmitEconomy(EconomyCommand command);
    }
}
