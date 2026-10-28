using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-2 mines and smelters (technical-design-v3 12.2). A mine stands on an ore point and digs it on its own; a smelter
    /// turns ore into metal. Each has an input and an output of BufferLimit items. Belts feed a building by pointing into
    /// its footprint; a building puts its output onto the own empty belt on the cell just outside the middle of the side it
    /// faces. Nothing here runs without industry.
    /// </summary>
    public sealed partial class Simulation
    {
        private bool CommonDrillOn => IndustryOn && (world.Config.Economy.CommonDrill || AiFactoryOn);

        /// <summary>Economy step, right after the belts: dig, smelt, then hand outputs to the belts.</summary>
        private void AdvanceIndustry()
        {
            if (!IndustryOn) return;
            BeginCraftFlowObservation();
            var rules = world.Config.Economy;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!IsActualBuilding(b) || !b.Complete || BuildingPaused(b)) continue;
                // Recovery keeps raw extraction and metallurgy alive while pausing optional input-consuming lines.
                if (ArmyRecoveryNeedsInfantry(b.FactionId) && b.Kind != BuildingKind.Farm && b.Kind != BuildingKind.Mine
                    && b.Kind != BuildingKind.LumberCamp && b.Kind != BuildingKind.Quarry && b.Kind != BuildingKind.Drill
                    && b.Kind != BuildingKind.Smelter) continue;
                int craftFlowTimerBefore = b.Timer;
                if (b.Kind == BuildingKind.Mine)
                {
                    ref var node = ref world.Nodes[b.NodeId - 1];
                    if (node.Remaining <= 0 || b.Output >= rules.BufferLimit) continue;
                    int interval = AgeStrength(b.FactionId, CivKind.Mountain, 2)
                        ? Math.Max(1, checked((rules.MineIntervalTicks * rules.MountainMiningTimePermille + 999) / 1000)) : rules.MineIntervalTicks;
                    if (++b.Timer < interval) continue;
                    b.Timer = 0;
                    node.Remaining--;
                    b.Output++;
                }
                else if (b.Kind == BuildingKind.LumberCamp)
                {
                    ref var node = ref world.Nodes[b.NodeId - 1];
                    if (node.Remaining <= 0 || b.Output >= rules.BufferLimit) continue;
                    int interval = AgeStrength(b.FactionId, CivKind.Forestry, 2)
                        ? Math.Max(1, checked((rules.LumberCampIntervalTicks * rules.ForestryLumberSpeedPermille + 999) / 1000)) : rules.LumberCampIntervalTicks;
                    if (++b.Timer < interval) continue;
                    b.Timer = 0;
                    node.Remaining--;
                    b.Output++;
                }
                else if (b.Kind == BuildingKind.Quarry)
                {
                    ref var node = ref world.Nodes[b.NodeId - 1];
                    if (node.Remaining <= 0 || b.Output >= rules.BufferLimit) continue;
                    if (++b.Timer < rules.QuarryIntervalTicks) continue;
                    b.Timer = 0;
                    node.Remaining--;
                    b.Output++;
                }
                else if (CommonDrillOn && b.Kind == BuildingKind.Drill)
                {
                    if (b.NodeId == 0 || b.NodeId > world.Nodes.Length) continue;
                    ref var node = ref world.Nodes[b.NodeId - 1];
                    if (node.Remaining <= 0 || b.Output >= rules.BufferLimit) continue;
                    int interval = AgeStrength(b.FactionId, CivKind.Mountain, 2)
                        ? Math.Max(1, checked((rules.DrillIntervalTicks * rules.MountainMiningTimePermille + 999) / 1000)) : rules.DrillIntervalTicks;
                    if (++b.Timer < interval) continue;
                    b.Timer = 0;
                    node.Remaining--;
                    b.Output++;
                }
                else if (b.Kind == BuildingKind.Farm)
                {
                    // V3-4 (27): food from nothing, at the pace its ground set when it was placed.
                    if (b.Output >= rules.BufferLimit) continue;
                    if (++b.Timer < FarmTicksFor(b)) continue;
                    b.Timer = 0;
                    b.Output++;
                }
                else if (b.Kind == BuildingKind.Smelter)
                {
                    int oreInput = CraftInputFor(b, rules.OrePerMetal);
                    if (b.Timer == 0 && b.Input >= oreInput && b.Output < rules.BufferLimit)
                    {
                        b.Input -= oreInput;
                        b.Timer = CraftTicksFor(b, SmeltTicksFor(b.FactionId));
                    }
                    if (b.Timer > 0 && --b.Timer == 0)
                    {
                        b.Output++;
                    }
                }
                else if (CharcoalKilnAllowed(b.FactionId) && b.Kind == BuildingKind.CharcoalKiln)
                {
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    int woodInput = CraftTreeCommonAllowed(b.FactionId) ? rules.CraftTreeCharcoalWoodInput : 2;
                    int charcoalTicks = CraftTreeCommonAllowed(b.FactionId) ? rules.CraftTreeCharcoalTicks : rules.CharcoalTicks;
                    if ((BricksAllowed(b.FactionId) || CraftTreeCommonAllowed(b.FactionId)) && b.Input < rules.BufferLimit && economy.Wood > 0)
                    {
                        economy.Wood--;
                        RecordLargeUse(b.FactionId, ResourceKind.Wood, LargeEconomyUseKind.ProcessingBuilding, 1);
                        b.Input++;
                    }
                    if (b.Timer == 0 && b.Input >= woodInput && b.Output < rules.BufferLimit)
                    {
                        b.Input -= woodInput;
                        b.Timer = CraftTicksFor(b, charcoalTicks);
                    }
                    if (b.Timer > 0 && --b.Timer == 0)
                    {
                        b.Output++;
                        if (BricksAllowed(b.FactionId)) economy.Charcoal++;
                    }
                }
                else if (ProcessingOn && b.Kind == BuildingKind.Steelworks)
                {
                    // Both materials are consumed as one atomic operation on the same tick.
                    int steelInput = CraftInputFor(b, 1);
                    if (b.Timer == 0 && b.Input >= steelInput && b.InputSecondary >= steelInput && b.Output < rules.BufferLimit)
                    {
                        b.Input -= steelInput;
                        b.InputSecondary -= steelInput;
                        b.Timer = CraftTicksFor(b, rules.SteelTicks);
                    }
                    if (b.Timer > 0 && --b.Timer == 0)
                    {
                        b.Output++;
                    }
                }
                else if (ForestryOn && b.Kind == BuildingKind.Fletcher)
                {
                    // Wood and food are consumed together.  Food may arrive from the core stock,
                    // while belts and hand hauling can fill either input slot.
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    if (b.InputSecondary < rules.BufferLimit && economy.Food > 0)
                    {
                        economy.Food--; b.InputSecondary++;
                    }
                    int fletcherWood = CraftInputFor(b, rules.FletcherWoodInput), fletcherFood = CraftInputFor(b, rules.FletcherFoodInput);
                    if (b.Timer == 0 && b.Input >= fletcherWood && b.InputSecondary >= fletcherFood && b.Output < rules.BufferLimit)
                    {
                        b.Input -= fletcherWood;
                        b.InputSecondary -= fletcherFood;
                        b.Timer = CraftTicksFor(b, rules.FletcherTicks);
                    }
                    if (b.Timer > 0 && --b.Timer == 0)
                    {
                        b.Output++;
                    }
                }
                else if (RationsOn && b.Kind == BuildingKind.Mill)
                {
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    if (b.Input < rules.BufferLimit && economy.Food > 0)
                    {
                        economy.Food--; b.Input++;
                    }
                    int millFood = CraftInputFor(b, rules.MillFoodInput);
                    if (b.Timer == 0 && b.Input >= millFood && b.Output < rules.BufferLimit)
                    {
                        b.Input -= millFood;
                        b.Timer = CraftTicksFor(b, rules.MillTicks);
                    }
                    if (b.Timer > 0 && --b.Timer == 0)
                    {
                        b.Output++;
                        economy.RationsProduced++;
                    }
                }
                else if (TackAllowed(b.FactionId) && b.Kind == BuildingKind.Saddlery)
                {
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    if (b.Input < rules.BufferLimit && economy.Food > 0)
                    {
                        economy.Food--; b.Input++;
                    }
                    if (b.InputSecondary < rules.BufferLimit && economy.Wood > 0)
                    {
                        economy.Wood--; RecordLargeUse(b.FactionId, ResourceKind.Wood, LargeEconomyUseKind.ProcessingBuilding, 1); b.InputSecondary++;
                    }
                    int tackFood = CraftInputFor(b, rules.TackFoodInput), tackWood = CraftInputFor(b, rules.TackWoodInput);
                    if (b.Timer == 0 && b.Input >= tackFood && b.InputSecondary >= tackWood && b.Output < rules.BufferLimit)
                    {
                        b.Input -= tackFood;
                        b.InputSecondary -= tackWood;
                        b.Timer = CraftTicksFor(b, rules.TackTicks);
                    }
                    if (b.Timer > 0 && --b.Timer == 0)
                    {
                        b.Output++;
                        economy.TackProduced++;
                    }
                }
                else if ((PlanksAllowed(b.FactionId) || CraftTreeCommonAllowed(b.FactionId)) && b.Kind == BuildingKind.Sawmill)
                {
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    if (b.Input < rules.BufferLimit && economy.Wood > 0)
                    {
                        economy.Wood--; RecordLargeUse(b.FactionId, ResourceKind.Wood, LargeEconomyUseKind.ProcessingBuilding, 1); b.Input++;
                    }
                    int plankInput = CraftTreeCommonAllowed(b.FactionId) ? rules.CraftTreePlankWoodInput : rules.PlankWoodInput;
                    plankInput = CraftInputFor(b, plankInput);
                    if (b.Timer == 0 && b.Input >= plankInput && b.Output < rules.BufferLimit)
                    {
                        b.Input -= plankInput;
                        b.Timer = CraftTicksFor(b, CraftTreeCommonAllowed(b.FactionId) ? rules.CraftTreePlankTicks : rules.PlankTicks);
                    }
                    if (b.Timer > 0 && --b.Timer == 0)
                    {
                        b.Output++;
                        economy.PlanksProduced++;
                    }
                }
                else if (StonecutterAllowed(b.FactionId) && b.Kind == BuildingKind.Stonecutter)
                {
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    if (b.Input < rules.BufferLimit && economy.Stone > 0)
                    {
                        economy.Stone--;
                        RecordLargeUse(b.FactionId, ResourceKind.Stone, LargeEconomyUseKind.ProcessingBuilding, 1);
                        b.Input++;
                    }
                    int stoneInput = CraftInputFor(b, rules.CraftTreeStoneInput);
                    if (b.Timer == 0 && b.Input >= stoneInput && b.Output < rules.BufferLimit)
                    {
                        b.Input -= stoneInput;
                        b.Timer = CraftTicksFor(b, rules.CraftTreeStonecutterTicks);
                    }
                    if (b.Timer > 0 && --b.Timer == 0)
                    {
                        b.Output++;
                        economy.CutStoneProduced++;
                    }
                }
                else if (JoineryAllowed(b.FactionId) && b.Kind == BuildingKind.Joinery)
                {
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    if (b.Input < rules.BufferLimit && economy.Planks > 0)
                    {
                        economy.Planks--;
                        b.Input++;
                    }
                    if (b.InputSecondary < rules.BufferLimit && economy.CutStone > 0)
                    {
                        economy.CutStone--;
                        b.InputSecondary++;
                    }
                    int joineryPlanks = CraftInputFor(b, rules.CraftTreeJoineryPlankInput), joineryStone = CraftInputFor(b, rules.CraftTreeJoineryCutStoneInput);
                    if (b.Timer == 0 && b.Input >= joineryPlanks
                        && b.InputSecondary >= joineryStone && b.Output < rules.BufferLimit)
                    {
                        b.Input -= joineryPlanks;
                        b.InputSecondary -= joineryStone;
                        economy.PlanksConsumed = checked(economy.PlanksConsumed + joineryPlanks);
                        economy.CutStoneConsumed = checked(economy.CutStoneConsumed + joineryStone);
                        b.Timer = CraftTicksFor(b, rules.CraftTreeJoineryTicks);
                    }
                    if (b.Timer > 0 && --b.Timer == 0)
                    {
                        b.Output++;
                        economy.FramesProduced++;
                        RecordCraftTracePlanks(b.FactionId, CraftTraceConstruction, joineryPlanks);
                    }
                }
                else if (PrimitiveCraftOn && b.Kind == BuildingKind.Hut)
                {
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    if (b.Recipe == RecipeKind.StoneTools)
                    {
                        FillRaw(b.FactionId, ResourceKind.Stone, ref economy.Stone, ref b.Input, rules.StoneToolsStoneInput);
                        FillRaw(b.FactionId, ResourceKind.Wood, ref economy.Wood, ref b.InputSecondary, rules.StoneToolsWoodInput);
                        if (b.Timer == 0 && b.Input >= rules.StoneToolsStoneInput && b.InputSecondary >= rules.StoneToolsWoodInput && b.Output < rules.BufferLimit)
                        { b.Input -= rules.StoneToolsStoneInput; b.InputSecondary -= rules.StoneToolsWoodInput; b.Timer = CraftTicksFor(b, rules.PrimitiveCraftTicks); }
                        if (b.Timer > 0 && --b.Timer == 0) { b.Output++; economy.StoneToolsProduced++; }
                    }
                    else if (b.Recipe == RecipeKind.Jerky)
                    {
                        Fill(ref economy.Food, ref b.Input, rules.JerkyFoodInput);
                        if (b.Timer == 0 && b.Input >= rules.JerkyFoodInput && b.Output <= rules.BufferLimit - rules.JerkyOutput)
                        { b.Input -= rules.JerkyFoodInput; b.Timer = CraftTicksFor(b, rules.PrimitiveCraftTicks); }
                        if (b.Timer > 0 && --b.Timer == 0) { b.Output += rules.JerkyOutput; economy.JerkyProduced += rules.JerkyOutput; }
                    }
                }
                else if (AssemblerAllowed(b.FactionId) && b.Kind == BuildingKind.Assembler)
                {
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    AdvanceAssembler(ref b, ref economy);
                }
                else if (BricksAllowed(b.FactionId) && b.Kind == BuildingKind.BrickKiln)
                {
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    if (b.Input < rules.BufferLimit && economy.Stone > 0)
                    {
                        economy.Stone--;
                        RecordLargeUse(b.FactionId, ResourceKind.Stone, LargeEconomyUseKind.ProcessingBuilding, 1);
                        b.Input++;
                    }
                    if (b.InputSecondary < rules.BufferLimit && economy.Charcoal > 0)
                    {
                        economy.Charcoal--;
                        b.InputSecondary++;
                    }
                    if (b.InputSecondary < rules.BufferLimit)
                        for (int sourceIndex = 0; sourceIndex < world.BuildingCount; sourceIndex++)
                        {
                            ref var source = ref world.Buildings[sourceIndex];
                if (!IsActualBuilding(source) || !source.Complete || source.FactionId != b.FactionId || source.Kind != BuildingKind.CharcoalKiln || source.Output <= 0) continue;
                            source.Output--;
                            b.InputSecondary++;
                            break;
                        }
                    int brickStone = CraftInputFor(b, rules.BrickStoneInput), brickCharcoal = CraftInputFor(b, rules.BrickCharcoalInput);
                    if (b.Timer == 0 && b.Input >= brickStone && b.InputSecondary >= brickCharcoal && b.Output < rules.BufferLimit)
                    {
                        b.Input -= brickStone;
                        b.InputSecondary -= brickCharcoal;
                        b.Timer = CraftTicksFor(b, rules.BrickTicks);
                    }
                    if (b.Timer > 0 && --b.Timer == 0)
                    {
                        b.Output++;
                        economy.BricksProduced++;
                    }
                }
                else if (BooksAllowed(b.FactionId) && b.Kind == BuildingKind.Scriptorium)
                {
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    if (b.Input < rules.BufferLimit && economy.Gold > 0)
                    {
                        economy.Gold--;
                        b.Input++;
                    }
                    if (b.InputSecondary < rules.BufferLimit && economy.Wood > 0)
                    {
                        economy.Wood--;
                        RecordLargeUse(b.FactionId, ResourceKind.Wood, LargeEconomyUseKind.ProcessingBuilding, 1);
                        b.InputSecondary++;
                    }
                    int bookGold = CraftInputFor(b, rules.BookGoldInput), bookWood = CraftInputFor(b, rules.BookWoodInput);
                    if (b.Timer == 0 && b.Input >= bookGold && b.InputSecondary >= bookWood && b.Output < rules.BufferLimit)
                    {
                        b.Input -= bookGold;
                        b.InputSecondary -= bookWood;
                        b.Timer = CraftTicksFor(b, rules.BookTicks);
                    }
                    if (b.Timer > 0 && --b.Timer == 0)
                    {
                        b.Output++;
                        economy.BooksProduced++;
                    }
                }
                else if (PreservesAllowed(b.FactionId) && b.Kind == BuildingKind.DryingRack)
                {
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    // Pull from the core only while food is above the configured safety floor.
                    if (b.Input < rules.BufferLimit && economy.Food > rules.PreserveKeepFood)
                    {
                        economy.Food--;
                        b.Input++;
                    }
                    int preserveFood = CraftInputFor(b, rules.PreserveFoodInput);
                    if (b.Timer == 0 && b.Input >= preserveFood && b.Output < rules.BufferLimit)
                    {
                        b.Input -= preserveFood;
                        b.Timer = CraftTicksFor(b, rules.PreserveTicks);
                    }
                    if (b.Timer > 0 && --b.Timer == 0)
                    {
                        b.Output++;
                        economy.PreservesProduced++;
                    }
                }
                else if (JewelsAllowed(b.FactionId) && b.Kind == BuildingKind.Lapidary)
                {
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    // Stone stays in the core above the configured floor so the mountain defences keep their reserve.
                    if (b.Input < rules.BufferLimit && economy.Stone > rules.JewelKeepStone)
                    {
                        economy.Stone--;
                        RecordLargeUse(b.FactionId, ResourceKind.Stone, LargeEconomyUseKind.ProcessingBuilding, 1);
                        b.Input++;
                    }
                    if (b.InputSecondary < rules.BufferLimit && economy.Ore > 0)
                    {
                        economy.Ore--;
                        b.InputSecondary++;
                    }
                    int jewelStone = CraftInputFor(b, rules.JewelStoneInput), jewelOre = CraftInputFor(b, rules.JewelOreInput);
                    if (b.Timer == 0 && b.Input >= jewelStone && b.InputSecondary >= jewelOre && b.Output < rules.BufferLimit)
                    {
                        b.Input -= jewelStone;
                        b.InputSecondary -= jewelOre;
                        b.Timer = CraftTicksFor(b, rules.JewelTicks);
                    }
                    if (b.Timer > 0 && --b.Timer == 0)
                    {
                        b.Output++;
                        economy.JewelsProduced++;
                    }
                }
                else if (TradeGoodsAllowed(b.FactionId) && b.Kind == BuildingKind.Weaver)
                {
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    // The reserve is strict: 80 wood is kept, and pulling starts at 81.
                    if (b.Input < rules.BufferLimit && economy.Wood > rules.TradeGoodsKeepWood)
                    {
                        economy.Wood--;
                        RecordLargeUse(b.FactionId, ResourceKind.Wood, LargeEconomyUseKind.ProcessingBuilding, 1);
                        b.Input++;
                    }
                    int tradeWood = CraftInputFor(b, rules.TradeGoodsWoodInput);
                    if (b.Timer == 0 && b.Input >= tradeWood && b.Output < rules.BufferLimit)
                    {
                        b.Input -= tradeWood;
                        b.Timer = CraftTicksFor(b, rules.TradeGoodsTicks);
                    }
                    if (b.Timer > 0 && --b.Timer == 0)
                    {
                        b.Output++;
                        economy.TradeGoodsProduced++;
                        int lineIndex = LineForBuilding(b.FactionId, b.Id);
                        if (lineIndex >= 0 && IsAutoLine(lineIndex)) SetSimpleHaulers(b.FactionId, b.Id, 0, 1);
                    }
                }
                else if (IncenseAllowed(b.FactionId) && b.Kind == BuildingKind.IncenseHouse)
                {
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    if (b.Input < rules.BufferLimit && economy.Wood > rules.IncenseKeepWood)
                    {
                        economy.Wood--;
                        RecordLargeUse(b.FactionId, ResourceKind.Wood, LargeEconomyUseKind.ProcessingBuilding, 1);
                        b.Input++;
                    }
                    if (b.InputSecondary < rules.BufferLimit && economy.Food > rules.IncenseKeepFood)
                    {
                        economy.Food--;
                        b.InputSecondary++;
                    }
                    int incenseWood = CraftInputFor(b, rules.IncenseWoodInput), incenseFood = CraftInputFor(b, rules.IncenseFoodInput);
                    if (b.Timer == 0 && b.Input >= incenseWood && b.InputSecondary >= incenseFood
                        && b.Output < rules.BufferLimit && economy.Incense < rules.IncenseStockLimit)
                    {
                        b.Input -= incenseWood;
                        b.InputSecondary -= incenseFood;
                        b.Timer = CraftTicksFor(b, rules.IncenseTicks);
                    }
                    if (b.Timer > 0 && --b.Timer == 0)
                    {
                        b.Output++;
                        economy.IncenseProduced++;
                    }
                }
                else if (CharmsAllowed(b.FactionId) && b.Kind == BuildingKind.CharmWorkshop)
                {
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    if (b.Input < rules.BufferLimit && economy.Wood > rules.CharmKeepWood)
                    {
                        economy.Wood--;
                        RecordLargeUse(b.FactionId, ResourceKind.Wood, LargeEconomyUseKind.ProcessingBuilding, 1);
                        b.Input++;
                    }
                    int charmWood = CraftInputFor(b, rules.CharmWoodInput);
                    if (b.Timer == 0 && b.Input >= charmWood
                        && b.Output < rules.BufferLimit && economy.Charms < rules.CharmStockLimit)
                    {
                        b.Input -= charmWood;
                        b.Timer = CraftTicksFor(b, rules.CharmTicks);
                    }
                    if (b.Timer > 0 && --b.Timer == 0)
                    {
                        b.Output++;
                        economy.CharmsProduced++;
                    }
                }
                else if (RepairKitsAllowed(b.FactionId) && b.Kind == BuildingKind.RepairYard)
                {
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    if (b.Input < rules.BufferLimit && economy.Wood > rules.RepairKitKeepWood)
                    {
                        economy.Wood--;
                        RecordLargeUse(b.FactionId, ResourceKind.Wood, LargeEconomyUseKind.ProcessingBuilding, 1);
                        b.Input++;
                    }
                    int repairWood = CraftInputFor(b, rules.RepairKitWoodInput);
                    if (b.Timer == 0 && b.Input >= repairWood
                        && b.Output < rules.BufferLimit && economy.RepairKits < rules.RepairKitStockLimit)
                    {
                        b.Input -= repairWood;
                        b.Timer = CraftTicksFor(b, rules.RepairKitTicks);
                    }
                    if (b.Timer > 0 && --b.Timer == 0)
                    {
                        b.Output++;
                        economy.RepairKitsProduced++;
                    }
                }
                else if (ToolsAllowed(b.FactionId) && b.Kind == BuildingKind.ToolShop)
                {
                    ref var economy = ref world.Economies[b.FactionId - 1];
                    if (b.Input < rules.BufferLimit && economy.Wood > rules.ToolKeepWood)
                    {
                        economy.Wood--; RecordLargeUse(b.FactionId, ResourceKind.Wood, LargeEconomyUseKind.ProcessingBuilding, 1); b.Input++;
                    }
                    int toolWood = CraftInputFor(b, rules.ToolWoodInput);
                    if (b.Timer == 0 && b.Input >= toolWood
                        && b.Output < rules.BufferLimit && economy.Tools < rules.ToolStockLimit)
                    {
                        b.Input -= toolWood;
                        b.Timer = CraftTicksFor(b, rules.ToolTicks);
                    }
                    if (b.Timer > 0 && --b.Timer == 0)
                    {
                        b.Output++;
                        economy.ToolsProduced++;
                    }
                }
                else if (world.Config.Economy.BeltComponents && b.Kind == BuildingKind.Storage)
                {
                    // Storage is passive: it accepts any one resource kind and releases one item per tick
                    // whenever its outward belt has room. The kind is retained until the last item leaves.
                }
                ObserveCraftFlowBuilding(ref b, craftFlowTimerBefore);
            }
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!IsActualBuilding(b) || !b.Complete || BuildingPaused(b) || b.Output == 0 && !(b.Kind == BuildingKind.Storage && b.StorageCount > 0)) continue;
                int port = OutputCell(b);
                if (port < 0) continue;
                ref var belt = ref world.Belts[port];
                if (belt.FactionId != b.FactionId || belt.Item != 0) continue;
                belt.Item = b.Kind == BuildingKind.Storage ? b.StorageKind : b.Kind == BuildingKind.Drill
                    ? world.Nodes[b.NodeId - 1].Definition.Kind : OutputKind(b);
                belt.Progress = 0;
                if (b.Kind == BuildingKind.Storage) { b.StorageCount--; b.Input = b.StorageCount; b.Output = b.StorageCount; if (b.StorageCount == 0) b.StorageKind = 0; }
                else b.Output--;
            }
            EndCraftFlowTick();
        }

        private static ResourceKind OutputKind(BuildingKind kind)
            => kind == BuildingKind.Mine ? ResourceKind.Ore : kind == BuildingKind.LumberCamp ? ResourceKind.Wood : kind == BuildingKind.Quarry ? ResourceKind.Stone : kind == BuildingKind.Farm ? ResourceKind.Food
                : kind == BuildingKind.CharcoalKiln ? ResourceKind.Charcoal : kind == BuildingKind.Steelworks ? ResourceKind.Steel
                : kind == BuildingKind.Fletcher ? ResourceKind.BowGear : kind == BuildingKind.Mill ? ResourceKind.Rations
                : kind == BuildingKind.BrickKiln ? ResourceKind.Bricks : kind == BuildingKind.Saddlery ? ResourceKind.Tack
                : kind == BuildingKind.Sawmill ? ResourceKind.Planks
                : kind == BuildingKind.Scriptorium ? ResourceKind.Books
                : kind == BuildingKind.DryingRack ? ResourceKind.Preserves
                : kind == BuildingKind.Lapidary ? ResourceKind.Gems
                : kind == BuildingKind.Weaver ? ResourceKind.TradeGoods
                 : kind == BuildingKind.Stonecutter ? ResourceKind.CutStone
                 : kind == BuildingKind.Joinery ? ResourceKind.Frames
                 : kind == BuildingKind.Assembler ? (ResourceKind)0
                 : kind == BuildingKind.IncenseHouse ? ResourceKind.Incense : kind == BuildingKind.CharmWorkshop ? ResourceKind.Charms : kind == BuildingKind.RepairYard ? ResourceKind.RepairKits : kind == BuildingKind.ToolShop ? ResourceKind.Tools : ResourceKind.Metal;

        private static ResourceKind OutputKind(BuildingState building)
            => building.Kind == BuildingKind.Hut
                ? (building.Recipe == RecipeKind.StoneTools ? ResourceKind.StoneTools : building.Recipe == RecipeKind.Jerky ? ResourceKind.Jerky : (ResourceKind)0)
                : OutputKind(building.Kind);

        private int CraftTicksFor(BuildingState building, int baseTicks) => CraftTicksScaled(building, baseTicks, 1000);

        /// <summary>CraftTicksFor with an extra time factor (AgeStrengths). Kept apart so the two-argument form stays unique.</summary>
        private int CraftTicksScaled(BuildingState building, int baseTicks, int extraPermille)
        {
            int result = Math.Max(1, checked((baseTicks * extraPermille + 999) / 1000));
            if (!CraftTree1On || building.Level < 2)
                return AgeFourOn && HasTech(building.FactionId, TechKind.GrandWorkshop)
                    ? Math.Max(1, checked((result * 4 + 4) / 5)) : result;
            if (building.Level >= 3 && CraftTree3On)
                result = Math.Max(1, checked((baseTicks * world.Config.Economy.MechanismBuildingTimePermille + 999) / 1000));
            else result = Math.Max(1, checked((baseTicks * 2 + 2) / 3));
            return AgeFourOn && HasTech(building.FactionId, TechKind.GrandWorkshop)
                ? Math.Max(1, checked((result * 4 + 4) / 5)) : result;
        }

        private int CraftInputFor(BuildingState building, int baseInput)
        {
            if (CraftTree3On && building.Level >= 3)
                return Math.Max(1, checked(baseInput * world.Config.Economy.MechanismBuildingMaterialPermille / 1000));
            return baseInput;
        }

        private void AdvanceAssembler(ref BuildingState building, ref FactionEconomy economy)
        {
            if (building.Recipe == RecipeKind.None) return;
            if (building.Timer > 0)
            {
                if (--building.Timer == 0)
                {
                    if (building.Recipe == RecipeKind.Frames)
                    {
                        economy.Frames++; economy.FramesProduced++;
                        RecordCraftTracePlanks(building.FactionId, CraftTraceConstruction,
                            CraftInputFor(building, world.Config.Economy.CraftTreeJoineryPlankInput));
                    }
                    else if (building.Recipe == RecipeKind.ArmsKit)
                    {
                        if (building.InputSecondaryKind == ResourceKind.Steel) { economy.FineArmsKit++; economy.FineArmsKitProduced++; }
                        else { economy.ArmsKit++; economy.ArmsKitProduced++; }
                        RecordCraftTracePlanks(building.FactionId, CraftTraceWeapons,
                            CraftInputFor(building, world.Config.Economy.ArmsKitPlankInput));
                    }
                    else if (building.Recipe == RecipeKind.Scholarly)
                    {
                        economy.Scholarly++; economy.ScholarlyProduced++;
                        RecordCraftTracePlanks(building.FactionId, CraftTraceScholarly,
                            CraftInputFor(building, world.Config.Economy.ScholarlyPlankInput));
                    }
                    else if (building.Recipe == RecipeKind.SiegeParts) { economy.SiegeParts++; economy.SiegePartsProduced++; }
                    else if (building.Recipe == RecipeKind.Mechanism) { economy.Mechanism++; economy.MechanismProduced++; }
                    else if (building.Recipe == RecipeKind.SiegeEngine) { economy.SiegeEngine++; economy.SiegeEngineProduced++; }
                    else if (building.Recipe == RecipeKind.EliteKit) { economy.EliteKit++; economy.EliteKitProduced++; }
                    building.Input = 0; building.InputSecondary = 0; building.InputTertiary = 0; building.InputSecondaryKind = 0;
                }
                return;
            }
            var rules = world.Config.Economy;
            if (CraftRequiredAiOtherPlankUseBlocked(building.FactionId, building.Recipe)) return;
            if (building.Recipe == RecipeKind.Frames)
            {
                int plankInput = CraftInputFor(building, rules.CraftTreeJoineryPlankInput);
                int stoneInput = CraftInputFor(building, rules.CraftTreeJoineryCutStoneInput);
                Fill(ref economy.Planks, ref building.Input, plankInput);
                if (economy.CutStone > 0 || building.InputSecondaryKind == ResourceKind.CutStone)
                {
                    Fill(ref economy.CutStone, ref building.InputSecondary, stoneInput);
                    building.InputSecondaryKind = ResourceKind.CutStone;
                }
                else
                {
                    Fill(ref economy.Bricks, ref building.InputSecondary, stoneInput);
                    building.InputSecondaryKind = ResourceKind.Bricks;
                }
                if (building.Input < plankInput || building.InputSecondary < stoneInput) return;
                if (building.InputSecondaryKind == ResourceKind.Bricks) economy.BricksConsumed = checked(economy.BricksConsumed + stoneInput);
                else economy.CutStoneConsumed = checked(economy.CutStoneConsumed + stoneInput);
                building.Timer = CraftTicksFor(building, rules.CraftTreeJoineryTicks);
                return;
            }
            if (building.Recipe == RecipeKind.ArmsKit)
            {
                int plankInput = CraftInputFor(building, rules.ArmsKitPlankInput);
                Fill(ref economy.Planks, ref building.Input, plankInput);
                if (building.InputSecondaryKind == 0)
                    building.InputSecondaryKind = economy.Steel >= rules.ArmsKitSteelInput ? ResourceKind.Steel : ResourceKind.Charcoal;
                if (building.InputSecondaryKind == ResourceKind.Steel) Fill(ref economy.Steel, ref building.InputSecondary, CraftInputFor(building, rules.ArmsKitSteelInput));
                else Fill(ref economy.Charcoal, ref building.InputSecondary, CraftInputFor(building, rules.ArmsKitCharcoalInput));
                if (building.Input < CraftInputFor(building, rules.ArmsKitPlankInput) || building.InputSecondary < CraftInputFor(building, building.InputSecondaryKind == ResourceKind.Steel ? rules.ArmsKitSteelInput : rules.ArmsKitCharcoalInput)) return;
                building.Timer = CraftTicksFor(building, rules.ArmsKitTicks);
                return;
            }
            if (building.Recipe == RecipeKind.Scholarly)
            {
                Fill(ref economy.Planks, ref building.Input, CraftInputFor(building, rules.ScholarlyPlankInput));
                Fill(ref economy.Charcoal, ref building.InputSecondary, CraftInputFor(building, rules.ScholarlyCharcoalInput));
                Fill(ref economy.Food, ref building.InputTertiary, CraftInputFor(building, rules.ScholarlyFoodInput));
                building.InputSecondaryKind = ResourceKind.Charcoal;
                if (building.Input < CraftInputFor(building, rules.ScholarlyPlankInput) || building.InputSecondary < CraftInputFor(building, rules.ScholarlyCharcoalInput) || building.InputTertiary < CraftInputFor(building, rules.ScholarlyFoodInput)) return;
                building.Timer = CraftTicksFor(building, rules.ScholarlyTicks);
                return;
            }
            if (building.Recipe == RecipeKind.SiegeParts)
            {
                if (economy.SiegeParts >= rules.SiegePartsStockLimit) return;
                int frames = CraftInputFor(building, rules.SiegePartsFramesInput), arms = CraftInputFor(building, rules.SiegePartsArmsKitInput);
                Fill(ref economy.Frames, ref building.Input, frames);
                Fill(ref economy.ArmsKit, ref building.InputSecondary, arms);
                building.InputSecondaryKind = ResourceKind.ArmsKit;
                if (building.Input < frames || building.InputSecondary < arms) return;
                economy.FramesConsumed = checked(economy.FramesConsumed + frames);
                economy.ArmsKitConsumed = checked(economy.ArmsKitConsumed + arms);
                building.Timer = CraftTicksScaled(building, rules.SiegePartsTicks,
                    AgeStrength(building.FactionId, CivKind.Metallurgy, 4) ? rules.MetallurgySiegePartsTimePermille : 1000);
                return;
            }
            if (building.Recipe == RecipeKind.Mechanism)
            {
                int arms = CraftInputFor(building, rules.MechanismArmsKitInput), scholarly = CraftInputFor(building, rules.MechanismScholarlyInput), stone = CraftInputFor(building, rules.MechanismCutStoneInput);
                Fill(ref economy.ArmsKit, ref building.Input, arms);
                Fill(ref economy.Scholarly, ref building.InputSecondary, scholarly);
                Fill(ref economy.CutStone, ref building.InputTertiary, stone);
                building.InputSecondaryKind = ResourceKind.Scholarly;
                if (building.Input < arms || building.InputSecondary < scholarly || building.InputTertiary < stone) return;
                economy.ArmsKitConsumed = checked(economy.ArmsKitConsumed + arms);
                economy.ScholarlyConsumed = checked(economy.ScholarlyConsumed + scholarly);
                economy.CutStoneConsumed = checked(economy.CutStoneConsumed + stone);
                building.Timer = CraftTicksFor(building, rules.MechanismTicks);
                return;
            }
            if (building.Recipe == RecipeKind.SiegeEngine)
            {
                int parts = CraftInputFor(building, rules.SiegeEngineSiegePartsInput), mechanism = CraftInputFor(building, rules.SiegeEngineMechanismInput);
                Fill(ref economy.SiegeParts, ref building.Input, parts);
                Fill(ref economy.Mechanism, ref building.InputSecondary, mechanism);
                building.InputSecondaryKind = ResourceKind.Mechanism;
                if (building.Input < parts || building.InputSecondary < mechanism) return;
                economy.SiegePartsConsumed = checked(economy.SiegePartsConsumed + parts);
                economy.MechanismConsumed = checked(economy.MechanismConsumed + mechanism);
                building.Timer = CraftTicksFor(building, rules.SiegeEngineTicks);
                return;
            }
            if (building.Recipe == RecipeKind.EliteKit)
            {
                int arms = CraftInputFor(building, rules.EliteKitArmsKitInput), mechanism = CraftInputFor(building, rules.EliteKitMechanismInput), metal = CraftInputFor(building, rules.EliteKitMetalInput);
                Fill(ref economy.ArmsKit, ref building.Input, arms);
                Fill(ref economy.Mechanism, ref building.InputSecondary, mechanism);
                Fill(ref economy.Metal, ref building.InputTertiary, metal);
                building.InputSecondaryKind = ResourceKind.Mechanism;
                if (building.Input < arms || building.InputSecondary < mechanism || building.InputTertiary < metal) return;
                economy.ArmsKitConsumed = checked(economy.ArmsKitConsumed + arms);
                economy.MechanismConsumed = checked(economy.MechanismConsumed + mechanism);
                building.Timer = CraftTicksFor(building, rules.EliteKitTicks);
            }
        }

        private static void Fill(ref int stock, ref int slot, int required)
        {
            if (slot >= required || stock <= 0) return;
            int amount = Math.Min(stock, required - slot);
            stock -= amount; slot += amount;
        }

        private void FillRaw(uint faction, ResourceKind resource, ref int stock, ref int slot, int required)
        {
            if (slot >= required || stock <= 0) return;
            int amount = Math.Min(stock, required - slot);
            stock -= amount; slot += amount;
            RecordLargeUse(faction, resource, LargeEconomyUseKind.ProcessingBuilding, amount);
        }

        private void SetAssemblerRecipe(uint faction, uint buildingId, RecipeKind recipe)
            => SetBuildingRecipe(faction, buildingId, recipe);

        private void SetBuildingRecipe(uint faction, uint buildingId, RecipeKind recipe)
        {
            if (!OwnBuilding(faction, buildingId, out int index)) return;
            ref var building = ref world.Buildings[index];
            if (!IsActualBuilding(building)) return;
            if (building.Kind == BuildingKind.Hut)
            {
                if (!PrimitiveCraftOn || (recipe != RecipeKind.StoneTools && recipe != RecipeKind.Jerky)) return;
            }
            else if (!AssemblerAllowed(faction) || building.Kind != BuildingKind.Assembler || (byte)recipe > (byte)RecipeKind.EliteKit
                || (recipe == RecipeKind.SiegeParts || recipe == RecipeKind.Mechanism) && !CraftTree3Allowed(faction)
                || (recipe == RecipeKind.SiegeEngine || recipe == RecipeKind.EliteKit) && !CraftTree4Allowed(faction)) return;
            RefundAssemblerInputs(faction, ref building);
            building.Recipe = recipe;
            building.Timer = 0;
            building.InputSecondaryKind = 0;
        }

        private void RefundAssemblerInputs(uint faction, ref BuildingState building)
        {
            ref var economy = ref world.Economies[faction - 1];
            if (building.Kind == BuildingKind.Hut && building.Recipe == RecipeKind.StoneTools)
            {
                economy.Stone += building.Input; economy.Wood += building.InputSecondary;
            }
            else if (building.Kind == BuildingKind.Hut && building.Recipe == RecipeKind.Jerky)
            {
                economy.Food += building.Input;
            }
            else if (building.Recipe == RecipeKind.Frames)
            {
                economy.Planks += building.Input;
                if (building.InputSecondaryKind == ResourceKind.Bricks) economy.Bricks += building.InputSecondary;
                else economy.CutStone += building.InputSecondary;
            }
            else if (building.Recipe == RecipeKind.ArmsKit)
            {
                if (building.InputSecondaryKind == ResourceKind.Steel) economy.Steel += building.InputSecondary;
                else economy.Charcoal += building.InputSecondary;
                economy.Planks += building.Input;
            }
            else if (building.Recipe == RecipeKind.Scholarly)
            {
                economy.Planks += building.Input; economy.Charcoal += building.InputSecondary; economy.Food += building.InputTertiary;
            }
            else if (building.Recipe == RecipeKind.SiegeParts)
            {
                economy.Frames += building.Input; economy.ArmsKit += building.InputSecondary;
            }
            else if (building.Recipe == RecipeKind.Mechanism)
            {
                economy.ArmsKit += building.Input; economy.Scholarly += building.InputSecondary; economy.CutStone += building.InputTertiary;
            }
            else if (building.Recipe == RecipeKind.SiegeEngine)
            {
                economy.SiegeParts += building.Input; economy.Mechanism += building.InputSecondary;
            }
            else if (building.Recipe == RecipeKind.EliteKit)
            {
                economy.ArmsKit += building.Input; economy.Mechanism += building.InputSecondary; economy.Metal += building.InputTertiary;
            }
            building.Input = 0; building.InputSecondary = 0; building.InputTertiary = 0; building.InputSecondaryKind = 0;
        }

        /// <summary>The cell just outside the middle of the side the building faces, or -1 off the map.</summary>
        private int OutputCell(BuildingState b) => OutputCell(b.OriginCell, SizeOf(b.Kind), b.Facing);

        private int OutputCell(int origin, int size, Facing facing)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int x0 = origin % width, z0 = origin / width, x, z;
            switch (facing)
            {
                case Facing.North: x = x0 + size / 2; z = z0 + size; break;
                case Facing.East: x = x0 + size; z = z0 + size / 2; break;
                case Facing.South: x = x0 + size / 2; z = z0 - 1; break;
                default: x = x0 - 1; z = z0 + size / 2; break;
            }
            return x < 0 || z < 0 || x >= width || z >= height ? -1 : z * width + x;
        }

        /// <summary>
        /// A belt pointing into a footprint hands its item to that building if it is the own, finished building and takes
        /// the item: a smelter takes ore while its input has room. Everything else holds the item on the belt.
        /// </summary>
        private bool TryFeedBuilding(int cell, uint faction, ResourceKind item)
        {
            var rules = world.Config.Economy;
            int width = world.Config.Map.WidthCells, x = cell % width, z = cell / width;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!IsActualBuilding(b)) continue;
                int size = SizeOf(b.Kind), x0 = b.OriginCell % width, z0 = b.OriginCell / width;
                if (x < x0 || x >= x0 + size || z < z0 || z >= z0 + size) continue;
                if (b.FactionId != faction || !b.Complete || BuildingPaused(b)) return false;
                if (b.Kind == BuildingKind.Smelter && item == ResourceKind.Ore && b.Input < rules.BufferLimit) { b.Input++; return true; }
                if (CharcoalKilnAllowed(faction) && b.Kind == BuildingKind.CharcoalKiln && item == ResourceKind.Wood && b.Input < rules.BufferLimit) { b.Input++; return true; }
                if (ProcessingOn && b.Kind == BuildingKind.Steelworks)
                {
                    if (item == ResourceKind.Metal && b.Input < rules.BufferLimit) { b.Input++; return true; }
                    if (item == ResourceKind.Charcoal && b.InputSecondary < rules.BufferLimit) { b.InputSecondary++; return true; }
                }
                if (ForestryOn && b.Kind == BuildingKind.Fletcher)
                {
                    if (item == ResourceKind.Wood && b.Input < rules.BufferLimit) { b.Input++; return true; }
                    if (item == ResourceKind.Food && b.InputSecondary < rules.BufferLimit) { b.InputSecondary++; return true; }
                }
                if (RationsOn && b.Kind == BuildingKind.Mill && item == ResourceKind.Food && b.Input < rules.BufferLimit)
                {
                    b.Input++; return true;
                }
                if (TackAllowed(faction) && b.Kind == BuildingKind.Saddlery)
                {
                    if (item == ResourceKind.Food && b.Input < rules.BufferLimit) { b.Input++; return true; }
                    if (item == ResourceKind.Wood && b.InputSecondary < rules.BufferLimit) { b.InputSecondary++; return true; }
                }
                if ((PlanksAllowed(faction) || CraftTreeCommonAllowed(faction)) && b.Kind == BuildingKind.Sawmill && item == ResourceKind.Wood && b.Input < rules.BufferLimit)
                {
                    b.Input++; return true;
                }
                if (PrimitiveCraftOn && b.Kind == BuildingKind.Hut && b.Recipe != RecipeKind.None)
                {
                    if (b.Recipe == RecipeKind.StoneTools && item == ResourceKind.Stone && b.Input < rules.BufferLimit) { b.Input++; return true; }
                    if (b.Recipe == RecipeKind.StoneTools && item == ResourceKind.Wood && b.InputSecondary < rules.BufferLimit) { b.InputSecondary++; return true; }
                    if (b.Recipe == RecipeKind.Jerky && item == ResourceKind.Food && b.Input < rules.BufferLimit) { b.Input++; return true; }
                }
                if (CraftTree2On && b.Kind == BuildingKind.Assembler && b.Recipe != RecipeKind.None)
                {
                    if (item == ResourceKind.Planks && (b.Recipe == RecipeKind.Frames || b.Recipe == RecipeKind.ArmsKit || b.Recipe == RecipeKind.Scholarly) && b.Input < rules.BufferLimit) { b.Input++; return true; }
                    if (b.Recipe == RecipeKind.SiegeParts && item == ResourceKind.Frames && b.Input < rules.BufferLimit) { b.Input++; return true; }
                    if (b.Recipe == RecipeKind.Mechanism && item == ResourceKind.ArmsKit && b.Input < rules.BufferLimit) { b.Input++; return true; }
                    if (item == ResourceKind.Food && b.Recipe == RecipeKind.Scholarly && b.InputTertiary < rules.BufferLimit) { b.InputTertiary++; return true; }
                    if (b.Recipe == RecipeKind.ArmsKit && item == ResourceKind.Steel && b.InputSecondary < rules.BufferLimit) { b.InputSecondaryKind = ResourceKind.Steel; b.InputSecondary++; return true; }
                    if ((b.Recipe == RecipeKind.ArmsKit && item == ResourceKind.Charcoal || b.Recipe == RecipeKind.Scholarly && item == ResourceKind.Charcoal)
                        && (b.InputSecondaryKind == 0 || b.InputSecondaryKind == ResourceKind.Charcoal) && b.InputSecondary < rules.BufferLimit)
                    { b.InputSecondaryKind = ResourceKind.Charcoal; b.InputSecondary++; return true; }
                    if (b.Recipe == RecipeKind.Frames && item == ResourceKind.CutStone && b.InputSecondary < rules.BufferLimit)
                    { b.InputSecondaryKind = ResourceKind.CutStone; b.InputSecondary++; return true; }
                    if (b.Recipe == RecipeKind.Frames && item == ResourceKind.Bricks && b.InputSecondary < rules.BufferLimit)
                    { b.InputSecondaryKind = ResourceKind.Bricks; b.InputSecondary++; return true; }
                    if (b.Recipe == RecipeKind.SiegeParts && item == ResourceKind.ArmsKit && b.InputSecondary < rules.BufferLimit)
                    { b.InputSecondaryKind = ResourceKind.ArmsKit; b.InputSecondary++; return true; }
                    if (b.Recipe == RecipeKind.Mechanism && item == ResourceKind.Scholarly && b.InputSecondary < rules.BufferLimit)
                    { b.InputSecondaryKind = ResourceKind.Scholarly; b.InputSecondary++; return true; }
                    if (b.Recipe == RecipeKind.Mechanism && item == ResourceKind.CutStone && b.InputTertiary < rules.BufferLimit)
                    { b.InputTertiary++; return true; }
                    if (b.Recipe == RecipeKind.SiegeEngine && item == ResourceKind.SiegeParts && b.Input < rules.BufferLimit) { b.Input++; return true; }
                    if (b.Recipe == RecipeKind.SiegeEngine && item == ResourceKind.Mechanism && b.InputSecondary < rules.BufferLimit) { b.InputSecondaryKind = ResourceKind.Mechanism; b.InputSecondary++; return true; }
                    if (b.Recipe == RecipeKind.EliteKit && item == ResourceKind.ArmsKit && b.Input < rules.BufferLimit) { b.Input++; return true; }
                    if (b.Recipe == RecipeKind.EliteKit && item == ResourceKind.Mechanism && b.InputSecondary < rules.BufferLimit) { b.InputSecondaryKind = ResourceKind.Mechanism; b.InputSecondary++; return true; }
                    if (b.Recipe == RecipeKind.EliteKit && item == ResourceKind.Metal && b.InputTertiary < rules.BufferLimit) { b.InputTertiary++; return true; }
                }
                if (StonecutterAllowed(faction) && b.Kind == BuildingKind.Stonecutter && item == ResourceKind.Stone && b.Input < rules.BufferLimit)
                {
                    b.Input++; return true;
                }
                if (JoineryAllowed(faction) && b.Kind == BuildingKind.Joinery)
                {
                    if (item == ResourceKind.Planks && b.Input < rules.BufferLimit) { b.Input++; return true; }
                    if (item == ResourceKind.CutStone && b.InputSecondary < rules.BufferLimit) { b.InputSecondary++; return true; }
                }
                if (BricksAllowed(faction) && b.Kind == BuildingKind.BrickKiln)
                {
                    if (item == ResourceKind.Stone && b.Input < rules.BufferLimit) { b.Input++; return true; }
                    if (item == ResourceKind.Charcoal && b.InputSecondary < rules.BufferLimit) { b.InputSecondary++; return true; }
                }
                if (BooksAllowed(faction) && b.Kind == BuildingKind.Scriptorium)
                {
                    if (item == ResourceKind.Gold && b.Input < rules.BufferLimit) { b.Input++; return true; }
                    if (item == ResourceKind.Wood && b.InputSecondary < rules.BufferLimit) { b.InputSecondary++; return true; }
                }
                if (PreservesAllowed(faction) && b.Kind == BuildingKind.DryingRack && item == ResourceKind.Food && b.Input < rules.BufferLimit)
                {
                    b.Input++; return true;
                }
                if (JewelsAllowed(faction) && b.Kind == BuildingKind.Lapidary)
                {
                    if (item == ResourceKind.Stone && b.Input < rules.BufferLimit) { b.Input++; return true; }
                    if (item == ResourceKind.Ore && b.InputSecondary < rules.BufferLimit) { b.InputSecondary++; return true; }
                }
                if (TradeGoodsAllowed(faction) && b.Kind == BuildingKind.Weaver && item == ResourceKind.Wood && b.Input < rules.BufferLimit)
                {
                    b.Input++; return true;
                }
                if (IncenseAllowed(faction) && b.Kind == BuildingKind.IncenseHouse)
                {
                    if (item == ResourceKind.Wood && b.Input < rules.BufferLimit) { b.Input++; return true; }
                    if (item == ResourceKind.Food && b.InputSecondary < rules.BufferLimit) { b.InputSecondary++; return true; }
                }
                if (CharmsAllowed(faction) && b.Kind == BuildingKind.CharmWorkshop && item == ResourceKind.Wood && b.Input < rules.BufferLimit)
                {
                    b.Input++; return true;
                }
                if (RepairKitsAllowed(faction) && b.Kind == BuildingKind.RepairYard && item == ResourceKind.Wood && b.Input < rules.BufferLimit)
                {
                    b.Input++; return true;
                }
                if (ToolsAllowed(faction) && b.Kind == BuildingKind.ToolShop && item == ResourceKind.Wood && b.Input < rules.BufferLimit)
                {
                    b.Input++; return true;
                }
                if (world.Config.Economy.BeltComponents && b.Kind == BuildingKind.Storage && b.StorageCount < rules.StorageCapacity
                    && (b.StorageCount == 0 || b.StorageKind == item))
                {
                    b.StorageKind = item; b.StorageCount++; b.Input = b.StorageCount; b.Output = b.StorageCount; return true;
                }
                return false;
            }
            return false;
        }

        /// <summary>
        /// Checks the same belt-input combinations as TryFeedBuilding without looking at BufferLimit. Storage route
        /// discovery uses this predicate so a full receiver still makes the storage a valid waiting point.
        /// </summary>
        private bool BeltCanFeedBuilding(int cell, uint faction, ResourceKind item)
        {
            int width = world.Config.Map.WidthCells, x = cell % width, z = cell / width;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!IsActualBuilding(b)) continue;
                int size = SizeOf(b.Kind), x0 = b.OriginCell % width, z0 = b.OriginCell / width;
                if (x < x0 || x >= x0 + size || z < z0 || z >= z0 + size) continue;
                if (b.FactionId != faction || !b.Complete) return false;
                if (b.Kind == BuildingKind.Smelter && item == ResourceKind.Ore) return true;
                if (CharcoalKilnAllowed(faction) && b.Kind == BuildingKind.CharcoalKiln && item == ResourceKind.Wood) return true;
                if (ProcessingOn && b.Kind == BuildingKind.Steelworks
                    && (item == ResourceKind.Metal || item == ResourceKind.Charcoal)) return true;
                if (ForestryOn && b.Kind == BuildingKind.Fletcher
                    && (item == ResourceKind.Wood || item == ResourceKind.Food)) return true;
                if (RationsOn && b.Kind == BuildingKind.Mill && item == ResourceKind.Food) return true;
                if (TackAllowed(faction) && b.Kind == BuildingKind.Saddlery
                    && (item == ResourceKind.Food || item == ResourceKind.Wood)) return true;
                if ((PlanksAllowed(faction) || CraftTreeCommonAllowed(faction)) && b.Kind == BuildingKind.Sawmill
                    && item == ResourceKind.Wood) return true;
                if (PrimitiveCraftOn && b.Kind == BuildingKind.Hut && b.Recipe != RecipeKind.None
                    && (item == ResourceKind.Food || item == ResourceKind.Wood || item == ResourceKind.Stone)) return true;
                if (CraftTree2On && b.Kind == BuildingKind.Assembler && b.Recipe != RecipeKind.None
                    && item == ResourceKind.Food && b.Recipe == RecipeKind.Scholarly) return true;
                if (CraftTree4On && b.Kind == BuildingKind.Assembler && (b.Recipe == RecipeKind.SiegeEngine || b.Recipe == RecipeKind.EliteKit)
                    && (item == ResourceKind.SiegeParts || item == ResourceKind.ArmsKit || item == ResourceKind.Mechanism || item == ResourceKind.Metal)) return true;
                if (StonecutterAllowed(faction) && b.Kind == BuildingKind.Stonecutter && item == ResourceKind.Stone) return true;
                if (JoineryAllowed(faction) && b.Kind == BuildingKind.Joinery
                    && (item == ResourceKind.Planks || item == ResourceKind.CutStone)) return true;
                if (BricksAllowed(faction) && b.Kind == BuildingKind.BrickKiln
                    && (item == ResourceKind.Stone || item == ResourceKind.Charcoal)) return true;
                if (BooksAllowed(faction) && b.Kind == BuildingKind.Scriptorium
                    && (item == ResourceKind.Gold || item == ResourceKind.Wood)) return true;
                if (PreservesAllowed(faction) && b.Kind == BuildingKind.DryingRack && item == ResourceKind.Food) return true;
                if (JewelsAllowed(faction) && b.Kind == BuildingKind.Lapidary
                    && (item == ResourceKind.Stone || item == ResourceKind.Ore)) return true;
                if (TradeGoodsAllowed(faction) && b.Kind == BuildingKind.Weaver && item == ResourceKind.Wood) return true;
                if (IncenseAllowed(faction) && b.Kind == BuildingKind.IncenseHouse
                    && (item == ResourceKind.Wood || item == ResourceKind.Food)) return true;
                if (CharmsAllowed(faction) && b.Kind == BuildingKind.CharmWorkshop && item == ResourceKind.Wood) return true;
                if (RepairKitsAllowed(faction) && b.Kind == BuildingKind.RepairYard && item == ResourceKind.Wood) return true;
                if (ToolsAllowed(faction) && b.Kind == BuildingKind.ToolShop && item == ResourceKind.Wood) return true;
                if (world.Config.Economy.BeltComponents && b.Kind == BuildingKind.Storage
                    && b.StorageCount < world.Config.Economy.StorageCapacity
                    && (b.StorageCount == 0 || b.StorageKind == item)) return true;
                return false;
            }
            return false;
        }

        /// <summary>
        /// A mine's footprint must cover exactly one resource point, an ore point with ore left, and otherwise be open
        /// ground with no belt and outside every core.
        /// </summary>
        private bool MineSiteIsClear(uint faction, int origin, out uint nodeId)
            => ResourceBuildingSiteIsClear(origin, world.Config.Economy.MineSizeCells, ResourceKind.Ore, out nodeId);

        private bool LumberCampSiteIsClear(uint faction, int origin, out uint nodeId)
            => ResourceBuildingSiteIsClear(origin, world.Config.Economy.LumberCampSizeCells, ResourceKind.Wood, out nodeId);

        private bool QuarrySiteIsClear(uint faction, int origin, out uint nodeId)
            => ResourceBuildingSiteIsClear(origin, world.Config.Economy.QuarrySizeCells, ResourceKind.Stone, out nodeId);

        private bool ResourceBuildingSiteIsClear(int origin, int size, ResourceKind required, out uint nodeId)
        {
            nodeId = 0;
            var footprint = Footprint(origin, size);
            foreach (int cell in footprint)
            {
                foreach (var node in world.Nodes)
                {
                    if (world.Map.Cell(node.Definition.Position) != cell) continue;
                    if (node.Definition.Kind != required || node.Remaining <= 0 || nodeId != 0) return false;
                    nodeId = node.Definition.Id;
                }
            }
            if (nodeId == 0) return false;
            if (NodeCoveredByMiningBuilding(nodeId)) return false;
            foreach (int cell in footprint)
            {
                if (!world.Map.IsPassable(cell) || IsReservedBuildingCell(cell)
                    || world.Belts[cell].FactionId != 0 || InsideAnyCore(cell)) return false;
            }
            return true;
        }

        /// <summary>Common drills cover exactly one remaining wood, stone, ore, or usable gold point.</summary>
        private bool DrillSiteIsClear(uint faction, int origin, out uint nodeId, bool goldAlreadyUsable = false)
        {
            nodeId = 0;
            var footprint = Footprint(origin, world.Config.Economy.DrillSizeCells);
            foreach (int cell in footprint)
            {
                foreach (var node in world.Nodes)
                {
                    if (world.Map.Cell(node.Definition.Position) != cell) continue;
                    if (node.Remaining <= 0 || (node.Definition.Kind != ResourceKind.Wood && node.Definition.Kind != ResourceKind.Stone
                        && node.Definition.Kind != ResourceKind.Ore && node.Definition.Kind != ResourceKind.Gold) || nodeId != 0) return false;
                    nodeId = node.Definition.Id;
                }
            }
            if (nodeId == 0) return false;
            if (NodeCoveredByMiningBuilding(nodeId)) return false;
            if (world.Nodes[nodeId - 1].Definition.Kind == ResourceKind.Gold && !goldAlreadyUsable
                && !GoldTargetUsable(faction, nodeId, world.Map.Cell(OwnCore(faction).Definition.Position))) return false;
            foreach (int cell in footprint)
            {
                if (!world.Map.IsPassable(cell) || IsReservedBuildingCell(cell)
                    || world.Belts[cell].FactionId != 0 || InsideAnyCore(cell)) return false;
            }
            return true;
        }

        private bool NodeCoveredByMiningBuilding(uint nodeId)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (IsActualBuilding(building) && building.NodeId == nodeId
                    && (building.Kind == BuildingKind.Drill || building.Kind == BuildingKind.LumberCamp
                        || building.Kind == BuildingKind.Quarry || building.Kind == BuildingKind.Mine)) return true;
            }
            return false;
        }


        /// <summary>
        /// Carrying by hand (12.3), economy step. At the source's work cell the villager takes what the output holds, up to
        /// a full load, and leaves as soon as it has a full load or the output is empty: ore from a mine goes to the own
        /// finished smelter with the lowest id (or to the core when there is none), metal goes to the core. At a smelter it
        /// puts in what the input has room for and waits with the rest. A source that is gone ends the carrying.
        /// </summary>
        private void Haul(ref VillagerState v)
        {
            var rules = world.Config.Economy;
            ref var source = ref world.Buildings[v.HaulFrom - 1];
            if (!IsActualBuilding(source) || !source.Complete || BuildingPaused(source))
            {
                v.HaulFrom = 0; v.HaulTo = 0; v.HaulNodeId = 0;
                v.Task = v.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.Idle;
                return;
            }
            if (v.Task == VillagerTask.ToPickup)
            {
                if (!InRange(v.Position, world.Map.Center(source.WorkCell), GatherReach)) return;
                var kind = source.Kind == BuildingKind.Storage ? source.StorageKind
                    : source.Kind == BuildingKind.Drill ? world.Nodes[source.NodeId - 1].Definition.Kind : OutputKind(source);
                if (v.Carry > 0 && v.CarryKind != kind) { v.Task = VillagerTask.ToDropOff; return; }
                int available = source.Kind == BuildingKind.Storage ? source.StorageCount : source.Output;
                int take = Math.Min(available, CarryFor(v.FactionId) - v.Carry);
                if (source.Kind == BuildingKind.Storage) { source.StorageCount -= take; source.Input = source.StorageCount; source.Output = source.StorageCount; if (source.StorageCount == 0) source.StorageKind = 0; }
                else source.Output -= take;
                v.Carry += take;
                v.CarryKind = kind;
                if (v.Carry == 0 || (v.Carry < CarryFor(v.FactionId) && source.Output > 0)) return;
                // An explicit V3-6 destination (smelter metal or kiln charcoal to a steelworks) survives pickup;
                // the old automatic mine-to-smelter choice is used only when no destination was assigned.
                if (v.HaulTo == 0) v.HaulTo = source.Kind == BuildingKind.Mine ? OwnSmelter(v.FactionId) : 0;
                v.Task = v.HaulTo != 0 ? VillagerTask.ToDeliver : VillagerTask.ToDropOff;
                return;
            }
            ref var target = ref world.Buildings[v.HaulTo - 1];
            if (!IsActualBuilding(target) || !target.Complete || BuildingPaused(target)) { v.HaulTo = 0; v.Task = VillagerTask.ToDropOff; return; }
            if (!InRange(v.Position, world.Map.Center(target.WorkCell), GatherReach)) return;
            int put = PutIntoProcessingTarget(ref target, v.CarryKind, v.Carry);
            v.Carry -= put;
            if (v.Carry == 0) v.Task = VillagerTask.ToPickup;
        }

        private int PutIntoProcessingTarget(ref BuildingState target, ResourceKind kind, int amount)
        {
            if (BuildingPaused(target)) return 0;
            var rules = world.Config.Economy;
            if (target.Kind == BuildingKind.Smelter && kind == ResourceKind.Ore)
            {
                int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
            }
            if (ProcessingOn && target.Kind == BuildingKind.Steelworks)
            {
                if (kind == ResourceKind.Metal)
                {
                    int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
                }
                if (kind == ResourceKind.Charcoal)
                {
                    int put = Math.Min(amount, rules.BufferLimit - target.InputSecondary); target.InputSecondary += put; return put;
                }
            }
            if (ForestryOn && target.Kind == BuildingKind.Fletcher && kind == ResourceKind.Wood)
            {
                int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
            }
            if (RationsOn && target.Kind == BuildingKind.Mill && kind == ResourceKind.Food)
            {
                int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
            }
            if (TackAllowed(target.FactionId) && target.Kind == BuildingKind.Saddlery)
            {
                if (kind == ResourceKind.Food)
                {
                    int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
                }
                if (kind == ResourceKind.Wood)
                {
                    int put = Math.Min(amount, rules.BufferLimit - target.InputSecondary); target.InputSecondary += put; return put;
                }
            }
            if ((PlanksAllowed(target.FactionId) || CraftTreeCommonAllowed(target.FactionId)) && target.Kind == BuildingKind.Sawmill && kind == ResourceKind.Wood)
            {
                int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
            }
            if (StonecutterAllowed(target.FactionId) && target.Kind == BuildingKind.Stonecutter && kind == ResourceKind.Stone)
            {
                int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
            }
            if (JoineryAllowed(target.FactionId) && target.Kind == BuildingKind.Joinery)
            {
                if (kind == ResourceKind.Planks)
                {
                    int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
                }
                if (kind == ResourceKind.CutStone)
                {
                    int put = Math.Min(amount, rules.BufferLimit - target.InputSecondary); target.InputSecondary += put; return put;
                }
            }
            if (BricksAllowed(target.FactionId) && target.Kind == BuildingKind.BrickKiln)
            {
                if (kind == ResourceKind.Stone)
                {
                    int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
                }
                if (kind == ResourceKind.Charcoal)
                {
                    int put = Math.Min(amount, rules.BufferLimit - target.InputSecondary); target.InputSecondary += put; return put;
                }
            }
            if (BooksAllowed(target.FactionId) && target.Kind == BuildingKind.Scriptorium)
            {
                if (kind == ResourceKind.Gold)
                {
                    int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
                }
                if (kind == ResourceKind.Wood)
                {
                    int put = Math.Min(amount, rules.BufferLimit - target.InputSecondary); target.InputSecondary += put; return put;
                }
            }
            if (PreservesAllowed(target.FactionId) && target.Kind == BuildingKind.DryingRack && kind == ResourceKind.Food)
            {
                int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
            }
            if (JewelsAllowed(target.FactionId) && target.Kind == BuildingKind.Lapidary)
            {
                if (kind == ResourceKind.Stone)
                {
                    int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
                }
                if (kind == ResourceKind.Ore)
                {
                    int put = Math.Min(amount, rules.BufferLimit - target.InputSecondary); target.InputSecondary += put; return put;
                }
            }
            if (TradeGoodsAllowed(target.FactionId) && target.Kind == BuildingKind.Weaver && kind == ResourceKind.Wood)
            {
                int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
            }
            if (IncenseAllowed(target.FactionId) && target.Kind == BuildingKind.IncenseHouse)
            {
                if (kind == ResourceKind.Wood)
                {
                    int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
                }
                if (kind == ResourceKind.Food)
                {
                    int put = Math.Min(amount, rules.BufferLimit - target.InputSecondary); target.InputSecondary += put; return put;
                }
            }
            if (CharmsAllowed(target.FactionId) && target.Kind == BuildingKind.CharmWorkshop && kind == ResourceKind.Wood)
            {
                int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
            }
            if (RepairKitsAllowed(target.FactionId) && target.Kind == BuildingKind.RepairYard && kind == ResourceKind.Wood)
            {
                int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
            }
            if (ToolsAllowed(target.FactionId) && target.Kind == BuildingKind.ToolShop && kind == ResourceKind.Wood)
            {
                int put = Math.Min(amount, rules.BufferLimit - target.Input); target.Input += put; return put;
            }
            if (world.Config.Economy.BeltComponents && target.Kind == BuildingKind.Storage && StorageFeedsResource(target, target.FactionId, kind)
                && target.StorageCount < rules.StorageCapacity && (target.StorageCount == 0 || target.StorageKind == kind))
            {
                int put = Math.Min(amount, rules.StorageCapacity - target.StorageCount);
                target.StorageKind = kind; target.StorageCount += put; target.Input = target.StorageCount; target.Output = target.StorageCount; return put;
            }
            return 0;
        }

        private int PutIntoStorage(ref BuildingState target, ResourceKind kind, int amount)
        {
            var rules = world.Config.Economy;
            if (!rules.BeltComponents || !StorageFeedsResource(target, target.FactionId, kind) || !StorageCanAccept(target, kind)) return 0;
            int put = Math.Min(amount, rules.StorageCapacity - target.StorageCount);
            target.StorageKind = kind;
            target.StorageCount += put;
            target.Input = target.StorageCount;
            target.Output = target.StorageCount;
            return put;
        }

        private bool StorageCanAccept(BuildingState target, ResourceKind kind)
            => target.Kind == BuildingKind.Storage && IsRawResource(kind)
                && target.StorageCount < world.Config.Economy.StorageCapacity
                && (target.StorageCount == 0 || target.StorageKind == kind);

        private bool CanHaulTo(BuildingKind source, BuildingKind destination, ResourceKind kind)
            => (world.Config.Economy.BeltComponents && destination == BuildingKind.Storage
                && kind != 0)
               || (ProcessingOn && destination == BuildingKind.Steelworks
               && ((source == BuildingKind.Smelter && kind == ResourceKind.Metal)
                   || (source == BuildingKind.CharcoalKiln && kind == ResourceKind.Charcoal)))
               || (ForestryOn && destination == BuildingKind.Fletcher && source == BuildingKind.LumberCamp && kind == ResourceKind.Wood)
               || (RationsOn && destination == BuildingKind.Mill && source == BuildingKind.Farm && kind == ResourceKind.Food)
               || (TackOn && destination == BuildingKind.Saddlery && (kind == ResourceKind.Food || kind == ResourceKind.Wood))
               || ((PlanksOn || CraftTree1On) && destination == BuildingKind.Sawmill && source == BuildingKind.LumberCamp && kind == ResourceKind.Wood)
               || ((CraftTree1On || AiFactoryOn) && destination == BuildingKind.Stonecutter
                   && (source == BuildingKind.Quarry || source == BuildingKind.Drill) && kind == ResourceKind.Stone)
               || (CraftTree1On && destination == BuildingKind.Joinery && (source == BuildingKind.Sawmill && kind == ResourceKind.Planks || source == BuildingKind.Stonecutter && kind == ResourceKind.CutStone))
               || (BricksOn && destination == BuildingKind.BrickKiln && ((source == BuildingKind.Quarry && kind == ResourceKind.Stone)
                   || (source == BuildingKind.CharcoalKiln && kind == ResourceKind.Charcoal)))
               || (BooksOn && destination == BuildingKind.Scriptorium
                   && (kind == ResourceKind.Gold || kind == ResourceKind.Wood))
               || (PreservesOn && destination == BuildingKind.DryingRack && kind == ResourceKind.Food)
               || (JewelsOn && destination == BuildingKind.Lapidary && (kind == ResourceKind.Stone || kind == ResourceKind.Ore))
               || (TradeGoodsOn && destination == BuildingKind.Weaver && kind == ResourceKind.Wood)
               || (IncenseOn && destination == BuildingKind.IncenseHouse && (kind == ResourceKind.Wood || kind == ResourceKind.Food))
               || (CharmsOn && destination == BuildingKind.CharmWorkshop && kind == ResourceKind.Wood)
               || (RepairKitsOn && destination == BuildingKind.RepairYard && kind == ResourceKind.Wood)
               || (ToolsOn && destination == BuildingKind.ToolShop && source == BuildingKind.LumberCamp && kind == ResourceKind.Wood);
               

        /// <summary>The own finished smelter with the lowest id, or 0.</summary>
        private uint OwnSmelter(uint faction)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (IsActualBuilding(b) && b.Complete && b.FactionId == faction && b.Kind == BuildingKind.Smelter) return b.Id;
            }
            return 0;
        }

        /// <summary>Stops carrying by hand: a load in hand goes to the core first.</summary>
        private static void StopHauling(ref VillagerState v)
        {
            v.HaulFrom = 0; v.HaulTo = 0; v.HaulNodeId = 0;
            if (v.Task == VillagerTask.ToPickup || v.Task == VillagerTask.ToDeliver) v.Task = v.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.Idle;
        }

        /// <summary>Villagers on their way to (or working) the ore point a new mine covers stop and look for other work.</summary>
        private void ReleaseNode(uint nodeId)
        {
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var v = ref world.Villagers[i];
                if (!v.Alive || v.NodeId != nodeId || (v.Task != VillagerTask.ToNode && v.Task != VillagerTask.Gathering)) continue;
                v.NodeId = 0;
                v.Task = v.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.Idle;
            }
        }

    }
}
