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
        /// <summary>V3-5 #22: transient conversion progress; never serialized into a scenario or replay state.</summary>
        internal int ConversionProgress;
        internal uint ConversionByFaction;
        internal long ConversionLastAttackTick;
        /// <summary>V3-13 #2: the observation contact currently retained by an autonomous cult monk.</summary>
        internal uint CultTargetContactId;
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
        internal ObjectiveMemory[] Objectives;
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
        internal int Hp, Progress, Queued;
        /// <summary>V3-11 #2: tick at which an automatic bridge was destroyed; 0 means it has not been destroyed.</summary>
        internal long DestroyedTick;
        internal long TrainRemaining;
        /// <summary>V3-2 mine and smelter (12.2): output side, items at the input and output, and the work clock
        /// (mine: ticks towards the next ore; smelter: ticks left on the metal being made, 0 when idle).</summary>
        internal Facing Facing;
        internal int Input, InputSecondary, Output, Timer;
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
    }

    /// <summary>V3-6: one automatic processing chain. The belt arrays are the canonical route chosen for this line.</summary>
    internal struct ProcessingLineState
    {
        internal uint Id, FactionId;
        internal ProcessingLineKind Kind;
        internal LineManager Manager;
        internal uint MineId, SmelterId, KilnId, SteelworksId, LumberCampId, FletcherId;
        internal int[] BeltCells;
        internal Facing[] BeltFacings;
    }

    internal enum ProcessingLineKind : byte { CoreMetal = 1, Steel = 2, CoreWood = 3, BowGear = 4 }

    internal struct FactionEconomy
    {
        internal int Food, Wood;
        /// <summary>V3-2 stock; always 0 without industry. Stone, Gems and Gold (V3-5) only with ages; Charcoal and Steel (V3-6) only with the processing chain.</summary>
        internal int Ore, Metal, Stone, Gems, Gold, Charcoal, Steel, BowGear;
        /// <summary>Villagers paid for and waiting at the core; the first one trains for TrainRemaining more ticks.</summary>
        internal int Queued;
        internal long TrainRemaining;
        /// <summary>The player turned the automatic economy off (it is on by default).</summary>
        internal bool AutoOff;
        /// <summary>V3-3 (19): the player trained or cancelled at the core; the automatic economy trains no villagers.</summary>
        internal bool CoreHeld;
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
    }

    internal sealed class WorldState
    {
        internal readonly ScenarioDefinition Config;
        internal SoldierState[] Soldiers;
        internal readonly ArmyState[] Armies;
        internal readonly CoreState[] Cores;
        internal readonly OutpostState[] Outposts;
        internal readonly GridMap Map;
        internal readonly FactionState[] Factions;
        internal int[] SoldierTraversal;
        internal readonly int[] ArmyTraversal;
        internal readonly SplitMix64 CombatRandom, AiRandom;
        internal uint NextSoldierId;
        internal int SoldierCount => checked((int)(NextSoldierId - 1));
        internal readonly uint NextArmyId, NextCoreId, NextOutpostId, NextFactionId;
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
            var armies = new System.Collections.Generic.List<int>();
            for (int f = 0; f < 2; f++)
            {
                var d = Config.Factions[f];
                int cellCount = checked(Config.Map.WidthCells * Config.Map.HeightCells);
                Factions[f] = new FactionState { Id = d.Id, CoreId = d.CoreId, ArmyIds = d.ArmyIds,
                    ContactIds = new uint[Soldiers.Length], ContactPositions = new SimPoint[Soldiers.Length],
                    ContactLastSeenTicks = new long[Soldiers.Length], ContactAbsent = new bool[Soldiers.Length],
                    CultObservedContactIds = new uint[Soldiers.Length], CultObservedKinds = new UnitKind[Soldiers.Length],
                    CultObservedLastSeenTicks = new long[Soldiers.Length], CultObservedValidUntilTicks = new long[Soldiers.Length],
                    VisibleCells = new bool[cellCount], ExploredCells = new bool[cellCount],
                    Objectives = new ObjectiveMemory[Cores.Length + Outposts.Length], NextContactId = 1, NextArmyContactId = 1, ArmyContacts = new ArmyContactMemory[Armies.Length] };
                foreach (uint id in d.ArmyIds)
                {
                    var ids = new System.Collections.Generic.List<uint>();
                    for (int i = 0; i < Soldiers.Length; i++)
                        if (Soldiers[i].Initial.ArmyId == id) { ids.Add((uint)i + 1); soldiers.Add(i); }
                    Armies[id - 1] = new ArmyState { Definition = Config.Armies[id - 1], SoldierIds = ids.ToArray(), AutoStartIds = ids.FindAll(id => Soldiers[id - 1].Alive).ToArray(), Path = Array.Empty<int>() };
                    armies.Add((int)id - 1);
                }
            }
            SoldierTraversal = soldiers.ToArray();
            ArmyTraversal = armies.ToArray();
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
            for (int f = 0; f < 2; f++) Economies[f] = new FactionEconomy { Food = e.StartFood, Wood = e.StartWood, Stone = e.Ages ? e.StartStone : 0, Metal = e.Ages ? e.StartMetal : 0, Gems = 0, Gold = 0, Charcoal = 0, Steel = 0, BowGear = 0 };
            VillagerStep = Fix64.FromRaw(e.VillagerSpeed.Raw / 20);
            if (e.Industry)
            {
                Belts = new BeltState[checked(Config.Map.WidthCells * Config.Map.HeightCells)];
                foreach (var b in Config.Belts)
                    Belts[b.Cell] = new BeltState { FactionId = b.FactionId, Facing = b.Facing, Hp = e.BeltHp, Item = b.Item };
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
            Require(r.FactionCap > 0 && r.CoreRadius.Raw >= 0 && r.CoreRadius <= Fix64.FromInt(1024)
                && r.OwnedObjectiveVision.Raw >= 0 && r.CaptureRadius.Raw >= 0
                && r.CaptureDurationTicks > 0 && r.CoreReinforcementIntervalTicks > 0
                && r.OutpostReinforcementIntervalTicks > 0 && r.OccupationThreatMemoryTicks >= 0
                && r.DefaultReservePermille <= 1000, "Invalid rules.");
            var c = new ScenarioDefinition { SchemaVersion = s.SchemaVersion, ScenarioId = s.ScenarioId, Seed = s.Seed,
                TickRateHz = s.TickRateHz, VerificationTickLimit = s.VerificationTickLimit,
                Map = new MapDefinition { WidthMeters = m.WidthMeters, HeightMeters = m.HeightMeters,
                    CellSizeMeters = m.CellSizeMeters, WidthCells = m.WidthCells, HeightCells = m.HeightCells, DefaultPassable = m.DefaultPassable, BlockedCellIds = blocked,
                    Terrain = Copy(m.Terrain) },
                Rules = new RuleDefinition { FactionCap = r.FactionCap, CoreRadius = r.CoreRadius,
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
            // Monk rules are opt-in. Keep disabled scenarios byte-for-byte unchanged, but make an enabled authored
            // scenario usable even when an older scenario file has no Monk parameter record yet.
            if (e.MonksEnabled && !Array.Exists(c.UnitParameters, value => value.Kind == UnitKind.Monk))
            {
                Array.Resize(ref c.UnitParameters, c.UnitParameters.Length + 1);
                c.UnitParameters[c.UnitParameters.Length - 1] = new UnitParameters { Kind = UnitKind.Monk, Hp = 100,
                    Speed = Fix64.FromInt(2), Vision = Fix64.FromInt(20), Range = Fix64.FromInt(4), Damage = 0, AttackIntervalTicks = 20 };
            }
            if (e.Enabled)
                Require(e.StartFood >= 0 && e.StartWood >= 0 && e.PopulationCap > 0 && e.VillagerHp > 0
                    && e.VillagerSpeed.Raw > 0 && e.VillagerSpeed <= Fix64.FromInt(16) && e.CarryCapacity > 0 && e.GatherIntervalTicks > 0
                    && e.VillagerFoodCost >= 0 && e.VillagerTrainTicks > 0 && e.QueueLimit > 0 && e.AutoVillagerTarget >= 0
                    && e.DropOffMargin.Raw >= 0 && e.DropOffMargin <= Fix64.FromInt(1024)
                    && e.BarracksSizeCells > 0 && e.BarracksSizeCells <= 8 && e.BarracksWoodCost >= 0 && e.BarracksWork > 0 && e.BarracksHp > 0
                    && e.Builders > 0 && e.InfantryFoodCost >= 0 && e.InfantryWoodCost >= 0 && e.InfantryTrainTicks > 0 && e.AutoInfantryQueue >= 0,
                    "Invalid economy rules.");
            else Require(c.Villagers.Length == 0 && !e.Industry, "Villagers and industry need an enabled economy.");
            if (e.Industry)
                Require(e.BeltWoodCost >= 0 && e.BeltTicksPerCell > 0 && e.BeltTicksPerCell <= 1000 && e.BeltHp > 0
                    && e.BeltLimit > 0 && e.BeltLimit <= 8192
                    && e.MineSizeCells > 0 && e.MineSizeCells <= 8 && e.MineWoodCost >= 0 && e.MineWork > 0 && e.MineHp > 0 && e.MineIntervalTicks > 0
                    && e.SmelterSizeCells > 0 && e.SmelterSizeCells <= 8 && e.SmelterWoodCost >= 0 && e.SmelterWork > 0 && e.SmelterHp > 0
                    && e.SmeltTicks > 0 && e.OrePerMetal > 0 && e.BufferLimit > 0 && e.OrePerMetal <= e.BufferLimit && e.InfantryMetalCost >= 0,
                    "Invalid industry rules.");
            else Require(c.Belts.Length == 0 && e.InfantryMetalCost == 0 && !e.ProcessingChain, "Belts and metal costs need industry.");
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
            // V3-4: ages come with the terrain map.
            Require(!e.Ages || (c.Map.Terrain.Length != 0 && e.AdvanceFoodCost >= 0 && e.AdvanceWoodCost >= 0 && e.AdvanceTicks > 0
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
                && e.TechFood != null && e.TechFood.Length == (e.Cavalry ? 13 : 12) && e.TechWood != null && e.TechWood.Length == (e.Cavalry ? 13 : 12)
                && e.TechTicks != null && e.TechTicks.Length == (e.Cavalry ? 13 : 12)
                && e.TechMetal != null && e.TechMetal.Length == (e.Cavalry ? 13 : 12) && e.TechGems != null && e.TechGems.Length == (e.Cavalry ? 13 : 12)
                && Array.TrueForAll(e.TechMetal, v => v >= 0) && Array.TrueForAll(e.TechGems, v => v >= 0)
                && e.SteelWeaponsDamage >= 0 && e.SteelArmourHp >= 0 && e.GemArmorHp >= 0
                && e.RepairHpPerTick > 0 && e.RepairAtPermille >= 0 && e.RepairAtPermille <= 1000
                && e.CastleSizeCells > 0 && e.CastleSizeCells <= 8 && e.CastleWoodCost >= 0 && e.CastleStoneCost >= 0 && e.CastleWork > 0 && e.CastleHp > 0
                && e.CastleRange >= 0 && e.CastleVision >= 0 && e.CastleDamage >= 0 && e.CastleIntervalTicks > 0
                 && e.MercenaryGems >= 0 && e.MercenaryTicks > 0 && e.MercenaryHp > 0 && e.MercenaryDamage >= 0 && e.MercenaryInterval > 0
                 && (!e.MonksEnabled || (e.ConversionTicks > 0 && e.MonkFoodCost >= 0 && e.MonkGoldCost >= 0 && e.MonkTrainTicks > 0))
                && Array.TrueForAll(e.TechFood, v => v >= 0) && Array.TrueForAll(e.TechWood, v => v >= 0) && Array.TrueForAll(e.TechTicks, v => v > 0)
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
            Require(!e.FishingEnabled || (e.Ages && e.FishRegrowTicks > 0 && e.FishAgrarianBonusPermille >= 0
                && e.FishAgrarianBonusPermille <= 1000 && e.FishReach >= 0 && e.FishReach <= 1024), "Invalid fishing rules.");
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
                    && ((byte)b.Item <= 4 || (e.ProcessingChain && (b.Item == ResourceKind.Charcoal || b.Item == ResourceKind.Steel))
                        || (e.Forestry && b.Item == ResourceKind.BowGear))
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
                Require((p.Kind == UnitKind.Infantry || p.Kind == UnitKind.Scout || p.Kind == UnitKind.Monk) && (i == 0 || c.UnitParameters[i - 1].Kind != p.Kind)
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
                        && ++factionCounts[d.FactionId - 1] <= r.FactionCap, "Initial capacity exceeded.");
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
            return new EconomyRules { Enabled = e.Enabled, StartFood = e.StartFood, StartWood = e.StartWood,
                PopulationCap = e.PopulationCap, VillagerHp = e.VillagerHp, VillagerSpeed = e.VillagerSpeed,
                CarryCapacity = e.CarryCapacity, GatherIntervalTicks = e.GatherIntervalTicks, VillagerFoodCost = e.VillagerFoodCost,
                VillagerTrainTicks = e.VillagerTrainTicks, QueueLimit = e.QueueLimit, AutoVillagerTarget = e.AutoVillagerTarget,
                DropOffMargin = e.DropOffMargin, BarracksSizeCells = e.BarracksSizeCells, BarracksWoodCost = e.BarracksWoodCost,
                BarracksWork = e.BarracksWork, BarracksHp = e.BarracksHp, Builders = e.Builders, InfantryFoodCost = e.InfantryFoodCost,
                InfantryWoodCost = e.InfantryWoodCost, InfantryTrainTicks = e.InfantryTrainTicks, AutoInfantryQueue = e.AutoInfantryQueue,
                 Industry = e.Industry, BeltWoodCost = e.BeltWoodCost, BeltTicksPerCell = e.BeltTicksPerCell, BeltHp = e.BeltHp, BeltLimit = e.BeltLimit,
                 MineSizeCells = e.MineSizeCells, MineWoodCost = e.MineWoodCost, MineWork = e.MineWork, MineHp = e.MineHp, MineIntervalTicks = e.MineIntervalTicks,
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
                 Ages = e.Ages, AdvanceFoodCost = e.AdvanceFoodCost, AdvanceWoodCost = e.AdvanceWoodCost, AdvanceTicks = e.AdvanceTicks,
                AgrarianInfantryFood = e.AgrarianInfantryFood, AgrarianInfantryWood = e.AgrarianInfantryWood, AgrarianInfantryTicks = e.AgrarianInfantryTicks,
                ForgedInfantryHp = e.ForgedInfantryHp, ForgedInfantryDamage = e.ForgedInfantryDamage,
                FarmSizeCells = e.FarmSizeCells, FarmWoodCost = e.FarmWoodCost, FarmWork = e.FarmWork, FarmHp = e.FarmHp,
                FarmBaseTicks = e.FarmBaseTicks, FarmStepTicks = e.FarmStepTicks, FarmMinTicks = e.FarmMinTicks, FarmFoodReach = e.FarmFoodReach, FarmRiverReach = e.FarmRiverReach,
                  Forestry = e.Forestry, Masonry = e.Masonry, Caravan = e.Caravan, Cavalry = e.Cavalry, Bridge = e.Bridge, Academy = e.Academy, Cult = e.Cult, Metropolis = e.Metropolis, Sanctuary = e.Sanctuary,
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
                 WallStoneCost = e.WallStoneCost, WallHp = e.WallHp, WallReach = e.WallReach,
                 MasonryDefenceCostPermille = e.MasonryDefenceCostPermille, MasonryDefenceWorkPermille = e.MasonryDefenceWorkPermille,
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
        private static T[] Copy<T>(T[] a) => a == null ? throw new ArgumentException("Missing scenario array.") : (T[])a.Clone();
        private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
    }
}
