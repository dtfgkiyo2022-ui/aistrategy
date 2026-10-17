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
        /// <summary>V3-5 32 #18: sends living own villagers between the own market and core.</summary>
        TradeRoute = 14,
        /// <summary>V3-2: takes the own belt off Cell; what it carried is lost.</summary>
        RemoveBelt = 7,
        /// <summary>V3-3: hands everything the player holds (villagers, buildings, belts, the core) back to the automatic economy.</summary>
        ReturnEconomyToAuto = 8,
        /// <summary>V3-3: sets the faction's economy policy (Policy).</summary>
        SetEconomyPolicy = 9,
        /// <summary>V3-4: starts advancing out of the primitive age into Civ, at the core.</summary>
        AdvanceAge = 10,
        /// <summary>V3-6: returns one processing line to the automatic economy.</summary>
        ReturnLineToAuto = 15,
        /// <summary>V3-6: changes the output direction of an own processing building.</summary>
        RotateBuilding = 16,
        /// <summary>V3-6: removes an own building without refunding its cost.</summary>
        RemoveBuilding = 17,
        /// <summary>V3-9 #1: sends living own villagers between a fixed market and caravanserai.</summary>
        CaravanRoute = 18,
        /// <summary>S-5: changes the human/AI owner of one geographic region.</summary>
        SetRegionControl = 19,
        /// <summary>V3-20: places a splitter, sorter, or paired underground belt.</summary>
        PlaceBeltComponent = 20,
        /// <summary>V3-20: removes any own belt cell, including a component.</summary>
        RemoveBeltComponent = 21,
        /// <summary>V3-6 follow-up: asks the automatic economy to build one processing line near a region or cell.</summary>
        RequestLine = 22,
        /// <summary>Manual control: turns the built-in late assault and push waves on or off.</summary>
        SetAutoAssault = 23,
        /// <summary>Manual control: stops every own villager and holds the current work state.</summary>
        StopVillagers = 24,
        /// <summary>CraftTree1: upgrades an own processing building from level 1 to level 2.</summary>
        UpgradeBuilding = 25,
        /// <summary>CraftTree2: selects the recipe of an assembler.</summary>
        SetRecipe = 26,
        /// <summary>AgeClockUx: pays the current discounted age price and completes the advance immediately.</summary>
        AdvanceNow = 27,
        /// <summary>EasyRebuild: moves an own completed building without changing its production state.</summary>
        MoveBuilding = 28
    }

    /// <summary>V3-6: who owns the next edit of an automatic processing line.</summary>
    public enum LineManager : byte { Automatic = 0, Manual = 1 }

    public enum EconomyTargetKind : byte { None = 0, ResourceNode = 1, Building = 2, Region = 3 }

    /// <summary>Public names for the processing lines that can be requested by a person, staff officer, or tactic.</summary>
