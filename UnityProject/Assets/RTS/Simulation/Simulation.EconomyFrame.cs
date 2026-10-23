using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// Ver.3: the economy part of a faction frame (V3-1 PR5). Own villagers and buildings in full; enemy villagers and
    /// buildings only while the faction sees them, and enemy villagers as bare positions, as enemy soldiers are shown.
    /// Resources are public in V3-1. The frame is display data only and is never read back into the simulation.
    /// </summary>
    public sealed partial class Simulation
    {
        private EconomyView EconomyViewFor(uint faction)
        {
            if (!EconomyOn) return null;
            var rules = world.Config.Economy;
            var economy = world.Economies[faction - 1];
            bool commonSawmill = CraftTreeCommonAllowed(faction);
            var villagers = new List<VillagerView>();
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (!v.Alive) continue;
                if (v.FactionId == faction)
                    villagers.Add(new VillagerView(v.Id, true, v.Position, Activity(v.Task), v.CarryKind, v.Carry, v.Hp, v.Held,
                        v.CaravanMarketId, v.CaravanseraiId, v.CaravanOutpostId, v.CaravanStage, v.CaravanWood, v.CaravanGems,
                        CaravanStopReasonFor(v)));
                else if (IsVisibleTo(faction, v.Position))
                    villagers.Add(new VillagerView(0, false, v.Position, VillagerActivity.Idle, 0, 0, 0));
            }
            var buildings = new List<BuildingView>();
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive) continue;
                bool own = b.FactionId == faction;
                if (!own && b.Reserved) continue;
                if (!own && !BuildingVisibleTo(faction, b)) continue;
                int size = SizeOf(b.Kind);
                CaravanStopReason caravanReason = CaravanStopReason.None;
                if (b.Kind == BuildingKind.Caravanserai && own)
                {
                    if (b.CaravanMarketId == 0 || b.CaravanMarketId > world.BuildingCount
                        || !IsActualBuilding(world.Buildings[b.CaravanMarketId - 1]) || !world.Buildings[b.CaravanMarketId - 1].Complete
                        || world.Buildings[b.CaravanMarketId - 1].FactionId != faction
                        || world.Buildings[b.CaravanMarketId - 1].Kind != BuildingKind.Market)
                        caravanReason = CaravanStopReason.MarketLost;
                    else if (b.CaravanOutpostId == 0 || b.CaravanOutpostId > world.Outposts.Length
                        || world.Outposts[b.CaravanOutpostId - 1].OwnerFactionId != faction)
                        caravanReason = CaravanStopReason.OutpostLost;
                    else caravanReason = CaravanStopReason.Normal;
                }
                buildings.Add(new BuildingView(b.Id, b.FactionId, b.Kind, BuildingCenter(b), size * world.Config.Map.CellSizeMeters,
                    own ? b.Hp : 0, own ? MaxHpOf(b) : 0, b.Complete, own ? b.Progress : 0, WorkOf(b),
                    own ? b.Queued : 0, own ? b.TrainRemaining : 0, b.Facing, own ? b.Input : 0, own ? b.Output : 0, own && b.Held,
                    own ? b.Researching : 0, own && b.Researching != 0 ? b.TrainRemaining : 0,
                    own && (ProcessingOn || ForestryOn || BricksOn || TackOn || BooksOn || PreservesOn || JewelsOn || TradeGoodsOn || IncenseOn || RepairKitsOn || ToolsOn) ? b.InputSecondary : 0,
                    own && ForestryOn && economy.Age >= 2 ? b.QueuedBowGear : 0,
                    own && b.Kind == BuildingKind.Caravanserai ? b.CaravanMarketId : 0,
                    own && b.Kind == BuildingKind.Caravanserai ? b.CaravanOutpostId : 0,
                    own && b.Kind == BuildingKind.Caravanserai ? b.CaravanDistance : default,
                    own && b.Kind == BuildingKind.Caravanserai ? b.CaravanWoodReward : 0, caravanReason,
                    CraftTree1On && IsCraftProcessingBuilding(b.Kind) ? b.Level : 1,
                     own && (CraftTree2On || PrimitiveCraftOn) ? b.InputTertiary : 0, own && (CraftTree2On || PrimitiveCraftOn) ? b.Recipe : RecipeKind.None,
                    b.Reserved, b.Reserved ? ReservationOrderFor(b) : 0));
            }
            IReadOnlyList<ResourceView> resources;
            if (ResourceFogOn)
                resources = ResourceViewsFor(faction);
            else
            {
                var publicResources = new List<ResourceView>();
                foreach (var n in world.Nodes)
                    if (n.Remaining > 0) publicResources.Add(new ResourceView(n.Definition.Id, n.Definition.Kind, n.Definition.Position, n.Remaining));
                resources = publicResources;
            }
            int population = LivingVillagers(faction) + LivingSoldiers(faction);
            // V3-2: own belts in full, enemy belts on cells the faction sees now (without what they carry).
            var belts = new List<BeltView>();
            for (int cell = 0; cell < world.Belts.Length; cell++)
            {
                var b = world.Belts[cell];
                if (b.FactionId == 0) continue;
                bool own = b.FactionId == faction;
                if (!own && !world.Factions[faction - 1].VisibleCells[cell]) continue;
                belts.Add(new BeltView(cell, b.FactionId, b.Facing, own ? b.Item : 0, own ? b.Progress : 0, own && b.Held,
                    own ? b.Component : BeltComponentKind.None, own ? b.SorterKind : (ResourceKind)0, own ? b.PairCell : -1,
                    own && b.Fast ? BeltSpeed.Fast : BeltSpeed.Normal));
            }
            var cavalryMission = CavalryMissionFor(faction);
            var advancePrice = AgesOn ? AdvancePrice(faction, economy) : (food: 0, wood: 0, gold: 0, ticks: 0);
            int advanceFood = AgeClockOn ? advancePrice.food : rules.AdvanceFoodCost;
            int advanceWood = AgeClockOn ? advancePrice.wood : rules.AdvanceWoodCost;
            int age2Food = AgeClockOn && economy.Age == 1 ? advancePrice.food : rules.Age2FoodCost;
            int age2Wood = AgeClockOn && economy.Age == 1 ? advancePrice.wood : rules.Age2WoodCost;
            int age3Food = AgeClockOn && economy.Age == 2 ? advancePrice.food : rules.Age3FoodCost;
            int age3Wood = AgeClockOn && economy.Age == 2 ? advancePrice.wood : rules.Age3WoodCost;
            long nextAgeClockTick = NextAgeClockTick(faction);
            long civChoiceDeadline = AgeClockUxOn && economy.Age == 0 && economy.Civ == CivKind.Primitive && nextAgeClockTick != 0
                ? economy.CivChoiceDeadlineTick != 0 ? economy.CivChoiceDeadlineTick : checked(nextAgeClockTick + rules.AgeClockUxChoiceTicks)
                : 0;
            var passableCells = PlacementPassableView();
            var riverCells = PlacementRiverView();
            return new EconomyView(economy.Food, economy.Wood, population, PopCapFor(faction), economy.Queued, economy.TrainRemaining,
                !economy.AutoOff, rules.BarracksSizeCells, rules.BarracksWoodCost, VillagerFoodCostFor(faction, CanUseToolForVillager(faction)), InfantryFoodFor(faction),
                InfantryWoodFor(faction), villagers, buildings, resources,
                rules.Industry, economy.Ore, economy.Metal, rules.BeltWoodCost, rules.BeltTicksPerCell, belts,
                InfantryMetalFor(faction), rules.MineWoodCost, rules.SmelterWoodCost, rules.MineSizeCells, rules.SmelterSizeCells, economy.CoreHeld, economy.Policy,
                rules.Ages, economy.Civ, economy.AdvancingTo, economy.AdvanceRemaining, advanceFood, advanceWood,
                rules.FarmWoodCost, rules.FarmSizeCells, rules.Ages ? rules.ScoutFoodCost : 0, rules.Ages ? rules.HouseWoodCost : 0, rules.Ages ? rules.DropSiteWoodCost : 0,
                 economy.Stone, rules.Ages ? StoneOf(BuildingKind.Wall, faction) : 0, rules.Ages ? WoodOf(BuildingKind.Tower, faction) : 0, rules.Ages ? StoneOf(BuildingKind.Tower, faction) : 0,
                rules.Ages ? rules.BlacksmithWoodCost : 0, economy.Techs, rules.Ages ? rules.TechFood : null, rules.Ages ? rules.TechWood : null, rules.Ages ? rules.TechMetal : null,
                economy.Age, rules.Ages ? age2Food : 0, rules.Ages ? age2Wood : 0, rules.Ages ? rules.ArcherFood : 0, rules.Ages ? rules.ArcherWood : 0,
                rules.Ages ? rules.CavalryFood : 0, rules.Ages ? rules.CavalryWood : 0, rules.Ages ? rules.CavalryMetal : 0,
                rules.Ages ? rules.MarketWoodCost : 0, rules.Ages ? rules.WorkshopWoodCost : 0, rules.Ages ? rules.TradeLot : 0, rules.Ages ? rules.TradeReturn : 0,
                rules.Ages ? rules.RamFood : 0, rules.Ages ? rules.RamWood : 0,
                rules.Ages ? age3Food : 0, rules.Ages ? age3Wood : 0,
                rules.Ages ? rules.RangeWoodCost : 0, rules.Ages ? rules.StableWoodCost : 0,
                 rules.Ages ? WoodOf(BuildingKind.Castle, faction) : 0, rules.Ages ? StoneOf(BuildingKind.Castle, faction) : 0,
                rules.Ages ? economy.Gems : 0, rules.Ages ? rules.GemsTradeReturn : 0, rules.Ages ? rules.TechGems : null, rules.Ages ? rules.GemArmorHp : 0,
                rules.Ages ? rules.MercenaryGems : 0, rules.Ages ? rules.MercenaryTicks : 0,
                rules.MonksEnabled, rules.MonksEnabled ? rules.MonkFoodCost : 0, rules.MonksEnabled ? rules.MonkGoldCost : 0,
                rules.MonksEnabled ? rules.MonkTrainTicks : 0,
                rules.ProcessingChain,
                ProcessingOn ? rules.CharcoalKilnWoodCost : 0, ProcessingOn ? rules.SteelworksWoodCost : 0,
                ProcessingOn ? rules.CharcoalKilnSizeCells : 0, ProcessingOn ? rules.SteelworksSizeCells : 0,
                ProcessingOn ? rules.HeavyInfantryFoodCost : 0, ProcessingOn ? rules.HeavyInfantrySteelCost : 0,
                ProcessingOn ? economy.Charcoal : 0, ProcessingOn ? economy.Steel : 0,
                (ProcessingOn || BricksOn || TackOn || PlanksOn || BooksOn || PreservesOn || JewelsOn || TradeGoodsOn || IncenseOn || CharmsOn || RepairKitsOn || ToolsOn || ForestryOn && economy.Age >= 2) ? new List<LineView>(LineViews(faction)) : null,
                rules.GoldEnabled ? economy.Gold : 0,
                ForestryOn ? economy.BowGear : 0, ForestryOn ? rules.FletcherWoodCost : 0, ForestryOn ? rules.FletcherSizeCells : 0,
                ForestryOn ? rules.FletcherTicks : 0, ForestryOn ? rules.SkirmishArcherFoodCost : 0,
                ForestryOn ? rules.SkirmishArcherBowGearCost : 0, ForestryOn ? rules.SkirmishArcherTrainTicks : 0, cavalryMission,
                AgesOn ? economy.ReservedCiv : CivKind.Primitive,
                AgesOn && economy.Civ != CivKind.Primitive && economy.Age == 2 ? AdvancePrice(faction, economy).gold : 0,
                rules.BeltComponents, rules.FastBeltWoodCost, rules.FastBeltTicksPerCell, rules.SplitterWoodCost, rules.SorterWoodCost,
                rules.UndergroundBeltWoodCost, rules.UndergroundBeltMaxLength, rules.StorageWoodCost, rules.StorageSizeCells, rules.StorageCapacity,
                  nextAgeClockTick: NextAgeClockTick(faction), autoAssaultOff: AutoAssaultOffFor(faction),
                  rationsEnabled: RationsAllowed(faction), rations: RationsAllowed(faction) ? economy.Rations : 0,
                  millWoodCost: RationsAllowed(faction) ? rules.MillWoodCost : 0, millSizeCells: RationsAllowed(faction) ? rules.MillSizeCells : 0,
                  millTicks: RationsAllowed(faction) ? rules.MillTicks : 0, millFoodInput: RationsAllowed(faction) ? rules.MillFoodInput : 0,
                  rationsPerSoldier: RationsAllowed(faction) ? rules.RationsPerSoldier : 0, rationsTrainPermille: RationsAllowed(faction) ? rules.RationsTrainPermille : 0,
                  rationsHealIntervalTicks: RationsAllowed(faction) ? rules.RationsHealIntervalTicks : 0,
                  rationsHealQuietTicks: RationsAllowed(faction) ? rules.RationsHealQuietTicks : 0,
                  rationsHealHp: RationsAllowed(faction) ? rules.RationsHealHp : 0,
                  rationsHealMaxPerInterval: RationsAllowed(faction) ? rules.RationsHealMaxPerInterval : 0,
                  rationsFarmMinimum: RationsAllowed(faction) ? rules.RationsFarmMinimum : 0,
                  rationsFoodReserve: RationsAllowed(faction) ? rules.RationsFoodReserve : 0,
                  bricksEnabled: BricksAllowed(faction), bricks: BricksAllowed(faction) ? economy.Bricks : 0,
                  brickKilnWoodCost: BricksAllowed(faction) ? rules.BrickKilnWoodCost : 0,
                  brickKilnSizeCells: BricksAllowed(faction) ? rules.BrickKilnSizeCells : 0,
                  brickTicks: BricksAllowed(faction) ? rules.BrickTicks : 0,
                  brickStoneInput: BricksAllowed(faction) ? rules.BrickStoneInput : 0,
                  brickCharcoalInput: BricksAllowed(faction) ? rules.BrickCharcoalInput : 0,
                  bricksPerWall: BricksAllowed(faction) ? rules.BricksPerWall : 0,
                  bricksPerTower: BricksAllowed(faction) ? rules.BricksPerTower : 0,
                  bricksPerCastle: BricksAllowed(faction) ? rules.BricksPerCastle : 0,
                  brickHpPermille: BricksAllowed(faction) ? rules.BrickHpPermille : 0,
                  brickUpgradeIntervalTicks: BricksAllowed(faction) ? rules.BrickUpgradeIntervalTicks : 0,
                brickCastleStonePermille: BricksAllowed(faction) ? rules.BrickCastleStonePermille : 0,
                tackEnabled: TackAllowed(faction), tack: TackAllowed(faction) ? economy.Tack : 0,
                saddleryWoodCost: TackAllowed(faction) ? rules.SaddleryWoodCost : 0,
                saddlerySizeCells: TackAllowed(faction) ? rules.SaddlerySizeCells : 0,
                tackTicks: TackAllowed(faction) ? rules.TackTicks : 0,
                tackFoodInput: TackAllowed(faction) ? rules.TackFoodInput : 0,
                tackWoodInput: TackAllowed(faction) ? rules.TackWoodInput : 0,
                heavyCavalryTackCost: TackAllowed(faction) ? rules.HeavyCavalryTackCost : 0,
                heavyCavalryFoodCost: TackAllowed(faction) ? rules.HeavyCavalryFoodCost : 0,
                heavyCavalryTrainTicks: TackAllowed(faction) ? rules.HeavyCavalryTrainTicks : 0,
                heavyCavalryHp: TackAllowed(faction) ? rules.HeavyCavalryHp : 0,
                heavyCavalryDamage: TackAllowed(faction) ? rules.HeavyCavalryDamage : 0,
                heavyCavalryAttackIntervalTicks: TackAllowed(faction) ? rules.HeavyCavalryAttackIntervalTicks : 0,
                heavyCavalryRange: TackAllowed(faction) ? rules.HeavyCavalryRange : default(Fix64),
                heavyCavalrySpeed: TackAllowed(faction) ? rules.HeavyCavalrySpeed : default(Fix64),
                heavyCavalryVision: TackAllowed(faction) ? rules.HeavyCavalryVision : default(Fix64),
                planksEnabled: PlanksAllowed(faction) || commonSawmill, planks: PlanksAllowed(faction) || commonSawmill ? economy.Planks : 0,
                sawmillWoodCost: PlanksAllowed(faction) || commonSawmill ? rules.SawmillWoodCost : 0,
                sawmillSizeCells: PlanksAllowed(faction) || commonSawmill ? rules.SawmillSizeCells : 0,
                plankTicks: commonSawmill ? rules.CraftTreePlankTicks : PlanksAllowed(faction) ? rules.PlankTicks : 0,
                plankWoodInput: commonSawmill ? rules.CraftTreePlankWoodInput : PlanksAllowed(faction) ? rules.PlankWoodInput : 0,
                planksPerRam: PlanksAllowed(faction) ? rules.PlanksPerRam : 0,
                plankRamHpPermille: PlanksAllowed(faction) ? rules.PlankRamHpPermille : 0,
                plankRamTrainPermille: PlanksAllowed(faction) ? rules.PlankRamTrainPermille : 0,
                planksPerBridgeCell: PlanksAllowed(faction) ? rules.PlanksPerBridgeCell : 0,
                plankBridgeWoodPermille: PlanksAllowed(faction) ? rules.PlankBridgeWoodPermille : 0,
                plankExtraRams: PlanksAllowed(faction) ? rules.PlankExtraRams : 0,
                plankWoodReserve: PlanksAllowed(faction) ? rules.PlankWoodReserve : 0,
                booksEnabled: BooksAllowed(faction), books: BooksAllowed(faction) ? economy.Books : 0,
                scriptoriumWoodCost: BooksAllowed(faction) ? rules.ScriptoriumWoodCost : 0,
                scriptoriumSizeCells: BooksAllowed(faction) ? rules.ScriptoriumSizeCells : 0,
                bookTicks: BooksAllowed(faction) ? rules.BookTicks : 0,
                bookGoldInput: BooksAllowed(faction) ? rules.BookGoldInput : 0,
                bookWoodInput: BooksAllowed(faction) ? rules.BookWoodInput : 0,
                booksPerResearch: BooksAllowed(faction) ? rules.BooksPerResearch : 0,
                bookResearchPermille: BooksAllowed(faction) ? rules.BookResearchPermille : 0,
                bookResearchCostPermille: BooksAllowed(faction) ? rules.BookResearchCostPermille : 0,
                bookWoodReserve: BooksAllowed(faction) ? rules.BookWoodReserve : 0,
                preservesEnabled: PreservesAllowed(faction),
                dryingRackWoodCost: PreservesAllowed(faction) ? rules.DryingRackWoodCost : 0,
                dryingRackSizeCells: PreservesAllowed(faction) ? rules.DryingRackSizeCells : 0,
                preserveTicks: PreservesAllowed(faction) ? rules.PreserveTicks : 0,
                preserveFoodInput: PreservesAllowed(faction) ? rules.PreserveFoodInput : 0,
                preserveFoodValue: PreservesAllowed(faction) ? rules.PreserveFoodValue : 0,
                preserveKeepFood: PreservesAllowed(faction) ? rules.PreserveKeepFood : 0,
                preserveMaxRacks: PreservesAllowed(faction) ? rules.PreserveMaxRacks : 0,
                preserveWoodReserve: PreservesAllowed(faction) ? rules.PreserveWoodReserve : 0,
                jewelsEnabled: JewelsAllowed(faction),
                lapidaryWoodCost: JewelsAllowed(faction) ? rules.LapidaryWoodCost : 0,
                lapidarySizeCells: JewelsAllowed(faction) ? rules.LapidarySizeCells : 0,
                jewelTicks: JewelsAllowed(faction) ? rules.JewelTicks : 0,
                jewelStoneInput: JewelsAllowed(faction) ? rules.JewelStoneInput : 0,
                jewelOreInput: JewelsAllowed(faction) ? rules.JewelOreInput : 0,
                jewelMaxLapidaries: JewelsAllowed(faction) ? rules.JewelMaxLapidaries : 0,
                jewelKeepStone: JewelsAllowed(faction) ? rules.JewelKeepStone : 0,
                jewelMercenaryGems: JewelsAllowed(faction) ? rules.JewelMercenaryGems : 0,
                jewelMercenaryFood: JewelsAllowed(faction) ? rules.JewelMercenaryFood : 0,
                jewelMercenaryInfantryRatio: JewelsAllowed(faction) ? rules.JewelMercenaryInfantryRatio : 0,
                tradeGoodsEnabled: TradeGoodsAllowed(faction),
                weaverWoodCost: TradeGoodsAllowed(faction) ? rules.WeaverWoodCost : 0,
                weaverSizeCells: TradeGoodsAllowed(faction) ? rules.WeaverSizeCells : 0,
                tradeGoodsTicks: TradeGoodsAllowed(faction) ? rules.TradeGoodsTicks : 0,
                tradeGoodsWoodInput: TradeGoodsAllowed(faction) ? rules.TradeGoodsWoodInput : 0,
                tradeGoodsKeepWood: TradeGoodsAllowed(faction) ? rules.TradeGoodsKeepWood : 0,
                tradeGoodsValue: TradeGoodsAllowed(faction) ? rules.TradeGoodsValue : 0,
                tradeGoodsMaxWeavers: TradeGoodsAllowed(faction) ? rules.TradeGoodsMaxWeavers : 0,
                tradeGoodsWoodReserve: TradeGoodsAllowed(faction) ? rules.TradeGoodsWoodReserve : 0,
                incenseEnabled: IncenseAllowed(faction),
                incense: IncenseAllowed(faction) ? economy.Incense : 0,
                incenseHouseWoodCost: IncenseAllowed(faction) ? rules.IncenseWoodCost : 0,
                incenseHouseSizeCells: IncenseAllowed(faction) ? rules.IncenseSizeCells : 0,
                incenseTicks: IncenseAllowed(faction) ? rules.IncenseTicks : 0,
                incenseWoodInput: IncenseAllowed(faction) ? rules.IncenseWoodInput : 0,
                incenseFoodInput: IncenseAllowed(faction) ? rules.IncenseFoodInput : 0,
                incenseKeepWood: IncenseAllowed(faction) ? rules.IncenseKeepWood : 0,
                incenseKeepFood: IncenseAllowed(faction) ? rules.IncenseKeepFood : 0,
                incenseStockLimit: IncenseAllowed(faction) ? rules.IncenseStockLimit : 0,
                incenseHealIntervalTicks: IncenseAllowed(faction) ? rules.IncenseHealIntervalTicks : 0,
                incenseHealHp: IncenseAllowed(faction) ? rules.IncenseHealHp : 0,
                incenseMaxHouses: IncenseAllowed(faction) ? rules.IncenseMaxHouses : 0,
                incenseWoodReserve: IncenseAllowed(faction) ? rules.IncenseWoodReserve : 0,
                charmsEnabled: CharmsAllowed(faction), charms: CharmsAllowed(faction) ? economy.Charms : 0,
                charmWorkshopWoodCost: CharmsAllowed(faction) ? rules.CharmWoodCost : 0,
                charmWorkshopSizeCells: CharmsAllowed(faction) ? rules.CharmSizeCells : 0,
                charmTicks: CharmsAllowed(faction) ? rules.CharmTicks : 0,
                charmWoodInput: CharmsAllowed(faction) ? rules.CharmWoodInput : 0,
                charmKeepWood: CharmsAllowed(faction) ? rules.CharmKeepWood : 0,
                charmStockLimit: CharmsAllowed(faction) ? rules.CharmStockLimit : 0,
                charmMaxWorkshops: CharmsAllowed(faction) ? rules.CharmMaxWorkshops : 0,
                charmWoodReserve: CharmsAllowed(faction) ? rules.CharmWoodReserve : 0,
                charmHpPermille: CharmsAllowed(faction) ? rules.CharmHpPermille : 0,
                repairKitsEnabled: RepairKitsAllowed(faction), repairKits: RepairKitsAllowed(faction) ? economy.RepairKits : 0,
                repairYardWoodCost: RepairKitsAllowed(faction) ? rules.RepairYardWoodCost : 0,
                repairYardSizeCells: RepairKitsAllowed(faction) ? rules.RepairYardSizeCells : 0,
                repairKitTicks: RepairKitsAllowed(faction) ? rules.RepairKitTicks : 0,
                repairKitWoodInput: RepairKitsAllowed(faction) ? rules.RepairKitWoodInput : 0,
                repairKitKeepWood: RepairKitsAllowed(faction) ? rules.RepairKitKeepWood : 0,
                repairKitStockLimit: RepairKitsAllowed(faction) ? rules.RepairKitStockLimit : 0,
                repairKitMaxYards: RepairKitsAllowed(faction) ? rules.RepairKitMaxYards : 0,
                repairKitWoodReserve: RepairKitsAllowed(faction) ? rules.RepairKitWoodReserve : 0,
                repairKitIntervalTicks: RepairKitsAllowed(faction) ? rules.RepairKitIntervalTicks : 0,
                repairKitHp: RepairKitsAllowed(faction) ? rules.RepairKitHp : 0,
                toolsEnabled: ToolsAllowed(faction), tools: ToolsAllowed(faction) ? economy.Tools : 0,
                toolShopWoodCost: ToolsAllowed(faction) ? rules.ToolShopWoodCost : 0,
                toolShopSizeCells: ToolsAllowed(faction) ? rules.ToolShopSizeCells : 0,
                toolTicks: ToolsAllowed(faction) ? rules.ToolTicks : 0,
                toolWoodInput: ToolsAllowed(faction) ? rules.ToolWoodInput : 0,
                toolKeepWood: ToolsAllowed(faction) ? rules.ToolKeepWood : 0,
                toolStockLimit: ToolsAllowed(faction) ? rules.ToolStockLimit : 0,
                toolMaxShops: ToolsAllowed(faction) ? rules.ToolMaxShops : 0,
                toolWoodReserve: ToolsAllowed(faction) ? rules.ToolWoodReserve : 0,
                toolVillagerFoodPermille: ToolsAllowed(faction) ? rules.ToolVillagerFoodPermille : 0,
                 toolVillagerTicksPermille: ToolsAllowed(faction) ? rules.ToolVillagerTicksPermille : 0,
                 goodsMarketEnabled: GoodsMarketOn,
                 goodsTradeLot: GoodsMarketOn ? rules.GoodsTradeLot : 0,
                 goodsPrices: GoodsMarketOn ? GoodsPriceViews(faction) : null,
                 adjacentBuildings: rules.AdjacentBuildings,
                 buildingClearanceCells: rules.AdjacentBuildings ? rules.AdjacentBuildingClearanceCells : BuildingClearanceCells,
                 coreClearanceCells: CoreClearanceCells, nodeClearanceCells: NodeClearanceCells,
                 mapWidthCells: world.Config.Map.WidthCells, mapHeightCells: world.Config.Map.HeightCells,
                 cellSizeMeters: world.Config.Map.CellSizeMeters, passableCells: passableCells, riverCells: riverCells,
                craftTree1Enabled: CraftTree1On,
                cutStone: CraftTree1On ? economy.CutStone : 0,
                frames: CraftTree1On ? economy.Frames : 0,
                stonecutterWoodCost: CraftTree1On ? rules.StonecutterWoodCost : 0,
                stonecutterSizeCells: CraftTree1On ? rules.StonecutterSizeCells : 0,
                stonecutterTicks: CraftTree1On ? rules.CraftTreeStonecutterTicks : 0,
                stonecutterStoneInput: CraftTree1On ? rules.CraftTreeStoneInput : 0,
                joineryWoodCost: JoineryAllowed(faction) ? rules.JoineryWoodCost : 0,
                joinerySizeCells: JoineryAllowed(faction) ? rules.JoinerySizeCells : 0,
                joineryTicks: JoineryAllowed(faction) ? rules.CraftTreeJoineryTicks : 0,
                joineryPlankInput: JoineryAllowed(faction) ? rules.CraftTreeJoineryPlankInput : 0,
                joineryCutStoneInput: JoineryAllowed(faction) ? rules.CraftTreeJoineryCutStoneInput : 0,
                framesPerBuilding: CraftTree1On ? rules.CraftTreeFramesPerBuilding : 0,
                frameWoodPermille: CraftTree1On ? rules.CraftTreeFrameWoodPermille : 0,
                frameWorkPermille: CraftTree1On ? rules.CraftTreeFrameWorkPermille : 0,
                level2FramesCost: CraftTree1On ? rules.CraftTreeLevel2FramesCost : 0,
                dropSiteMax: DropSiteLimitOn ? rules.DropSiteMax : 0,
                dropSiteCount: DropSiteLimitOn ? AliveDropSites(faction) : 0,
                craftTree2Enabled: CraftTree2On,
                armsKit: CraftTree2On ? economy.ArmsKit : 0,
                scholarly: CraftTree2On ? economy.Scholarly : 0,
                fineArmsKit: CraftTree2On ? economy.FineArmsKit : 0,
                assemblerWoodCost: AssemblerAllowed(faction) ? rules.AssemblerWoodCost : 0,
                assemblerSizeCells: AssemblerAllowed(faction) ? rules.AssemblerSizeCells : 0,
                armsKitAttack: CraftTree2On ? rules.ArmsKitAttack : 0,
                armsKitHpPermille: CraftTree2On ? rules.ArmsKitHpPermille : 0,
                scholarlyPerResearch: CraftTree2On ? rules.ScholarlyPerResearch : 0,
                scholarlyCostPermille: CraftTree2On ? rules.ScholarlyCostPermille : 0,
                scholarlyTimePermille: CraftTree2On ? rules.ScholarlyTimePermille : 0,
                armsKitTicks: CraftTree2On ? rules.ArmsKitTicks : 0,
                armsKitPlankInput: CraftTree2On ? rules.ArmsKitPlankInput : 0,
                armsKitCharcoalInput: CraftTree2On ? rules.ArmsKitCharcoalInput : 0,
                armsKitSteelInput: CraftTree2On ? rules.ArmsKitSteelInput : 0,
                scholarlyTicks: CraftTree2On ? rules.ScholarlyTicks : 0,
                scholarlyPlankInput: CraftTree2On ? rules.ScholarlyPlankInput : 0,
                scholarlyCharcoalInput: CraftTree2On ? rules.ScholarlyCharcoalInput : 0,
                 scholarlyFoodInput: CraftTree2On ? rules.ScholarlyFoodInput : 0,
                 craftTree3Enabled: CraftTree3On,
                 siegeParts: CraftTree3On ? economy.SiegeParts : 0,
                 mechanism: CraftTree3On ? economy.Mechanism : 0,
                 siegePartsTicks: CraftTree3On ? rules.SiegePartsTicks : 0,
                 mechanismTicks: CraftTree3On ? rules.MechanismTicks : 0,
                 siegePartsHpPermille: CraftTree3On ? rules.SiegePartsHpPermille : 0,
                 siegePartsDamagePermille: CraftTree3On ? rules.SiegePartsDamagePermille : 0,
                 siegePartsPerCastle: CraftTree3On ? rules.SiegePartsPerCastle : 0,
                 siegePartsCastleStonePermille: CraftTree3On ? rules.SiegePartsCastleStonePermille : 0,
                 mechanismBuildingTimePermille: CraftTree3On ? rules.MechanismBuildingTimePermille : 0,
                 mechanismBuildingMaterialPermille: CraftTree3On ? rules.MechanismBuildingMaterialPermille : 0,
                 mechanismResearchCostPermille: CraftTree3On ? rules.MechanismResearchCostPermille : 0,
                 mechanismResearchTimePermille: CraftTree3On ? rules.MechanismResearchTimePermille : 0,
                 craftTree4Enabled: CraftTree4On,
                 siegeEngine: CraftTree4On ? economy.SiegeEngine : 0,
                 eliteKit: CraftTree4On ? economy.EliteKit : 0,
                 siegeEngineTicks: CraftTree4On ? rules.SiegeEngineTicks : 0,
                 eliteKitTicks: CraftTree4On ? rules.EliteKitTicks : 0,
                 eliteKitAttack: CraftTree4On ? rules.EliteKitAttack : 0,
                 eliteKitHpPermille: CraftTree4On ? rules.EliteKitHpPermille : 0,
                 cannonFoodCost: CraftTree4On ? rules.CannonFoodCost : 0,
                 cannonWoodCost: CraftTree4On ? rules.CannonWoodCost : 0,
                 cannonRange: CraftTree4On ? (int)rules.CannonRange.Raw : 0,
                 cannonSiegeDamage: CraftTree4On ? rules.CannonSiegeDamage : 0,
                 siegeEngineProduced: CraftTree4On ? economy.SiegeEngineProduced : 0,
                 siegeEngineConsumed: CraftTree4On ? economy.SiegeEngineConsumed : 0,
                 eliteKitProduced: CraftTree4On ? economy.EliteKitProduced : 0,
                 eliteKitConsumed: CraftTree4On ? economy.EliteKitConsumed : 0,
                 cannonProduced: CraftTree4On ? economy.CannonProduced : 0,
                civChoiceDeadlineTick: civChoiceDeadline,
                ageClockUx: AgeClockUxOn,
                commonDrill: CommonDrillOn, drillWoodCost: CommonDrillOn ? rules.DrillWoodCost : 0,
                drillStoneCost: CommonDrillOn ? rules.DrillStoneCost : 0, drillSizeCells: CommonDrillOn ? rules.DrillSizeCells : 0,
                inserters: InsertersOn, inserterWoodCost: InsertersOn ? rules.InserterWoodCost : 0,
                inserterIntervalTicks: InsertersOn ? rules.InserterIntervalTicks : 0,
                easyRebuild: EasyRebuildOn,
                  buildingReservations: BuildingReservationsOn, reservationLimit: BuildingReservationsOn ? rules.ReservationLimit : 0,
                  reservationCount: BuildingReservationsOn ? ReservationCount(faction) : 0,
                  reservationCheckTicks: BuildingReservationsOn ? rules.ReservationCheckTicks : 0,
                  ageFour: AgeFourOn,
                  age4FoodCost: AgeFourOn ? rules.Age4FoodCost : 0,
                  age4WoodCost: AgeFourOn ? rules.Age4WoodCost : 0,
                  age4Ticks: AgeFourOn ? rules.Age4Ticks : 0,
                  totalWarFoodCost: AgeFourOn ? rules.TotalWarFoodCost : 0,
                  totalWarWoodCost: AgeFourOn ? rules.TotalWarWoodCost : 0,
                  totalWarMetalCost: AgeFourOn ? rules.TotalWarMetalCost : 0,
                  totalWarTicks: AgeFourOn ? rules.TotalWarTicks : 0,
                  grandWorkshopFoodCost: AgeFourOn ? rules.GrandWorkshopFoodCost : 0,
                  grandWorkshopWoodCost: AgeFourOn ? rules.GrandWorkshopWoodCost : 0,
                   grandWorkshopMetalCost: AgeFourOn ? rules.GrandWorkshopMetalCost : 0,
                   grandWorkshopTicks: AgeFourOn ? rules.GrandWorkshopTicks : 0,
                    ageStrengths: AgeStrengthsOn,
                    ageFourAiResearchFailureTech: AgeFourAiOn ? economy.AgeFourAiResearchFailureTech : 0,
                    ageFourAiResearchFailureReason: AgeFourAiOn ? economy.AgeFourAiResearchFailureReason : (byte)0,
                    ageFourAiResearchFailureTick: AgeFourAiOn ? economy.AgeFourAiResearchFailureTick : 0,
                    primitiveCraftEnabled: PrimitiveCraftOn,
                    stoneTools: PrimitiveCraftOn ? economy.StoneTools : 0,
                    jerky: PrimitiveCraftOn ? economy.Jerky : 0,
                    hutWoodCost: PrimitiveCraftOn ? rules.HutWoodCost : 0,
                    hutSizeCells: PrimitiveCraftOn ? rules.HutSizeCells : 0,
                    primitiveCraftTicks: PrimitiveCraftOn ? rules.PrimitiveCraftTicks : 0,
                    stoneToolsStoneInput: PrimitiveCraftOn ? rules.StoneToolsStoneInput : 0,
                    stoneToolsWoodInput: PrimitiveCraftOn ? rules.StoneToolsWoodInput : 0,
                    jerkyFoodInput: PrimitiveCraftOn ? rules.JerkyFoodInput : 0,
                    jerkyOutput: PrimitiveCraftOn ? rules.JerkyOutput : 0,
                    stoneToolsGatherIntervalPermille: PrimitiveCraftOn ? rules.StoneToolsGatherIntervalPermille : 0,
                    jerkyHpPermille: PrimitiveCraftOn ? rules.JerkyHpPermille : 0);
        }

        // The placement ghost reads the map from the frame. Building it per frame cost two whole-map arrays (and two copies)
        // every tick for each side, so the views are kept and rebuilt only when a building opens or closes a cell.
        // SharedPrefixList with no replacements is a read-only view that ContractList keeps without copying.
        private SharedPrefixList<bool> placementPassableView, placementRiverView;
        private int placementPassableVersion = -1;

        private IReadOnlyList<bool> PlacementPassableView()
        {
            if (placementPassableView != null && placementPassableVersion == world.Map.PassableVersion) return placementPassableView;
            int cells = checked(world.Config.Map.WidthCells * world.Config.Map.HeightCells);
            var passable = new bool[cells];
            for (int cell = 0; cell < cells; cell++) passable[cell] = world.Map.IsPassable(cell);
            placementPassableView = new SharedPrefixList<bool>(passable, cells, Array.Empty<int>(), Array.Empty<bool>());
            placementPassableVersion = world.Map.PassableVersion;
            return placementPassableView;
        }

        private IReadOnlyList<bool> PlacementRiverView()
        {
            if (placementRiverView != null) return placementRiverView; // the terrain never changes
            int cells = checked(world.Config.Map.WidthCells * world.Config.Map.HeightCells);
            var river = new bool[cells];
            for (int cell = 0; cell < cells; cell++) river[cell] = IsRiverCell(cell);
            placementRiverView = new SharedPrefixList<bool>(river, cells, Array.Empty<int>(), Array.Empty<bool>());
            return placementRiverView;
        }

        private CavalryMissionView CavalryMissionFor(uint faction)
        {
            if (!CavalryOn) return default(CavalryMissionView);
            foreach (uint armyId in world.Factions[faction - 1].ArmyIds)
            {
                var army = world.Armies[armyId - 1];
                bool mobile = false;
                foreach (uint soldierId in army.SoldierIds)
                    if (world.Soldiers[soldierId - 1].Alive && world.Soldiers[soldierId - 1].Class == UnitKind.LightCavalry) { mobile = true; break; }
                if (!mobile || army.Decision.Goal.Kind != GoalKind.Point) continue;
                uint id = army.Decision.Goal.Id;
                RaidTargetKind kind = (id & 0x80000000U) != 0 ? RaidTargetKind.IsolatedArmy
                    : (id & 0x40000000U) != 0 ? RaidTargetKind.Carrier : RaidTargetKind.Resource;
                uint first = 0;
                foreach (uint soldierId in army.SoldierIds)
                    if (world.Soldiers[soldierId - 1].Alive) { first = soldierId; break; }
                if (first == 0) continue;
                var lead = world.Soldiers[first - 1];
                var path = world.Map.FindPath(world.Map.Cell(lead.Position), army.Decision.Goal.Point, army.Definition.FactionId);
                long eta = path.Length == 0 ? -1 : checked((long)path.Length * 20);
                return new CavalryMissionView(kind, id, army.Decision.Goal.Point, eta, army.Decision.Returning);
            }
            return default(CavalryMissionView);
        }

        private static VillagerActivity Activity(VillagerTask task) => task switch
        {
            VillagerTask.ToNode => VillagerActivity.ToResource,
            VillagerTask.Gathering => VillagerActivity.Gathering,
            VillagerTask.ToDropOff => VillagerActivity.Returning,
            VillagerTask.ToBuild => VillagerActivity.ToBuild,
            VillagerTask.Building => VillagerActivity.Building,
            VillagerTask.ToPickup => VillagerActivity.Hauling,
            VillagerTask.ToDeliver => VillagerActivity.Hauling,
            VillagerTask.ToTradeMarket => VillagerActivity.Trading,
            VillagerTask.ToTradeCore => VillagerActivity.Trading,
            VillagerTask.ToCaravanMarket => VillagerActivity.Trading,
            VillagerTask.ToCaravanserai => VillagerActivity.Trading,
            VillagerTask.ToCaravanCore => VillagerActivity.Trading,
            _ => VillagerActivity.Idle
        };

        private CaravanStopReason CaravanStopReasonFor(VillagerState v)
        {
            if (v.CaravanMarketId == 0 && v.CaravanseraiId == 0 && v.CaravanOutpostId == 0) return CaravanStopReason.None;
            if (v.CaravanDangerStopped) return CaravanStopReason.Danger;
            if (v.CaravanMarketId == 0 || v.CaravanMarketId > world.BuildingCount) return CaravanStopReason.MarketLost;
            var market = world.Buildings[v.CaravanMarketId - 1];
            if (!IsActualBuilding(market) || !market.Complete || market.FactionId != v.FactionId || market.Kind != BuildingKind.Market)
                return CaravanStopReason.MarketLost;
            if (v.CaravanseraiId == 0 || v.CaravanseraiId > world.BuildingCount) return CaravanStopReason.HostLost;
            var host = world.Buildings[v.CaravanseraiId - 1];
            if (!IsActualBuilding(host) || !host.Complete || host.FactionId != v.FactionId || host.Kind != BuildingKind.Caravanserai)
                return CaravanStopReason.HostLost;
            if (v.CaravanOutpostId == 0 || v.CaravanOutpostId > world.Outposts.Length
                || world.Outposts[v.CaravanOutpostId - 1].OwnerFactionId != v.FactionId) return CaravanStopReason.OutpostLost;
            return CaravanStopReason.Normal;
        }
    }
}
