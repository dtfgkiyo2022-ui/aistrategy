using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    // Private to the simulation assembly: callers only receive copied Contracts snapshots.
    internal struct SoldierState
    {
        internal SoldierDefinition Initial;
        internal SimPoint Position, MoveGoal;
        internal int Hp;
        internal bool Alive, IsMoving, IsAttacking, IsRetreating;
        internal long NextAttackTick;
        internal long LastCombatTick;
        internal PursuitMemory Pursuit;
        internal byte TargetKind; // 0 = none, 1 = soldier, 2 = core, 3 = villager, 4 = building (Ver.3), 5 = belt cell (V3-5)
        internal uint TargetId;
        internal UnitParameters Parameters;
        internal Fix64 StepDistance;
        internal bool Joining, TacticalRoute;
        internal int[] LocalPath;
        internal int LocalCursor, JoinCursor;
        internal long SlotWaitSinceTick;
        internal bool SlotWaitTimedOut;
        internal SimPoint SlotWaitTarget;
        internal SimPoint LocalGoal;
        /// <summary>V3-5 (32 #8): what the barracks trained it as (archer, cavalry); 0 for everyone else. Display and training only.</summary>
        internal UnitKind Class;
        internal bool SiegePartsEnhanced;
        internal bool EliteKitEnhanced;
        /// <summary>V3-5 #22: transient conversion progress; never serialized into a scenario or replay state.</summary>
        internal int ConversionProgress;
        internal uint ConversionByFaction;
        internal long ConversionLastAttackTick;
        /// <summary>V3-13 #2: the observation contact currently retained by an autonomous cult monk.</summary>
        internal uint CultTargetContactId;
        /// <summary>LargeEconomy: each automatic scout keeps its own deterministic patrol chunk.</summary>
        internal bool ExploreHasGoal;
        internal SimPoint ExploreGoal;
        internal int ExploreGoalChunkX, ExploreGoalChunkZ;
        internal long ExploreRetargetTick;
    }

    internal struct ArmyState
    {
        internal ArmyDefinition Definition;
        internal ArmyDecisionMemory Decision;
        internal uint[] AutoStartIds;
        internal uint[] SoldierIds;
        internal int[] Path;
        internal int PathCursor, AutoStage;
        internal SimPoint PathGoal;
        internal bool HasPathGoal, PathImpossible;
        internal bool ExplorePatrolActive, ExplorePatrolAuto, ExploreHasGoal;
        internal int ExploreGoalChunkX, ExploreGoalChunkZ, ExploreAvoidChunkX, ExploreAvoidChunkZ;
        internal int ExploreFailedChunkX, ExploreFailedChunkZ, ExploreFailureCount;
        internal SimPoint ExploreGoal;
        internal long ExploreRetargetTick, ExploreAvoidUntilTick;
        internal PolicyKind Policy;
        internal PolicyGoal Goal;
        internal ulong CommandId, LogIndex;
        internal long AcceptedTick, ApplyTick;
    }

    internal struct OutpostState
    {
        internal OutpostDefinition Definition;
        internal uint OwnerFactionId, CapturingFaction;
        internal int CaptureTicks;
        internal long NextReinforcementTick;
    }

    internal struct CoreState
    {
        internal CoreDefinition Definition;
        internal int Hp;
        internal long NextReinforcementTick;
        /// <summary>V3 core defence: ticks remaining before the next core volley.</summary>
        internal int DefenceTimer;
    }

    internal struct FactionState
    {
        internal uint Id, CoreId;
        internal uint[] ArmyIds;
        internal int AliveCount;
        internal uint NextContactId;
        internal uint NextArmyContactId;
        internal ArmyContactMemory[] ArmyContacts;
        internal uint[] ContactIds; // soldier ID - 1 -> local contact ID; never exported
        internal SimPoint[] ContactPositions;
        internal long[] ContactLastSeenTicks;
        internal bool[] ContactAbsent;
        /// <summary>V3-13 #4: one record per observed enemy soldier for cult choice scoring.</summary>
        internal uint[] CultObservedContactIds;
        internal UnitKind[] CultObservedKinds;
        internal long[] CultObservedLastSeenTicks;
        internal long[] CultObservedValidUntilTicks;
        internal bool[] VisibleCells, ExploredCells;
        internal int ExploredCellCount;
        internal int[] ExploredChunkCounts;
        // Visibility is maintained as source counts so a moving or removed source can subtract only its old cells.
        internal int[] VisibleCounts;
        /// <summary>Resource amounts last seen by this faction. Allocated only while ResourceFog is enabled.</summary>
        internal bool[] ResourceSeen;
        internal int[] ResourceRemaining;
        internal ObjectiveMemory[] Objectives;
        internal bool[] RegionHuman;
        internal EconomyPolicy[] RegionEconomyPolicies;
    }

    internal struct ArmyContactMemory
    {
        internal uint Id;
        internal SimPoint Position;
        internal long LastSeenTick;
        internal int Min, Max;
        internal bool Visible, Absent;
        internal uint[] Covered;
    }

    internal struct ObjectiveMemory
    {
        internal bool OwnerKnown, HpKnown;
        internal uint OwnerFactionId;
        internal int Hp;
        internal long LastSeenTick;
    }

    /// <summary>Ver.3 (technical-design-v3 3.2). Villagers are a separate array: soldier code indexes armies by
    /// ArmyId - 1, and a villager has no army (the sentry lesson in 5.5).</summary>
    internal struct VillagerState
    {
        internal uint Id, FactionId;
        internal bool Alive, IsMoving;
        internal int Hp;
        internal SimPoint Position, MoveGoal;
        internal VillagerTask Task;
        internal uint NodeId;
        internal ResourceKind CarryKind;
        internal int Carry;
        internal long NextGatherTick;
        internal int[] Route;
        internal int RouteCursor;
        internal SimPoint RouteGoal;
        internal uint BuildingId;
        /// <summary>V3-2 carrying by hand (12.3): the mine or smelter it takes from (0 = not carrying), and the smelter
        /// it takes ore to (0 = the core).</summary>
        internal uint HaulFrom, HaulTo;
        /// <summary>V3-6: a charcoal-kiln worker's selected wood point.</summary>
        internal uint HaulNodeId;
        /// <summary>V3-3 (19): the player assigned this villager; the automatic economy leaves it alone.</summary>
        internal bool Held;
        /// <summary>V3-9 #1: route state kept apart from the ordinary one-kind Carry/CarryKind load.</summary>
        internal uint CaravanMarketId, CaravanseraiId, CaravanOutpostId;
        /// <summary>0 none, 1 initial market approach, 2 outbound to the host, 3 loaded return to market.</summary>
        internal byte CaravanStage;
        internal int CaravanWood, CaravanGems;
        /// <summary>V3-9 #2: a danger stop is autonomous and distinct from losing the registered Outpost.</summary>
        internal bool CaravanDangerStopped;
        internal int CaravanSafeTicks;
    }

    /// <summary>ToPickup and ToDeliver are V3-2 carrying by hand; a load for the core goes by ToDropOff.</summary>
    internal enum VillagerTask : byte { Idle = 0, ToNode = 1, Gathering = 2, ToDropOff = 3, ToBuild = 4, Building = 5, ToPickup = 6, ToDeliver = 7, ToTradeMarket = 8, ToTradeCore = 9,
        ToCaravanMarket = 10, ToCaravanserai = 11, ToCaravanCore = 12 }

    internal struct BuildingState
    {
        internal uint Id, FactionId;
        internal BuildingKind Kind;
        /// <summary>Lower-left cell of the square footprint; its cells are impassable from placement on.</summary>
        internal int OriginCell;
        /// <summary>V3-11 #1: the exact river cells occupied by a bridge; null for every other building.</summary>
        internal int[] BridgeCells;
        /// <summary>Nearest passable cell outside the footprint, where builders stand and new soldiers appear.</summary>
        internal int WorkCell;
        internal bool Alive, Complete;
        /// <summary>BuildingReservations: a legal human placement waiting for its first payment.</summary>
        internal bool Reserved;
        internal int Hp, Progress, Queued;
        /// <summary>V3-11 #2: tick at which an automatic bridge was destroyed; 0 means it has not been destroyed.</summary>
        internal long DestroyedTick;
        internal long TrainRemaining;
        /// <summary>V3-2 mine and smelter (12.2): output side, items at the input and output, and the work clock
        /// (mine: ticks towards the next ore; smelter: ticks left on the metal being made, 0 when idle).</summary>
        internal Facing Facing;
        internal int Input, InputSecondary, InputTertiary, Output, Timer;
        /// <summary>CraftTree2: selected assembler recipe and the kind held in its second slot.</summary>
        internal RecipeKind Recipe;
        internal ResourceKind InputSecondaryKind;
        internal bool BrickUpgraded;
        /// <summary>CraftTree1: level of a processing building. Level one is the default.</summary>
        internal byte Level;
        /// <summary>CraftTree1: this building consumed one frames stock for its discounted construction.</summary>
        internal bool FramesUsed;
        /// <summary>V3-4 (26): metal paid for the infantry in the queue, so a cancel never returns metal that was not paid.</summary>
        internal int QueuedMetal;
        /// <summary>V3-6: steel paid by heavy infantry still in a queue.</summary>
        internal int QueuedSteel;
        /// <summary>V3-7: bow gear paid by a skirmish archer still in a queue.</summary>
        internal int QueuedBowGear;
        internal int QueuedGems;
        internal int QueuedGold;
        /// <summary>V3-4 farm: ticks per food, fixed when it is placed (27).</summary>
        internal int Interval;
        /// <summary>V3-5 (32): the kind of each queued unit, front first; null without ages (then every entry is infantry).</summary>
        internal UnitKind[] QueueKinds;
        internal bool[] QueueRations;
        internal bool[] QueuePlanks;
        internal bool[] QueueCharms;
        internal bool[] QueueTools;
        internal bool[] QueueArmsKit, QueueFineArmsKit, QueueSiegeParts, QueueEliteKit, QueueSiegeEngine;
        internal int QueuedSiegeEngine;
        /// <summary>V3-5 tower: shots fired so far.</summary>
        internal int Shots;
        /// <summary>V3-5 blacksmith: the tech being researched (0 when none); its clock is TrainRemaining.</summary>
        internal TechKind Researching;
        /// <summary>V3-15 #1: a mine shaft alternates stone and ore; false means stone is next.</summary>
        internal bool MountainOreNext;
        /// <summary>V3-2 mine: the ore point under its footprint.</summary>
        internal uint NodeId;
        /// <summary>V3-3 (19): placed or operated by the player; the automatic economy leaves it alone.</summary>
        internal bool Held;
        /// <summary>V3-9 #1: fixed at placement; never reselected when another market or Outpost appears.</summary>
        internal uint CaravanOutpostId, CaravanMarketId;
        internal Fix64 CaravanDistance;
        internal int CaravanWoodReward;
        /// <summary>V3-18 #1: the shrine's fixed outpost link; losing the outpost stops the effect without removing it.</summary>
        internal uint SanctuaryOutpostId;
        /// <summary>V3-20: a storage keeps one resource kind at a time so mixed items never lose their type.</summary>
        internal ResourceKind StorageKind;
        internal int StorageCount;
        /// <summary>EasyRebuild: the exact placement materials paid for this building.</summary>
        internal int ConstructionWood, ConstructionStone, ConstructionPlanks, ConstructionBricks, ConstructionSiegeParts, ConstructionFrames;
        /// <summary>EasyRebuild: the last tick covered by the post-move pause.</summary>
        internal long MovePauseUntilTick;
    }

    internal struct ResourceNodeState
    {
        internal ResourceNodeDefinition Definition;
        internal int Remaining;
        internal bool Fishing;
    }

    /// <summary>V3-2 (technical-design-v3 11.2): one belt cell. FactionId 0 means no belt on the cell.</summary>
    internal struct BeltState
    {
        internal uint FactionId;
        internal Facing Facing;
        internal int Hp;
        /// <summary>0 when empty. At most one item per cell.</summary>
        internal ResourceKind Item;
        /// <summary>Ticks since the item entered this cell.</summary>
        internal int Progress;
        /// <summary>V3-3 (19): laid by the player; the automatic line routes around it.</summary>
        internal bool Held;
        internal BeltComponentKind Component;
        internal ResourceKind SorterKind;
        internal int PairCell;
        internal bool SplitterRightNext, Fast;
    }

    /// <summary>V3-6: one automatic processing chain. The belt arrays are the canonical route chosen for this line.</summary>
    internal struct ProcessingLineState
    {
        internal uint Id, FactionId;
        internal ProcessingLineKind Kind;
        internal LineManager Manager;
        internal bool RequestPending;
        /// <summary>Tick at or after which an enemy-damaged automatic line may be rebuilt. 0 means no wait.</summary>
        internal long RebuildAvailableTick;
        internal int RequestedCell;
        internal uint RequestedRegionId;
        internal uint MineId, SmelterId, KilnId, SteelworksId, LumberCampId, FletcherId, MillId, QuarryId, BrickKilnId, SaddleryId, SawmillId, ScriptoriumId, DryingRackId, LapidaryId, WeaverId, IncenseHouseId, CharmWorkshopId, RepairYardId, ToolShopId, StonecutterId, JoineryId, AssemblerId;
        internal int[] BeltCells;
        internal Facing[] BeltFacings;
    }

    internal struct FactionEconomy
    {
        internal int Food, Wood;
        /// <summary>V3-2 stock; always 0 without industry. Stone, Gems and Gold (V3-5) only with ages; Charcoal and Steel (V3-6) only with the processing chain.</summary>
        internal int Ore, Metal, Stone, Gems, Gold, Charcoal, Steel, BowGear, Rations, Bricks, Tack, Planks, Books, Incense, Charms, RepairKits, Tools, CutStone, Frames, ArmsKit, Scholarly, FineArmsKit, SiegeParts, Mechanism, SiegeEngine, EliteKit;
        /// <summary>Rations-on diagnostics: production, consumption, rationed soldier training, and healed HP.</summary>
        internal int RationsProduced, RationsConsumed, RationsTrainedSoldiers, RationsHealedHp;
        internal int BricksProduced, BricksConsumed, BricksUpgradedWalls, BricksUpgradedTowers, BricksCoreRepairHp;
        internal int BricksConsumedWalls, BricksConsumedTowers, BricksConsumedCoreRepair, BricksConsumedCastles;
        internal int TackProduced, TackConsumed, TackTrainedHeavyCavalry;
        internal int PlanksProduced, PlanksConsumed, PlanksConsumedRams, PlanksConsumedBridges, PlanksEnhancedRams;
        internal int CutStoneProduced, CutStoneConsumed, FramesProduced, FramesConsumed, FramesConsumedBuildings, FramesConsumedUpgrades;
        internal int BooksProduced, BooksConsumed, BooksUsedResearch;
        internal int ArmsKitProduced, ArmsKitConsumed, FineArmsKitProduced, FineArmsKitConsumed, ScholarlyProduced, ScholarlyConsumed, ScholarlyUsedResearch;
        internal int SiegePartsProduced, SiegePartsConsumed, SiegePartsEnhancedRams, MechanismProduced, MechanismConsumed, MechanismUsedResearch;
        internal int SiegeEngineProduced, SiegeEngineConsumed, EliteKitProduced, EliteKitConsumed, CannonProduced;
        internal int PreservesProduced, PreservesConvertedFood;
        internal int JewelsProduced, JewelsConsumed, JewelsTrainedMercenaries;
        internal int TradeGoodsProduced, TradeGoodsConvertedFood, TradeGoodsConvertedStone;
        internal int IncenseProduced, IncenseConsumed, IncenseHealedHp;
        internal int CharmsProduced, CharmsConsumed, CharmsTrainedSoldiers;
        internal int RepairKitsProduced, RepairKitsConsumed, RepairKitsRepairedHp, RepairKitsCoreRepairedHp;
        internal int ToolsProduced, ToolsConsumed, ToolsTrainedVillagers, ToolsSavedFood;
        /// <summary>Goods-market diagnostics, indexed by ResourceKind. Allocated only when the feature is enabled.</summary>
        internal int[] GoodsSoldCounts, GoodsSoldUnits, GoodsReceivedFood, GoodsReceivedWood;
        /// <summary>Villagers paid for and waiting at the core; the first one trains for TrainRemaining more ticks.</summary>
        internal int Queued;
        internal long TrainRemaining;
        /// <summary>The player turned the automatic economy off (it is on by default).</summary>
        internal bool AutoOff;
        /// <summary>V3-3 (19): the player trained or cancelled at the core; the automatic economy trains no villagers.</summary>
        internal bool CoreHeld;
        internal bool[] QueueTools;
        /// <summary>V3-3 (20): what the automatic economy aims for. Balanced without industry.</summary>
        internal EconomyPolicy Policy;
        /// <summary>V3-4 (26): the civilisation, and while advancing the one chosen and the ticks left.</summary>
        internal CivKind Civ, AdvancingTo;
        /// <summary>V3-5 (32 #7): 0 primitive, 1 on taking a civilisation, 2 after the second age.</summary>
        internal byte Age;
        internal long AdvanceRemaining;
        /// <summary>Consecutive ticks at age 3 with a living core, for the optional age victory.</summary>
        internal int AgeVictoryProgress;
        /// <summary>V3-5 (32 #6): researched techs, bit 1 &lt;&lt; (TechKind - 1).</summary>
        internal ulong Techs;
        /// <summary>
        /// The civilisation the player asked for in the primitive age before advancing was possible; Primitive when none.
        /// Advancing starts with it on the first tick it can, and starting any advance clears it.
        /// </summary>
        internal CivKind ReservedCiv;
        /// <summary>AgeClockUx: the manual civilisation-choice deadline after the age clock fires.</summary>
        internal long CivChoiceDeadlineTick;
    }

    internal struct RegionState
    {
        internal uint Id, CenterId;
        internal RegionCenterKind CenterKind;
        internal SimPoint Center;
        internal bool HasCenter;
    }

    internal sealed class WorldState
    {
        internal readonly ScenarioDefinition Config;
        internal SoldierState[] Soldiers;
        internal ArmyState[] Armies;
        internal readonly CoreState[] Cores;
        internal readonly OutpostState[] Outposts;
        internal readonly GridMap Map;
        internal readonly FactionState[] Factions;
        internal int[] SoldierTraversal;
        internal int[] ArmyTraversal;
        internal readonly SplitMix64 CombatRandom, AiRandom;
        internal uint NextSoldierId;
        internal int SoldierCount => checked((int)(NextSoldierId - 1));
        internal uint NextArmyId;
        internal readonly uint NextCoreId, NextOutpostId, NextFactionId;
        internal long Tick;
        internal ulong InputCursor;
        internal ResourceNodeState[] Nodes;
        /// <summary>Fishing resource IDs, sorted by node ID and fixed at match start.</summary>
        internal uint[] FishingNodeIds = Array.Empty<uint>();
        internal VillagerState[] Villagers;
        internal uint NextVillagerId;
        internal int VillagerCount => checked((int)(NextVillagerId - 1));
        internal FactionEconomy[] Economies;
        internal Fix64 VillagerStep;
        internal BuildingState[] Buildings = Array.Empty<BuildingState>();
        internal uint NextBuildingId = 1;
        internal int BuildingCount => checked((int)(NextBuildingId - 1));
        internal ProcessingLineState[] ProcessingLines = Array.Empty<ProcessingLineState>();
        internal uint NextProcessingLineId = 1;
        /// <summary>V3-2: one slot per map cell, empty without industry. Walked in cell order, so no id is needed.</summary>
        internal BeltState[] Belts = Array.Empty<BeltState>();
        /// <summary>Processing order of the belts (11.3). A cache: rebuilt from Belts alone, never hashed.</summary>
        internal int[] BeltOrder;
        internal MatchResult Result;
        /// <summary>S-5: geographic regions are derived from the map and current town centres.</summary>
        internal RegionState[] Regions = Array.Empty<RegionState>();
        internal uint[] CellRegions = Array.Empty<uint>();

        internal WorldState(ScenarioDefinition source)
        {
            Config = CopyAndValidate(source);
            Map = new GridMap(Config.Map);
            Outposts = new OutpostState[Config.Outposts.Length];
            for (int i = 0; i < Outposts.Length; i++)
                Outposts[i] = new OutpostState { Definition = Config.Outposts[i], OwnerFactionId = Config.Outposts[i].OwnerFactionId,
                    NextReinforcementTick = Config.Outposts[i].OwnerFactionId == 0 ? 0 : Config.Rules.OutpostReinforcementIntervalTicks };
            CombatRandom = new SplitMix64(Config.Seed);
            AiRandom = new SplitMix64(Config.Seed);
            Soldiers = new SoldierState[Config.Soldiers.Length];
            Armies = new ArmyState[Config.Armies.Length];
            Cores = new CoreState[Config.Cores.Length];
            Factions = new FactionState[2];
            NextSoldierId = checked((uint)Soldiers.Length + 1);
            NextArmyId = checked((uint)Armies.Length + 1);
            NextCoreId = checked((uint)Cores.Length + 1);
            NextOutpostId = checked((uint)Config.Outposts.Length + 1);
            NextFactionId = 3;
            for (int i = 0; i < Cores.Length; i++)
                Cores[i] = new CoreState { Definition = Config.Cores[i], Hp = Config.Cores[i].Hp, NextReinforcementTick = Config.Rules.CoreReinforcementIntervalTicks };
            for (int i = 0; i < Soldiers.Length; i++)
            {
                var d = Config.Soldiers[i];
                var p = Array.Find(Config.UnitParameters, value => value.Kind == d.Kind);
                Soldiers[i] = new SoldierState { Initial = d, Position = d.Position, MoveGoal = d.Position,
                    Alive = d.Alive, Hp = d.Hp, Parameters = p, StepDistance = Fix64.FromRaw(p.Speed.Raw / 20) };
            }
            var soldiers = new System.Collections.Generic.List<int>();
            for (int f = 0; f < 2; f++)
            {
                var d = Config.Factions[f];
                int cellCount = checked(Config.Map.WidthCells * Config.Map.HeightCells);
                int exploreChunkCells = Math.Max(1, Config.Economy.ExploreChunkCells);
                int exploreChunkCount = (Config.Map.WidthCells + exploreChunkCells - 1) / exploreChunkCells
                    * ((Config.Map.HeightCells + exploreChunkCells - 1) / exploreChunkCells);
                Factions[f] = new FactionState { Id = d.Id, CoreId = d.CoreId, ArmyIds = d.ArmyIds,
                    ContactIds = new uint[Soldiers.Length], ContactPositions = new SimPoint[Soldiers.Length],
                    ContactLastSeenTicks = new long[Soldiers.Length], ContactAbsent = new bool[Soldiers.Length],
                    CultObservedContactIds = new uint[Soldiers.Length], CultObservedKinds = new UnitKind[Soldiers.Length],
                    CultObservedLastSeenTicks = new long[Soldiers.Length], CultObservedValidUntilTicks = new long[Soldiers.Length],
                    VisibleCells = new bool[cellCount], ExploredCells = new bool[cellCount], VisibleCounts = new int[cellCount],
                    ExploredChunkCounts = new int[exploreChunkCount],
                    ResourceSeen = Config.Economy.ResourceFog ? new bool[Config.ResourceNodes.Length] : null,
                    ResourceRemaining = Config.Economy.ResourceFog ? new int[Config.ResourceNodes.Length] : null,
                    Objectives = new ObjectiveMemory[Cores.Length + Outposts.Length], NextContactId = 1, NextArmyContactId = 1, ArmyContacts = new ArmyContactMemory[Armies.Length] };
                foreach (uint id in d.ArmyIds)
                {
                    var ids = new System.Collections.Generic.List<uint>();
                    for (int i = 0; i < Soldiers.Length; i++)
                        if (Soldiers[i].Initial.ArmyId == id) { ids.Add((uint)i + 1); soldiers.Add(i); }
                    Armies[id - 1] = new ArmyState { Definition = Config.Armies[id - 1], SoldierIds = ids.ToArray(), AutoStartIds = ids.FindAll(id => Soldiers[id - 1].Alive).ToArray(), Path = Array.Empty<int>() };
                }
            }
            SoldierTraversal = soldiers.ToArray();
            ArmyTraversal = new int[Armies.Length];
            for (int i = 0; i < ArmyTraversal.Length; i++) ArmyTraversal[i] = i;
            var e = Config.Economy;
            Nodes = new ResourceNodeState[Config.ResourceNodes.Length];
            for (int i = 0; i < Nodes.Length; i++) Nodes[i] = new ResourceNodeState { Definition = Config.ResourceNodes[i], Remaining = Config.ResourceNodes[i].Amount };
            if (e.FishingEnabled && e.Ages)
            {
                var fishing = new System.Collections.Generic.List<uint>();
                for (int i = 0; i < Nodes.Length; i++)
                {
                    if (Nodes[i].Definition.Kind != ResourceKind.Food || !NearRiver(Nodes[i].Definition.Position, e.FishReach)) continue;
                    Nodes[i].Fishing = true;
                    fishing.Add(Nodes[i].Definition.Id);
                }
                FishingNodeIds = fishing.ToArray();
            }
            Villagers = new VillagerState[Config.Villagers.Length];
            for (int i = 0; i < Villagers.Length; i++)
            {
                var v = Config.Villagers[i];
                Villagers[i] = new VillagerState { Id = v.Id, FactionId = v.FactionId, Alive = true, Hp = e.VillagerHp,
                    Position = v.Position, MoveGoal = v.Position, Route = Array.Empty<int>() };
            }
            NextVillagerId = checked((uint)Villagers.Length + 1);
            Economies = new FactionEconomy[2];
            bool goodsMarket = e.GoodsMarket && e.Enabled && e.Industry && e.Ages;
        for (int f = 0; f < 2; f++) Economies[f] = new FactionEconomy { Food = e.StartFood, Wood = e.StartWood, Stone = e.Ages ? e.StartStone : 0, Metal = e.Ages ? e.StartMetal : 0, Gems = 0, Gold = 0, Charcoal = 0, Steel = 0, BowGear = 0, Rations = 0, Bricks = 0, Tack = 0, Planks = 0, Books = 0, Incense = 0, Charms = 0, RepairKits = 0, Tools = 0, CutStone = 0, Frames = 0, ArmsKit = 0, Scholarly = 0, FineArmsKit = 0, SiegeParts = 0, Mechanism = 0, SiegeEngine = 0, EliteKit = 0,
                GoodsSoldCounts = goodsMarket ? new int[31] : null, GoodsSoldUnits = goodsMarket ? new int[31] : null,
                GoodsReceivedFood = goodsMarket ? new int[31] : null, GoodsReceivedWood = goodsMarket ? new int[31] : null };
            VillagerStep = Fix64.FromRaw(e.VillagerSpeed.Raw / 20);
            if (e.Industry)
            {
                Belts = new BeltState[checked(Config.Map.WidthCells * Config.Map.HeightCells)];
                foreach (var b in Config.Belts)
                    Belts[b.Cell] = new BeltState { FactionId = b.FactionId, Facing = b.Facing, Hp = e.BeltHp, Item = b.Item,
                        Component = e.BeltComponents ? b.Component : BeltComponentKind.None,
                        SorterKind = e.BeltComponents ? b.SorterKind : (ResourceKind)0,
                        PairCell = e.BeltComponents && b.Component != BeltComponentKind.None ? b.PairCell : -1,
                        Fast = e.BeltComponents && b.Speed == BeltSpeed.Fast };
            }
        }

        private static ScenarioDefinition CopyAndValidate(ScenarioDefinition s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            Require(s.Map != null && s.Rules != null && s.ScenarioId != null, "Missing configuration.");
            Require(s.SchemaVersion == 1 && s.TickRateHz == 20 && s.VerificationTickLimit > 0, "Unsupported scenario version or tick rate.");
            var m = s.Map;
            Require(m.WidthMeters > 0 && m.WidthMeters <= 1024 && m.HeightMeters > 0 && m.HeightMeters <= 1024
                && m.CellSizeMeters > 0 && m.WidthCells > 0 && m.HeightCells > 0
                && (long)m.WidthCells * m.CellSizeMeters == m.WidthMeters
                && (long)m.HeightCells * m.CellSizeMeters == m.HeightMeters, "Invalid map dimensions.");
            // mapgen-4 is 256x128 cells. Keep a finite bound for malformed authored maps, but allow the S-2 grid.
            Require(m.BlockedCellIds != null && (long)m.WidthCells * m.HeightCells <= 65536, "Invalid grid.");
            var blocked = Copy(m.BlockedCellIds); Array.Sort(blocked);
            for (int i = 0; i < blocked.Length; i++)
                Require(blocked[i] >= 0 && blocked[i] < m.WidthCells * m.HeightCells && (i == 0 || blocked[i] != blocked[i - 1]), "Invalid blocked cell.");
            var r = s.Rules;
            int factionCap = s.Economy != null && s.Economy.ArmyGrowth && !s.Economy.MeasurementScale ? 200 : r.FactionCap;
            Require(factionCap > 0 && r.CoreRadius.Raw >= 0 && r.CoreRadius <= Fix64.FromInt(1024)
                && r.OwnedObjectiveVision.Raw >= 0 && r.CaptureRadius.Raw >= 0
                && r.CaptureDurationTicks > 0 && r.CoreReinforcementIntervalTicks > 0
                && r.OutpostReinforcementIntervalTicks > 0 && r.OccupationThreatMemoryTicks >= 0
                && r.DefaultReservePermille <= 1000, "Invalid rules.");
            var c = new ScenarioDefinition { SchemaVersion = s.SchemaVersion, ScenarioId = s.ScenarioId, Seed = s.Seed,
                TickRateHz = s.TickRateHz, VerificationTickLimit = s.VerificationTickLimit,
                Map = new MapDefinition { WidthMeters = m.WidthMeters, HeightMeters = m.HeightMeters,
                    CellSizeMeters = m.CellSizeMeters, WidthCells = m.WidthCells, HeightCells = m.HeightCells, DefaultPassable = m.DefaultPassable, BlockedCellIds = blocked,
                    Terrain = Copy(m.Terrain) },
                Rules = new RuleDefinition { FactionCap = factionCap, CoreRadius = r.CoreRadius,
                    OwnedObjectiveVision = r.OwnedObjectiveVision, CaptureRadius = r.CaptureRadius,
                    CaptureDurationTicks = r.CaptureDurationTicks, CoreReinforcementIntervalTicks = r.CoreReinforcementIntervalTicks,
                    OutpostReinforcementIntervalTicks = r.OutpostReinforcementIntervalTicks,
                    OccupationThreatMemoryTicks = r.OccupationThreatMemoryTicks,
                    DefaultReservePermille = r.DefaultReservePermille },
                UnitParameters = Copy(s.UnitParameters), Factions = Copy(s.Factions), Cores = Copy(s.Cores),
                Outposts = Copy(s.Outposts), Armies = Copy(s.Armies), Soldiers = Copy(s.Soldiers),
                ResourceNodes = Copy(s.ResourceNodes), Economy = CopyEconomy(s.Economy), Villagers = Copy(s.Villagers), Belts = Copy(s.Belts),
            Extensions = Copy(s.Extensions) };
            var e = c.Economy;
            // Army growth deliberately has a fixed population ceiling for this experiment.  The authored value is
            // ignored while the flag is on, and the ordinary scenario value remains untouched while it is off.
            if (e.ArmyGrowth && !e.MeasurementScale && !e.LargeEconomy)
            {
                e.PopulationCap = 200;
                c.Rules.FactionCap = 200;
            }
            if (e.LargeEconomy)
            {
                c.Rules.FactionCap = e.LargeFactionCap;
                // LargeEconomy owns the map-sized cap. Keep the legacy command/frame field synchronized even when
                // an authored or decoded scenario did not also update extension 38.
                e.DropSiteMax = e.LargeDropSiteMax;
            }
            // Monk rules are opt-in. Keep disabled scenarios byte-for-byte unchanged, but make an enabled authored
            // scenario usable even when an older scenario file has no Monk parameter record yet.
            if (e.MonksEnabled && !Array.Exists(c.UnitParameters, value => value.Kind == UnitKind.Monk))
            {
                Array.Resize(ref c.UnitParameters, c.UnitParameters.Length + 1);
                c.UnitParameters[c.UnitParameters.Length - 1] = new UnitParameters { Kind = UnitKind.Monk, Hp = 100,
                    Speed = Fix64.FromInt(2), Vision = Fix64.FromInt(20), Range = Fix64.FromInt(4), Damage = 0, AttackIntervalTicks = 20 };
            }
            if (e.CraftTree4 && !Array.Exists(c.UnitParameters, value => value.Kind == UnitKind.Cannon))
            {
                Array.Resize(ref c.UnitParameters, c.UnitParameters.Length + 1);
                c.UnitParameters[c.UnitParameters.Length - 1] = new UnitParameters { Kind = UnitKind.Cannon, Hp = e.CannonHp,
                    Speed = e.CannonSpeed, Vision = e.CannonVision, Range = e.CannonRange, Damage = e.CannonDamage, AttackIntervalTicks = e.CannonInterval };
            }
            Require(!e.EconomyScale || e.Enabled, "Economy scale requires an enabled economy.");
            Require(!e.AiArmyRecovery || (e.Enabled && e.Ages), "AI army recovery requires ages and an economy.");
            Require(!e.LineBuildingCheck || (e.Enabled && e.Industry), "Line building checks require industry and an economy.");
            Require(e.CivVarietyWindowPermille >= 0 && e.CivVarietyWindowPermille <= 1000,
                "Invalid civ variety window.");
            Require(!e.CivVariety || (e.Enabled && e.Ages), "Civ variety requires ages and an economy.");
            Require(!e.CivVarietyCaps || (e.Enabled && e.Ages && e.CivVarietyCapMetallurgy > 0 && e.CivVarietyCapAgrarian > 0),
                "Civ variety caps require ages, an economy, and positive caps.");
            Require(e.LineRebuildDelayTicks >= 0 && e.RaidLinePriority >= 0, "Invalid line-raid rules.");
            if (e.Enabled)
                Require(e.StartFood >= 0 && e.StartWood >= 0 && e.PopulationCap > 0 && e.VillagerHp > 0
                    && e.VillagerSpeed.Raw > 0 && e.VillagerSpeed <= Fix64.FromInt(16) && e.CarryCapacity > 0 && e.GatherIntervalTicks > 0
                    && e.VillagerFoodCost >= 0 && e.VillagerTrainTicks > 0 && e.QueueLimit > 0 && e.AutoVillagerTarget >= 0
                    && e.DropOffMargin.Raw >= 0 && e.DropOffMargin <= Fix64.FromInt(1024)
                    && e.BarracksSizeCells > 0 && e.BarracksSizeCells <= 8 && e.BarracksWoodCost >= 0 && e.BarracksWork > 0 && e.BarracksHp > 0
                     && e.Builders > 0 && e.InfantryFoodCost >= 0 && e.InfantryWoodCost >= 0 && e.InfantryTrainTicks > 0 && e.AutoInfantryQueue >= 0,
                     "Invalid economy rules.");
            else Require(c.Villagers.Length == 0 && !e.Industry, "Villagers and industry need an enabled economy.");
            Require(!e.LargeEconomy || (e.Enabled && e.LargeVillagerTrainTicks > 0 && e.LargeInfantryTrainTicks > 0
                && e.LargeVillagerBatch > 0 && e.LargeInfantryBatch > 0 && e.LargeBarracksMaxBuildings > 0
                && e.LargeArmyCapacity > 0 && e.LargeTownDecisionIntervalTicks > 0 && e.LargeTownMinAge >= 0
                && e.LargeFactionCap > 0 && e.LargeResourceAmountPermille > 0 && e.LargeFoodAmountPermille > 0
                && e.LargeOreAmountPermille > 0 && e.LargeStoneAmountPermille > 0 && e.LargeGoldAmountPermille > 0
                 && e.LargeFoodPerWood > 0 && e.LargeConstructionReserveWood >= 0 && e.LargeTownStoneGatherers > 0
                 && e.LargeTownVillagersPerTown > 0 && e.LargeBarracksVillagersPerBuilding > 0 && e.LargeTownAutoWorkers > 0
                  && e.LargeTargetRoundTripTicks > 0 && e.LargeResourceClusterRadiusMeters > 0
                  && e.LargeClusterDecisionIntervalTicks > 0 && e.LargeResourceClusterMaxDropSites > 0
                  && e.LargeDropSiteMax > 0
                  && e.LargeClusterMinGatherers > 0 && e.LargeDropSiteMinSpacingMeters > 0
                  && e.LargeClusterSwitchPermille > 0 && e.LargeClusterSwitchPermille <= 1000
                  && e.LargeVillagersPerFarm > 0 && e.LargeFarmMinimum > 0 && e.LargeFarmMaximum >= e.LargeFarmMinimum
                  && e.LargeInfantryResourceReserveUnits > 0
                  && e.LargeExploreTargetPermille > 0 && e.LargeExploreTargetPermille <= 1000
                  && e.LargeExploreScoutCount > 0 && e.LargeExploreChunkCells > 0 && e.LargeExploreChunkCells <= 64),
                "Invalid large economy rules.");
            if (e.Industry)
                Require(e.BeltWoodCost >= 0 && e.BeltTicksPerCell > 0 && e.BeltTicksPerCell <= 1000 && e.BeltHp > 0
                    && e.BeltLimit > 0 && e.BeltLimit <= 8192
                    && e.MineSizeCells > 0 && e.MineSizeCells <= 8 && e.MineWoodCost >= 0 && e.MineWork > 0 && e.MineHp > 0 && e.MineIntervalTicks > 0
                    && e.SmelterSizeCells > 0 && e.SmelterSizeCells <= 8 && e.SmelterWoodCost >= 0 && e.SmelterWork > 0 && e.SmelterHp > 0
                    && e.SmeltTicks > 0 && e.OrePerMetal > 0 && e.BufferLimit > 0 && e.OrePerMetal <= e.BufferLimit && e.InfantryMetalCost >= 0,
                    "Invalid industry rules.");
            else Require(c.Belts.Length == 0 && e.InfantryMetalCost == 0 && !e.ProcessingChain, "Belts and metal costs need industry.");
            Require(!e.CommonDrill || (e.Enabled && e.Industry && e.DrillSizeCells > 0 && e.DrillSizeCells <= 8
                && e.DrillWoodCost >= 0 && e.DrillStoneCost >= 0 && e.DrillWork > 0 && e.DrillHp > 0 && e.DrillIntervalTicks > 0),
                "Common drill requires valid industry rules.");
            Require(!e.Inserters || (e.Enabled && e.Industry && e.BeltComponents && e.InserterWoodCost >= 0
                && e.InserterIntervalTicks > 0 && e.AiInserterCoreReserve >= 0),
                "Inserters require valid belt-component rules.");
            if (e.BeltComponents)
            {
                Require(e.Industry && e.SplitterWoodCost >= 0 && e.SorterWoodCost >= 0 && e.UndergroundBeltWoodCost >= 0
                    && e.UndergroundBeltMaxLength > 0 && e.UndergroundBeltMaxLength <= 64
                    && e.FastBeltWoodCost >= 0 && e.FastBeltTicksPerCell > 0
                    && e.FastBeltAge >= 1 && e.StorageSizeCells == 2 && e.StorageWoodCost >= 0 && e.StorageWork > 0
                    && e.StorageHp > 0 && e.StorageCapacity > 0, "Invalid belt-component rules.");
                foreach (var b in c.Belts)
                {
                    Require((byte)b.Component <= (byte)BeltComponentKind.Inserter && (byte)b.Speed <= (byte)BeltSpeed.Fast,
                        "Invalid belt component.");
                    Require(b.Component != BeltComponentKind.Inserter || e.Inserters, "Inserters are disabled.");
                    if (b.Component == BeltComponentKind.Sorter)
            Require((byte)b.SorterKind > 0 && (byte)b.SorterKind <= (byte)(e.Tools ? ResourceKind.Tools : e.Charms ? ResourceKind.Charms : e.Incense ? ResourceKind.Incense : e.Rations ? ResourceKind.Rations : ResourceKind.BowGear), "A sorter needs a resource kind.");
                    if (b.Component == BeltComponentKind.Inserter)
                        Require(b.SorterKind == 0 || (byte)b.SorterKind <= (byte)ResourceKind.Mechanism, "An inserter needs a valid selector.");
                    if (b.Component == BeltComponentKind.UndergroundEntrance || b.Component == BeltComponentKind.UndergroundExit)
                    {
                        Require(b.PairCell >= 0 && b.PairCell < c.Map.WidthCells * c.Map.HeightCells && b.PairCell != b.Cell,
                            "An underground belt needs an in-map pair.");
                        bool paired = false;
                        foreach (var other in c.Belts)
                            if (other.Cell == b.PairCell)
                            {
                                paired = other.FactionId == b.FactionId
                                    && other.PairCell == b.Cell
                                    && (b.Component == BeltComponentKind.UndergroundEntrance
                                        ? other.Component == BeltComponentKind.UndergroundExit
                                        : other.Component == BeltComponentKind.UndergroundEntrance);
                                break;
                            }
                        Require(paired, "An underground belt needs a reciprocal pair.");
                    }
                }
            }
            if (e.ProcessingChain)
                Require(e.Industry && e.CharcoalKilnSizeCells > 0 && e.CharcoalKilnSizeCells <= 8 && e.CharcoalKilnWoodCost >= 0
                    && e.CharcoalKilnWork > 0 && e.CharcoalKilnHp > 0 && e.CharcoalTicks > 0
                    && e.SteelworksSizeCells > 0 && e.SteelworksSizeCells <= 8 && e.SteelworksWoodCost >= 0
                    && e.SteelworksWork > 0 && e.SteelworksHp > 0 && e.SteelTicks > 0
                    && e.HeavyInfantryFoodCost >= 0 && e.HeavyInfantryWoodCost >= 0 && e.HeavyInfantrySteelCost >= 0
                    && e.HeavyInfantryTrainTicks > 0 && e.HeavyInfantryHp > 0 && e.HeavyInfantryDamage >= 0
                    && e.HeavyInfantryAttackIntervalTicks > 0 && e.HeavyInfantrySpeed.Raw > 0 && e.HeavyInfantrySpeed <= Fix64.FromInt(16)
                    && e.HeavyInfantryRange.Raw >= 0 && e.HeavyInfantryRange <= Fix64.FromInt(64)
                     && e.HeavyInfantryVision.Raw >= 0, "Invalid processing-chain rules.");
            Require(!e.Rations || (e.Enabled && e.Industry && e.Ages && e.MillSizeCells > 0 && e.MillSizeCells <= 8
                && e.MillWoodCost >= 0 && e.MillWork > 0 && e.MillHp > 0 && e.MillTicks > 0 && e.MillFoodInput > 0
                && e.MillFoodInput <= e.BufferLimit && e.RationsPerSoldier > 0 && e.RationsTrainPermille > 0 && e.RationsTrainPermille <= 1000
                && e.RationsHealIntervalTicks > 0 && e.RationsHealQuietTicks >= 0 && e.RationsHealHp > 0 && e.RationsHealMaxPerInterval > 0
                && e.RationsFarmMinimum >= 0 && e.RationsFoodReserve >= 0), "Invalid rations rules.");
            Require(!e.EasyRebuild || (e.Enabled && e.MoveBuildingPauseTicks > 0), "Invalid easy-rebuild rules.");
            Require(!e.AdjacentBuildings || (e.Enabled && e.AdjacentBuildingClearanceCells > 0 && e.AdjacentBuildingClearanceCells <= 2),
                "Invalid adjacent-building rules.");
            Require(!e.BuildingReservations || (e.Enabled && e.ReservationLimit > 0 && e.ReservationCheckTicks > 0),
                "Invalid building-reservation rules.");
            Require(!e.ResourceFog || e.Enabled, "Resource fog requires an enabled economy.");
            Require(!e.ExploreScouting || e.ExploreChunkCells > 0 && e.ExploreChunkCells <= 64
                && e.ExploreMinUnexploredPermille >= 0 && e.ExploreMinUnexploredPermille <= 1000
                && e.ExploreMinUnexploredPermilleStage2 >= 0 && e.ExploreMinUnexploredPermilleStage2 <= e.ExploreMinUnexploredPermille
                && e.ExploreMinUnexploredPermilleStage3 >= 0 && e.ExploreMinUnexploredPermilleStage3 <= e.ExploreMinUnexploredPermilleStage2
                && e.ExploreMinUnexploredPermilleStage4 >= 0 && e.ExploreMinUnexploredPermilleStage4 <= e.ExploreMinUnexploredPermilleStage3
                && e.ExploreRetargetTicks > 0 && e.ExploreAvoidChunks >= 0 && e.ExploreAvoidTicks >= 0
                && e.ExploreAutoUntilTick >= 0, "Invalid exploration rules.");
            Require(!e.DropSiteLimit || (e.Enabled && e.Ages && e.DropSiteMax > 0), "Invalid drop-site-limit rules.");
            Require(!e.AiBelts || (e.Enabled && e.Industry && e.Ages && e.AiBeltWoodReserve >= 0), "Invalid AI-belt rules.");
            Require(!e.Bricks || (e.Enabled && e.Industry && e.Ages && e.BrickKilnSizeCells > 0 && e.BrickKilnSizeCells <= 8
                && e.BrickKilnWoodCost >= 0 && e.BrickKilnWork > 0 && e.BrickKilnHp > 0 && e.BrickTicks > 0
                && e.BrickStoneInput > 0 && e.BrickCharcoalInput > 0 && e.BrickStoneInput <= e.BufferLimit
                && e.BrickCharcoalInput <= e.BufferLimit && e.BricksPerWall >= 0 && e.BricksPerTower >= 0 && e.BricksPerCastle >= 0
                && e.BrickHpPermille >= 1000 && e.BrickUpgradeIntervalTicks > 0 && e.BrickCastleStonePermille > 0
                && e.BrickCoreRepairIntervalTicks > 0 && e.BricksPerCoreRepair > 0 && e.BrickCoreRepairHp > 0
                && e.BrickExtraTowers >= 0
                && e.BrickCastleStonePermille <= 1000), "Invalid bricks rules.");
            Require(!e.Tack || (e.Enabled && e.Industry && e.Ages && e.SaddlerySizeCells > 0 && e.SaddlerySizeCells <= 8
                && e.SaddleryWoodCost >= 0 && e.TackWoodReserve >= 0 && e.SaddleryWork > 0 && e.SaddleryHp > 0 && e.TackTicks > 0
                && e.TackFoodInput > 0 && e.TackWoodInput > 0 && e.TackFoodInput <= e.BufferLimit && e.TackWoodInput <= e.BufferLimit
                && e.HeavyCavalryTackCost > 0 && e.HeavyCavalryFoodCost >= 0 && e.HeavyCavalryTrainTicks > 0
                && e.HeavyCavalryHp > 0 && e.HeavyCavalryDamage >= 0 && e.HeavyCavalryAttackIntervalTicks > 0
                && e.HeavyCavalryRange.Raw >= 0 && e.HeavyCavalryRange <= Fix64.FromInt(64)
                && e.HeavyCavalrySpeed.Raw > 0 && e.HeavyCavalrySpeed <= Fix64.FromInt(16) && e.HeavyCavalryVision.Raw >= 0), "Invalid tack rules.");
            Require(!e.Planks || (e.Enabled && e.Industry && e.Ages && e.SawmillSizeCells > 0 && e.SawmillSizeCells <= 8
                && e.SawmillWoodCost >= 0 && e.SawmillWork > 0 && e.SawmillHp > 0 && e.PlankTicks > 0
                && e.PlankWoodInput > 0 && e.PlankWoodInput <= e.BufferLimit && e.PlanksPerRam > 0
                && e.PlankRamHpPermille >= 1000 && e.PlankRamTrainPermille > 0 && e.PlankRamTrainPermille <= 1000
                && e.PlanksPerBridgeCell > 0 && e.PlankBridgeWoodPermille > 0 && e.PlankBridgeWoodPermille <= 1000
                && e.PlankExtraRams >= 0 && e.PlankWoodReserve >= 0), "Invalid planks rules.");
            Require(!e.CraftTree1 || (e.Enabled && e.Industry && e.Ages
                && e.CraftTreePlankWoodInput > 0 && e.CraftTreePlankWoodInput <= e.BufferLimit && e.CraftTreePlankTicks > 0
                && e.CraftTreeCharcoalWoodInput > 0 && e.CraftTreeCharcoalWoodInput <= e.BufferLimit && e.CraftTreeCharcoalTicks > 0
                && e.StonecutterSizeCells > 0 && e.StonecutterSizeCells <= 8 && e.StonecutterWoodCost >= 0
                && e.StonecutterWork > 0 && e.StonecutterHp > 0 && e.CraftTreeStoneInput > 0
                && e.CraftTreeStoneInput <= e.BufferLimit && e.CraftTreeStonecutterTicks > 0
                && e.JoinerySizeCells > 0 && e.JoinerySizeCells <= 8 && e.JoineryWoodCost >= 0
                && e.JoineryWork > 0 && e.JoineryHp > 0 && e.CraftTreeJoineryTicks > 0
                && e.CraftTreeJoineryPlankInput > 0 && e.CraftTreeJoineryPlankInput <= e.BufferLimit
                && e.CraftTreeJoineryCutStoneInput > 0 && e.CraftTreeJoineryCutStoneInput <= e.BufferLimit
                && e.CraftTreeFramesPerBuilding > 0 && e.CraftTreeFrameWoodPermille > 0 && e.CraftTreeFrameWoodPermille <= 1000
                && e.CraftTreeFrameWorkPermille > 0 && e.CraftTreeFrameWorkPermille <= 1000 && e.CraftTreeLevel2FramesCost > 0), "Invalid craft-tree rules.");
            Require(!e.CraftTree2 || (e.Enabled && e.Industry && e.Ages && e.AssemblerSizeCells > 0 && e.AssemblerSizeCells <= 8
                && e.AssemblerWoodCost >= 0 && e.AssemblerWork > 0 && e.AssemblerHp > 0
                && e.ArmsKitTicks > 0 && e.ArmsKitPlankInput > 0 && e.ArmsKitCharcoalInput > 0 && e.ArmsKitSteelInput > 0
                && e.ScholarlyTicks > 0 && e.ScholarlyPlankInput > 0 && e.ScholarlyCharcoalInput > 0 && e.ScholarlyFoodInput > 0
                && e.ArmsKitAttack >= 0 && e.ArmsKitHpPermille >= 1000 && e.FineArmsKitAttack >= e.ArmsKitAttack
                && e.FineArmsKitHpPermille >= e.ArmsKitHpPermille && e.ScholarlyPerResearch > 0
                && e.ScholarlyCostPermille > 0 && e.ScholarlyCostPermille <= 1000
                && e.ScholarlyTimePermille > 0 && e.ScholarlyTimePermille <= 1000), "Invalid craft-tree2 rules.");
            Require(!e.CraftTree2 || (e.GoodsValueArmsKit > 0 && e.GoodsValueScholarly > 0 && e.GoodsValueFineArmsKit > 0), "Invalid craft-tree2 market values.");
            Require(!e.CraftTree3 || (e.CraftTree2 && e.SiegePartsTicks > 0 && e.SiegePartsFramesInput > 0 && e.SiegePartsArmsKitInput > 0
                && e.MechanismTicks > 0 && e.MechanismArmsKitInput > 0 && e.MechanismScholarlyInput > 0 && e.MechanismCutStoneInput > 0
                && e.SiegePartsHpPermille > 0 && e.SiegePartsDamagePermille > 0 && e.SiegePartsPerCastle > 0 && e.SiegePartsCastleStonePermille > 0 && e.SiegePartsCastleStonePermille <= 1000
                && e.MechanismBuildingTimePermille > 0 && e.MechanismBuildingTimePermille <= 1000 && e.MechanismBuildingMaterialPermille > 0 && e.MechanismBuildingMaterialPermille <= 1000
                && e.MechanismResearchCostPermille > 0 && e.MechanismResearchCostPermille <= 1000 && e.MechanismResearchTimePermille > 0 && e.MechanismResearchTimePermille <= 1000
                && e.SiegePartsStockLimit > 0 && e.SiegePartsStockLimit <= e.BufferLimit
                && e.GoodsValueSiegeParts > 0 && e.GoodsValueMechanism > 0), "Invalid craft-tree3 rules.");
            Require(!e.CraftTree4 || (e.CraftTree3 && e.AgeFour && e.SiegeEngineTicks > 0 && e.SiegeEngineSiegePartsInput > 0
                && e.SiegeEngineMechanismInput > 0 && e.EliteKitTicks > 0 && e.EliteKitArmsKitInput > 0 && e.EliteKitMechanismInput > 0
                && e.EliteKitMetalInput > 0 && e.EliteKitAttack >= 0 && e.EliteKitHpPermille > 0
                && e.CannonSiegeEngineCost > 0 && e.CannonFoodCost >= 0 && e.CannonWoodCost >= 0 && e.CannonHp > 0
                && e.CannonDamage >= 0 && e.CannonSiegeDamage >= 0 && e.CannonInterval > 0 && e.CannonRange.Raw >= 0
                && e.CannonSpeed.Raw > 0 && e.CannonVision.Raw >= 0 && e.CannonAutoLimit > 0
                && e.GoodsValueSiegeEngine > 0 && e.GoodsValueEliteKit > 0), "Invalid craft-tree4 rules.");
            Require(!e.Books || (e.Enabled && e.Industry && e.Ages && e.ScriptoriumSizeCells > 0 && e.ScriptoriumSizeCells <= 8
                && e.ScriptoriumWoodCost >= 0 && e.ScriptoriumWork > 0 && e.ScriptoriumHp > 0 && e.BookTicks > 0
                && e.BookGoldInput > 0 && e.BookWoodInput > 0 && e.BookGoldInput <= e.BufferLimit && e.BookWoodInput <= e.BufferLimit
                && e.BooksPerResearch > 0 && e.BookResearchPermille > 0 && e.BookResearchPermille <= 1000
                && e.BookResearchCostPermille > 0 && e.BookResearchCostPermille <= 1000 && e.BookWoodReserve >= 0), "Invalid books rules.");
            Require(!e.Preserves || (e.Enabled && e.Industry && e.Ages && e.DryingRackSizeCells > 0 && e.DryingRackSizeCells <= 8
                && e.DryingRackWoodCost >= 0 && e.DryingRackWork > 0 && e.DryingRackHp > 0 && e.PreserveTicks > 0
                && e.PreserveFoodInput > 0 && e.PreserveFoodInput <= e.BufferLimit && e.PreserveFoodValue > 0
                && e.PreserveKeepFood >= 0 && e.PreserveMaxRacks > 0 && e.PreserveWoodReserve >= 0), "Invalid preserves rules.");
            Require(!e.Jewels || (e.Enabled && e.Industry && e.Ages && e.LapidarySizeCells > 0 && e.LapidarySizeCells <= 8
                && e.LapidaryWoodCost >= 0 && e.LapidaryWork > 0 && e.LapidaryHp > 0 && e.JewelTicks > 0
                && e.JewelStoneInput > 0 && e.JewelOreInput > 0 && e.JewelStoneInput <= e.BufferLimit && e.JewelOreInput <= e.BufferLimit
                && e.JewelMaxLapidaries > 0 && e.JewelKeepStone >= 0 && e.JewelWoodReserve >= 0
                && e.JewelMercenaryGems > 0 && e.JewelMercenaryFood >= 0 && e.JewelMercenaryInfantryRatio > 0), "Invalid jewels rules.");
            Require(!e.TradeGoods || (e.Enabled && e.Industry && e.Ages && e.WeaverSizeCells > 0 && e.WeaverSizeCells <= 8
                && e.WeaverWoodCost >= 0 && e.WeaverWork > 0 && e.WeaverHp > 0 && e.TradeGoodsTicks > 0
                && e.TradeGoodsWoodInput > 0 && e.TradeGoodsWoodInput <= e.BufferLimit && e.TradeGoodsKeepWood >= 0
                && e.TradeGoodsValue > 0 && e.TradeGoodsMaxWeavers > 0 && e.TradeGoodsWoodReserve >= 0), "Invalid trade-goods rules.");
            Require(!e.Incense || (e.Enabled && e.Industry && e.Ages && e.IncenseSizeCells > 0 && e.IncenseSizeCells <= 8
                && e.IncenseWoodCost >= 0 && e.IncenseWork > 0 && e.IncenseHp > 0 && e.IncenseTicks > 0
                && e.IncenseWoodInput > 0 && e.IncenseWoodInput <= e.BufferLimit && e.IncenseFoodInput > 0
                && e.IncenseFoodInput <= e.BufferLimit && e.IncenseKeepWood >= 0 && e.IncenseKeepFood >= 0
                && e.IncenseStockLimit > 0 && e.IncenseHealIntervalTicks > 0 && e.IncenseHealHp > 0
                && e.IncenseMaxHouses > 0 && e.IncenseWoodReserve >= 0), "Invalid incense rules.");
            Require(!e.Charms || (e.Enabled && e.Industry && e.Ages && e.CharmSizeCells > 0 && e.CharmSizeCells <= 8
                && e.CharmWoodCost >= 0 && e.CharmWork > 0 && e.CharmHp > 0 && e.CharmTicks > 0
                && e.CharmWoodInput > 0 && e.CharmWoodInput <= e.BufferLimit && e.CharmKeepWood >= 0
                && e.CharmStockLimit > 0 && e.CharmMaxWorkshops > 0 && e.CharmWoodReserve >= 0
                && e.CharmHpPermille >= 1000), "Invalid charms rules.");
            Require(!e.RepairKits || (e.Enabled && e.Industry && e.Ages && e.RepairYardSizeCells > 0 && e.RepairYardSizeCells <= 8
                && e.RepairYardWoodCost >= 0 && e.RepairYardWork > 0 && e.RepairYardHp > 0 && e.RepairKitTicks > 0
                && e.RepairKitWoodInput > 0 && e.RepairKitWoodInput <= e.BufferLimit && e.RepairKitKeepWood >= 0
                && e.RepairKitStockLimit > 0 && e.RepairKitMaxYards > 0 && e.RepairKitWoodReserve >= 0
                && e.RepairKitIntervalTicks > 0 && e.RepairKitHp > 0), "Invalid repair-kits rules.");
             Require(!e.Tools || (e.Enabled && e.Industry && e.Ages && e.ToolShopSizeCells > 0 && e.ToolShopSizeCells <= 8
                && e.ToolShopWoodCost >= 0 && e.ToolShopWork > 0 && e.ToolShopHp > 0 && e.ToolTicks > 0
                && e.ToolWoodInput > 0 && e.ToolWoodInput <= e.BufferLimit && e.ToolKeepWood >= 0
                && e.ToolStockLimit > 0 && e.ToolMaxShops > 0 && e.ToolWoodReserve >= 0
                && e.ToolVillagerFoodPermille > 0 && e.ToolVillagerFoodPermille <= 1000
                 && e.ToolVillagerTicksPermille > 0 && e.ToolVillagerTicksPermille <= 1000), "Invalid tools rules.");
            Require(!e.GoodsMarket || (e.Enabled && e.Industry && e.Ages && e.GoodsTradeLot > 0
                && e.GoodsAutoSellAt > 0 && e.GoodsAutoSellIntervalTicks > 0 && e.GoodsAutoKeep >= 0 && e.GoodsMarketWoodReserve >= 0
                && e.GoodsValueSteel > 0 && e.GoodsValueBowGear > 0 && e.GoodsValueRations > 0 && e.GoodsValueBricks > 0
                && e.GoodsValueTack > 0 && e.GoodsValuePlanks > 0 && e.GoodsValueBooks > 0 && e.GoodsValueIncense > 0
                && e.GoodsValueCharms > 0 && e.GoodsValueRepairKits > 0 && e.GoodsValueTools > 0), "Invalid goods market rules.");
            Require(!e.CraftTiming || (e.Enabled && e.Industry && e.Ages && e.CraftTimingFoodPerWood > 0), "Invalid craft-timing rules.");
            Require(!e.CraftBalance || (e.Enabled && e.Industry && e.Ages
                && e.CraftBalanceTollgateTiming >= 0 && e.CraftBalanceTollgateTiming <= 1), "Invalid craft-balance rules.");
            // V3-4: ages come with the terrain map.
            Require(!e.AgeClock || (e.Enabled && e.Ages), "Age clock requires ages and an economy.");
            Require(!e.AgeClockUx || (e.AgeClock && e.AgeClockUxChoiceTicks > 0), "Age-clock UX requires the age clock and a positive choice window.");
            Require(!e.Ages || (c.Map.Terrain.Length != 0 && e.AdvanceFoodCost >= 0 && e.AdvanceWoodCost >= 0 && e.AdvanceTicks > 0
                && (!e.AgeClock || (e.AgeClockTicks1 > 0 && e.AgeClockTicks2 > e.AgeClockTicks1 && e.AgeClockTicks3 > e.AgeClockTicks2
                    && (!e.AgeFour || e.AgeClockTicks4 > e.AgeClockTicks3)))
                && e.AgrarianInfantryFood >= 0 && e.AgrarianInfantryWood >= 0 && e.AgrarianInfantryTicks > 0 && e.ForgedInfantryHp > 0 && e.ForgedInfantryDamage >= 0
                && e.FarmSizeCells > 0 && e.FarmSizeCells <= 8 && e.FarmWoodCost >= 0 && e.FarmWork > 0 && e.FarmHp > 0
                && e.FarmMinTicks > 0 && e.FarmBaseTicks >= e.FarmMinTicks && e.FarmStepTicks >= 0 && e.FarmFoodReach >= 0 && e.FarmRiverReach >= 0
                && (!e.Forestry || (e.Industry && e.LumberCampSizeCells > 0 && e.LumberCampSizeCells <= 8 && e.LumberCampWoodCost >= 0
                    && e.LumberCampWork > 0 && e.LumberCampHp > 0 && e.LumberCampIntervalTicks > 0
                    && e.MarketFoodFloor >= 0 && e.MarketWoodReserve >= 0 && e.MarketStoneReserve >= 0
                    && e.FletcherSizeCells > 0 && e.FletcherSizeCells <= 8 && e.FletcherWoodCost >= 0 && e.FletcherWork > 0 && e.FletcherHp > 0
                    && e.FletcherTicks > 0 && e.FletcherWoodInput > 0 && e.FletcherFoodInput > 0
                    && e.SkirmishArcherFoodCost >= 0 && e.SkirmishArcherBowGearCost >= 0 && e.SkirmishArcherTrainTicks > 0
                    && e.SkirmishArcherHp > 0 && e.SkirmishArcherDamage >= 0 && e.SkirmishArcherAttackIntervalTicks > 0
                    && e.SkirmishArcherSpeed.Raw > 0 && e.SkirmishArcherSpeed <= Fix64.FromInt(16)
                    && e.SkirmishArcherRange.Raw >= 0 && e.SkirmishArcherRange <= Fix64.FromInt(64) && e.SkirmishArcherVision.Raw >= 0))
                && e.ScoutFoodCost >= 0 && e.ScoutWoodCost >= 0 && e.ScoutTrainTicks > 0
                && e.BasePopulation > 0 && e.HousePopulation >= 0 && e.HouseSizeCells > 0 && e.HouseSizeCells <= 8
                && e.HouseWoodCost >= 0 && e.HouseWork > 0 && e.HouseHp > 0
                && e.DropSiteSizeCells > 0 && e.DropSiteSizeCells <= 8 && e.DropSiteWoodCost >= 0 && e.DropSiteWork > 0 && e.DropSiteHp > 0
                && e.WallStoneCost >= 0 && e.WallHp > 0 && e.WallReach >= 0 && e.StartStone >= 0 && e.StartMetal >= 0 && e.TowerSizeCells > 0 && e.TowerSizeCells <= 8
                && e.TowerWoodCost >= 0 && e.TowerStoneCost >= 0 && e.TowerWork > 0 && e.TowerHp > 0 && e.TowerRange >= 0 && e.TowerVision >= 0
                && e.TowerDamage >= 0 && e.TowerIntervalTicks > 0
                && e.BlacksmithSizeCells > 0 && e.BlacksmithSizeCells <= 8 && e.BlacksmithWoodCost >= 0 && e.BlacksmithWork > 0 && e.BlacksmithHp > 0
                && e.TechFood != null && e.TechWood != null && e.TechTicks != null && e.TechMetal != null && e.TechGems != null
                && Array.TrueForAll(e.TechMetal, v => v >= 0) && Array.TrueForAll(e.TechGems, v => v >= 0)
                && e.SteelWeaponsDamage >= 0 && e.SteelArmourHp >= 0 && e.GemArmorHp >= 0
                && e.RepairHpPerTick > 0 && e.RepairAtPermille >= 0 && e.RepairAtPermille <= 1000
                && e.CastleSizeCells > 0 && e.CastleSizeCells <= 8 && e.CastleWoodCost >= 0 && e.CastleStoneCost >= 0 && e.CastleWork > 0 && e.CastleHp > 0
                && e.CastleRange >= 0 && e.CastleVision >= 0 && e.CastleDamage >= 0 && e.CastleIntervalTicks > 0
                 && e.MercenaryGems >= 0 && e.MercenaryTicks > 0 && e.MercenaryHp > 0 && e.MercenaryDamage >= 0 && e.MercenaryInterval > 0
                 && (!e.MonksEnabled || (e.ConversionTicks > 0 && e.MonkFoodCost >= 0 && e.MonkGoldCost >= 0 && e.MonkTrainTicks > 0))
                && Array.TrueForAll(e.TechFood, v => v >= 0) && Array.TrueForAll(e.TechWood, v => v >= 0) && ValidTechTicks(e.TechTicks)
                && e.WeaponsDamage >= 0 && e.ArmourHp >= 0 && e.ToolsGatherTicks >= 0 && e.ToolsGatherTicks < e.GatherIntervalTicks && e.CartsCarry >= 0
                && e.IrrigationTicks >= 0 && e.BlastFurnaceTicks >= 0 && e.BlastFurnaceTicks < e.SmeltTicks
                && e.Age2FoodCost >= 0 && e.Age2WoodCost >= 0 && e.Age2Ticks > 0 && e.Age2PopulationBonus >= 0
                && e.Age2SaveArmyFloor >= 0
                && e.Age3FoodCost >= 0 && e.Age3WoodCost >= 0 && e.Age3Ticks > 0 && e.Age3PopulationBonus >= 0
                && (!e.AgeVictoryEnabled || e.AgeVictoryTicks > 0)
                && e.SiegecraftSiegeDamage >= 0 && e.MasonryTowerDamage >= 0 && e.BankingTradeReturn >= 0
                && e.RangeSizeCells > 0 && e.RangeWoodCost >= 0 && e.RangeWork > 0 && e.RangeHp > 0
                && e.StableSizeCells > 0 && e.StableWoodCost >= 0 && e.StableWork > 0 && e.StableHp > 0
                && e.CounterBonusPermille >= 0
                && e.ArcherFood >= 0 && e.ArcherWood >= 0 && e.ArcherTicks > 0 && e.ArcherHp > 0 && e.ArcherDamage >= 0 && e.ArcherInterval > 0
                && e.ArcherRange.Raw >= 0 && e.ArcherRange <= Fix64.FromInt(64) && e.ArcherSpeed.Raw > 0 && e.ArcherSpeed <= Fix64.FromInt(16) && e.ArcherVision.Raw >= 0
                && e.CavalryFood >= 0 && e.CavalryWood >= 0 && e.CavalryMetal >= 0 && e.CavalryTicks > 0 && e.CavalryHp > 0 && e.CavalryDamage >= 0 && e.CavalryInterval > 0
                && e.CavalryRange.Raw >= 0 && e.CavalryRange <= Fix64.FromInt(64) && e.CavalrySpeed.Raw > 0 && e.CavalrySpeed <= Fix64.FromInt(16) && e.CavalryVision.Raw >= 0
                && (!e.Cavalry || (e.LightCavalryFood >= 0 && e.LightCavalryWood >= 0 && e.LightCavalryTicks > 0
                    && e.LightCavalryHp > 0 && e.LightCavalryDamage >= 0 && e.LightCavalryInterval > 0
                    && e.LightCavalryRange.Raw >= 0 && e.LightCavalryRange <= Fix64.FromInt(64)
                    && e.LightCavalrySpeed.Raw > 0 && e.LightCavalrySpeed <= Fix64.FromInt(16) && e.LightCavalryVision.Raw >= 0))
                && e.MarketSizeCells > 0 && e.MarketSizeCells <= 8 && e.MarketWoodCost >= 0 && e.MarketWork > 0 && e.MarketHp > 0 && e.TradeLot > 0 && e.TradeReturn >= 0 && e.GemsTradeReturn >= 0
                 && e.TradeRouteWood > 0 && e.TradeRouteMin > 0
                && e.WorkshopSizeCells > 0 && e.WorkshopSizeCells <= 8 && e.WorkshopWoodCost >= 0 && e.WorkshopWork > 0 && e.WorkshopHp > 0
                && e.RamFood >= 0 && e.RamWood >= 0 && e.RamTicks > 0 && e.RamHp > 0 && e.RamDamage >= 0 && e.RamSiegeDamage >= 0 && e.RamInterval > 0
                && e.RamRange.Raw >= 0 && e.RamRange <= Fix64.FromInt(64) && e.RamSpeed.Raw > 0 && e.RamSpeed <= Fix64.FromInt(16) && e.RamVision.Raw >= 0), "Invalid age rules.");
            int expectedTechLength = e.AgeFour ? 12 : (e.Cavalry ? 13 : 12);
            Require(e.TechFood.Length == expectedTechLength || (e.AgeFour && e.TechFood.Length == 29), "Invalid AgeFour tech-food array.");
            Require(e.TechWood.Length == expectedTechLength || (e.AgeFour && e.TechWood.Length == 29), "Invalid AgeFour tech-wood array.");
            Require(e.TechTicks.Length == expectedTechLength || (e.AgeFour && e.TechTicks.Length == 29), "Invalid AgeFour tech-ticks array.");
            Require(e.TechMetal.Length == expectedTechLength || (e.AgeFour && e.TechMetal.Length == 29), "Invalid AgeFour tech-metal array.");
            Require(e.TechGems.Length == expectedTechLength || (e.AgeFour && e.TechGems.Length == 29), "Invalid AgeFour tech-gems array.");
             Require(!e.AgeFour || (e.Age4FoodCost >= 0 && e.Age4WoodCost >= 0 && e.Age4Ticks > 0 && e.Age4PopulationBonus >= 0
                 && e.SiegeAge4Permille > 0 && e.SiegeAge4Permille <= 10000
                 && e.TotalWarFoodCost >= 0 && e.TotalWarWoodCost >= 0 && e.TotalWarMetalCost >= 0 && e.TotalWarTicks > 0
                 && e.GrandWorkshopFoodCost >= 0 && e.GrandWorkshopWoodCost >= 0 && e.GrandWorkshopMetalCost >= 0 && e.GrandWorkshopTicks > 0), "Invalid AgeFour rules.");
             Require(!e.AgeStrengths || (e.AgeFour && e.AgrarianFarmSpeedPermille > 0 && e.AgrarianRationsHealHp >= 0 && e.AgrarianPopulationCapBonus >= 0
                 && e.MetallurgySmeltSpeedPermille > 0 && e.MetallurgyForgedDamageBonus >= 0 && e.MetallurgySiegePartsTimePermille > 0
                 && e.ForestryLumberSpeedPermille > 0 && e.ForestryArcherRangeBonus >= 0 && e.ForestryBuildingWoodPermille > 0
                 && e.MasonryWallHpPermille > 0 && e.MasonryTowerDamageBonus >= 0 && e.MasonryCoreHpPermille > 0
                 && e.CaravanTradeGoodsValue >= 0 && e.CaravanMarketReturnPermille > 0 && e.CaravanRewardPermille > 0
                 && e.CavalryStableTimePermille > 0 && e.CavalrySpeedPermille > 0 && e.CavalryHeavyHpPermille > 0
                 && e.BridgeBuildingWorkPermille > 0 && e.BridgeRamSiegeDamagePermille > 0 && e.BridgeGeneralWorkPermille > 0
                 && e.AcademyResearchTimePermille > 0 && e.AcademyScholarlyCostPermille > 0 && e.AcademyAge4ResearchCostPermille > 0
                 && e.CultConversionTimePermille > 0 && e.CultIncenseHealHp >= 0 && e.CultMonkHpPermille > 0
                 && e.FishingGatherTimePermille > 0 && e.FishingPreserveFoodValue >= 0 && e.FishingFoodCarryBonus >= 0
                 && e.MountainMiningTimePermille > 0 && e.MountainMercenaryGems >= 0 && e.MountainMercenaryDamageBonus >= 0
                 && e.TollgateFeePermille > 0 && e.TollgateHpPermille > 0 && e.TollgateRepairKitHp >= 0
                 && e.MetropolisVillagerTimePermille > 0 && e.MetropolisTownLimitBonus >= 0 && e.MetropolisToolTimePermille > 0
                 && e.SanctuaryPilgrimagePermille > 0 && e.SanctuaryCharmHpPermille > 0 && e.SanctuaryCharmHpAge4Permille > 0), "Invalid age-strength rules.");
            Require(!e.FishingEnabled || (e.Ages && e.FishRegrowTicks > 0 && e.FishAgrarianBonusPermille >= 0
                && e.FishAgrarianBonusPermille <= 1000 && e.FishReach >= 0 && e.FishReach <= 1024), "Invalid fishing rules.");
            Require(!e.CoreDefence || (e.Enabled && e.CoreDefenceRange >= 0 && e.CoreDefenceRange <= 64
                && e.CoreDefenceDamage >= 0 && e.CoreDefenceIntervalTicks > 0
                && e.CoreDefenceMaxTargets > 0 && e.CoreDefenceMaxTargets <= 16), "Invalid core defence rules.");
            Require(!e.LatePush || (e.Enabled && e.LatePushAfterTicks > 0 && e.LatePushAdvantageAfterTicks > 0
                && e.LatePushEnemyMultiplierPermille > 0 && e.LatePushEnemyMultiplierPermille <= 10000), "Invalid late-push rules.");
            Require(!e.SiegeByAge || (e.Enabled && e.Ages && e.SiegeAge0Permille > 0 && e.SiegeAge0Permille <= 10000
                && e.SiegeAge1Permille > 0 && e.SiegeAge1Permille <= 10000
                && e.SiegeAge2Permille > 0 && e.SiegeAge2Permille <= 10000
                && e.SiegeAge3Permille > 0 && e.SiegeAge3Permille <= 10000
                && (!e.AgeFour || (e.SiegeAge4Permille > 0 && e.SiegeAge4Permille <= 10000))), "Invalid siege-by-age rules.");
            Require(!e.PushWaves || (e.Enabled && e.Ages && e.LatePush && e.SiegeByAge && e.WaveStagingDistance > 0
                && e.WaveSize > 0 && e.WaveMaxWaitTicks > 0 && e.WaveMinSize > 0 && e.WaveMinSize <= e.WaveSize
                && e.WaveRetreatPermille > 0 && e.WaveRetreatPermille <= 1000), "Invalid push-wave rules.");
            Require(!e.ManualControl || (e.Enabled && e.LatePush), "Manual control requires the economy and late-push rules.");
            Require(!e.AutoReturnAfterHuman || (e.Enabled && e.AutoReturnArriveMeters > 0
                && e.AutoReturnArriveMeters <= 64 && e.AutoReturnHoldTicks > 0), "Invalid automatic human-order return rules.");
            Require(!e.FishingCiv || (e.Enabled && e.Ages && e.FishingEnabled && e.HarborSizeCells > 0 && e.HarborSizeCells <= 8
                && e.HarborWoodCost >= 0 && e.HarborWork > 0 && e.HarborHp > 0
                && e.FishingNetFoodCost >= 0 && e.FishingNetWoodCost >= 0 && e.FishingNetTicks > 0
                && e.DriedFishFoodCost >= 0 && e.DriedFishWoodCost >= 0 && e.DriedFishTicks > 0
                && e.FishingNetCarryBonusPermille >= 0 && e.DriedFishRegrowIntervalPermille > 0 && e.DriedFishRegrowIntervalPermille <= 1000),
                "Fishing civilisation requires valid age, fishing and harbour rules.");
            Require(!e.Metropolis || (e.Enabled && e.Ages && e.GrandHouseSizeCells > 0 && e.GrandHouseSizeCells <= 8
                && e.GrandHouseWoodCost >= 0 && e.GrandHouseWork > 0 && e.GrandHouseHp > 0
                && e.MetropolisMarketFoodCost >= 0 && e.MetropolisMarketWoodCost >= 0 && e.MetropolisMarketTicks > 0
                && e.MetropolisMilitiaFoodCost >= 0 && e.MetropolisMilitiaWoodCost >= 0 && e.MetropolisMilitiaTicks > 0
                && e.MetropolisMarketVillagerStep > 0 && e.MetropolisMarketBonusPermille > 0
                && e.MetropolisMarketMaxBonusPermille >= e.MetropolisMarketBonusPermille
                && e.MetropolisMilitiaDamage >= 0 && e.MetropolisMilitiaCoreRadius >= 0),
                "Metropolis requires valid grand-house and research rules.");
            Require(!e.Forestry || (e.Ages && e.Industry), "Forestry requires ages and industry.");
            Require(!e.Cavalry || e.Ages, "Cavalry requires ages.");
            if (e.Cavalry)
                Require(e.CavalryDrillFood >= 0 && e.CavalryDrillWood >= 0 && e.CavalryDrillTicks > 0
                    && e.CavalryDrillSpeed.Raw > 0 && e.CavalryDrillSpeed <= Fix64.FromInt(16)
                    && c.Economy.TechFood.Length >= 13 && c.Economy.TechWood.Length >= 13
                    && c.Economy.TechTicks.Length >= 13 && c.Economy.TechMetal.Length >= 13
                    && c.Economy.TechGems.Length >= 13, "Invalid cavalry drill rules.");
            Require(!e.Masonry || (e.Ages && e.Industry && e.QuarrySizeCells > 0 && e.QuarrySizeCells <= 8 && e.QuarryWoodCost >= 0
                && e.QuarryWork > 0 && e.QuarryHp > 0 && e.QuarryIntervalTicks > 0
                && e.MarketFoodFloor >= 0 && e.MarketWoodReserve >= 0 && e.MarketStoneReserve >= 0
                && e.MasonryDefenceCostPermille > 0 && e.MasonryDefenceCostPermille <= 1000
                && e.MasonryDefenceWorkPermille > 0 && e.MasonryDefenceWorkPermille <= 1000), "Masonry requires valid ages, industry and quarry rules.");
            Require(!e.Bridge || (e.Ages && e.Industry && e.EngineerCampSizeCells > 0 && e.EngineerCampSizeCells <= 8
                && e.EngineerCampWoodCost >= 0 && e.EngineerCampWork > 0 && e.EngineerCampHp > 0
                && e.BridgeWoodCost >= 0 && e.BridgeWork > 0 && e.BridgeHp > 0
                && e.MaxBridgeLength > 0 && e.MaxBridgeLength <= 256
                && e.BridgeworksFoodCost >= 0 && e.BridgeworksWoodCost >= 0 && e.BridgeworksTicks > 0
                && e.BridgeworksHpBonus >= 0 && e.BridgeworksWorkReduction >= 0
                && e.SiegeDeploymentFoodCost >= 0 && e.SiegeDeploymentWoodCost >= 0 && e.SiegeDeploymentTicks > 0
                && e.SiegeDeploymentRamTicksReduction >= 0 && e.SiegeDeploymentRamTicksReduction < e.RamTicks
                && e.SiegeDeploymentRamCapacityBonus >= 0), "Bridge requires valid ages, industry and bridge rules.");
            Require(!e.Caravan || (e.Ages && e.CaravanseraiSizeCells > 0 && e.CaravanseraiSizeCells <= 8
                && e.CaravanseraiWoodCost >= 0 && e.CaravanseraiWork > 0 && e.CaravanseraiHp > 0
                && e.CaravanOutpostReach >= 0 && e.CaravanMinimumDistance > 0 && e.CaravanRewardDistanceStep > 0
                && e.CaravanRewardMaxWood > 0 && e.CaravanAutoVillagers > 0), "Caravan requires valid age and caravanserai rules.");
            Require(!e.Academy || (e.Enabled && e.Ages && e.AcademySizeCells > 0 && e.AcademySizeCells <= 8
                && e.AcademyWoodCost >= 0 && e.AcademyWork > 0 && e.AcademyHp > 0
                && e.AcademyToolsFoodCost >= 0 && e.AcademyToolsWoodCost >= 0 && e.AcademyToolsGoldCost > 0 && e.AcademyToolsTicks > 0
                && e.AcademyCartsFoodCost >= 0 && e.AcademyCartsWoodCost >= 0 && e.AcademyCartsGoldCost > 0 && e.AcademyCartsTicks > 0),
                "Academy requires valid age and academy rules.");
            Require(!e.Sanctuary || (e.Enabled && e.Ages && e.ShrineSizeCells > 0 && e.ShrineSizeCells <= 8
                && e.ShrineWoodCost >= 0 && e.ShrineStoneCost >= 0 && e.ShrineWork > 0 && e.ShrineHp > 0
                && e.ShrineOutpostReach >= 0 && e.SanctuaryAttackBonusPermille >= 0
                && e.SanctuaryMaxBonusPermille >= e.SanctuaryAttackBonusPermille
                && e.SanctuaryPilgrimageFoodCost >= 0 && e.SanctuaryPilgrimageWoodCost >= 0 && e.SanctuaryPilgrimageTicks > 0
                && e.SanctuaryRelicFoodCost >= 0 && e.SanctuaryRelicWoodCost >= 0 && e.SanctuaryRelicTicks > 0
                && e.SanctuaryPilgrimageRadius >= 0 && e.SanctuaryPilgrimageIntervalTicks > 0 && e.SanctuaryPilgrimageHeal >= 0
                && e.SanctuaryRelicBonusPermille >= 0 && e.SanctuaryRelicMaxBonusPermille >= e.SanctuaryRelicBonusPermille),
                "Sanctuary requires valid age, shrine and research rules.");
             Require(!e.Cult || (e.Enabled && e.Ages && e.MonasterySizeCells > 0 && e.MonasterySizeCells <= 8
                && e.MonasteryWoodCost >= 0 && e.MonasteryWork > 0 && e.MonasteryHp > 0
                && e.MonasteryMonkFoodCost >= 0 && e.MonasteryMonkWoodCost >= 0
                && e.SermonFoodCost >= 0 && e.SermonWoodCost >= 0 && e.SermonTicks > 0
                && e.MartyrBlessingFoodCost >= 0 && e.MartyrBlessingWoodCost >= 0 && e.MartyrBlessingTicks > 0
                && e.MartyrBlessingHpPermille >= 1000),
                 "Cult requires valid age and monastery rules.");
             Require(!e.Towns || (e.Enabled && e.Ages && e.TownSizeCells > 0 && e.TownSizeCells <= 8
                 && e.TownWoodCost >= 0 && e.TownStoneCost >= 0 && e.TownWork > 0 && e.TownHp > 0
                 && e.TownMaxBuildings > 0 && e.TownCoreDistance > 0 && e.TownResourceReach >= 0),
                 "Towns require valid age and town rules.");
            Require(!e.Mountain || (e.Enabled && e.Ages && e.Industry && e.MountainSizeCells > 0 && e.MountainSizeCells <= 8
                 && e.MountainWoodCost >= 0 && e.MountainWork > 0 && e.MountainHp > 0
                 && e.MountainBaseIntervalTicks > 0 && e.MountainIntervalStepTicks >= 0
                 && e.MountainMinIntervalTicks > 0 && e.MountainMinIntervalTicks <= e.MountainBaseIntervalTicks
                 && e.MountainMaxBuildings > 0 && e.MountainMaxAdjacentCells >= 0
                 && e.MountainStoneYield > 0 && e.MountainOreYield > 0
                 && e.MountainDeepShaftFoodCost >= 0 && e.MountainDeepShaftWoodCost >= 0 && e.MountainDeepShaftTicks > 0
                 && e.MountainFortFoodCost >= 0 && e.MountainFortWoodCost >= 0 && e.MountainFortTicks > 0
                 && e.MountainDeepShaftIntervalPermille > 0 && e.MountainDeepShaftIntervalPermille <= 1000
                 && e.MountainDeepShaftMaxBuildingsBonus >= 0
                 && e.MountainFortHpPermille >= 1000 && e.MountainFortRangeBonus >= 0),
                "Mountain requires valid age, industry and mine-shaft rules.");
             Require(!e.Tollgate || (e.Enabled && e.TollgateLengthCells == 2
                 && e.TollgateWoodCost >= 0 && e.TollgateStoneCost >= 0 && e.TollgateWork > 0 && e.TollgateHp > 0
                 && e.TollgateMaxBuildings > 0 && e.TollgateFeeRadiusMeters >= 0 && e.TollgateFeeIntervalTicks > 0
                 && e.TollgateWoodPerEnemy >= 0 && e.TollgateFoodPerEnemy >= 0 && e.TollgateNetworkRadiusMeters >= 0
                 && e.TollgateNetworkFeeBonusPermille >= 0 && e.TollgateNetworkFeeBonusPermille <= 1000
                 && e.TollgateGateDefenceFoodCost >= 0 && e.TollgateGateDefenceWoodCost >= 0 && e.TollgateGateDefenceTicks > 0
                 && e.TollgateGateNetworkFoodCost >= 0 && e.TollgateGateNetworkWoodCost >= 0 && e.TollgateGateNetworkTicks > 0
                 && e.TollgateGateDefenceHpPermille >= 1000 && e.TollgateGateDefenceDamageReductionPermille >= 0
                 && e.TollgateGateDefenceDamageReductionPermille <= 1000 && e.TollgateGateNetworkMaxBuildingsBonus >= 0),
                 "Tollgate requires valid age and tollgate rules.");
            Require(!e.GoldEnabled || (e.Ages && e.Age3GoldCostAgrarian >= 0 && e.Age3GoldCostMetallurgy >= 0 && e.GoldGatherers >= 0 && e.GoldAmount > 0
                && e.GoldDangerMeters >= 0 && e.GoldDangerMeters <= 1024), "Invalid gold rules.");
            // V3-4: terrain comes with the industry map, and every cell that is not plain must be blocked.
            if (c.Map.Terrain.Length != 0)
            {
                Require(e.Industry && c.Map.Terrain.Length == m.WidthCells * m.HeightCells && m.DefaultPassable, "Invalid terrain.");
                var closed = new bool[c.Map.Terrain.Length];
                foreach (int id in blocked) closed[id] = true;
                for (int i = 0; i < closed.Length; i++)
                    Require(c.Map.Terrain[i] <= (byte)TerrainKind.Mountain && (c.Map.Terrain[i] == 0 || closed[i]), "Terrain outside a blocked cell.");
            }
            Array.Sort(c.Villagers, (a, b) => a.Id.CompareTo(b.Id));
            var villagerCounts = new int[2];
            var villagerGrid = new GridMap(c.Map);
            for (int i = 0; i < c.Villagers.Length; i++)
            {
                var v = c.Villagers[i];
                Require(v.Id == i + 1 && v.FactionId >= 1 && v.FactionId <= 2, "Invalid villager.");
                ValidatePoint(v.Position, c.Map);
                Require(villagerGrid.IsPassable(villagerGrid.Cell(v.Position)), "Villager starts outside passable terrain.");
                villagerCounts[v.FactionId - 1]++;
            }
            Array.Sort(c.ResourceNodes, (a, b) => a.Id.CompareTo(b.Id));
            var nodeCells = new System.Collections.Generic.HashSet<long>();
            for (int i = 0; i < c.ResourceNodes.Length; i++)
            {
                var n = c.ResourceNodes[i];
                Require(n.Id == i + 1 && (n.Kind == ResourceKind.Food || n.Kind == ResourceKind.Wood || (n.Kind == ResourceKind.Ore && e.Industry)
                    || (n.Kind == ResourceKind.Stone && e.Ages) || (n.Kind == ResourceKind.Gold && e.GoldEnabled))
                    && n.Amount > 0, "Invalid resource node.");
                ValidatePoint(n.Position, c.Map);
                // One node per cell; the key is the cell, not the point, so two points in one cell are rejected too.
                long cell = (n.Position.Z.Raw / 65536 / m.CellSizeMeters) * m.WidthCells + n.Position.X.Raw / 65536 / m.CellSizeMeters;
                Require(nodeCells.Add(cell), "Two resource nodes share a cell.");
            }
            Array.Sort(c.Belts, (a, b) => a.Cell.CompareTo(b.Cell));
            var beltCounts = new int[2];
            for (int i = 0; i < c.Belts.Length; i++)
            {
                var b = c.Belts[i];
                Require(b.Cell >= 0 && b.Cell < m.WidthCells * m.HeightCells && (i == 0 || c.Belts[i - 1].Cell != b.Cell)
                    && b.FactionId >= 1 && b.FactionId <= 2 && (byte)b.Facing <= 3
                    && ((byte)b.Item <= 4 || (e.Jewels && b.Item == ResourceKind.Gems)
                        || (e.ProcessingChain && (b.Item == ResourceKind.Charcoal || b.Item == ResourceKind.Steel))
                        || (e.Forestry && b.Item == ResourceKind.BowGear) || (e.Rations && b.Item == ResourceKind.Rations)
                        || (e.Bricks && b.Item == ResourceKind.Bricks) || (e.Planks && b.Item == ResourceKind.Planks)
                        || (e.Books && b.Item == ResourceKind.Books) || (e.Preserves && b.Item == ResourceKind.Preserves)
|| (e.TradeGoods && b.Item == ResourceKind.TradeGoods) || (e.Incense && b.Item == ResourceKind.Incense) || (e.Charms && b.Item == ResourceKind.Charms) || (e.Tools && b.Item == ResourceKind.Tools))
                    && villagerGrid.IsPassable(b.Cell) && !nodeCells.Contains(b.Cell), "Invalid belt.");
                Require(++beltCounts[b.FactionId - 1] <= e.BeltLimit, "Too many belts.");
            }
            // Definitions may arrive in any enumeration order; IDs are explicit and contiguous.
            Array.Sort(c.Factions, (a, b) => a.Id.CompareTo(b.Id));
            Array.Sort(c.Cores, (a, b) => a.Id.CompareTo(b.Id));
            Array.Sort(c.Outposts, (a, b) => a.Id.CompareTo(b.Id));
            Array.Sort(c.Armies, (a, b) => a.Id.CompareTo(b.Id));
            Array.Sort(c.Soldiers, (a, b) => a.Id.CompareTo(b.Id));
            Array.Sort(c.UnitParameters, (a, b) => a.Kind.CompareTo(b.Kind));
            Require(c.Factions.Length == 2 && c.Cores.Length == 2, "Exactly two factions and cores are required.");
            for (int i = 0; i < c.UnitParameters.Length; i++)
            {
                var p = c.UnitParameters[i];
                Require((p.Kind == UnitKind.Infantry || p.Kind == UnitKind.Scout || p.Kind == UnitKind.Monk || e.CraftTree4 && p.Kind == UnitKind.Cannon) && (i == 0 || c.UnitParameters[i - 1].Kind != p.Kind)
                    && p.Hp > 0 && p.Damage >= 0 && p.AttackIntervalTicks > 0 && p.Speed.Raw >= 0 && p.Speed <= Fix64.FromInt(16)
                    && p.Range.Raw >= 0 && p.Range <= Fix64.FromInt(1024) && p.Vision.Raw >= 0, "Invalid unit parameters.");
            }
            var assigned = new bool[c.Armies.Length];
            for (int f = 0; f < 2; f++)
            {
                var d = c.Factions[f];
                Require(d.Id == f + 1 && d.CoreId > 0 && d.CoreId <= 2, "Invalid faction.");
                d.ArmyIds = Copy(d.ArmyIds);
                Array.Sort(d.ArmyIds);
                foreach (uint id in d.ArmyIds)
                {
                    Require(id > 0 && id <= c.Armies.Length && !assigned[id - 1]
                        && c.Armies[id - 1].FactionId == d.Id, "Invalid army membership.");
                    assigned[id - 1] = true;
                }
                c.Factions[f] = d;
                Require(c.Cores[d.CoreId - 1].FactionId == d.Id, "Invalid faction core.");
            }
            for (int i = 0; i < c.Cores.Length; i++)
            {
                var d = c.Cores[i];
                Require(d.Id == i + 1 && d.FactionId >= 1 && d.FactionId <= 2 && d.Hp >= 0, "Invalid core.");
                ValidatePoint(d.Position, c.Map);
            }
            for (int i = 0; i < c.Outposts.Length; i++)
            {
                var d = c.Outposts[i];
                Require(d.Id == i + 1 && d.OwnerFactionId <= 2, "Invalid outpost.");
                ValidatePoint(d.Position, c.Map);
            }
            for (int i = 0; i < c.Armies.Length; i++)
            {
                var d = c.Armies[i];
                Require(d.Id == i + 1 && assigned[i] && d.Role != null && d.Capacity > 0
                    && ((d.HomeObjective.Kind == GoalKind.Core && d.HomeObjective.Id == c.Factions[d.FactionId - 1].CoreId)
                    || (d.HomeObjective.Kind == GoalKind.Outpost && d.HomeObjective.Id > 0 && d.HomeObjective.Id <= c.Outposts.Length)),
                    "Invalid week-one army.");
            }
            var grid = new GridMap(c.Map);
            var armyCounts = new int[c.Armies.Length];
            var factionCounts = new int[2];
            for (int i = 0; i < c.Soldiers.Length; i++)
            {
                var d = c.Soldiers[i];
                Require(d.Id == i + 1 && d.ArmyId > 0 && d.ArmyId <= c.Armies.Length
                    && d.FactionId == c.Armies[d.ArmyId - 1].FactionId, "Invalid soldier identity.");
                var p = Array.Find(c.UnitParameters, value => value.Kind == d.Kind);
                Require(p.Hp > 0 && d.Hp >= 0 && d.Hp <= p.Hp && d.Alive == (d.Hp > 0), "Invalid soldier HP or kind.");
                ValidatePoint(d.Position, c.Map);
                Require(grid.IsPassable(grid.Cell(d.Position)), "Soldier starts outside passable terrain.");
                if (d.Alive)
                {
                    Require(++armyCounts[d.ArmyId - 1] <= c.Armies[d.ArmyId - 1].Capacity
                        && ++factionCounts[d.FactionId - 1] <= factionCap, "Initial capacity exceeded.");
                }
            }
            if (e.Enabled)
                for (int f = 0; f < 2; f++)
                    Require(factionCounts[f] + villagerCounts[f] <= e.PopulationCap, "Initial population exceeds the cap.");
            return c;
        }

        private static EconomyRules CopyEconomy(EconomyRules e)
        {
            Require(e != null, "Missing economy rules.");
            var techFood = (int[])e.TechFood.Clone();
            var techWood = (int[])e.TechWood.Clone();
            var techTicks = (int[])e.TechTicks.Clone();
            var techMetal = (int[])e.TechMetal.Clone();
            var techGems = (int[])e.TechGems.Clone();
            if (e.AgeFour)
            {
                Array.Resize(ref techFood, 29); Array.Resize(ref techWood, 29); Array.Resize(ref techTicks, 29);
                Array.Resize(ref techMetal, 29); Array.Resize(ref techGems, 29);
                techFood[(int)TechKind.TotalWar - 1] = e.TotalWarFoodCost; techWood[(int)TechKind.TotalWar - 1] = e.TotalWarWoodCost;
                techTicks[(int)TechKind.TotalWar - 1] = e.TotalWarTicks; techMetal[(int)TechKind.TotalWar - 1] = e.TotalWarMetalCost;
                techFood[(int)TechKind.GrandWorkshop - 1] = e.GrandWorkshopFoodCost; techWood[(int)TechKind.GrandWorkshop - 1] = e.GrandWorkshopWoodCost;
                techTicks[(int)TechKind.GrandWorkshop - 1] = e.GrandWorkshopTicks; techMetal[(int)TechKind.GrandWorkshop - 1] = e.GrandWorkshopMetalCost;
            }
            if (e.Cavalry)
            {
                if (techFood.Length < 13) Array.Resize(ref techFood, 13);
                if (techWood.Length < 13) Array.Resize(ref techWood, 13);
                if (techTicks.Length < 13) Array.Resize(ref techTicks, 13);
                if (techMetal.Length < 13) Array.Resize(ref techMetal, 13);
                if (techGems.Length < 13) Array.Resize(ref techGems, 13);
                techFood[12] = e.CavalryDrillFood; techWood[12] = e.CavalryDrillWood;
                techTicks[12] = e.CavalryDrillTicks; techMetal[12] = 0; techGems[12] = 0;
            }
            return new EconomyRules { Enabled = e.Enabled, MeasurementScale = e.MeasurementScale, BuildingReservations = e.BuildingReservations, NoNegativeStock = e.NoNegativeStock,
                ReservationLimit = e.ReservationLimit, ReservationCheckTicks = e.ReservationCheckTicks,
                StartFood = e.StartFood, StartWood = e.StartWood,
                ArmyGrowth = e.ArmyGrowth, EconomyScale = e.EconomyScale, LargeEconomy = e.LargeEconomy,
                LargeVillagerTrainTicks = e.LargeVillagerTrainTicks, LargeInfantryTrainTicks = e.LargeInfantryTrainTicks,
                LargeVillagerBatch = e.LargeVillagerBatch, LargeInfantryBatch = e.LargeInfantryBatch,
                LargeBarracksMaxBuildings = e.LargeBarracksMaxBuildings, LargeArmyCapacity = e.LargeArmyCapacity,
                LargeTownDecisionIntervalTicks = e.LargeTownDecisionIntervalTicks, LargeTownMinAge = e.LargeTownMinAge,
                LargeFactionCap = e.LargeFactionCap, LargeResourceAmountPermille = e.LargeResourceAmountPermille,
                LargeFoodAmountPermille = e.LargeFoodAmountPermille, LargeOreAmountPermille = e.LargeOreAmountPermille,
                LargeStoneAmountPermille = e.LargeStoneAmountPermille, LargeGoldAmountPermille = e.LargeGoldAmountPermille,
                 LargeFoodPerWood = e.LargeFoodPerWood, LargeConstructionReserveWood = e.LargeConstructionReserveWood,
                  LargeTownStoneGatherers = e.LargeTownStoneGatherers, LargeTownVillagersPerTown = e.LargeTownVillagersPerTown,
                  LargeBarracksVillagersPerBuilding = e.LargeBarracksVillagersPerBuilding, LargeTownAutoWorkers = e.LargeTownAutoWorkers,
                  LargeTargetRoundTripTicks = e.LargeTargetRoundTripTicks, LargeResourceClusterRadiusMeters = e.LargeResourceClusterRadiusMeters,
             LargeClusterDecisionIntervalTicks = e.LargeClusterDecisionIntervalTicks, LargeResourceClusterMaxDropSites = e.LargeResourceClusterMaxDropSites,
             LargeDropSiteMax = e.LargeDropSiteMax,
                   LargeClusterMinGatherers = e.LargeClusterMinGatherers,
                   LargeDropSiteMinSpacingMeters = e.LargeDropSiteMinSpacingMeters,
                   LargeClusterSwitchPermille = e.LargeClusterSwitchPermille,
                   LargeVillagersPerFarm = e.LargeVillagersPerFarm, LargeFarmMinimum = e.LargeFarmMinimum,
                   LargeFarmMaximum = e.LargeFarmMaximum,
                   LargeInfantryResourceReserveUnits = e.LargeInfantryResourceReserveUnits,
                   LargeExploreTargetPermille = e.LargeExploreTargetPermille, LargeExploreScoutCount = e.LargeExploreScoutCount,
                   LargeExploreChunkCells = e.LargeExploreChunkCells,
                AiArmyRecovery = e.AiArmyRecovery, LineBuildingCheck = e.LineBuildingCheck, Regions = e.Regions, DirectPathMovement = e.DirectPathMovement, LatePush = e.LatePush,
                LatePushAfterTicks = e.LatePushAfterTicks, LatePushAdvantageAfterTicks = e.LatePushAdvantageAfterTicks,
                 LatePushEnemyMultiplierPermille = e.LatePushEnemyMultiplierPermille,
                 SiegeByAge = e.SiegeByAge, SiegeAge0Permille = e.SiegeAge0Permille, SiegeAge1Permille = e.SiegeAge1Permille,
                 SiegeAge2Permille = e.SiegeAge2Permille, SiegeAge3Permille = e.SiegeAge3Permille, SiegeAge4Permille = e.SiegeAge4Permille,
                 PushWaves = e.PushWaves, WaveStagingDistance = e.WaveStagingDistance, WaveSize = e.WaveSize,
                 WaveMaxWaitTicks = e.WaveMaxWaitTicks, WaveMinSize = e.WaveMinSize, WaveRetreatPermille = e.WaveRetreatPermille,
                 ManualControl = e.ManualControl, AutoReturnAfterHuman = e.AutoReturnAfterHuman,
                 AutoReturnArriveMeters = e.AutoReturnArriveMeters, AutoReturnHoldTicks = e.AutoReturnHoldTicks,
                PopulationCap = e.PopulationCap, VillagerHp = e.VillagerHp, VillagerSpeed = e.VillagerSpeed,
                CarryCapacity = e.CarryCapacity, GatherIntervalTicks = e.GatherIntervalTicks, VillagerFoodCost = e.VillagerFoodCost,
                VillagerTrainTicks = e.VillagerTrainTicks, QueueLimit = e.QueueLimit, AutoVillagerTarget = e.AutoVillagerTarget,
                DropOffMargin = e.DropOffMargin, BarracksSizeCells = e.BarracksSizeCells, BarracksWoodCost = e.BarracksWoodCost,
                BarracksWork = e.BarracksWork, BarracksHp = e.BarracksHp, Builders = e.Builders, InfantryFoodCost = e.InfantryFoodCost,
                InfantryWoodCost = e.InfantryWoodCost, InfantryTrainTicks = e.InfantryTrainTicks, AutoInfantryQueue = e.AutoInfantryQueue,
                  Industry = e.Industry, BeltWoodCost = e.BeltWoodCost, BeltTicksPerCell = e.BeltTicksPerCell, BeltHp = e.BeltHp, BeltLimit = e.BeltLimit,
                  LineRebuildDelayTicks = e.LineRebuildDelayTicks, RaidLinePriority = e.RaidLinePriority,
                 BeltComponents = e.BeltComponents, SplitterWoodCost = e.SplitterWoodCost, SorterWoodCost = e.SorterWoodCost,
                 UndergroundBeltWoodCost = e.UndergroundBeltWoodCost, UndergroundBeltMaxLength = e.UndergroundBeltMaxLength,
                 FastBeltWoodCost = e.FastBeltWoodCost, FastBeltTicksPerCell = e.FastBeltTicksPerCell, FastBeltAge = e.FastBeltAge,
                 StorageSizeCells = e.StorageSizeCells, StorageWoodCost = e.StorageWoodCost, StorageWork = e.StorageWork,
                 StorageHp = e.StorageHp, StorageCapacity = e.StorageCapacity,
                 MineSizeCells = e.MineSizeCells, MineWoodCost = e.MineWoodCost, MineWork = e.MineWork, MineHp = e.MineHp, MineIntervalTicks = e.MineIntervalTicks,
                 CommonDrill = e.CommonDrill, DrillSizeCells = e.DrillSizeCells, DrillWoodCost = e.DrillWoodCost, DrillStoneCost = e.DrillStoneCost,
                 DrillWork = e.DrillWork, DrillHp = e.DrillHp, DrillIntervalTicks = e.DrillIntervalTicks,
                 Inserters = e.Inserters, InserterWoodCost = e.InserterWoodCost, InserterIntervalTicks = e.InserterIntervalTicks,
                 AiInserterCoreReserve = e.AiInserterCoreReserve,
                 SmelterSizeCells = e.SmelterSizeCells, SmelterWoodCost = e.SmelterWoodCost, SmelterWork = e.SmelterWork, SmelterHp = e.SmelterHp,
                 SmeltTicks = e.SmeltTicks, OrePerMetal = e.OrePerMetal, BufferLimit = e.BufferLimit, InfantryMetalCost = e.InfantryMetalCost,
                  ProcessingChain = e.ProcessingChain, CharcoalKilnSizeCells = e.CharcoalKilnSizeCells, CharcoalKilnWoodCost = e.CharcoalKilnWoodCost,
                 CharcoalKilnWork = e.CharcoalKilnWork, CharcoalKilnHp = e.CharcoalKilnHp, CharcoalTicks = e.CharcoalTicks,
                 SteelworksSizeCells = e.SteelworksSizeCells, SteelworksWoodCost = e.SteelworksWoodCost, SteelworksWork = e.SteelworksWork,
                 SteelworksHp = e.SteelworksHp, SteelTicks = e.SteelTicks,
                 HeavyInfantryFoodCost = e.HeavyInfantryFoodCost, HeavyInfantryWoodCost = e.HeavyInfantryWoodCost,
                 HeavyInfantrySteelCost = e.HeavyInfantrySteelCost, HeavyInfantryTrainTicks = e.HeavyInfantryTrainTicks,
                 HeavyInfantryHp = e.HeavyInfantryHp, HeavyInfantryDamage = e.HeavyInfantryDamage,
                 HeavyInfantryAttackIntervalTicks = e.HeavyInfantryAttackIntervalTicks, HeavyInfantrySpeed = e.HeavyInfantrySpeed,
                  HeavyInfantryVision = e.HeavyInfantryVision, HeavyInfantryRange = e.HeavyInfantryRange,
                  Rations = e.Rations, MillSizeCells = e.MillSizeCells, MillWoodCost = e.MillWoodCost, MillWork = e.MillWork, MillHp = e.MillHp,
                  MillTicks = e.MillTicks, MillFoodInput = e.MillFoodInput, RationsPerSoldier = e.RationsPerSoldier,
                  RationsTrainPermille = e.RationsTrainPermille, RationsHealIntervalTicks = e.RationsHealIntervalTicks,
                  RationsHealQuietTicks = e.RationsHealQuietTicks, RationsHealHp = e.RationsHealHp,
                   RationsHealMaxPerInterval = e.RationsHealMaxPerInterval, RationsFarmMinimum = e.RationsFarmMinimum,
                  RationsFoodReserve = e.RationsFoodReserve, EasyRebuild = e.EasyRebuild, MoveBuildingPauseTicks = e.MoveBuildingPauseTicks,
                  AdjacentBuildings = e.AdjacentBuildings,
                   AdjacentBuildingClearanceCells = e.AdjacentBuildingClearanceCells,
                  ResourceFog = e.ResourceFog, ExploreScouting = e.ExploreScouting,
                ExploreChunkCells = e.ExploreChunkCells, ExploreMinUnexploredPermille = e.ExploreMinUnexploredPermille,
                ExploreMinUnexploredPermilleStage2 = e.ExploreMinUnexploredPermilleStage2,
                ExploreMinUnexploredPermilleStage3 = e.ExploreMinUnexploredPermilleStage3,
                ExploreMinUnexploredPermilleStage4 = e.ExploreMinUnexploredPermilleStage4,
                  ExploreRetargetTicks = e.ExploreRetargetTicks, ExploreAvoidChunks = e.ExploreAvoidChunks,
                  ExploreAvoidTicks = e.ExploreAvoidTicks, ExploreAutoUntilTick = e.ExploreAutoUntilTick,
                  Bricks = e.Bricks, BrickKilnSizeCells = e.BrickKilnSizeCells, BrickKilnWoodCost = e.BrickKilnWoodCost,
                  BrickKilnWork = e.BrickKilnWork, BrickKilnHp = e.BrickKilnHp, BrickTicks = e.BrickTicks,
                  BrickStoneInput = e.BrickStoneInput, BrickCharcoalInput = e.BrickCharcoalInput, BricksPerWall = e.BricksPerWall,
                  BricksPerTower = e.BricksPerTower, BricksPerCastle = e.BricksPerCastle, BrickHpPermille = e.BrickHpPermille,
                  BrickUpgradeIntervalTicks = e.BrickUpgradeIntervalTicks, BrickCastleStonePermille = e.BrickCastleStonePermille,
                  BrickCoreRepairIntervalTicks = e.BrickCoreRepairIntervalTicks, BricksPerCoreRepair = e.BricksPerCoreRepair,
                  BrickCoreRepairHp = e.BrickCoreRepairHp, BrickExtraTowers = e.BrickExtraTowers,
                  Tack = e.Tack, SaddlerySizeCells = e.SaddlerySizeCells, SaddleryWoodCost = e.SaddleryWoodCost, TackWoodReserve = e.TackWoodReserve,
                  SaddleryWork = e.SaddleryWork, SaddleryHp = e.SaddleryHp, TackTicks = e.TackTicks,
                  TackFoodInput = e.TackFoodInput, TackWoodInput = e.TackWoodInput,
                  HeavyCavalryTackCost = e.HeavyCavalryTackCost, HeavyCavalryFoodCost = e.HeavyCavalryFoodCost,
                  HeavyCavalryTrainTicks = e.HeavyCavalryTrainTicks, HeavyCavalryHp = e.HeavyCavalryHp,
                  HeavyCavalryDamage = e.HeavyCavalryDamage, HeavyCavalryAttackIntervalTicks = e.HeavyCavalryAttackIntervalTicks,
                  HeavyCavalryRange = e.HeavyCavalryRange, HeavyCavalrySpeed = e.HeavyCavalrySpeed, HeavyCavalryVision = e.HeavyCavalryVision,
                  Planks = e.Planks, SawmillSizeCells = e.SawmillSizeCells, SawmillWoodCost = e.SawmillWoodCost,
                  SawmillWork = e.SawmillWork, SawmillHp = e.SawmillHp, PlankTicks = e.PlankTicks,
                  PlankWoodInput = e.PlankWoodInput, PlanksPerRam = e.PlanksPerRam, PlankRamHpPermille = e.PlankRamHpPermille,
                  PlankRamTrainPermille = e.PlankRamTrainPermille, PlanksPerBridgeCell = e.PlanksPerBridgeCell,
                  PlankBridgeWoodPermille = e.PlankBridgeWoodPermille, PlankExtraRams = e.PlankExtraRams, PlankWoodReserve = e.PlankWoodReserve,
                  CraftTree1 = e.CraftTree1, CraftTreePlankWoodInput = e.CraftTreePlankWoodInput, CraftTreePlankTicks = e.CraftTreePlankTicks,
                  CraftTreeCharcoalWoodInput = e.CraftTreeCharcoalWoodInput, CraftTreeCharcoalTicks = e.CraftTreeCharcoalTicks,
                  StonecutterSizeCells = e.StonecutterSizeCells, StonecutterWoodCost = e.StonecutterWoodCost,
                  StonecutterWork = e.StonecutterWork, StonecutterHp = e.StonecutterHp, CraftTreeStoneInput = e.CraftTreeStoneInput,
                  CraftTreeStonecutterTicks = e.CraftTreeStonecutterTicks, JoinerySizeCells = e.JoinerySizeCells,
                  JoineryWoodCost = e.JoineryWoodCost, JoineryWork = e.JoineryWork, JoineryHp = e.JoineryHp,
                  CraftTreeJoineryTicks = e.CraftTreeJoineryTicks, CraftTreeJoineryPlankInput = e.CraftTreeJoineryPlankInput,
                  CraftTreeJoineryCutStoneInput = e.CraftTreeJoineryCutStoneInput, CraftTreeFramesPerBuilding = e.CraftTreeFramesPerBuilding,
                  CraftTreeFrameWoodPermille = e.CraftTreeFrameWoodPermille, CraftTreeFrameWorkPermille = e.CraftTreeFrameWorkPermille,
                  CraftTreeLevel2FramesCost = e.CraftTreeLevel2FramesCost,
                  CraftTree2 = e.CraftTree2, AssemblerSizeCells = e.AssemblerSizeCells, AssemblerWoodCost = e.AssemblerWoodCost,
                  AssemblerWork = e.AssemblerWork, AssemblerHp = e.AssemblerHp, ArmsKitTicks = e.ArmsKitTicks,
                  ArmsKitPlankInput = e.ArmsKitPlankInput, ArmsKitCharcoalInput = e.ArmsKitCharcoalInput, ArmsKitSteelInput = e.ArmsKitSteelInput,
                  ScholarlyTicks = e.ScholarlyTicks, ScholarlyPlankInput = e.ScholarlyPlankInput, ScholarlyCharcoalInput = e.ScholarlyCharcoalInput,
                  ScholarlyFoodInput = e.ScholarlyFoodInput, ArmsKitAttack = e.ArmsKitAttack, ArmsKitHpPermille = e.ArmsKitHpPermille,
                  FineArmsKitAttack = e.FineArmsKitAttack, FineArmsKitHpPermille = e.FineArmsKitHpPermille,
                  ScholarlyPerResearch = e.ScholarlyPerResearch, ScholarlyCostPermille = e.ScholarlyCostPermille,
                  ScholarlyTimePermille = e.ScholarlyTimePermille,
                  GoodsValueArmsKit = e.GoodsValueArmsKit, GoodsValueScholarly = e.GoodsValueScholarly, GoodsValueFineArmsKit = e.GoodsValueFineArmsKit,
                  CraftTree3 = e.CraftTree3, SiegePartsTicks = e.SiegePartsTicks, SiegePartsFramesInput = e.SiegePartsFramesInput,
                  SiegePartsArmsKitInput = e.SiegePartsArmsKitInput, MechanismTicks = e.MechanismTicks, MechanismArmsKitInput = e.MechanismArmsKitInput,
                  MechanismScholarlyInput = e.MechanismScholarlyInput, MechanismCutStoneInput = e.MechanismCutStoneInput,
                  SiegePartsHpPermille = e.SiegePartsHpPermille, SiegePartsDamagePermille = e.SiegePartsDamagePermille,
                  SiegePartsPerCastle = e.SiegePartsPerCastle, SiegePartsCastleStonePermille = e.SiegePartsCastleStonePermille,
                  MechanismBuildingTimePermille = e.MechanismBuildingTimePermille, MechanismBuildingMaterialPermille = e.MechanismBuildingMaterialPermille,
                  MechanismResearchCostPermille = e.MechanismResearchCostPermille, MechanismResearchTimePermille = e.MechanismResearchTimePermille,
                  SiegePartsStockLimit = e.SiegePartsStockLimit,
                  GoodsValueSiegeParts = e.GoodsValueSiegeParts, GoodsValueMechanism = e.GoodsValueMechanism,
                  CraftTree4 = e.CraftTree4, SiegeEngineTicks = e.SiegeEngineTicks, SiegeEngineSiegePartsInput = e.SiegeEngineSiegePartsInput,
                  SiegeEngineMechanismInput = e.SiegeEngineMechanismInput, EliteKitTicks = e.EliteKitTicks, EliteKitArmsKitInput = e.EliteKitArmsKitInput,
                  EliteKitMechanismInput = e.EliteKitMechanismInput, EliteKitMetalInput = e.EliteKitMetalInput, EliteKitAttack = e.EliteKitAttack,
                  EliteKitHpPermille = e.EliteKitHpPermille, GoodsValueSiegeEngine = e.GoodsValueSiegeEngine, GoodsValueEliteKit = e.GoodsValueEliteKit,
                  Books = e.Books, ScriptoriumSizeCells = e.ScriptoriumSizeCells, ScriptoriumWoodCost = e.ScriptoriumWoodCost,
                  ScriptoriumWork = e.ScriptoriumWork, ScriptoriumHp = e.ScriptoriumHp, BookTicks = e.BookTicks,
                  BookGoldInput = e.BookGoldInput, BookWoodInput = e.BookWoodInput, BooksPerResearch = e.BooksPerResearch,
                  BookResearchPermille = e.BookResearchPermille, BookResearchCostPermille = e.BookResearchCostPermille, BookWoodReserve = e.BookWoodReserve,
                  Preserves = e.Preserves, DryingRackSizeCells = e.DryingRackSizeCells, DryingRackWoodCost = e.DryingRackWoodCost,
                  DryingRackWork = e.DryingRackWork, DryingRackHp = e.DryingRackHp, PreserveTicks = e.PreserveTicks,
                  PreserveFoodInput = e.PreserveFoodInput, PreserveFoodValue = e.PreserveFoodValue, PreserveKeepFood = e.PreserveKeepFood,
                  PreserveMaxRacks = e.PreserveMaxRacks, PreserveWoodReserve = e.PreserveWoodReserve,
                  Jewels = e.Jewels, LapidarySizeCells = e.LapidarySizeCells, LapidaryWoodCost = e.LapidaryWoodCost,
                  LapidaryWork = e.LapidaryWork, LapidaryHp = e.LapidaryHp, JewelTicks = e.JewelTicks,
                  JewelStoneInput = e.JewelStoneInput, JewelOreInput = e.JewelOreInput, JewelMaxLapidaries = e.JewelMaxLapidaries,
                  JewelKeepStone = e.JewelKeepStone, JewelWoodReserve = e.JewelWoodReserve, JewelMercenaryGems = e.JewelMercenaryGems,
                  JewelMercenaryFood = e.JewelMercenaryFood, JewelMercenaryInfantryRatio = e.JewelMercenaryInfantryRatio,
                  TradeGoods = e.TradeGoods, WeaverSizeCells = e.WeaverSizeCells, WeaverWoodCost = e.WeaverWoodCost,
                  WeaverWork = e.WeaverWork, WeaverHp = e.WeaverHp, TradeGoodsTicks = e.TradeGoodsTicks,
                  TradeGoodsWoodInput = e.TradeGoodsWoodInput, TradeGoodsKeepWood = e.TradeGoodsKeepWood,
                  TradeGoodsValue = e.TradeGoodsValue, TradeGoodsMaxWeavers = e.TradeGoodsMaxWeavers,
                  TradeGoodsWoodReserve = e.TradeGoodsWoodReserve,
                  Incense = e.Incense, IncenseSizeCells = e.IncenseSizeCells, IncenseWoodCost = e.IncenseWoodCost,
                  IncenseWork = e.IncenseWork, IncenseHp = e.IncenseHp, IncenseTicks = e.IncenseTicks,
                  IncenseWoodInput = e.IncenseWoodInput, IncenseFoodInput = e.IncenseFoodInput,
                  IncenseKeepWood = e.IncenseKeepWood, IncenseKeepFood = e.IncenseKeepFood, IncenseStockLimit = e.IncenseStockLimit,
                  IncenseHealIntervalTicks = e.IncenseHealIntervalTicks, IncenseHealHp = e.IncenseHealHp,
                  IncenseMaxHouses = e.IncenseMaxHouses, IncenseWoodReserve = e.IncenseWoodReserve,
                  Charms = e.Charms, CharmSizeCells = e.CharmSizeCells, CharmWoodCost = e.CharmWoodCost,
                  CharmWork = e.CharmWork, CharmHp = e.CharmHp, CharmTicks = e.CharmTicks, CharmWoodInput = e.CharmWoodInput,
                  CharmKeepWood = e.CharmKeepWood, CharmStockLimit = e.CharmStockLimit, CharmMaxWorkshops = e.CharmMaxWorkshops,
                  CharmWoodReserve = e.CharmWoodReserve, CharmHpPermille = e.CharmHpPermille,
                  RepairKits = e.RepairKits, RepairYardSizeCells = e.RepairYardSizeCells, RepairYardWoodCost = e.RepairYardWoodCost,
                  RepairYardWork = e.RepairYardWork, RepairYardHp = e.RepairYardHp, RepairKitTicks = e.RepairKitTicks,
                  RepairKitWoodInput = e.RepairKitWoodInput, RepairKitKeepWood = e.RepairKitKeepWood, RepairKitStockLimit = e.RepairKitStockLimit,
                  RepairKitMaxYards = e.RepairKitMaxYards, RepairKitWoodReserve = e.RepairKitWoodReserve,
                  RepairKitIntervalTicks = e.RepairKitIntervalTicks, RepairKitHp = e.RepairKitHp,
                  Tools = e.Tools, ToolShopSizeCells = e.ToolShopSizeCells, ToolShopWoodCost = e.ToolShopWoodCost,
                  ToolShopWork = e.ToolShopWork, ToolShopHp = e.ToolShopHp, ToolTicks = e.ToolTicks,
                  ToolWoodInput = e.ToolWoodInput, ToolKeepWood = e.ToolKeepWood, ToolStockLimit = e.ToolStockLimit,
                  ToolMaxShops = e.ToolMaxShops, ToolWoodReserve = e.ToolWoodReserve,
                   ToolVillagerFoodPermille = e.ToolVillagerFoodPermille, ToolVillagerTicksPermille = e.ToolVillagerTicksPermille,
                   GoodsMarket = e.GoodsMarket, GoodsTradeLot = e.GoodsTradeLot, GoodsAutoSellAt = e.GoodsAutoSellAt,
                   GoodsAutoSellIntervalTicks = e.GoodsAutoSellIntervalTicks, GoodsAutoKeep = e.GoodsAutoKeep, GoodsMarketWoodReserve = e.GoodsMarketWoodReserve,
                   GoodsValueSteel = e.GoodsValueSteel, GoodsValueBowGear = e.GoodsValueBowGear, GoodsValueRations = e.GoodsValueRations,
                   GoodsValueBricks = e.GoodsValueBricks, GoodsValueTack = e.GoodsValueTack, GoodsValuePlanks = e.GoodsValuePlanks,
                   GoodsValueBooks = e.GoodsValueBooks, GoodsValueIncense = e.GoodsValueIncense, GoodsValueCharms = e.GoodsValueCharms,
                   GoodsValueRepairKits = e.GoodsValueRepairKits, GoodsValueTools = e.GoodsValueTools,
                   CraftTiming = e.CraftTiming, CraftTimingFoodPerWood = e.CraftTimingFoodPerWood,
                   CraftBalance = e.CraftBalance, CraftBalanceTollgateTiming = e.CraftBalanceTollgateTiming,
                Ages = e.Ages, AdvanceFoodCost = e.AdvanceFoodCost, AdvanceWoodCost = e.AdvanceWoodCost, AdvanceTicks = e.AdvanceTicks,
                 AgeFour = e.AgeFour, AgeStrengths = e.AgeStrengths, Age4FoodCost = e.Age4FoodCost, Age4WoodCost = e.Age4WoodCost, Age4Ticks = e.Age4Ticks, Age4PopulationBonus = e.Age4PopulationBonus,
                 AgrarianFarmSpeedPermille = e.AgrarianFarmSpeedPermille, AgrarianRationsHealHp = e.AgrarianRationsHealHp, AgrarianPopulationCapBonus = e.AgrarianPopulationCapBonus,
                 MetallurgySmeltSpeedPermille = e.MetallurgySmeltSpeedPermille, MetallurgyForgedDamageBonus = e.MetallurgyForgedDamageBonus, MetallurgySiegePartsTimePermille = e.MetallurgySiegePartsTimePermille,
                 ForestryLumberSpeedPermille = e.ForestryLumberSpeedPermille, ForestryArcherRangeBonus = e.ForestryArcherRangeBonus, ForestryBuildingWoodPermille = e.ForestryBuildingWoodPermille,
                 MasonryWallHpPermille = e.MasonryWallHpPermille, MasonryTowerDamageBonus = e.MasonryTowerDamageBonus, MasonryCoreHpPermille = e.MasonryCoreHpPermille,
                 CaravanTradeGoodsValue = e.CaravanTradeGoodsValue, CaravanMarketReturnPermille = e.CaravanMarketReturnPermille, CaravanRewardPermille = e.CaravanRewardPermille,
                 CavalryStableTimePermille = e.CavalryStableTimePermille, CavalrySpeedPermille = e.CavalrySpeedPermille, CavalryHeavyHpPermille = e.CavalryHeavyHpPermille,
                 BridgeBuildingWorkPermille = e.BridgeBuildingWorkPermille, BridgeRamSiegeDamagePermille = e.BridgeRamSiegeDamagePermille, BridgeGeneralWorkPermille = e.BridgeGeneralWorkPermille,
                 AcademyResearchTimePermille = e.AcademyResearchTimePermille, AcademyScholarlyCostPermille = e.AcademyScholarlyCostPermille, AcademyAge4ResearchCostPermille = e.AcademyAge4ResearchCostPermille,
                 CultConversionTimePermille = e.CultConversionTimePermille, CultIncenseHealHp = e.CultIncenseHealHp, CultMonkHpPermille = e.CultMonkHpPermille,
                 FishingGatherTimePermille = e.FishingGatherTimePermille, FishingPreserveFoodValue = e.FishingPreserveFoodValue, FishingFoodCarryBonus = e.FishingFoodCarryBonus,
                 MountainMiningTimePermille = e.MountainMiningTimePermille, MountainMercenaryGems = e.MountainMercenaryGems, MountainMercenaryDamageBonus = e.MountainMercenaryDamageBonus,
                 TollgateFeePermille = e.TollgateFeePermille, TollgateHpPermille = e.TollgateHpPermille, TollgateRepairKitHp = e.TollgateRepairKitHp,
                 MetropolisVillagerTimePermille = e.MetropolisVillagerTimePermille, MetropolisTownLimitBonus = e.MetropolisTownLimitBonus, MetropolisToolTimePermille = e.MetropolisToolTimePermille,
                 SanctuaryPilgrimagePermille = e.SanctuaryPilgrimagePermille, SanctuaryCharmHpPermille = e.SanctuaryCharmHpPermille, SanctuaryCharmHpAge4Permille = e.SanctuaryCharmHpAge4Permille,
                TotalWarFoodCost = e.TotalWarFoodCost, TotalWarWoodCost = e.TotalWarWoodCost, TotalWarMetalCost = e.TotalWarMetalCost, TotalWarTicks = e.TotalWarTicks,
                GrandWorkshopFoodCost = e.GrandWorkshopFoodCost, GrandWorkshopWoodCost = e.GrandWorkshopWoodCost, GrandWorkshopMetalCost = e.GrandWorkshopMetalCost, GrandWorkshopTicks = e.GrandWorkshopTicks,
                 AgeClock = e.AgeClock, AgeClockTicks1 = e.AgeClockTicks1, AgeClockTicks2 = e.AgeClockTicks2, AgeClockTicks3 = e.AgeClockTicks3, AgeClockTicks4 = e.AgeClockTicks4,
                 AgeClockUx = e.AgeClockUx, AgeClockUxChoiceTicks = e.AgeClockUxChoiceTicks,
                EarlyArms = e.EarlyArms, EarlyGatherIntervalTicks = e.EarlyGatherIntervalTicks,
                CivVariety = e.CivVariety, CivVarietyWindowPermille = e.CivVarietyWindowPermille,
                CivVarietyCaps = e.CivVarietyCaps, CivVarietyCapMetallurgy = e.CivVarietyCapMetallurgy,
                CivVarietyCapAgrarian = e.CivVarietyCapAgrarian,
                EarlyAdvanceFoodCost = e.EarlyAdvanceFoodCost, EarlyAdvanceWoodCost = e.EarlyAdvanceWoodCost, EarlyAdvanceTicks = e.EarlyAdvanceTicks,
                EarlyAge2FoodCost = e.EarlyAge2FoodCost, EarlyAge2WoodCost = e.EarlyAge2WoodCost, EarlyAge2Ticks = e.EarlyAge2Ticks,
                EarlyAdvanceVillagers = e.EarlyAdvanceVillagers, EarlyAge2Villagers = e.EarlyAge2Villagers,
                AgrarianInfantryFood = e.AgrarianInfantryFood, AgrarianInfantryWood = e.AgrarianInfantryWood, AgrarianInfantryTicks = e.AgrarianInfantryTicks,
                ForgedInfantryHp = e.ForgedInfantryHp, ForgedInfantryDamage = e.ForgedInfantryDamage,
                FarmSizeCells = e.FarmSizeCells, FarmWoodCost = e.FarmWoodCost, FarmWork = e.FarmWork, FarmHp = e.FarmHp,
                FarmBaseTicks = e.FarmBaseTicks, FarmStepTicks = e.FarmStepTicks, FarmMinTicks = e.FarmMinTicks, FarmFoodReach = e.FarmFoodReach, FarmRiverReach = e.FarmRiverReach,
                  Forestry = e.Forestry, Masonry = e.Masonry, Caravan = e.Caravan, Cavalry = e.Cavalry, Bridge = e.Bridge, Academy = e.Academy, Cult = e.Cult, Metropolis = e.Metropolis, Sanctuary = e.Sanctuary, Towns = e.Towns,
                 EngineerCampSizeCells = e.EngineerCampSizeCells, EngineerCampWoodCost = e.EngineerCampWoodCost,
                 EngineerCampWork = e.EngineerCampWork, EngineerCampHp = e.EngineerCampHp,
                  BridgeWoodCost = e.BridgeWoodCost, BridgeWork = e.BridgeWork, BridgeHp = e.BridgeHp, MaxBridgeLength = e.MaxBridgeLength,
                  BridgeworksFoodCost = e.BridgeworksFoodCost, BridgeworksWoodCost = e.BridgeworksWoodCost, BridgeworksTicks = e.BridgeworksTicks,
                  BridgeworksHpBonus = e.BridgeworksHpBonus, BridgeworksWorkReduction = e.BridgeworksWorkReduction,
                  SiegeDeploymentFoodCost = e.SiegeDeploymentFoodCost, SiegeDeploymentWoodCost = e.SiegeDeploymentWoodCost, SiegeDeploymentTicks = e.SiegeDeploymentTicks,
                  SiegeDeploymentRamTicksReduction = e.SiegeDeploymentRamTicksReduction, SiegeDeploymentRamCapacityBonus = e.SiegeDeploymentRamCapacityBonus,
                 QuarrySizeCells = e.QuarrySizeCells, QuarryWoodCost = e.QuarryWoodCost,
                 QuarryWork = e.QuarryWork, QuarryHp = e.QuarryHp, QuarryIntervalTicks = e.QuarryIntervalTicks,
                 LumberCampSizeCells = e.LumberCampSizeCells, LumberCampWoodCost = e.LumberCampWoodCost,
                LumberCampWork = e.LumberCampWork, LumberCampHp = e.LumberCampHp, LumberCampIntervalTicks = e.LumberCampIntervalTicks,
                MarketFoodFloor = e.MarketFoodFloor, MarketWoodReserve = e.MarketWoodReserve, MarketStoneReserve = e.MarketStoneReserve,
                CaravanseraiSizeCells = e.CaravanseraiSizeCells, CaravanseraiWoodCost = e.CaravanseraiWoodCost, CaravanseraiWork = e.CaravanseraiWork,
                 CaravanseraiHp = e.CaravanseraiHp, CaravanOutpostReach = e.CaravanOutpostReach, CaravanMinimumDistance = e.CaravanMinimumDistance,
                 CaravanRewardDistanceStep = e.CaravanRewardDistanceStep, CaravanRewardMaxWood = e.CaravanRewardMaxWood, CaravanAutoVillagers = e.CaravanAutoVillagers,
                 AcademySizeCells = e.AcademySizeCells, AcademyWoodCost = e.AcademyWoodCost, AcademyWork = e.AcademyWork, AcademyHp = e.AcademyHp,
                 AcademyToolsFoodCost = e.AcademyToolsFoodCost, AcademyToolsWoodCost = e.AcademyToolsWoodCost, AcademyToolsGoldCost = e.AcademyToolsGoldCost,
                 AcademyToolsTicks = e.AcademyToolsTicks, AcademyCartsFoodCost = e.AcademyCartsFoodCost, AcademyCartsWoodCost = e.AcademyCartsWoodCost,
                 AcademyCartsGoldCost = e.AcademyCartsGoldCost, AcademyCartsTicks = e.AcademyCartsTicks,
                 ShrineSizeCells = e.ShrineSizeCells, ShrineWoodCost = e.ShrineWoodCost, ShrineStoneCost = e.ShrineStoneCost,
                 ShrineWork = e.ShrineWork, ShrineHp = e.ShrineHp, ShrineOutpostReach = e.ShrineOutpostReach,
                 SanctuaryAttackBonusPermille = e.SanctuaryAttackBonusPermille, SanctuaryMaxBonusPermille = e.SanctuaryMaxBonusPermille,
                 SanctuaryPilgrimageFoodCost = e.SanctuaryPilgrimageFoodCost, SanctuaryPilgrimageWoodCost = e.SanctuaryPilgrimageWoodCost,
                 SanctuaryPilgrimageTicks = e.SanctuaryPilgrimageTicks, SanctuaryRelicFoodCost = e.SanctuaryRelicFoodCost,
                 SanctuaryRelicWoodCost = e.SanctuaryRelicWoodCost, SanctuaryRelicTicks = e.SanctuaryRelicTicks,
                 SanctuaryPilgrimageRadius = e.SanctuaryPilgrimageRadius, SanctuaryPilgrimageIntervalTicks = e.SanctuaryPilgrimageIntervalTicks,
                 SanctuaryPilgrimageHeal = e.SanctuaryPilgrimageHeal, SanctuaryRelicBonusPermille = e.SanctuaryRelicBonusPermille,
                 SanctuaryRelicMaxBonusPermille = e.SanctuaryRelicMaxBonusPermille,
                  MonasterySizeCells = e.MonasterySizeCells, MonasteryWoodCost = e.MonasteryWoodCost, MonasteryWork = e.MonasteryWork, MonasteryHp = e.MonasteryHp,
                  MonasteryMonkFoodCost = e.MonasteryMonkFoodCost, MonasteryMonkWoodCost = e.MonasteryMonkWoodCost,
                  SermonFoodCost = e.SermonFoodCost, SermonWoodCost = e.SermonWoodCost, SermonTicks = e.SermonTicks,
                  MartyrBlessingFoodCost = e.MartyrBlessingFoodCost, MartyrBlessingWoodCost = e.MartyrBlessingWoodCost,
                  MartyrBlessingTicks = e.MartyrBlessingTicks, MartyrBlessingHpPermille = e.MartyrBlessingHpPermille,
                 Mountain = e.Mountain, MountainSizeCells = e.MountainSizeCells, MountainWoodCost = e.MountainWoodCost,
                  MountainWork = e.MountainWork, MountainHp = e.MountainHp, MountainBaseIntervalTicks = e.MountainBaseIntervalTicks,
                 MountainIntervalStepTicks = e.MountainIntervalStepTicks, MountainMinIntervalTicks = e.MountainMinIntervalTicks,
                 MountainMaxBuildings = e.MountainMaxBuildings, MountainMaxAdjacentCells = e.MountainMaxAdjacentCells,
                 MountainStoneYield = e.MountainStoneYield, MountainOreYield = e.MountainOreYield,
                 FishingCiv = e.FishingCiv, HarborSizeCells = e.HarborSizeCells, HarborWoodCost = e.HarborWoodCost,
                  HarborWork = e.HarborWork, HarborHp = e.HarborHp,
                  FishingNetFoodCost = e.FishingNetFoodCost, FishingNetWoodCost = e.FishingNetWoodCost, FishingNetTicks = e.FishingNetTicks,
                  DriedFishFoodCost = e.DriedFishFoodCost, DriedFishWoodCost = e.DriedFishWoodCost, DriedFishTicks = e.DriedFishTicks,
                  FishingNetCarryBonusPermille = e.FishingNetCarryBonusPermille, DriedFishRegrowIntervalPermille = e.DriedFishRegrowIntervalPermille,
                 MountainDeepShaftFoodCost = e.MountainDeepShaftFoodCost, MountainDeepShaftWoodCost = e.MountainDeepShaftWoodCost,
                 MountainDeepShaftTicks = e.MountainDeepShaftTicks, MountainFortFoodCost = e.MountainFortFoodCost,
                 MountainFortWoodCost = e.MountainFortWoodCost, MountainFortTicks = e.MountainFortTicks,
                 MountainDeepShaftIntervalPermille = e.MountainDeepShaftIntervalPermille,
                 MountainDeepShaftMaxBuildingsBonus = e.MountainDeepShaftMaxBuildingsBonus,
                 MountainFortHpPermille = e.MountainFortHpPermille, MountainFortRangeBonus = e.MountainFortRangeBonus,
                  GrandHouseSizeCells = e.GrandHouseSizeCells, GrandHouseWoodCost = e.GrandHouseWoodCost, GrandHouseWork = e.GrandHouseWork, GrandHouseHp = e.GrandHouseHp,
                  MetropolisMarketFoodCost = e.MetropolisMarketFoodCost, MetropolisMarketWoodCost = e.MetropolisMarketWoodCost, MetropolisMarketTicks = e.MetropolisMarketTicks,
                  MetropolisMilitiaFoodCost = e.MetropolisMilitiaFoodCost, MetropolisMilitiaWoodCost = e.MetropolisMilitiaWoodCost, MetropolisMilitiaTicks = e.MetropolisMilitiaTicks,
                   MetropolisMarketVillagerStep = e.MetropolisMarketVillagerStep, MetropolisMarketBonusPermille = e.MetropolisMarketBonusPermille,
                   MetropolisMarketMaxBonusPermille = e.MetropolisMarketMaxBonusPermille, MetropolisMilitiaDamage = e.MetropolisMilitiaDamage,
                   MetropolisMilitiaCoreRadius = e.MetropolisMilitiaCoreRadius,
                   TownSizeCells = e.TownSizeCells, TownWoodCost = e.TownWoodCost, TownStoneCost = e.TownStoneCost,
                   TownWork = e.TownWork, TownHp = e.TownHp, TownMaxBuildings = e.TownMaxBuildings,
                   TownCoreDistance = e.TownCoreDistance, TownResourceReach = e.TownResourceReach,
                   Tollgate = e.Tollgate, TollgateLengthCells = e.TollgateLengthCells, TollgateWoodCost = e.TollgateWoodCost,
                   TollgateStoneCost = e.TollgateStoneCost, TollgateWork = e.TollgateWork, TollgateHp = e.TollgateHp,
                   TollgateMaxBuildings = e.TollgateMaxBuildings, TollgateFeeRadiusMeters = e.TollgateFeeRadiusMeters,
                   TollgateFeeIntervalTicks = e.TollgateFeeIntervalTicks, TollgateWoodPerEnemy = e.TollgateWoodPerEnemy,
                   TollgateFoodPerEnemy = e.TollgateFoodPerEnemy, TollgateNetworkRadiusMeters = e.TollgateNetworkRadiusMeters,
                   TollgateNetworkFeeBonusPermille = e.TollgateNetworkFeeBonusPermille,
                   TollgateGateDefenceFoodCost = e.TollgateGateDefenceFoodCost, TollgateGateDefenceWoodCost = e.TollgateGateDefenceWoodCost,
                   TollgateGateDefenceTicks = e.TollgateGateDefenceTicks, TollgateGateNetworkFoodCost = e.TollgateGateNetworkFoodCost,
                   TollgateGateNetworkWoodCost = e.TollgateGateNetworkWoodCost, TollgateGateNetworkTicks = e.TollgateGateNetworkTicks,
                   TollgateGateDefenceHpPermille = e.TollgateGateDefenceHpPermille,
                   TollgateGateDefenceDamageReductionPermille = e.TollgateGateDefenceDamageReductionPermille,
                   TollgateGateNetworkMaxBuildingsBonus = e.TollgateGateNetworkMaxBuildingsBonus,
                FletcherSizeCells = e.FletcherSizeCells, FletcherWoodCost = e.FletcherWoodCost, FletcherWork = e.FletcherWork,
                FletcherHp = e.FletcherHp, FletcherTicks = e.FletcherTicks, FletcherWoodInput = e.FletcherWoodInput, FletcherFoodInput = e.FletcherFoodInput,
                SkirmishArcherFoodCost = e.SkirmishArcherFoodCost, SkirmishArcherBowGearCost = e.SkirmishArcherBowGearCost,
                SkirmishArcherTrainTicks = e.SkirmishArcherTrainTicks, SkirmishArcherHp = e.SkirmishArcherHp, SkirmishArcherDamage = e.SkirmishArcherDamage,
                SkirmishArcherAttackIntervalTicks = e.SkirmishArcherAttackIntervalTicks, SkirmishArcherSpeed = e.SkirmishArcherSpeed,
                SkirmishArcherVision = e.SkirmishArcherVision, SkirmishArcherRange = e.SkirmishArcherRange,
                ScoutFoodCost = e.ScoutFoodCost, ScoutWoodCost = e.ScoutWoodCost, ScoutTrainTicks = e.ScoutTrainTicks,
                BasePopulation = e.BasePopulation, HousePopulation = e.HousePopulation,
                HouseSizeCells = e.HouseSizeCells, HouseWoodCost = e.HouseWoodCost, HouseWork = e.HouseWork, HouseHp = e.HouseHp,
                DropSiteSizeCells = e.DropSiteSizeCells, DropSiteWoodCost = e.DropSiteWoodCost, DropSiteWork = e.DropSiteWork, DropSiteHp = e.DropSiteHp,
                DropSiteLimit = e.DropSiteLimit, DropSiteMax = e.DropSiteMax, AiBelts = e.AiBelts, AiBeltWoodReserve = e.AiBeltWoodReserve,
                StorageFeedsProcessing = e.StorageFeedsProcessing,
                  WallStoneCost = e.WallStoneCost, WallHp = e.WallHp, WallReach = e.WallReach,
                  MasonryDefenceCostPermille = e.MasonryDefenceCostPermille, MasonryDefenceWorkPermille = e.MasonryDefenceWorkPermille,
                  CoreDefence = e.CoreDefence, CoreDefenceRange = e.CoreDefenceRange, CoreDefenceDamage = e.CoreDefenceDamage,
                  CoreDefenceIntervalTicks = e.CoreDefenceIntervalTicks, CoreDefenceMaxTargets = e.CoreDefenceMaxTargets,
                  StartStone = e.StartStone, StartMetal = e.StartMetal,
                TowerSizeCells = e.TowerSizeCells, TowerWoodCost = e.TowerWoodCost, TowerStoneCost = e.TowerStoneCost, TowerWork = e.TowerWork, TowerHp = e.TowerHp,
                TowerRange = e.TowerRange, TowerVision = e.TowerVision, TowerDamage = e.TowerDamage, TowerIntervalTicks = e.TowerIntervalTicks,
                BlacksmithSizeCells = e.BlacksmithSizeCells, BlacksmithWoodCost = e.BlacksmithWoodCost, BlacksmithWork = e.BlacksmithWork, BlacksmithHp = e.BlacksmithHp,
                TechFood = techFood, TechWood = techWood, TechTicks = techTicks,
                TechMetal = techMetal, TechGems = techGems, SteelWeaponsDamage = e.SteelWeaponsDamage, SteelArmourHp = e.SteelArmourHp,
                GemArmorHp = e.GemArmorHp,
                WeaponsDamage = e.WeaponsDamage, ArmourHp = e.ArmourHp, ToolsGatherTicks = e.ToolsGatherTicks, CartsCarry = e.CartsCarry,
                IrrigationTicks = e.IrrigationTicks, BlastFurnaceTicks = e.BlastFurnaceTicks,
                Age2FoodCost = e.Age2FoodCost, Age2WoodCost = e.Age2WoodCost, Age2Ticks = e.Age2Ticks, Age2PopulationBonus = e.Age2PopulationBonus,
                Age2SaveArmyFloor = e.Age2SaveArmyFloor,
                Age3FoodCost = e.Age3FoodCost, Age3WoodCost = e.Age3WoodCost, Age3Ticks = e.Age3Ticks, Age3PopulationBonus = e.Age3PopulationBonus,
                AgeVictoryEnabled = e.AgeVictoryEnabled, AgeVictoryTicks = e.AgeVictoryTicks,
                SiegecraftSiegeDamage = e.SiegecraftSiegeDamage, MasonryTowerDamage = e.MasonryTowerDamage, BankingTradeReturn = e.BankingTradeReturn,
                RangeSizeCells = e.RangeSizeCells, RangeWoodCost = e.RangeWoodCost, RangeWork = e.RangeWork, RangeHp = e.RangeHp,
                StableSizeCells = e.StableSizeCells, StableWoodCost = e.StableWoodCost, StableWork = e.StableWork, StableHp = e.StableHp,
                CounterBonusPermille = e.CounterBonusPermille, RepairHpPerTick = e.RepairHpPerTick, RepairAtPermille = e.RepairAtPermille,
                CastleSizeCells = e.CastleSizeCells, CastleWoodCost = e.CastleWoodCost, CastleStoneCost = e.CastleStoneCost, CastleWork = e.CastleWork,
                CastleHp = e.CastleHp, CastleRange = e.CastleRange, CastleVision = e.CastleVision, CastleDamage = e.CastleDamage,
                CastleIntervalTicks = e.CastleIntervalTicks,
                MercenaryGems = e.MercenaryGems, MercenaryTicks = e.MercenaryTicks, MercenaryHp = e.MercenaryHp,
                MercenaryDamage = e.MercenaryDamage, MercenaryInterval = e.MercenaryInterval,
                ArcherFood = e.ArcherFood, ArcherWood = e.ArcherWood, ArcherTicks = e.ArcherTicks, ArcherHp = e.ArcherHp, ArcherDamage = e.ArcherDamage,
                ArcherInterval = e.ArcherInterval, ArcherRange = e.ArcherRange, ArcherSpeed = e.ArcherSpeed, ArcherVision = e.ArcherVision,
                CavalryFood = e.CavalryFood, CavalryWood = e.CavalryWood, CavalryMetal = e.CavalryMetal, CavalryTicks = e.CavalryTicks, CavalryHp = e.CavalryHp,
                CavalryDamage = e.CavalryDamage, CavalryInterval = e.CavalryInterval, CavalryRange = e.CavalryRange, CavalrySpeed = e.CavalrySpeed, CavalryVision = e.CavalryVision,
                LightCavalryFood = e.LightCavalryFood, LightCavalryWood = e.LightCavalryWood, LightCavalryTicks = e.LightCavalryTicks,
                LightCavalryHp = e.LightCavalryHp, LightCavalryDamage = e.LightCavalryDamage, LightCavalryInterval = e.LightCavalryInterval,
                LightCavalryRange = e.LightCavalryRange, LightCavalrySpeed = e.LightCavalrySpeed, LightCavalryVision = e.LightCavalryVision,
                CavalryDrillFood = e.CavalryDrillFood, CavalryDrillWood = e.CavalryDrillWood, CavalryDrillTicks = e.CavalryDrillTicks,
                CavalryDrillSpeed = e.CavalryDrillSpeed,
                MarketSizeCells = e.MarketSizeCells, MarketWoodCost = e.MarketWoodCost, MarketWork = e.MarketWork, MarketHp = e.MarketHp, TradeLot = e.TradeLot, TradeReturn = e.TradeReturn,
                GemsTradeReturn = e.GemsTradeReturn,
                TradeRouteWood = e.TradeRouteWood, TradeRouteMin = e.TradeRouteMin,
                WorkshopSizeCells = e.WorkshopSizeCells, WorkshopWoodCost = e.WorkshopWoodCost, WorkshopWork = e.WorkshopWork, WorkshopHp = e.WorkshopHp,
                RamFood = e.RamFood, RamWood = e.RamWood, RamTicks = e.RamTicks, RamHp = e.RamHp, RamDamage = e.RamDamage, RamSiegeDamage = e.RamSiegeDamage,
                 RamInterval = e.RamInterval, RamRange = e.RamRange, RamSpeed = e.RamSpeed, RamVision = e.RamVision,
                 CannonSiegeEngineCost = e.CannonSiegeEngineCost, CannonFoodCost = e.CannonFoodCost, CannonWoodCost = e.CannonWoodCost,
                 CannonHp = e.CannonHp, CannonDamage = e.CannonDamage, CannonSiegeDamage = e.CannonSiegeDamage, CannonInterval = e.CannonInterval,
                 CannonRange = e.CannonRange, CannonSpeed = e.CannonSpeed, CannonVision = e.CannonVision, CannonAutoLimit = e.CannonAutoLimit,
                 MonksEnabled = e.MonksEnabled, ConversionTicks = e.ConversionTicks, MonkFoodCost = e.MonkFoodCost,
                 MonkGoldCost = e.MonkGoldCost, MonkTrainTicks = e.MonkTrainTicks,
                 FishingEnabled = e.FishingEnabled, FishRegrowTicks = e.FishRegrowTicks,
                 FishAgrarianBonusPermille = e.FishAgrarianBonusPermille, FishReach = e.FishReach,
                  GoldEnabled = e.GoldEnabled, Age3GoldCostAgrarian = e.Age3GoldCostAgrarian, Age3GoldCostMetallurgy = e.Age3GoldCostMetallurgy, GoldGatherers = e.GoldGatherers,
                 GoldAmount = e.GoldAmount, GoldDangerMeters = e.GoldDangerMeters };
        }

        private static ScenarioExtensionData[] Copy(ScenarioExtensionData[] source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var copy = new ScenarioExtensionData[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] == null) throw new ArgumentException("Missing scenario extension.", nameof(source));
                copy[i] = new ScenarioExtensionData { Id = source[i].Id, Version = source[i].Version,
                    Data = source[i].Data == null ? null : (byte[])source[i].Data.Clone() };
            }
            return copy;
        }

        private bool NearRiver(SimPoint point, int reachMeters)
        {
            long reach = Fix64.FromInt(reachMeters).Raw;
            long limit = checked(reach * reach);
            var terrain = Config.Map.Terrain;
            for (int cell = 0; cell < terrain.Length; cell++)
            {
                if (terrain[cell] != (byte)TerrainKind.River) continue;
                var centre = Map.Center(cell);
                long dx = checked(point.X.Raw - centre.X.Raw), dz = checked(point.Z.Raw - centre.Z.Raw);
                if (checked(dx * dx + dz * dz) <= limit) return true;
            }
            return false;
        }

        internal static void ValidatePoint(SimPoint p, MapDefinition map) => Require(
            p.X.Raw >= 0 && p.X <= Fix64.FromInt(map.WidthMeters) && p.Z.Raw >= 0 && p.Z <= Fix64.FromInt(map.HeightMeters),
            "Point is outside the map.");
        private static bool ValidTechTicks(int[] values)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Length; i++)
                if (values[i] <= 0 && (values.Length < 29 || (i < 12 || i == (int)TechKind.TotalWar - 1 || i == (int)TechKind.GrandWorkshop - 1))) return false;
            return true;
        }

        private static T[] Copy<T>(T[] a) => a == null ? throw new ArgumentException("Missing scenario array.") : (T[])a.Clone();
        private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
    }
}