public enum ProcessingLineKind : byte { CoreMetal = 1, Steel = 2, CoreWood = 3, BowGear = 4, Rations = 5, Bricks = 6, Tack = 7, Planks = 8, Books = 9, Preserves = 10, Jewels = 11, TradeGoods = 12, Incense = 13, Charms = 14, RepairKits = 15, Tools = 16, CutStone = 17, Frames = 18, Assembler = 19 }

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
        /// <summary>Train and CancelTrain: 0 for the core, otherwise a building id. AssignVillagers: optional haul destination.</summary>
        public uint ProducerId { get; }
        /// <summary>V3-6: the processing line targeted by ReturnLineToAuto.</summary>
        public uint LineId => ProducerId;
        /// <summary>AssignVillagers: when the target is a source building, ProducerId is the optional destination building.</summary>
        public uint HaulToId => ProducerId;
        public UnitKind Unit { get; }
        public IReadOnlyList<uint> VillagerIds { get; }
        public EconomyTargetKind TargetKind { get; }
        public uint TargetId { get; }
        /// <summary>V3-20: the paired exit cell for an underground entrance.</summary>
        public int PairCell => unchecked((int)ProducerId);
        /// <summary>V3-20/V3-34.3: the resource selected by a sorter or inserter.</summary>
        public ResourceKind SorterKind => (ResourceKind)(TargetId & 0x7fffffffU);
        /// <summary>V3-34.3: true for an inserter placement using the shared component command.</summary>
        public bool IsInserter => Kind == EconomyCommandKind.PlaceBeltComponent && (TargetId & 0x80000000U) != 0;
        /// <summary>SetAutoEconomy: the new state.</summary>
        public bool Enabled { get; }
        /// <summary>V3-20: true on a fast-belt placement command.</summary>
        public bool FastBelt => Enabled && (Kind == EconomyCommandKind.PlaceBelt || Kind == EconomyCommandKind.PlaceBeltComponent);
        /// <summary>S-5: the region targeted by SetRegionControl or a regional economy policy.</summary>
        public uint RegionId => TargetId;
        /// <summary>S-5: the requested owner of RegionId.</summary>
        public RegionControl Control => Enabled ? RegionControl.Human : RegionControl.Ai;
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
        /// <summary>RequestLine: the line to build. Zero for other economy commands.</summary>
        public ProcessingLineKind Line { get; }
        /// <summary>SetRecipe: the selected assembler recipe.</summary>
        public RecipeKind Recipe { get; }

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
            : this(factionId, issuerSequence, kind, building, cell, producerId, unit, villagerIds, targetKind, targetId, enabled,
                cells, facings, facing, policy, civ, tech, give, take, (ProcessingLineKind)0, RecipeKind.None)
        {
        }

        public EconomyCommand(uint factionId, ulong issuerSequence, EconomyCommandKind kind, BuildingKind building, int cell,
            uint producerId, UnitKind unit, IReadOnlyList<uint> villagerIds, EconomyTargetKind targetKind, uint targetId, bool enabled,
            IReadOnlyList<int> cells, IReadOnlyList<Facing> facings, Facing facing, EconomyPolicy policy, CivKind civ, TechKind tech,
            ResourceKind give, ResourceKind take, ProcessingLineKind line)
            : this(factionId, issuerSequence, kind, building, cell, producerId, unit, villagerIds, targetKind, targetId, enabled,
                cells, facings, facing, policy, civ, tech, give, take, line, RecipeKind.None)
        {
        }

        public EconomyCommand(uint factionId, ulong issuerSequence, EconomyCommandKind kind, BuildingKind building, int cell,
            uint producerId, UnitKind unit, IReadOnlyList<uint> villagerIds, EconomyTargetKind targetKind, uint targetId, bool enabled,
            IReadOnlyList<int> cells, IReadOnlyList<Facing> facings, Facing facing, EconomyPolicy policy, CivKind civ, TechKind tech,
            ResourceKind give, ResourceKind take, ProcessingLineKind line, RecipeKind recipe)
        {
            Give = give; Take = take;
            Line = line;
            Recipe = recipe;
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

        /// <summary>Assigns villagers to carry from a processing source to a chosen processing destination.</summary>
        public static EconomyCommand AssignHaul(uint faction, ulong sequence, IReadOnlyList<uint> villagers, uint sourceId, uint destinationId)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.AssignVillagers, 0, 0, destinationId, 0, villagers,
                EconomyTargetKind.Building, sourceId, false);

        public static EconomyCommand Auto(uint faction, ulong sequence, bool enabled)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.SetAutoEconomy, 0, 0, 0, 0, null, EconomyTargetKind.None, 0, enabled);

        /// <summary>Manual control: turns the built-in late assault and push waves on or off.</summary>
        public static EconomyCommand SetAutoAssault(uint faction, ulong sequence, bool enabled)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.SetAutoAssault, 0, 0, 0, 0, null, EconomyTargetKind.None, 0, enabled);

        /// <summary>Manual control: stops every own villager at its current position.</summary>
        public static EconomyCommand StopVillagers(uint faction, ulong sequence)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.StopVillagers, 0, 0, 0, 0, null, EconomyTargetKind.None, 0, false);

        public static EconomyCommand PlaceBelt(uint faction, ulong sequence, IReadOnlyList<int> cells, IReadOnlyList<Facing> facings)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.PlaceBelt, 0, 0, 0, 0, null, EconomyTargetKind.None, 0, false, cells, facings);

        /// <summary>V3-20: places a normal or fast belt run.</summary>
        public static EconomyCommand PlaceBelt(uint faction, ulong sequence, IReadOnlyList<int> cells, IReadOnlyList<Facing> facings, BeltSpeed speed)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.PlaceBelt, 0, 0, 0, 0, null, EconomyTargetKind.None, 0,
                speed == BeltSpeed.Fast, cells, facings);

        public static EconomyCommand PlaceFastBelt(uint faction, ulong sequence, IReadOnlyList<int> cells, IReadOnlyList<Facing> facings)
            => PlaceBelt(faction, sequence, cells, facings, BeltSpeed.Fast);

        public static EconomyCommand PlaceSplitter(uint faction, ulong sequence, int cell, Facing facing)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.PlaceBeltComponent, 0, cell, 0, 0, null,
                EconomyTargetKind.None, 0, false, null, null, facing);

        public static EconomyCommand PlaceSorter(uint faction, ulong sequence, int cell, Facing facing, ResourceKind kind)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.PlaceBeltComponent, 0, cell, 0, 0, null,
                EconomyTargetKind.None, (uint)kind, false, null, null, facing);

        public static EconomyCommand PlaceInserter(uint faction, ulong sequence, int cell, Facing facing, ResourceKind kind)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.PlaceBeltComponent, 0, cell, 0, 0, null,
                EconomyTargetKind.None, 0x80000000U | (uint)kind, false, null, null, facing);

        /// <summary>Places an entrance and its exit together. The simulation validates the ray and range.</summary>
        public static EconomyCommand PlaceUnderground(uint faction, ulong sequence, int entrance, int exit, Facing facing)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.PlaceBeltComponent, 0, entrance, unchecked((uint)exit), 0, null,
                EconomyTargetKind.None, 0, false, new[] { entrance, exit }, new[] { facing, facing }, facing);

        public static EconomyCommand PlaceStorage(uint faction, ulong sequence, int origin)
            => Place(faction, sequence, BuildingKind.Storage, origin, Facing.North);

        public static EconomyCommand SetPolicy(uint faction, ulong sequence, EconomyPolicy policy)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.SetEconomyPolicy, 0, 0, 0, 0, null, EconomyTargetKind.None, 0, false, null, null, Facing.North, policy);

        /// <summary>S-5: sets the automatic economy policy for one region.</summary>
        public static EconomyCommand SetRegionPolicy(uint faction, ulong sequence, uint regionId, EconomyPolicy policy)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.SetEconomyPolicy, 0, 0, 0, 0, null, EconomyTargetKind.None, regionId, false, null, null, Facing.North, policy);

        /// <summary>S-5: gives one region to the human or back to the automatic economy.</summary>
        public static EconomyCommand SetRegionControl(uint faction, ulong sequence, uint regionId, RegionControl control)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.SetRegionControl, 0, 0, 0, 0, null,
                EconomyTargetKind.None, regionId, control == RegionControl.Human);

        public static EconomyCommand Advance(uint faction, ulong sequence, CivKind civ)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.AdvanceAge, 0, 0, 0, 0, null, EconomyTargetKind.None, 0, false, null, null, Facing.North, EconomyPolicy.Balanced, civ);

        /// <summary>AgeClockUx: pays the current age-clock price and completes the advance in this command tick.</summary>
        public static EconomyCommand AdvanceNow(uint faction, ulong sequence, CivKind civ = CivKind.Primitive)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.AdvanceNow, 0, 0, 0, 0, null, EconomyTargetKind.None, 0, false, null, null, Facing.North, EconomyPolicy.Balanced, civ);

        public static EconomyCommand ReturnToAuto(uint faction, ulong sequence)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.ReturnEconomyToAuto, 0, 0, 0, 0, null, EconomyTargetKind.None, 0, false);

        public static EconomyCommand ReturnLineToAuto(uint faction, ulong sequence, uint lineId)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.ReturnLineToAuto, 0, 0, lineId, 0, null, EconomyTargetKind.None, 0, false);

        public static EconomyCommand RotateBuilding(uint faction, ulong sequence, uint buildingId, Facing facing)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.RotateBuilding, 0, 0, buildingId, 0, null,
                EconomyTargetKind.None, 0, false, null, null, facing);

        public static EconomyCommand MoveBuilding(uint faction, ulong sequence, uint buildingId, int cell, Facing facing)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.MoveBuilding, 0, cell, buildingId, 0, null,
                EconomyTargetKind.None, 0, false, null, null, facing);

        public static EconomyCommand RemoveBuilding(uint faction, ulong sequence, uint buildingId)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.RemoveBuilding, 0, 0, buildingId, 0, null,
                EconomyTargetKind.None, 0, false);

        public static EconomyCommand UpgradeBuilding(uint faction, ulong sequence, uint buildingId)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.UpgradeBuilding, 0, 0, buildingId, 0, null,
                EconomyTargetKind.None, 0, false);

        public static EconomyCommand SetRecipe(uint faction, ulong sequence, uint assemblerId, RecipeKind recipe)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.SetRecipe, BuildingKind.Assembler, 0, assemblerId, 0, null,
                EconomyTargetKind.None, 0, false, null, null, Facing.North, EconomyPolicy.Balanced, CivKind.Primitive,
                0, 0, 0, 0, recipe);

        public static EconomyCommand Research(uint faction, ulong sequence, uint blacksmith, TechKind tech)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.Research, 0, 0, blacksmith, 0, null, EconomyTargetKind.None, 0, false, null, null, Facing.North, EconomyPolicy.Balanced, CivKind.Primitive, tech);

        public static EconomyCommand Trade(uint faction, ulong sequence, ResourceKind give, ResourceKind take)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.Trade, 0, 0, 0, 0, null, EconomyTargetKind.None, 0, false, null, null, Facing.North,
                EconomyPolicy.Balanced, CivKind.Primitive, 0, give, take);

        public static EconomyCommand TradeRoute(uint faction, ulong sequence, IReadOnlyList<uint> villagers)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.TradeRoute, 0, 0, 0, 0, villagers, EconomyTargetKind.None, 0, false);

        /// <summary>V3-9 #1: sends villagers to the fixed market/Outpost pair stored by caravanseraiId.</summary>
        public static EconomyCommand CaravanRoute(uint faction, ulong sequence, uint caravanseraiId, IReadOnlyList<uint> villagers)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.CaravanRoute, 0, 0, caravanseraiId, 0, villagers, EconomyTargetKind.None, 0, false);

        /// <summary>Requests an automatic line near a region. The region is resolved by the simulation's deterministic map table.</summary>
        public static EconomyCommand RequestLine(uint faction, ulong sequence, ProcessingLineKind line, uint regionId)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.RequestLine, 0, 0, 0, 0, null,
                EconomyTargetKind.Region, regionId, false, null, null, Facing.North, EconomyPolicy.Balanced,
                CivKind.Primitive, 0, 0, 0, line);

        /// <summary>Requests an automatic line near a map cell selected by the person.</summary>
        public static EconomyCommand RequestLineAt(uint faction, ulong sequence, ProcessingLineKind line, int cell)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.RequestLine, 0, cell, 0, 0, null,
                EconomyTargetKind.None, 0, false, null, null, Facing.North, EconomyPolicy.Balanced,
                CivKind.Primitive, 0, 0, 0, line);

        public static EconomyCommand PlaceWall(uint faction, ulong sequence, IReadOnlyList<int> cells)
        {
            var facings = new Facing[cells == null ? 0 : cells.Count];
            return new EconomyCommand(faction, sequence, EconomyCommandKind.PlaceWall, 0, 0, 0, 0, null, EconomyTargetKind.None, 0, false, cells, facings);
        }

        /// <summary>V3-11 #1: places a short straight bridge on the supplied river cells.</summary>
        public static EconomyCommand PlaceBridge(uint faction, ulong sequence, IReadOnlyList<int> cells, Facing facing)
        {
            var bridgeCells = cells ?? System.Array.Empty<int>();
            var facings = new Facing[bridgeCells.Count];
            for (int i = 0; i < facings.Length; i++) facings[i] = facing;
            return new EconomyCommand(faction, sequence, EconomyCommandKind.PlaceBuilding, BuildingKind.Bridge,
                bridgeCells.Count == 0 ? 0 : bridgeCells[0], 0, 0, null, EconomyTargetKind.None, 0, false,
                bridgeCells, facings, facing);
        }

        public static EconomyCommand RemoveBelt(uint faction, ulong sequence, int cell)
            => new EconomyCommand(faction, sequence, EconomyCommandKind.RemoveBelt, 0, cell, 0, 0, null, EconomyTargetKind.None, 0, false);

        /// <summary>The longest run one PlaceBelt may carry (a drag across the whole map is 128 cells).</summary>
        public const int MaxBeltRun = 256;
    }

    /// <summary>Hauling (V3-2) covers carrying by hand between a mine, a smelter and the core.</summary>
    public enum VillagerActivity : byte { Idle = 0, ToResource = 1, Gathering = 2, Returning = 3, ToBuild = 4, Building = 5, Hauling = 6, Trading = 7 }

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
        /// <summary>V3-9 #3: fixed caravan pair, stage, dedicated cargo and stop reason.</summary>
        public uint CaravanMarketId { get; }
        public uint CaravanseraiId { get; }
        public uint CaravanOutpostId { get; }
        public int CaravanStage { get; }
        public int CaravanWood { get; }
        public int CaravanGems { get; }
        public CaravanStopReason CaravanStopReason { get; }

        public VillagerView(uint id, bool isOwn, SimPoint position, VillagerActivity activity, ResourceKind carryKind, int carry, int hp)
            : this(id, isOwn, position, activity, carryKind, carry, hp, false)
        {
        }

        public VillagerView(uint id, bool isOwn, SimPoint position, VillagerActivity activity, ResourceKind carryKind, int carry, int hp, bool playerHeld)
            : this(id, isOwn, position, activity, carryKind, carry, hp, playerHeld, 0, 0, 0, 0, 0, 0, CaravanStopReason.None)
        {
        }

        public VillagerView(uint id, bool isOwn, SimPoint position, VillagerActivity activity, ResourceKind carryKind, int carry, int hp, bool playerHeld,
            uint caravanMarketId, uint caravanseraiId, uint caravanOutpostId, int caravanStage, int caravanWood, int caravanGems,
            CaravanStopReason caravanStopReason)
        {
            Id = id; IsOwn = isOwn; Position = position; Activity = activity; CarryKind = carryKind; Carry = carry; Hp = hp; PlayerHeld = playerHeld;
            CaravanMarketId = caravanMarketId; CaravanseraiId = caravanseraiId; CaravanOutpostId = caravanOutpostId;
            CaravanStage = caravanStage; CaravanWood = caravanWood; CaravanGems = caravanGems; CaravanStopReason = caravanStopReason;
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
        /// <summary>CraftTree1: processing-building level; 1 for level-one or non-processing buildings.</summary>
        public int Level { get; }

        /// <summary>V3-2: the side a mine or smelter puts its output out of.</summary>
        public Facing Facing { get; }
        /// <summary>V3-2, own buildings only: ore waiting at a smelter's input, and items waiting at the output.</summary>
        public int Input { get; }
        /// <summary>V3-6: the second material slot (charcoal at a steelworks); zero for earlier buildings.</summary>
        public int InputSecondary { get; }
        public int InputTertiary { get; }
        public RecipeKind Recipe { get; }
        public int Output { get; }
        /// <summary>V3-7: BowGear reserved by SkirmishArcher training.</summary>
        public int QueuedBowGear { get; }
        /// <summary>V3-9 #3: the fixed market/outpost pair and the provisional reward.</summary>
        public uint CaravanMarketId { get; }
        public uint CaravanOutpostId { get; }
        public Fix64 CaravanDistance { get; }
        public int CaravanWoodReward { get; }
        public CaravanStopReason CaravanStopReason { get; }

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
            : this(id, factionId, kind, center, sizeMeters, hp, maxHp, complete, progress, work, queued, trainRemaining, facing, input, output, playerHeld,
                researching, researchRemaining, 0)
        {
        }

        public BuildingView(uint id, uint factionId, BuildingKind kind, SimPoint center, int sizeMeters, int hp, int maxHp,
            bool complete, int progress, int work, int queued, long trainRemaining, Facing facing, int input, int output, bool playerHeld,
            TechKind researching, long researchRemaining, int inputSecondary, int queuedBowGear = 0)
            : this(id, factionId, kind, center, sizeMeters, hp, maxHp, complete, progress, work, queued, trainRemaining, facing, input, output,
                playerHeld, researching, researchRemaining, inputSecondary, queuedBowGear, 0, 0, default, 0, CaravanStopReason.None)
        {
        }

        public BuildingView(uint id, uint factionId, BuildingKind kind, SimPoint center, int sizeMeters, int hp, int maxHp,
            bool complete, int progress, int work, int queued, long trainRemaining, Facing facing, int input, int output, bool playerHeld,
            TechKind researching, long researchRemaining, int inputSecondary, int queuedBowGear,
            uint caravanMarketId, uint caravanOutpostId, Fix64 caravanDistance, int caravanWoodReward, CaravanStopReason caravanStopReason,
            int level = 1, int inputTertiary = 0, RecipeKind recipe = RecipeKind.None)
        {
            Researching = researching; ResearchRemaining = researchRemaining;
            PlayerHeld = playerHeld;
            Facing = facing; Input = input; InputSecondary = inputSecondary; InputTertiary = inputTertiary; Recipe = recipe; Output = output;
            QueuedBowGear = queuedBowGear;
            CaravanMarketId = caravanMarketId; CaravanOutpostId = caravanOutpostId; CaravanDistance = caravanDistance;
            CaravanWoodReward = caravanWoodReward; CaravanStopReason = caravanStopReason;
            Level = level;
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

    /// <summary>One finished good's displayed return for a single market lot, after the faction's modifiers.</summary>
    public readonly struct GoodsPriceView
    {
        public ResourceKind Goods { get; }
        public int Amount { get; }

        public GoodsPriceView(ResourceKind goods, int amount)
        {
            Goods = goods;
            Amount = amount;
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
        public BeltComponentKind Component { get; }
        public ResourceKind SorterKind { get; }
        public int PairCell { get; }
        public BeltSpeed Speed { get; }

        public BeltView(int cell, uint factionId, Facing facing, ResourceKind item, int progress)
            : this(cell, factionId, facing, item, progress, false)
        {
        }

        public BeltView(int cell, uint factionId, Facing facing, ResourceKind item, int progress, bool playerHeld)
            : this(cell, factionId, facing, item, progress, playerHeld, BeltComponentKind.None, 0, -1, BeltSpeed.Normal)
        {
        }

        public BeltView(int cell, uint factionId, Facing facing, ResourceKind item, int progress, bool playerHeld,
            BeltComponentKind component, ResourceKind sorterKind, int pairCell, BeltSpeed speed)
        {
            Cell = cell; FactionId = factionId; Facing = facing; Item = item; Progress = progress; PlayerHeld = playerHeld;
            Component = component; SorterKind = sorterKind; PairCell = pairCell; Speed = speed;
        }
    }

    /// <summary>V3-6: the display-facing management state of one automatic processing line.</summary>
    public readonly struct LineView
    {
        public uint Id { get; }
        public uint FactionId { get; }
        public LineManager Manager { get; }

        public LineView(uint id, uint factionId, LineManager manager)
        {
            Id = id; FactionId = factionId; Manager = manager;
        }
    }

    /// <summary>S-5 display snapshot. Policy is the effective military policy for the region, if any.</summary>
    public readonly struct RegionView
    {
        public uint Id { get; }
        public RegionCenterKind CenterKind { get; }
        public uint CenterId { get; }
        public SimPoint Center { get; }
        public RegionControl Control { get; }
        public PolicyKind Policy { get; }
        public EconomyPolicy EconomyPolicy { get; }

        public RegionView(uint id, RegionCenterKind centerKind, uint centerId, SimPoint center,
            RegionControl control, PolicyKind policy, EconomyPolicy economyPolicy)
        {
            Id = id; CenterKind = centerKind; CenterId = centerId; Center = center;
            Control = control; Policy = policy; EconomyPolicy = economyPolicy;
        }
    }

    /// <summary>Display-only summary of the current automatic cavalry raid mission.</summary>
    public readonly struct CavalryMissionView
    {
        public RaidTargetKind TargetKind { get; }
        public uint TargetId { get; }
        public SimPoint TargetPosition { get; }
        public long ArrivalTicks { get; }
        public bool IsRetreating { get; }

        public CavalryMissionView(RaidTargetKind targetKind, uint targetId, SimPoint targetPosition, long arrivalTicks, bool isRetreating)
        {
            TargetKind = targetKind; TargetId = targetId; TargetPosition = targetPosition;
            ArrivalTicks = arrivalTicks; IsRetreating = isRetreating;
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
        /// <summary>V3-6: processing-chain stock; zero and unwritten from the simulation without the flag.</summary>
        public int Charcoal { get; }
        public int Steel { get; }
        /// <summary>V3-7: bow gear stock and the forestry second-age production values.</summary>
        public int BowGear { get; }
        public int FletcherWoodCost { get; }
        public int FletcherSizeCells { get; }
        public int FletcherTicks { get; }
        public int SkirmishArcherFoodCost { get; }
        public int SkirmishArcherBowGearCost { get; }
        public int SkirmishArcherTrainTicks { get; }
        /// <summary>V3-10 #3: current observed cavalry raid target, ETA, and retreat state.</summary>
        public CavalryMissionView CavalryMission { get; }
        public int BeltWoodCost { get; }
        public int BeltTicksPerCell { get; }
        public IReadOnlyList<BeltView> Belts { get; }
        public bool BeltComponents { get; }
        public int FastBeltWoodCost { get; }
        public int FastBeltTicksPerCell { get; }
        public int SplitterWoodCost { get; }
        public int SorterWoodCost { get; }
        public int UndergroundBeltWoodCost { get; }
        public int UndergroundBeltMaxLength { get; }
        public int StorageWoodCost { get; }
        public int StorageSizeCells { get; }
        public int StorageCapacity { get; }
        /// <summary>V3-2 costs and footprints for the placement buttons.</summary>
        public int InfantryMetalCost { get; }
        public int MineWoodCost { get; }
        public bool CommonDrill { get; }
        public int DrillWoodCost { get; }
        public int DrillStoneCost { get; }
        public int DrillSizeCells { get; }
        public bool Inserters { get; }
        public int InserterWoodCost { get; }
        public int InserterIntervalTicks { get; }
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
        /// <summary>V3-5: configured living/construction drop-site cap (0 when disabled).</summary>
        public int DropSiteMax { get; }
        /// <summary>V3-5: current living drop-site count, including construction.</summary>
        public int DropSiteCount { get; }
        /// <summary>V3-5 stone and defences (maps with ages only; 0 otherwise).</summary>
        public int Stone { get; }
        /// <summary>V3-5 (32 #19): Gems received from a market (maps with ages only; 0 otherwise).</summary>
        public int Gems { get; }
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
        /// <summary>V3-5 (32 #19): the Gems a tech costs - 15 for GemArmor and 0 for earlier techs.</summary>
        public IReadOnlyList<int> TechGemsCosts { get; }
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
        public int GemsTradeReturn { get; }
        public int GemArmorHp { get; }
        public int RamFoodCost { get; }
        public int RamWoodCost { get; }

        /// <summary>V3-5 (32 #10): the price of the third age (trade age for farming, steel age for metallurgy).</summary>
        public int Age3FoodCost { get; }
        public int Age3WoodCost { get; }

        /// <summary>V3-5 (32 #12): the archery range and the stable.</summary>
        public int RangeWoodCost { get; }
        public int StableWoodCost { get; }

        /// <summary>V3-5 (32 #17): the castle of the third age.</summary>
        public int CastleWoodCost { get; }
        public int CastleStoneCost { get; }
        public int MercenaryGemsCost { get; }
        public int MercenaryTrainTicks { get; }
        public bool MonksEnabled { get; }
        public int MonkFoodCost { get; }
        public int MonkGoldCost { get; }
        public int MonkTrainTicks { get; }
        /// <summary>V3-6: the optional charcoal/steel branch and its placement/training values.</summary>
        public bool ProcessingChain { get; }
        public int CharcoalKilnWoodCost { get; }
        public int SteelworksWoodCost { get; }
        public int CharcoalKilnSizeCells { get; }
        public int SteelworksSizeCells { get; }
        public int HeavyInfantryFoodCost { get; }
        public int HeavyInfantrySteelCost { get; }
        /// <summary>V3-6: automatic processing lines and their manager.</summary>
        public IReadOnlyList<LineView> Lines { get; }
        public int Gold { get; }
        /// <summary>The civilisation reserved in the primitive age (taken once advancing can start); Primitive when none.</summary>
        public CivKind ReservedCiv { get; }
        /// <summary>The gold the next advance costs (only the third age can cost gold); 0 otherwise.</summary>
        public int NextAgeGoldCost { get; }
        /// <summary>Age clock: the next scheduled age tick, or 0 when the clock is off or the last age is reached.</summary>
        public long NextAgeClockTick { get; }
        /// <summary>AgeClockUx: the deadline for choosing a civilisation after the age clock fires, or 0 otherwise.</summary>
        public long CivChoiceDeadlineTick { get; }
        /// <summary>AgeClockUx: the reservation/instant-payment age-clock controls are available.</summary>
        public bool AgeClockUx { get; }
        /// <summary>Manual control: true while the faction's built-in late assault is stopped.</summary>
        public bool AutoAssaultOff { get; }
        public bool RationsEnabled { get; }
        public bool EasyRebuild { get; }
        public int Rations { get; }
        public int MillWoodCost { get; }
        public int MillSizeCells { get; }
        public int MillTicks { get; }
        public int MillFoodInput { get; }
        public int RationsPerSoldier { get; }
        public int RationsTrainPermille { get; }
        public int RationsHealIntervalTicks { get; }
        public int RationsHealQuietTicks { get; }
        public int RationsHealHp { get; }
        public int RationsHealMaxPerInterval { get; }
        public int RationsFarmMinimum { get; }
        public int RationsFoodReserve { get; }
        public bool BricksEnabled { get; }
        public int Bricks { get; }
        public int BrickKilnWoodCost { get; }
        public int BrickKilnSizeCells { get; }
        public int BrickTicks { get; }
        public int BrickStoneInput { get; }
        public int BrickCharcoalInput { get; }
        public int BricksPerWall { get; }
        public int BricksPerTower { get; }
        public int BricksPerCastle { get; }
        public int BrickHpPermille { get; }
        public int BrickUpgradeIntervalTicks { get; }
        public int BrickCastleStonePermille { get; }
        public bool TackEnabled { get; }
        public int Tack { get; }
        public int SaddleryWoodCost { get; }
        public int SaddlerySizeCells { get; }
        public int TackTicks { get; }
        public int TackFoodInput { get; }
        public int TackWoodInput { get; }
        public int HeavyCavalryTackCost { get; }
        public int HeavyCavalryFoodCost { get; }
        public int HeavyCavalryTrainTicks { get; }
        public int HeavyCavalryHp { get; }
        public int HeavyCavalryDamage { get; }
        public int HeavyCavalryAttackIntervalTicks { get; }
        public Fix64 HeavyCavalryRange { get; }
        public Fix64 HeavyCavalrySpeed { get; }
        public Fix64 HeavyCavalryVision { get; }
        public bool PlanksEnabled { get; }
        public int Planks { get; }
        public int SawmillWoodCost { get; }
        public int SawmillSizeCells { get; }
        public int PlankTicks { get; }
        public int PlankWoodInput { get; }
        public int PlanksPerRam { get; }
        public int PlankRamHpPermille { get; }
        public int PlankRamTrainPermille { get; }
        public int PlanksPerBridgeCell { get; }
        public int PlankBridgeWoodPermille { get; }
        public int PlankExtraRams { get; }
        public int PlankWoodReserve { get; }
        /// <summary>CraftTree1: common first-stage processing and the frames building material.</summary>
        public bool CraftTree1Enabled { get; }
        public int CutStone { get; }
        public int Frames { get; }
        public int ArmsKit { get; }
        public int Scholarly { get; }
        public int FineArmsKit { get; }
        public int SiegeParts { get; }
        public int Mechanism { get; }
        public int StonecutterWoodCost { get; }
        public int StonecutterSizeCells { get; }
        public int StonecutterTicks { get; }
        public int StonecutterStoneInput { get; }
        public int JoineryWoodCost { get; }
        public int JoinerySizeCells { get; }
        public int JoineryTicks { get; }
        public int JoineryPlankInput { get; }
        public int JoineryCutStoneInput { get; }
        public int FramesPerBuilding { get; }
        public int FrameWoodPermille { get; }
        public int FrameWorkPermille { get; }
        public int Level2FramesCost { get; }
        public bool CraftTree2Enabled { get; }
        public int AssemblerWoodCost { get; }
        public int AssemblerSizeCells { get; }
        public int ArmsKitTicks { get; }
        public int ArmsKitPlankInput { get; }
        public int ArmsKitCharcoalInput { get; }
        public int ArmsKitSteelInput { get; }
        public int ScholarlyTicks { get; }
        public int ScholarlyPlankInput { get; }
        public int ScholarlyCharcoalInput { get; }
        public int ScholarlyFoodInput { get; }
        public int ArmsKitAttack { get; }
        public int ArmsKitHpPermille { get; }
        public int ScholarlyPerResearch { get; }
        public int ScholarlyCostPermille { get; }
        public int ScholarlyTimePermille { get; }
        public bool CraftTree3Enabled { get; }
        public int SiegePartsTicks { get; }
        public int MechanismTicks { get; }
        public int SiegePartsHpPermille { get; }
        public int SiegePartsDamagePermille { get; }
        public int SiegePartsPerCastle { get; }
        public int SiegePartsCastleStonePermille { get; }
        public int MechanismBuildingTimePermille { get; }
        public int MechanismBuildingMaterialPermille { get; }
        public int MechanismResearchCostPermille { get; }
        public int MechanismResearchTimePermille { get; }
        public bool BooksEnabled { get; }
        public int Books { get; }
        public int ScriptoriumWoodCost { get; }
        public int ScriptoriumSizeCells { get; }
        public int BookTicks { get; }
        public int BookGoldInput { get; }
        public int BookWoodInput { get; }
        public int BooksPerResearch { get; }
        public int BookResearchPermille { get; }
        public int BookResearchCostPermille { get; }
        public int BookWoodReserve { get; }
        public bool PreservesEnabled { get; }
        public int DryingRackWoodCost { get; }
        public int DryingRackSizeCells { get; }
        public int PreserveTicks { get; }
        public int PreserveFoodInput { get; }
        public int PreserveFoodValue { get; }
        public int PreserveKeepFood { get; }
        public int PreserveMaxRacks { get; }
        public int PreserveWoodReserve { get; }
        public bool JewelsEnabled { get; }
        public int LapidaryWoodCost { get; }
        public int LapidarySizeCells { get; }
        public int JewelTicks { get; }
        public int JewelStoneInput { get; }
        public int JewelOreInput { get; }
        public int JewelMaxLapidaries { get; }
        public int JewelKeepStone { get; }
        public int JewelMercenaryGems { get; }
        public int JewelMercenaryFood { get; }
        public int JewelMercenaryInfantryRatio { get; }
        public bool TradeGoodsEnabled { get; }
        public int WeaverWoodCost { get; }
        public int WeaverSizeCells { get; }
        public int TradeGoodsTicks { get; }
        public int TradeGoodsWoodInput { get; }
        public int TradeGoodsKeepWood { get; }
        public int TradeGoodsValue { get; }
        public int TradeGoodsMaxWeavers { get; }
        public int TradeGoodsWoodReserve { get; }
        public bool IncenseEnabled { get; }
        public int Incense { get; }
        public int IncenseHouseWoodCost { get; }
        public int IncenseHouseSizeCells { get; }
        public int IncenseTicks { get; }
        public int IncenseWoodInput { get; }
        public int IncenseFoodInput { get; }
        public int IncenseKeepWood { get; }
        public int IncenseKeepFood { get; }
        public int IncenseStockLimit { get; }
        public int IncenseHealIntervalTicks { get; }
        public int IncenseHealHp { get; }
        public int IncenseMaxHouses { get; }
        public int IncenseWoodReserve { get; }
        public bool CharmsEnabled { get; }
        public int Charms { get; }
        public int CharmWorkshopWoodCost { get; }
        public int CharmWorkshopSizeCells { get; }
        public int CharmTicks { get; }
        public int CharmWoodInput { get; }
        public int CharmKeepWood { get; }
        public int CharmStockLimit { get; }
        public int CharmMaxWorkshops { get; }
        public int CharmWoodReserve { get; }
        public int CharmHpPermille { get; }
        public bool RepairKitsEnabled { get; }
        public int RepairKits { get; }
        public int RepairYardWoodCost { get; }
        public int RepairYardSizeCells { get; }
        public int RepairKitTicks { get; }
        public int RepairKitWoodInput { get; }
        public int RepairKitKeepWood { get; }
        public int RepairKitStockLimit { get; }
        public int RepairKitMaxYards { get; }
        public int RepairKitWoodReserve { get; }
        public int RepairKitIntervalTicks { get; }
        public int RepairKitHp { get; }
        public bool ToolsEnabled { get; }
        public int Tools { get; }
        public int ToolShopWoodCost { get; }
        public int ToolShopSizeCells { get; }
        public int ToolTicks { get; }
        public int ToolWoodInput { get; }
        public int ToolKeepWood { get; }
        public int ToolStockLimit { get; }
        public int ToolMaxShops { get; }
        public int ToolWoodReserve { get; }
        public int ToolVillagerFoodPermille { get; }
        public int ToolVillagerTicksPermille { get; }
        public bool GoodsMarketEnabled { get; }
        public int GoodsTradeLot { get; }
        public IReadOnlyList<GoodsPriceView> GoodsPrices { get; }
        /// <summary>Manual placement uses the effective clearance and the read-only map snapshot below.</summary>
        public bool AdjacentBuildings { get; }
        public int BuildingClearanceCells { get; }
        public int CoreClearanceCells { get; }
        public int NodeClearanceCells { get; }
        public int MapWidthCells { get; }
        public int MapHeightCells { get; }
        public int CellSizeMeters { get; }
        public IReadOnlyList<bool> PassableCells { get; }
        public IReadOnlyList<bool> RiverCells { get; }

        public EconomyView(int food, int wood, int population, int populationCap, int villagerQueued, long villagerTrainRemaining,
            bool autoEconomy, int buildingSizeCells, int barracksWoodCost, int villagerFoodCost, int infantryFoodCost, int infantryWoodCost,
            IReadOnlyList<VillagerView> villagers, IReadOnlyList<BuildingView> buildings, IReadOnlyList<ResourceView> resources)
            : this(food, wood, population, populationCap, villagerQueued, villagerTrainRemaining, autoEconomy, buildingSizeCells,
                barracksWoodCost, villagerFoodCost, infantryFoodCost, infantryWoodCost, villagers, buildings, resources,
                false, 0, 0, 0, 0, null, 0, 0, 0, 0, 0, false, EconomyPolicy.Balanced, false, CivKind.Primitive, CivKind.Primitive, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
        {
        }

        public EconomyView(int wood, int stone, bool adjacentBuildings, int buildingClearanceCells, int coreClearanceCells,
            int nodeClearanceCells, int mapWidthCells, int mapHeightCells, int cellSizeMeters,
            IReadOnlyList<BuildingView> buildings, IReadOnlyList<ResourceView> resources, IReadOnlyList<BeltView> belts,
            IReadOnlyList<bool> passableCells, IReadOnlyList<bool> riverCells)
        {
            Wood = wood; Stone = stone; AdjacentBuildings = adjacentBuildings;
            BuildingClearanceCells = buildingClearanceCells; CoreClearanceCells = coreClearanceCells;
            NodeClearanceCells = nodeClearanceCells; MapWidthCells = mapWidthCells; MapHeightCells = mapHeightCells;
            CellSizeMeters = cellSizeMeters;
            Buildings = ContractList.Copy(buildings ?? Array.Empty<BuildingView>());
            Resources = ContractList.Copy(resources ?? Array.Empty<ResourceView>());
            Belts = ContractList.Copy(belts ?? Array.Empty<BeltView>());
            PassableCells = ContractList.Copy(passableCells ?? Array.Empty<bool>());
            RiverCells = ContractList.Copy(riverCells ?? Array.Empty<bool>());
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
            int age3FoodCost, int age3WoodCost, int rangeWoodCost, int stableWoodCost, int castleWoodCost, int castleStoneCost,
            int gems = 0, int gemsTradeReturn = 0, IReadOnlyList<int> techGemsCosts = null, int gemArmorHp = 0,
             int mercenaryGemsCost = 0, int mercenaryTrainTicks = 0,
            bool monksEnabled = false, int monkFoodCost = 0, int monkGoldCost = 0, int monkTrainTicks = 0,
            bool processingChain = false, int charcoalKilnWoodCost = 0, int steelworksWoodCost = 0,
            int charcoalKilnSizeCells = 0, int steelworksSizeCells = 0, int heavyInfantryFoodCost = 0, int heavyInfantrySteelCost = 0,
            int charcoal = 0, int steel = 0, IReadOnlyList<LineView> lines = null,
            int gold = 0, int bowGear = 0, int fletcherWoodCost = 0, int fletcherSizeCells = 0, int fletcherTicks = 0,
            int skirmishArcherFoodCost = 0, int skirmishArcherBowGearCost = 0, int skirmishArcherTrainTicks = 0,
             CavalryMissionView cavalryMission = default(CavalryMissionView),
             CivKind reservedCiv = CivKind.Primitive, int nextAgeGoldCost = 0,
             bool beltComponents = false, int fastBeltWoodCost = 0, int fastBeltTicksPerCell = 0,
             int splitterWoodCost = 0, int sorterWoodCost = 0, int undergroundBeltWoodCost = 0,
             int undergroundBeltMaxLength = 0, int storageWoodCost = 0, int storageSizeCells = 0, int storageCapacity = 0,
             long nextAgeClockTick = 0, bool autoAssaultOff = false,
             bool rationsEnabled = false, int rations = 0, int millWoodCost = 0, int millSizeCells = 0,
             int millTicks = 0, int millFoodInput = 0, int rationsPerSoldier = 0, int rationsTrainPermille = 0,
             int rationsHealIntervalTicks = 0, int rationsHealQuietTicks = 0, int rationsHealHp = 0,
             int rationsHealMaxPerInterval = 0, int rationsFarmMinimum = 0, int rationsFoodReserve = 0,
             bool bricksEnabled = false, int bricks = 0, int brickKilnWoodCost = 0, int brickKilnSizeCells = 0,
             int brickTicks = 0, int brickStoneInput = 0, int brickCharcoalInput = 0, int bricksPerWall = 0,
             int bricksPerTower = 0, int bricksPerCastle = 0, int brickHpPermille = 0, int brickUpgradeIntervalTicks = 0,
             int brickCastleStonePermille = 0,
             bool tackEnabled = false, int tack = 0, int saddleryWoodCost = 0, int saddlerySizeCells = 0,
             int tackTicks = 0, int tackFoodInput = 0, int tackWoodInput = 0,
             int heavyCavalryTackCost = 0, int heavyCavalryFoodCost = 0, int heavyCavalryTrainTicks = 0,
             int heavyCavalryHp = 0, int heavyCavalryDamage = 0, int heavyCavalryAttackIntervalTicks = 0,
             Fix64 heavyCavalryRange = default(Fix64), Fix64 heavyCavalrySpeed = default(Fix64), Fix64 heavyCavalryVision = default(Fix64),
             bool planksEnabled = false, int planks = 0, int sawmillWoodCost = 0, int sawmillSizeCells = 0,
             int plankTicks = 0, int plankWoodInput = 0, int planksPerRam = 0, int plankRamHpPermille = 0,
             int plankRamTrainPermille = 0, int planksPerBridgeCell = 0, int plankBridgeWoodPermille = 0,
             int plankExtraRams = 0, int plankWoodReserve = 0,
             bool booksEnabled = false, int books = 0, int scriptoriumWoodCost = 0, int scriptoriumSizeCells = 0,
             int bookTicks = 0, int bookGoldInput = 0, int bookWoodInput = 0, int booksPerResearch = 0,
             int bookResearchPermille = 0, int bookResearchCostPermille = 0, int bookWoodReserve = 0,
             bool preservesEnabled = false, int dryingRackWoodCost = 0, int dryingRackSizeCells = 0,
             int preserveTicks = 0, int preserveFoodInput = 0, int preserveFoodValue = 0,
             int preserveKeepFood = 0, int preserveMaxRacks = 0, int preserveWoodReserve = 0,
             bool jewelsEnabled = false, int lapidaryWoodCost = 0, int lapidarySizeCells = 0,
             int jewelTicks = 0, int jewelStoneInput = 0, int jewelOreInput = 0,
             int jewelMaxLapidaries = 0, int jewelKeepStone = 0,
             int jewelMercenaryGems = 0, int jewelMercenaryFood = 0, int jewelMercenaryInfantryRatio = 0,
             bool tradeGoodsEnabled = false, int weaverWoodCost = 0, int weaverSizeCells = 0,
             int tradeGoodsTicks = 0, int tradeGoodsWoodInput = 0, int tradeGoodsKeepWood = 0,
             int tradeGoodsValue = 0, int tradeGoodsMaxWeavers = 0, int tradeGoodsWoodReserve = 0,
             bool incenseEnabled = false, int incense = 0, int incenseHouseWoodCost = 0, int incenseHouseSizeCells = 0,
             int incenseTicks = 0, int incenseWoodInput = 0, int incenseFoodInput = 0, int incenseKeepWood = 0,
             int incenseKeepFood = 0, int incenseStockLimit = 0, int incenseHealIntervalTicks = 0,
             int incenseHealHp = 0, int incenseMaxHouses = 0, int incenseWoodReserve = 0,
             bool charmsEnabled = false, int charms = 0, int charmWorkshopWoodCost = 0, int charmWorkshopSizeCells = 0,
             int charmTicks = 0, int charmWoodInput = 0, int charmKeepWood = 0, int charmStockLimit = 0,
             int charmMaxWorkshops = 0, int charmWoodReserve = 0, int charmHpPermille = 0,
             bool repairKitsEnabled = false, int repairKits = 0, int repairYardWoodCost = 0, int repairYardSizeCells = 0,
             int repairKitTicks = 0, int repairKitWoodInput = 0, int repairKitKeepWood = 0, int repairKitStockLimit = 0,
             int repairKitMaxYards = 0, int repairKitWoodReserve = 0, int repairKitIntervalTicks = 0, int repairKitHp = 0,
              bool toolsEnabled = false, int tools = 0, int toolShopWoodCost = 0, int toolShopSizeCells = 0,
              int toolTicks = 0, int toolWoodInput = 0, int toolKeepWood = 0, int toolStockLimit = 0,
              int toolMaxShops = 0, int toolWoodReserve = 0, int toolVillagerFoodPermille = 0, int toolVillagerTicksPermille = 0,
              bool goodsMarketEnabled = false, int goodsTradeLot = 0, IReadOnlyList<GoodsPriceView> goodsPrices = null,
              bool adjacentBuildings = false, int buildingClearanceCells = 2, int coreClearanceCells = 4,
              int nodeClearanceCells = 2, int mapWidthCells = 0, int mapHeightCells = 0, int cellSizeMeters = 0,
              IReadOnlyList<bool> passableCells = null, IReadOnlyList<bool> riverCells = null,
             bool craftTree1Enabled = false, int cutStone = 0, int frames = 0,
             int stonecutterWoodCost = 0, int stonecutterSizeCells = 0, int stonecutterTicks = 0, int stonecutterStoneInput = 0,
             int joineryWoodCost = 0, int joinerySizeCells = 0, int joineryTicks = 0, int joineryPlankInput = 0,
             int joineryCutStoneInput = 0, int framesPerBuilding = 0, int frameWoodPermille = 0, int frameWorkPermille = 0,
             int level2FramesCost = 0,
             int dropSiteMax = 0, int dropSiteCount = 0,
             bool craftTree2Enabled = false, int armsKit = 0, int scholarly = 0, int fineArmsKit = 0,
             int assemblerWoodCost = 0, int assemblerSizeCells = 0, int armsKitAttack = 0, int armsKitHpPermille = 0,
             int scholarlyPerResearch = 0, int scholarlyCostPermille = 0, int scholarlyTimePermille = 0,
             int armsKitTicks = 0, int armsKitPlankInput = 0, int armsKitCharcoalInput = 0, int armsKitSteelInput = 0,
              int scholarlyTicks = 0, int scholarlyPlankInput = 0, int scholarlyCharcoalInput = 0, int scholarlyFoodInput = 0,
              bool craftTree3Enabled = false, int siegeParts = 0, int mechanism = 0, int siegePartsTicks = 0, int mechanismTicks = 0,
              int siegePartsHpPermille = 0, int siegePartsDamagePermille = 0, int siegePartsPerCastle = 0, int siegePartsCastleStonePermille = 0,
              int mechanismBuildingTimePermille = 0, int mechanismBuildingMaterialPermille = 0,
              int mechanismResearchCostPermille = 0, int mechanismResearchTimePermille = 0,
             long civChoiceDeadlineTick = 0, bool ageClockUx = false,
             bool commonDrill = false, int drillWoodCost = 0, int drillStoneCost = 0, int drillSizeCells = 0,
             bool inserters = false, int inserterWoodCost = 0, int inserterIntervalTicks = 0, bool easyRebuild = false)
        {
            ReservedCiv = reservedCiv; NextAgeGoldCost = nextAgeGoldCost; NextAgeClockTick = nextAgeClockTick; CivChoiceDeadlineTick = civChoiceDeadlineTick; AgeClockUx = ageClockUx;
            AutoAssaultOff = autoAssaultOff;
            RationsEnabled = rationsEnabled; EasyRebuild = easyRebuild; Rations = rations; MillWoodCost = millWoodCost; MillSizeCells = millSizeCells;
            MillTicks = millTicks; MillFoodInput = millFoodInput; RationsPerSoldier = rationsPerSoldier; RationsTrainPermille = rationsTrainPermille;
            RationsHealIntervalTicks = rationsHealIntervalTicks; RationsHealQuietTicks = rationsHealQuietTicks; RationsHealHp = rationsHealHp;
            RationsHealMaxPerInterval = rationsHealMaxPerInterval; RationsFarmMinimum = rationsFarmMinimum; RationsFoodReserve = rationsFoodReserve;
            BricksEnabled = bricksEnabled; Bricks = bricks; BrickKilnWoodCost = brickKilnWoodCost; BrickKilnSizeCells = brickKilnSizeCells;
            BrickTicks = brickTicks; BrickStoneInput = brickStoneInput; BrickCharcoalInput = brickCharcoalInput;
            BricksPerWall = bricksPerWall; BricksPerTower = bricksPerTower; BricksPerCastle = bricksPerCastle;
            BrickHpPermille = brickHpPermille; BrickUpgradeIntervalTicks = brickUpgradeIntervalTicks; BrickCastleStonePermille = brickCastleStonePermille;
            TackEnabled = tackEnabled; Tack = tack; SaddleryWoodCost = saddleryWoodCost; SaddlerySizeCells = saddlerySizeCells;
            TackTicks = tackTicks; TackFoodInput = tackFoodInput; TackWoodInput = tackWoodInput;
            HeavyCavalryTackCost = heavyCavalryTackCost; HeavyCavalryFoodCost = heavyCavalryFoodCost;
            HeavyCavalryTrainTicks = heavyCavalryTrainTicks; HeavyCavalryHp = heavyCavalryHp; HeavyCavalryDamage = heavyCavalryDamage;
            HeavyCavalryAttackIntervalTicks = heavyCavalryAttackIntervalTicks; HeavyCavalryRange = heavyCavalryRange;
            HeavyCavalrySpeed = heavyCavalrySpeed; HeavyCavalryVision = heavyCavalryVision;
            PlanksEnabled = planksEnabled; Planks = planks; SawmillWoodCost = sawmillWoodCost; SawmillSizeCells = sawmillSizeCells;
            PlankTicks = plankTicks; PlankWoodInput = plankWoodInput; PlanksPerRam = planksPerRam;
            PlankRamHpPermille = plankRamHpPermille; PlankRamTrainPermille = plankRamTrainPermille;
            PlanksPerBridgeCell = planksPerBridgeCell; PlankBridgeWoodPermille = plankBridgeWoodPermille;
            PlankExtraRams = plankExtraRams; PlankWoodReserve = plankWoodReserve;
            BooksEnabled = booksEnabled; Books = books; ScriptoriumWoodCost = scriptoriumWoodCost; ScriptoriumSizeCells = scriptoriumSizeCells;
            BookTicks = bookTicks; BookGoldInput = bookGoldInput; BookWoodInput = bookWoodInput; BooksPerResearch = booksPerResearch;
            BookResearchPermille = bookResearchPermille; BookResearchCostPermille = bookResearchCostPermille; BookWoodReserve = bookWoodReserve;
            PreservesEnabled = preservesEnabled; DryingRackWoodCost = dryingRackWoodCost; DryingRackSizeCells = dryingRackSizeCells;
            PreserveTicks = preserveTicks; PreserveFoodInput = preserveFoodInput; PreserveFoodValue = preserveFoodValue;
            PreserveKeepFood = preserveKeepFood; PreserveMaxRacks = preserveMaxRacks; PreserveWoodReserve = preserveWoodReserve;
            JewelsEnabled = jewelsEnabled; LapidaryWoodCost = lapidaryWoodCost; LapidarySizeCells = lapidarySizeCells;
            JewelTicks = jewelTicks; JewelStoneInput = jewelStoneInput; JewelOreInput = jewelOreInput;
            JewelMaxLapidaries = jewelMaxLapidaries; JewelKeepStone = jewelKeepStone;
            JewelMercenaryGems = jewelMercenaryGems; JewelMercenaryFood = jewelMercenaryFood;
            JewelMercenaryInfantryRatio = jewelMercenaryInfantryRatio;
            TradeGoodsEnabled = tradeGoodsEnabled; WeaverWoodCost = weaverWoodCost; WeaverSizeCells = weaverSizeCells;
            TradeGoodsTicks = tradeGoodsTicks; TradeGoodsWoodInput = tradeGoodsWoodInput; TradeGoodsKeepWood = tradeGoodsKeepWood;
            TradeGoodsValue = tradeGoodsValue; TradeGoodsMaxWeavers = tradeGoodsMaxWeavers; TradeGoodsWoodReserve = tradeGoodsWoodReserve;
            IncenseEnabled = incenseEnabled; Incense = incense; IncenseHouseWoodCost = incenseHouseWoodCost; IncenseHouseSizeCells = incenseHouseSizeCells;
            IncenseTicks = incenseTicks; IncenseWoodInput = incenseWoodInput; IncenseFoodInput = incenseFoodInput;
            IncenseKeepWood = incenseKeepWood; IncenseKeepFood = incenseKeepFood; IncenseStockLimit = incenseStockLimit;
            IncenseHealIntervalTicks = incenseHealIntervalTicks; IncenseHealHp = incenseHealHp; IncenseMaxHouses = incenseMaxHouses;
            IncenseWoodReserve = incenseWoodReserve;
            CharmsEnabled = charmsEnabled; Charms = charms; CharmWorkshopWoodCost = charmWorkshopWoodCost; CharmWorkshopSizeCells = charmWorkshopSizeCells;
            CharmTicks = charmTicks; CharmWoodInput = charmWoodInput; CharmKeepWood = charmKeepWood; CharmStockLimit = charmStockLimit;
            CharmMaxWorkshops = charmMaxWorkshops; CharmWoodReserve = charmWoodReserve; CharmHpPermille = charmHpPermille;
            RepairKitsEnabled = repairKitsEnabled; RepairKits = repairKits; RepairYardWoodCost = repairYardWoodCost; RepairYardSizeCells = repairYardSizeCells;
            RepairKitTicks = repairKitTicks; RepairKitWoodInput = repairKitWoodInput; RepairKitKeepWood = repairKitKeepWood; RepairKitStockLimit = repairKitStockLimit;
            RepairKitMaxYards = repairKitMaxYards; RepairKitWoodReserve = repairKitWoodReserve; RepairKitIntervalTicks = repairKitIntervalTicks; RepairKitHp = repairKitHp;
            ToolsEnabled = toolsEnabled; Tools = tools; ToolShopWoodCost = toolShopWoodCost; ToolShopSizeCells = toolShopSizeCells;
            ToolTicks = toolTicks; ToolWoodInput = toolWoodInput; ToolKeepWood = toolKeepWood; ToolStockLimit = toolStockLimit;
            ToolMaxShops = toolMaxShops; ToolWoodReserve = toolWoodReserve; ToolVillagerFoodPermille = toolVillagerFoodPermille; ToolVillagerTicksPermille = toolVillagerTicksPermille;
            GoodsMarketEnabled = goodsMarketEnabled; GoodsTradeLot = goodsTradeLot;
            GoodsPrices = ContractList.Copy(goodsPrices ?? Array.Empty<GoodsPriceView>());
            AdjacentBuildings = adjacentBuildings; BuildingClearanceCells = buildingClearanceCells;
            CoreClearanceCells = coreClearanceCells; NodeClearanceCells = nodeClearanceCells;
            MapWidthCells = mapWidthCells; MapHeightCells = mapHeightCells; CellSizeMeters = cellSizeMeters;
            PassableCells = ContractList.Copy(passableCells ?? Array.Empty<bool>());
            RiverCells = ContractList.Copy(riverCells ?? Array.Empty<bool>());
            CraftTree1Enabled = craftTree1Enabled; CutStone = cutStone; Frames = frames;
             CraftTree2Enabled = craftTree2Enabled; ArmsKit = armsKit; Scholarly = scholarly; FineArmsKit = fineArmsKit;
             CraftTree3Enabled = craftTree3Enabled; SiegeParts = siegeParts; Mechanism = mechanism;
            StonecutterWoodCost = stonecutterWoodCost; StonecutterSizeCells = stonecutterSizeCells;
            StonecutterTicks = stonecutterTicks; StonecutterStoneInput = stonecutterStoneInput;
            JoineryWoodCost = joineryWoodCost; JoinerySizeCells = joinerySizeCells; JoineryTicks = joineryTicks;
            JoineryPlankInput = joineryPlankInput; JoineryCutStoneInput = joineryCutStoneInput;
            FramesPerBuilding = framesPerBuilding; FrameWoodPermille = frameWoodPermille; FrameWorkPermille = frameWorkPermille;
            Level2FramesCost = level2FramesCost; AssemblerWoodCost = assemblerWoodCost; AssemblerSizeCells = assemblerSizeCells;
            ArmsKitTicks = armsKitTicks; ArmsKitPlankInput = armsKitPlankInput; ArmsKitCharcoalInput = armsKitCharcoalInput; ArmsKitSteelInput = armsKitSteelInput;
            ScholarlyTicks = scholarlyTicks; ScholarlyPlankInput = scholarlyPlankInput; ScholarlyCharcoalInput = scholarlyCharcoalInput; ScholarlyFoodInput = scholarlyFoodInput;
            ArmsKitAttack = armsKitAttack; ArmsKitHpPermille = armsKitHpPermille; ScholarlyPerResearch = scholarlyPerResearch;
             ScholarlyCostPermille = scholarlyCostPermille; ScholarlyTimePermille = scholarlyTimePermille;
             SiegePartsTicks = siegePartsTicks; MechanismTicks = mechanismTicks;
             SiegePartsHpPermille = siegePartsHpPermille; SiegePartsDamagePermille = siegePartsDamagePermille;
             SiegePartsPerCastle = siegePartsPerCastle; SiegePartsCastleStonePermille = siegePartsCastleStonePermille;
             MechanismBuildingTimePermille = mechanismBuildingTimePermille; MechanismBuildingMaterialPermille = mechanismBuildingMaterialPermille;
             MechanismResearchCostPermille = mechanismResearchCostPermille; MechanismResearchTimePermille = mechanismResearchTimePermille;
            BeltComponents = beltComponents; FastBeltWoodCost = fastBeltWoodCost; FastBeltTicksPerCell = fastBeltTicksPerCell;
            SplitterWoodCost = splitterWoodCost; SorterWoodCost = sorterWoodCost; UndergroundBeltWoodCost = undergroundBeltWoodCost;
            UndergroundBeltMaxLength = undergroundBeltMaxLength; StorageWoodCost = storageWoodCost; StorageSizeCells = storageSizeCells;
            StorageCapacity = storageCapacity;
            MarketWoodCost = marketWoodCost; WorkshopWoodCost = workshopWoodCost; TradeLot = tradeLot; TradeReturn = tradeReturn;
            GemsTradeReturn = gemsTradeReturn; GemArmorHp = gemArmorHp;
            MercenaryGemsCost = mercenaryGemsCost; MercenaryTrainTicks = mercenaryTrainTicks;
            MonksEnabled = monksEnabled; MonkFoodCost = monkFoodCost; MonkGoldCost = monkGoldCost; MonkTrainTicks = monkTrainTicks;
            ProcessingChain = processingChain; CharcoalKilnWoodCost = charcoalKilnWoodCost; SteelworksWoodCost = steelworksWoodCost;
            CharcoalKilnSizeCells = charcoalKilnSizeCells; SteelworksSizeCells = steelworksSizeCells;
            HeavyInfantryFoodCost = heavyInfantryFoodCost; HeavyInfantrySteelCost = heavyInfantrySteelCost;
            Lines = ContractList.Copy(lines ?? Array.Empty<LineView>());
            Gold = gold;
            BowGear = bowGear; FletcherWoodCost = fletcherWoodCost; FletcherSizeCells = fletcherSizeCells; FletcherTicks = fletcherTicks;
            SkirmishArcherFoodCost = skirmishArcherFoodCost; SkirmishArcherBowGearCost = skirmishArcherBowGearCost; SkirmishArcherTrainTicks = skirmishArcherTrainTicks;
            CavalryMission = cavalryMission;
            RamFoodCost = ramFoodCost; RamWoodCost = ramWoodCost;
            Age3FoodCost = age3FoodCost; Age3WoodCost = age3WoodCost;
            RangeWoodCost = rangeWoodCost; StableWoodCost = stableWoodCost;
            CastleWoodCost = castleWoodCost; CastleStoneCost = castleStoneCost;
            Age = age; Age2FoodCost = age2FoodCost; Age2WoodCost = age2WoodCost; ArcherFoodCost = archerFoodCost; ArcherWoodCost = archerWoodCost;
            CavalryFoodCost = cavalryFoodCost; CavalryWoodCost = cavalryWoodCost; CavalryMetalCost = cavalryMetalCost;
            BlacksmithWoodCost = blacksmithWoodCost; Techs = techs;
            TechFoodCosts = ContractList.Copy(techFoodCosts ?? Array.Empty<int>());
            TechWoodCosts = ContractList.Copy(techWoodCosts ?? Array.Empty<int>());
            TechMetalCosts = ContractList.Copy(techMetalCosts ?? Array.Empty<int>());
            TechGemsCosts = ContractList.Copy(techGemsCosts ?? Array.Empty<int>());
            Stone = stone; Gems = gems; WallStoneCost = wallStoneCost; TowerWoodCost = towerWoodCost; TowerStoneCost = towerStoneCost;
            DropSiteWoodCost = dropSiteWoodCost;
            DropSiteMax = dropSiteMax; DropSiteCount = dropSiteCount;
            HouseWoodCost = houseWoodCost;
            ScoutFoodCost = scoutFoodCost;
            FarmWoodCost = farmWoodCost; FarmSizeCells = farmSizeCells;
            Ages = ages; Civ = civ; AdvancingTo = advancingTo; AdvanceRemaining = advanceRemaining;
            AdvanceFoodCost = advanceFoodCost; AdvanceWoodCost = advanceWoodCost;
            CorePlayerHeld = corePlayerHeld;
            Policy = policy;
            InfantryMetalCost = infantryMetalCost; MineWoodCost = mineWoodCost; SmelterWoodCost = smelterWoodCost;
            CommonDrill = commonDrill; DrillWoodCost = drillWoodCost; DrillStoneCost = drillStoneCost; DrillSizeCells = drillSizeCells;
            Inserters = inserters; InserterWoodCost = inserterWoodCost; InserterIntervalTicks = inserterIntervalTicks;
            MineSizeCells = mineSizeCells; SmelterSizeCells = smelterSizeCells;
            Industry = industry; Ore = ore; Metal = metal; Charcoal = charcoal; Steel = steel; BeltWoodCost = beltWoodCost; BeltTicksPerCell = beltTicksPerCell;
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
