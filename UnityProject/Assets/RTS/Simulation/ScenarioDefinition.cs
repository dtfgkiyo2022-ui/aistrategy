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
    }

    public struct BeltDefinition
    {
        public int Cell;
        public uint FactionId;
        public Facing Facing;
        /// <summary>0 for an empty belt.</summary>
        public ResourceKind Item;
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
        /// <summary>Mine (12.2): footprint, wood, work, HP, and ticks per ore taken from the point under it.</summary>
        public int MineSizeCells = 2, MineWoodCost = 60, MineWork = 200, MineHp = 400, MineIntervalTicks = 20;
        /// <summary>Smelter (12.2): footprint, wood, work, HP, ticks per metal, and ore per metal.</summary>
        public int SmelterSizeCells = 3, SmelterWoodCost = 120, SmelterWork = 300, SmelterHp = 600, SmeltTicks = 60, OrePerMetal = 2;
        /// <summary>Items a mine or smelter holds at its input and at its output; a full output stops the work.</summary>
        public int BufferLimit = 10;
        /// <summary>Metal an infantry costs on top of food and wood (12.4). 0 unless the map sets it.</summary>
        public int InfantryMetalCost;

        /// <summary>
        /// V3-4 (technical-design-v3 26): everyone starts in the primitive age and advances into a civilisation. Mines,
        /// smelters and the metal cost of infantry then belong to the metallurgy civilisation only. False keeps V3-2/V3-3.
        /// </summary>
        public bool Ages;
        /// <summary>Advancing: food and wood paid at the start, and the ticks it takes at the core.</summary>
        public int AdvanceFoodCost = 400, AdvanceWoodCost = 300, AdvanceTicks = 1200;
        /// <summary>V3-4 agrarian (27, 26.2): infantry is cheaper and quicker to train.</summary>
        public int AgrarianInfantryFood = 35, AgrarianInfantryWood = 10, AgrarianInfantryTicks = 200;
        /// <summary>V3-4 metallurgy (27): trained infantry is born with these HP and damage.</summary>
        public int ForgedInfantryHp = 140, ForgedInfantryDamage = 13;
        /// <summary>V3-4 farm (27): footprint, wood, work, HP. It makes one food every FarmBaseTicks, FarmStepTicks faster
        /// for each food point within FarmFoodReach metres and once more with river within FarmRiverReach, never under FarmMinTicks.</summary>
        public int FarmSizeCells = 2, FarmWoodCost = 80, FarmWork = 200, FarmHp = 400;
        public int FarmBaseTicks = 60, FarmStepTicks = 10, FarmMinTicks = 20, FarmFoodReach = 16, FarmRiverReach = 8;
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
        /// <summary>Stone each side starts with (maps with ages).</summary>
        public int StartStone;
        /// <summary>V3-5 (32 #5): a tower shoots the nearest enemy soldier (else villager) in TowerRange metres, and sees around it.</summary>
        public int TowerSizeCells = 2, TowerWoodCost = 50, TowerStoneCost = 100, TowerWork = 250, TowerHp = 800;
        public int TowerRange = 10, TowerVision = 16, TowerDamage = 6, TowerIntervalTicks = 20;
        /// <summary>V3-5 (32 #6): the blacksmith, and the price and time of each tech (index TechKind - 1).</summary>
        public int BlacksmithSizeCells = 3, BlacksmithWoodCost = 150, BlacksmithWork = 300, BlacksmithHp = 700;
        public int[] TechFood = { 150, 150, 100, 100, 120, 120 };
        public int[] TechWood = { 100, 100, 50, 100, 80, 80 };
        public int[] TechTicks = { 600, 600, 400, 400, 500, 500 };
        /// <summary>What the techs do: attack, HP, ticks per gather, carry, farm and smelt ticks saved.</summary>
        public int WeaponsDamage = 2, ArmourHp = 20, ToolsGatherTicks = 5, CartsCarry = 5, IrrigationTicks = 10, BlastFurnaceTicks = 20;
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
