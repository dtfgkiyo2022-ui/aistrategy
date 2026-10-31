using System;
using System.Numerics;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private struct CarriedMaterialRequirement
        {
            internal ResourceKind Material;
            internal byte Slot;
            internal int Amount;
        }

        private readonly CarriedMaterialRequirement[] carriedMaterialWorkspace = new CarriedMaterialRequirement[3];

        /// <summary>One table for every automatic processing building and the selected assembler recipe.</summary>
        private int CarriedMaterialRequirements(BuildingState building, CarriedMaterialRequirement[] result)
        {
            int count = 0;
            void Add(ResourceKind material, byte slot, int amount)
            {
                if (material == 0 || amount <= 0 || count >= result.Length) return;
                result[count++] = new CarriedMaterialRequirement { Material = material, Slot = slot, Amount = amount };
            }
            var rules = world.Config.Economy;
            switch (building.Kind)
            {
                case BuildingKind.Smelter: Add(ResourceKind.Ore, 0, CraftInputFor(building, rules.OrePerMetal)); break;
                case BuildingKind.CharcoalKiln:
                    if (CharcoalKilnAllowed(building.FactionId)) Add(ResourceKind.Wood, 0,
                        CraftInputFor(building, CraftTreeCommonAllowed(building.FactionId) ? rules.CraftTreeCharcoalWoodInput : 2));
                    break;
                case BuildingKind.Steelworks:
                    if (ProcessingOn) { Add(ResourceKind.Metal, 0, CraftInputFor(building, 1)); Add(ResourceKind.Charcoal, 1, CraftInputFor(building, 1)); }
                    break;
                case BuildingKind.Metalworks:
                    if (CraftDepthOn) Add(ResourceKind.Metal, 0, CraftInputFor(building, rules.CraftDepthFittingsMetalInput));
                    break;
                case BuildingKind.RopeHut:
                    if (CraftDepthOn) Add(ResourceKind.Wood, 0, CraftInputFor(building, rules.CraftDepthRopeWoodInput));
                    break;
                case BuildingKind.GearWorkshop:
                    if (BeltCraftOn) Add(ResourceKind.Metal, 0, CraftInputFor(building, rules.GearMetalInput));
                    break;
                case BuildingKind.Fletcher:
                    if (ForestryOn) { Add(ResourceKind.Wood, 0, CraftInputFor(building, rules.FletcherWoodInput)); Add(ResourceKind.Food, 1, CraftInputFor(building, rules.FletcherFoodInput)); }
                    break;
                case BuildingKind.Mill:
                    if (RationsOn) Add(ResourceKind.Food, 0, CraftInputFor(building, rules.MillFoodInput));
                    break;
                case BuildingKind.Saddlery:
                    if (TackAllowed(building.FactionId)) { Add(ResourceKind.Food, 0, CraftInputFor(building, rules.TackFoodInput)); Add(ResourceKind.Wood, 1, CraftInputFor(building, rules.TackWoodInput)); }
                    break;
                case BuildingKind.Sawmill:
                    if (PlanksAllowed(building.FactionId) || CraftTreeCommonAllowed(building.FactionId))
                        Add(ResourceKind.Wood, 0, CraftInputFor(building, CraftTreeCommonAllowed(building.FactionId) ? rules.CraftTreePlankWoodInput : rules.PlankWoodInput));
                    break;
                case BuildingKind.Stonecutter:
                    if (StonecutterAllowed(building.FactionId)) Add(ResourceKind.Stone, 0, CraftInputFor(building, rules.CraftTreeStoneInput));
                    break;
                case BuildingKind.Joinery:
                    if (JoineryAllowed(building.FactionId)) { Add(ResourceKind.Planks, 0, CraftInputFor(building, rules.CraftTreeJoineryPlankInput)); Add(ResourceKind.CutStone, 1, CraftInputFor(building, rules.CraftTreeJoineryCutStoneInput)); }
                    break;
                case BuildingKind.Hut:
                    if (PrimitiveCraftOn && building.Recipe == RecipeKind.StoneTools)
                    { Add(ResourceKind.Stone, 0, rules.StoneToolsStoneInput); Add(ResourceKind.Wood, 1, rules.StoneToolsWoodInput); }
                    else if (PrimitiveCraftOn && building.Recipe == RecipeKind.Jerky) Add(ResourceKind.Food, 0, rules.JerkyFoodInput);
                    break;
                case BuildingKind.BrickKiln:
                    if (BricksAllowed(building.FactionId)) { Add(ResourceKind.Stone, 0, CraftInputFor(building, rules.BrickStoneInput)); Add(ResourceKind.Charcoal, 1, CraftInputFor(building, rules.BrickCharcoalInput)); }
                    break;
                case BuildingKind.Scriptorium:
                    if (BooksAllowed(building.FactionId)) { Add(ResourceKind.Gold, 0, CraftInputFor(building, rules.BookGoldInput)); Add(ResourceKind.Wood, 1, CraftInputFor(building, rules.BookWoodInput)); }
                    break;
                case BuildingKind.DryingRack:
                    if (PreservesAllowed(building.FactionId)) Add(ResourceKind.Food, 0, CraftInputFor(building, rules.PreserveFoodInput));
                    break;
                case BuildingKind.Lapidary:
                    if (JewelsAllowed(building.FactionId)) { Add(ResourceKind.Stone, 0, CraftInputFor(building, rules.JewelStoneInput)); Add(ResourceKind.Ore, 1, CraftInputFor(building, rules.JewelOreInput)); }
                    break;
                case BuildingKind.Weaver:
                    if (TradeGoodsAllowed(building.FactionId)) Add(ResourceKind.Wood, 0, CraftInputFor(building, rules.TradeGoodsWoodInput));
                    break;
                case BuildingKind.IncenseHouse:
                    if (IncenseAllowed(building.FactionId)) { Add(ResourceKind.Wood, 0, CraftInputFor(building, rules.IncenseWoodInput)); Add(ResourceKind.Food, 1, CraftInputFor(building, rules.IncenseFoodInput)); }
                    break;
                case BuildingKind.CharmWorkshop:
                    if (CharmsAllowed(building.FactionId)) Add(ResourceKind.Wood, 0, CraftInputFor(building, rules.CharmWoodInput));
                    break;
                case BuildingKind.RepairYard:
                    if (RepairKitsAllowed(building.FactionId)) Add(ResourceKind.Wood, 0, CraftInputFor(building, rules.RepairKitWoodInput));
                    break;
                case BuildingKind.ToolShop:
                    if (ToolsAllowed(building.FactionId)) Add(ResourceKind.Wood, 0, CraftInputFor(building, rules.ToolWoodInput));
                    break;
                case BuildingKind.Assembler:
                    if (!AssemblerAllowed(building.FactionId) || building.Recipe == RecipeKind.None) break;
                    if (building.Recipe == RecipeKind.Frames)
                    {
                        Add(ResourceKind.Planks, 0, CraftInputFor(building, rules.CraftTreeJoineryPlankInput));
                        ResourceKind secondary = building.InputSecondaryKind == ResourceKind.Bricks ? ResourceKind.Bricks : ResourceKind.CutStone;
                        Add(secondary, 1, CraftInputFor(building, rules.CraftTreeJoineryCutStoneInput));
                        if (CraftDepthOn) Add(ResourceKind.Rope, 2, rules.CraftDepthFramesRopeInput);
                    }
                    else if (building.Recipe == RecipeKind.ArmsKit)
                    {
                        Add(ResourceKind.Planks, 0, CraftInputFor(building, rules.ArmsKitPlankInput));
                        ResourceKind secondary = building.InputSecondaryKind != 0 ? building.InputSecondaryKind
                            : world.Economies[building.FactionId - 1].Steel >= rules.ArmsKitSteelInput ? ResourceKind.Steel
                            : CraftDepthOn && world.Economies[building.FactionId - 1].Fittings > 0 ? ResourceKind.Fittings : ResourceKind.Charcoal;
                        Add(secondary, 1, CraftInputFor(building, secondary == ResourceKind.Steel ? rules.ArmsKitSteelInput : secondary == ResourceKind.Fittings ? 1 : rules.ArmsKitCharcoalInput));
                        if (secondary == ResourceKind.Fittings) Add(ResourceKind.Charcoal, 2, 1);
                    }
                    else if (building.Recipe == RecipeKind.Scholarly)
                    {
                        Add(ResourceKind.Planks, 0, CraftInputFor(building, rules.ScholarlyPlankInput));
                        Add(ResourceKind.Charcoal, 1, CraftInputFor(building, rules.ScholarlyCharcoalInput));
                        Add(ResourceKind.Food, 2, CraftInputFor(building, rules.ScholarlyFoodInput));
                    }
                    else if (building.Recipe == RecipeKind.SiegeParts)
                    {
                        if (CraftDepthOn)
                        { Add(ResourceKind.Frames, 0, CraftInputFor(building, rules.SiegePartsFramesInput)); Add(ResourceKind.Fittings, 1, rules.CraftDepthSiegePartsFittingsInput); Add(ResourceKind.Rope, 2, rules.CraftDepthSiegePartsRopeInput); }
                        else { Add(ResourceKind.Frames, 0, CraftInputFor(building, rules.SiegePartsFramesInput)); Add(ResourceKind.ArmsKit, 1, CraftInputFor(building, rules.SiegePartsArmsKitInput)); }
                    }
                    else if (building.Recipe == RecipeKind.Mechanism)
                    {
                        if (CraftDepthOn)
                        { Add(ResourceKind.Fittings, 0, rules.CraftDepthMechanismFittingsInput); Add(ResourceKind.Scholarly, 1, CraftInputFor(building, rules.MechanismScholarlyInput)); Add(ResourceKind.CutStone, 2, CraftInputFor(building, rules.MechanismCutStoneInput)); }
                        else { Add(ResourceKind.ArmsKit, 0, CraftInputFor(building, rules.MechanismArmsKitInput)); Add(ResourceKind.Scholarly, 1, CraftInputFor(building, rules.MechanismScholarlyInput)); Add(ResourceKind.CutStone, 2, CraftInputFor(building, rules.MechanismCutStoneInput)); }
                    }
                    else if (building.Recipe == RecipeKind.SiegeEngine)
                    { Add(ResourceKind.SiegeParts, 0, CraftInputFor(building, rules.SiegeEngineSiegePartsInput)); Add(ResourceKind.Mechanism, 1, CraftInputFor(building, rules.SiegeEngineMechanismInput)); }
                    else if (building.Recipe == RecipeKind.EliteKit)
                    { Add(ResourceKind.ArmsKit, 0, CraftInputFor(building, rules.EliteKitArmsKitInput)); Add(ResourceKind.Mechanism, 1, CraftInputFor(building, rules.EliteKitMechanismInput)); Add(ResourceKind.Metal, 2, CraftInputFor(building, rules.EliteKitMetalInput)); }
                    break;
            }
            return count;
        }

        private static int MaterialSlot(BuildingState building, byte slot)
            => slot == 0 ? building.Input : slot == 1 ? building.InputSecondary : building.InputTertiary;

        private bool CarriedLineSuppliesMaterial(uint faction, uint buildingId, ResourceKind material)
        {
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                var line = world.ProcessingLines[i];
                if (line.FactionId != faction || line.Manager != LineManager.Automatic) continue;
                if (line.SawmillId == buildingId && line.LumberCampId != 0 && material == ResourceKind.Wood) return true;
                if (line.SmelterId == buildingId && line.MineId != 0 && material == ResourceKind.Ore) return true;
                if (line.StonecutterId == buildingId && (line.DrillId != 0 || line.QuarryId != 0) && material == ResourceKind.Stone) return true;
                if (line.JoineryId == buildingId)
                {
                    if (line.SawmillId != 0 && material == ResourceKind.Planks) return true;
                    if (line.StonecutterId != 0 && material == ResourceKind.CutStone) return true;
                }
                if (line.BrickKilnId == buildingId && line.QuarryId != 0 && material == ResourceKind.Stone) return true;
                if (line.ScriptoriumId == buildingId && material == ResourceKind.Gold && line.BeltCells.Length > 0) return true;
                if (line.MillId == buildingId && material == ResourceKind.Food && line.BeltCells.Length > 0) return true;
            }
            return false;
        }

        private int FindCarriedSupplyRoute(uint buildingId, ResourceKind material)
        {
            for (int i = 0; i < world.CarriedSupplyRoutes.Length; i++)
                if (world.CarriedSupplyRoutes[i].BuildingId == buildingId && world.CarriedSupplyRoutes[i].Material == material) return i;
            return -1;
        }

        private void PruneCarriedSupplyRoutes(uint faction)
        {
            if (!CarriedInputsOn) return;
            for (int i = world.CarriedSupplyRoutes.Length - 1; i >= 0; i--)
            {
                var route = world.CarriedSupplyRoutes[i];
                bool valid = route.BuildingId > 0 && route.BuildingId <= world.BuildingCount;
                if (valid)
                {
                    var building = world.Buildings[route.BuildingId - 1];
                    if (building.FactionId != faction) continue;
                    valid = IsActualBuilding(building) && building.Complete
                        && IsAutomaticProcessingBuilding(faction, route.BuildingId) && !CarriedLineSuppliesMaterial(faction, route.BuildingId, route.Material);
                }
                if (valid) continue;
                RemoveCarriedSupplyRoute(i);
            }
        }

        private void RemoveCarriedSupplyRoute(int index)
        {
            int last = world.CarriedSupplyRoutes.Length - 1;
            for (int i = index; i < last; i++) world.CarriedSupplyRoutes[i] = world.CarriedSupplyRoutes[i + 1];
            Array.Resize(ref world.CarriedSupplyRoutes, last);
        }

        private uint CarriedOutletIdForCell(int cell, uint faction)
        {
            if (FeedsOwnCore(cell, faction)) return 0;
            uint best = 0;
            BigInteger bestDistance = default;
            bool found = false;
            var point = world.Map.Center(cell);
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var town = world.Buildings[i];
                if (!IsActualBuilding(town) || !town.Complete || town.FactionId != faction || town.Kind != BuildingKind.Town) continue;
                if (!InRange(point, BuildingCenter(town), world.Config.Rules.CoreRadius)) continue;
                var distance = DistanceSquared(point, BuildingCenter(town));
                if (!found || distance < bestDistance || distance == bestDistance && town.Id < best)
                { found = true; best = town.Id; bestDistance = distance; }
            }
            return best;
        }

        private bool TryLayCarriedSupplyRoute(uint faction, BuildingState building, ResourceKind material, bool[] taken)
        {
            if (!AiInserterPartsOn) return false;
            int start = -1; Facing startFacing = Facing.North; BigInteger bestDistance = default; bool found = false;
            var target = BuildingCenter(building);
            for (int cell = 0; cell < world.Belts.Length; cell++)
            {
                if (!PortIsOpen(cell, faction) || taken[cell]) continue;
                for (int sideIndex = 0; sideIndex < Sides.Length; sideIndex++)
                {
                    Facing facing = Sides[sideIndex];
                    int rear = BeltPrevious(cell, facing);
                    if (rear < 0 || !(FeedsOwnCore(rear, faction) || FeedsCompletedTown(rear, faction)) || FeedsOwnCore(cell, faction) || FeedsCompletedTown(cell, faction)) continue;
                    var distance = DistanceSquared(world.Map.Center(cell), target);
                    if (!found || distance < bestDistance || distance == bestDistance && (cell < start || cell == start && sideIndex < Array.IndexOf(Sides, startFacing)))
                    { found = true; start = cell; startFacing = facing; bestDistance = distance; }
                }
            }
            if (!found) return false;
            var route = BeltRoute(faction, start, taken, next => InFootprint(building, next));
            if (route.cells == null) return false;
            bool whole = LayRoutes(faction, AiReason.ProcessingBelt, (int)material, route);
            if (!whole) return false;
            int componentCell = -1; BeltComponentKind component = BeltComponentKind.FilterInserter;
            var first = world.Belts[route.cells[0]];
            if (first.Component == BeltComponentKind.None && first.Item == 0) componentCell = route.cells[0];
            else if (route.cells.Length > 1 && route.cells[1] == BeltNext(route.cells[0], route.facings[0]))
            {
                var second = world.Belts[route.cells[1]];
                if (second.Component == BeltComponentKind.None && second.Item == 0) { componentCell = route.cells[1]; component = BeltComponentKind.LongInserter; }
            }
            if (componentCell < 0 || !LayAutomaticInserterPart(faction, componentCell, world.Belts[componentCell].Facing, material, component)) return false;
            Array.Resize(ref world.CarriedSupplyRoutes, world.CarriedSupplyRoutes.Length + 1);
            world.CarriedSupplyRoutes[world.CarriedSupplyRoutes.Length - 1] = new CarriedSupplyRouteState
            {
                BuildingId = building.Id, OutletId = CarriedOutletIdForCell(start, faction), Material = material,
                BeltCells = route.cells, BeltFacings = route.facings
            };
            return true;
        }

        private void DecideCarriedSupplyRoutes(uint faction)
        {
            if (!CarriedInputsOn || faction < 1 || faction > 2 || !InsertersOn) return;
            PruneCarriedSupplyRoutes(faction);
            var taken = TakenForOtherLines(faction, -1);
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (!IsActualBuilding(building) || !building.Complete || building.FactionId != faction || !IsAiProcessingBuilding(building.Kind)
                    || !IsAutomaticProcessingBuilding(faction, building.Id)) continue;
                int count = CarriedMaterialRequirements(building, carriedMaterialWorkspace);
                for (int m = 0; m < count; m++)
                {
                    var requirement = carriedMaterialWorkspace[m];
                    if (CarriedLineSuppliesMaterial(faction, building.Id, requirement.Material)) { SetCarriedMaterialHauler(faction, building.Id, requirement.Material, 0); continue; }
                    if (MaterialSlot(building, requirement.Slot) >= world.Config.Economy.BufferLimit / 2) { SetCarriedMaterialHauler(faction, building.Id, requirement.Material, 0); continue; }
                    if (FindCarriedSupplyRoute(building.Id, requirement.Material) >= 0) { SetCarriedMaterialHauler(faction, building.Id, requirement.Material, 0); continue; }
                    if (TryLayCarriedSupplyRoute(faction, building, requirement.Material, taken))
                    {
                        foreach (int cell in world.CarriedSupplyRoutes[world.CarriedSupplyRoutes.Length - 1].BeltCells) if (cell >= 0 && cell < taken.Length) taken[cell] = true;
                        SetCarriedMaterialHauler(faction, building.Id, requirement.Material, 0);
                        return;
                    }
                    SetCarriedMaterialHauler(faction, building.Id, requirement.Material, 1);
                    return;
                }
            }
        }

        internal int CarriedSupplyRouteCount(uint faction)
        {
            if (!CarriedInputsOn) return 0;
            int count = 0;
            foreach (var route in world.CarriedSupplyRoutes)
                if (route.BuildingId > 0 && route.BuildingId <= world.BuildingCount && world.Buildings[route.BuildingId - 1].FactionId == faction) count++;
            return count;
        }

        internal int CarriedMaterialHaulerCount(uint faction)
        {
            if (!CarriedInputsOn) return 0;
            int count = 0;
            for (int i = 0; i < world.VillagerCount; i++) if (world.Villagers[i].Alive && world.Villagers[i].FactionId == faction && IsCarriedMaterialHaul(world.Villagers[i])) count++;
            return count;
        }

        internal int CarriedMaterialWaitingBuildingCount(uint faction)
        {
            if (!CarriedInputsOn) return 0;
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (!IsActualBuilding(building) || !building.Complete || building.FactionId != faction || !IsAiProcessingBuilding(building.Kind)
                    || !IsAutomaticProcessingBuilding(faction, building.Id) || building.Timer != 0) continue;
                int requirements = CarriedMaterialRequirements(building, carriedMaterialWorkspace);
                bool missing = false;
                for (int m = 0; m < requirements; m++)
                    if (!CarriedLineSuppliesMaterial(faction, building.Id, carriedMaterialWorkspace[m].Material)
                        && MaterialSlot(building, carriedMaterialWorkspace[m].Slot) < carriedMaterialWorkspace[m].Amount) { missing = true; break; }
                if (missing) count++;
            }
            return count;
        }
    }
}
