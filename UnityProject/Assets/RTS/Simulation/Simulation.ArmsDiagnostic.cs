using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{

/// <summary>
/// armsdiag1 の観測専用結果。正規状態、乱数、判断条件には含めない。
/// </summary>
public sealed class ArmsDiagnosticObservation
{
    public long Tick;
    public ArmsDiagnosticFactionObservation[] Factions = Array.Empty<ArmsDiagnosticFactionObservation>();
}

public sealed class ArmsDiagnosticFactionObservation
{
    public uint FactionId;
    public long[] DecisionCalls = Array.Empty<long>();
    public long[] DecisionResults = Array.Empty<long>();
    public long[] DecisionReasons = Array.Empty<long>();
    public int Assemblers, Sawmills, Kilns;
    public int PlanksProduced, CharcoalProduced, PlanksDeliveredToAssemblers, CharcoalDeliveredToAssemblers;
    public int PlanksUsedBuilding, PlanksUsedBelt, PlanksUsedWeapons, PlanksUsedScholarly, PlanksUsedConstruction, PlanksUsedOther;
    public int ArmsProduced, ScholarlyProduced, FramesProduced, ArmsUsedBySoldiers;
    public long[] TrainingArmsUnavailableByAge = Array.Empty<long>();
    public long TrainingArmsUsed;
    public ArmsDiagnosticAssemblerObservation[] AssemblersDetail = Array.Empty<ArmsDiagnosticAssemblerObservation>();
}

public sealed class ArmsDiagnosticAssemblerObservation
{
    public uint Id;
    public bool Complete;
    public RecipeKind Recipe;
    public long[] RecipeTicks = Array.Empty<long>();
    public long[] Produced = Array.Empty<long>();
    public long[] MaterialEmptyTicks = Array.Empty<long>();
    public long OutputFullTicks;
}

public sealed partial class Simulation
{
    internal const int ArmsDiagDecisionCount = 5;
    internal const int ArmsDiagDecisionReasonCount = 13;
    internal const int ArmsDiagDecisionResultCount = 4;
    private const int ArmsDiagRecipeCount = 16;
    private const int ArmsDiagMaterialCount = 3;
    private const int ArmsDiagReasonNoSite = 0;
    private const int ArmsDiagReasonNoPort = 1;
    private const int ArmsDiagReasonPlaced = 2;
    private const int ArmsDiagReasonNoMoney = 3;
    private const int ArmsDiagReasonTarget = 4;
    private const int ArmsDiagReasonReservation = 5;
    private const int ArmsDiagReasonBarracks = 6;
    private const int ArmsDiagReasonNoPlanks = 7;
    private const int ArmsDiagReasonAgeOrStock = 8;
    private const int ArmsDiagReasonUnderConstruction = 9;
    private const int ArmsDiagReasonNoRoom = 10;
    private const int ArmsDiagReasonDisabled = 11;
    private const int ArmsDiagReasonOther = 12;

    private sealed class ArmsDiagnosticAssemblerState
    {
        internal readonly long[] RecipeTicks = new long[ArmsDiagRecipeCount];
        internal readonly long[] Produced = new long[ArmsDiagRecipeCount];
        internal readonly long[] MaterialEmptyTicks = new long[ArmsDiagMaterialCount];
        internal long OutputFullTicks;
    }

    private readonly long[,] armsDiagDecisionCalls = new long[2, ArmsDiagDecisionCount];
    private readonly long[,] armsDiagDecisionResults = new long[2, ArmsDiagDecisionResultCount];
    private readonly long[,] armsDiagDecisionReasons = new long[2, ArmsDiagDecisionReasonCount];
    private readonly int[] armsDiagPlanksDelivered = new int[2];
    private readonly int[] armsDiagCharcoalDelivered = new int[2];
    private readonly int[] armsDiagCharcoalProduced = new int[2];
    private readonly long[,] armsDiagTrainingUnavailableByAge = new long[2, 5];
    private readonly long[] armsDiagTrainingUsed = new long[2];
    private ArmsDiagnosticAssemblerState[] armsDiagAssemblers = Array.Empty<ArmsDiagnosticAssemblerState>();

    private static int ArmsDiagDecisionIndex(string source)
        => source switch
        {
            "craft-required" => 0,
            "arms-scale" => 1,
            "ai-factory-age2" => 2,
            "craft-tree2" => 3,
            _ => 4
        };

    private void ArmsDiagDecision(uint faction, string source)
    {
        if (faction < 1 || faction > 2) return;
        armsDiagDecisionCalls[faction - 1, ArmsDiagDecisionIndex(source)]++;
    }

    private void ArmsDiagDecisionReason(uint faction, int reason)
    {
        if (faction < 1 || faction > 2 || reason < 0 || reason >= ArmsDiagDecisionReasonCount) return;
        armsDiagDecisionReasons[faction - 1, reason]++;
    }

    private void ArmsDiagDecisionResult(uint faction, int result)
    {
        if (faction < 1 || faction > 2 || result < 0 || result >= ArmsDiagDecisionResultCount) return;
        armsDiagDecisionResults[faction - 1, result]++;
    }

    private void ArmsDiagAssemblerAttempt(uint faction, bool armsScale, bool armsFlow, RecipeKind forcedRecipe)
    {
        if (faction < 1 || faction > 2) return;
        // Result slots: generic/forced, arms-scale, arms-flow, forced lens/precision are deliberately fixed.
        int result = armsFlow ? 2 : armsScale ? 1 : forcedRecipe == RecipeKind.Lens || forcedRecipe == RecipeKind.Precision ? 3 : 0;
        ArmsDiagDecisionResult(faction, result);
    }

    private void ArmsDiagAssemblerPlacement(uint faction, bool placed, int reason)
    {
        if (faction < 1 || faction > 2) return;
        if (placed) ArmsDiagDecisionReason(faction, ArmsDiagReasonPlaced);
        else ArmsDiagDecisionReason(faction, reason);
    }

    private ArmsDiagnosticAssemblerState ArmsDiagAssemblerStateFor(uint id)
    {
        if (id == 0) return null;
        if (id > armsDiagAssemblers.Length)
        {
            int old = armsDiagAssemblers.Length;
            Array.Resize(ref armsDiagAssemblers, checked((int)id));
            for (int i = old; i < armsDiagAssemblers.Length; i++) armsDiagAssemblers[i] = new ArmsDiagnosticAssemblerState();
        }
        return armsDiagAssemblers[id - 1] ??= new ArmsDiagnosticAssemblerState();
    }

    private static int ArmsDiagSlot(ResourceKind kind)
        => kind == ResourceKind.Planks ? 0 : kind == ResourceKind.Charcoal ? 1 : 2;

    private static int ArmsDiagStock(FactionEconomy economy, ResourceKind kind)
        => kind switch
        {
            ResourceKind.Planks => economy.Planks,
            ResourceKind.Charcoal => economy.Charcoal,
            ResourceKind.Steel => economy.Steel,
            ResourceKind.Fittings => economy.Fittings,
            ResourceKind.CutStone => economy.CutStone,
            ResourceKind.Frames => economy.Frames,
            ResourceKind.ArmsKit => economy.ArmsKit,
            ResourceKind.Scholarly => economy.Scholarly,
            ResourceKind.Food => economy.Food,
            ResourceKind.Tack => economy.Tack,
            _ => 0
        };

    private static int ArmsDiagInput(BuildingState building, int slot)
        => slot == 0 ? building.Input : slot == 1 ? building.InputSecondary : building.InputTertiary;

    private void ArmsDiagMaterial(uint faction, BuildingState building, FactionEconomy economy, ResourceKind material, int required, int slot)
    {
        if (required <= 0 || ArmsDiagInput(building, slot) >= required) return;
        if (!CarriedInputsOn && ArmsDiagInput(building, slot) + ArmsDiagStock(economy, material) >= required) return;
        var state = ArmsDiagAssemblerStateFor(building.Id);
        if (state != null && material is ResourceKind.Planks or ResourceKind.Charcoal)
            state.MaterialEmptyTicks[ArmsDiagSlot(material)]++;
    }

    private void ArmsDiagObserveAssembler(ref BuildingState building, ref FactionEconomy economy)
    {
        if (building.FactionId < 1 || building.FactionId > 2 || building.Recipe == RecipeKind.None) return;
        var state = ArmsDiagAssemblerStateFor(building.Id);
        if (state == null) return;
        int recipe = (int)building.Recipe;
        if (recipe >= 0 && recipe < state.RecipeTicks.Length) state.RecipeTicks[recipe]++;
        if (building.Timer != 0) return;
        if (building.Output >= world.Config.Economy.BufferLimit) state.OutputFullTicks++;
        var rules = world.Config.Economy;
        switch (building.Recipe)
        {
            case RecipeKind.ArmsKit:
                ArmsDiagMaterial(building.FactionId, building, economy, ResourceKind.Planks, CraftInputFor(building, rules.ArmsKitPlankInput), 0);
                ResourceKind secondary = building.InputSecondaryKind == ResourceKind.Steel ? ResourceKind.Steel
                    : building.InputSecondaryKind == ResourceKind.Fittings ? ResourceKind.Fittings : ResourceKind.Charcoal;
                int secondaryInput = secondary == ResourceKind.Steel ? CraftInputFor(building, rules.ArmsKitSteelInput)
                    : secondary == ResourceKind.Fittings ? 1 : CraftInputFor(building, rules.ArmsKitCharcoalInput);
                ArmsDiagMaterial(building.FactionId, building, economy, secondary, secondaryInput, 1);
                ArmsDiagMaterial(building.FactionId, building, economy,
                    CivGoodsUsageOn(building.FactionId) ? ResourceKind.Tack : secondary == ResourceKind.Fittings ? ResourceKind.Charcoal : 0,
                    CivGoodsUsageOn(building.FactionId) || secondary == ResourceKind.Fittings ? 1 : 0, 2);
                break;
            case RecipeKind.Scholarly:
                ArmsDiagMaterial(building.FactionId, building, economy, ResourceKind.Planks, CraftInputFor(building, rules.ScholarlyPlankInput), 0);
                ArmsDiagMaterial(building.FactionId, building, economy, ResourceKind.Charcoal, CraftInputFor(building, rules.ScholarlyCharcoalInput), 1);
                ArmsDiagMaterial(building.FactionId, building, economy, ResourceKind.Food, CraftInputFor(building, rules.ScholarlyFoodInput), 2);
                break;
            case RecipeKind.Frames:
                ArmsDiagMaterial(building.FactionId, building, economy, ResourceKind.Planks, CraftInputFor(building, rules.CraftTreeJoineryPlankInput), 0);
                break;
        }
    }

    private void ArmsDiagProduced(BuildingState building)
    {
        var state = ArmsDiagAssemblerStateFor(building.Id);
        int recipe = (int)building.Recipe;
        if (state != null && recipe >= 0 && recipe < state.Produced.Length) state.Produced[recipe]++;
    }

    private void ArmsDiagDeliveredToAssembler(BuildingState building, ResourceKind kind, int amount)
    {
        if (amount <= 0 || building.Kind != BuildingKind.Assembler || building.FactionId < 1 || building.FactionId > 2) return;
        if (kind == ResourceKind.Planks) armsDiagPlanksDelivered[building.FactionId - 1] = checked(armsDiagPlanksDelivered[building.FactionId - 1] + amount);
        else if (kind == ResourceKind.Charcoal) armsDiagCharcoalDelivered[building.FactionId - 1] = checked(armsDiagCharcoalDelivered[building.FactionId - 1] + amount);
    }

    private void ArmsDiagKilnProduced(uint faction)
    {
        if (faction >= 1 && faction <= 2) armsDiagCharcoalProduced[faction - 1]++;
    }

    private void ArmsDiagTrainingUnavailable(uint faction)
    {
        if (faction < 1 || faction > 2) return;
        int age = world.Economies[faction - 1].Age;
        if (age >= 0 && age < 5) armsDiagTrainingUnavailableByAge[faction - 1, age]++;
    }

    private void ArmsDiagTrainingUsed(uint faction)
    {
        if (faction >= 1 && faction <= 2) armsDiagTrainingUsed[faction - 1]++;
    }

    public ArmsDiagnosticObservation CaptureArmsDiagnostic()
    {
        var result = new ArmsDiagnosticObservation { Tick = world.Tick, Factions = new ArmsDiagnosticFactionObservation[2] };
        for (uint faction = 1; faction <= 2; faction++)
        {
            var e = world.Economies[faction - 1];
            var assemblers = new List<ArmsDiagnosticAssemblerObservation>();
            int count = 0, sawmills = 0, kilns = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!IsActualBuilding(b) || b.FactionId != faction) continue;
                if (b.Kind == BuildingKind.Assembler)
                {
                    count++;
                    var state = b.Id <= armsDiagAssemblers.Length ? armsDiagAssemblers[b.Id - 1] : null;
                    assemblers.Add(new ArmsDiagnosticAssemblerObservation
                    {
                        Id = b.Id, Complete = b.Complete, Recipe = b.Recipe,
                        RecipeTicks = state?.RecipeTicks?.Clone() as long[] ?? new long[ArmsDiagRecipeCount],
                        Produced = state?.Produced?.Clone() as long[] ?? new long[ArmsDiagRecipeCount],
                        MaterialEmptyTicks = state?.MaterialEmptyTicks?.Clone() as long[] ?? new long[ArmsDiagMaterialCount],
                        OutputFullTicks = state?.OutputFullTicks ?? 0
                    });
                }
                else if (b.Kind == BuildingKind.Sawmill) sawmills++;
                else if (b.Kind == BuildingKind.CharcoalKiln) kilns++;
            }
            var factionResult = new ArmsDiagnosticFactionObservation
            {
                FactionId = faction, DecisionCalls = new long[ArmsDiagDecisionCount], DecisionResults = new long[ArmsDiagDecisionResultCount],
                DecisionReasons = new long[ArmsDiagDecisionReasonCount], Assemblers = count, Sawmills = sawmills, Kilns = kilns,
                 PlanksProduced = e.PlanksProduced, CharcoalProduced = armsDiagCharcoalProduced[faction - 1],
                 PlanksDeliveredToAssemblers = armsDiagPlanksDelivered[faction - 1], CharcoalDeliveredToAssemblers = armsDiagCharcoalDelivered[faction - 1],
                 PlanksUsedBuilding = craftTracePlankUses[faction - 1, CraftTraceBuilding],
                 PlanksUsedBelt = craftTracePlankUses[faction - 1, CraftTraceBelt],
                 PlanksUsedWeapons = craftTracePlankUses[faction - 1, CraftTraceWeapons],
                 PlanksUsedScholarly = craftTracePlankUses[faction - 1, CraftTraceScholarly],
                 PlanksUsedConstruction = craftTracePlankUses[faction - 1, CraftTraceConstruction],
                 PlanksUsedOther = checked(craftTracePlankUses[faction - 1, CraftTraceOther] + craftTracePlankUses[faction - 1, CraftTraceBridges]),
                ArmsProduced = e.ArmsKitProduced + e.FineArmsKitProduced, ScholarlyProduced = e.ScholarlyProduced,
                FramesProduced = e.FramesProduced, ArmsUsedBySoldiers = e.ArmsKitConsumed + e.FineArmsKitConsumed,
                TrainingArmsUnavailableByAge = new long[5], TrainingArmsUsed = armsDiagTrainingUsed[faction - 1], AssemblersDetail = assemblers.ToArray()
            };
            for (int i = 0; i < ArmsDiagDecisionCount; i++) factionResult.DecisionCalls[i] = armsDiagDecisionCalls[faction - 1, i];
            for (int i = 0; i < ArmsDiagDecisionResultCount; i++) factionResult.DecisionResults[i] = armsDiagDecisionResults[faction - 1, i];
            for (int i = 0; i < ArmsDiagDecisionReasonCount; i++) factionResult.DecisionReasons[i] = armsDiagDecisionReasons[faction - 1, i];
            for (int i = 0; i < 5; i++) factionResult.TrainingArmsUnavailableByAge[i] = armsDiagTrainingUnavailableByAge[faction - 1, i];
            result.Factions[faction - 1] = factionResult;
        }
        return result;
    }
}
}
