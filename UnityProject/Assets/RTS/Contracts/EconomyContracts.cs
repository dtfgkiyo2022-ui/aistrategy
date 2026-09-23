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
        /// <summary>V3-5: one run of wall cells (Cells; Facings unused, all north), as one drag on screen.</summary>
        PlaceWall = 11,
        /// <summary>V3-5: starts researching Tech at the blacksmith ProducerId.</summary>
        Research = 12,
        /// <summary>V3-5: at an own market, gives a lot of Give for Take (needs a finished market).</summary>
        Trade = 13,
        /// <summary>V3-2: takes the own belt off Cell; what it carried is lost.</summary>
        RemoveBelt = 7,
        /// <summary>V3-3: hands everything the player holds (villagers, buildings, belts, the core) back to the automatic economy.</summary>
        ReturnEconomyToAuto = 8,
        /// <summary>V3-3: sets the faction's economy policy (Policy).</summary>
        SetEconomyPolicy = 9,
        /// <summary>V3-4: starts advancing out of the primitive age into Civ, at the core.</summary>
        AdvanceAge = 10
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
        /// <summary>PlaceBuilding of a mine or smelter: the side its output comes out of. The barracks ignores it.</summary>
        public Facing Facing { get; }
        /// <summary>SetEconomyPolicy: the new policy.</summary>
        public EconomyPolicy Policy { get; }
        /// <summary>AdvanceAge: the civilisation to advance into.</summary>
        public CivKind Civ { get; }
        /// <summary>Research: the tech.</summary>
        public TechKind Tech { get; }
        /// <summary>Trade: what is given and what is taken.</summary>
        public ResourceKind Give { get; }
        public ResourceKind Take { get; }

        public EconomyCommand(uint factionId, ulong issuerSequence, EconomyCommandKind kind, BuildingKind building, int cell,
            uint producerId, UnitKind unit, IReadOnlyList<uint> villagerIds, EconomyTargetKind targetKind, uint targetId, bool enabled)
            : this(factionId, issuerSequence, kind, building, cell, producerId, unit, villagerIds, targetKind, targetId, enabled, null, null)
        {
        }

        public EconomyCommand(uint factionId, ulong issuerSequence, EconomyCommandKind kind, BuildingKind building, int cell,
            uint producerId, UnitKind unit, IReadOnlyList<uint> villagerIds, EconomyTargetKind targetKind, uint targetId, bool enabled,
            IReadOnlyList<int> cells, IReadOnlyList<Facing> facings)
            : this(factionId, issuerSequence, kind, building, cell, producerId, unit, villagerIds, targetKind, targetId, enabled, cells, facings, Facing.North)
        {
        }

        public EconomyCommand(uint factionId, ulong issuerSequence, EconomyCommandKind kind, BuildingKind building, int cell,
            uint producerId, UnitKind unit, IReadOnlyList<uint> villagerIds, EconomyTargetKind targetKind, uint targetId, bool enabled,
            IReadOnlyList<int> cells, IReadOnlyList<Facing> facings, Facing facing)
            : this(factionId, issuerSequence, kind, building, cell, producerId, unit, villagerIds, targetKind, targetId, enabled, cells, facings, facing, EconomyPolicy.Balanced)
        {
        }

        public EconomyCommand(uint factionId, ulong issuerSequence, EconomyCommandKind kind, BuildingKind building, int cell,
            uint producerId, UnitKind unit, IReadOnlyList<uint> villagerIds, EconomyTargetKind targetKind, uint targetId, bool enabled,
            IReadOnlyList<int> cells, IReadOnlyList<Facing> facings, Facing facing, EconomyPolicy policy)
            : this(factionId, issuerSequence, kind, building, cell, producerId, unit, villagerIds, targetKind, targetId, enabled, cells, facings, facing, policy, CivKind.Primitive)
        {
        }

        public EconomyCommand(uint factionId, ulong issuerSequence, EconomyCommandKind kind, BuildingKind building, int cell,
            uint producerId, UnitKind unit, IReadOnlyList<uint> villagerIds, EconomyTargetKind targetKind, uint targetId, bool enabled,
            IReadOnlyList<int> cells, IReadOnlyList<Facing> facings, Facing facing, EconomyPolicy policy, CivKind civ)
            : this(factionId, issuerSequence, kind, building, cell, producerId, unit, villagerIds, targetKind, targetId, enabled, cells, facings, facing, policy, civ, 0)
        {
        }

        public EconomyCommand(uint factionId, ulong issuerSequence, EconomyCommandKind kind, BuildingKind building, int cell,
            uint producerId, UnitKind unit, IReadOnlyList<uint> villagerIds, EconomyTargetKind targetKind, uint targetId, bool enabled,
            IReadOnlyList<int> cells, IReadOnlyList<Facing> facings, Facing facing, EconomyPolicy policy, CivKind civ, TechKind tech)
            : this(factionId, issuerSequence, kind, building, cell, producerId, unit, villagerIds, targetKind, targetId, enabled, cells, facings, facing, policy, civ, tech, 0, 0)
        {
        }

        public EconomyCommand(uint factionId, ulong issuerSequence, EconomyCommandKind kind, BuildingKind building, int cell,
            uint producerId, UnitKind unit, IReadOnlyList<uint> villagerIds, EconomyTargetKind targetKind, uint targetId, bool enabled,
            IReadOnlyList<int> cells, IReadOnlyList<Facing> facings, Facing facing, EconomyPolicy policy, CivKind civ, TechKind tech,
            ResourceKind give, ResourceKind take)
        {
            Give = give; Take = take;
            Tech = tech;
            Civ = civ;
            Policy = policy;
            Facing = facing;
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

        public static EconomyCommand Place(uint faction, ulong sequence, BuildingKind building, int cell, Facing facing)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.PlaceBuilding, building, cell, 0, 0, null, EconomyTargetKind.None, 0, false, null, null, facing);

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

        public static EconomyCommand SetPolicy(uint faction, ulong sequence, EconomyPolicy policy)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.SetEconomyPolicy, 0, 0, 0, 0, null, EconomyTargetKind.None, 0, false, null, null, Facing.North, policy);

        public static EconomyCommand Advance(uint faction, ulong sequence, CivKind civ)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.AdvanceAge, 0, 0, 0, 0, null, EconomyTargetKind.None, 0, false, null, null, Facing.North, EconomyPolicy.Balanced, civ);

        public static EconomyCommand ReturnToAuto(uint faction, ulong sequence)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.ReturnEconomyToAuto, 0, 0, 0, 0, null, EconomyTargetKind.None, 0, false);

        public static EconomyCommand Research(uint faction, ulong sequence, uint blacksmith, TechKind tech)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.Research, 0, 0, blacksmith, 0, null, EconomyTargetKind.None, 0, false, null, null, Facing.North, EconomyPolicy.Balanced, CivKind.Primitive, tech);

        public static EconomyCommand Trade(uint faction, ulong sequence, ResourceKind give, ResourceKind take)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.Trade, 0, 0, 0, 0, null, EconomyTargetKind.None, 0, false, null, null, Facing.North,
                EconomyPolicy.Balanced, CivKind.Primitive, 0, give, take);

        public static EconomyCommand PlaceWall(uint faction, ulong sequence, IReadOnlyList<int> cells)
        {
            var facings = new Facing[cells == null ? 0 : cells.Count];
            return new EconomyCommand(faction, sequence, EconomyCommandKind.PlaceWall, 0, 0, 0, 0, null, EconomyTargetKind.None, 0, false, cells, facings);
        }

        public static EconomyCommand RemoveBelt(uint faction, ulong sequence, int cell)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.RemoveBelt, 0, cell, 0, 0, null, EconomyTargetKind.None, 0, false);

        /// <summary>The longest run one PlaceBelt may carry (a drag across the whole map is 128 cells).</summary>
        public const int MaxBeltRun = 256;
    }

    /// <summary>Hauling (V3-2) covers carrying by hand between a mine, a smelter and the core.</summary>
    public enum VillagerActivity : byte { Idle = 0, ToResource = 1, Gathering = 2, Returning = 3, ToBuild = 4, Building = 5, Hauling = 6 }

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
        /// <summary>V3-3: the player assigned this own villager; the automatic economy leaves it alone.</summary>
        public bool PlayerHeld { get; }

        public VillagerView(uint id, bool isOwn, SimPoint position, VillagerActivity activity, ResourceKind carryKind, int carry, int hp)
            : this(id, isOwn, position, activity, carryKind, carry, hp, false)
        {
        }

        public VillagerView(uint id, bool isOwn, SimPoint position, VillagerActivity activity, ResourceKind carryKind, int carry, int hp, bool playerHeld)
        {
            Id = id; IsOwn = isOwn; Position = position; Activity = activity; CarryKind = carryKind; Carry = carry; Hp = hp; PlayerHeld = playerHeld;
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

        /// <summary>V3-2: the side a mine or smelter puts its output out of.</summary>
        public Facing Facing { get; }
        /// <summary>V3-2, own buildings only: ore waiting at a smelter's input, and items waiting at the output.</summary>
        public int Input { get; }
        public int Output { get; }

        public BuildingView(uint id, uint factionId, BuildingKind kind, SimPoint center, int sizeMeters, int hp, int maxHp,
            bool complete, int progress, int work, int queued, long trainRemaining)
            : this(id, factionId, kind, center, sizeMeters, hp, maxHp, complete, progress, work, queued, trainRemaining, Facing.North, 0, 0)
        {
        }

        /// <summary>V3-3: an own building the player placed or operated.</summary>
        public bool PlayerHeld { get; }
        /// <summary>V3-5 blacksmith: the tech being researched (0 when none) and the ticks left.</summary>
        public TechKind Researching { get; }
        public long ResearchRemaining { get; }

        public BuildingView(uint id, uint factionId, BuildingKind kind, SimPoint center, int sizeMeters, int hp, int maxHp,
            bool complete, int progress, int work, int queued, long trainRemaining, Facing facing, int input, int output)
            : this(id, factionId, kind, center, sizeMeters, hp, maxHp, complete, progress, work, queued, trainRemaining, facing, input, output, false)
        {
        }

        public BuildingView(uint id, uint factionId, BuildingKind kind, SimPoint center, int sizeMeters, int hp, int maxHp,
            bool complete, int progress, int work, int queued, long trainRemaining, Facing facing, int input, int output, bool playerHeld)
            : this(id, factionId, kind, center, sizeMeters, hp, maxHp, complete, progress, work, queued, trainRemaining, facing, input, output, playerHeld, 0, 0)
        {
        }

        public BuildingView(uint id, uint factionId, BuildingKind kind, SimPoint center, int sizeMeters, int hp, int maxHp,
            bool complete, int progress, int work, int queued, long trainRemaining, Facing facing, int input, int output, bool playerHeld,
            TechKind researching, long researchRemaining)
        {
            Researching = researching; ResearchRemaining = researchRemaining;
            PlayerHeld = playerHeld;
            Facing = facing; Input = input; Output = output;
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
        /// <summary>V3-3: an own belt the player laid; the automatic line goes around it.</summary>
        public bool PlayerHeld { get; }

        public BeltView(int cell, uint factionId, Facing facing, ResourceKind item, int progress)
            : this(cell, factionId, facing, item, progress, false)
        {
        }

        public BeltView(int cell, uint factionId, Facing facing, ResourceKind item, int progress, bool playerHeld)
        {
            Cell = cell; FactionId = factionId; Facing = facing; Item = item; Progress = progress; PlayerHeld = playerHeld;
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
        /// <summary>V3-2 costs and footprints for the placement buttons.</summary>
        public int InfantryMetalCost { get; }
        public int MineWoodCost { get; }
        public int SmelterWoodCost { get; }
        public int MineSizeCells { get; }
        public int SmelterSizeCells { get; }
        /// <summary>V3-3: the player trains villagers at the core by hand; the automatic economy does not.</summary>
        public bool CorePlayerHeld { get; }
        /// <summary>V3-3: the faction's economy policy (always Balanced without industry).</summary>
        public EconomyPolicy Policy { get; }
        /// <summary>V3-4: false on a map without ages (then Civ stays Primitive and means nothing).</summary>
        public bool Ages { get; }
        public CivKind Civ { get; }
        /// <summary>V3-4: while advancing, the civilisation chosen and the ticks left; otherwise Primitive and 0.</summary>
        public CivKind AdvancingTo { get; }
        public long AdvanceRemaining { get; }
        public int AdvanceFoodCost { get; }
        public int AdvanceWoodCost { get; }
        /// <summary>V3-4: the farm (agrarian civilisation only).</summary>
        public int FarmWoodCost { get; }
        public int FarmSizeCells { get; }
        /// <summary>V3-5: a scout at the barracks (maps with ages only; 0 otherwise).</summary>
        public int ScoutFoodCost { get; }
        /// <summary>V3-5: a house (maps with ages only; 0 otherwise).</summary>
        public int HouseWoodCost { get; }
        /// <summary>V3-5: a resource drop-off (maps with ages only; 0 otherwise).</summary>
        public int DropSiteWoodCost { get; }
        /// <summary>V3-5 stone and defences (maps with ages only; 0 otherwise).</summary>
        public int Stone { get; }
        public int WallStoneCost { get; }
        public int TowerWoodCost { get; }
        public int TowerStoneCost { get; }
        /// <summary>V3-5 research: the blacksmith's wood, the techs researched (bit 1 &lt;&lt; (TechKind - 1)), and each tech's
        /// food and wood (index TechKind - 1). Empty without ages.</summary>
        public int BlacksmithWoodCost { get; }
        public ulong Techs { get; }
        public IReadOnlyList<int> TechFoodCosts { get; }
        public IReadOnlyList<int> TechWoodCosts { get; }
        /// <summary>V3-5 (32 #14): the metal a tech costs - 0 for all but the steel ones.</summary>
        public IReadOnlyList<int> TechMetalCosts { get; }
        /// <summary>V3-5 (32 #7, #8): the age inside the civilisation (0 primitive, 1, 2), the price of the second age, and the
        /// civilisations' own units.</summary>
        public int Age { get; }
        public int Age2FoodCost { get; }
        public int Age2WoodCost { get; }
        public int ArcherFoodCost { get; }
        public int ArcherWoodCost { get; }
        public int CavalryFoodCost { get; }
        public int CavalryWoodCost { get; }
        public int CavalryMetalCost { get; }
        /// <summary>V3-5 (32 #9): the market and the siege workshop, a trade (give TradeLot, take TradeReturn), and the ram.</summary>
        public int MarketWoodCost { get; }
        public int WorkshopWoodCost { get; }
        public int TradeLot { get; }
        public int TradeReturn { get; }
        public int RamFoodCost { get; }
        public int RamWoodCost { get; }

        /// <summary>V3-5 (32 #10): the price of the third age (trade age for farming, steel age for metallurgy).</summary>
        public int Age3FoodCost { get; }
        public int Age3WoodCost { get; }

        /// <summary>V3-5 (32 #12): the archery range and the stable.</summary>
        public int RangeWoodCost { get; }
        public int StableWoodCost { get; }

        public EconomyView(int food, int wood, int population, int populationCap, int villagerQueued, long villagerTrainRemaining,
            bool autoEconomy, int buildingSizeCells, int barracksWoodCost, int villagerFoodCost, int infantryFoodCost, int infantryWoodCost,
            IReadOnlyList<VillagerView> villagers, IReadOnlyList<BuildingView> buildings, IReadOnlyList<ResourceView> resources)
            : this(food, wood, population, populationCap, villagerQueued, villagerTrainRemaining, autoEconomy, buildingSizeCells,
                barracksWoodCost, villagerFoodCost, infantryFoodCost, infantryWoodCost, villagers, buildings, resources,
                false, 0, 0, 0, 0, null, 0, 0, 0, 0, 0, false, EconomyPolicy.Balanced, false, CivKind.Primitive, CivKind.Primitive, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
        {
        }

        public EconomyView(int food, int wood, int population, int populationCap, int villagerQueued, long villagerTrainRemaining,
            bool autoEconomy, int buildingSizeCells, int barracksWoodCost, int villagerFoodCost, int infantryFoodCost, int infantryWoodCost,
            IReadOnlyList<VillagerView> villagers, IReadOnlyList<BuildingView> buildings, IReadOnlyList<ResourceView> resources,
            bool industry, int ore, int metal, int beltWoodCost, int beltTicksPerCell, IReadOnlyList<BeltView> belts,
            int infantryMetalCost, int mineWoodCost, int smelterWoodCost, int mineSizeCells, int smelterSizeCells, bool corePlayerHeld,
            EconomyPolicy policy, bool ages, CivKind civ, CivKind advancingTo, long advanceRemaining, int advanceFoodCost, int advanceWoodCost,
            int farmWoodCost, int farmSizeCells, int scoutFoodCost, int houseWoodCost, int dropSiteWoodCost,
            int stone, int wallStoneCost, int towerWoodCost, int towerStoneCost,
            int blacksmithWoodCost, ulong techs, IReadOnlyList<int> techFoodCosts, IReadOnlyList<int> techWoodCosts, IReadOnlyList<int> techMetalCosts,
            int age, int age2FoodCost, int age2WoodCost, int archerFoodCost, int archerWoodCost, int cavalryFoodCost, int cavalryWoodCost, int cavalryMetalCost,
            int marketWoodCost, int workshopWoodCost, int tradeLot, int tradeReturn, int ramFoodCost, int ramWoodCost,
            int age3FoodCost, int age3WoodCost, int rangeWoodCost, int stableWoodCost)
        {
            MarketWoodCost = marketWoodCost; WorkshopWoodCost = workshopWoodCost; TradeLot = tradeLot; TradeReturn = tradeReturn;
            RamFoodCost = ramFoodCost; RamWoodCost = ramWoodCost;
            Age3FoodCost = age3FoodCost; Age3WoodCost = age3WoodCost;
            RangeWoodCost = rangeWoodCost; StableWoodCost = stableWoodCost;
            Age = age; Age2FoodCost = age2FoodCost; Age2WoodCost = age2WoodCost; ArcherFoodCost = archerFoodCost; ArcherWoodCost = archerWoodCost;
            CavalryFoodCost = cavalryFoodCost; CavalryWoodCost = cavalryWoodCost; CavalryMetalCost = cavalryMetalCost;
            BlacksmithWoodCost = blacksmithWoodCost; Techs = techs;
            TechFoodCosts = ContractList.Copy(techFoodCosts ?? Array.Empty<int>());
            TechWoodCosts = ContractList.Copy(techWoodCosts ?? Array.Empty<int>());
            TechMetalCosts = ContractList.Copy(techMetalCosts ?? Array.Empty<int>());
            Stone = stone; WallStoneCost = wallStoneCost; TowerWoodCost = towerWoodCost; TowerStoneCost = towerStoneCost;
            DropSiteWoodCost = dropSiteWoodCost;
            HouseWoodCost = houseWoodCost;
            ScoutFoodCost = scoutFoodCost;
            FarmWoodCost = farmWoodCost; FarmSizeCells = farmSizeCells;
            Ages = ages; Civ = civ; AdvancingTo = advancingTo; AdvanceRemaining = advanceRemaining;
            AdvanceFoodCost = advanceFoodCost; AdvanceWoodCost = advanceWoodCost;
            CorePlayerHeld = corePlayerHeld;
            Policy = policy;
            InfantryMetalCost = infantryMetalCost; MineWoodCost = mineWoodCost; SmelterWoodCost = smelterWoodCost;
            MineSizeCells = mineSizeCells; SmelterSizeCells = smelterSizeCells;
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
