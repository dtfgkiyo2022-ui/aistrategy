using System;
using System.Collections.Generic;
using System.Numerics;
using Rts.Contracts;
using Rts.Decision;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-2 automatic economy for industry (technical-design-v3 13): one fixed line, mine -> smelter -> core. A mine on the
    /// ore point nearest the core, a smelter between them, then belts on the shortest open route between the ports; until
    /// the line is whole, two villagers carry by hand. Every choice is a fixed-order search over the true own state, the
    /// public resource points and the terrain, so the same state always gives the same line.
    /// </summary>
    public sealed partial class Simulation
    {
        private const int Haulers = 2;
        // V3-11 #2/#4 provisional policy: one cell is two metres on the default map, so the bridge must save 4 m.
        private const int BridgeShorteningThresholdCells = 2;
        // The choice score is deliberately capped and tiered so path length never enters the resource-point score
        // with its original magnitude.  0/1/2/3 correspond to <4 m, 4-15 m, 16-31 m and 32 m or more on the default map.
        private const int BridgeScoreTier2Cells = 8, BridgeScoreTier3Cells = 16;
        private const int BridgeDangerMeters = 16;
        private const long BridgeRebuildWaitTicks = 200;
        private RouteDistance.Workspace bridgeRouteWorkspace;
        private RouteDistance.Workspace bridgeRouteWorkspace2 = new RouteDistance.Workspace();
        private bool[] bridgeBasePassable;
        private int bridgeBasePassableVersion = -1;
        private readonly Dictionary<int, BridgeDistanceMap> bridgeDistanceMaps = new Dictionary<int, BridgeDistanceMap>();
        private int bridgeDistanceMapVersion = -1;
        private bool bridgeDistanceCacheEnabled = true;
        private const int BridgeDistanceMapCacheLimit = 64;

        internal long BridgeSearchCalls { get; private set; }
        internal long BridgeCandidateStartCellsInspected { get; private set; }
        internal long BridgeCandidatesEvaluated { get; private set; }
        internal long BridgeRouteEvaluations { get; private set; }
        internal long BridgeDistanceMapCells { get; private set; }
        internal long BridgeDistanceMapsBuilt { get; private set; }
        internal long BridgeDistanceMapCacheHits { get; private set; }
        internal long BridgeDistanceMapRequests { get; private set; }
        internal long BridgeLegacyDistanceMapCells { get; private set; }
        internal long BridgeBasePassableBuilds { get; private set; }
        internal long BridgeBasePassableCellsScanned { get; private set; }
        internal long BridgeSourceCellsConsidered { get; private set; }
        internal long BridgeDestinationCellsConsidered { get; private set; }

        private sealed class BridgeCandidate
        {
            internal int[] Cells;
            internal Facing Facing;
            internal int WorkCell;
        }

        private sealed class BridgeDistanceMap
        {
            internal int[] Distances;
            internal int VisitedCells;
        }
        // A requested-region value cannot legally be uint.MaxValue. It is used only after a shared steel route
        // was tried and rejected, so the same line falls back to the dedicated mine/smelter path deterministically.
        private const uint SharedMetalFallbackMarker = uint.MaxValue;
        private static readonly Facing[] Sides = { Facing.North, Facing.East, Facing.South, Facing.West };
        private static readonly TechKind[] CraftTree3Researches =
        {
            TechKind.Siegecraft, TechKind.Masonry, TechKind.Banking, TechKind.GemArmor,
            BridgeTech.SiegeDeployment, MountainTech.MountainFort, FishingTech.DriedFish,
            CultTech.MartyrBlessing, TollgateTech.GateNetwork, MetropolisTech.CitizenMilitia,
            SanctuaryTech.HolyRelic
        };

        /// <summary>AI phase, after the barracks and infantry (13 steps 1-4).</summary>
        private void DecideIndustry(uint faction)
        {
            if (LineBuildingCheckOn)
                MeasureSubphase("AI.DecideEconomy.DecideIndustry.ValidateLineBuildingReferences", ValidateLineBuildingReferences, faction);
            if (BuildingReservationsOn)
                MeasureSubphase("AI.DecideEconomy.DecideIndustry.BindReservedLineBuildings", BindReservedLineBuildingsForFaction, faction);
            MeasureSubphase("AI.DecideEconomy.DecideIndustry.CraftTree1", DecideCraftTree1, faction);
            MeasureSubphase("AI.DecideEconomy.DecideIndustry.CraftTree2", DecideCraftTree2, faction);
            MeasureSubphase("AI.DecideEconomy.DecideIndustry.CraftTree3", DecideCraftTree3, faction);
            MeasureSubphase("AI.DecideEconomy.DecideIndustry.CraftTree4", DecideCraftTree4, faction);
            if (MeasureSubphase("AI.DecideEconomy.DecideIndustry.ArmyRecovery", TryDecideArmyRecoveryMetalLine, faction)) return;
            if (EarlyArmsOn && world.Economies[faction - 1].Age >= 1 && EarlyFoodFarmNeeded(faction))
            {
                MeasureSubphase("AI.DecideEconomy.DecideIndustry.EarlyArms.Farms", DecideFarms, faction);
                if (CavalryAllowed(faction)) MeasureSubphase("AI.DecideEconomy.DecideIndustry.EarlyArms.Tack", DecideTack, faction);
                if (IncenseAllowed(faction)) MeasureSubphase("AI.DecideEconomy.DecideIndustry.EarlyArms.Incense", DecideIncense, faction);
                if (CharmsAllowed(faction)) MeasureSubphase("AI.DecideEconomy.DecideIndustry.EarlyArms.Charms", DecideCharms, faction);
                if (RepairKitsAllowed(faction)) MeasureSubphase("AI.DecideEconomy.DecideIndustry.EarlyArms.RepairKits", DecideRepairKits, faction);
                if (ToolsAllowed(faction)) MeasureSubphase("AI.DecideEconomy.DecideIndustry.EarlyArms.Tools", DecideTools, faction);
                return;
            }
            if (MeasureSubphase("AI.DecideEconomy.DecideIndustry.RequestedLine", TryDecideRequestedLine, faction))
            {
                if (ToolsAllowed(faction)) MeasureSubphase("AI.DecideEconomy.DecideIndustry.RequestedLine.Tools", DecideTools, faction);
                return;
            }
            // No return: the caravan's own decisions below stay exactly as they were.
            if (TradeGoodsAllowed(faction)) MeasureSubphase("AI.DecideEconomy.DecideIndustry.TradeGoods", DecideTradeGoods, faction);
            if (IncenseAllowed(faction)) MeasureSubphase("AI.DecideEconomy.DecideIndustry.Incense", DecideIncense, faction);
            if (CharmsAllowed(faction)) MeasureSubphase("AI.DecideEconomy.DecideIndustry.Charms", DecideCharms, faction);
            if (RepairKitsAllowed(faction)) MeasureSubphase("AI.DecideEconomy.DecideIndustry.RepairKits", DecideRepairKits, faction);
            if (FarmingAllowed(faction))
            {
                MeasureSubphase("AI.DecideEconomy.DecideIndustry.Farms", DecideFarms, faction);
                if (RationsAllowed(faction)) MeasureSubphase("AI.DecideEconomy.DecideIndustry.Rations", DecideRations, faction);
                return;
            }
            if (FishingAllowed(faction))
            {
                MeasureSubphase("AI.DecideEconomy.DecideIndustry.FishingHarbor", DecideFishingHarbor, faction);
                MeasureSubphase("AI.DecideEconomy.DecideIndustry.Preserves", DecidePreserves, faction);
                return;
            }
            if (ForestryAllowed(faction))
            {
                if (world.Economies[faction - 1].Age < 2)
                {
                    MeasureSubphase("AI.DecideEconomy.DecideIndustry.LumberCamp", DecideLumberCamp, faction);
                    return;
                }
                MeasureSubphase("AI.DecideEconomy.DecideIndustry.ForestryLines", DecideForestryLines, faction);
                return;
            }
            if (MasonryAllowed(faction))
            {
                MeasureSubphase("AI.DecideEconomy.DecideIndustry.Quarry", DecideQuarry, faction);
                if (BricksAllowed(faction)) MeasureSubphase("AI.DecideEconomy.DecideIndustry.Bricks", DecideBricks, faction);
                return;
            }
            if (CavalryAllowed(faction))
            {
                MeasureSubphase("AI.DecideEconomy.DecideIndustry.Tack", DecideTack, faction);
                return;
            }
            if (MountainAllowed(faction))
            {
                MeasureSubphase("AI.DecideEconomy.DecideIndustry.Mountain", DecideMountain, faction);
                MeasureSubphase("AI.DecideEconomy.DecideIndustry.Jewels", DecideJewels, faction);
                return;
            }
            if (BridgeAllowed(faction))
            {
                MeasureSubphase("AI.DecideEconomy.DecideIndustry.EngineerCamp", DecideEngineerCamp, faction);
                MeasureSubphase("AI.DecideEconomy.DecideIndustry.Planks", DecidePlanks, faction);
                return;
            }
            if (ToolsAllowed(faction)) MeasureSubphase("AI.DecideEconomy.DecideIndustry.Tools", DecideTools, faction);
            if (!IndustryOn || !MetalworkAllowed(faction)) return;
            // ProcessingChain is an opt-in extension. Keep the V3-2/V3-5 core-line decision path byte-for-byte
            // equivalent on every map that does not carry the new flag.
            if (!ProcessingOn)
            {
                MeasureSubphase("AI.DecideEconomy.DecideIndustry.LegacyIndustry", DecideLegacyIndustry, faction);
                return;
            }
            MeasureSubphase("AI.DecideEconomy.DecideIndustry.Metalwork", DecideMetalwork, faction);
        }

        private void DecideMetalwork(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            int coreIndex = GetOrCreateLine(faction, ProcessingLineKind.CoreMetal);
            ref var coreLine = ref world.ProcessingLines[coreIndex];
            if (!IsAutoLine(coreIndex)) return;
            if (LineRebuildBlocked(coreIndex)) { StopLineHaulers(coreIndex); return; }
            if (!BuildingReady(coreLine.MineId))
            {
                if (BuildingPending(coreLine.MineId)) return;
                if (economy.Wood < world.Config.Economy.MineWoodCost) return;
                uint id = PlaceMine(faction);
                if (id != 0) SetLineBuilding(coreIndex, BuildingKind.Mine, id);
                return;
            }
            if (!BuildingReady(coreLine.SmelterId))
            {
                if (BuildingPending(coreLine.SmelterId)) return;
                if (economy.Wood < world.Config.Economy.SmelterWoodCost) return;
                uint id = PlaceSmelter(faction, world.Buildings[coreLine.MineId - 1]);
                if (id != 0) SetLineBuilding(coreIndex, BuildingKind.Smelter, id);
                return;
            }
            var coreMine = world.Buildings[coreLine.MineId - 1];
            var coreSmelter = world.Buildings[coreLine.SmelterId - 1];
            int existingSteelIndex = LineIndex(faction, ProcessingLineKind.Steel);
            bool preserveSharedCoreRoute = IsSharedMetalLine(coreIndex, existingSteelIndex);
            if (!preserveSharedCoreRoute && !LayCoreLine(faction, coreIndex, coreMine, coreSmelter)) SetLineHaulers(faction, coreLine, Haulers);

            if (!ProcessingAvailable(faction) || !LineBeltsWhole(coreIndex) || !LineBuildingsReady(coreLine)) return;
            int steelIndex = GetOrCreateLine(faction, ProcessingLineKind.Steel);
            ref var steelLine = ref world.ProcessingLines[steelIndex];
            if (!IsAutoLine(steelIndex)) return;
            if (LineRebuildBlocked(steelIndex)) { StopLineHaulers(steelIndex); return; }
            TryBindSharedMetalLine(coreIndex, steelIndex);
            // One AI cycle advances exactly one stage of the steel line. A lack of wood leaves that stage pending.
            if (!BuildingReady(steelLine.MineId))
            {
                if (BuildingPending(steelLine.MineId)) return;
                if (economy.Wood < world.Config.Economy.MineWoodCost) return;
                uint id = PlaceMine(faction);
                if (id != 0) SetLineBuilding(steelIndex, BuildingKind.Mine, id);
                return;
            }
            if (!BuildingReady(steelLine.SmelterId))
            {
                if (BuildingPending(steelLine.SmelterId)) return;
                if (economy.Wood < world.Config.Economy.SmelterWoodCost) return;
                uint id = PlaceSmelter(faction, world.Buildings[steelLine.MineId - 1]);
                if (id != 0) SetLineBuilding(steelIndex, BuildingKind.Smelter, id);
                return;
            }
            if (!BuildingReady(steelLine.KilnId))
            {
                if (BuildingPending(steelLine.KilnId)) return;
                if (economy.Wood < world.Config.Economy.CharcoalKilnWoodCost) return;
                uint id = PlaceKiln(faction);
                if (id != 0) SetLineBuilding(steelIndex, BuildingKind.CharcoalKiln, id);
                return;
            }
            if (!BuildingReady(steelLine.SteelworksId))
            {
                if (BuildingPending(steelLine.SteelworksId)) return;
                if (economy.Wood < world.Config.Economy.SteelworksWoodCost) return;
                uint id = PlaceSteelworks(faction, steelLine);
                if (id != 0) SetLineBuilding(steelIndex, BuildingKind.Steelworks, id);
                return;
            }
            var steelMine = world.Buildings[steelLine.MineId - 1];
            var steelSmelter = world.Buildings[steelLine.SmelterId - 1];
            var kiln = world.Buildings[steelLine.KilnId - 1];
            var steelworks = world.Buildings[steelLine.SteelworksId - 1];
            LaySteelLine(faction, steelIndex, steelMine, steelSmelter, kiln, steelworks);
            // Unlike the core line, this branch always needs its two wood carriers: the kiln has no automatic
            // resource input, even after all four belt routes are complete.
            SetLineHaulers(faction, steelLine, Haulers);
        }

        private void BindReservedLineBuildingsForFaction(uint faction)
        {
            for (int i = 0; i < world.ProcessingLines.Length; i++)
                if (world.ProcessingLines[i].FactionId == faction && IsAutoLine(i))
                {
                    ref var pendingLine = ref world.ProcessingLines[i];
                    BindReservedLineBuildings(faction, ref pendingLine);
                }
        }

        /// <summary>AiArmyRecovery: restore the metal line before optional industry while infantry is below its reserve.</summary>
        private bool TryDecideArmyRecoveryMetalLine(uint faction)
        {
            if (!ArmyRecoveryNeedsInfantry(faction) || InfantryMetalFor(faction) <= 0 || !ProcessingAvailable(faction)) return false;
            ref var economy = ref world.Economies[faction - 1];
            int lineIndex = GetOrCreateLine(faction, ProcessingLineKind.CoreMetal);
            ref var line = ref world.ProcessingLines[lineIndex];
            if (!IsAutoLine(lineIndex)) return true;
            if (LineRebuildBlocked(lineIndex)) { StopLineHaulers(lineIndex); return true; }
            if (line.MineId != 0 && !RecoveryBuildingReady(line.MineId, BuildingKind.Mine))
            {
                StopLineHaulers(lineIndex);
                line.MineId = 0;
                line.SmelterId = 0;
                line.BeltCells = Array.Empty<int>();
                line.BeltFacings = Array.Empty<Facing>();
            }
            else if (RecoveryMineDepleted(line.MineId))
            {
                RetireRecoveryMine(lineIndex);
                line.MineId = 0;
                line.SmelterId = 0;
                line.BeltCells = Array.Empty<int>();
                line.BeltFacings = Array.Empty<Facing>();
            }
            if (!RecoveryBuildingReady(line.MineId, BuildingKind.Mine))
            {
                if (BuildingPending(line.MineId))
                {
                    SetRecoveryWorkCell(faction, line.MineId);
                    PrioritizeRecoveryBuilding(faction, line.MineId);
                    return true;
                }
                if (economy.Wood < world.Config.Economy.MineWoodCost) return true;
                uint id = PlaceMine(faction);
                if (id != 0) { SetLineBuilding(lineIndex, BuildingKind.Mine, id); SetRecoveryWorkCell(faction, id); }
                return true;
            }
            if (!RecoveryBuildingReady(line.SmelterId, BuildingKind.Smelter))
            {
                if (BuildingPending(line.SmelterId))
                {
                    SetRecoveryWorkCell(faction, line.SmelterId);
                    PrioritizeRecoveryBuilding(faction, line.SmelterId);
                    return true;
                }
                if (economy.Wood < world.Config.Economy.SmelterWoodCost) return true;
                uint id = PlaceSmelter(faction, world.Buildings[line.MineId - 1]);
                if (id != 0) { SetLineBuilding(lineIndex, BuildingKind.Smelter, id); SetRecoveryWorkCell(faction, id); }
                return true;
            }
            var mine = world.Buildings[line.MineId - 1];
            var smelter = world.Buildings[line.SmelterId - 1];
            if (!LayCoreLine(faction, lineIndex, mine, smelter)) SetLineHaulers(faction, line, Haulers);
            else SetLineHaulers(faction, line, 0);
            return true;
        }

        private bool RecoveryBuildingReady(uint id, BuildingKind expected)
            => id != 0 && id <= world.BuildingCount && world.Buildings[id - 1].Alive
                && world.Buildings[id - 1].Complete && world.Buildings[id - 1].Kind == expected;

        private bool RecoveryMineDepleted(uint id)
            => id != 0 && id <= world.BuildingCount && world.Buildings[id - 1].Kind == BuildingKind.Mine
                && world.Buildings[id - 1].NodeId != 0 && world.Nodes[world.Buildings[id - 1].NodeId - 1].Remaining <= 0;

        private void RetireRecoveryMine(int lineIndex)
        {
            ref var line = ref world.ProcessingLines[lineIndex];
            if (line.MineId == 0 || line.MineId > world.BuildingCount) return;
            ref var mine = ref world.Buildings[line.MineId - 1];
            if (!mine.Alive) return;
            mine.Alive = false;
            RemoveRegionCenter(mine);
            foreach (int cell in Footprint(mine)) world.Map.SetPassable(cell, true);
            TerrainChanged();
        }

        private void PrioritizeRecoveryBuilding(uint faction, uint buildingId)
        {
            if (buildingId == 0 || buildingId > world.BuildingCount) return;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var v = ref world.Villagers[i];
                if (!v.Alive || v.FactionId != faction || v.Held
                    || (v.Task != VillagerTask.ToBuild && v.Task != VillagerTask.Building)
                    || v.BuildingId == buildingId) continue;
                v.Task = VillagerTask.Idle;
                v.BuildingId = 0;
                v.NodeId = 0;
                v.HaulFrom = 0;
                v.HaulTo = 0;
            }
            ref var target = ref world.Buildings[buildingId - 1];
            AssignBuilders(ref target);
        }

        private void SetRecoveryWorkCell(uint faction, uint buildingId)
        {
            if (buildingId == 0 || buildingId > world.BuildingCount) return;
            ref var building = ref world.Buildings[buildingId - 1];
            int reachable = NearestReachablePassableCell(FootprintCenter(building.OriginCell, SizeOf(building.Kind)), faction);
            if (reachable >= 0) building.WorkCell = reachable;
        }

        private void DecideCraftTree2(uint faction)
        {
            if (!AssemblerAllowed(faction)) return;
            ref var economy = ref world.Economies[faction - 1];
            var rules = world.Config.Economy;
            int lineIndex = GetOrCreateLine(faction, ProcessingLineKind.Assembler);
            ref var line = ref world.ProcessingLines[lineIndex];
            int existing = OwnBuildingIndex(faction, BuildingKind.Assembler);
            if (existing >= 0)
            {
                ref var current = ref world.Buildings[existing];
                line.AssemblerId = current.Id;
                if (IsActualBuilding(current) && current.Complete && current.Recipe == RecipeKind.None)
                    current.Recipe = PreferredAssemblerRecipe(faction);
            }
            else
            {
                if (economy.Wood < rules.AssemblerWoodCost) return;
                uint id = PlaceAssembler(faction);
                if (id == 0) return;
                line.AssemblerId = id;
                return;
            }

            if (!BuildingReady(line.AssemblerId)) return;
            ref var assembler = ref world.Buildings[line.AssemblerId - 1];
            if (assembler.Recipe == RecipeKind.None) return;

            // CraftTree1 makes charcoal legal for every civilisation, but the old automatic
            // economy only built a kiln for the metallurgy/masonry chains.  The assembler owns
            // one shared kiln line so its charcoal can reach the core stock.
            if (!BuildingReady(line.KilnId))
            {
                if (BuildingPending(line.KilnId)) return;
                if (economy.Wood < rules.CharcoalKilnWoodCost) return;
                uint id = PlaceKiln(faction, assembler.OriginCell);
                if (id != 0) line.KilnId = id;
                return;
            }
            var kiln = world.Buildings[line.KilnId - 1];
            var route = BeltRoute(faction, OutputCell(kiln), TakenForOtherLines(faction, lineIndex), next => FeedsOwnCore(next, faction));
            bool whole = false;
            if (route.cells != null)
            {
                SetLineBelts(lineIndex, route);
                whole = LayRoutes(faction, route);
            }
            SetLineHaulers(faction, line, whole ? 0 : 1);
        }

        private void DecideCraftTree3(uint faction)
        {
            if (!CraftTree3Allowed(faction)) return;
            ref var economy = ref world.Economies[faction - 1];
            int assemblerIndex = OwnBuildingIndex(faction, BuildingKind.Assembler);
            if (assemblerIndex < 0) return;
            ref var assembler = ref world.Buildings[assemblerIndex];
            if (!IsActualBuilding(assembler) || !assembler.Complete) return;
            RecipeKind wanted = PreferredCraftTree3Recipe(faction);
            if (assembler.Recipe != wanted && assembler.Timer == 0)
                SetAssemblerRecipe(faction, assembler.Id, wanted);
            if (economy.Mechanism < 1) return;
            for (uint buildingId = 1; buildingId < world.NextBuildingId; buildingId++)
            {
                if (!OwnBuilding(faction, buildingId, out int buildingIndex)) continue;
                var building = world.Buildings[buildingIndex];
                if (!IsActualBuilding(building) || !building.Complete || !IsCraftProcessingBuilding(building.Kind) || building.Level != 2) continue;
                UpgradeProcessingBuilding(faction, buildingId);
                return;
            }
        }

        private void DecideCraftTree4(uint faction)
        {
            if (!CraftTree4Allowed(faction)) return;
            int assemblerIndex = OwnBuildingIndex(faction, BuildingKind.Assembler);
            if (assemblerIndex < 0) return;
            ref var assembler = ref world.Buildings[assemblerIndex];
            if (!IsActualBuilding(assembler) || !assembler.Complete || assembler.Timer != 0) return;
            RecipeKind wanted = PreferredCraftTree4Recipe(faction);
            if (wanted != RecipeKind.None && assembler.Recipe != wanted)
                SetAssemblerRecipe(faction, assembler.Id, wanted);
        }

        private RecipeKind PreferredCraftTree4Recipe(uint faction)
        {
            var e = world.Economies[faction - 1];
            var r = world.Config.Economy;
            if (e.SiegeParts >= r.SiegeEngineSiegePartsInput && e.Mechanism >= r.SiegeEngineMechanismInput)
                return RecipeKind.SiegeEngine;
            if (e.ArmsKit >= r.EliteKitArmsKitInput && e.Mechanism >= r.EliteKitMechanismInput && e.Metal >= r.EliteKitMetalInput)
                return RecipeKind.EliteKit;
            if (e.SiegeParts > 0 || e.Mechanism > 0) return RecipeKind.SiegeEngine;
            if (e.ArmsKit > 0) return RecipeKind.EliteKit;
            return RecipeKind.SiegeEngine;
        }

        private RecipeKind PreferredAssemblerRecipe(uint faction)
            => PreferredCraftTree3Recipe(faction);

        private RecipeKind PreferredCraftTree3Recipe(uint faction)
        {
            var economy = world.Economies[faction - 1];
            bool research = CraftTree3Allowed(faction) && ShouldResearchWithAssembler(faction);
            if (research)
            {
                // Mechanism needs Scholarly as an input.  With one assembler, make the intermediate
                // first, then switch to Mechanism on the next allocation cycle.
                RecipeKind researchRecipe = economy.Scholarly < 1 ? RecipeKind.Scholarly : RecipeKind.Mechanism;
                if (RecipeInputsAvailable(economy, researchRecipe)) return researchRecipe;
                // A research recipe can be blocked by a missing charcoal/plank/stone line.  Keep
                // the single assembler useful when the siege recipe has fewer missing inputs.
                if (OwnBuildingIndex(faction, BuildingKind.SiegeWorkshop) >= 0
                    && economy.SiegeParts < world.Config.Economy.SiegePartsStockLimit
                    && RecipeInputsAvailable(economy, RecipeKind.SiegeParts)) return RecipeKind.SiegeParts;
                return researchRecipe;
            }

            bool hasSiegeWorkshop = OwnBuildingIndex(faction, BuildingKind.SiegeWorkshop) >= 0;
            if (CraftTree3Allowed(faction) && hasSiegeWorkshop
                && economy.SiegeParts < world.Config.Economy.SiegePartsStockLimit)
                return RecipeKind.SiegeParts;
            return RecipeKind.ArmsKit;
        }

        private bool RecipeInputsAvailable(FactionEconomy economy, RecipeKind recipe)
        {
            var rules = world.Config.Economy;
            if (recipe == RecipeKind.Scholarly)
                return economy.Planks >= rules.ScholarlyPlankInput && economy.Charcoal >= rules.ScholarlyCharcoalInput
                    && economy.Food >= rules.ScholarlyFoodInput;
            if (recipe == RecipeKind.Mechanism)
                return economy.ArmsKit >= rules.MechanismArmsKitInput && economy.Scholarly >= rules.MechanismScholarlyInput
                    && economy.CutStone >= rules.MechanismCutStoneInput;
            if (recipe == RecipeKind.SiegeParts)
                return economy.Frames >= rules.SiegePartsFramesInput && economy.ArmsKit >= rules.SiegePartsArmsKitInput;
            if (recipe == RecipeKind.SiegeEngine)
                return economy.SiegeParts >= rules.SiegeEngineSiegePartsInput && economy.Mechanism >= rules.SiegeEngineMechanismInput;
            if (recipe == RecipeKind.EliteKit)
                return economy.ArmsKit >= rules.EliteKitArmsKitInput && economy.Mechanism >= rules.EliteKitMechanismInput && economy.Metal >= rules.EliteKitMetalInput;
            return false;
        }

        private bool ShouldResearchWithAssembler(uint faction)
        {
            if (!CraftTree3Allowed(faction)) return false;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (IsActualBuilding(b) && b.Complete && b.FactionId == faction
                    && b.Researching != 0 && IsThirdAgeResearch(b.Researching)) return true;
            }
            // A finished research building with an open third-age research is a real demand even
            // when the raw-resource budget has not started it yet.  That lets the assembler make
            // the discount item before StartResearch is retried on the next allocation cycle.
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!IsActualBuilding(b) || !b.Complete || b.FactionId != faction || b.Researching != 0) continue;
                for (int r = 0; r < CraftTree3Researches.Length; r++)
                    if (ResearchBuildingAccepts(faction, b.Kind, CraftTree3Researches[r])
                        && TechOpen(faction, CraftTree3Researches[r]) && !BeingResearched(faction, CraftTree3Researches[r])) return true;
            }
            return false;
        }

        private bool ResearchBuildingAccepts(uint faction, BuildingKind building, TechKind tech)
        {
            if (building == BuildingKind.Academy) return AcademyTechOpen(faction, tech);
            if (building == BuildingKind.MineShaft) return IsMountainTech(tech);
            if (building == BuildingKind.Harbor) return IsFishingTech(tech);
            if (building == BuildingKind.Monastery) return IsCultTech(tech);
            if (building == BuildingKind.Tollgate) return IsTollgateTech(tech);
            if (building == BuildingKind.GrandHouse) return IsMetropolisTech(tech);
            if (building == BuildingKind.Shrine) return IsSanctuaryTech(tech);
            return building == BuildingKind.Blacksmith && !IsMountainTech(tech) && !IsTollgateTech(tech)
                && !IsMetropolisTech(tech) && !IsSanctuaryTech(tech);
        }

        private void DecideCraftTree1(uint faction)
        {
            if (!CraftTreeCommonAllowed(faction)) return;
            int houseIndex = OwnBuildingIndex(faction, BuildingKind.House);
            if (!CompleteBarracks(faction) || houseIndex < 0 || !world.Buildings[houseIndex].Complete) return;
            ref var economy = ref world.Economies[faction - 1];
            var rules = world.Config.Economy;
            int plankIndex = GetOrCreateLine(faction, ProcessingLineKind.Planks);
            ref var plankLine = ref world.ProcessingLines[plankIndex];
            if (IsAutoLine(plankIndex) && !BuildingReady(plankLine.SawmillId))
            {
                if (!BuildingPending(plankLine.SawmillId) && economy.Wood >= rules.SawmillWoodCost + rules.CraftTreePlankWoodInput * 2)
                {
                    uint id = PlaceCommonSawmill(faction);
                    if (id != 0) plankLine.SawmillId = id;
                }
            }
            else if (IsAutoLine(plankIndex) && BuildingReady(plankLine.SawmillId))
            {
                SetLineHaulers(faction, plankLine, 0);
                var sawmill = world.Buildings[plankLine.SawmillId - 1];
                var route = BeltRoute(faction, OutputCell(sawmill), TakenForOtherLines(faction, plankIndex), next => FeedsOwnCore(next, faction));
                if (route.cells != null) { SetLineBelts(plankIndex, route); LayRoutes(faction, route); }
            }

            int stoneIndex = GetOrCreateLine(faction, ProcessingLineKind.CutStone);
            ref var stoneLine = ref world.ProcessingLines[stoneIndex];
            if (IsAutoLine(stoneIndex) && !BuildingReady(stoneLine.StonecutterId))
            {
                if (!BuildingPending(stoneLine.StonecutterId) && economy.Wood >= rules.StonecutterWoodCost)
                {
                    uint id = PlaceStonecutter(faction);
                    if (id != 0) stoneLine.StonecutterId = id;
                }
            }
            else if (IsAutoLine(stoneIndex) && BuildingReady(stoneLine.StonecutterId))
            {
                SetLineHaulers(faction, stoneLine, 0);
                var stonecutter = world.Buildings[stoneLine.StonecutterId - 1];
                var route = BeltRoute(faction, OutputCell(stonecutter), TakenForOtherLines(faction, stoneIndex), next => FeedsOwnCore(next, faction));
                if (route.cells != null) { SetLineBelts(stoneIndex, route); LayRoutes(faction, route); }
            }

            if (!JoineryAllowed(faction)) return;
            int frameIndex = GetOrCreateLine(faction, ProcessingLineKind.Frames);
            ref var frameLine = ref world.ProcessingLines[frameIndex];
            if (!BuildingReady(frameLine.JoineryId))
            {
                if (!BuildingPending(frameLine.JoineryId) && economy.Wood >= rules.JoineryWoodCost)
                {
                    uint id = PlaceJoinery(faction);
                    if (id != 0) frameLine.JoineryId = id;
                }
                return;
            }
            var joinery = world.Buildings[frameLine.JoineryId - 1];
            var frameRoute = BeltRoute(faction, OutputCell(joinery), TakenForOtherLines(faction, frameIndex), next => FeedsOwnCore(next, faction));
            if (frameRoute.cells != null) { SetLineBelts(frameIndex, frameRoute); LayRoutes(faction, frameRoute); }
            if (economy.Frames >= rules.CraftTreeLevel2FramesCost)
                for (uint buildingId = 1; buildingId < world.NextBuildingId; buildingId++)
                {
                    if (!OwnBuilding(faction, buildingId, out int buildingIndex)) continue;
                    var building = world.Buildings[buildingIndex];
                    if (!IsActualBuilding(building) || !building.Complete || !IsCraftProcessingBuilding(building.Kind) || building.Level >= 2) continue;
                    UpgradeProcessingBuilding(faction, buildingId);
                    break;
                }
        }

        private uint LineBuildingIdAfterPlacement(uint before, BuildingKind expected)
        {
            uint id = world.NextBuildingId - 1;
            if (!LineBuildingCheckOn) return id;
            if (world.NextBuildingId == before || id == 0 || id > world.BuildingCount) return 0;
            var building = world.Buildings[id - 1];
            return building.Id == id && building.Alive && building.Kind == expected ? id : 0;
        }

        private uint PlaceCommonSawmill(uint faction)
        {
            int size = world.Config.Economy.SawmillSizeCells;
            int origin = FindProcessingSite(faction, world.Map.Cell(OwnCore(faction).Definition.Position), size);
            if (origin < 0) return 0;
            var core = OwnCore(faction).Definition.Position;
            foreach (var side in SidesToward(FootprintCenter(origin, size), core))
                if (PortIsOpen(OutputCell(origin, size, side), faction))
                {
                    uint before = world.NextBuildingId;
                    PlaceBuildingAt(faction, BuildingKind.Sawmill, origin, side, 0);
                    return LineBuildingIdAfterPlacement(before, BuildingKind.Sawmill);
                }
            return 0;
        }

        private uint PlaceStonecutter(uint faction)
        {
            int size = world.Config.Economy.StonecutterSizeCells;
            int origin = FindProcessingSite(faction, world.Map.Cell(OwnCore(faction).Definition.Position), size);
            if (origin < 0) return 0;
            var core = OwnCore(faction).Definition.Position;
            foreach (var side in SidesToward(FootprintCenter(origin, size), core))
                if (PortIsOpen(OutputCell(origin, size, side), faction))
                {
                    uint before = world.NextBuildingId;
                    PlaceBuildingAt(faction, BuildingKind.Stonecutter, origin, side, 0);
                    return LineBuildingIdAfterPlacement(before, BuildingKind.Stonecutter);
                }
            return 0;
        }

        private uint PlaceJoinery(uint faction)
        {
            int size = world.Config.Economy.JoinerySizeCells;
            int origin = FindProcessingSite(faction, world.Map.Cell(OwnCore(faction).Definition.Position), size);
            if (origin < 0) return 0;
            var core = OwnCore(faction).Definition.Position;
            foreach (var side in SidesToward(FootprintCenter(origin, size), core))
                if (PortIsOpen(OutputCell(origin, size, side), faction))
                {
                    uint before = world.NextBuildingId;
                    PlaceBuildingAt(faction, BuildingKind.Joinery, origin, side, 0);
                    return LineBuildingIdAfterPlacement(before, BuildingKind.Joinery);
                }
            return 0;
        }

        private uint PlaceAssembler(uint faction)
        {
            int size = world.Config.Economy.AssemblerSizeCells;
            int origin = FindProcessingSite(faction, world.Map.Cell(OwnCore(faction).Definition.Position), size);
            if (origin < 0) return 0;
            var core = OwnCore(faction).Definition.Position;
            foreach (var side in SidesToward(FootprintCenter(origin, size), core))
                if (PortIsOpen(OutputCell(origin, size, side), faction))
                {
                    uint before = world.NextBuildingId;
                    PlaceBuildingAt(faction, BuildingKind.Assembler, origin, side, 0);
                    return LineBuildingIdAfterPlacement(before, BuildingKind.Assembler);
                }
            return 0;
        }

        private bool EarlyFoodFarmNeeded(uint faction)
        {
            var economy = world.Economies[faction - 1];
            int farms = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (IsPlannedBuilding(world.Buildings[i]) && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == BuildingKind.Farm) farms++;
            int target = EconomyDecision.FoodSourceTarget(EconomyScaleOn, FarmTarget) + 1;
            int ageFood = AgeClockOn ? world.Config.Economy.Age2FoodCost : world.Config.Economy.EarlyAge2FoodCost;
            return farms < target && economy.Food < ageFood + VillagerFoodCostFor(faction);
        }

        private void DecideFishingHarbor(uint faction)
        {
            var rules = world.Config.Economy;
            if (OwnBuildingIndex(faction, BuildingKind.Harbor) >= 0 || world.Economies[faction - 1].Wood < rules.HarborWoodCost) return;
            int origin = FindHarborSite(faction);
            if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.Harbor, origin, Facing.North, 0);
        }

        private bool FishingIndustryPrerequisites(uint faction)
        {
            if (!CompleteBarracks(faction)) return false;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (IsActualBuilding(b) && b.Complete && b.FactionId == faction
                    && (b.Kind == BuildingKind.House || b.Kind == BuildingKind.GrandHouse)) return true;
            }
            return false;
        }

        private void DecidePreserves(uint faction)
        {
            if (!PreservesAllowed(faction) || !FishingIndustryPrerequisites(faction)) return;
            ref var economy = ref world.Economies[faction - 1];
            var rules = world.Config.Economy;
            int racks = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (IsPlannedBuilding(world.Buildings[i]) && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == BuildingKind.DryingRack) racks++;
            if (racks >= rules.PreserveMaxRacks || economy.Wood < rules.DryingRackWoodCost + rules.PreserveWoodReserve) return;
            int index = GetOrCreateLine(faction, ProcessingLineKind.Preserves);
            ref var line = ref world.ProcessingLines[index];
            if (!IsAutoLine(index) || LineRebuildBlocked(index)) { StopLineHaulers(index); return; }
            if (!BuildingReady(line.DryingRackId))
            {
                if (BuildingPending(line.DryingRackId)) return;
                uint id = PlaceDryingRack(faction);
                if (id != 0) line.DryingRackId = id;
                return;
            }
            var rack = world.Buildings[line.DryingRackId - 1];
            bool whole = LayPreservesLine(faction, index, rack);
            SetLineHaulers(faction, line, whole ? 0 : 1);
        }

        private void DecideTradeGoods(uint faction)
        {
            if (!TradeGoodsAllowed(faction) || !FishingIndustryPrerequisites(faction)) return;
            ref var economy = ref world.Economies[faction - 1];
            var rules = world.Config.Economy;
            int weavers = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (IsPlannedBuilding(world.Buildings[i]) && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == BuildingKind.Weaver) weavers++;
            int index = GetOrCreateLine(faction, ProcessingLineKind.TradeGoods);
            ref var line = ref world.ProcessingLines[index];
            if (!IsAutoLine(index) || LineRebuildBlocked(index)) { StopLineHaulers(index); return; }
            if (!BuildingReady(line.WeaverId))
            {
                if (BuildingPending(line.WeaverId)) return;
                if (weavers >= rules.TradeGoodsMaxWeavers || economy.Wood < rules.WeaverWoodCost + rules.TradeGoodsWoodReserve) return;
                uint id = PlaceWeaver(faction);
                if (id != 0) line.WeaverId = id;
                return;
            }
            var weaver = world.Buildings[line.WeaverId - 1];
            bool whole = LayTradeGoodsLine(faction, index, weaver);
            SetLineHaulers(faction, line, whole ? 0 : 1);
        }

        private uint PlaceWeaver(uint faction, int preferredCell = -1)
        {
            var core = OwnCore(faction).Definition.Position;
            int centre = preferredCell >= 0 ? preferredCell : world.Map.Cell(core);
            int size = world.Config.Economy.WeaverSizeCells;
            int origin = FindProcessingSite(faction, centre, size);
            if (origin < 0) origin = FindProcessingSite(faction, world.Map.Cell(core), size);
            if (origin < 0) return 0;
            foreach (var side in SidesToward(FootprintCenter(origin, size), core))
            {
                if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                uint before = world.NextBuildingId;
                PlaceBuildingAt(faction, BuildingKind.Weaver, origin, side, 0);
                return LineBuildingIdAfterPlacement(before, BuildingKind.Weaver);
            }
            return 0;
        }

        private bool LayTradeGoodsLine(uint faction, int lineIndex, BuildingState weaver)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var route = BeltRoute(faction, OutputCell(weaver), taken, next => FeedsOwnCore(next, faction));
            if (route.cells == null) return false;
            SetLineBelts(lineIndex, route);
            return LayRoutes(faction, route);
        }

        private void DecideIncense(uint faction)
        {
            if (!IncenseAllowed(faction) || !CompleteBarracks(faction)) return;
            bool hasHouse = false;
            int houses = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!IsActualBuilding(b) || b.FactionId != faction || b.Kind != BuildingKind.House && b.Kind != BuildingKind.GrandHouse) continue;
                if (b.Complete) hasHouse = true;
            }
            if (!hasHouse) return;
            for (int i = 0; i < world.BuildingCount; i++)
                if (IsPlannedBuilding(world.Buildings[i]) && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == BuildingKind.IncenseHouse) houses++;
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            int index = GetOrCreateLine(faction, ProcessingLineKind.Incense);
            ref var line = ref world.ProcessingLines[index];
            if (!IsAutoLine(index) || LineRebuildBlocked(index)) { StopLineHaulers(index); return; }
            if (!BuildingReady(line.IncenseHouseId))
            {
                if (BuildingPending(line.IncenseHouseId)) return;
                if (houses >= rules.IncenseMaxHouses || economy.Wood < rules.IncenseWoodCost + rules.IncenseWoodReserve) return;
                uint id = PlaceIncenseHouse(faction);
                if (id != 0) line.IncenseHouseId = id;
                return;
            }
            var house = world.Buildings[line.IncenseHouseId - 1];
            bool whole = LayIncenseLine(faction, index, house);
            SetLineHaulers(faction, line, whole ? 0 : 1);
        }

        private void DecideCharms(uint faction)
        {
            if (!CharmsAllowed(faction) || !FishingIndustryPrerequisites(faction)) return;
            int workshops = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (IsPlannedBuilding(world.Buildings[i]) && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == BuildingKind.CharmWorkshop) workshops++;
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            int index = GetOrCreateLine(faction, ProcessingLineKind.Charms);
            ref var line = ref world.ProcessingLines[index];
            if (!IsAutoLine(index) || LineRebuildBlocked(index)) { StopLineHaulers(index); return; }
            if (!BuildingReady(line.CharmWorkshopId))
            {
                if (BuildingPending(line.CharmWorkshopId)) return;
                if (workshops >= rules.CharmMaxWorkshops || economy.Wood < rules.CharmWoodCost + rules.CharmWoodReserve) return;
                uint id = PlaceCharmWorkshop(faction);
                if (id != 0) line.CharmWorkshopId = id;
                return;
            }
            var workshop = world.Buildings[line.CharmWorkshopId - 1];
            bool whole = LayCharmsLine(faction, index, workshop);
            SetLineHaulers(faction, line, whole ? 0 : 1);
        }

        private void DecideTools(uint faction)
        {
            if (!ToolsAllowed(faction) || !CompleteBarracks(faction)) return;
            bool hasHouse = false;
            int shops = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive || (b.Reserved && !BuildingReservationsOn) || b.FactionId != faction) continue;
                if (b.Complete && (b.Kind == BuildingKind.House || b.Kind == BuildingKind.GrandHouse)) hasHouse = true;
                if (b.Kind == BuildingKind.ToolShop) shops++;
            }
            if (!hasHouse) return;
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            int index = GetOrCreateLine(faction, ProcessingLineKind.Tools);
            ref var line = ref world.ProcessingLines[index];
            BindReservedLineBuildings(faction, ref line);
            if (!IsAutoLine(index) || LineRebuildBlocked(index)) { StopLineHaulers(index); return; }
            if (!BuildingReady(line.ToolShopId))
            {
                if (BuildingPending(line.ToolShopId)) return;
                int availableWood = economy.Wood - (BuildingReservationsOn ? ReservedConstructionWood(faction) : 0);
                if (shops >= rules.ToolMaxShops || availableWood < rules.ToolShopWoodCost + rules.ToolWoodReserve) return;
                uint id = PlaceToolShop(faction);
                if (id != 0) line.ToolShopId = id;
                return;
            }
            var workshop = world.Buildings[line.ToolShopId - 1];
            bool whole = LayToolsLine(faction, index, workshop);
            SetLineHaulers(faction, line, whole ? 0 : 1);
        }

        private uint PlaceToolShop(uint faction, int preferredCell = -1)
        {
            var core = OwnCore(faction).Definition.Position;
            int centre = preferredCell >= 0 ? preferredCell : world.Map.Cell(core);
            int size = world.Config.Economy.ToolShopSizeCells;
            int origin = FindProcessingSite(faction, centre, size);
            if (origin < 0) origin = FindProcessingSite(faction, world.Map.Cell(core), size);
            if (origin < 0) return 0;
            foreach (var side in SidesToward(FootprintCenter(origin, size), core))
            {
                if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                uint before = world.NextBuildingId;
                PlaceBuildingAt(faction, BuildingKind.ToolShop, origin, side, 0);
                return LineBuildingIdAfterPlacement(before, BuildingKind.ToolShop);
            }
            return 0;
        }

        private bool LayToolsLine(uint faction, int lineIndex, BuildingState workshop)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var route = BeltRoute(faction, OutputCell(workshop), taken, next => FeedsOwnCore(next, faction));
            if (route.cells == null) return false;
            SetLineBelts(lineIndex, route);
            return LayRoutes(faction, route);
        }

        private uint PlaceCharmWorkshop(uint faction, int preferredCell = -1)
        {
            var core = OwnCore(faction).Definition.Position;
            int centre = preferredCell >= 0 ? preferredCell : world.Map.Cell(core);
            int size = world.Config.Economy.CharmSizeCells;
            int origin = FindProcessingSite(faction, centre, size);
            if (origin < 0) origin = FindProcessingSite(faction, world.Map.Cell(core), size);
            if (origin < 0) return 0;
            foreach (var side in SidesToward(FootprintCenter(origin, size), core))
            {
                if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                uint before = world.NextBuildingId;
                PlaceBuildingAt(faction, BuildingKind.CharmWorkshop, origin, side, 0);
                return LineBuildingIdAfterPlacement(before, BuildingKind.CharmWorkshop);
            }
            return 0;
        }

        private bool LayCharmsLine(uint faction, int lineIndex, BuildingState workshop)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var route = BeltRoute(faction, OutputCell(workshop), taken, next => FeedsOwnCore(next, faction));
            if (route.cells == null) return false;
            SetLineBelts(lineIndex, route);
            return LayRoutes(faction, route);
        }

        private void DecideRepairKits(uint faction)
        {
            if (!RepairKitsAllowed(faction) || !FishingIndustryPrerequisites(faction)) return;
            int yards = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (IsPlannedBuilding(world.Buildings[i]) && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == BuildingKind.RepairYard) yards++;
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            int index = GetOrCreateLine(faction, ProcessingLineKind.RepairKits);
            ref var line = ref world.ProcessingLines[index];
            if (!IsAutoLine(index) || LineRebuildBlocked(index)) { StopLineHaulers(index); return; }
            if (!BuildingReady(line.RepairYardId))
            {
                if (BuildingPending(line.RepairYardId)) return;
                if (yards >= rules.RepairKitMaxYards || economy.Wood < rules.RepairYardWoodCost + rules.RepairKitWoodReserve) return;
                uint id = PlaceRepairYard(faction);
                if (id != 0) line.RepairYardId = id;
                return;
            }
            var yard = world.Buildings[line.RepairYardId - 1];
            bool whole = LayRepairKitsLine(faction, index, yard);
            SetLineHaulers(faction, line, whole ? 0 : 1);
        }

        private uint PlaceRepairYard(uint faction, int preferredCell = -1)
        {
            var core = OwnCore(faction).Definition.Position;
            int centre = preferredCell >= 0 ? preferredCell : world.Map.Cell(core);
            int size = world.Config.Economy.RepairYardSizeCells;
            int origin = FindProcessingSite(faction, centre, size);
            if (origin < 0) origin = FindProcessingSite(faction, world.Map.Cell(core), size);
            if (origin < 0) return 0;
            foreach (var side in SidesToward(FootprintCenter(origin, size), core))
            {
                if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                uint before = world.NextBuildingId;
                PlaceBuildingAt(faction, BuildingKind.RepairYard, origin, side, 0);
                return LineBuildingIdAfterPlacement(before, BuildingKind.RepairYard);
            }
            return 0;
        }

        private bool LayRepairKitsLine(uint faction, int lineIndex, BuildingState yard)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var route = BeltRoute(faction, OutputCell(yard), taken, next => FeedsOwnCore(next, faction));
            if (route.cells == null) return false;
            SetLineBelts(lineIndex, route);
            return LayRoutes(faction, route);
        }

        private uint PlaceIncenseHouse(uint faction, int preferredCell = -1)
        {
            var core = OwnCore(faction).Definition.Position;
            int centre = preferredCell >= 0 ? preferredCell : world.Map.Cell(core);
            int size = world.Config.Economy.IncenseSizeCells;
            int origin = FindProcessingSite(faction, centre, size);
            if (origin < 0) origin = FindProcessingSite(faction, world.Map.Cell(core), size);
            if (origin < 0) return 0;
            foreach (var side in SidesToward(FootprintCenter(origin, size), core))
            {
                if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                uint before = world.NextBuildingId;
                PlaceBuildingAt(faction, BuildingKind.IncenseHouse, origin, side, 0);
                return LineBuildingIdAfterPlacement(before, BuildingKind.IncenseHouse);
            }
            return 0;
        }

        private bool LayIncenseLine(uint faction, int lineIndex, BuildingState house)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var route = BeltRoute(faction, OutputCell(house), taken, next => FeedsOwnCore(next, faction));
            if (route.cells == null) return false;
            SetLineBelts(lineIndex, route);
            return LayRoutes(faction, route);
        }

        private int FindHarborSite(uint faction)
        {
            var rules = world.Config.Economy;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells, size = rules.HarborSizeCells;
            var core = OwnCore(faction).Definition.Position;
            int best = -1;
            BigInteger bestFish = 0, bestCore = 0;
            long reach = Fix64.FromInt(rules.FishReach * 2).Raw;
            BigInteger limit = new BigInteger(reach) * reach;
            for (int origin = 0; origin < width * height; origin++)
            {
                if (origin % width + size > width || origin / width + size > height) continue;
                var centre = FootprintCenter(origin, size);
                BigInteger fishDistance = 0;
                bool nearFish = false;
                for (int n = 0; n < world.Nodes.Length; n++)
                {
                    var node = world.Nodes[n];
                    if (!ResourceKnownToFaction(faction, n) || !node.Fishing || node.Remaining <= 0) continue;
                    BigInteger distance = DistanceSquared(centre, node.Definition.Position);
                    if (distance > limit) continue;
                    if (!nearFish || distance < fishDistance) { nearFish = true; fishDistance = distance; }
                }
                if (!nearFish) continue;
                if (!HarborSiteIsClear(faction, origin) || !KeepsMapConnected(faction, origin, size)) continue;
                BigInteger coreDistance = DistanceSquared(centre, core);
                if (best < 0 || fishDistance < bestFish || fishDistance == bestFish && (coreDistance < bestCore
                    || coreDistance == bestCore && origin < best))
                {
                    best = origin; bestFish = fishDistance; bestCore = coreDistance;
                }
            }
            return best;
        }

        private bool HarborSiteIsClear(uint faction, int origin)
        {
            int size = world.Config.Economy.HarborSizeCells;
            return SiteIsClear(origin, world.Map.Cell(OwnCore(faction).Definition.Position), size)
                && HarborTouchesRiver(origin, size);
        }

        private bool HarborTouchesRiver(int origin, int size)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                {
                    int cell = origin + z * width + x;
                    if (x > 0 && IsRiverCell(cell - 1) || x + 1 < size && IsRiverCell(cell + 1)
                        || z > 0 && IsRiverCell(cell - width) || z + 1 < size && IsRiverCell(cell + width)) return true;
                    if (x == 0 && origin % width > 0 && IsRiverCell(cell - 1)
                        || x + 1 == size && origin % width + size < width && IsRiverCell(cell + 1)
                        || z == 0 && origin / width > 0 && IsRiverCell(cell - width)
                        || z + 1 == size && origin / width + size < height && IsRiverCell(cell + width)) return true;
                }
            return false;
        }

        private void DecideLegacyIndustry(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            int mine = OwnBuildingIndex(faction, BuildingKind.Mine), smelter = OwnBuildingIndex(faction, BuildingKind.Smelter);
            if (mine < 0)
            {
                if (economy.Wood >= world.Config.Economy.MineWoodCost) PlaceMine(faction);
                return;
            }
            if (smelter < 0)
            {
                if (economy.Wood >= world.Config.Economy.SmelterWoodCost) PlaceSmelter(faction, world.Buildings[mine]);
                return;
            }
            var m = world.Buildings[mine];
            var s = world.Buildings[smelter];
            if (!m.Complete || !s.Complete) return;
            if (m.Held || s.Held) { SetHaulers(faction, m.Id, s.Id, 0); return; }
            bool whole = LayLineLegacy(faction, m, s);
            SetHaulers(faction, m.Id, s.Id, whole ? 0 : Haulers);
        }

        /// <summary>
        /// V3-7 first pass: one lumber camp on the nearest usable wood point, then one existing farm-style route to
        /// the core. No terrain score is used here; the later terrain pass owns that decision.
        /// </summary>
        private void DecideLumberCamp(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            int camp = OwnBuildingIndex(faction, BuildingKind.LumberCamp);
            if (camp < 0)
            {
                if (economy.Wood >= world.Config.Economy.LumberCampWoodCost) PlaceLumberCamp(faction);
                return;
            }
            var b = world.Buildings[camp];
            if (!b.Complete || b.Held) return;
            var taken = new bool[world.Belts.Length];
            var route = BeltRoute(faction, OutputCell(b), taken, next => FeedsOwnCore(next, faction));
            bool whole = false;
            if (route.cells != null)
            {
                foreach (int c in route.cells) taken[c] = true;
                whole = LayBelts(faction, route.cells, route.facings);
            }
            SetFarmHauler(faction, b.Id, whole ? 0 : 1);
        }

        /// <summary>
        /// V3-8 first pass: one quarry on the nearest usable stone point, then the same core route and hand-haul fallback
        /// as a lumber camp. Terrain quality is deliberately not scored until the second masonry pass.
        /// </summary>
        private void DecideQuarry(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            int quarry = OwnBuildingIndex(faction, BuildingKind.Quarry);
            if (quarry < 0)
            {
                if (economy.Wood >= world.Config.Economy.QuarryWoodCost) PlaceQuarry(faction);
                return;
            }
            var b = world.Buildings[quarry];
            if (!b.Complete || b.Held) return;
            var taken = new bool[world.Belts.Length];
            var route = BeltRoute(faction, OutputCell(b), taken, next => FeedsOwnCore(next, faction));
            bool whole = false;
            if (route.cells != null)
            {
                foreach (int c in route.cells) taken[c] = true;
                whole = LayBelts(faction, route.cells, route.facings);
            }
            SetFarmHauler(faction, b.Id, whole ? 0 : 1);
        }

        private void DecideBricks(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            int quarry = OwnBuildingIndex(faction, BuildingKind.Quarry);
            if (quarry < 0 || !world.Buildings[quarry].Complete || world.Buildings[quarry].Held
                || economy.Stone < world.Config.Economy.BrickStoneInput * 2) return;
            int index = GetOrCreateLine(faction, ProcessingLineKind.Bricks);
            ref var line = ref world.ProcessingLines[index];
            if (line.Manager != LineManager.Automatic || line.RequestPending) return;
            if (line.BrickKilnId != 0)
            {
                if (BuildingReady(line.QuarryId) && BuildingReady(line.KilnId) && BuildingReady(line.BrickKilnId))
                {
                    bool whole = LayBricksLine(faction, index, world.Buildings[line.QuarryId - 1], world.Buildings[line.KilnId - 1], world.Buildings[line.BrickKilnId - 1]);
                    SetLineHaulers(faction, line, whole ? 0 : 1);
                }
                return;
            }
            line.QuarryId = world.Buildings[quarry].Id;
            line.RequestedCell = world.Buildings[quarry].OriginCell;
            line.RequestPending = true;
        }

        /// <summary>V3-11: build the camp, then choose and build one useful bridge at a time.</summary>
        private void DecideEngineerCamp(uint faction)
        {
            int camp = OwnBuildingIndex(faction, BuildingKind.EngineerCamp);
            if (camp < 0)
            {
                ref var economy = ref world.Economies[faction - 1];
                if (economy.Wood < world.Config.Economy.EngineerCampWoodCost) return;
                int origin = FindSite(faction, world.Config.Economy.EngineerCampSizeCells);
                if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.EngineerCamp, origin, Facing.North, 0);
                return;
            }
            // Any human bridge operation owns the bridge policy for the rest of this match. This covers a manually
            // removed bridge too: the automatic economy must not replace it at a different location.
            if (HasManualBridge(faction) || ActiveBridgeCount(faction) >= AutomaticBridgeLimit(faction)) return;
            // With no surviving automatic bridge, retain the old single-line behaviour: the destroyed line itself
            // waits 200 ticks. Once another automatic bridge survives, cooldowns are independent and this one may
            // be rebuilt without stopping the surviving route.
            if (AutomaticBridgeRebuildBlocked(faction)) return;
            ref var bridgeEconomy = ref world.Economies[faction - 1];
            if (bridgeEconomy.Wood < BridgeWoodCostFor(faction, 1)) return;
            if (!TryFindBridgeCandidate(faction, out int[] cells, out Facing facing, out int workCell)) return;
            if (!TryValidateBridge(faction, cells, facing, out cells, out workCell)) return;
            PlaceBuildingAt(faction, BuildingKind.Bridge, cells[0], facing, 0, cells, workCell);
        }

        private bool HasManualBridge(uint faction)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.FactionId == faction && b.Kind == BuildingKind.Bridge && b.Held) return true;
            }
            return false;
        }

        private bool AutomaticBridgeRebuildBlocked(uint faction)
        {
            bool hasActive = false, hasCooldown = false;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.FactionId != faction || b.Kind != BuildingKind.Bridge || b.Held) continue;
                if (IsActualBuilding(b)) hasActive = true;
                else if (b.DestroyedTick > 0 && world.Tick - b.DestroyedTick < BridgeRebuildWaitTicks) hasCooldown = true;
            }
            return hasCooldown && !hasActive;
        }

        private int AutomaticBridgeLimit(uint faction)
        {
            // A single observed resource line keeps the legacy one-bridge behaviour (and avoids opening a second
            // route merely because the fixed core objectives exist). Two or more observed public resource points
            // justify the provisional three-route limit used by the multi-bridge policy.
            int observedResources = 0;
            var explored = world.Factions[faction - 1].ExploredCells;
            foreach (var node in world.Nodes)
            {
                int cell = world.Map.Cell(node.Definition.Position);
                if (node.Remaining > 0 && cell >= 0 && explored[cell]) observedResources++;
            }
            return observedResources >= 2 ? MaxActiveBridges : 1;
        }

        private bool TryFindBridgeCandidate(uint faction, out int[] bestCells, out Facing bestFacing, out int bestWorkCell)
            => TryFindBridgeCandidate(faction, true, true, true, out bestCells, out bestFacing, out bestWorkCell, out _);

        /// <summary>Finds the best legal bridge. The start-of-match evaluator uses the same search with the camp and
        /// civilisation gates disabled; it still requires a legal river crossing and a reachable building bank.</summary>
        private bool TryFindBridgeCandidate(uint faction, bool requireCamp, bool requireCiv, bool avoidDanger,
            out int[] bestCells, out Facing bestFacing, out int bestWorkCell, out int bestOwnSaving)
        {
            if (measure != null) BridgeSearchCalls++;
            bestCells = null; bestFacing = Facing.North; bestWorkCell = -1; bestOwnSaving = 0;
            var destinations = BridgeDestinations(faction, includeKnownEnemyCore: requireCamp);
            if (destinations.Count == 0) return false;
            // The start-of-match score is explicitly core-to-destination. Once the civ is active, retain the
            // previous operational search over the known objective set so an already useful bridge line is not lost.
            var sources = requireCamp ? destinations : BridgeSources(faction);
            if (sources.Count == 0) return false;
            var observedEnemyCells = avoidDanger ? ObservedEnemyCells(faction) : new List<int>();
            if (measure != null)
            {
                BridgeSourceCellsConsidered += sources.Count;
                BridgeDestinationCellsConsidered += destinations.Count;
            }
            int bestAdjusted = 0, bestOwn = 0, bestEnemy = 0, bestFirst = int.MaxValue, bestLength = int.MaxValue;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int mapCells = checked(width * height);
            if (bridgeBasePassable == null || bridgeBasePassable.Length != mapCells || bridgeBasePassableVersion != world.Map.PassableVersion)
            {
                if (bridgeBasePassable == null || bridgeBasePassable.Length != mapCells) bridgeBasePassable = new bool[mapCells];
                for (int cell = 0; cell < mapCells; cell++) bridgeBasePassable[cell] = world.Map.IsPassable(cell);
                bridgeBasePassableVersion = world.Map.PassableVersion;
                bridgeDistanceMapVersion = bridgeBasePassableVersion;
                bridgeDistanceMaps.Clear();
                if (measure != null)
                {
                    BridgeBasePassableBuilds++;
                    BridgeBasePassableCellsScanned += mapCells;
                }
            }
            var ownWithout = BridgeRouteBaseline(sources, destinations, bridgeBasePassable);
            var enemyWithout = avoidDanger ? BridgeRouteBaseline(observedEnemyCells, destinations, bridgeBasePassable) : null;
            int max = world.Config.Economy.MaxBridgeLength;
            // Positive directions and z/x order are the deterministic candidate order. Reverse input is equivalent
            // for placement, and the first candidate wins every remaining tie.
            int[] deltas = { 1, width };
            var candidates = new List<BridgeCandidate>();
            for (int direction = 0; direction < deltas.Length; direction++)
                for (int z = 0; z < height; z++)
                    for (int x = 0; x < width; x++)
                        {
                            if (measure != null) BridgeCandidateStartCellsInspected++;
                            int first = z * width + x, delta = deltas[direction];
                            if (!IsRiverCell(first) || world.Map.IsPassable(first)) continue;
                            int before = first - delta;
                            if (before < 0 || before >= width * height || !world.Map.IsPassable(before) || IsRiverCell(before)) continue;
                            int length = 0, last = first;
                            while (length < max && last >= 0 && last < width * height && IsRiverCell(last) && !world.Map.IsPassable(last))
                            {
                                if (direction == 0 && last / width != z || direction == 1 && last % width != x) break;
                                length++; last += delta;
                            }
                            if (length == 0 || length > max || last < 0 || last >= width * height || !world.Map.IsPassable(last) || IsRiverCell(last)) continue;
                            var candidate = new int[length];
                            for (int i = 0; i < length; i++) candidate[i] = first + delta * i;
                            if (!TryValidateBridge(faction, candidate, direction == 0 ? Facing.East : Facing.South, out _, out int workCell,
                                requireCamp, requireCiv)) continue;
                            if (avoidDanger && ObservedEnemyNear(faction, workCell)) continue;
                            if (BridgeOnRebuildCooldown(faction, candidate)) continue;
                            candidates.Add(new BridgeCandidate { Cells = candidate,
                                Facing = direction == 0 ? Facing.East : Facing.South, WorkCell = workCell });
                        }
            if (measure != null) BridgeCandidatesEvaluated += candidates.Count;
            var ownSavings = BridgeSavingsForCandidates(candidates, sources, destinations, bridgeBasePassable, ownWithout);
            var enemySavings = avoidDanger
                ? BridgeSavingsForCandidates(candidates, observedEnemyCells, destinations, bridgeBasePassable, enemyWithout)
                : null;
            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                            var candidate = candidates[candidateIndex];
                            int ownSaving = ownSavings[candidateIndex];
                            if (ownSaving < BridgeShorteningThresholdCells) continue;
                            int enemySaving = avoidDanger ? enemySavings[candidateIndex] : 0;
                            int adjusted = checked(ownSaving - enemySaving / 2); // enemy shortcut is a discount, based only on observed enemies
                            if (adjusted <= 0) continue;
                            if (bestCells == null || adjusted > bestAdjusted || adjusted == bestAdjusted && ownSaving > bestOwn
                                || adjusted == bestAdjusted && ownSaving == bestOwn && enemySaving < bestEnemy
                                || adjusted == bestAdjusted && ownSaving == bestOwn && enemySaving == bestEnemy && candidate.Cells[0] < bestFirst
                                || adjusted == bestAdjusted && ownSaving == bestOwn && enemySaving == bestEnemy && candidate.Cells[0] == bestFirst && candidate.Cells.Length < bestLength)
                            {
                                bestCells = candidate.Cells; bestFacing = candidate.Facing; bestWorkCell = candidate.WorkCell;
                                bestAdjusted = adjusted; bestOwn = ownSaving; bestEnemy = enemySaving;
                                bestFirst = candidate.Cells[0]; bestLength = candidate.Cells.Length;
                                bestOwnSaving = ownSaving;
                            }
            }
            return bestCells != null;
        }

        private List<int> BridgeDestinations(uint faction, bool includeKnownEnemyCore)
        {
            var result = new List<int>();
            var seen = new HashSet<int>();
            void Add(int cell)
            {
                if (cell >= 0 && world.Map.IsPassable(cell) && seen.Add(cell)) result.Add(cell);
            }
            // Cores and outposts are fixed public objectives; resource nodes enter only after this faction has
            // observed their cells. The source side remains the own core, so the enemy core is only a known goal,
            // never a source of hidden unit information.
            Add(world.Map.Cell(OwnCore(faction).Definition.Position));
            if (includeKnownEnemyCore) Add(world.Map.Cell(world.Cores[world.Factions[2 - faction].CoreId - 1].Definition.Position));
            foreach (var outpost in world.Outposts) Add(world.Map.Cell(outpost.Definition.Position));
            var explored = world.Factions[faction - 1].ExploredCells;
            foreach (var node in world.Nodes)
            {
                int cell = world.Map.Cell(node.Definition.Position);
                if (node.Remaining > 0 && cell >= 0 && explored[cell]) Add(cell);
            }
            return result;
        }

        private List<int> BridgeSources(uint faction)
        {
            var result = new List<int>(1);
            int core = world.Map.Cell(OwnCore(faction).Definition.Position);
            if (core >= 0 && world.Map.IsPassable(core)) result.Add(core);
            return result;
        }

        private List<int> ObservedEnemyCells(uint faction)
        {
            var result = new List<int>();
            var seen = new HashSet<int>();
            var visible = world.Factions[faction - 1].VisibleCells;
            for (int i = 0; i < world.SoldierCount; i++)
            {
                var soldier = world.Soldiers[i];
                if (!soldier.Alive || soldier.Initial.FactionId == faction) continue;
                int cell = world.Map.Cell(soldier.Position);
                if (cell >= 0 && visible[cell] && world.Map.IsPassable(cell) && seen.Add(cell)) result.Add(cell);
            }
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var villager = world.Villagers[i];
                if (!villager.Alive || villager.FactionId == faction) continue;
                int cell = world.Map.Cell(villager.Position);
                if (cell >= 0 && visible[cell] && world.Map.IsPassable(cell) && seen.Add(cell)) result.Add(cell);
            }
            return result;
        }

        private int[] BridgeRouteBaseline(List<int> sources, List<int> destinations, bool[] basePassable)
        {
            var without = new int[sources.Count * destinations.Count];
            for (int s = 0; s < sources.Count; s++)
                for (int d = 0; d < destinations.Count; d++)
                    without[s * destinations.Count + d] = sources[s] == destinations[d] ? 0
                        : BridgeRouteLength(sources[s], destinations[d], basePassable, null, null);
            return without;
        }

        private int[] BridgeSavingsForCandidates(List<BridgeCandidate> candidates, List<int> sources,
            List<int> destinations, bool[] basePassable, int[] without)
        {
            var savings = new int[candidates.Count];
            if (sources.Count == 0 || candidates.Count == 0) return savings;
            int width = world.Config.Map.WidthCells;
            for (int s = 0; s < sources.Count; s++)
            {
                var fromSource = BridgeDistanceMapFor(sources[s], basePassable, bridgeRouteWorkspace);
                for (int d = 0; d < destinations.Count; d++)
                {
                    int before = without[s * destinations.Count + d];
                    if (before == 0) continue;
                    var toDestination = BridgeDistanceMapFor(destinations[d], basePassable, bridgeRouteWorkspace2);
                    if (measure != null) BridgeRouteEvaluations += candidates.Count;
                    for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                    {
                        int after = BridgeOpenedRouteLength(candidates[candidateIndex].Cells, fromSource, toDestination, width);
                        if (after < 0) continue;
                        int saving = before < 0 ? checked(basePassable.Length - after) : before - after;
                        if (saving > savings[candidateIndex]) savings[candidateIndex] = saving;
                    }
                }
            }
            return savings;
        }

        private int[] BridgeDistanceMapFor(int start, bool[] basePassable, RouteDistance.Workspace workspace)
        {
            if (measure != null) BridgeDistanceMapRequests++;
            if (bridgeDistanceCacheEnabled && bridgeDistanceMapVersion == world.Map.PassableVersion
                && bridgeDistanceMaps.TryGetValue(start, out var cached))
            {
                if (measure != null)
                {
                    BridgeDistanceMapCacheHits++;
                    BridgeLegacyDistanceMapCells += cached.VisitedCells;
                }
                return cached.Distances;
            }
            if (workspace == null) workspace = new RouteDistance.Workspace();
            RouteDistance.FillDistances(world.Config.Map.WidthCells, world.Config.Map.HeightCells, basePassable, start, workspace);
            int visitedCells = workspace.LastVisitedCells;
            if (measure != null)
            {
                BridgeDistanceMapsBuilt++;
                BridgeDistanceMapCells += visitedCells;
                BridgeLegacyDistanceMapCells += visitedCells;
            }
            if (!bridgeDistanceCacheEnabled) return workspace.Distances;
            // Each map covers the whole grid (about 0.5 MB on the large map). Dropping them all only costs a rebuild,
            // never a different answer, so the cache stays bounded between terrain changes.
            if (bridgeDistanceMaps.Count >= BridgeDistanceMapCacheLimit) bridgeDistanceMaps.Clear();
            var distances = new int[basePassable.Length];
            System.Array.Copy(workspace.Distances, distances, distances.Length);
            bridgeDistanceMaps[start] = new BridgeDistanceMap { Distances = distances, VisitedCells = visitedCells };
            return distances;
        }

        /// <summary>Test-only comparison of the uncached and cached candidate selection.</summary>
        internal string FindBridgeCandidateForTest(uint faction, bool useDistanceCache)
        {
            bool previous = bridgeDistanceCacheEnabled;
            bridgeDistanceCacheEnabled = useDistanceCache;
            try
            {
                if (!TryFindBridgeCandidate(faction, false, false, false, out var cells, out var facing, out var workCell, out _))
                    return "none";
                return facing + ":" + workCell + ":" + string.Join("/", cells);
            }
            finally { bridgeDistanceCacheEnabled = previous; }
        }

        /// <summary>Test-only distance-map probe used to verify terrain invalidation.</summary>
        internal void ProbeBridgeDistanceMapForTest(int start)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int mapCells = checked(width * height);
            if (bridgeBasePassable == null || bridgeBasePassable.Length != mapCells || bridgeBasePassableVersion != world.Map.PassableVersion)
            {
                if (bridgeBasePassable == null || bridgeBasePassable.Length != mapCells) bridgeBasePassable = new bool[mapCells];
                for (int cell = 0; cell < mapCells; cell++) bridgeBasePassable[cell] = world.Map.IsPassable(cell);
                bridgeBasePassableVersion = world.Map.PassableVersion;
                bridgeDistanceMapVersion = bridgeBasePassableVersion;
                bridgeDistanceMaps.Clear();
                if (measure != null)
                {
                    BridgeBasePassableBuilds++;
                    BridgeBasePassableCellsScanned += mapCells;
                }
            }
            BridgeDistanceMapFor(start, bridgeBasePassable, bridgeRouteWorkspace);
        }

        private static int BridgeOpenedRouteLength(int[] cells, int[] fromSource, int[] toDestination, int width)
        {
            int best = int.MaxValue;
            for (int i = 0; i < cells.Length; i++)
            {
                int sourceEntry = NearestBaseNeighbourDistance(cells[i], fromSource, width);
                if (sourceEntry < 0) continue;
                for (int j = 0; j < cells.Length; j++)
                {
                    int destinationExit = NearestBaseNeighbourDistance(cells[j], toDestination, width);
                    if (destinationExit < 0) continue;
                    int distance = checked(sourceEntry + Math.Abs(i - j) + destinationExit);
                    if (distance < best) best = distance;
                }
            }
            return best == int.MaxValue ? -1 : best;
        }

        private static int NearestBaseNeighbourDistance(int cell, int[] distances, int width)
        {
            int best = -1, x = cell % width;
            int z = cell / width;
            if (z + 1 < distances.Length / width) best = SmallerDistance(best, distances[cell + width]);
            if (x + 1 < width) best = SmallerDistance(best, distances[cell + 1]);
            if (z > 0) best = SmallerDistance(best, distances[cell - width]);
            if (x > 0) best = SmallerDistance(best, distances[cell - 1]);
            return best < 0 ? -1 : best + 1;
        }

        private static int SmallerDistance(int current, int candidate)
            => candidate >= 0 && (current < 0 || candidate < current) ? candidate : current;

        private int MaximumBridgeSaving(int[] bridgeCells, List<int> sources, List<int> destinations,
            bool[] basePassable, int[] without)
        {
            if (sources.Count == 0) return 0;
            int best = 0;
            for (int s = 0; s < sources.Count; s++)
                for (int d = 0; d < destinations.Count; d++)
                {
                    int before = without[s * destinations.Count + d];
                    if (before == 0) continue;
                    int after = BridgeRouteLength(sources[s], destinations[d], basePassable, null, bridgeCells);
                    if (after < 0) continue;
                    int saving = before < 0 ? checked(basePassable.Length - after) : before - after;
                    if (saving > best) best = saving;
                }
            return best;
        }

        /// <summary>Converts a route-cell saving into the small integer used beside resource-point scores.</summary>
        private static int EngineerBridgeScore(int savingCells)
        {
            if (savingCells < BridgeShorteningThresholdCells) return 0;
            if (savingCells < BridgeScoreTier2Cells) return 1;
            if (savingCells < BridgeScoreTier3Cells) return 2;
            return 3;
        }

        private int BridgeRouteLength(int start, int destination, bool[] basePassable, bool[] known,
            IReadOnlyList<int> temporarilyOpen)
        {
            if (bridgeRouteWorkspace == null) bridgeRouteWorkspace = new RouteDistance.Workspace();
            return RouteDistance.Measure(world.Config.Map.WidthCells, world.Config.Map.HeightCells,
                basePassable, known, start, destination, temporarilyOpen, bridgeRouteWorkspace);
        }

        private bool ObservedEnemyNear(uint faction, int cell)
        {
            long radius = Fix64.FromInt(BridgeDangerMeters).Raw;
            long limit = checked(radius * radius);
            var point = world.Map.Center(cell);
            var visible = world.Factions[faction - 1].VisibleCells;
            for (int i = 0; i < world.SoldierCount; i++)
            {
                var enemy = world.Soldiers[i];
                if (!enemy.Alive || enemy.Initial.FactionId == faction) continue;
                int enemyCell = world.Map.Cell(enemy.Position);
                if (enemyCell < 0 || !visible[enemyCell]) continue;
                long dx = enemy.Position.X.Raw - point.X.Raw, dz = enemy.Position.Z.Raw - point.Z.Raw;
                if (checked(dx * dx + dz * dz) <= limit) return true;
            }
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var enemy = world.Villagers[i];
                if (!enemy.Alive || enemy.FactionId == faction) continue;
                int enemyCell = world.Map.Cell(enemy.Position);
                if (enemyCell < 0 || !visible[enemyCell]) continue;
                long dx = enemy.Position.X.Raw - point.X.Raw, dz = enemy.Position.Z.Raw - point.Z.Raw;
                if (checked(dx * dx + dz * dz) <= limit) return true;
            }
            return false;
        }

        private bool BridgeOnRebuildCooldown(uint faction, int[] cells)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.FactionId != faction || b.Kind != BuildingKind.Bridge || b.Alive || b.Held || b.DestroyedTick <= 0
                    || world.Tick - b.DestroyedTick >= BridgeRebuildWaitTicks) continue;
                var old = b.BridgeCells ?? Array.Empty<int>();
                if (old.Length != cells.Length) continue;
                bool same = true;
                foreach (int cell in cells) if (Array.IndexOf(old, cell) < 0) { same = false; break; }
                if (same) return true;
            }
            return false;
        }

        /// <summary>V3-7 #2: after the first camp, keep a separate core-wood line and a bow-gear line.</summary>
        private void DecideForestryLines(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            var rules = world.Config.Economy;
            int coreIndex = GetOrCreateLine(faction, ProcessingLineKind.CoreWood);
            ref var coreLine = ref world.ProcessingLines[coreIndex];
            if (LineRebuildBlocked(coreIndex)) { StopLineHaulers(coreIndex); return; }
            if (!BindForestryCamp(faction, coreIndex, ref coreLine, 0)) return;
            if (IsAutoLine(coreIndex))
            {
                var coreCamp = world.Buildings[coreLine.LumberCampId - 1];
                if (!coreCamp.Complete) return;
                bool coreWhole = LayForestryCoreLine(faction, coreIndex, coreCamp);
                SetLineHaulers(faction, coreLine, coreWhole ? 0 : 1);
            }

            int bowIndex = LineIndex(faction, ProcessingLineKind.BowGear);
            if (LineRebuildBlocked(bowIndex)) { StopLineHaulers(bowIndex); return; }
            uint bowCampId = FindCamp(faction, coreLine.LumberCampId);
            if (bowCampId == 0)
            {
                // Keep the market, the next age and ordinary construction funded; only the surplus opens the second line.
                if (economy.Food < rules.MarketFoodFloor || economy.Wood < rules.MarketWoodReserve + rules.LumberCampWoodCost + rules.FletcherWoodCost) return;
                PlaceLumberCamp(faction);
                return;
            }
            bowIndex = bowIndex >= 0 ? bowIndex : GetOrCreateLine(faction, ProcessingLineKind.BowGear);
            ref var bowLine = ref world.ProcessingLines[bowIndex];
            if (bowLine.LumberCampId == 0 || !BuildingAlive(bowLine.LumberCampId)) SetLineBuilding(bowIndex, BuildingKind.LumberCamp, bowCampId);
            if (bowLine.Manager == LineManager.Automatic && world.Buildings[bowLine.LumberCampId - 1].Held) bowLine.Manager = LineManager.Manual;
            if (!IsAutoLine(bowIndex)) return;
            if (!BuildingReady(bowLine.FletcherId))
            {
                if (BuildingPending(bowLine.FletcherId)) return;
                if (economy.Wood < rules.FletcherWoodCost) return;
                uint id = PlaceFletcher(faction, world.Buildings[bowLine.LumberCampId - 1]);
                if (id != 0) SetLineBuilding(bowIndex, BuildingKind.Fletcher, id);
                return;
            }
            var bowCamp = world.Buildings[bowLine.LumberCampId - 1];
            var fletcher = world.Buildings[bowLine.FletcherId - 1];
            if (!bowCamp.Complete || !fletcher.Complete) return;
            bool bowWhole = LayBowLine(faction, bowIndex, bowCamp, fletcher);
            SetLineHaulers(faction, bowLine, bowWhole ? 0 : 1);
        }

        private bool BindForestryCamp(uint faction, int lineIndex, ref ProcessingLineState line, uint exclude)
        {
            if (line.LumberCampId != 0 && BuildingAlive(line.LumberCampId)) return true;
            uint id = FindCamp(faction, exclude);
            if (id == 0)
            {
                DecideLumberCamp(faction);
                return false;
            }
            SetLineBuilding(lineIndex, BuildingKind.LumberCamp, id);
            line = world.ProcessingLines[lineIndex];
            if (world.Buildings[id - 1].Held) line.Manager = LineManager.Manual;
            return true;
        }

        private uint FindCamp(uint faction, uint exclude)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (IsActualBuilding(b) && b.FactionId == faction && b.Kind == BuildingKind.LumberCamp && b.Id != exclude) return b.Id;
            }
            return 0;
        }

        private bool BuildingAlive(uint id) => IsActualBuilding(id);

        private uint PlaceFletcher(uint faction, BuildingState camp)
        {
            var core = OwnCore(faction).Definition.Position;
            int centre = world.Map.Cell(FootprintCenter(camp.OriginCell, SizeOf(camp.Kind)));
            int origin = FindProcessingSite(faction, centre, world.Config.Economy.FletcherSizeCells);
            if (origin < 0) return 0;
            foreach (var side in SidesToward(FootprintCenter(origin, world.Config.Economy.FletcherSizeCells), core))
            {
                if (!PortIsOpen(OutputCell(origin, world.Config.Economy.FletcherSizeCells, side), faction)) continue;
                uint before = world.NextBuildingId;
                PlaceBuildingAt(faction, BuildingKind.Fletcher, origin, side, 0);
                return LineBuildingIdAfterPlacement(before, BuildingKind.Fletcher);
            }
            return 0;
        }

        private void DecideRations(uint faction)
        {
            if (!RationsAllowed(faction)) return;
            ref var economy = ref world.Economies[faction - 1];
            int farms = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (IsPlannedBuilding(world.Buildings[i]) && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == BuildingKind.Farm) farms++;
            var rules = world.Config.Economy;
            if (farms < rules.RationsFarmMinimum || economy.Food < rules.RationsFoodReserve) return;
            int index = GetOrCreateLine(faction, ProcessingLineKind.Rations);
            if (!IsAutoLine(index) || LineRebuildBlocked(index)) { StopLineHaulers(index); return; }
            ref var line = ref world.ProcessingLines[index];
            if (!BuildingReady(line.MillId))
            {
                if (BuildingPending(line.MillId) || economy.Wood < rules.MillWoodCost) return;
                uint id = PlaceMill(faction);
                if (id != 0) line.MillId = id;
                return;
            }
            var mill = world.Buildings[line.MillId - 1];
            bool whole = LayRationsLine(faction, index, mill);
            SetLineHaulers(faction, line, whole ? 0 : 1);
        }

        private uint PlaceMill(uint faction, int preferredCell = -1)
        {
            var core = OwnCore(faction).Definition.Position;
            int centre = preferredCell >= 0 ? preferredCell : world.Map.Cell(core);
            int size = world.Config.Economy.MillSizeCells;
            int origin = FindProcessingSite(faction, centre, size);
            if (origin < 0) return 0;
            foreach (var side in SidesToward(FootprintCenter(origin, size), core))
            {
                if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                uint before = world.NextBuildingId;
                PlaceBuildingAt(faction, BuildingKind.Mill, origin, side, 0);
                return LineBuildingIdAfterPlacement(before, BuildingKind.Mill);
            }
            return 0;
        }

        private bool LayRationsLine(uint faction, int lineIndex, BuildingState mill)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var route = BeltRoute(faction, OutputCell(mill), taken, next => FeedsOwnCore(next, faction));
            if (route.cells == null) return false;
            SetLineBelts(lineIndex, route);
            return LayRoutes(faction, route);
        }

        private void DecideJewels(uint faction)
        {
            if (!JewelsAllowed(faction)) return;
            ref var economy = ref world.Economies[faction - 1];
            var rules = world.Config.Economy;
            int shafts = 0, lapidaries = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!IsActualBuilding(b) || b.FactionId != faction) continue;
                if (b.Kind == BuildingKind.MineShaft && b.Complete) shafts++;
                if (b.Kind == BuildingKind.Lapidary) lapidaries++;
            }
            if (shafts == 0) return;
            int index = GetOrCreateLine(faction, ProcessingLineKind.Jewels);
            ref var line = ref world.ProcessingLines[index];
            if (!IsAutoLine(index) || LineRebuildBlocked(index)) { StopLineHaulers(index); return; }
            if (!BuildingReady(line.LapidaryId))
            {
                if (lapidaries >= rules.JewelMaxLapidaries) return;
                if (BuildingPending(line.LapidaryId)) return;
                if (economy.Wood < rules.LapidaryWoodCost + rules.JewelWoodReserve) return;
                uint id = PlaceLapidary(faction);
                if (id != 0) line.LapidaryId = id;
                return;
            }
            var lapidary = world.Buildings[line.LapidaryId - 1];
            bool whole = LayJewelsLine(faction, index, lapidary);
            SetLineHaulers(faction, line, whole ? 0 : 1);
        }

        private uint PlaceLapidary(uint faction, int preferredCell = -1)
        {
            var core = OwnCore(faction).Definition.Position;
            int centre = preferredCell >= 0 ? preferredCell : world.Map.Cell(core);
            int size = world.Config.Economy.LapidarySizeCells;
            int origin = FindProcessingSite(faction, centre, size);
            if (origin < 0) origin = FindProcessingSite(faction, world.Map.Cell(core), size);
            if (origin < 0) return 0;
            foreach (var side in SidesToward(FootprintCenter(origin, size), core))
            {
                if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                uint before = world.NextBuildingId;
                PlaceBuildingAt(faction, BuildingKind.Lapidary, origin, side, 0);
                return LineBuildingIdAfterPlacement(before, BuildingKind.Lapidary);
            }
            return 0;
        }

        private bool LayJewelsLine(uint faction, int lineIndex, BuildingState lapidary)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var route = BeltRoute(faction, OutputCell(lapidary), taken, next => FeedsOwnCore(next, faction));
            if (route.cells == null) return false;
            SetLineBelts(lineIndex, route);
            return LayRoutes(faction, route);
        }

        private uint PlaceDryingRack(uint faction, int preferredCell = -1)
        {
            var core = OwnCore(faction).Definition.Position;
            int centre = preferredCell >= 0 ? preferredCell : world.Map.Cell(core);
            int size = world.Config.Economy.DryingRackSizeCells;
            int origin = FindProcessingSite(faction, centre, size);
            if (origin < 0) return 0;
            foreach (var side in SidesToward(FootprintCenter(origin, size), core))
            {
                if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                uint before = world.NextBuildingId;
                PlaceBuildingAt(faction, BuildingKind.DryingRack, origin, side, 0);
                return LineBuildingIdAfterPlacement(before, BuildingKind.DryingRack);
            }
            return 0;
        }

        private bool LayPreservesLine(uint faction, int lineIndex, BuildingState rack)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var route = BeltRoute(faction, OutputCell(rack), taken, next => FeedsOwnCore(next, faction));
            if (route.cells == null) return false;
            SetLineBelts(lineIndex, route);
            return LayRoutes(faction, route);
        }

        private bool DecideBooks(uint faction)
        {
            if (!BooksAllowed(faction)) return false;
            ref var economy = ref world.Economies[faction - 1];
            var rules = world.Config.Economy;
            int index = GetOrCreateLine(faction, ProcessingLineKind.Books);
            ref var line = ref world.ProcessingLines[index];
            if (!IsAutoLine(index) || LineRebuildBlocked(index)) { StopLineHaulers(index); return false; }
            if (!BuildingReady(line.ScriptoriumId))
            {
                // Starting the line is a priority after the Academy, but it must not hold research hostage
                // while the building is pending or the stock is below its construction reserve.
                if (BuildingPending(line.ScriptoriumId) || economy.Wood < rules.ScriptoriumWoodCost + rules.BookWoodReserve) return false;
                uint id = PlaceScriptorium(faction);
                if (id != 0) line.ScriptoriumId = id;
                return false;
            }
            var scriptorium = world.Buildings[line.ScriptoriumId - 1];
            bool whole = LayBooksLine(faction, index, scriptorium);
            SetLineHaulers(faction, line, whole ? 0 : 1);
            return false;
        }

        private uint PlaceScriptorium(uint faction, int preferredCell = -1)
        {
            var core = OwnCore(faction).Definition.Position;
            int centre = preferredCell >= 0 ? preferredCell : world.Map.Cell(core);
            int size = world.Config.Economy.ScriptoriumSizeCells;
            int origin = FindProcessingSite(faction, centre, size);
            if (origin < 0) origin = FindProcessingSite(faction, world.Map.Cell(core), size);
            if (origin < 0) return 0;
            foreach (var side in SidesToward(FootprintCenter(origin, size), core))
            {
                if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                uint before = world.NextBuildingId;
                PlaceBuildingAt(faction, BuildingKind.Scriptorium, origin, side, 0);
                return LineBuildingIdAfterPlacement(before, BuildingKind.Scriptorium);
            }
            return 0;
        }

        private bool LayBooksLine(uint faction, int lineIndex, BuildingState scriptorium)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var route = BeltRoute(faction, OutputCell(scriptorium), taken, next => FeedsOwnCore(next, faction));
            if (route.cells == null) return false;
            SetLineBelts(lineIndex, route);
            return LayRoutes(faction, route);
        }

        private void DecidePlanks(uint faction)
        {
            if (!PlanksAllowed(faction)) return;
            ref var economy = ref world.Economies[faction - 1];
            var rules = world.Config.Economy;
            int index = GetOrCreateLine(faction, ProcessingLineKind.Planks);
            ref var line = ref world.ProcessingLines[index];
            if (!IsAutoLine(index) || LineRebuildBlocked(index)) { StopLineHaulers(index); return; }
            if (!BuildingReady(line.LumberCampId))
            {
                if (BuildingPending(line.LumberCampId)) return;
                if (economy.Wood < rules.LumberCampWoodCost + rules.SawmillWoodCost + rules.PlankWoodReserve) return;
                uint id = PlaceLumberCamp(faction);
                if (id != 0) line.LumberCampId = id;
                return;
            }
            if (!BuildingReady(line.SawmillId))
            {
                if (BuildingPending(line.SawmillId) || economy.Wood < rules.SawmillWoodCost + rules.PlankWoodReserve) return;
                uint id = PlaceSawmill(faction, world.Buildings[line.LumberCampId - 1]);
                if (id != 0) line.SawmillId = id;
                return;
            }
            var camp = world.Buildings[line.LumberCampId - 1];
            var sawmill = world.Buildings[line.SawmillId - 1];
            bool whole = LayPlanksLine(faction, index, camp, sawmill);
            SetLineHaulers(faction, line, whole ? 0 : 1);
        }

        private void DecideTack(uint faction)
        {
            if (!TackAllowed(faction)) return;
            int stable = OwnBuildingIndex(faction, BuildingKind.Stable);
            if (stable < 0 || !world.Buildings[stable].Complete || world.Buildings[stable].Held
                || world.Economies[faction - 1].Age < 1) return;
            ref var economy = ref world.Economies[faction - 1];
            var rules = world.Config.Economy;
            int index = GetOrCreateLine(faction, ProcessingLineKind.Tack);
            ref var line = ref world.ProcessingLines[index];
            if (!IsAutoLine(index) || LineRebuildBlocked(index)) { StopLineHaulers(index); return; }
            if (!BuildingReady(line.SaddleryId))
            {
                if (BuildingPending(line.SaddleryId) || economy.Wood < rules.SaddleryWoodCost + rules.TackWoodReserve) return;
                uint id = PlaceSaddlery(faction);
                if (id != 0) line.SaddleryId = id;
                return;
            }
            var saddlery = world.Buildings[line.SaddleryId - 1];
            bool whole = LayTackLine(faction, index, saddlery);
            SetLineHaulers(faction, line, whole ? 0 : 1);
        }

        private uint PlaceSaddlery(uint faction, int preferredCell = -1)
        {
            var core = OwnCore(faction).Definition.Position;
            int centre = preferredCell >= 0 ? preferredCell : world.Map.Cell(core);
            int size = world.Config.Economy.SaddlerySizeCells;
            int origin = FindProcessingSite(faction, centre, size);
            if (origin < 0) return 0;
            foreach (var side in SidesToward(FootprintCenter(origin, size), core))
            {
                if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                uint before = world.NextBuildingId;
                PlaceBuildingAt(faction, BuildingKind.Saddlery, origin, side, 0);
                return LineBuildingIdAfterPlacement(before, BuildingKind.Saddlery);
            }
            return 0;
        }

        private bool LayTackLine(uint faction, int lineIndex, BuildingState saddlery)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var route = BeltRoute(faction, OutputCell(saddlery), taken, next => FeedsOwnCore(next, faction));
            if (route.cells == null) return false;
            SetLineBelts(lineIndex, route);
            return LayRoutes(faction, route);
        }

        private uint PlaceSawmill(uint faction, BuildingState camp)
        {
            int size = world.Config.Economy.SawmillSizeCells;
            int centre = camp.OriginCell;
            int origin = FindProcessingSite(faction, centre, size);
            if (origin < 0) origin = FindProcessingSite(faction, world.Map.Cell(OwnCore(faction).Definition.Position), size);
            if (origin < 0) return 0;
            var core = OwnCore(faction).Definition.Position;
            foreach (var side in SidesToward(FootprintCenter(origin, size), core))
            {
                if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                uint before = world.NextBuildingId;
                PlaceBuildingAt(faction, BuildingKind.Sawmill, origin, side, 0);
                return LineBuildingIdAfterPlacement(before, BuildingKind.Sawmill);
            }
            return 0;
        }

        private bool LayPlanksLine(uint faction, int lineIndex, BuildingState camp, BuildingState sawmill)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var toSawmill = BeltRoute(faction, OutputCell(camp), taken, next => InFootprint(sawmill, next));
            if (toSawmill.cells == null) return false;
            foreach (int c in toSawmill.cells) taken[c] = true;
            var toCore = BeltRoute(faction, OutputCell(sawmill), taken, next => FeedsOwnCore(next, faction));
            if (toCore.cells == null) return false;
            SetLineBelts(lineIndex, toSawmill, toCore);
            return LayRoutes(faction, toSawmill, toCore);
        }

        private bool LayBricksLine(uint faction, int lineIndex, BuildingState quarry, BuildingState kiln, BuildingState brickKiln)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var toBrickStone = BeltRoute(faction, OutputCell(quarry), taken, next => InFootprint(brickKiln, next));
            if (toBrickStone.cells == null) return false;
            foreach (int c in toBrickStone.cells) taken[c] = true;
            var toBrickCharcoal = BeltRoute(faction, OutputCell(kiln), taken, next => InFootprint(brickKiln, next));
            if (toBrickCharcoal.cells == null) return false;
            foreach (int c in toBrickCharcoal.cells) taken[c] = true;
            var toCore = BeltRoute(faction, OutputCell(brickKiln), taken, next => FeedsOwnCore(next, faction));
            if (toCore.cells == null) return false;
            SetLineBelts(lineIndex, toBrickStone, toBrickCharcoal, toCore);
            return LayRoutes(faction, toBrickStone, toBrickCharcoal, toCore);
        }

        private bool LayForestryCoreLine(uint faction, int lineIndex, BuildingState camp)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var route = BeltRoute(faction, OutputCell(camp), taken, next => FeedsOwnCore(next, faction));
            if (route.cells == null) return false;
            SetLineBelts(lineIndex, route);
            return LayRoutes(faction, route);
        }

        private bool LayBowLine(uint faction, int lineIndex, BuildingState camp, BuildingState fletcher)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var toFletcher = BeltRoute(faction, OutputCell(camp), taken, next => InFootprint(fletcher, next));
            if (toFletcher.cells == null) return false;
            foreach (int c in toFletcher.cells) taken[c] = true;
            var toCore = BeltRoute(faction, OutputCell(fletcher), taken, next => FeedsOwnCore(next, faction));
            if (toCore.cells == null) return false;
            SetLineBelts(lineIndex, toFletcher, toCore);
            return LayRoutes(faction, toFletcher, toCore);
        }

        /// <summary>Places the camp over the nearest remaining wood point, with deterministic footprint and port order.</summary>
        private uint PlaceLumberCamp(uint faction, int preferredCell = -1)
        {
            var core = preferredCell >= 0 ? world.Map.Center(preferredCell) : OwnCore(faction).Definition.Position;
            int size = world.Config.Economy.LumberCampSizeCells, width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            var order = new int[world.Nodes.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int c = DistanceSquared(world.Nodes[a].Definition.Position, core).CompareTo(DistanceSquared(world.Nodes[b].Definition.Position, core));
                return c != 0 ? c : world.Nodes[a].Definition.Id.CompareTo(world.Nodes[b].Definition.Id);
            });
            foreach (int n in order)
            {
                var node = world.Nodes[n];
                if (!ResourceKnownToFaction(faction, n) || node.Definition.Kind != ResourceKind.Wood || node.Remaining <= 0) continue;
                int cell = world.Map.Cell(node.Definition.Position), nx = cell % width, nz = cell / width;
                for (int dz = 0; dz < size; dz++)
                    for (int dx = 0; dx < size; dx++)
                    {
                        int x0 = nx - dx, z0 = nz - dz;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (!LumberCampSiteIsClear(faction, origin, out uint nodeId) || !KeepsMapConnected(faction, origin, size)) continue;
                        foreach (var side in SidesToward(FootprintCenter(origin, size), core))
                        {
                            if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                            uint before = world.NextBuildingId;
                            PlaceBuildingAt(faction, BuildingKind.LumberCamp, origin, side, nodeId);
                            return LineBuildingIdAfterPlacement(before, BuildingKind.LumberCamp);
                        }
                    }
            }
            return 0;
        }

        /// <summary>Places the quarry over the nearest remaining stone point, with the same fixed search as a lumber camp.</summary>
        private uint PlaceQuarry(uint faction, int preferredCell = -1)
        {
            var core = preferredCell >= 0 ? world.Map.Center(preferredCell) : OwnCore(faction).Definition.Position;
            int size = world.Config.Economy.QuarrySizeCells, width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            var order = new int[world.Nodes.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int c = DistanceSquared(world.Nodes[a].Definition.Position, core).CompareTo(DistanceSquared(world.Nodes[b].Definition.Position, core));
                return c != 0 ? c : world.Nodes[a].Definition.Id.CompareTo(world.Nodes[b].Definition.Id);
            });
            foreach (int n in order)
            {
                var node = world.Nodes[n];
                if (!ResourceKnownToFaction(faction, n) || node.Definition.Kind != ResourceKind.Stone || node.Remaining <= 0) continue;
                int cell = world.Map.Cell(node.Definition.Position), nx = cell % width, nz = cell / width;
                for (int dz = 0; dz < size; dz++)
                    for (int dx = 0; dx < size; dx++)
                    {
                        int x0 = nx - dx, z0 = nz - dz;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (!QuarrySiteIsClear(faction, origin, out uint nodeId) || !KeepsMapConnected(faction, origin, size)) continue;
                        foreach (var side in SidesToward(FootprintCenter(origin, size), core))
                        {
                            if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                            uint before = world.NextBuildingId;
                            PlaceBuildingAt(faction, BuildingKind.Quarry, origin, side, nodeId);
                            return LineBuildingIdAfterPlacement(before, BuildingKind.Quarry);
                        }
                    }
            }
            return 0;
        }

        private int OwnBuildingIndex(uint faction, BuildingKind kind)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (IsActualBuilding(b) && b.FactionId == faction && b.Kind == kind) return i;
            }
            return -1;
        }

        /// <summary>Ore points by distance to the core, then id; on each, the four footprints and the sides toward the core first.</summary>
        private uint PlaceMine(uint faction, int preferredCell = -1)
        {
            var core = preferredCell >= 0 ? world.Map.Center(preferredCell) : OwnCore(faction).Definition.Position;
            int size = world.Config.Economy.MineSizeCells, width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            var order = new int[world.Nodes.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int c = DistanceSquared(world.Nodes[a].Definition.Position, core).CompareTo(DistanceSquared(world.Nodes[b].Definition.Position, core));
                return c != 0 ? c : a.CompareTo(b);
            });
            foreach (int n in order)
            {
                var node = world.Nodes[n];
                if (!ResourceKnownToFaction(faction, n) || node.Definition.Kind != ResourceKind.Ore || node.Remaining <= 0) continue;
                int cell = world.Map.Cell(node.Definition.Position), nx = cell % width, nz = cell / width;
                for (int dz = 0; dz < size; dz++)
                    for (int dx = 0; dx < size; dx++)
                    {
                        int x0 = nx - dx, z0 = nz - dz;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (!MineSiteIsClear(faction, origin, out uint id) || !KeepsMapConnected(faction, origin, size)) continue;
                        foreach (var side in SidesToward(FootprintCenter(origin, size), core))
                        {
                            if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                            uint before = world.NextBuildingId;
                            PlaceBuildingAt(faction, BuildingKind.Mine, origin, side, id);
                            return LineBuildingIdAfterPlacement(before, BuildingKind.Mine);
                        }
                    }
            }
            return 0;
        }

        /// <summary>Rings around the point halfway from the mine to the core, like the barracks search.</summary>
        private uint PlaceSmelter(uint faction, BuildingState mine)
        {
            var core = OwnCore(faction).Definition.Position;
            var from = FootprintCenter(mine.OriginCell, SizeOf(mine.Kind));
            var middle = new SimPoint(Fix64.FromRaw((from.X.Raw + core.X.Raw) / 2), Fix64.FromRaw((from.Z.Raw + core.Z.Raw) / 2));
            int size = world.Config.Economy.SmelterSizeCells, width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int centre = world.Map.Cell(middle), cx = centre % width, cz = centre / width, coreCell = world.Map.Cell(core);
            for (int r = 0; r <= SiteSearchRadiusCells; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x0 = cx + dx - size / 2, z0 = cz + dz - size / 2;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (!SiteIsClear(origin, coreCell, size) || !KeepsMapConnected(faction, origin, size)) continue;
                        foreach (var side in SidesToward(FootprintCenter(origin, size), core))
                        {
                            if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                            uint before = world.NextBuildingId;
                            PlaceBuildingAt(faction, BuildingKind.Smelter, origin, side, 0);
                            return LineBuildingIdAfterPlacement(before, BuildingKind.Smelter);
                        }
                    }
            return 0;
        }

        /// <summary>Places the charcoal kiln in the same deterministic rings as a barracks, around the nearest wood point.</summary>
        private uint PlaceKiln(uint faction, int preferredCell = -1)
        {
            var core = preferredCell >= 0 ? world.Map.Center(preferredCell) : OwnCore(faction).Definition.Position;
            int bestNode = -1;
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                var node = world.Nodes[i];
                if (!ResourceKnownToFaction(faction, i) || node.Definition.Kind != ResourceKind.Wood || node.Remaining <= 0) continue;
                if (bestNode < 0 || DistanceSquared(node.Definition.Position, core) < DistanceSquared(world.Nodes[bestNode].Definition.Position, core)
                    || DistanceSquared(node.Definition.Position, core) == DistanceSquared(world.Nodes[bestNode].Definition.Position, core)
                    && node.Definition.Id < world.Nodes[bestNode].Definition.Id) bestNode = i;
            }
            if (bestNode < 0) return 0;
            int centre = world.Map.Cell(world.Nodes[bestNode].Definition.Position);
            int origin = FindProcessingSite(faction, centre, world.Config.Economy.CharcoalKilnSizeCells);
            if (origin < 0) return 0;
            foreach (var side in SidesToward(FootprintCenter(origin, world.Config.Economy.CharcoalKilnSizeCells), core))
            {
                if (!PortIsOpen(OutputCell(origin, world.Config.Economy.CharcoalKilnSizeCells, side), faction)) continue;
                uint before = world.NextBuildingId;
                PlaceBuildingAt(faction, BuildingKind.CharcoalKiln, origin, side, 0);
                return LineBuildingIdAfterPlacement(before, BuildingKind.CharcoalKiln);
            }
            return 0;
        }

        private uint PlaceBrickKiln(uint faction, int preferredCell = -1)
        {
            var core = preferredCell >= 0 ? world.Map.Center(preferredCell) : OwnCore(faction).Definition.Position;
            int size = world.Config.Economy.BrickKilnSizeCells;
            int origin = FindProcessingSite(faction, preferredCell >= 0 ? preferredCell : world.Map.Cell(core), size);
            if (origin < 0) return 0;
            foreach (var side in SidesToward(FootprintCenter(origin, size), core))
            {
                if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                uint before = world.NextBuildingId;
                PlaceBuildingAt(faction, BuildingKind.BrickKiln, origin, side, 0);
                return LineBuildingIdAfterPlacement(before, BuildingKind.BrickKiln);
            }
            return 0;
        }

        /// <summary>Places the steelworks at the midpoint of the dedicated smelter, kiln and core.</summary>
        private uint PlaceSteelworks(uint faction, ProcessingLineState line)
        {
            var core = OwnCore(faction).Definition.Position;
            var smelter = world.Buildings[line.SmelterId - 1];
            var kiln = world.Buildings[line.KilnId - 1];
            var smelterPoint = FootprintCenter(smelter.OriginCell, SizeOf(smelter.Kind));
            var kilnPoint = FootprintCenter(kiln.OriginCell, SizeOf(kiln.Kind));
            var middle = new SimPoint(Fix64.FromRaw((smelterPoint.X.Raw + kilnPoint.X.Raw + core.X.Raw) / 3),
                Fix64.FromRaw((smelterPoint.Z.Raw + kilnPoint.Z.Raw + core.Z.Raw) / 3));
            int centre = world.Map.Cell(middle);
            int origin = FindProcessingSite(faction, centre, world.Config.Economy.SteelworksSizeCells);
            if (origin < 0) return 0;
            foreach (var side in SidesToward(FootprintCenter(origin, world.Config.Economy.SteelworksSizeCells), core))
            {
                if (!PortIsOpen(OutputCell(origin, world.Config.Economy.SteelworksSizeCells, side), faction)) continue;
                uint before = world.NextBuildingId;
                PlaceBuildingAt(faction, BuildingKind.Steelworks, origin, side, 0);
                return LineBuildingIdAfterPlacement(before, BuildingKind.Steelworks);
            }
            return 0;
        }

        private int FindProcessingSite(uint faction, int centre, int size)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int coreCell = world.Map.Cell(OwnCore(faction).Definition.Position), cx = centre % width, cz = centre / width;
            for (int r = 0; r <= SiteSearchRadiusCells; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x0 = cx + dx - size / 2, z0 = cz + dz - size / 2;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (SiteIsClear(origin, coreCell, size) && KeepsMapConnected(faction, origin, size)) return origin;
                    }
            return -1;
        }

        /// <summary>The four sides, the one pointing most toward <paramref name="to"/> first (ties in N, E, S, W order).</summary>
        private static Facing[] SidesToward(SimPoint from, SimPoint to)
        {
            long dx = (to.X.Raw - from.X.Raw) / 65536, dz = (to.Z.Raw - from.Z.Raw) / 65536;
            var sides = (Facing[])Sides.Clone();
            long Score(Facing f) => f == Facing.North ? dz : f == Facing.East ? dx : f == Facing.South ? -dz : -dx;
            Array.Sort(sides, (a, b) => { int c = Score(b).CompareTo(Score(a)); return c != 0 ? c : a.CompareTo(b); });
            return sides;
        }

        /// <summary>A cell a belt could start on: on the map, open, not a resource point, outside every core, no other side's belt.</summary>
        private bool PortIsOpen(int cell, uint faction)
            => cell >= 0 && world.Map.IsPassable(cell) && !IsNodeCell(cell) && !InsideAnyCore(cell)
               && (world.Belts[cell].FactionId == 0 || (world.Belts[cell].FactionId == faction && !world.Belts[cell].Held));

        private bool LayLineLegacy(uint faction, BuildingState mine, BuildingState smelter)
        {
            int cells = world.Belts.Length;
            var taken = new bool[cells];
            var toSmelter = BeltRoute(faction, OutputCell(mine), taken, next => InFootprint(smelter, next));
            if (toSmelter.cells == null) return false;
            foreach (int c in toSmelter.cells) taken[c] = true;
            var toCore = BeltRoute(faction, OutputCell(smelter), taken, next => FeedsOwnCore(next, faction));
            if (toCore.cells == null) return false;
            return LayRoutes(faction, toSmelter, toCore);
        }

        private bool LayCoreLine(uint faction, int lineIndex, BuildingState mine, BuildingState smelter)
        {
            var taken = TakenForOtherLines(faction, lineIndex);
            var toSmelter = BeltRoute(faction, OutputCell(mine), taken, next => InFootprint(smelter, next));
            if (toSmelter.cells == null) return false;
            foreach (int c in toSmelter.cells) taken[c] = true;
            var toCore = BeltRoute(faction, OutputCell(smelter), taken, next => FeedsOwnCore(next, faction));
            if (toCore.cells == null) return false;
            SetLineBelts(lineIndex, toSmelter, toCore);
            return LayRoutes(faction, toSmelter, toCore);
        }

        private bool LaySteelLine(uint faction, int lineIndex, BuildingState mine, BuildingState smelter, BuildingState kiln, BuildingState steelworks)
        {
            int coreIndex = LineIndex(faction, ProcessingLineKind.CoreMetal);
            if (IsSharedMetalLine(coreIndex, lineIndex))
            {
                if (LineBeltsWhole(lineIndex)) return true;
                if (LaySharedSteelLine(faction, coreIndex, lineIndex, mine, smelter, kiln, steelworks)) return true;
                // The shared plan is all-or-nothing. Remember a terrain/wood failure on this line, then let the
                // existing dedicated-line stage build its own mine and smelter on the next AI cycle.
                ref var failed = ref world.ProcessingLines[lineIndex];
                failed.MineId = 0; failed.SmelterId = 0; failed.RequestedRegionId = SharedMetalFallbackMarker;
                return false;
            }
            var taken = TakenForOtherLines(faction, lineIndex);
            var toSmelter = BeltRoute(faction, OutputCell(mine), taken, next => InFootprint(smelter, next));
            if (toSmelter.cells == null) return false;
            foreach (int c in toSmelter.cells) taken[c] = true;
            var metal = BeltRoute(faction, OutputCell(smelter), taken, next => InFootprint(steelworks, next));
            if (metal.cells == null) return false;
            foreach (int c in metal.cells) taken[c] = true;
            Facing metalSide = metal.facings[metal.facings.Length - 1];
            var charcoal = BeltRoute(faction, OutputCell(kiln), taken,
                (from, next) => InFootprint(steelworks, next) && FacingFrom(from, next) != metalSide);
            if (charcoal.cells == null) return false;
            foreach (int c in charcoal.cells) taken[c] = true;
            var steel = BeltRoute(faction, OutputCell(steelworks), taken, next => FeedsOwnCore(next, faction));
            if (steel.cells == null) return false;
            SetLineBelts(lineIndex, toSmelter, metal, charcoal, steel);
            return LayRoutes(faction, toSmelter, metal, charcoal, steel);
        }

        /// <summary>
        /// V3-20 automatic economy policy: when the component flag is on, reuse the completed core metal line for
        /// steel. The splitter is put on the smelter output itself; its forward branch remains the core route and
        /// its left branch is the deterministic metal input route to the steelworks.
        /// </summary>
        private void TryBindSharedMetalLine(int coreIndex, int steelIndex)
        {
            if (!world.Config.Economy.BeltComponents || coreIndex < 0 || steelIndex < 0
                || coreIndex >= world.ProcessingLines.Length || steelIndex >= world.ProcessingLines.Length || coreIndex == steelIndex) return;
            ref var core = ref world.ProcessingLines[coreIndex];
            ref var steel = ref world.ProcessingLines[steelIndex];
            if (core.Kind != ProcessingLineKind.CoreMetal || steel.Kind != ProcessingLineKind.Steel
                || core.Manager != LineManager.Automatic || steel.Manager != LineManager.Automatic
                || steel.RequestedRegionId == SharedMetalFallbackMarker || steel.MineId != 0 || steel.SmelterId != 0) return;
            // The caller of the autonomous path already gates on a whole core route. The request path may reach this
            // helper one decision earlier, so a recorded route plus ready buildings is the stable shared-plan input.
            if (!LineBuildingsReady(core) || core.BeltCells.Length == 0) return;
            steel.MineId = core.MineId;
            steel.SmelterId = core.SmelterId;
        }

        private bool IsSharedMetalLine(int coreIndex, int steelIndex)
        {
            if (coreIndex < 0 || steelIndex < 0 || coreIndex >= world.ProcessingLines.Length || steelIndex >= world.ProcessingLines.Length)
                return false;
            var core = world.ProcessingLines[coreIndex];
            var steel = world.ProcessingLines[steelIndex];
            return core.Kind == ProcessingLineKind.CoreMetal && steel.Kind == ProcessingLineKind.Steel
                && core.MineId != 0 && core.MineId == steel.MineId && core.SmelterId != 0 && core.SmelterId == steel.SmelterId;
        }

        private bool LaySharedSteelLine(uint faction, int coreIndex, int steelIndex, BuildingState mine, BuildingState smelter,
            BuildingState kiln, BuildingState steelworks)
        {
            int smelterOutput = OutputCell(smelter);
            if (smelterOutput < 0 || smelterOutput >= world.Belts.Length) return false;
            var taken = TakenForOtherLines(faction, coreIndex);
            var toSmelter = BeltRoute(faction, OutputCell(mine), taken, next => InFootprint(smelter, next));
            if (toSmelter.cells == null) return false;
            foreach (int cell in toSmelter.cells) taken[cell] = true;

            var recordedCore = world.ProcessingLines[coreIndex];
            int outputIndex = Array.IndexOf(recordedCore.BeltCells, smelterOutput);
            if (outputIndex < 0 || outputIndex >= recordedCore.BeltFacings.Length)
            { return false; }
            for (int splitterIndex = outputIndex; splitterIndex < recordedCore.BeltCells.Length; splitterIndex++)
            {
                int splitter = recordedCore.BeltCells[splitterIndex];
                Facing facing = recordedCore.BeltFacings[splitterIndex];
                if (!SplitterPlacementAllowed(faction, splitter)) continue;
                int forward = BeltNext(splitter, facing), left = BeltNext(splitter, TurnLeft(facing));
                int suffixLength = recordedCore.BeltCells.Length - splitterIndex - 1;
                var coreCells = new int[suffixLength]; var coreFacings = new Facing[suffixLength];
                for (int i = 0; i < suffixLength; i++)
                {
                    coreCells[i] = recordedCore.BeltCells[splitterIndex + 1 + i];
                    coreFacings[i] = recordedCore.BeltFacings[splitterIndex + 1 + i];
                }
                if (suffixLength > 0 && coreCells[0] != forward) continue;
                var branchTaken = (bool[])taken.Clone(); branchTaken[splitter] = true;
                var coreRoute = (coreCells, coreFacings);
                foreach (int cell in coreRoute.coreCells) branchTaken[cell] = true;
                var metalRoute = SplitterExitRoute(faction, left, branchTaken, next => InFootprint(steelworks, next));
                if (metalRoute.cells == null) continue;
                foreach (int cell in metalRoute.cells) branchTaken[cell] = true;
                Facing metalSide = metalRoute.cells.Length == 0 ? FacingFrom(splitter, left)
                    : metalRoute.facings[metalRoute.facings.Length - 1];
                var charcoal = BeltRoute(faction, OutputCell(kiln), branchTaken,
                    (from, next) => InFootprint(steelworks, next) && FacingFrom(from, next) != metalSide);
                if (charcoal.cells == null) continue;
                foreach (int cell in charcoal.cells) branchTaken[cell] = true;
                var steel = BeltRoute(faction, OutputCell(steelworks), branchTaken, next => FeedsOwnCore(next, faction));
                if (steel.cells == null) continue;

                var splitterRoute = (new[] { splitter }, new[] { facing });
                if (!CanLaySharedRoutes(faction, splitter, facing, toSmelter, coreRoute, metalRoute, charcoal, steel)) continue;
                if (!LayAutomaticSplitter(faction, splitter, facing)) continue;
                SetLineBelts(coreIndex, toSmelter, splitterRoute, coreRoute);
                SetLineBelts(steelIndex, toSmelter, splitterRoute, metalRoute, charcoal, steel);
                LayRoutes(faction, toSmelter, coreRoute, metalRoute, charcoal, steel);
                return true;
            }
            return false;
        }

        private (int[] cells, Facing[] facings) SplitterExitRoute(uint faction, int start, bool[] taken, Func<int, bool> into)
        {
            if (start < 0 || start >= world.Belts.Length) return (null, null);
            if (into(start)) return (Array.Empty<int>(), Array.Empty<Facing>());
            return BeltRoute(faction, start, taken, into);
        }

        private bool SplitterPlacementAllowed(uint faction, int cell)
        {
            if (cell < 0 || cell >= world.Belts.Length || !world.Map.IsPassable(cell) || IsRiverCell(cell)
                || IsNodeCell(cell) || InsideAnyCore(cell)) return false;
            var belt = world.Belts[cell];
            if (belt.FactionId == 0) return true;
            return belt.FactionId == faction && !belt.Held
                && (belt.Component == BeltComponentKind.None || belt.Component == BeltComponentKind.Splitter);
        }

        private bool CanLaySharedRoutes(uint faction, int splitter, Facing facing,
            params (int[] cells, Facing[] facings)[] routes)
        {
            ref var economy = ref world.Economies[faction - 1];
            int owned = 0, added = 0, wood = 0;
            var seen = new bool[world.Belts.Length];
            for (int i = 0; i < world.Belts.Length; i++) if (world.Belts[i].FactionId == faction) owned++;
            var splitBelt = world.Belts[splitter];
            if (splitBelt.FactionId == 0 || splitBelt.Component == BeltComponentKind.None) wood = checked(wood + world.Config.Economy.SplitterWoodCost);
            else if (splitBelt.FactionId != faction || splitBelt.Held || splitBelt.Component != BeltComponentKind.Splitter || splitBelt.Facing != facing) return false;
            foreach (var route in routes)
                for (int i = 0; i < route.cells.Length; i++)
                {
                    int cell = route.cells[i];
                    if (cell < 0 || cell >= world.Belts.Length || seen[cell]) continue;
                    seen[cell] = true;
                    var belt = world.Belts[cell];
                    if (belt.FactionId == faction)
                    {
                        if (belt.Held || belt.Facing != route.facings[i]) return false;
                        continue;
                    }
                    if (belt.FactionId != 0) return false;
                    added++; wood = checked(wood + world.Config.Economy.BeltWoodCost);
                }
            return owned + added <= world.Config.Economy.BeltLimit && economy.Wood >= wood;
        }

        private bool LayAutomaticSplitter(uint faction, int cell, Facing facing)
        {
            ref var belt = ref world.Belts[cell];
            if (belt.FactionId == faction && belt.Component == BeltComponentKind.Splitter)
                return !belt.Held && belt.Facing == facing;
            if (!SplitterPlacementAllowed(faction, cell)) return false;
            ref var economy = ref world.Economies[faction - 1];
            if (economy.Wood < world.Config.Economy.SplitterWoodCost) return false;
            economy.Wood = checked(economy.Wood - world.Config.Economy.SplitterWoodCost);
            belt.FactionId = faction; belt.Facing = facing; belt.Hp = world.Config.Economy.BeltHp;
            belt.Held = false; belt.Component = BeltComponentKind.Splitter; belt.SorterKind = 0; belt.PairCell = 0; belt.SplitterRightNext = false;
            world.BeltOrder = null;
            return true;
        }

        private bool[] TakenForOtherLines(uint faction, int currentLine)
        {
            var taken = new bool[world.Belts.Length];
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                if (i == currentLine || world.ProcessingLines[i].FactionId != faction) continue;
                if (LinesShareAutomaticMetalRoute(currentLine, i)) continue;
                foreach (int cell in world.ProcessingLines[i].BeltCells)
                    if (cell >= 0 && cell < taken.Length && world.Belts[cell].FactionId == faction) taken[cell] = true;
            }
            return taken;
        }

        private bool LayRoutes(uint faction, params (int[] cells, Facing[] facings)[] routes)
        {
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            int owned = 0;
            for (int i = 0; i < world.Belts.Length; i++) if (world.Belts[i].FactionId == faction) owned++;
            if (LargeEconomyOn)
            {
                int missing = 0;
                var seen = new bool[world.Belts.Length];
                foreach (var (route, facings) in routes)
                    for (int i = 0; i < route.Length; i++)
                    {
                        int cell = route[i];
                        if (cell < 0 || cell >= world.Belts.Length || seen[cell]) continue;
                        seen[cell] = true;
                        if (world.Belts[cell].FactionId == 0) missing++;
                    }
                if (owned + missing > rules.BeltLimit || economy.Wood < missing * rules.BeltWoodCost) return false;
            }
            bool whole = true;
            foreach (var (route, facings) in routes)
                for (int i = 0; i < route.Length; i++)
                {
                    ref var belt = ref world.Belts[route[i]];
                    if (belt.FactionId == faction) { if (belt.Facing != facings[i]) whole = false; continue; }
                    whole = false;
                    if (owned >= rules.BeltLimit || economy.Wood < rules.BeltWoodCost) continue;
                    economy.Wood = checked(economy.Wood - rules.BeltWoodCost);
                    belt = new BeltState { FactionId = faction, Facing = facings[i], Hp = rules.BeltHp };
                    owned++;
                    world.BeltOrder = null;
                }
            return whole;
        }

        private Facing FacingFrom(int from, int to)
        {
            int width = world.Config.Map.WidthCells;
            return to == from + width ? Facing.North : to == from + 1 ? Facing.East : to == from - width ? Facing.South : Facing.West;
        }

        private bool InFootprint(BuildingState b, int cell)
        {
            int width = world.Config.Map.WidthCells, size = SizeOf(b.Kind), x0 = b.OriginCell % width, z0 = b.OriginCell / width;
            int x = cell % width, z = cell / width;
            return x >= x0 && x < x0 + size && z >= z0 && z < z0 + size;
        }

        /// <summary>Breadth-first from <paramref name="start"/> in N, E, S, W order; null when no route exists.</summary>
        private (int[] cells, Facing[] facings) BeltRoute(uint faction, int start, bool[] taken, Func<int, bool> into)
            => BeltRoute(faction, start, taken, (from, next) => into(next));

        private (int[] cells, Facing[] facings) BeltRoute(uint faction, int start, bool[] taken, Func<int, int, bool> into)
        {
            if (!PortIsOpen(start, faction) || !AiBeltCellKnown(faction, start) || taken[start]) return (null, null);
            int cells = world.Belts.Length;
            var from = new int[cells];
            for (int i = 0; i < cells; i++) from[i] = -2;
            var queue = new int[cells];
            int head = 0, tail = 0;
            queue[tail++] = start; from[start] = -1;
            while (head < tail)
            {
                int cell = queue[head++];
                foreach (var side in Sides)
                {
                    int next = BeltNext(cell, side);
                    if (next < 0) continue;
                    if (!AiBeltCellKnown(faction, next)) continue;
                    if (into(cell, next))
                    {
                        int length = 0;
                        for (int c = cell; c != -1; c = from[c]) length++;
                        var path = new int[length];
                        var facings = new Facing[length];
                        int k = length - 1;
                        for (int c = cell; c != -1; c = from[c]) path[k--] = c;
                        for (int i = 0; i + 1 < length; i++)
                            foreach (var d in Sides) if (BeltNext(path[i], d) == path[i + 1]) { facings[i] = d; break; }
                        facings[length - 1] = side;
                        return (path, facings);
                    }
                    if (from[next] != -2 || taken[next] || !PortIsOpen(next, faction)) continue;
                    from[next] = cell;
                    queue[tail++] = next;
                }
            }
            return (null, null);
        }

        private bool AiBeltCellKnown(uint faction, int cell)
        {
            if (!LargeEconomyOn) return true;
            var explored = world.Factions[faction - 1].ExploredCells;
            return cell >= 0 && cell < explored.Length && explored[cell];
        }

        private const int FarmTarget = 2, FarmSearchRadiusCells = 12;

        /// <summary>
        /// V3-4 agrarian (technical-design-v3 29): up to FarmTarget farms on the quickest ground near the core, each with
        /// a belt line into the core; until a farm's line is whole, one villager carries its food by hand. A farm the
        /// player placed or runs is left to them.
        /// </summary>
        private void DecideFarms(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            int farms = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (IsActualBuilding(world.Buildings[i]) && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == BuildingKind.Farm) farms++;
            int farmTarget = LargeEconomyOn
                ? Math.Min(world.Config.Economy.LargeFarmMaximum,
                    Math.Max(world.Config.Economy.LargeFarmMinimum,
                        (LivingVillagers(faction) + world.Config.Economy.LargeVillagersPerFarm - 1)
                            / world.Config.Economy.LargeVillagersPerFarm))
                : EconomyDecision.FoodSourceTarget(EconomyScaleOn, FarmTarget) + (EarlyArmsOn ? 1 : 0);
            if (farms < farmTarget && economy.Wood >= world.Config.Economy.FarmWoodCost && PlaceFarm(faction)) return;
            var taken = new bool[world.Belts.Length];
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!IsActualBuilding(b) || b.FactionId != faction || b.Kind != BuildingKind.Farm || !b.Complete || b.Held) continue;
                var route = BeltRoute(faction, OutputCell(b), taken, next => FeedsOwnCore(next, faction));
                bool whole = false;
                if (route.cells != null)
                {
                    foreach (int c in route.cells) taken[c] = true;
                    whole = LayBelts(faction, route.cells, route.facings);
                }
                SetFarmHauler(faction, b.Id, whole ? 0 : 1);
            }
        }

        /// <summary>Clear footprints in rings around the core; the quickest ground wins (then the ring order).</summary>
        private bool PlaceFarm(uint faction)
        {
            var core = OwnCore(faction).Definition.Position;
            int size = world.Config.Economy.FarmSizeCells, width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int coreCell = world.Map.Cell(core), cx = coreCell % width, cz = coreCell / width;
            var candidates = new System.Collections.Generic.List<(int interval, int order, int origin, Facing side)>();
            int order = 0;
            for (int r = 2; r <= FarmSearchRadiusCells; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x0 = cx + dx - size / 2, z0 = cz + dz - size / 2;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (!SiteIsClear(origin, coreCell, size)) continue;
                        foreach (var side in SidesToward(FootprintCenter(origin, size), core))
                        {
                            if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                            candidates.Add((FarmInterval(faction, origin), order++, origin, side));
                            break;
                        }
                    }
            candidates.Sort((a, b) => a.interval != b.interval ? a.interval.CompareTo(b.interval) : a.order.CompareTo(b.order));
            foreach (var c in candidates)
            {
                if (!KeepsMapConnected(faction, c.origin, size)) continue;
                PlaceBuildingAt(faction, BuildingKind.Farm, c.origin, c.side, 0);
                return true;
            }
            return false;
        }

        /// <summary>Lays the missing belts of one route while wood lasts; true when every cell carries an own belt along it.</summary>
        private bool LayBelts(uint faction, int[] route, Facing[] facings)
        {
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            int owned = 0;
            for (int i = 0; i < world.Belts.Length; i++) if (world.Belts[i].FactionId == faction) owned++;
            bool whole = true;
            for (int i = 0; i < route.Length; i++)
            {
                ref var belt = ref world.Belts[route[i]];
                if (belt.FactionId == faction) { if (belt.Facing != facings[i]) whole = false; continue; }
                whole = false;
                if (owned >= rules.BeltLimit || economy.Wood < rules.BeltWoodCost) continue;
                economy.Wood = checked(economy.Wood - rules.BeltWoodCost);
                belt = new BeltState { FactionId = faction, Facing = facings[i], Hp = rules.BeltHp };
                owned++;
                world.BeltOrder = null;
            }
            return whole;
        }

        /// <summary>One carrier per farm while its line is not whole (the nearest gatherer or idle villager), none after.</summary>
        private void SetFarmHauler(uint faction, uint farm, int wanted)
        {
            int current = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var v = ref world.Villagers[i];
                if (!v.Alive || v.FactionId != faction || v.Held || v.HaulFrom != farm) continue;
                if (wanted == 0) StopHauling(ref v); else current++;
            }
            if (current >= wanted) return;
            var spot = world.Map.Center(world.Buildings[farm - 1].WorkCell);
            int best = -1;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (!v.Alive || v.FactionId != faction || v.Held || v.HaulFrom != 0 || v.Carry > 0
                    || (v.Task != VillagerTask.Idle && v.Task != VillagerTask.ToNode && v.Task != VillagerTask.Gathering)) continue;
                if (best < 0 || DistanceSquared(v.Position, spot) < DistanceSquared(world.Villagers[best].Position, spot)) best = i;
            }
            if (best < 0) return;
            ref var chosen = ref world.Villagers[best];
            chosen.NodeId = 0;
            chosen.HaulFrom = farm;
            chosen.Task = VillagerTask.ToPickup;
        }

        /// <summary>
        /// Keeps <paramref name="wanted"/> villagers carrying by hand: the first from the mine, the second from the smelter.
        /// New carriers are the nearest to the mine among those gathering or idle (distance, then id); with none wanted,
        /// every carrier of this line stops.
        /// </summary>
        private void SetHaulers(uint faction, uint mine, uint smelter, int wanted, bool recovery = false)
        {
            int fromMine = 0, fromSmelter = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var v = ref world.Villagers[i];
                if (!v.Alive || v.FactionId != faction || v.Held || (v.HaulFrom != mine && v.HaulFrom != smelter)) continue;
                if (wanted == 0) StopHauling(ref v);
                else if (v.HaulFrom == mine) fromMine++;
                else fromSmelter++;
            }
            var spot = world.Map.Center(world.Buildings[mine - 1].WorkCell);
            int wantMine = (wanted + 1) / 2, wantSmelter = wanted / 2;
            while (fromMine < wantMine || fromSmelter < wantSmelter)
            {
                int best = -1;
                for (int i = 0; i < world.VillagerCount; i++)
                {
                    ref var v = ref world.Villagers[i];
                    if (recovery && (v.Task == VillagerTask.ToBuild || v.Task == VillagerTask.Building)
                        && v.BuildingId != mine && v.BuildingId != smelter)
                    {
                        v.Task = VillagerTask.Idle;
                        v.BuildingId = 0;
                    }
                    if (!v.Alive || v.FactionId != faction || v.Held || v.HaulFrom != 0 || v.Carry > 0
                        || (v.Task != VillagerTask.Idle && v.Task != VillagerTask.ToNode && v.Task != VillagerTask.Gathering)) continue;
                    if (best < 0 || DistanceSquared(v.Position, spot) < DistanceSquared(world.Villagers[best].Position, spot)) best = i;
                }
                if (best < 0) return;
                ref var chosen = ref world.Villagers[best];
                chosen.NodeId = 0;
                bool toMine = fromMine < wantMine;
                chosen.HaulFrom = toMine ? mine : smelter;
                chosen.Task = VillagerTask.ToPickup;
                if (toMine) fromMine++; else fromSmelter++;
            }
        }
    }
}
