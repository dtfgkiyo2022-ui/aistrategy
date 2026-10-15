using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>霧を適用する前の、記録パック用の集計スナップショットです。</summary>
    public sealed class MatchPackSnapshot
    {
        public long Tick { get; internal set; }
        public MatchPackFactionSnapshot[] Factions { get; internal set; } = Array.Empty<MatchPackFactionSnapshot>();
        public MatchPackObjectiveSnapshot[] Cores { get; internal set; } = Array.Empty<MatchPackObjectiveSnapshot>();
        public MatchPackObjectiveSnapshot[] Outposts { get; internal set; } = Array.Empty<MatchPackObjectiveSnapshot>();
    }

    public sealed class MatchPackFactionSnapshot
    {
        public uint FactionId { get; internal set; }
        public int Villagers { get; internal set; }
        public int FoodGatherers { get; internal set; }
        public int WoodGatherers { get; internal set; }
        public int Food { get; internal set; }
        public int Wood { get; internal set; }
        public int Ore { get; internal set; }
        public int Metal { get; internal set; }
        public int Charcoal { get; internal set; }
        public int Steel { get; internal set; }
        public int BowGear { get; internal set; }
        public int Stone { get; internal set; }
        public int Gold { get; internal set; }
        public int Gems { get; internal set; }
        public int Rations { get; internal set; }
        public int RationsProduced { get; internal set; }
        public int RationsConsumed { get; internal set; }
        public int RationsTrainedSoldiers { get; internal set; }
        public int RationsHealedHp { get; internal set; }
        public int Bricks { get; internal set; }
        public int BricksProduced { get; internal set; }
        public int BricksConsumed { get; internal set; }
        public int BricksUpgradedWalls { get; internal set; }
        public int BricksUpgradedTowers { get; internal set; }
        public int BricksCoreRepairHp { get; internal set; }
        public int BricksConsumedWalls { get; internal set; }
        public int BricksConsumedTowers { get; internal set; }
        public int BricksConsumedCoreRepair { get; internal set; }
        public int BricksConsumedCastles { get; internal set; }
        public int Tack { get; internal set; }
        public int TackProduced { get; internal set; }
        public int TackConsumed { get; internal set; }
        public int TackTrainedHeavyCavalry { get; internal set; }
        public int Planks { get; internal set; }
        public int PlanksProduced { get; internal set; }
        public int PlanksConsumed { get; internal set; }
        public int PlanksConsumedRams { get; internal set; }
        public int PlanksConsumedBridges { get; internal set; }
        public int PlanksEnhancedRams { get; internal set; }
        public int Books { get; internal set; }
        public int BooksProduced { get; internal set; }
        public int BooksConsumed { get; internal set; }
        public int BooksUsedResearch { get; internal set; }
        public int PreservesProduced { get; internal set; }
        public int PreservesConvertedFood { get; internal set; }
        public int JewelsProduced { get; internal set; }
        public int JewelsConsumed { get; internal set; }
        public int JewelsTrainedMercenaries { get; internal set; }
        public int TradeGoodsProduced { get; internal set; }
        public int TradeGoodsConvertedFood { get; internal set; }
        public int TradeGoodsConvertedStone { get; internal set; }
        public int Incense { get; internal set; }
        public int IncenseProduced { get; internal set; }
        public int IncenseConsumed { get; internal set; }
        public int IncenseHealedHp { get; internal set; }
        public int Charms { get; internal set; }
        public int CharmsProduced { get; internal set; }
        public int CharmsConsumed { get; internal set; }
        public int CharmsTrainedSoldiers { get; internal set; }
        public int RepairKits { get; internal set; }
        public int RepairKitsProduced { get; internal set; }
        public int RepairKitsConsumed { get; internal set; }
        public int RepairKitsRepairedHp { get; internal set; }
        public int RepairKitsCoreRepairedHp { get; internal set; }
        public int Tools { get; internal set; }
        public int ToolsProduced { get; internal set; }
        public int ToolsConsumed { get; internal set; }
        public int ToolsTrainedVillagers { get; internal set; }
        public int ToolsSavedFood { get; internal set; }
        public MatchPackGoodsTradeSnapshot[] GoodsMarket { get; internal set; } = Array.Empty<MatchPackGoodsTradeSnapshot>();
        public long CraftBalanceMarketSaveStartedTick { get; internal set; }
        public string Civ { get; internal set; }
        public int Age { get; internal set; }
        public MatchPackCount[] Soldiers { get; internal set; } = Array.Empty<MatchPackCount>();
        public MatchPackCount[] Buildings { get; internal set; } = Array.Empty<MatchPackCount>();
    }

    public sealed class MatchPackObjectiveSnapshot
    {
        public uint Id { get; internal set; }
        public uint OwnerFactionId { get; internal set; }
    }

    public sealed class MatchPackCount
    {
        public string Kind { get; internal set; }
        public int Count { get; internal set; }
    }

    public sealed class MatchPackGoodsTradeSnapshot
    {
        public string Goods { get; internal set; }
        public int SoldCount { get; internal set; }
        public int SoldUnits { get; internal set; }
        public int ReceivedFood { get; internal set; }
        public int ReceivedWood { get; internal set; }
    }

    public sealed partial class Simulation
    {
        /// <summary>
        /// Returns a fog-free aggregate for post-match analysis. This is deliberately separate from Capture(), which
        /// remains faction-local and is the only view exposed to tactics.
        /// </summary>
        public MatchPackSnapshot CaptureMatchPackSnapshot()
        {
            var snapshot = new MatchPackSnapshot { Tick = world.Tick };
            snapshot.Factions = new MatchPackFactionSnapshot[world.Factions.Length];
            for (int f = 0; f < world.Factions.Length; f++)
            {
                var economy = world.Economies != null && f < world.Economies.Length ? world.Economies[f] : default(FactionEconomy);
                var faction = new MatchPackFactionSnapshot
                {
                    FactionId = world.Factions[f].Id,
                    Villagers = LivingVillagers(world.Factions[f].Id),
                    FoodGatherers = CountGatherers(world.Factions[f].Id, ResourceKind.Food),
                    WoodGatherers = CountGatherers(world.Factions[f].Id, ResourceKind.Wood),
                    Food = economy.Food,
                    Wood = economy.Wood,
                    Ore = economy.Ore,
                    Metal = economy.Metal,
                    Charcoal = economy.Charcoal,
                    Steel = economy.Steel,
                    BowGear = economy.BowGear,
                    Stone = economy.Stone,
                    Gold = economy.Gold,
                    Gems = economy.Gems,
                    Rations = economy.Rations,
                    RationsProduced = economy.RationsProduced,
                    RationsConsumed = economy.RationsConsumed,
                    RationsTrainedSoldiers = economy.RationsTrainedSoldiers,
                    RationsHealedHp = economy.RationsHealedHp,
                    Bricks = economy.Bricks,
                    BricksProduced = economy.BricksProduced,
                    BricksConsumed = economy.BricksConsumed,
                    BricksUpgradedWalls = economy.BricksUpgradedWalls,
                    BricksUpgradedTowers = economy.BricksUpgradedTowers,
                    BricksCoreRepairHp = economy.BricksCoreRepairHp,
                    BricksConsumedWalls = economy.BricksConsumedWalls,
                    BricksConsumedTowers = economy.BricksConsumedTowers,
                    BricksConsumedCoreRepair = economy.BricksConsumedCoreRepair,
                    BricksConsumedCastles = economy.BricksConsumedCastles,
                    Tack = economy.Tack,
                    TackProduced = economy.TackProduced,
                    TackConsumed = economy.TackConsumed,
                    TackTrainedHeavyCavalry = economy.TackTrainedHeavyCavalry,
                    Planks = economy.Planks,
                    PlanksProduced = economy.PlanksProduced,
                    PlanksConsumed = economy.PlanksConsumed,
                    PlanksConsumedRams = economy.PlanksConsumedRams,
                    PlanksConsumedBridges = economy.PlanksConsumedBridges,
                    PlanksEnhancedRams = economy.PlanksEnhancedRams,
                    Books = economy.Books,
                    BooksProduced = economy.BooksProduced,
                    BooksConsumed = economy.BooksConsumed,
                    BooksUsedResearch = economy.BooksUsedResearch,
                    PreservesProduced = economy.PreservesProduced,
                    PreservesConvertedFood = economy.PreservesConvertedFood,
                    JewelsProduced = economy.JewelsProduced,
                    JewelsConsumed = economy.JewelsConsumed,
                    JewelsTrainedMercenaries = economy.JewelsTrainedMercenaries,
                    TradeGoodsProduced = economy.TradeGoodsProduced,
                    TradeGoodsConvertedFood = economy.TradeGoodsConvertedFood,
                    TradeGoodsConvertedStone = economy.TradeGoodsConvertedStone,
                    Incense = economy.Incense,
                    IncenseProduced = economy.IncenseProduced,
                    IncenseConsumed = economy.IncenseConsumed,
                    IncenseHealedHp = economy.IncenseHealedHp,
                    Charms = economy.Charms,
                    CharmsProduced = economy.CharmsProduced,
                    CharmsConsumed = economy.CharmsConsumed,
                    CharmsTrainedSoldiers = economy.CharmsTrainedSoldiers,
                    RepairKits = economy.RepairKits,
                    RepairKitsProduced = economy.RepairKitsProduced,
                    RepairKitsConsumed = economy.RepairKitsConsumed,
                    RepairKitsRepairedHp = economy.RepairKitsRepairedHp,
                    RepairKitsCoreRepairedHp = economy.RepairKitsCoreRepairedHp,
                    Tools = economy.Tools,
                    ToolsProduced = economy.ToolsProduced,
                    ToolsConsumed = economy.ToolsConsumed,
                    ToolsTrainedVillagers = economy.ToolsTrainedVillagers,
                    ToolsSavedFood = economy.ToolsSavedFood,
                    GoodsMarket = GoodsMarketSnapshot(economy),
                    CraftBalanceMarketSaveStartedTick = CraftBalanceOn ? economy.CraftBalanceMarketSaveStartedTick : 0,
                    Civ = economy.Civ.ToString(),
                    Age = economy.Age,
                    Soldiers = CountSoldiers(world.Factions[f].Id),
                    Buildings = CountBuildings(world.Factions[f].Id)
                };
                snapshot.Factions[f] = faction;
            }
            snapshot.Cores = new MatchPackObjectiveSnapshot[world.Cores.Length];
            for (int i = 0; i < world.Cores.Length; i++)
                snapshot.Cores[i] = new MatchPackObjectiveSnapshot { Id = world.Cores[i].Definition.Id, OwnerFactionId = world.Cores[i].Definition.FactionId };
            snapshot.Outposts = new MatchPackObjectiveSnapshot[world.Outposts.Length];
            for (int i = 0; i < world.Outposts.Length; i++)
                snapshot.Outposts[i] = new MatchPackObjectiveSnapshot { Id = world.Outposts[i].Definition.Id, OwnerFactionId = world.Outposts[i].OwnerFactionId };
            return snapshot;
        }

        private MatchPackGoodsTradeSnapshot[] GoodsMarketSnapshot(FactionEconomy economy)
        {
            if (!GoodsMarketOn || economy.GoodsSoldCounts == null) return Array.Empty<MatchPackGoodsTradeSnapshot>();
            var result = new MatchPackGoodsTradeSnapshot[GoodsKinds.Length];
            for (int i = 0; i < GoodsKinds.Length; i++)
            {
                int index = (int)GoodsKinds[i];
                result[i] = new MatchPackGoodsTradeSnapshot
                {
                    Goods = GoodsKinds[i].ToString(), SoldCount = economy.GoodsSoldCounts[index], SoldUnits = economy.GoodsSoldUnits[index],
                    ReceivedFood = economy.GoodsReceivedFood[index], ReceivedWood = economy.GoodsReceivedWood[index]
                };
            }
            return result;
        }

        private int CountGatherers(uint factionId, ResourceKind kind)
        {
            int count = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var villager = world.Villagers[i];
                if (!villager.Alive || villager.FactionId != factionId || villager.NodeId == 0
                    || villager.NodeId > world.Nodes.Length) continue;
                if (world.Nodes[villager.NodeId - 1].Definition.Kind == kind) count++;
            }
            return count;
        }

        private MatchPackCount[] CountSoldiers(uint factionId)
        {
            var counts = new Dictionary<UnitKind, int>();
            foreach (UnitKind kind in Enum.GetValues(typeof(UnitKind))) counts[kind] = 0;
            for (int i = 0; i < world.SoldierCount; i++)
            {
                if (!world.Soldiers[i].Alive || world.Soldiers[i].Initial.FactionId != factionId) continue;
                // What the soldier was trained as (archer, cavalry...), as the frame shows it; the initial kind alone
                // counted every trained archer and rider as infantry.
                var kind = world.Soldiers[i].Class != 0 ? world.Soldiers[i].Class : world.Soldiers[i].Initial.Kind;
                counts[kind]++;
            }
            var result = new MatchPackCount[counts.Count];
            int index = 0;
            foreach (var pair in counts)
                result[index++] = new MatchPackCount { Kind = pair.Key.ToString(), Count = pair.Value };
            return result;
        }

        private MatchPackCount[] CountBuildings(uint factionId)
        {
            var counts = new Dictionary<BuildingKind, int>();
            foreach (BuildingKind kind in Enum.GetValues(typeof(BuildingKind))) counts[kind] = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive && world.Buildings[i].FactionId == factionId) counts[world.Buildings[i].Kind]++;
            var result = new MatchPackCount[counts.Count];
            int index = 0;
            foreach (var pair in counts)
                result[index++] = new MatchPackCount { Kind = pair.Key.ToString(), Count = pair.Value };
            return result;
        }
    }
}
