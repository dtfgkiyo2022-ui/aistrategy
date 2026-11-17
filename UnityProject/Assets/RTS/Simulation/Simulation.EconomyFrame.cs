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
                int craftProduced = 0, craftStarved = 0, craftBlocked = 0, craftFullRate = 0;
                IReadOnlyList<CraftFlowInputView> craftInputs = Array.Empty<CraftFlowInputView>();
                if (own && CraftFlowOn && IsCraftProcessingBuilding(b.Kind))
                    CraftFlowData(b, out craftProduced, out craftStarved, out craftBlocked, out craftInputs, out craftFullRate);
                // 条件式は呼び出しの前に（巨大な new の引数に ? : を足すと製品版でスタックがあふれる）
                ResourceKind missingMaterial = 0;
                MaterialSupplyView materialSupply = MaterialSupplyView.None;
                if (own) CarriedSupplyView(b, out missingMaterial, out materialSupply);
                buildings.Add(new BuildingView(b.Id, b.FactionId, b.Kind, BuildingCenter(b), size * world.Config.Map.CellSizeMeters,
                    b.Hp, MaxHpOf(b), b.Complete, own ? b.Progress : 0, WorkOf(b),
                    own ? b.Queued : 0, own ? b.TrainRemaining : 0, b.Facing, own ? b.Input : 0, own ? b.Output : 0,
                    own && b.Held,
                    own ? b.Researching : 0, own && b.Researching != 0 ? b.TrainRemaining : 0,
                    own && (ProcessingOn || ForestryOn || BricksOn || TackOn || BooksOn || PreservesOn || JewelsOn || TradeGoodsOn || IncenseOn || RepairKitsOn || ToolsOn) ? b.InputSecondary : 0,
                    own && ForestryOn && economy.Age >= 2 ? b.QueuedBowGear : 0,
                    own && b.Kind == BuildingKind.Caravanserai ? b.CaravanMarketId : 0,
                    own && b.Kind == BuildingKind.Caravanserai ? b.CaravanOutpostId : 0,
                    own && b.Kind == BuildingKind.Caravanserai ? b.CaravanDistance : default,
                    own && b.Kind == BuildingKind.Caravanserai ? b.CaravanWoodReward : 0, caravanReason,
                    CraftTree1On && IsCraftProcessingBuilding(b.Kind) ? b.Level : 1,
                     own && (CraftTree2On || PrimitiveCraftOn) ? b.InputTertiary : 0, own && (CraftTree2On || PrimitiveCraftOn) ? b.Recipe : RecipeKind.None,
                     b.Reserved, b.Reserved ? ReservationOrderFor(b) : 0,
                     producedPerMinute: craftProduced, inputsPerMinute: craftInputs, starvedTicksLastMinute: craftStarved,
                     blockedTicksLastMinute: craftBlocked, fullRatePerMinute: craftFullRate,
                     aiReason: own && AiReasonsOn ? b.AiReason : AiReason.None,
                     aiReasonValue: own && AiReasonsOn ? b.AiReasonValue : 0,
                     missingMaterial: missingMaterial, materialSupply: materialSupply,
                     moduleCount: own && ModulesOn ? b.ModuleCount : 0));
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
            var supplyBuildingByCell = CarriedSupplyBuildingsByCell();
            for (int cell = 0; cell < world.Belts.Length; cell++)
            {
                var b = world.Belts[cell];
                if (b.FactionId == 0) continue;
                bool own = b.FactionId == faction;
                if (!own && !world.Factions[faction - 1].VisibleCells[cell]) continue;
                uint supplyBuildingId = 0;
                if (own && supplyBuildingByCell != null) supplyBuildingByCell.TryGetValue(cell, out supplyBuildingId);
                int beltHp = b.Hp;
                belts.Add(new BeltView(cell, b.FactionId, b.Facing, own ? b.Item : 0, own ? b.Progress : 0, own && b.Held,
                    own ? b.Component : BeltComponentKind.None, own ? b.SorterKind : (ResourceKind)0, own ? b.PairCell : -1,
                    own && b.VeryFast ? BeltSpeed.VeryFast : own && b.Fast ? BeltSpeed.Fast : BeltSpeed.Normal,
                    own && BeltStackOn ? BeltItemCount(b) : 1,
                    own && BeltCraftOn ? b.Priority : BeltPriority.None,
                    own && AiReasonsOn ? b.AiReason : AiReason.None,
                    own && AiReasonsOn ? b.AiReasonValue : 0, supplyBuildingId, beltHp));
            }
            var catapultShots = new List<CatapultShotView>();
            if (BeltCraftOn)
                for (int i = 0; i < world.Catapults.Length; i++)
                {
                    var catapult = world.Catapults[i];
                    if (!catapult.Active || catapult.FactionId != faction) continue;
                    for (int j = 0; j < catapult.Shots.Length; j++)
                    {
                        var shot = catapult.Shots[j];
                        if (shot.Active) catapultShots.Add(new CatapultShotView(catapult.LauncherCell, catapult.ReceiverCell, shot.Item, shot.Count,
                            shot.RemainingTicks, rules.CatapultFlightTicks));
                    }
                }
            var cavalryMission = CavalryMissionFor(faction);
            var advancePrice = AgesOn ? AdvancePrice(faction, economy) : (food: 0, wood: 0, gold: 0, ticks: 0);
            var infantryTrainingCost = CostOf(faction, UnitKind.Infantry);
            var archerTrainingCost = CostOf(faction, UnitKind.Archer);
            var cavalryTrainingCost = CostOf(faction, UnitKind.Cavalry);
            var ramTrainingCost = CostOf(faction, UnitKind.Ram);
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
            var craftBuildingCosts = CraftRequiredBuildingDisplayCosts(faction);
            bool craftRequiredBelts = CraftRequiredOn && economy.Age >= 1;
            int fastBeltPlanks = craftRequiredBelts ? 1 : 0;
            int splitterPlanks = craftRequiredBelts ? (rules.SplitterWoodCost + 1) / 2 : 0;
            int sorterPlanks = craftRequiredBelts ? (rules.SorterWoodCost + 1) / 2 : 0;
            int undergroundPlanks = craftRequiredBelts ? (rules.UndergroundBeltWoodCost + 1) / 2 : 0;
            var advanceCraft = CraftRequiredAdvanceMaterials(economy);
            // IL2CPP keeps a copy of every earlier argument for each `? :` inside this call, so the frame grows as
            // arguments x conditionals. Twenty more of them overflowed the 1 MB main-thread stack in the player (build1031a,
            // __chkstk in EconomyViewFor). Work new conditionals out here, not inside the argument list.
            int beltPlanksPerTwo = craftRequiredBelts ? 1 : 0;
            int fittingsView = CraftDepthOn ? economy.Fittings : 0, ropeView = CraftDepthOn ? economy.Rope : 0;
            bool beltCraft = BeltCraftOn;
            int gearsView = beltCraft ? economy.Gears : 0;
            int filterInserterGears = beltCraft ? rules.FilterInserterGears : 0, longInserterGears = beltCraft ? rules.LongInserterGears : 0;
            int catapultGears = beltCraft ? rules.CatapultGears : 0, catapultPlanks = beltCraft ? rules.CatapultPlanks : 0;
            int catapultFittings = beltCraft && CraftDepthOn ? rules.CatapultFittings : 0, catapultRope = beltCraft && CraftDepthOn ? rules.CatapultRope : 0;
            int catapultSteel = beltCraft && !CraftDepthOn ? rules.CatapultSteel : 0;
            int catapultReceiverGears = beltCraft ? rules.CatapultReceiverGears : 0, catapultReceiverPlanks = beltCraft ? rules.CatapultReceiverPlanks : 0;
            int catapultMinAge = beltCraft ? rules.CatapultMinAge : 0, catapultRangeCells = beltCraft ? rules.CatapultRangeCells : 0;
            int catapultLoad = beltCraft ? rules.CatapultLoad : 0, catapultMaxWaitTicks = beltCraft ? rules.CatapultMaxWaitTicks : 0;
             int catapultIntervalTicks = beltCraft ? rules.CatapultIntervalTicks : 0, catapultFlightTicks = beltCraft ? rules.CatapultFlightTicks : 0;
             int catapultBuffer = beltCraft ? rules.CatapultBuffer : 0;
             int livelihoodMet = LivelihoodOn ? economy.LivelihoodMetCount : 0;
             int livelihoodRequired = LivelihoodOn ? economy.LivelihoodRequiredCount : 0;
               int livelihoodShortGoods = LivelihoodOn ? economy.LivelihoodShortGoods : 0;
            int gateStoneView = FortifyOn ? StoneOf(BuildingKind.Gate, faction) : 0;
               int slagView = ByproductsOn ? economy.Slag : 0;
               int blastFurnaceSizeView = ByproductsOn ? rules.BlastFurnaceSizeCells : 0;
               int blastFurnaceWoodCostView = ByproductsOn ? rules.BlastFurnaceWoodCost : 0;
               int blastFurnaceBrickCostView = ByproductsOn ? rules.BlastFurnaceBrickCost : 0;
               bool glassworksView = GlassworksOn;
               int sandView = glassworksView ? economy.Sand : 0;
               int glassView = glassworksView ? economy.Glass : 0;
               int lensView = glassworksView ? economy.Lens : 0;
               int glassSandInputView = glassworksView ? rules.GlassSandInput : 0;
               int glassCharcoalInputView = glassworksView ? rules.GlassCharcoalInput : 0;
               int glassTicksView = glassworksView ? rules.GlassTicks : 0;
               int lensGlassInputView = glassworksView ? rules.LensGlassInput : 0;
               int lensGearsInputView = glassworksView ? rules.LensGearsInput : 0;
               int lensTicksView = glassworksView ? rules.LensTicks : 0;
               int archerLensCostView = glassworksView ? rules.ArcherLensCost : 0;
               int towerGlassCostView = glassworksView ? rules.TowerGlassCost : 0;
               int castleGlassCostView = glassworksView ? rules.CastleGlassCost : 0;
               int towerGlassVisionBonusView = glassworksView ? rules.TowerGlassVisionBonus : 0;
               int glassMaxLinesView = glassworksView ? rules.GlassMaxLines : 0;
               bool modulesView = ModulesOn;
               int precisionView = modulesView ? economy.Precision : 0;
               int precisionLensInputView = modulesView ? rules.PrecisionLensInput : 0;
               int precisionGearsInputView = modulesView ? rules.PrecisionGearsInput : 0;
               int precisionMetalInputView = modulesView ? rules.PrecisionMetalInput : 0;
               int precisionTicksView = modulesView ? rules.PrecisionTicks : 0;
               int precisionLensCapacityView = modulesView ? rules.PrecisionLensInputCapacity : 0;
               int precisionGearsCapacityView = modulesView ? rules.PrecisionGearsInputCapacity : 0;
               int precisionMetalCapacityView = modulesView ? rules.PrecisionMetalInputCapacity : 0;
               int moduleSlotsView = modulesView ? rules.ModuleSlots : 0;
               int moduleSpeedView = modulesView ? rules.ModuleSpeedPermille : 0;
               int moduleMinAgeView = modulesView ? rules.ModuleMinAge : 0;
               int cannonPrecisionCostView = modulesView ? rules.CannonPrecisionCost : 0;
               int cannonPrecisionMinAgeView = modulesView ? rules.CannonPrecisionMinAge : 0;
               int precisionMaxLinesView = modulesView ? rules.PrecisionMaxLines : 0;
               int aiModulesPerDecisionView = modulesView ? rules.AiModulesPerDecision : 0;
               int aiPrecisionReserveView = modulesView ? rules.AiPrecisionReserve : 0;
            // Hoisted conditionals (IL2CPP stack size, see the note above).
            var hv0 = FarmingAllowed(faction) ? rules.FarmWoodCost : 0;
            var hv1 = FarmingAllowed(faction) ? rules.FarmSizeCells : 0;
            var hv2 = rules.Ages ? rules.ScoutFoodCost : 0;
            var hv3 = rules.Ages ? rules.HouseWoodCost : 0;
            var hv4 = rules.Ages ? rules.DropSiteWoodCost : 0;
            var hv5 = rules.Ages ? StoneOf(BuildingKind.Wall, faction) : 0;
            var hv6 = rules.Ages ? BuildingWoodDisplayCost(faction, BuildingKind.Tower, WoodOf(BuildingKind.Tower, faction)) : 0;
            var hv7 = rules.Ages ? StoneOf(BuildingKind.Tower, faction) : 0;
            var hv8 = rules.Ages ? BuildingWoodDisplayCost(faction, BuildingKind.Blacksmith, rules.BlacksmithWoodCost) : 0;
            var hv9 = rules.Ages ? rules.TechFood : null;
            var hv10 = rules.Ages ? rules.TechWood : null;
            var hv11 = rules.Ages ? rules.TechMetal : null;
            var hv12 = rules.Ages ? age2Food : 0;
            var hv13 = rules.Ages ? age2Wood : 0;
            var hv14 = rules.Ages ? archerTrainingCost.food : 0;
            var hv15 = rules.Ages ? archerTrainingCost.wood : 0;
            var hv16 = rules.Ages ? cavalryTrainingCost.food : 0;
            var hv17 = rules.Ages ? cavalryTrainingCost.wood : 0;
            var hv18 = rules.Ages ? cavalryTrainingCost.metal : 0;
            var hv19 = rules.Ages ? BuildingWoodDisplayCost(faction, BuildingKind.Market, rules.MarketWoodCost) : 0;
            var hv20 = rules.Ages ? BuildingWoodDisplayCost(faction, BuildingKind.SiegeWorkshop, rules.WorkshopWoodCost) : 0;
            var hv21 = rules.Ages ? rules.TradeLot : 0;
            var hv22 = rules.Ages ? rules.TradeReturn : 0;
            var hv23 = rules.Ages ? ramTrainingCost.food : 0;
            var hv24 = rules.Ages ? ramTrainingCost.wood : 0;
            var hv25 = rules.Ages ? age3Food : 0;
            var hv26 = rules.Ages ? age3Wood : 0;
            var hv27 = rules.Ages ? BuildingWoodDisplayCost(faction, BuildingKind.ArcheryRange, rules.RangeWoodCost) : 0;
            var hv28 = rules.Ages ? BuildingWoodDisplayCost(faction, BuildingKind.Stable, rules.StableWoodCost) : 0;
            var hv29 = rules.Ages ? BuildingWoodDisplayCost(faction, BuildingKind.Castle, WoodOf(BuildingKind.Castle, faction)) : 0;
            var hv30 = rules.Ages ? StoneOf(BuildingKind.Castle, faction) : 0;
            var hv31 = rules.Ages ? economy.Gems : 0;
            var hv32 = rules.Ages ? rules.GemsTradeReturn : 0;
            var hv33 = rules.Ages ? rules.TechGems : null;
            var hv34 = rules.Ages ? rules.GemArmorHp : 0;
            var hv35 = rules.Ages ? rules.MercenaryGems : 0;
            var hv36 = rules.Ages ? rules.MercenaryTicks : 0;
            var hv37 = rules.MonksEnabled ? rules.MonkFoodCost : 0;
            var hv38 = rules.MonksEnabled ? rules.MonkGoldCost : 0;
            var hv39 = rules.MonksEnabled ? rules.MonkTrainTicks : 0;
            var hv40 = ProcessingOn ? rules.CharcoalKilnWoodCost : 0;
            var hv41 = ProcessingOn ? rules.SteelworksWoodCost : 0;
            var hv42 = ProcessingOn ? rules.CharcoalKilnSizeCells : 0;
            var hv43 = ProcessingOn ? rules.SteelworksSizeCells : 0;
            var hv44 = ProcessingOn ? rules.HeavyInfantryFoodCost : 0;
            var hv45 = ProcessingOn ? rules.HeavyInfantrySteelCost : 0;
            var hv46 = ProcessingOn ? economy.Charcoal : 0;
            var hv47 = ProcessingOn ? economy.Steel : 0;
            var hv48 = (ProcessingOn || BricksOn || TackOn || PlanksOn || BooksOn || PreservesOn || JewelsOn || TradeGoodsOn || IncenseOn || CharmsOn || RepairKitsOn || ToolsOn || ForestryOn && economy.Age >= 2) ? new List<LineView>(LineViews(faction)) : null;
            var hv49 = rules.GoldEnabled ? economy.Gold : 0;
            var hv50 = ForestryOn ? economy.BowGear : 0;
            var hv51 = ForestryOn ? rules.FletcherWoodCost : 0;
            var hv52 = ForestryOn ? rules.FletcherSizeCells : 0;
            var hv53 = ForestryOn ? rules.FletcherTicks : 0;
            var hv54 = ForestryOn ? rules.SkirmishArcherFoodCost : 0;
            var hv55 = ForestryOn ? rules.SkirmishArcherBowGearCost : 0;
            var hv56 = ForestryOn ? rules.SkirmishArcherTrainTicks : 0;
            var hv57 = AgesOn ? economy.ReservedCiv : CivKind.Primitive;
            var hv58 = AgesOn && economy.Civ != CivKind.Primitive && economy.Age == 2 ? AdvancePrice(faction, economy).gold : 0;
            var hv59 = RationsAllowed(faction) ? economy.Rations : 0;
            var hv60 = RationsAllowed(faction) ? rules.MillWoodCost : 0;
            var hv61 = RationsAllowed(faction) ? rules.MillSizeCells : 0;
            var hv62 = RationsAllowed(faction) ? rules.MillTicks : 0;
            var hv63 = RationsAllowed(faction) ? rules.MillFoodInput : 0;
            var hv64 = RationsAllowed(faction) ? rules.RationsPerSoldier : 0;
            var hv65 = RationsAllowed(faction) ? rules.RationsTrainPermille : 0;
            var hv66 = RationsAllowed(faction) ? rules.RationsHealIntervalTicks : 0;
            var hv67 = RationsAllowed(faction) ? rules.RationsHealQuietTicks : 0;
            var hv68 = RationsAllowed(faction) ? rules.RationsHealHp : 0;
            var hv69 = RationsAllowed(faction) ? rules.RationsHealMaxPerInterval : 0;
            var hv70 = RationsAllowed(faction) ? rules.RationsFarmMinimum : 0;
            var hv71 = RationsAllowed(faction) ? rules.RationsFoodReserve : 0;
            var hv72 = BricksAllowed(faction) ? economy.Bricks : 0;
            var hv73 = BricksAllowed(faction) ? rules.BrickKilnWoodCost : 0;
            var hv74 = BricksAllowed(faction) ? rules.BrickKilnSizeCells : 0;
            var hv75 = BricksAllowed(faction) ? rules.BrickTicks : 0;
            var hv76 = BricksAllowed(faction) ? rules.BrickStoneInput : 0;
            var hv77 = BricksAllowed(faction) ? rules.BrickCharcoalInput : 0;
            var hv78 = BricksAllowed(faction) ? rules.BricksPerWall : 0;
            var hv79 = BricksAllowed(faction) ? rules.BricksPerTower : 0;
            var hv80 = BricksAllowed(faction) ? rules.BricksPerCastle : 0;
            var hv81 = BricksAllowed(faction) ? rules.BrickHpPermille : 0;
            var hv82 = BricksAllowed(faction) ? rules.BrickUpgradeIntervalTicks : 0;
            var hv83 = BricksAllowed(faction) ? rules.BrickCastleStonePermille : 0;
            var hv84 = TackAllowed(faction) ? economy.Tack : 0;
            var hv85 = TackAllowed(faction) ? rules.SaddleryWoodCost : 0;
            var hv86 = TackAllowed(faction) ? rules.SaddlerySizeCells : 0;
            var hv87 = TackAllowed(faction) ? rules.TackTicks : 0;
            var hv88 = TackAllowed(faction) ? rules.TackFoodInput : 0;
            var hv89 = TackAllowed(faction) ? rules.TackWoodInput : 0;
            var hv90 = CavalryTackEffectAllowed(faction) ? rules.HeavyCavalryTackCost : 0;
            var hv91 = CavalryTackEffectAllowed(faction) ? rules.HeavyCavalryFoodCost : 0;
            var hv92 = CavalryTackEffectAllowed(faction) ? rules.HeavyCavalryTrainTicks : 0;
            var hv93 = CavalryTackEffectAllowed(faction) ? rules.HeavyCavalryHp : 0;
            var hv94 = CavalryTackEffectAllowed(faction) ? rules.HeavyCavalryDamage : 0;
            var hv95 = CavalryTackEffectAllowed(faction) ? rules.HeavyCavalryAttackIntervalTicks : 0;
            var hv96 = CavalryTackEffectAllowed(faction) ? rules.HeavyCavalryRange : default(Fix64);
            var hv97 = CavalryTackEffectAllowed(faction) ? rules.HeavyCavalrySpeed : default(Fix64);
            var hv98 = CavalryTackEffectAllowed(faction) ? rules.HeavyCavalryVision : default(Fix64);
            var hv99 = PlanksAllowed(faction) || commonSawmill ? economy.Planks : 0;
            var hv100 = PlanksAllowed(faction) || commonSawmill ? rules.SawmillWoodCost : 0;
            var hv101 = PlanksAllowed(faction) || commonSawmill ? rules.SawmillSizeCells : 0;
            var hv102 = commonSawmill ? rules.CraftTreePlankTicks : PlanksAllowed(faction) ? rules.PlankTicks : 0;
            var hv103 = commonSawmill ? rules.CraftTreePlankWoodInput : PlanksAllowed(faction) ? rules.PlankWoodInput : 0;
            var hv104 = PlanksAllowed(faction) ? rules.PlanksPerRam : 0;
            var hv105 = PlanksAllowed(faction) ? rules.PlankRamHpPermille : 0;
            var hv106 = PlanksAllowed(faction) ? rules.PlankRamTrainPermille : 0;
            var hv107 = PlanksAllowed(faction) ? rules.PlanksPerBridgeCell : 0;
            var hv108 = PlanksAllowed(faction) ? rules.PlankBridgeWoodPermille : 0;
            var hv109 = PlanksAllowed(faction) ? rules.PlankExtraRams : 0;
            var hv110 = PlanksAllowed(faction) ? rules.PlankWoodReserve : 0;
            var hv111 = BooksAllowed(faction) ? economy.Books : 0;
            var hv112 = BooksAllowed(faction) ? rules.ScriptoriumWoodCost : 0;
            var hv113 = BooksAllowed(faction) ? rules.ScriptoriumSizeCells : 0;
            var hv114 = BooksAllowed(faction) ? rules.BookTicks : 0;
            var hv115 = BooksAllowed(faction) ? rules.BookGoldInput : 0;
            var hv116 = BooksAllowed(faction) ? rules.BookWoodInput : 0;
            var hv117 = BooksAllowed(faction) ? rules.BooksPerResearch : 0;
            var hv118 = BooksAllowed(faction) ? rules.BookResearchPermille : 0;
            var hv119 = BooksAllowed(faction) ? rules.BookResearchCostPermille : 0;
            var hv120 = BooksAllowed(faction) ? rules.BookWoodReserve : 0;
            var hv121 = PreservesAllowed(faction) ? rules.DryingRackWoodCost : 0;
            var hv122 = PreservesAllowed(faction) ? rules.DryingRackSizeCells : 0;
            var hv123 = PreservesAllowed(faction) ? rules.PreserveTicks : 0;
            var hv124 = PreservesAllowed(faction) ? rules.PreserveFoodInput : 0;
            var hv125 = PreservesAllowed(faction) ? rules.PreserveFoodValue : 0;
            var hv126 = PreservesAllowed(faction) ? rules.PreserveKeepFood : 0;
            var hv127 = PreservesAllowed(faction) ? rules.PreserveMaxRacks : 0;
            var hv128 = PreservesAllowed(faction) ? rules.PreserveWoodReserve : 0;
            var hv129 = JewelsAllowed(faction) ? rules.LapidaryWoodCost : 0;
            var hv130 = JewelsAllowed(faction) ? rules.LapidarySizeCells : 0;
            var hv131 = JewelsAllowed(faction) ? rules.JewelTicks : 0;
            var hv132 = JewelsAllowed(faction) ? rules.JewelStoneInput : 0;
            var hv133 = JewelsAllowed(faction) ? rules.JewelOreInput : 0;
            var hv134 = JewelsAllowed(faction) ? rules.JewelMaxLapidaries : 0;
            var hv135 = JewelsAllowed(faction) ? rules.JewelKeepStone : 0;
            var hv136 = JewelsAllowed(faction) ? rules.JewelMercenaryGems : 0;
            var hv137 = JewelsAllowed(faction) ? rules.JewelMercenaryFood : 0;
            var hv138 = JewelsAllowed(faction) ? rules.JewelMercenaryInfantryRatio : 0;
            var hv139 = TradeGoodsAllowed(faction) ? rules.WeaverWoodCost : 0;
            var hv140 = TradeGoodsAllowed(faction) ? rules.WeaverSizeCells : 0;
            var hv141 = TradeGoodsAllowed(faction) ? rules.TradeGoodsTicks : 0;
            var hv142 = TradeGoodsAllowed(faction) ? rules.TradeGoodsWoodInput : 0;
            var hv143 = TradeGoodsAllowed(faction) ? rules.TradeGoodsKeepWood : 0;
            var hv144 = TradeGoodsAllowed(faction) ? rules.TradeGoodsValue : 0;
            var hv145 = TradeGoodsAllowed(faction) ? rules.TradeGoodsMaxWeavers : 0;
            var hv146 = TradeGoodsAllowed(faction) ? rules.TradeGoodsWoodReserve : 0;
            var hv147 = IncenseAllowed(faction) ? economy.Incense : 0;
            var hv148 = IncenseAllowed(faction) ? rules.IncenseWoodCost : 0;
            var hv149 = IncenseAllowed(faction) ? rules.IncenseSizeCells : 0;
            var hv150 = IncenseAllowed(faction) ? rules.IncenseTicks : 0;
            var hv151 = IncenseAllowed(faction) ? rules.IncenseWoodInput : 0;
            var hv152 = IncenseAllowed(faction) ? rules.IncenseFoodInput : 0;
            var hv153 = IncenseAllowed(faction) ? rules.IncenseKeepWood : 0;
            var hv154 = IncenseAllowed(faction) ? rules.IncenseKeepFood : 0;
            var hv155 = IncenseAllowed(faction) ? rules.IncenseStockLimit : 0;
            var hv156 = IncenseAllowed(faction) ? rules.IncenseHealIntervalTicks : 0;
            var hv157 = IncenseAllowed(faction) ? rules.IncenseHealHp : 0;
            var hv158 = IncenseAllowed(faction) ? rules.IncenseMaxHouses : 0;
            var hv159 = IncenseAllowed(faction) ? rules.IncenseWoodReserve : 0;
            var hv160 = CharmsAllowed(faction) ? economy.Charms : 0;
            var hv161 = CharmsAllowed(faction) ? rules.CharmWoodCost : 0;
            var hv162 = CharmsAllowed(faction) ? rules.CharmSizeCells : 0;
            var hv163 = CharmsAllowed(faction) ? rules.CharmTicks : 0;
            var hv164 = CharmsAllowed(faction) ? rules.CharmWoodInput : 0;
            var hv165 = CharmsAllowed(faction) ? rules.CharmKeepWood : 0;
            var hv166 = CharmsAllowed(faction) ? rules.CharmStockLimit : 0;
            var hv167 = CharmsAllowed(faction) ? rules.CharmMaxWorkshops : 0;
            var hv168 = CharmsAllowed(faction) ? rules.CharmWoodReserve : 0;
            var hv169 = CharmsAllowed(faction) ? rules.CharmHpPermille : 0;
            var hv170 = RepairKitsAllowed(faction) ? economy.RepairKits : 0;
            var hv171 = RepairKitsAllowed(faction) ? rules.RepairYardWoodCost : 0;
            var hv172 = RepairKitsAllowed(faction) ? rules.RepairYardSizeCells : 0;
            var hv173 = RepairKitsAllowed(faction) ? rules.RepairKitTicks : 0;
            var hv174 = RepairKitsAllowed(faction) ? rules.RepairKitWoodInput : 0;
            var hv175 = RepairKitsAllowed(faction) ? rules.RepairKitKeepWood : 0;
            var hv176 = RepairKitsAllowed(faction) ? rules.RepairKitStockLimit : 0;
            var hv177 = RepairKitsAllowed(faction) ? rules.RepairKitMaxYards : 0;
            var hv178 = RepairKitsAllowed(faction) ? rules.RepairKitWoodReserve : 0;
            var hv179 = RepairKitsAllowed(faction) ? rules.RepairKitIntervalTicks : 0;
            var hv180 = RepairKitsAllowed(faction) ? rules.RepairKitHp : 0;
            var hv181 = ToolsAllowed(faction) ? economy.Tools : 0;
            var hv182 = ToolsAllowed(faction) ? rules.ToolShopWoodCost : 0;
            var hv183 = ToolsAllowed(faction) ? rules.ToolShopSizeCells : 0;
            var hv184 = ToolsAllowed(faction) ? rules.ToolTicks : 0;
            var hv185 = ToolsAllowed(faction) ? rules.ToolWoodInput : 0;
            var hv186 = ToolsAllowed(faction) ? rules.ToolKeepWood : 0;
            var hv187 = ToolsAllowed(faction) ? rules.ToolStockLimit : 0;
            var hv188 = ToolsAllowed(faction) ? rules.ToolMaxShops : 0;
            var hv189 = ToolsAllowed(faction) ? rules.ToolWoodReserve : 0;
            var hv190 = ToolsAllowed(faction) ? rules.ToolVillagerFoodPermille : 0;
            var hv191 = ToolsAllowed(faction) ? rules.ToolVillagerTicksPermille : 0;
            var hv192 = GoodsMarketOn ? rules.GoodsTradeLot : 0;
            var hv193 = GoodsMarketOn ? GoodsPriceViews(faction) : null;
            var hv194 = rules.AdjacentBuildings ? rules.AdjacentBuildingClearanceCells : BuildingClearanceCells;
            var hv195 = CraftTree1On ? economy.CutStone : 0;
            var hv196 = CraftTree1On ? economy.Frames : 0;
            var hv197 = CraftTree1On ? rules.StonecutterWoodCost : 0;
            var hv198 = CraftTree1On ? rules.StonecutterSizeCells : 0;
            var hv199 = CraftTree1On ? rules.CraftTreeStonecutterTicks : 0;
            var hv200 = CraftTree1On ? rules.CraftTreeStoneInput : 0;
            var hv201 = JoineryAllowed(faction) ? rules.JoineryWoodCost : 0;
            var hv202 = JoineryAllowed(faction) ? rules.JoinerySizeCells : 0;
            var hv203 = JoineryAllowed(faction) ? rules.CraftTreeJoineryTicks : 0;
            var hv204 = JoineryAllowed(faction) ? rules.CraftTreeJoineryPlankInput : 0;
            var hv205 = JoineryAllowed(faction) ? rules.CraftTreeJoineryCutStoneInput : 0;
            var hv206 = CraftTree1On ? rules.CraftTreeFramesPerBuilding : 0;
            var hv207 = CraftTree1On ? rules.CraftTreeFrameWoodPermille : 0;
            var hv208 = CraftTree1On ? rules.CraftTreeFrameWorkPermille : 0;
            var hv209 = CraftTree1On ? rules.CraftTreeLevel2FramesCost : 0;
            var hv210 = DropSiteLimitOn ? rules.DropSiteMax : 0;
            var hv211 = DropSiteLimitOn ? AliveDropSites(faction) : 0;
            var hv212 = CraftTree2On ? economy.ArmsKit : 0;
            var hv213 = CraftTree2On ? economy.Scholarly : 0;
            var hv214 = CraftTree2On ? economy.FineArmsKit : 0;
            var hv215 = AssemblerAllowed(faction) ? rules.AssemblerWoodCost : 0;
            var hv216 = AssemblerAllowed(faction) ? rules.AssemblerSizeCells : 0;
            var hv217 = CraftTree2On ? rules.ArmsKitAttack : 0;
            var hv218 = CraftTree2On ? rules.ArmsKitHpPermille : 0;
            var hv219 = CraftTree2On ? rules.ScholarlyPerResearch : 0;
            var hv220 = CraftTree2On ? rules.ScholarlyCostPermille : 0;
            var hv221 = CraftTree2On ? rules.ScholarlyTimePermille : 0;
            var hv222 = CraftTree2On ? rules.ArmsKitTicks : 0;
            var hv223 = CraftTree2On ? rules.ArmsKitPlankInput : 0;
            var hv224 = CraftTree2On ? rules.ArmsKitCharcoalInput : 0;
            var hv225 = CraftTree2On ? rules.ArmsKitSteelInput : 0;
            var hv226 = CraftTree2On ? rules.ScholarlyTicks : 0;
            var hv227 = CraftTree2On ? rules.ScholarlyPlankInput : 0;
            var hv228 = CraftTree2On ? rules.ScholarlyCharcoalInput : 0;
            var hv229 = CraftTree2On ? rules.ScholarlyFoodInput : 0;
            var hv230 = CraftTree3On ? economy.SiegeParts : 0;
            var hv231 = CraftTree3On ? economy.Mechanism : 0;
            var hv232 = CraftTree3On ? rules.SiegePartsTicks : 0;
            var hv233 = CraftTree3On ? rules.MechanismTicks : 0;
            var hv234 = CraftTree3On ? rules.SiegePartsHpPermille : 0;
            var hv235 = CraftTree3On ? rules.SiegePartsDamagePermille : 0;
            var hv236 = CraftTree3On ? rules.SiegePartsPerCastle : 0;
            var hv237 = CraftTree3On ? rules.SiegePartsCastleStonePermille : 0;
            var hv238 = CraftTree3On ? rules.MechanismBuildingTimePermille : 0;
            var hv239 = CraftTree3On ? rules.MechanismBuildingMaterialPermille : 0;
            var hv240 = CraftTree3On ? rules.MechanismResearchCostPermille : 0;
            var hv241 = CraftTree3On ? rules.MechanismResearchTimePermille : 0;
            var hv242 = CraftTree4On ? economy.SiegeEngine : 0;
            var hv243 = CraftTree4On ? economy.EliteKit : 0;
            var hv244 = CraftTree4On ? rules.SiegeEngineTicks : 0;
            var hv245 = CraftTree4On ? rules.EliteKitTicks : 0;
            var hv246 = CraftTree4On ? rules.EliteKitAttack : 0;
            var hv247 = CraftTree4On ? rules.EliteKitHpPermille : 0;
            var hv248 = CraftTree4On ? rules.CannonFoodCost : 0;
            var hv249 = CraftTree4On ? rules.CannonWoodCost : 0;
            var hv250 = CraftTree4On ? (int)rules.CannonRange.Raw : 0;
            var hv251 = CraftTree4On ? rules.CannonSiegeDamage : 0;
            var hv252 = CraftTree4On ? economy.SiegeEngineProduced : 0;
            var hv253 = CraftTree4On ? economy.SiegeEngineConsumed : 0;
            var hv254 = CraftTree4On ? economy.EliteKitProduced : 0;
            var hv255 = CraftTree4On ? economy.EliteKitConsumed : 0;
            var hv256 = CraftTree4On ? economy.CannonProduced : 0;
            var hv257 = CommonDrillOn ? rules.DrillWoodCost : 0;
            var hv258 = CommonDrillOn ? rules.DrillStoneCost : 0;
            var hv259 = CommonDrillOn ? rules.DrillSizeCells : 0;
            var hv260 = InsertersOn ? rules.InserterWoodCost : 0;
            var hv261 = InsertersOn ? rules.InserterIntervalTicks : 0;
            var hv262 = BuildingReservationsOn ? rules.ReservationLimit : 0;
            var hv263 = BuildingReservationsOn ? ReservationCount(faction) : 0;
            var hv264 = BuildingReservationsOn ? rules.ReservationCheckTicks : 0;
            var hv265 = AgeFourOn ? rules.Age4FoodCost : 0;
            var hv266 = AgeFourOn ? rules.Age4WoodCost : 0;
            var hv267 = AgeFourOn ? rules.Age4Ticks : 0;
            var hv268 = AgeFourOn ? rules.TotalWarFoodCost : 0;
            var hv269 = AgeFourOn ? rules.TotalWarWoodCost : 0;
            var hv270 = AgeFourOn ? rules.TotalWarMetalCost : 0;
            var hv271 = AgeFourOn ? rules.TotalWarTicks : 0;
            var hv272 = AgeFourOn ? rules.GrandWorkshopFoodCost : 0;
            var hv273 = AgeFourOn ? rules.GrandWorkshopWoodCost : 0;
            var hv274 = AgeFourOn ? rules.GrandWorkshopMetalCost : 0;
            var hv275 = AgeFourOn ? rules.GrandWorkshopTicks : 0;
            var hv276 = AgeFourAiOn ? economy.AgeFourAiResearchFailureTech : 0;
            var hv277 = AgeFourAiOn ? economy.AgeFourAiResearchFailureReason : (byte)0;
            var hv278 = AgeFourAiOn ? economy.AgeFourAiResearchFailureTick : 0;
            var hv279 = PrimitiveCraftOn ? economy.StoneTools : 0;
            var hv280 = PrimitiveCraftOn ? economy.Jerky : 0;
            var hv281 = PrimitiveCraftOn ? rules.HutWoodCost : 0;
            var hv282 = PrimitiveCraftOn ? rules.HutSizeCells : 0;
            var hv283 = PrimitiveCraftOn ? rules.PrimitiveCraftTicks : 0;
            var hv284 = PrimitiveCraftOn ? rules.StoneToolsStoneInput : 0;
            var hv285 = PrimitiveCraftOn ? rules.StoneToolsWoodInput : 0;
            var hv286 = PrimitiveCraftOn ? rules.JerkyFoodInput : 0;
            var hv287 = PrimitiveCraftOn ? rules.JerkyOutput : 0;
            var hv288 = PrimitiveCraftOn ? rules.StoneToolsGatherIntervalPermille : 0;
            var hv289 = PrimitiveCraftOn ? rules.JerkyHpPermille : 0;
            var hv290 = AiFactoryOn ? rules.AiFactoryPrimitiveLineCount : 0;
            var hv291 = AiFactoryOn ? rules.AiFactoryAge1LineCount : 0;
            var hv292 = AiFactoryOn ? rules.AiFactoryAge2LineCount : 0;
            var hv293 = AiFactoryOn ? rules.AiFactoryAge3LineCount : 0;
            var hv294 = AiFactoryOn ? rules.AiFactoryBarracksIdleWindowTicks : 0;
            var hv295 = AiFactoryOn ? rules.AiFactoryBarracksIdlePermille : 0;
            var hv296 = AiFactoryOn ? rules.AiFactoryPrimitiveBarracksMaxBuildings : 0;
            var hv297 = AiFactoryOn ? rules.AiFactoryAge1BarracksMaxBuildings : 0;
            var hv298 = AiFactoryOn ? rules.AiFactoryAge2BarracksMaxBuildings : 0;
            var hv299 = AiFactoryOn ? rules.AiFactoryAge3BarracksMaxBuildings : 0;
            var hv300 = AiFactoryOn ? rules.AiFactoryAge4BarracksMaxBuildings : 0;
            return new EconomyView(economy.Food, economy.Wood, population, PopCapFor(faction), economy.Queued, economy.TrainRemaining,
                !economy.AutoOff, rules.BarracksSizeCells, BuildingWoodDisplayCost(faction, BuildingKind.Barracks, rules.BarracksWoodCost), VillagerFoodCostFor(faction, CanUseToolForVillager(faction)), infantryTrainingCost.food,
                infantryTrainingCost.wood, villagers, buildings, resources,
                rules.Industry, economy.Ore, economy.Metal, rules.BeltWoodCost, rules.BeltTicksPerCell, belts,
                InfantryMetalFor(faction), rules.MineWoodCost, rules.SmelterWoodCost, rules.MineSizeCells, rules.SmelterSizeCells, economy.CoreHeld, economy.Policy,
                rules.Ages, economy.Civ, economy.AdvancingTo, economy.AdvanceRemaining, advanceFood, advanceWood,
                 hv0, hv1,
                 hv2, hv3, hv4,
                 economy.Stone, hv5, hv6, hv7,
                hv8, economy.Techs, hv9, hv10, hv11,
                 economy.Age, hv12, hv13, hv14, hv15,
                 hv16, hv17, hv18,
                hv19, hv20, hv21, hv22,
                 hv23, hv24,
                hv25, hv26,
                hv27, hv28,
                 hv29, hv30,
                hv31, hv32, hv33, hv34,
                hv35, hv36,
                rules.MonksEnabled, hv37, hv38,
                hv39,
                rules.ProcessingChain,
                hv40, hv41,
                hv42, hv43,
                hv44, hv45,
                hv46, hv47,
                hv48,
                hv49,
                hv50, hv51, hv52,
                hv53, hv54,
                hv55, hv56, cavalryMission,
                hv57,
                hv58,
                rules.BeltComponents, rules.FastBeltWoodCost, rules.FastBeltTicksPerCell, rules.SplitterWoodCost, rules.SorterWoodCost,
                rules.UndergroundBeltWoodCost, rules.UndergroundBeltMaxLength, rules.StorageWoodCost, rules.StorageSizeCells, rules.StorageCapacity,
                  nextAgeClockTick: NextAgeClockTick(faction), autoAssaultOff: AutoAssaultOffFor(faction),
                  rationsEnabled: RationsAllowed(faction), rations: hv59,
                  millWoodCost: hv60, millSizeCells: hv61,
                  millTicks: hv62, millFoodInput: hv63,
                  rationsPerSoldier: hv64, rationsTrainPermille: hv65,
                  rationsHealIntervalTicks: hv66,
                  rationsHealQuietTicks: hv67,
                  rationsHealHp: hv68,
                  rationsHealMaxPerInterval: hv69,
                  rationsFarmMinimum: hv70,
                  rationsFoodReserve: hv71,
                  bricksEnabled: BricksAllowed(faction), bricks: hv72,
                  brickKilnWoodCost: hv73,
                  brickKilnSizeCells: hv74,
                  brickTicks: hv75,
                  brickStoneInput: hv76,
                  brickCharcoalInput: hv77,
                  bricksPerWall: hv78,
                  bricksPerTower: hv79,
                  bricksPerCastle: hv80,
                  brickHpPermille: hv81,
                  brickUpgradeIntervalTicks: hv82,
                brickCastleStonePermille: hv83,
                tackEnabled: TackAllowed(faction), tack: hv84,
                saddleryWoodCost: hv85,
                saddlerySizeCells: hv86,
                tackTicks: hv87,
                tackFoodInput: hv88,
                tackWoodInput: hv89,
                heavyCavalryTackCost: hv90,
                heavyCavalryFoodCost: hv91,
                heavyCavalryTrainTicks: hv92,
                heavyCavalryHp: hv93,
                heavyCavalryDamage: hv94,
                heavyCavalryAttackIntervalTicks: hv95,
                heavyCavalryRange: hv96,
                heavyCavalrySpeed: hv97,
                heavyCavalryVision: hv98,
                planksEnabled: PlanksAllowed(faction) || commonSawmill, planks: hv99,
                sawmillWoodCost: hv100,
                sawmillSizeCells: hv101,
                plankTicks: hv102,
                plankWoodInput: hv103,
                planksPerRam: hv104,
                plankRamHpPermille: hv105,
                plankRamTrainPermille: hv106,
                planksPerBridgeCell: hv107,
                plankBridgeWoodPermille: hv108,
                plankExtraRams: hv109,
                plankWoodReserve: hv110,
                booksEnabled: BooksAllowed(faction), books: hv111,
                scriptoriumWoodCost: hv112,
                scriptoriumSizeCells: hv113,
                bookTicks: hv114,
                bookGoldInput: hv115,
                bookWoodInput: hv116,
                booksPerResearch: hv117,
                bookResearchPermille: hv118,
                bookResearchCostPermille: hv119,
                bookWoodReserve: hv120,
                preservesEnabled: PreservesAllowed(faction),
                dryingRackWoodCost: hv121,
                dryingRackSizeCells: hv122,
                preserveTicks: hv123,
                preserveFoodInput: hv124,
                preserveFoodValue: hv125,
                preserveKeepFood: hv126,
                preserveMaxRacks: hv127,
                preserveWoodReserve: hv128,
                jewelsEnabled: JewelsAllowed(faction),
                lapidaryWoodCost: hv129,
                lapidarySizeCells: hv130,
                jewelTicks: hv131,
                jewelStoneInput: hv132,
                jewelOreInput: hv133,
                jewelMaxLapidaries: hv134,
                jewelKeepStone: hv135,
                jewelMercenaryGems: hv136,
                jewelMercenaryFood: hv137,
                jewelMercenaryInfantryRatio: hv138,
                tradeGoodsEnabled: TradeGoodsAllowed(faction),
                weaverWoodCost: hv139,
                weaverSizeCells: hv140,
                tradeGoodsTicks: hv141,
                tradeGoodsWoodInput: hv142,
                tradeGoodsKeepWood: hv143,
                tradeGoodsValue: hv144,
                tradeGoodsMaxWeavers: hv145,
                tradeGoodsWoodReserve: hv146,
                incenseEnabled: IncenseAllowed(faction),
                incense: hv147,
                incenseHouseWoodCost: hv148,
                incenseHouseSizeCells: hv149,
                incenseTicks: hv150,
                incenseWoodInput: hv151,
                incenseFoodInput: hv152,
                incenseKeepWood: hv153,
                incenseKeepFood: hv154,
                incenseStockLimit: hv155,
                incenseHealIntervalTicks: hv156,
                incenseHealHp: hv157,
                incenseMaxHouses: hv158,
                incenseWoodReserve: hv159,
                charmsEnabled: CharmsAllowed(faction), charms: hv160,
                charmWorkshopWoodCost: hv161,
                charmWorkshopSizeCells: hv162,
                charmTicks: hv163,
                charmWoodInput: hv164,
                charmKeepWood: hv165,
                charmStockLimit: hv166,
                charmMaxWorkshops: hv167,
                charmWoodReserve: hv168,
                charmHpPermille: hv169,
                repairKitsEnabled: RepairKitsAllowed(faction), repairKits: hv170,
                repairYardWoodCost: hv171,
                repairYardSizeCells: hv172,
                repairKitTicks: hv173,
                repairKitWoodInput: hv174,
                repairKitKeepWood: hv175,
                repairKitStockLimit: hv176,
                repairKitMaxYards: hv177,
                repairKitWoodReserve: hv178,
                repairKitIntervalTicks: hv179,
                repairKitHp: hv180,
                toolsEnabled: ToolsAllowed(faction), tools: hv181,
                toolShopWoodCost: hv182,
                toolShopSizeCells: hv183,
                toolTicks: hv184,
                toolWoodInput: hv185,
                toolKeepWood: hv186,
                toolStockLimit: hv187,
                toolMaxShops: hv188,
                toolWoodReserve: hv189,
                toolVillagerFoodPermille: hv190,
                 toolVillagerTicksPermille: hv191,
                 goodsMarketEnabled: GoodsMarketOn,
                 goodsTradeLot: hv192,
                 goodsPrices: hv193,
                 adjacentBuildings: rules.AdjacentBuildings,
                 buildingClearanceCells: hv194,
                 coreClearanceCells: CoreClearanceCells, nodeClearanceCells: NodeClearanceCells,
                 mapWidthCells: world.Config.Map.WidthCells, mapHeightCells: world.Config.Map.HeightCells,
                 cellSizeMeters: world.Config.Map.CellSizeMeters, passableCells: passableCells, riverCells: riverCells,
                craftTree1Enabled: CraftTree1On,
                cutStone: hv195,
                frames: hv196,
                stonecutterWoodCost: hv197,
                stonecutterSizeCells: hv198,
                stonecutterTicks: hv199,
                stonecutterStoneInput: hv200,
                joineryWoodCost: hv201,
                joinerySizeCells: hv202,
                joineryTicks: hv203,
                joineryPlankInput: hv204,
                joineryCutStoneInput: hv205,
                framesPerBuilding: hv206,
                frameWoodPermille: hv207,
                frameWorkPermille: hv208,
                level2FramesCost: hv209,
                dropSiteMax: hv210,
                dropSiteCount: hv211,
                craftTree2Enabled: CraftTree2On,
                armsKit: hv212,
                scholarly: hv213,
                fineArmsKit: hv214,
                assemblerWoodCost: hv215,
                assemblerSizeCells: hv216,
                armsKitAttack: hv217,
                armsKitHpPermille: hv218,
                scholarlyPerResearch: hv219,
                scholarlyCostPermille: hv220,
                scholarlyTimePermille: hv221,
                armsKitTicks: hv222,
                armsKitPlankInput: hv223,
                armsKitCharcoalInput: hv224,
                armsKitSteelInput: hv225,
                scholarlyTicks: hv226,
                scholarlyPlankInput: hv227,
                scholarlyCharcoalInput: hv228,
                 scholarlyFoodInput: hv229,
                 craftTree3Enabled: CraftTree3On,
                 siegeParts: hv230,
                 mechanism: hv231,
                 siegePartsTicks: hv232,
                 mechanismTicks: hv233,
                 siegePartsHpPermille: hv234,
                 siegePartsDamagePermille: hv235,
                 siegePartsPerCastle: hv236,
                 siegePartsCastleStonePermille: hv237,
                 mechanismBuildingTimePermille: hv238,
                 mechanismBuildingMaterialPermille: hv239,
                 mechanismResearchCostPermille: hv240,
                 mechanismResearchTimePermille: hv241,
                 craftTree4Enabled: CraftTree4On,
                 siegeEngine: hv242,
                 eliteKit: hv243,
                 siegeEngineTicks: hv244,
                 eliteKitTicks: hv245,
                 eliteKitAttack: hv246,
                 eliteKitHpPermille: hv247,
                 cannonFoodCost: hv248,
                 cannonWoodCost: hv249,
                 cannonRange: hv250,
                 cannonSiegeDamage: hv251,
                 siegeEngineProduced: hv252,
                 siegeEngineConsumed: hv253,
                 eliteKitProduced: hv254,
                 eliteKitConsumed: hv255,
                 cannonProduced: hv256,
                civChoiceDeadlineTick: civChoiceDeadline,
                ageClockUx: AgeClockUxOn,
                commonDrill: CommonDrillOn, drillWoodCost: hv257,
                drillStoneCost: hv258, drillSizeCells: hv259,
                inserters: InsertersOn, inserterWoodCost: hv260,
                inserterIntervalTicks: hv261,
                easyRebuild: EasyRebuildOn,
                  buildingReservations: BuildingReservationsOn, reservationLimit: hv262,
                  reservationCount: hv263,
                  reservationCheckTicks: hv264,
                  ageFour: AgeFourOn,
                  age4FoodCost: hv265,
                  age4WoodCost: hv266,
                  age4Ticks: hv267,
                  totalWarFoodCost: hv268,
                  totalWarWoodCost: hv269,
                  totalWarMetalCost: hv270,
                  totalWarTicks: hv271,
                  grandWorkshopFoodCost: hv272,
                  grandWorkshopWoodCost: hv273,
                   grandWorkshopMetalCost: hv274,
                   grandWorkshopTicks: hv275,
                    ageStrengths: AgeStrengthsOn,
                    ageFourAiResearchFailureTech: hv276,
                    ageFourAiResearchFailureReason: hv277,
                    ageFourAiResearchFailureTick: hv278,
                    primitiveCraftEnabled: PrimitiveCraftOn,
                    stoneTools: hv279,
                    jerky: hv280,
                    hutWoodCost: hv281,
                    hutSizeCells: hv282,
                    primitiveCraftTicks: hv283,
                    stoneToolsStoneInput: hv284,
                    stoneToolsWoodInput: hv285,
                    jerkyFoodInput: hv286,
                    jerkyOutput: hv287,
                    stoneToolsGatherIntervalPermille: hv288,
                    jerkyHpPermille: hv289,
                    aiFactoryEnabled: AiFactoryOn,
                    aiFactoryPrimitiveLineCount: hv290,
                    aiFactoryAge1LineCount: hv291,
                    aiFactoryAge2LineCount: hv292,
                    aiFactoryAge3LineCount: hv293,
                    aiFactoryBarracksIdleWindowTicks: hv294,
                    aiFactoryBarracksIdlePermille: hv295,
                    aiFactoryPrimitiveBarracksMaxBuildings: hv296,
                    aiFactoryAge1BarracksMaxBuildings: hv297,
                    aiFactoryAge2BarracksMaxBuildings: hv298,
                    aiFactoryAge3BarracksMaxBuildings: hv299,
                     aiFactoryAge4BarracksMaxBuildings: hv300,
                     craftRequiredEnabled: CraftRequiredOn,
                     buildingPlankCosts: craftBuildingCosts.planks, buildingFrameCosts: craftBuildingCosts.frames,
                     beltPlankCostPerTwoCells: beltPlanksPerTwo, fastBeltPlankCost: fastBeltPlanks,
                     splitterPlankCost: splitterPlanks, sorterPlankCost: sorterPlanks,
                     undergroundBeltPlankCost: undergroundPlanks,
                     researchScholarlyCost: CraftRequiredResearchScholarlyCost(faction),
                     advancePlanks: advanceCraft.planks, advanceCutStone: advanceCraft.cutStone,
                     advanceFrames: advanceCraft.frames, advanceScholarly: advanceCraft.scholarly,
                     advanceSiegeParts: advanceCraft.siegeParts, advanceMechanism: advanceCraft.mechanism,
                     craftFlowEnabled: CraftFlowOn, productionStatistics: CraftFlowStatistics(faction),
                     craftDepthEnabled: CraftDepthOn,
                     fittings: fittingsView, rope: ropeView,
                      beltCraftEnabled: beltCraft, gears: gearsView,
                      filterInserterGears: filterInserterGears, longInserterGears: longInserterGears,
                      catapultGears: catapultGears, catapultPlanks: catapultPlanks,
                      catapultFittings: catapultFittings, catapultRope: catapultRope,
                      catapultSteel: catapultSteel,
                      catapultReceiverGears: catapultReceiverGears, catapultReceiverPlanks: catapultReceiverPlanks,
                      catapultMinAge: catapultMinAge, catapultRangeCells: catapultRangeCells,
                      catapultLoad: catapultLoad, catapultMaxWaitTicks: catapultMaxWaitTicks,
                      catapultIntervalTicks: catapultIntervalTicks, catapultFlightTicks: catapultFlightTicks,
                       catapultBuffer: catapultBuffer, catapultShots: catapultShots,
                       livelihoodMet: livelihoodMet, livelihoodRequired: livelihoodRequired, livelihoodShortGoods: livelihoodShortGoods,
                       fortify: FortifyOn, gateStoneCost: gateStoneView, slag: slagView, byproductsEnabled: ByproductsOn,
                       blastFurnaceSizeCells: blastFurnaceSizeView,
                       blastFurnaceWoodCost: blastFurnaceWoodCostView,
                        blastFurnaceBrickCost: blastFurnaceBrickCostView, armySplit: ArmySplitOn,
                        glassworksEnabled: glassworksView, sand: sandView, glass: glassView, lens: lensView,
                        glassSandInput: glassSandInputView, glassCharcoalInput: glassCharcoalInputView, glassTicks: glassTicksView,
                        lensGlassInput: lensGlassInputView, lensGearsInput: lensGearsInputView, lensTicks: lensTicksView,
                        archerLensCost: archerLensCostView, towerGlassCost: towerGlassCostView, castleGlassCost: castleGlassCostView,
                        towerGlassVisionBonus: towerGlassVisionBonusView, glassMaxLines: glassMaxLinesView,
                        modulesEnabled: modulesView, precision: precisionView,
                        precisionLensInput: precisionLensInputView, precisionGearsInput: precisionGearsInputView,
                        precisionMetalInput: precisionMetalInputView, precisionTicks: precisionTicksView,
                        precisionLensInputCapacity: precisionLensCapacityView, precisionGearsInputCapacity: precisionGearsCapacityView,
                        precisionMetalInputCapacity: precisionMetalCapacityView, moduleSlots: moduleSlotsView,
                        moduleSpeedPermille: moduleSpeedView, moduleMinAge: moduleMinAgeView,
                        cannonPrecisionCost: cannonPrecisionCostView, cannonPrecisionMinAge: cannonPrecisionMinAgeView,
                        precisionMaxLines: precisionMaxLinesView, aiModulesPerDecision: aiModulesPerDecisionView,
                        aiPrecisionReserve: aiPrecisionReserveView);
        }

        // Sized from the enum so building kinds added later (e.g. CraftDepth) are not cut off.
        private static readonly int BuildingKindCount = MaxBuildingKind() + 1;

        private static int MaxBuildingKind()
        {
            int max = 0;
            foreach (BuildingKind kind in Enum.GetValues(typeof(BuildingKind))) max = Math.Max(max, (int)kind);
            return max;
        }

        private (IReadOnlyList<int> planks, IReadOnlyList<int> frames) CraftRequiredBuildingDisplayCosts(uint faction)
        {
            int count = BuildingKindCount;
            var planks = new int[count];
            var frames = new int[count];
            if (!CraftRequiredOn) return (planks, frames);
            for (int value = 1; value < count; value++)
            {
                var kind = (BuildingKind)value;
                if (kind == BuildingKind.Bridge) continue;
                var cost = BuildingCostForPlacement(kind, faction, 0);
                planks[value] = cost.Planks;
                frames[value] = cost.Frames;
            }
            return (planks, frames);
        }

        private int BuildingWoodDisplayCost(uint faction, BuildingKind kind, int fallback)
        {
            if (!CraftRequiredOn) return fallback;
            return BuildingCostForPlacement(kind, faction, 0).Wood;
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
