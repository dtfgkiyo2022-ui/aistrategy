using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    // Authoring DTOs only. Simulation validates and copies them; no JSON dependency.
    public sealed class ScenarioDefinition
    {
        public int SchemaVersion = 1;
        public string ScenarioId = "week1-10v10";
        public ulong Seed;
        public int TickRateHz = 20;
        public long VerificationTickLimit = 36000;
        public MapDefinition Map = new MapDefinition();
        public RuleDefinition Rules = new RuleDefinition();
        public UnitParameters[] UnitParameters = Array.Empty<UnitParameters>();
        public FactionDefinition[] Factions = Array.Empty<FactionDefinition>();
        public CoreDefinition[] Cores = Array.Empty<CoreDefinition>();
        public OutpostDefinition[] Outposts = Array.Empty<OutpostDefinition>();
        public ArmyDefinition[] Armies = Array.Empty<ArmyDefinition>();
        public SoldierDefinition[] Soldiers = Array.Empty<SoldierDefinition>();
        /// <summary>Ver.3: resource points. They do not block movement. Empty in every Ver.1 scenario.</summary>
        public ResourceNodeDefinition[] ResourceNodes = Array.Empty<ResourceNodeDefinition>();
        /// <summary>Ver.3: economy rules. Disabled in every Ver.1 scenario, so nothing new runs there.</summary>
        public EconomyRules Economy = new EconomyRules();
        /// <summary>Ver.3: villagers at S0. Only allowed when the economy is enabled.</summary>
        public VillagerDefinition[] Villagers = Array.Empty<VillagerDefinition>();
        /// <summary>Ver.3 V3-2: belts at S0 (with what they carry). Only allowed when the economy has industry.</summary>
        public BeltDefinition[] Belts = Array.Empty<BeltDefinition>();
        /// <summary>Optional length-delimited records for rules introduced after the civilisation tails.</summary>
        public ScenarioExtensionData[] Extensions = Array.Empty<ScenarioExtensionData>();
    }

    /// <summary>One registered scenario extension. Its payload is opaque to simulation rules.</summary>
    public sealed class ScenarioExtensionData
    {
        public int Id;
        public int Version;
        public byte[] Data = Array.Empty<byte>();
    }

    public struct BeltDefinition
    {
        public int Cell;
        public uint FactionId;
        public Facing Facing;
        /// <summary>0 for an empty belt.</summary>
        public ResourceKind Item;
        /// <summary>V3-20 component metadata. All values are ignored while BeltComponents is false.</summary>
        public BeltComponentKind Component;
        public ResourceKind SorterKind;
        public int PairCell;
        public BeltSpeed Speed;
    }

    public struct VillagerDefinition
    {
        public uint Id, FactionId;
        public SimPoint Position;
    }

    public struct ResourceNodeDefinition
    {
        public uint Id;
        public ResourceKind Kind;
        public SimPoint Position;
        public int Amount;
    }

    /// <summary>technical-design-v3 4: provisional values. They make one pass work; they are not tuned.</summary>
    public sealed class EconomyRules
    {
        public bool Enabled;
        /// <summary>Measurement-only escape hatch for scaled matches; normal LiveGameRules never sets this.</summary>
        public bool MeasurementScale;
        /// <summary>Human building placement may reserve a legal site until the required resources are available.</summary>
        public bool BuildingReservations;
        /// <summary>AI/resource deductions must never make a stock negative.</summary>
        public bool NoNegativeStock;
        public int ReservationLimit = 10, ReservationCheckTicks = 10;
        /// <summary>S-5: enables deterministic geographic regions and regional controls.</summary>
        public bool Regions;
        /// <summary>MoveFix1: allows a deterministic straight-line move when the exact line of sight is clear.</summary>
        public bool DirectPathMovement;
        /// <summary>S-4: allow full armies to create a new field army for later reinforcements.</summary>
        public bool ArmyGrowth;
        /// <summary>S-4b: scale automatic villagers, barracks and housing with the economy.</summary>
        public bool EconomyScale;
        /// <summary>LargeEconomy: the large-map economy profile for up to 300 villagers and 1000 soldiers.</summary>
        public bool LargeEconomy;
        /// <summary>38.10: keeps automatic housing placements flowing on the large economy.</summary>
        public bool HouseFlow;
        public int HouseFlowStallTicks = 600;
        public int HouseFlowMarginPerProducer = 8;
        public int HouseFlowMaxConcurrent = 3;
        /// <summary>FastMovement: doubles movement steps without changing configured speed, combat, or presentation values.</summary>
        public bool FastMovement;
        public int FastMovementUnitSpeedPermille = 2000, FastMovementVillagerSpeedPermille = 2000;
        public int LargeVillagerTrainTicks = 40, LargeInfantryTrainTicks = 80;
        public int LargeVillagerBatch = 4, LargeInfantryBatch = 4;
        public int LargeBarracksMaxBuildings = 16, LargeArmyCapacity = 300;
        public int LargeTownDecisionIntervalTicks = 60, LargeTownMinAge;
        public int LargeFactionCap = 1400;
        public int LargeResourceAmountPermille = 20000, LargeFoodAmountPermille = 40000;
        public int LargeOreAmountPermille = 20000, LargeStoneAmountPermille = 20000, LargeGoldAmountPermille = 20000;
        /// <summary>LargeEconomy allocation: desired food gatherers per wood gatherer while construction is funded.</summary>
        public int LargeFoodPerWood = 3;
        /// <summary>LargeEconomy allocation: wood kept ahead of the next ordinary-cost building.</summary>
        public int LargeConstructionReserveWood = 300;
        /// <summary>LargeEconomy allocation: stone gatherers kept while another town can be afforded.</summary>
        public int LargeTownStoneGatherers = 24;
        /// <summary>LargeEconomy allocation: villagers per planned remote town.</summary>
        public int LargeTownVillagersPerTown = 80;
        /// <summary>LargeEconomy allocation: villagers per automatic barracks.</summary>
        public int LargeBarracksVillagersPerBuilding = 20;
        /// <summary>LargeEconomy allocation: automatic workers assigned around each completed town.</summary>
        public int LargeTownAutoWorkers = 4;
        /// <summary>LargeEconomy logistics: a resource cluster is served locally when its average round trip exceeds this many ticks.</summary>
        public int LargeTargetRoundTripTicks = 300;
        /// <summary>LargeEconomy logistics: same-kind nodes within this distance belong to one deterministic cluster.</summary>
        public int LargeResourceClusterRadiusMeters = 24;
        /// <summary>LargeEconomy logistics: cluster decisions are spaced out to keep large matches light.</summary>
        public int LargeClusterDecisionIntervalTicks = 100;
        /// <summary>LargeEconomy logistics: maximum automatic resource-cluster drop-offs.</summary>
        public int LargeResourceClusterMaxDropSites = 12;
        /// <summary>LargeEconomy logistics: the shared human and automatic drop-site limit on the large map.</summary>
        public int LargeDropSiteMax = 8;
        /// <summary>LargeEconomy logistics: a cluster must have this many active gatherers before it gets a site.</summary>
        public int LargeClusterMinGatherers = 2;
        /// <summary>LargeEconomy logistics: automatic drop-sites keep this many metres from own receivers and bases.</summary>
        public int LargeDropSiteMinSpacingMeters = 24;
        /// <summary>LargeEconomy logistics: start the next cluster's receiver when the current cluster falls below this share.</summary>
        public int LargeClusterSwitchPermille = 200;
        /// <summary>LargeEconomy food: automatic farms scale with this many living villagers per farm.</summary>
        public int LargeVillagersPerFarm = 20, LargeFarmMinimum = 5, LargeFarmMaximum = 32;
        /// <summary>LargeEconomy military: keep this many infantry input sets available before rebalancing away from the input.</summary>
        public int LargeInfantryResourceReserveUnits = 8;
        /// <summary>LargeEconomy scouting: automatic patrol continues until this fraction of the map is explored.</summary>
        public int LargeExploreTargetPermille = 900;
        /// <summary>LargeEconomy scouting: desired living plus queued scouts per faction.</summary>
        public int LargeExploreScoutCount = 4;
        /// <summary>LargeEconomy scouting: cells in one patrol chunk.</summary>
        public int LargeExploreChunkCells = 16;
        /// <summary>Reserves infantry inputs and restores the corresponding automatic income when the army is depleted.</summary>
        public bool AiArmyRecovery;
        /// <summary>Checks automatic processing-line building IDs against the live building kind before reuse.</summary>
        public bool LineBuildingCheck;
        /// <summary>Technical-design-v3 38.5: expands automatic factory bases through completed towns.</summary>
        public bool AiExpand;
        public int AiExpandAge1TownCount = 1, AiExpandAge2TownCount = 2, AiExpandAge3TownCount = 3;
        public int AiExpandDecisionIntervalTicks = 100;
        public int AiExpandUnreachableWorkTimeoutTicks = 1200;
        /// <summary>AiExpand: minimum villager population before enforcing resource-worker shares.</summary>
        public int AiExpandShareMinVillagers = 20;
        public int AiExpandFoodWorkerPermille = 300, AiExpandWoodWorkerPermille = 350, AiExpandStoneWorkerPermille = 100;
        public int AiExpandTownStoneWorkerPermille = 150, AiExpandPrimitiveFoodWorkerPermille = 400;
        /// <summary>Technical-design-v3 38.8: makes towns free and raises their limit by age.</summary>
        public bool FreeTowns;
        public int FreeTownsPerAge = 1;
        public int FreeTownsAiVillagersPerTown = 25, FreeTownsAiTownBuilders = 2;
        public int FreeTownsRouteRecheckTicks = 300;
        public int StartFood = 200, StartWood = 150;
        /// <summary>Villagers plus soldiers plus villagers queued at the core.</summary>
        public int PopulationCap = 60;
        public int VillagerHp = 40;
        public Fix64 VillagerSpeed = Fix64.FromInt(2);
        public int CarryCapacity = 10, GatherIntervalTicks = 20;
        public int VillagerFoodCost = 50, VillagerTrainTicks = 200, QueueLimit = 5;
        /// <summary>The automatic economy trains villagers up to this many (5.4 step 1).</summary>
        public int AutoVillagerTarget = 10;
        /// <summary>A villager drops its load once within the core radius plus this.</summary>
        public Fix64 DropOffMargin = Fix64.FromInt(2);
        /// <summary>Barracks: square footprint in cells, wood cost, work to build (one villager adds one per tick), HP.</summary>
        public int BarracksSizeCells = 3, BarracksWoodCost = 150, BarracksWork = 400, BarracksHp = 1000;
        /// <summary>Villagers sent to build a new barracks.</summary>
        public int Builders = 2;
        public int InfantryFoodCost = 50, InfantryWoodCost = 20, InfantryTrainTicks = 300;
        /// <summary>The automatic economy keeps at most this many infantry queued at a barracks (5.4 step 3).</summary>
        public int AutoInfantryQueue = 2;

        /// <summary>
        /// V3-2 (technical-design-v3 10-12): ore, metal and belts. False keeps a V3-1 economy exactly as it was - the
        /// scenario binary, the canonical state and every tick.
        /// </summary>
        public bool Industry;
        /// <summary>Belt: wood per cell, ticks an item spends on a cell before it moves on, HP, and cells per faction.</summary>
        public int BeltWoodCost = 1, BeltTicksPerCell = 8, BeltHp = 50, BeltLimit = 200;
        /// <summary>V3-32.32: ticks before the automatic economy may rebuild a line damaged by an enemy raid. 0 keeps the old immediate rebuild.</summary>
        public int LineRebuildDelayTicks;
        /// <summary>V3-32.32: 0 keeps the historical raid and tower target order; positive values put line targets first.</summary>
        public int RaidLinePriority;
        /// <summary>V3-20: opt-in belt components. False preserves the original belt state and hashes.</summary>
        public bool BeltComponents;
        public int SplitterWoodCost = 5, SorterWoodCost = 7, UndergroundBeltWoodCost = 3, UndergroundBeltMaxLength = 4;
        public int FastBeltWoodCost = 2, FastBeltTicksPerCell = 4, FastBeltAge = 2;
        /// <summary>LargeEconomy: villagers may put gathered items on a nearby connected belt.</summary>
        public bool BeltDropOff;
        public int BeltDropOffRangeCells = 2, BeltDropOffWaitTicks = 40;
        public int StorageSizeCells = 2, StorageWoodCost = 120, StorageWork = 300, StorageHp = 700, StorageCapacity = 100;
        /// <summary>Mine (12.2): footprint, wood, work, HP, and ticks per ore taken from the point under it.</summary>
        public int MineSizeCells = 2, MineWoodCost = 60, MineWork = 200, MineHp = 400, MineIntervalTicks = 20;
        /// <summary>Common drill: an age-one, all-civilisation extractor for one wood, stone, ore, or gold point.</summary>
        public bool CommonDrill;
        public int DrillSizeCells = 2, DrillWoodCost = 60, DrillStoneCost = 30, DrillWork = 200, DrillHp = 400, DrillIntervalTicks = 40;
        /// <summary>V3-34.3: one-cell inserters; the selector uses BeltDefinition.SorterKind.</summary>
        public bool Inserters;
        public int InserterWoodCost = 15, InserterIntervalTicks = 10, AiInserterCoreReserve = 100;
        /// <summary>Smelter (12.2): footprint, wood, work, HP, ticks per metal, and ore per metal.</summary>
        public int SmelterSizeCells = 3, SmelterWoodCost = 120, SmelterWork = 300, SmelterHp = 600, SmeltTicks = 60, OrePerMetal = 2;
        /// <summary>Items a mine or smelter holds at its input and at its output; a full output stops the work.</summary>
        public int BufferLimit = 10;
        /// <summary>Metal an infantry costs on top of food and wood (12.4). 0 unless the map sets it.</summary>
        public int InfantryMetalCost;

        /// <summary>
        /// V3-6 processing chain. This is deliberately separate from Industry: false keeps existing industry maps
        /// byte-for-byte unchanged, while true opts into charcoal, steel, the two processing buildings and heavy infantry.
        /// </summary>
        public bool ProcessingChain;
        public int CharcoalKilnSizeCells = 2, CharcoalKilnWoodCost = 80, CharcoalKilnWork = 200, CharcoalKilnHp = 200, CharcoalTicks = 60;
        public int SteelworksSizeCells = 3, SteelworksWoodCost = 150, SteelworksWork = 300, SteelworksHp = 300, SteelTicks = 80;
        /// <summary>V3-6 provisional values: heavy infantry is about twice the forged infantry HP and 1.5x its attack.</summary>
        public int HeavyInfantryFoodCost = 60, HeavyInfantryWoodCost = 0, HeavyInfantrySteelCost = 2, HeavyInfantryTrainTicks = 320;
        public int HeavyInfantryHp = 280, HeavyInfantryDamage = 20, HeavyInfantryAttackIntervalTicks = 20;
        public Fix64 HeavyInfantrySpeed = Fix64.FromRaw(98304), HeavyInfantryVision = Fix64.FromInt(20), HeavyInfantryRange = Fix64.FromInt(2);

        /// <summary>
        /// V3-4 (technical-design-v3 26): everyone starts in the primitive age and advances into a civilisation. Mines,
        /// smelters and the metal cost of infantry then belong to the metallurgy civilisation only. False keeps V3-2/V3-3.
        /// </summary>
        public bool Ages;
        /// <summary>V3-7: enables the forestry civilisation and its lumber camps. False keeps the prior age maps unchanged.</summary>
        public bool Forestry;
        /// <summary>V3-8: enables the masonry civilisation and its quarries. False keeps prior age maps unchanged.</summary>
        public bool Masonry;
        /// <summary>V3-9 #1: enables the caravan civilisation. False keeps prior age maps unchanged.</summary>
        public bool Caravan;
        /// <summary>V3-10 #1: enables the cavalry civilisation and its early stable/light cavalry rules.</summary>
        public bool Cavalry;
        /// <summary>V3-11 #1: enables the engineer-and-bridging civilisation. False keeps prior age maps unchanged.</summary>
        public bool Bridge;
        /// <summary>V3-12 #1: enables the academy civilisation. Its rules are kept in the length-delimited extension section.</summary>
        public bool Academy;
        /// <summary>V3-11 #1 provisional engineer camp values; the first pass matches a lumber camp.</summary>
        public int EngineerCampSizeCells = 2, EngineerCampWoodCost = 80, EngineerCampWork = 200, EngineerCampHp = 400;
        /// <summary>V3-11 #1 provisional short bridge values. BridgeWoodCost is paid once per bridge.</summary>
        public int BridgeWoodCost = 40, BridgeWork = 120, BridgeHp = 400, MaxBridgeLength = 6;
        /// <summary>V3-11 #3 provisional bridge-civilisation tech values. The first tech hardens and speeds bridges.</summary>
        public int BridgeworksFoodCost = 200, BridgeworksWoodCost = 150, BridgeworksTicks = 700;
        public int BridgeworksHpBonus = 200, BridgeworksWorkReduction = 40;
        /// <summary>The third-age bridge tech reuses the existing siege workshop and ram, making ram deployment quicker and larger.</summary>
        public int SiegeDeploymentFoodCost = 250, SiegeDeploymentWoodCost = 200, SiegeDeploymentTicks = 800;
        public int SiegeDeploymentRamTicksReduction = 100, SiegeDeploymentRamCapacityBonus = 1;
        /// <summary>V3-8 provisional quarry values; they intentionally match the lumber camp for the first pass.</summary>
        public int QuarrySizeCells = 2, QuarryWoodCost = 80, QuarryWork = 200, QuarryHp = 400, QuarryIntervalTicks = 20;
        /// <summary>Advancing: food and wood paid at the start, and the ticks it takes at the core.</summary>
        public int AdvanceFoodCost = 400, AdvanceWoodCost = 300, AdvanceTicks = 1200;
        /// <summary>Early arms economy: faster gathering and lower age prices are opt-in and never affect old scenarios.</summary>
        public int EarlyGatherIntervalTicks = 10;
        public int EarlyAdvanceFoodCost = 200, EarlyAdvanceWoodCost = 120, EarlyAdvanceTicks = 600;
        public int EarlyAge2FoodCost = 300, EarlyAge2WoodCost = 200, EarlyAge2Ticks = 600;
        public int EarlyAdvanceVillagers = 5, EarlyAge2Villagers = 6;
        /// <summary>V3-4 agrarian (27, 26.2): infantry is cheaper and quicker to train.</summary>
        public int AgrarianInfantryFood = 35, AgrarianInfantryWood = 10, AgrarianInfantryTicks = 200;
        /// <summary>V3-4 metallurgy (27): trained infantry is born with these HP and damage.</summary>
        public int ForgedInfantryHp = 140, ForgedInfantryDamage = 13;
        /// <summary>V3-4 farm (27): footprint, wood, work, HP. It makes one food every FarmBaseTicks, FarmStepTicks faster
        /// for each food point within FarmFoodReach metres and once more with river within FarmRiverReach, never under FarmMinTicks.</summary>
        public int FarmSizeCells = 2, FarmWoodCost = 80, FarmWork = 200, FarmHp = 400;
        public int FarmBaseTicks = 60, FarmStepTicks = 10, FarmMinTicks = 20, FarmFoodReach = 16, FarmRiverReach = 8;
        /// <summary>technical-design-v3 38.7: lets every age/civilisation build farms without changing agrarian strengths.</summary>
        public bool OpenFarms;
        public int OpenFarmsOtherCivExtraTicks = 20, OpenFarmsPrimitiveAiFarms = 2, OpenFarmsOtherCivAiFarms = 4;
        /// <summary>Technical-design-v3 38.9: the large-map early-start opening. False preserves the original three villagers.</summary>
        public bool EarlyStart;
        public int EarlyStartVillagers = 10, EarlyStartVillagersPerFarm = 5, EarlyStartPrimitiveFarmMax = 6;
        /// <summary>Rations: agrarian mills turn food into stock used for soldier training and quiet-time healing.</summary>
        public bool Rations;
        /// <summary>Resource fog: resources are shown and used by the automatic economy only after their cell was explored.</summary>
        public bool ResourceFog;
        /// <summary>ExploreScouting: scouts visit unexplored chunks in deterministic core-distance order.</summary>
        public bool ExploreScouting;
        public int ExploreChunkCells = 8;
        public int ExploreMinUnexploredPermille = 500;
        public int ExploreMinUnexploredPermilleStage2 = 250;
        public int ExploreMinUnexploredPermilleStage3 = 100;
        public int ExploreMinUnexploredPermilleStage4 = 1;
        public int ExploreRetargetTicks = 20;
        public int ExploreAvoidChunks = 1;
        public int ExploreAvoidTicks = 1200;
        public int ExploreAutoUntilTick = 7200;
        public int MillSizeCells = 2, MillWoodCost = 80, MillWork = 200, MillHp = 400, MillTicks = 80, MillFoodInput = 3;
        public int RationsPerSoldier = 1, RationsTrainPermille = 700;
        public int RationsHealIntervalTicks = 200, RationsHealQuietTicks = 200, RationsHealHp = 10, RationsHealMaxPerInterval = 10;
        public int RationsFarmMinimum = 2, RationsFoodReserve = 120;
        /// <summary>EasyRebuild: refunds paid materials on destruction and permits moving completed own buildings.</summary>
        public bool EasyRebuild;
        /// <summary>EasyRebuild: ticks for which a moved building remains paused.</summary>
        public int MoveBuildingPauseTicks = 60;
        /// <summary>Allows human building commands to touch an existing building without changing automatic site searches.</summary>
        public bool AdjacentBuildings;
        public int AdjacentBuildingClearanceCells = 1;
        /// <summary>V3-5 drop-site limit: both human and automatic placement share this cap, including construction.</summary>
        public bool DropSiteLimit;
        public int DropSiteMax = 2;
        /// <summary>AI raw-resource belts: the automatic economy keeps this much wood after laying a route.</summary>
        public bool AiBelts;
        public int AiBeltWoodReserve = 40;
        /// <summary>Allows a storage output belt to feed an own receiving building without first reaching the core.</summary>
        public bool StorageFeedsProcessing;
        /// <summary>Bricks: the masonry civilisation turns stone and charcoal into defensive stock.</summary>
        public bool Bricks;
        public int BrickKilnSizeCells = 2, BrickKilnWoodCost = 80, BrickKilnWork = 200, BrickKilnHp = 400;
        public int BrickTicks = 100, BrickStoneInput = 2, BrickCharcoalInput = 1;
        public int BricksPerWall = 1, BricksPerTower = 3, BricksPerCastle = 10;
        public int BrickHpPermille = 1500, BrickUpgradeIntervalTicks = 100, BrickCastleStonePermille = 600;
        /// <summary>Bricks continuation: masonry cores spend stored bricks on periodic repairs, and the automatic economy may add towers while that stock exists.</summary>
        public int BrickCoreRepairIntervalTicks = 100, BricksPerCoreRepair = 2, BrickCoreRepairHp = 60, BrickExtraTowers = 2;
        /// <summary>Tack: the cavalry civilisation turns food and wood into tack for heavy cavalry.</summary>
        public bool Tack;
        public int SaddlerySizeCells = 2, SaddleryWoodCost = 80, TackWoodReserve = 20, SaddleryWork = 200, SaddleryHp = 400;
        public int TackTicks = 100, TackFoodInput = 2, TackWoodInput = 2;
        public int HeavyCavalryTackCost = 2, HeavyCavalryFoodCost = 70, HeavyCavalryTrainTicks = 300;
        public int HeavyCavalryHp = 128, HeavyCavalryDamage = 10, HeavyCavalryAttackIntervalTicks = 20;
        public Fix64 HeavyCavalryRange = Fix64.FromInt(2), HeavyCavalrySpeed = Fix64.FromInt(4), HeavyCavalryVision = Fix64.FromInt(24);
        /// <summary>Planks: the bridge civilisation turns wood into planks for rams and bridges.</summary>
        public bool Planks;
        public int SawmillSizeCells = 2, SawmillWoodCost = 80, SawmillWork = 200, SawmillHp = 400;
        public int PlankTicks = 80, PlankWoodInput = 3, PlanksPerRam = 3;
        public int PlankRamHpPermille = 1500, PlankRamTrainPermille = 700;
        public int PlanksPerBridgeCell = 1, PlankBridgeWoodPermille = 500;
        public int PlankExtraRams = 1, PlankWoodReserve = 20;
        /// <summary>CraftTree1: shared first-stage processing, frames, and level-two processing buildings.</summary>
        public bool CraftTree1;
        public int CraftTreePlankWoodInput = 2, CraftTreePlankTicks = 60;
        public int CraftTreeCharcoalWoodInput = 2, CraftTreeCharcoalTicks = 60;
        public int StonecutterSizeCells = 2, StonecutterWoodCost = 80, StonecutterWork = 200, StonecutterHp = 400;
        public int CraftTreeStoneInput = 2, CraftTreeStonecutterTicks = 60;
        public int JoinerySizeCells = 2, JoineryWoodCost = 100, JoineryWork = 250, JoineryHp = 500;
        public int CraftTreeJoineryTicks = 80, CraftTreeJoineryPlankInput = 2, CraftTreeJoineryCutStoneInput = 1;
        public int CraftTreeFramesPerBuilding = 1, CraftTreeFrameWoodPermille = 700, CraftTreeFrameWorkPermille = 700;
        public int CraftTreeLevel2FramesCost = 2;
        /// <summary>PrimitiveCraft: the age-zero hut and its two first recipes.</summary>
        public bool PrimitiveCraft;
        public int HutSizeCells = 2, HutWoodCost = 40, HutWork = 100, HutHp = 250;
        public int PrimitiveCraftTicks = 60, StoneToolsStoneInput = 1, StoneToolsWoodInput = 1;
        public int JerkyFoodInput = 3, JerkyOutput = 2, StoneToolsGatherIntervalPermille = 850, JerkyHpPermille = 1100;
        /// <summary>AiFactory (technical-design-v3 37.8): deterministic line templates and restrained barracks growth.</summary>
        public bool AiFactory;
        public int AiFactoryPrimitiveLineCount = 3, AiFactoryAge1LineCount = 1, AiFactoryAge2LineCount = 2, AiFactoryAge3LineCount = 1;
        public int AiFactoryBarracksIdleWindowTicks = 1200, AiFactoryBarracksIdlePermille = 100;
        public int AiFactoryPrimitiveBarracksMaxBuildings = 1, AiFactoryAge1BarracksMaxBuildings = 2;
        public int AiFactoryAge2BarracksMaxBuildings = 4, AiFactoryAge3BarracksMaxBuildings = 6, AiFactoryAge4BarracksMaxBuildings = 8;
        /// <summary>CraftTree2: the shared second-stage assembler and its effects.</summary>
        public bool CraftTree2;
        public int AssemblerSizeCells = 2, AssemblerWoodCost = 120, AssemblerWork = 300, AssemblerHp = 600;
        public int ArmsKitTicks = 80, ArmsKitPlankInput = 1, ArmsKitCharcoalInput = 2, ArmsKitSteelInput = 1;
        public int ScholarlyTicks = 100, ScholarlyPlankInput = 1, ScholarlyCharcoalInput = 1, ScholarlyFoodInput = 2;
        public int ArmsKitAttack = 2, ArmsKitHpPermille = 1100, FineArmsKitAttack = 3, FineArmsKitHpPermille = 1150;
        public int ScholarlyPerResearch = 1, ScholarlyCostPermille = 600, ScholarlyTimePermille = 700;
        public int GoodsValueArmsKit = 6, GoodsValueScholarly = 8, GoodsValueFineArmsKit = 14;
        /// <summary>CraftRequired (technical-design-v3 38.2): age-two soldiers require one arms kit.</summary>
        public bool CraftRequired;
        public int CraftRequiredAiArmsStockTarget = 20;
        public int CraftRequiredAiArmsAssemblersAge1 = 1, CraftRequiredAiArmsAssemblersAge2 = 2,
            CraftRequiredAiArmsAssemblersAge3 = 4, CraftRequiredAiArmsAssemblersAge4 = 4;
        /// <summary>CraftRequired: extra wood-gatherer share added to the normal large-economy floor after age 1.</summary>
        public int CraftRequiredAiWoodWorkerPermille = 50;
        /// <summary>CraftRequired: buildings from age two pay this share of their effective wood price in planks.</summary>
        public int CraftRequiredBuildingPlankPermille = 300;
        /// <summary>CraftRequired: scholarly stock required by researches in ages two through four.</summary>
        public int CraftRequiredResearchScholarlyAge2 = 2, CraftRequiredResearchScholarlyAge3 = 4,
            CraftRequiredResearchScholarlyAge4 = 6;
        /// <summary>CraftRequired: additional common sawmills for the building and belt plank demand.</summary>
        public int CraftRequiredAiPlankSawmillsAge1 = 1, CraftRequiredAiPlankSawmillsAge2 = 2,
            CraftRequiredAiPlankSawmillsAge3 = 3;
        /// <summary>CraftRequired: assemblers reserved for scholarly stock and the frames stock target.</summary>
        public int CraftRequiredAiScholarlyAssemblers = 1, CraftRequiredAiFramesStockTarget = 10;
        /// <summary>CraftRequired: automatic scholarly stock is kept ahead of this many researches.</summary>
        public int CraftRequiredAiScholarlyStockResearches = 2;
        /// <summary>CraftRequired: at this many assemblers, one line is reserved for scholarly goods.</summary>
        public int CraftRequiredAiScholarlyMinAssemblers = 2;
        /// <summary>CraftTree3: siege parts, mechanisms, level-three processing, and third-age research.</summary>
        public bool CraftTree3;
        public int SiegePartsTicks = 120, SiegePartsFramesInput = 1, SiegePartsArmsKitInput = 1;
        public int MechanismTicks = 150, MechanismArmsKitInput = 1, MechanismScholarlyInput = 1, MechanismCutStoneInput = 1;
        public int SiegePartsHpPermille = 1500, SiegePartsDamagePermille = 1300, SiegePartsPerCastle = 3, SiegePartsCastleStonePermille = 700;
        public int MechanismBuildingTimePermille = 500, MechanismBuildingMaterialPermille = 900;
        public int MechanismResearchCostPermille = 500, MechanismResearchTimePermille = 500;
        /// <summary>Automatic CraftTree3 keeps this many siege parts before changing back to the next useful recipe.</summary>
        public int SiegePartsStockLimit = 8;
        public int GoodsValueSiegeParts = 20, GoodsValueMechanism = 22;
        /// <summary>CraftTree4: fourth-age siege engines and elite kits.</summary>
        public bool CraftTree4;
        public int SiegeEngineTicks = 200, SiegeEngineSiegePartsInput = 2, SiegeEngineMechanismInput = 1;
        public int EliteKitTicks = 180, EliteKitArmsKitInput = 2, EliteKitMechanismInput = 1, EliteKitMetalInput = 5;
        public int EliteKitAttack = 3, EliteKitHpPermille = 1200;
        public int GoodsValueSiegeEngine = 30, GoodsValueEliteKit = 28;
        /// <summary>Books: academy scriptoria turn gold and wood into research stock.</summary>
        public bool Books;
        public int ScriptoriumSizeCells = 2, ScriptoriumWoodCost = 80, ScriptoriumWork = 200, ScriptoriumHp = 400;
        public int BookTicks = 120, BookGoldInput = 1, BookWoodInput = 2;
        public int BooksPerResearch = 3, BookResearchPermille = 500, BookResearchCostPermille = 700;
        public int BookWoodReserve = 20;
        /// <summary>Preserves: the fishing civilisation turns food into more food at drying racks.</summary>
        public bool Preserves;
        public int DryingRackSizeCells = 2, DryingRackWoodCost = 80, DryingRackWork = 200, DryingRackHp = 400;
        public int PreserveTicks = 60, PreserveFoodInput = 2, PreserveFoodValue = 3;
        public int PreserveKeepFood = 60, PreserveMaxRacks = 2, PreserveWoodReserve = 20;
        /// <summary>Jewels: the mountain civilisation turns stored stone and ore into gems for mercenaries.</summary>
        public bool Jewels;
        public int LapidarySizeCells = 2, LapidaryWoodCost = 80, LapidaryWork = 200, LapidaryHp = 400;
        public int JewelTicks = 80, JewelStoneInput = 3, JewelOreInput = 1;
        public int JewelMaxLapidaries = 1, JewelKeepStone = 40, JewelWoodReserve = 20;
        /// <summary>TradeGoods: caravan weavers turn stored wood into trade goods that become the lower core stock.</summary>
        public bool TradeGoods;
        public int WeaverSizeCells = 2, WeaverWoodCost = 80, WeaverWork = 200, WeaverHp = 400;
        public int TradeGoodsTicks = 80, TradeGoodsWoodInput = 3, TradeGoodsKeepWood = 80, TradeGoodsValue = 4;
        public int TradeGoodsMaxWeavers = 1, TradeGoodsWoodReserve = 20;
        /// <summary>Incense: the cult civilisation turns wood and food into stock that heals soldiers during battle.</summary>
        public bool Incense;
        public int IncenseSizeCells = 2, IncenseWoodCost = 80, IncenseWork = 200, IncenseHp = 400;
        public int IncenseTicks = 80, IncenseWoodInput = 2, IncenseFoodInput = 1;
        public int IncenseKeepWood = 80, IncenseKeepFood = 60, IncenseStockLimit = 30;
        public int IncenseHealIntervalTicks = 40, IncenseHealHp = 20, IncenseMaxHouses = 1, IncenseWoodReserve = 20;
        /// <summary>Sanctuary charms: wood is turned into stock for blessed soldiers.</summary>
        public bool Charms;
        public int CharmSizeCells = 2, CharmWoodCost = 80, CharmWork = 200, CharmHp = 400;
        public int CharmTicks = 100, CharmWoodInput = 3, CharmKeepWood = 80, CharmStockLimit = 20;
        public int CharmMaxWorkshops = 1, CharmWoodReserve = 20, CharmHpPermille = 1200;
        /// <summary>Tollgate repair kits: stored wood is turned into kits for deterministic building and core repairs.</summary>
        public bool RepairKits;
        public int RepairYardSizeCells = 2, RepairYardWoodCost = 80, RepairYardWork = 200, RepairYardHp = 400;
        public int RepairKitTicks = 80, RepairKitWoodInput = 2, RepairKitKeepWood = 80, RepairKitStockLimit = 20;
        public int RepairKitMaxYards = 1, RepairKitWoodReserve = 20, RepairKitIntervalTicks = 60, RepairKitHp = 40;
        /// <summary>Metropolis tools: wood is turned into stock that discounts villager training.</summary>
        public bool Tools;
        public int ToolShopSizeCells = 2, ToolShopWoodCost = 80, ToolShopWork = 200, ToolShopHp = 400;
        public int ToolTicks = 80, ToolWoodInput = 2, ToolKeepWood = 80, ToolStockLimit = 10;
        public int ToolMaxShops = 1, ToolWoodReserve = 20, ToolVillagerFoodPermille = 700, ToolVillagerTicksPermille = 500;
        /// <summary>Goods market: finished civilisation goods can be sold in small lots for food, wood, or stone.</summary>
        public bool GoodsMarket;
        public int GoodsTradeLot = 5, GoodsAutoSellAt = 15, GoodsAutoSellIntervalTicks = 100, GoodsAutoKeep = 5, GoodsMarketWoodReserve = 20;
        public int GoodsValueSteel = 8, GoodsValueBowGear = 4, GoodsValueRations = 6, GoodsValueBricks = 6;
        public int GoodsValueTack = 8, GoodsValuePlanks = 6, GoodsValueBooks = 6, GoodsValueIncense = 6;
        public int GoodsValueCharms = 6, GoodsValueRepairKits = 4, GoodsValueTools = 4;
        /// <summary>Craft timing: automatic economies gather more wood until their civilisation's first processing building is complete.</summary>
        public bool CraftTiming;
        public int CraftTimingFoodPerWood = 1;
        /// <summary>Craft balance: keeps the tollgate outside CraftTiming when set to 0.</summary>
        public bool CraftBalance;
        public int CraftBalanceTollgateTiming;
        // Measured 2026-10-13: the mountain piles up stone and ore but is short of food, so the hire costs no food.
        public int JewelMercenaryGems = 10, JewelMercenaryFood = 0, JewelMercenaryInfantryRatio = 2;
        /// <summary>V3-7 provisional lumber camp values. The camp gathers wood from one covered wood point.</summary>
        public int LumberCampSizeCells = 2, LumberCampWoodCost = 80, LumberCampWork = 200, LumberCampHp = 400, LumberCampIntervalTicks = 20;
        /// <summary>V3-7 provisional market-food policy for non-agrarian civilizations.</summary>
        public int MarketFoodFloor = 150, MarketWoodReserve = 300, MarketStoneReserve = 100;
        /// <summary>V3-9 #1 provisional caravanserai values. The route pair and reward are fixed when the building is placed.</summary>
        public int CaravanseraiSizeCells = 2, CaravanseraiWoodCost = 80, CaravanseraiWork = 200, CaravanseraiHp = 400;
        // 24m leaves enough room for a two-cell host footprint around the snapped outpost centre while keeping
        // the host tied to its registered outpost. This is a provisional placement value, not a reward distance.
        public int CaravanOutpostReach = 24, CaravanMinimumDistance = 20, CaravanRewardDistanceStep = 10, CaravanRewardMaxWood = 30;
        public int CaravanAutoVillagers = 2;
        /// <summary>V3-12 #1 provisional academy values; building cost is wood only and research uses fixed gold prices.</summary>
        public int AcademySizeCells = 2, AcademyWoodCost = 100, AcademyWork = 200, AcademyHp = 400;
        public int AcademyToolsFoodCost = 50, AcademyToolsWoodCost = 25, AcademyToolsGoldCost = 50, AcademyToolsTicks = 200;
        public int AcademyCartsFoodCost = 50, AcademyCartsWoodCost = 50, AcademyCartsGoldCost = 50, AcademyCartsTicks = 200;
        /// <summary>V3-18 #1 provisional shrine values. A shrine is tied to one outpost and costs wood and stone.</summary>
        public int ShrineSizeCells = 2, ShrineWoodCost = 80, ShrineStoneCost = 40, ShrineWork = 200, ShrineHp = 300;
        public int ShrineOutpostReach = 24, SanctuaryAttackBonusPermille = 100, SanctuaryMaxBonusPermille = 300;
        /// <summary>V3-18 #2 provisional sanctuary research values. Both researches are performed at a shrine.</summary>
        public int SanctuaryPilgrimageFoodCost = 150, SanctuaryPilgrimageWoodCost = 100, SanctuaryPilgrimageTicks = 500;
        public int SanctuaryRelicFoodCost = 250, SanctuaryRelicWoodCost = 200, SanctuaryRelicTicks = 700;
        public int SanctuaryPilgrimageRadius = 16, SanctuaryPilgrimageIntervalTicks = 20, SanctuaryPilgrimageHeal = 1;
        public int SanctuaryRelicBonusPermille = 150, SanctuaryRelicMaxBonusPermille = 450;
        /// <summary>V3-14 #1 provisional fishing-civilisation values; the harbour is the fish drop-off.</summary>
        public bool FishingCiv;
        public int HarborSizeCells = 2, HarborWoodCost = 100, HarborWork = 200, HarborHp = 400;
        /// <summary>V3-14 #2 provisional fishing-civilisation research values.</summary>
        public int FishingNetFoodCost = 150, FishingNetWoodCost = 100, FishingNetTicks = 400;
        public int DriedFishFoodCost = 200, DriedFishWoodCost = 150, DriedFishTicks = 500;
        public int FishingNetCarryBonusPermille = 500, DriedFishRegrowIntervalPermille = 333;
        /// <summary>V3-13 #1: enables the cult civilisation. Its rules are kept in the length-delimited extension section.</summary>
        public bool Cult;
        /// <summary>V3-17 #1: enables the metropolis civilisation. Its rules are kept in extension ID 7.</summary>
        public bool Metropolis;
        /// <summary>V3-18 #1: enables the sanctuary civilisation. Its rules are kept in extension ID 8.</summary>
        public bool Sanctuary;
        /// <summary>Early arms and the accelerated automatic economy. False preserves the historical age-map decisions.</summary>
        public bool EarlyArms;
        /// <summary>CivVariety: compare unlike civilisation terrain scores on one integer scale.</summary>
        public bool CivVariety;
        /// <summary>CivVariety: near-best normalised scores remain eligible for the seed-based choice.</summary>
        public int CivVarietyWindowPermille = 250;
        /// <summary>CivVarietyCaps: use measured upper-quartile caps for agrarian and metallurgy.</summary>
        public bool CivVarietyCaps;
        public int CivVarietyCapMetallurgy = 5, CivVarietyCapAgrarian = 3;
        /// <summary>Age clock: ages arrive at fixed ticks, with the original price discounted as that tick approaches.</summary>
        public bool AgeClock;
        public int AgeClockTicks1 = 4800, AgeClockTicks2 = 10800, AgeClockTicks3 = 15600, AgeClockTicks4 = 24000;
        /// <summary>Technical-design-v3 37: opts into the fifth age stage and its age-four-only rules.</summary>
        public bool AgeFour;
        /// <summary>AgeFourAi: lets the automatic economy sustain the age-four research and craft chain.</summary>
        public bool AgeFourAi;
        /// <summary>AgeFourAiIndustry: opts into the additional processing buildings and assembler recipe policy.</summary>
        public bool AgeFourAiIndustry;
        public int AgeFourAiAssemblers = 3;
        /// <summary>AgeClockUx: separates civilisation reservation from payment and gives manual players time to choose after the clock.</summary>
        public bool AgeClockUx;
        public int AgeClockUxChoiceTicks = 600;
        /// <summary>S-3: enables outpost towns. Its rules are kept in extension ID 11.</summary>
        public bool Towns;
        /// <summary>V3-13 #1 provisional monastery and cult-only monk training values. The monastery costs wood; its monk costs food and wood.</summary>
        public int MonasterySizeCells = 2, MonasteryWoodCost = 100, MonasteryWork = 200, MonasteryHp = 400;
        public int MonasteryMonkFoodCost = 80, MonasteryMonkWoodCost = 40;
        /// <summary>V3-13 #3 provisional cult research prices and effects. These are stored in extension ID 3 version 2.</summary>
        public int SermonFoodCost = 150, SermonWoodCost = 100, SermonTicks = 400;
        public int MartyrBlessingFoodCost = 200, MartyrBlessingWoodCost = 150, MartyrBlessingTicks = 500;
        public int MartyrBlessingHpPermille = 1500;
        /// <summary>V3-15 #1 provisional mountain values; a mine shaft uses mountain cells, not a resource node.</summary>
        public bool Mountain;
        public int MountainSizeCells = 2, MountainWoodCost = 100, MountainWork = 200, MountainHp = 400;
        public int MountainBaseIntervalTicks = 40, MountainIntervalStepTicks = 4, MountainMinIntervalTicks = 20;
        public int MountainMaxBuildings = 3, MountainMaxAdjacentCells = 4, MountainStoneYield = 1, MountainOreYield = 1;
        /// <summary>V3-15 #2 provisional mountain research prices, times and effects.</summary>
        public int MountainDeepShaftFoodCost = 250, MountainDeepShaftWoodCost = 150, MountainDeepShaftTicks = 600;
        public int MountainFortFoodCost = 400, MountainFortWoodCost = 250, MountainFortTicks = 800;
        public int MountainDeepShaftIntervalPermille = 667, MountainDeepShaftMaxBuildingsBonus = 1;
        public int MountainFortHpPermille = 1500, MountainFortRangeBonus = 2;
        /// <summary>V3-16: a completed tollgate is passable to its own faction only.</summary>
        public bool Tollgate;
        public int TollgateLengthCells = 2, TollgateWoodCost = 80, TollgateStoneCost = 40, TollgateWork = 250, TollgateHp = 600;
        public int TollgateMaxBuildings = 3;
        /// <summary>V3-16 #2: toll per completed gate, and the second/third-age tollgate research values.</summary>
        public int TollgateFeeRadiusMeters = 8, TollgateFeeIntervalTicks = 100, TollgateWoodPerEnemy = 1, TollgateFoodPerEnemy = 1;
        public int TollgateNetworkRadiusMeters = 12, TollgateNetworkFeeBonusPermille = 500;
        public int TollgateGateDefenceFoodCost = 350, TollgateGateDefenceWoodCost = 250, TollgateGateDefenceTicks = 700;
        public int TollgateGateNetworkFoodCost = 500, TollgateGateNetworkWoodCost = 350, TollgateGateNetworkTicks = 900;
        public int TollgateGateDefenceHpPermille = 1500, TollgateGateDefenceDamageReductionPermille = 200, TollgateGateNetworkMaxBuildingsBonus = 2;
        /// <summary>V3-17 #1 provisional grand-house values; one completed grand house adds three ordinary house slots.</summary>
        public int GrandHouseSizeCells = 3, GrandHouseWoodCost = 150, GrandHouseWork = 300, GrandHouseHp = 500;
        /// <summary>V3-17 #2 provisional metropolis research values. Both researches are performed at a grand house.</summary>
        public int MetropolisMarketFoodCost = 200, MetropolisMarketWoodCost = 150, MetropolisMarketTicks = 500;
        public int MetropolisMilitiaFoodCost = 300, MetropolisMilitiaWoodCost = 200, MetropolisMilitiaTicks = 700;
        public int MetropolisMarketVillagerStep = 10, MetropolisMarketBonusPermille = 20, MetropolisMarketMaxBonusPermille = 100;
        public int MetropolisMilitiaDamage = 3, MetropolisMilitiaCoreRadius = 12;
        /// <summary>S-3 provisional town values: a four-cell remote drop-off and villager-training base.</summary>
        public int TownSizeCells = 4, TownWoodCost = 300, TownStoneCost = 200, TownWork = 800, TownHp = 2000;
        public int TownMaxBuildings = 3, TownCoreDistance = 60, TownResourceReach = 16;
        /// <summary>V3-7 #2 provisional bow workshop values. Wood is the primary slot and food the secondary slot.</summary>
        public int FletcherSizeCells = 3, FletcherWoodCost = 150, FletcherWork = 300, FletcherHp = 300, FletcherTicks = 80;
        public int FletcherWoodInput = 1, FletcherFoodInput = 1;
        /// <summary>V3-7 #2 provisional skirmish archer values. It is born as infantry and keeps the infantry combat path.</summary>
        public int SkirmishArcherFoodCost = 50, SkirmishArcherBowGearCost = 1, SkirmishArcherTrainTicks = 250;
        public int SkirmishArcherHp = 50, SkirmishArcherDamage = 7, SkirmishArcherAttackIntervalTicks = 25;
        public Fix64 SkirmishArcherSpeed = Fix64.FromInt(3), SkirmishArcherVision = Fix64.FromInt(22), SkirmishArcherRange = Fix64.FromInt(8);
        /// <summary>V3-5 (32): scouts trained at a barracks on a map with ages.</summary>
        public int ScoutFoodCost = 40, ScoutWoodCost = 0, ScoutTrainTicks = 200;
        /// <summary>V3-5 (32 #2): the population cap starts at BasePopulation and each finished house adds HousePopulation,
        /// never over PopulationCap.</summary>
        public int BasePopulation = 30, HousePopulation = 5;
        public int HouseSizeCells = 2, HouseWoodCost = 30, HouseWork = 100, HouseHp = 250;
        /// <summary>V3-5 (32 #3): a drop-off where villagers leave what they carry instead of walking to the core.</summary>
        public int DropSiteSizeCells = 2, DropSiteWoodCost = 50, DropSiteWork = 120, DropSiteHp = 300;
        /// <summary>V3-5 (32 #5): a wall cell (stands at once, no builders), placed within WallReach metres of the own core or a
        /// finished own building, never so that the own core loses its way to the enemy core.</summary>
        public int WallStoneCost = 3, WallHp = 400, WallReach = 30;
        /// <summary>V3-8 #2: masonry's fraction of wall, tower and castle costs after discount. Integer floor is used.</summary>
        public int MasonryDefenceCostPermille = 750, MasonryDefenceWorkPermille = 800;
        /// <summary>Core defence: a core fires at nearby enemy soldiers (or villagers when no soldier is in range).</summary>
        public bool CoreDefence;
        public int CoreDefenceRange = 16, CoreDefenceDamage = 30, CoreDefenceIntervalTicks = 20, CoreDefenceMaxTargets = 3;
        /// <summary>S-5: after a late-game condition, automatic armies keep a deterministic focus on the enemy core.</summary>
        public bool LatePush;
        public int LatePushAfterTicks = 14400, LatePushAdvantageAfterTicks = 9600, LatePushEnemyMultiplierPermille = 1500;
        /// <summary>Siege by age: soldiers and rams deal age-scaled damage to cores and buildings.</summary>
        public bool SiegeByAge;
        public int SiegeAge0Permille = 100, SiegeAge1Permille = 250, SiegeAge2Permille = 600, SiegeAge3Permille = 4000, SiegeAge4Permille = 4000;
        /// <summary>V3-32.33: gather automatic late pushes into deterministic attack waves.</summary>
        public bool PushWaves;
        public int WaveStagingDistance = 25, WaveSize = 24, WaveMaxWaitTicks = 2400, WaveMinSize = 6, WaveRetreatPermille = 200;
        /// <summary>Manual control: allows a faction to stop its built-in late assault.</summary>
        public bool ManualControl;
        /// <summary>Returns completed human movement and attack orders to the automatic army policy.</summary>
        public bool AutoReturnAfterHuman;
        public int AutoReturnArriveMeters = 4, AutoReturnHoldTicks = 200;
        /// <summary>Stone and metal each side starts with (maps with ages).</summary>
        public int StartStone, StartMetal;
        /// <summary>V3-5 (32 #5): a tower shoots the nearest enemy soldier (else villager) in TowerRange metres, and sees around it.</summary>
        public int TowerSizeCells = 2, TowerWoodCost = 50, TowerStoneCost = 100, TowerWork = 250, TowerHp = 800;
        public int TowerRange = 10, TowerVision = 16, TowerDamage = 6, TowerIntervalTicks = 20;
        /// <summary>V3-5 (32 #6): the blacksmith, and the price and time of each tech (index TechKind - 1).</summary>
        public int BlacksmithSizeCells = 3, BlacksmithWoodCost = 150, BlacksmithWork = 300, BlacksmithHp = 700;
        public int[] TechFood = { 150, 150, 100, 100, 120, 120, 200, 150, 200, 200, 200, 250 };
        public int[] TechWood = { 100, 100, 50, 100, 80, 80, 150, 200, 100, 100, 100, 150 };
        public int[] TechTicks = { 600, 600, 400, 400, 500, 500, 700, 700, 600, 800, 800, 900 };
        /// <summary>V3-5 (32 #14): the metal the steel techs cost on top of food and wood; every other tech costs none.</summary>
        public int[] TechMetal = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 100, 120, 0 };
        /// <summary>V3-5 (32 #19): Gems paid by the final armour tech; every earlier tech costs none.</summary>
        public int[] TechGems = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 15 };
        /// <summary>What the techs do: attack, HP, ticks per gather, carry, farm and smelt ticks saved.</summary>
        public int WeaponsDamage = 2, ArmourHp = 20, ToolsGatherTicks = 5, CartsCarry = 5, IrrigationTicks = 10, BlastFurnaceTicks = 20;
        /// <summary>V3-5 (32 #10): what the third age's techs do - a heavier ram, harder tower shots, a better trade.</summary>
        public int SiegecraftSiegeDamage = 30, MasonryTowerDamage = 4, BankingTradeReturn = 25;
        /// <summary>V3-5 (32 #7): the second age inside a civilisation (city age for farming, iron age for metallurgy).</summary>
        public int Age2FoodCost = 800, Age2WoodCost = 500, Age2Ticks = 1800, Age2PopulationBonus = 20;
        /// <summary>0 keeps the stock-half rule; otherwise saving for the next age waits for this many living soldiers.</summary>
        public int Age2SaveArmyFloor;
        /// <summary>V3-5 (32 #8): archers (farming, second age) shoot from afar; cavalry (metallurgy, second age) is quick and hard.</summary>
        public int ArcherFood = 40, ArcherWood = 50, ArcherTicks = 250, ArcherHp = 60, ArcherDamage = 7, ArcherInterval = 25;
        public Fix64 ArcherRange = Fix64.FromInt(8), ArcherSpeed = Fix64.FromInt(2), ArcherVision = Fix64.FromInt(22);
        public int CavalryFood = 80, CavalryWood = 20, CavalryMetal = 10, CavalryTicks = 300, CavalryHp = 150, CavalryDamage = 12, CavalryInterval = 20;
        public Fix64 CavalryRange = Fix64.FromInt(2), CavalrySpeed = Fix64.FromInt(4), CavalryVision = Fix64.FromInt(24);
        /// <summary>V3-10 #1 provisional light cavalry: cheaper and quicker than cavalry, with lower HP and damage.</summary>
        public int LightCavalryFood = 50, LightCavalryWood = 10, LightCavalryTicks = 180, LightCavalryHp = 90,
            LightCavalryDamage = 8, LightCavalryInterval = 20;
        public Fix64 LightCavalryRange = Fix64.FromInt(2), LightCavalrySpeed = Fix64.FromInt(4), LightCavalryVision = Fix64.FromInt(24);
        /// <summary>V3-10 #2 provisional second-age cavalry drill: faster light-cavalry movement and replenishment.</summary>
        public int CavalryDrillFood = 150, CavalryDrillWood = 100, CavalryDrillTicks = 500;
        public Fix64 CavalryDrillSpeed = Fix64.FromInt(1);
        /// <summary>V3-5 (32 #9): a market trades TradeLot of food, wood or stone for TradeReturn of another.</summary>
        public int MarketSizeCells = 3, MarketWoodCost = 175, MarketWork = 300, MarketHp = 700, TradeLot = 100, TradeReturn = 60;
        /// <summary>V3-5 (32 #19): a market gives this many Gems when a normal lot is exchanged for Gems.</summary>
        public int GemsTradeReturn = 3;
        /// <summary>V3-5 32 #18: wood gained at the core for each completed trade-route round trip.</summary>
        public int TradeRouteWood = 25;
        /// <summary>V3-5 32 #18: minimum eligible trader count.</summary>
        public int TradeRouteMin = 1;
        /// <summary>V3-5 (32 #9): the siege workshop (second age) and its ram - slow, weak on soldiers, heavy on buildings and cores.</summary>
        public int WorkshopSizeCells = 3, WorkshopWoodCost = 200, WorkshopWork = 350, WorkshopHp = 800;
        public int RamFood = 40, RamWood = 160, RamTicks = 400, RamHp = 220, RamDamage = 3, RamSiegeDamage = 60, RamInterval = 40;
        public Fix64 RamRange = Fix64.FromInt(2), RamSpeed = Fix64.FromInt(1), RamVision = Fix64.FromInt(12);
        public int CannonSiegeEngineCost = 1, CannonFoodCost = 100, CannonWoodCost = 100, CannonHp = 180, CannonDamage = 1, CannonSiegeDamage = 120, CannonInterval = 50, CannonAutoLimit = 4;
        public Fix64 CannonRange = Fix64.FromInt(8), CannonSpeed = Fix64.FromInt(1), CannonVision = Fix64.FromInt(12);
        /// <summary>V3-5 (32 #10): the third age of a civilisation (trade age for farming, steel age for metallurgy).</summary>
        public int Age3FoodCost = 1200, Age3WoodCost = 800, Age3Ticks = 2400, Age3PopulationBonus = 20;
        /// <summary>AgeFour: the fourth advance uses the third advance's duration and has a +20 population ceiling.</summary>
        public int Age4FoodCost = 1080, Age4WoodCost = 720, Age4Ticks = 2400, Age4PopulationBonus = 20;
        /// <summary>AgeFour research values. These are kept as named fields because the older tech arrays are schema-stable.</summary>
        public int TotalWarFoodCost = 400, TotalWarWoodCost = 300, TotalWarMetalCost = 100, TotalWarTicks = 900;
        public int GrandWorkshopFoodCost = 300, GrandWorkshopWoodCost = 400, GrandWorkshopMetalCost = 0, GrandWorkshopTicks = 900;
        /// <summary>Technical-design-v3 37.5.1: civilisation age strengths. Only the AgeStrengths extension enables these.</summary>
        public bool AgeStrengths;
        public int AgrarianFarmSpeedPermille = 800, AgrarianRationsHealHp = 15, AgrarianPopulationCapBonus = 20;
        public int MetallurgySmeltSpeedPermille = 800, MetallurgyForgedDamageBonus = 1, MetallurgySiegePartsTimePermille = 700;
        public int ForestryLumberSpeedPermille = 800, ForestryArcherRangeBonus = 1, ForestryBuildingWoodPermille = 800;
        public int MasonryWallHpPermille = 1200, MasonryTowerDamageBonus = 2, MasonryCoreHpPermille = 1300;
        public int CaravanTradeGoodsValue = 5, CaravanMarketReturnPermille = 1100, CaravanRewardPermille = 1500;
        public int CavalryStableTimePermille = 800, CavalrySpeedPermille = 1100, CavalryHeavyHpPermille = 1150;
        public int BridgeBuildingWorkPermille = 700, BridgeRamSiegeDamagePermille = 1200, BridgeGeneralWorkPermille = 800;
        public int AcademyResearchTimePermille = 850, AcademyScholarlyCostPermille = 600, AcademyAge4ResearchCostPermille = 500;
        public int CultConversionTimePermille = 833, CultIncenseHealHp = 30, CultMonkHpPermille = 1500;
        public int FishingGatherTimePermille = 833, FishingPreserveFoodValue = 4, FishingFoodCarryBonus = 2;
        public int MountainMiningTimePermille = 833, MountainMercenaryGems = 8, MountainMercenaryDamageBonus = 2;
        public int TollgateFeePermille = 1500, TollgateHpPermille = 1300, TollgateRepairKitHp = 60;
        public int MetropolisVillagerTimePermille = 800, MetropolisTownLimitBonus = 1, MetropolisToolTimePermille = 400;
        public int SanctuaryPilgrimagePermille = 1200, SanctuaryCharmHpPermille = 1300, SanctuaryCharmHpAge4Permille = 1500;
        /// <summary>V3-5 (32 #21): optional victory after holding the third age while the core remains alive.</summary>
        public bool AgeVictoryEnabled;
        public int AgeVictoryTicks = 3000;
        /// <summary>V3-5 (32 #12): the archery range and the stable - from the second age, in either civilisation, so a side
        /// can field the unit its own civilisation does not train.</summary>
        public int RangeSizeCells = 3, RangeWoodCost = 150, RangeWork = 300, RangeHp = 600;
        /// <summary>V3-5 (32 #13): thousandths added to a blow that counters its target (archer over infantry, cavalry over
        /// archer, infantry over cavalry). 0 turns the triangle off.</summary>
        public int CounterBonusPermille = 500;
        /// <summary>V3-5 (32 #14): what the steel techs give, on top of weapons and armour. They are paid in metal, so the
        /// stock metallurgy piles up has a use and farming has to mine or trade for one.</summary>
        public int SteelWeaponsDamage = 3, SteelArmourHp = 30, GemArmorHp = 40;
        /// <summary>V3-5 (32 #15): HP a villager puts back into a damaged finished building each tick, and the share of its
        /// HP (in thousandths) under which the automatic economy sends villagers to repair it.</summary>
        public int RepairHpPerTick = 2, RepairAtPermille = 700;
        /// <summary>V3-5 (32 #17): the castle of the third age - stone and wood, hard to bring down, shoots further and
        /// harder than a tower, and trains any of the three line units whatever the civilisation.</summary>
        public int CastleSizeCells = 4, CastleWoodCost = 200, CastleStoneCost = 300, CastleWork = 600, CastleHp = 2400;
        public int CastleRange = 14, CastleVision = 20, CastleDamage = 14, CastleIntervalTicks = 20;
        /// <summary>V3-5 (32.20): a castle can hire mercenaries with Gems.</summary>
        public int MercenaryGems = 20, MercenaryTicks = 250, MercenaryHp = 150, MercenaryDamage = 14, MercenaryInterval = 25;
        public int StableSizeCells = 3, StableWoodCost = 150, StableWork = 300, StableHp = 600;
        /// <summary>V3-5 #22: optional monks convert enemy soldiers instead of dealing combat damage.</summary>
        public bool MonksEnabled;
        public int ConversionTicks = 400;
        public int MonkFoodCost = 60, MonkGoldCost = 40;
        public int MonkTrainTicks = 200;

        /// <summary>Optional gold deposits on the terrain. Defaults are deliberately omitted from the scenario binary.</summary>
        public bool GoldEnabled;
        public int Age3GoldCostAgrarian, Age3GoldCostMetallurgy, GoldGatherers = 2, GoldAmount = 400, GoldDangerMeters = 16;

        /// <summary>Optional river fishing on age maps. Defaults are deliberately omitted from the scenario binary.</summary>
        public bool FishingEnabled;
        public int FishRegrowTicks = 100, FishAgrarianBonusPermille = 300, FishReach = 6;
    }

    public sealed class MapDefinition
    {
        public int WidthMeters = 256, HeightMeters = 128, CellSizeMeters = 2;
        public int WidthCells = 128, HeightCells = 64;
        public bool DefaultPassable = true;
        public int[] BlockedCellIds = Array.Empty<int>();
        /// <summary>
        /// V3-4 (technical-design-v3 28.1): one TerrainKind per cell, or empty for plain ground everywhere. It only names
        /// what a blocked cell is; BlockedCellIds alone decides where units can go.
        /// </summary>
        public byte[] Terrain = Array.Empty<byte>();
    }

    /// <summary>V3-4 terrain kinds. Everything but Plain is impassable.</summary>
    public enum TerrainKind : byte { Plain = 0, Forest = 1, River = 2, Mountain = 3 }

    public sealed class RuleDefinition
    {
        public int FactionCap = 40;
        public Fix64 CoreRadius = Fix64.FromInt(4);
        public Fix64 OwnedObjectiveVision = Fix64.FromInt(24);
        public Fix64 CaptureRadius = Fix64.FromInt(8);
        public int CaptureDurationTicks = 200;
        public int CoreReinforcementIntervalTicks = 100, OutpostReinforcementIntervalTicks = 200;
        public int OccupationThreatMemoryTicks = 200;
        public ushort DefaultReservePermille = 100;
    }

    public struct UnitParameters
    {
        public UnitKind Kind;
        public int Hp, Damage, AttackIntervalTicks;
        public Fix64 Speed, Vision, Range;
    }

    public struct FactionDefinition
    {
        public uint Id, CoreId;
        public uint[] ArmyIds;
    }

    public struct CoreDefinition
    {
        public uint Id, FactionId;
        public SimPoint Position;
        public int Hp;
    }

    public struct OutpostDefinition
    {
        public uint Id, OwnerFactionId;
        public SimPoint Position;
    }

    public struct ArmyDefinition
    {
        public uint Id, FactionId;
        public string Role;
        public int Capacity;
        public PolicyGoal HomeObjective;
    }

    public struct SoldierDefinition
    {
        public uint Id, FactionId, ArmyId;
        public UnitKind Kind;
        public bool Alive;
        public SimPoint Position;
        public int Hp;
    }

    public static class WeekOneScenario
    {
        /// <summary>Same explicit values as TestData/week1-10v10.json. No JSON loading.</summary>
        public static ScenarioDefinition Create()
        {
            var s = new ScenarioDefinition
            {
                UnitParameters = new[]
                {
                    new UnitParameters { Kind = UnitKind.Infantry, Hp = 100, Speed = Fix64.FromInt(2),
                        Vision = Fix64.FromInt(20), Range = Fix64.FromInt(2), Damage = 10, AttackIntervalTicks = 20 },
                    new UnitParameters { Kind = UnitKind.Scout, Hp = 40, Speed = Fix64.FromInt(4),
                        Vision = Fix64.FromInt(32), Range = Fix64.FromInt(2), Damage = 2, AttackIntervalTicks = 20 }
                },
                Factions = new FactionDefinition[2], Cores = new CoreDefinition[2],
                Armies = new ArmyDefinition[8], Soldiers = new SoldierDefinition[20],
                Outposts = new[]
                {
                    new OutpostDefinition { Id = 1, Position = Point(128, 96) },
                    new OutpostDefinition { Id = 2, Position = Point(128, 32) }
                }
            };
            string[] roles = { "north", "south", "reserve", "scout" };
            int[] capacities = { 16, 16, 6, 2 };
            for (uint f = 1; f <= 2; f++)
            {
                uint firstArmy = (f - 1) * 4 + 1;
                s.Factions[f - 1] = new FactionDefinition { Id = f, CoreId = f,
                    ArmyIds = new[] { firstArmy, firstArmy + 1, firstArmy + 2, firstArmy + 3 } };
                s.Cores[f - 1] = new CoreDefinition { Id = f, FactionId = f,
                    Position = Point(f == 1 ? 24 : 232, 64), Hp = 3000 };
                for (int a = 0; a < 4; a++)
                    s.Armies[firstArmy - 1 + a] = new ArmyDefinition { Id = firstArmy + (uint)a,
                        FactionId = f, Role = roles[a], Capacity = capacities[a],
                        HomeObjective = new PolicyGoal(GoalKind.Core, f, default) };
                for (int i = 0; i < 10; i++)
                {
                    int a = i < 8 ? i / 4 : i - 6;
                    int x = i < 8 ? 24 + i % 4 : i == 8 ? 24 : 28;
                    uint id = (f - 1) * 10 + (uint)i + 1;
                    s.Soldiers[id - 1] = new SoldierDefinition { Id = id, FactionId = f,
                        ArmyId = firstArmy + (uint)a, Kind = i == 9 ? UnitKind.Scout : UnitKind.Infantry,
                        Alive = true, Position = Point(f == 1 ? x : 256 - x, i < 4 ? 96 : i < 8 ? 32 : 64),
                        Hp = i == 9 ? 40 : 100 };
                }
            }
            return s;
        }

        private static SimPoint Point(int x, int z) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));
    }
}
