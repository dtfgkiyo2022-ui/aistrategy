using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// CraftFlow is deliberately kept outside WorldState. It observes production transitions and rotates two reusable
    /// one-minute buffers; none of these counters are read by simulation decisions or diagnostic serialization.
    /// </summary>
    public sealed partial class Simulation
    {
        // Sized from the enum so resources added later (e.g. CraftDepth fittings and rope) fit.
        private static readonly int CraftFlowResourceSlots = MaxResourceKind() + 1;

        private static int MaxResourceKind()
        {
            int max = 0;
            foreach (ResourceKind kind in Enum.GetValues(typeof(ResourceKind))) max = Math.Max(max, (int)kind);
            return max;
        }
        private const int CraftFlowMinuteTicks = 20 * 60;

        private sealed class CraftFlowBuildingState
        {
            internal int CurrentProduced, LastProduced;
            internal int CurrentStarved, LastStarved;
            internal int CurrentBlocked, LastBlocked;
            internal readonly int[] CurrentInputs = new int[CraftFlowResourceSlots];
            internal readonly int[] LastInputs = new int[CraftFlowResourceSlots];
        }

        private sealed class CraftFlowFactionState
        {
            internal readonly int[] CurrentProduced = new int[CraftFlowResourceSlots];
            internal readonly int[] CurrentConsumed = new int[CraftFlowResourceSlots];
            internal readonly int[] LastProduced = new int[CraftFlowResourceSlots];
            internal readonly int[] LastConsumed = new int[CraftFlowResourceSlots];
        }

        private CraftFlowBuildingState[] craftFlowBuildings = Array.Empty<CraftFlowBuildingState>();
        private readonly CraftFlowFactionState[] craftFlowFactions = { new CraftFlowFactionState(), new CraftFlowFactionState() };

        private bool CraftFlowOn => world.Config.Economy.Enabled && world.Config.Economy.Industry && world.Config.Economy.CraftFlow;

        private void EnsureCraftFlowCapacity()
        {
            if (!CraftFlowOn || craftFlowBuildings.Length >= world.BuildingCount) return;
            int oldLength = craftFlowBuildings.Length;
            Array.Resize(ref craftFlowBuildings, world.BuildingCount);
            for (int i = oldLength; i < craftFlowBuildings.Length; i++) craftFlowBuildings[i] = new CraftFlowBuildingState();
        }

        private void BeginCraftFlowObservation()
        {
            if (CraftFlowOn) EnsureCraftFlowCapacity();
        }

        private void ObserveCraftFlowBuilding(ref BuildingState building, int timerBefore)
        {
            if (!CraftFlowOn || !CraftFlowRecipe(building, out ResourceKind output, out ResourceKind input1, out int amount1,
                out ResourceKind input2, out int amount2, out ResourceKind input3, out int amount3, out int baseTicks, out int outputAmount)) return;
            if (building.Id == 0 || building.Id > craftFlowBuildings.Length) return;
            var state = craftFlowBuildings[building.Id - 1];
            if (timerBefore == 0 && building.Timer > 0)
            {
                RecordCraftFlowConsumed(building.FactionId, state, input1, amount1);
                RecordCraftFlowConsumed(building.FactionId, state, input2, amount2);
                RecordCraftFlowConsumed(building.FactionId, state, input3, amount3);
            }
            else if (timerBefore > 0 && building.Timer == 0)
            {
                state.CurrentProduced = checked(state.CurrentProduced + outputAmount);
                craftFlowFactions[building.FactionId - 1].CurrentProduced[(int)output] =
                    checked(craftFlowFactions[building.FactionId - 1].CurrentProduced[(int)output] + outputAmount);
            }
            else if (timerBefore == 0 && building.Timer == 0)
            {
                if (CraftFlowOutputBlocked(building, output)) state.CurrentBlocked++;
                else if (!CraftFlowHasInputs(building, input1, amount1, input2, amount2, input3, amount3)) state.CurrentStarved++;
            }
        }

        private void RecordCraftFlowConsumed(uint faction, CraftFlowBuildingState building, ResourceKind kind, int amount)
        {
            if (kind == 0 || amount <= 0) return;
            building.CurrentInputs[(int)kind] = checked(building.CurrentInputs[(int)kind] + amount);
            craftFlowFactions[faction - 1].CurrentConsumed[(int)kind] =
                checked(craftFlowFactions[faction - 1].CurrentConsumed[(int)kind] + amount);
        }

        private void EndCraftFlowTick()
        {
            if (!CraftFlowOn || world.Tick % CraftFlowMinuteTicks != 0) return;
            EnsureCraftFlowCapacity();
            for (int i = 0; i < craftFlowBuildings.Length; i++)
            {
                var state = craftFlowBuildings[i];
                state.LastProduced = state.CurrentProduced; state.CurrentProduced = 0;
                state.LastStarved = state.CurrentStarved; state.CurrentStarved = 0;
                state.LastBlocked = state.CurrentBlocked; state.CurrentBlocked = 0;
                Array.Copy(state.CurrentInputs, state.LastInputs, CraftFlowResourceSlots);
                Array.Clear(state.CurrentInputs, 0, CraftFlowResourceSlots);
            }
            for (int faction = 0; faction < craftFlowFactions.Length; faction++)
            {
                var state = craftFlowFactions[faction];
                Array.Copy(state.CurrentProduced, state.LastProduced, CraftFlowResourceSlots);
                Array.Copy(state.CurrentConsumed, state.LastConsumed, CraftFlowResourceSlots);
                Array.Clear(state.CurrentProduced, 0, CraftFlowResourceSlots);
                Array.Clear(state.CurrentConsumed, 0, CraftFlowResourceSlots);
            }
        }

        private bool CraftFlowRecipe(BuildingState building, out ResourceKind output, out ResourceKind input1, out int amount1,
            out ResourceKind input2, out int amount2, out ResourceKind input3, out int amount3, out int baseTicks, out int outputAmount)
        {
            output = input1 = input2 = input3 = 0; amount1 = amount2 = amount3 = baseTicks = outputAmount = 0;
            uint faction = building.FactionId;
            var rules = world.Config.Economy;
            switch (building.Kind)
            {
                case BuildingKind.Smelter: if (!IndustryOn) return false; output = ResourceKind.Metal; input1 = ResourceKind.Ore; amount1 = rules.OrePerMetal; baseTicks = rules.SmeltTicks; break;
                case BuildingKind.CharcoalKiln: if (!CharcoalKilnAllowed(faction)) return false; output = ResourceKind.Charcoal; input1 = ResourceKind.Wood; amount1 = CraftTreeCommonAllowed(faction) ? rules.CraftTreeCharcoalWoodInput : 2; baseTicks = CraftTreeCommonAllowed(faction) ? rules.CraftTreeCharcoalTicks : rules.CharcoalTicks; break;
                case BuildingKind.Steelworks: if (!ProcessingOn) return false; output = ResourceKind.Steel; input1 = ResourceKind.Metal; amount1 = 1; input2 = ResourceKind.Charcoal; amount2 = 1; baseTicks = rules.SteelTicks; break;
                case BuildingKind.Fletcher: if (!ForestryOn) return false; output = ResourceKind.BowGear; input1 = ResourceKind.Wood; amount1 = rules.FletcherWoodInput; input2 = ResourceKind.Food; amount2 = rules.FletcherFoodInput; baseTicks = rules.FletcherTicks; break;
                case BuildingKind.Mill: if (!RationsOn) return false; output = ResourceKind.Rations; input1 = ResourceKind.Food; amount1 = rules.MillFoodInput; baseTicks = rules.MillTicks; break;
                case BuildingKind.BrickKiln: if (!BricksAllowed(faction)) return false; output = ResourceKind.Bricks; input1 = ResourceKind.Stone; amount1 = rules.BrickStoneInput; input2 = ResourceKind.Charcoal; amount2 = rules.BrickCharcoalInput; baseTicks = rules.BrickTicks; break;
                case BuildingKind.Saddlery: if (!TackAllowed(faction)) return false; output = ResourceKind.Tack; input1 = ResourceKind.Food; amount1 = rules.TackFoodInput; input2 = ResourceKind.Wood; amount2 = rules.TackWoodInput; baseTicks = rules.TackTicks; break;
                case BuildingKind.Sawmill: if (!(PlanksAllowed(faction) || CraftTreeCommonAllowed(faction))) return false; output = ResourceKind.Planks; input1 = ResourceKind.Wood; amount1 = CraftTreeCommonAllowed(faction) ? rules.CraftTreePlankWoodInput : rules.PlankWoodInput; baseTicks = CraftTreeCommonAllowed(faction) ? rules.CraftTreePlankTicks : rules.PlankTicks; break;
                case BuildingKind.Scriptorium: if (!BooksAllowed(faction)) return false; output = ResourceKind.Books; input1 = ResourceKind.Gold; amount1 = rules.BookGoldInput; input2 = ResourceKind.Wood; amount2 = rules.BookWoodInput; baseTicks = rules.BookTicks; break;
                case BuildingKind.DryingRack: if (!PreservesAllowed(faction)) return false; output = ResourceKind.Preserves; input1 = ResourceKind.Food; amount1 = rules.PreserveFoodInput; baseTicks = rules.PreserveTicks; break;
                case BuildingKind.Lapidary: if (!JewelsAllowed(faction)) return false; output = ResourceKind.Gems; input1 = ResourceKind.Stone; amount1 = rules.JewelStoneInput; input2 = ResourceKind.Ore; amount2 = rules.JewelOreInput; baseTicks = rules.JewelTicks; break;
                case BuildingKind.Weaver: if (!TradeGoodsAllowed(faction)) return false; output = ResourceKind.TradeGoods; input1 = ResourceKind.Wood; amount1 = rules.TradeGoodsWoodInput; baseTicks = rules.TradeGoodsTicks; break;
                case BuildingKind.IncenseHouse: if (!IncenseAllowed(faction)) return false; output = ResourceKind.Incense; input1 = ResourceKind.Wood; amount1 = rules.IncenseWoodInput; input2 = ResourceKind.Food; amount2 = rules.IncenseFoodInput; baseTicks = rules.IncenseTicks; break;
                case BuildingKind.CharmWorkshop: if (!CharmsAllowed(faction)) return false; output = ResourceKind.Charms; input1 = ResourceKind.Wood; amount1 = rules.CharmWoodInput; baseTicks = rules.CharmTicks; break;
                case BuildingKind.RepairYard: if (!RepairKitsAllowed(faction)) return false; output = ResourceKind.RepairKits; input1 = ResourceKind.Wood; amount1 = rules.RepairKitWoodInput; baseTicks = rules.RepairKitTicks; break;
                case BuildingKind.ToolShop: if (!ToolsAllowed(faction)) return false; output = ResourceKind.Tools; input1 = ResourceKind.Wood; amount1 = rules.ToolWoodInput; baseTicks = rules.ToolTicks; break;
                case BuildingKind.Stonecutter: if (!StonecutterAllowed(faction)) return false; output = ResourceKind.CutStone; input1 = ResourceKind.Stone; amount1 = rules.CraftTreeStoneInput; baseTicks = rules.CraftTreeStonecutterTicks; break;
                case BuildingKind.Joinery: if (!JoineryAllowed(faction)) return false; output = ResourceKind.Frames; input1 = ResourceKind.Planks; amount1 = rules.CraftTreeJoineryPlankInput; input2 = ResourceKind.CutStone; amount2 = rules.CraftTreeJoineryCutStoneInput; baseTicks = rules.CraftTreeJoineryTicks; break;
                case BuildingKind.Hut:
                    if (!PrimitiveCraftOn || building.Recipe == RecipeKind.None) return false;
                    if (building.Recipe == RecipeKind.StoneTools) { output = ResourceKind.StoneTools; input1 = ResourceKind.Stone; amount1 = rules.StoneToolsStoneInput; input2 = ResourceKind.Wood; amount2 = rules.StoneToolsWoodInput; }
                    else if (building.Recipe == RecipeKind.Jerky) { output = ResourceKind.Jerky; input1 = ResourceKind.Food; amount1 = rules.JerkyFoodInput; outputAmount = rules.JerkyOutput; }
                    else return false;
                    baseTicks = rules.PrimitiveCraftTicks; break;
                case BuildingKind.Assembler:
                    if (!AssemblerAllowed(faction)) return false;
                    outputAmount = 1;
                    switch (building.Recipe)
                    {
                        case RecipeKind.Frames: output = ResourceKind.Frames; input1 = ResourceKind.Planks; amount1 = rules.CraftTreeJoineryPlankInput; input2 = ResourceKind.CutStone; amount2 = rules.CraftTreeJoineryCutStoneInput; baseTicks = rules.CraftTreeJoineryTicks; break;
                        case RecipeKind.ArmsKit: output = building.InputSecondaryKind == ResourceKind.Steel ? ResourceKind.FineArmsKit : ResourceKind.ArmsKit; input1 = ResourceKind.Planks; amount1 = rules.ArmsKitPlankInput; input2 = building.InputSecondaryKind == ResourceKind.Steel ? ResourceKind.Steel : ResourceKind.Charcoal; amount2 = building.InputSecondaryKind == ResourceKind.Steel ? rules.ArmsKitSteelInput : rules.ArmsKitCharcoalInput; baseTicks = rules.ArmsKitTicks; break;
                        case RecipeKind.Scholarly: output = ResourceKind.Scholarly; input1 = ResourceKind.Planks; amount1 = rules.ScholarlyPlankInput; input2 = ResourceKind.Charcoal; amount2 = rules.ScholarlyCharcoalInput; input3 = ResourceKind.Food; amount3 = rules.ScholarlyFoodInput; baseTicks = rules.ScholarlyTicks; break;
                        case RecipeKind.SiegeParts: output = ResourceKind.SiegeParts; input1 = ResourceKind.Frames; amount1 = rules.SiegePartsFramesInput; input2 = ResourceKind.ArmsKit; amount2 = rules.SiegePartsArmsKitInput; baseTicks = rules.SiegePartsTicks; break;
                        case RecipeKind.Mechanism: output = ResourceKind.Mechanism; input1 = ResourceKind.ArmsKit; amount1 = rules.MechanismArmsKitInput; input2 = ResourceKind.Scholarly; amount2 = rules.MechanismScholarlyInput; input3 = ResourceKind.CutStone; amount3 = rules.MechanismCutStoneInput; baseTicks = rules.MechanismTicks; break;
                        case RecipeKind.SiegeEngine: output = ResourceKind.SiegeEngine; input1 = ResourceKind.SiegeParts; amount1 = rules.SiegeEngineSiegePartsInput; input2 = ResourceKind.Mechanism; amount2 = rules.SiegeEngineMechanismInput; baseTicks = rules.SiegeEngineTicks; break;
                        case RecipeKind.EliteKit: output = ResourceKind.EliteKit; input1 = ResourceKind.ArmsKit; amount1 = rules.EliteKitArmsKitInput; input2 = ResourceKind.Mechanism; amount2 = rules.EliteKitMechanismInput; input3 = ResourceKind.Metal; amount3 = rules.EliteKitMetalInput; baseTicks = rules.EliteKitTicks; break;
                        default: return false;
                    }
                    break;
                default: return false;
            }
            amount1 = CraftInputFor(building, amount1); amount2 = CraftInputFor(building, amount2); amount3 = CraftInputFor(building, amount3);
            if (outputAmount == 0) outputAmount = 1;
            return output != 0 && baseTicks > 0;
        }

        private bool CraftFlowHasInputs(BuildingState b, ResourceKind i1, int a1, ResourceKind i2, int a2, ResourceKind i3, int a3)
            => (i1 == 0 || b.Input >= a1) && (i2 == 0 || b.InputSecondary >= a2) && (i3 == 0 || b.InputTertiary >= a3);

        private bool CraftFlowOutputBlocked(BuildingState b, ResourceKind output)
        {
            if (b.Output >= world.Config.Economy.BufferLimit) return true;
            var e = world.Economies[b.FactionId - 1];
            int limit = output == ResourceKind.Incense ? world.Config.Economy.IncenseStockLimit
                : output == ResourceKind.Charms ? world.Config.Economy.CharmStockLimit
                : output == ResourceKind.RepairKits ? world.Config.Economy.RepairKitStockLimit
                : output == ResourceKind.Tools ? world.Config.Economy.ToolStockLimit
                : output == ResourceKind.SiegeParts ? world.Config.Economy.SiegePartsStockLimit : 0;
            int stock = output == ResourceKind.Incense ? e.Incense : output == ResourceKind.Charms ? e.Charms
                : output == ResourceKind.RepairKits ? e.RepairKits : output == ResourceKind.Tools ? e.Tools
                : output == ResourceKind.SiegeParts ? e.SiegeParts : 0;
            return limit > 0 && stock >= limit;
        }

        private int CraftFlowFullRate(BuildingState b)
        {
            if (!CraftFlowRecipe(b, out _, out _, out _, out _, out _, out _, out _, out int ticks, out int amount)) return 0;
            ticks = CraftTicksFor(b, ticks);
            return ticks <= 0 ? 0 : checked((CraftFlowMinuteTicks / ticks) * amount);
        }

        private void CraftFlowData(BuildingState b, out int produced, out int starved, out int blocked, out IReadOnlyList<CraftFlowInputView> inputs, out int fullRate)
        {
            produced = starved = blocked = fullRate = 0;
            inputs = Array.Empty<CraftFlowInputView>();
            if (!CraftFlowOn || b.Id == 0 || b.Id > craftFlowBuildings.Length) return;
            var values = new List<CraftFlowInputView>();
            inputs = values;
            var state = craftFlowBuildings[b.Id - 1];
            produced = state.LastProduced; starved = state.LastStarved; blocked = state.LastBlocked; fullRate = CraftFlowFullRate(b);
            for (int i = 1; i < CraftFlowResourceSlots; i++)
                if (state.LastInputs[i] != 0) values.Add(new CraftFlowInputView((ResourceKind)i, state.LastInputs[i]));
        }

        private IReadOnlyList<ProductionStatView> CraftFlowStatistics(uint faction)
        {
            if (!CraftFlowOn) return Array.Empty<ProductionStatView>();
            var values = new List<ProductionStatView>();
            var state = craftFlowFactions[faction - 1];
            for (int i = 1; i < CraftFlowResourceSlots; i++)
                if (IsCraftFlowProduct((ResourceKind)i) && (state.LastProduced[i] != 0 || state.LastConsumed[i] != 0))
                    values.Add(new ProductionStatView((ResourceKind)i, state.LastProduced[i], state.LastConsumed[i]));
            return values;
        }

        private static bool IsCraftFlowProduct(ResourceKind kind)
            => kind >= ResourceKind.Metal && kind <= ResourceKind.Jerky;
    }
}
