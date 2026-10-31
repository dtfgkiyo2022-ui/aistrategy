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
        private const long CarriedSupplyRetryTicks = 600;

        private sealed class CarriedSupplyDiagnostics
        {
            internal int UnaffordableSkips;
            internal int FailedRoutes;
        }

        private readonly CarriedSupplyDiagnostics[] carriedSupplyDiagnostics =
            { new CarriedSupplyDiagnostics(), new CarriedSupplyDiagnostics() };

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

        /// <summary>
        /// お任せの取り出し口で在庫に残す数。原料（建物・兵の支払いに使う）は今までどおり AiInserterCoreReserve、
        /// 板はベルトの分だけ、ほかの中間の品は残さない（100 残すと板・木炭・鋼がほぼ取り出せず武具が 0 だった）。
        /// </summary>
        private int CarriedOutletReserve(ResourceKind kind)
        {
            var rules = world.Config.Economy;
            if (kind == ResourceKind.Food || kind == ResourceKind.Wood || kind == ResourceKind.Stone
                || kind == ResourceKind.Gold || kind == ResourceKind.Ore) return rules.AiInserterCoreReserve;
            return kind == ResourceKind.Planks ? rules.CarriedPlankReserve : 0;
        }

        /// <summary>
        /// お任せが材料を届ける建物：人が持っていない自分の完成した建物で、材料の表に材料があるもの。
        /// 線の「加工の建物」1 つに絞ると、武具の流れで足した組立所・製材所に材料が届かず武具が 0 になった。
        /// </summary>
        private bool IsCarriedSupplyTarget(BuildingState building, uint faction)
            => IsActualBuilding(building) && building.Complete && building.FactionId == faction && !building.Held
               && !world.Economies[faction - 1].AutoOff && CarriedMaterialRequirements(building, carriedMaterialWorkspace) > 0;

        private bool CarriedLineSuppliesMaterial(uint faction, uint buildingId, ResourceKind material)
        {
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                var line = world.ProcessingLines[i];
                if (line.FactionId != faction || line.Manager != LineManager.Automatic) continue;
                if (!LineBeltsWhole(i)) continue;
                if (line.SawmillId == buildingId && line.LumberCampId != 0 && material == ResourceKind.Wood
                    && LineFeedsBuilding(line, line.LumberCampId, buildingId)) return true;
                if (line.SmelterId == buildingId && line.MineId != 0 && material == ResourceKind.Ore
                    && LineFeedsBuilding(line, line.MineId, buildingId)) return true;
                if (line.StonecutterId == buildingId && (line.DrillId != 0 || line.QuarryId != 0) && material == ResourceKind.Stone
                    && (line.DrillId != 0 && LineFeedsBuilding(line, line.DrillId, buildingId)
                        || line.QuarryId != 0 && LineFeedsBuilding(line, line.QuarryId, buildingId))) return true;
                if (line.JoineryId == buildingId)
                {
                    if (line.SawmillId != 0 && material == ResourceKind.Planks && LineFeedsBuilding(line, line.SawmillId, buildingId)) return true;
                    if (line.StonecutterId != 0 && material == ResourceKind.CutStone && LineFeedsBuilding(line, line.StonecutterId, buildingId)) return true;
                }
                if (line.BrickKilnId == buildingId && line.QuarryId != 0 && material == ResourceKind.Stone
                    && LineFeedsBuilding(line, line.QuarryId, buildingId)) return true;
                if (line.ScriptoriumId == buildingId && material == ResourceKind.Gold) return true;
                if (line.MillId == buildingId && material == ResourceKind.Food) return true;
            }
            return false;
        }

        private bool LineFeedsBuilding(ProcessingLineState line, uint sourceId, uint targetId)
        {
            if (sourceId == 0 || targetId == 0 || sourceId > world.BuildingCount || targetId > world.BuildingCount) return false;
            int cell = OutputCell(world.Buildings[sourceId - 1]);
            int max = line.BeltCells.Length + 1;
            for (int step = 0; step < max; step++)
            {
                if (cell < 0 || cell >= world.Belts.Length || Array.IndexOf(line.BeltCells, cell) < 0) return false;
                int next = BeltNext(cell, world.Belts[cell].Facing);
                if (next >= 0 && InFootprint(world.Buildings[targetId - 1], next)) return true;
                cell = next;
            }
            return false;
        }

        private int FindCarriedSupplyRoute(uint buildingId, ResourceKind material)
        {
            for (int i = 0; i < world.CarriedSupplyRoutes.Length; i++)
                if (world.CarriedSupplyRoutes[i].BuildingId == buildingId && world.CarriedSupplyRoutes[i].Material == material) return i;
            return -1;
        }

        private void RecordCarriedSupplyFailure(uint buildingId, ResourceKind material, uint outletId)
        {
            int existing = FindCarriedSupplyRoute(buildingId, material);
            if (existing >= 0)
            {
                world.CarriedSupplyRoutes[existing].Failed = true;
                world.CarriedSupplyRoutes[existing].NextAttemptTick = world.Tick + CarriedSupplyRetryTicks;
                return;
            }
            Array.Resize(ref world.CarriedSupplyRoutes, world.CarriedSupplyRoutes.Length + 1);
            world.CarriedSupplyRoutes[world.CarriedSupplyRoutes.Length - 1] = new CarriedSupplyRouteState
            {
                BuildingId = buildingId, OutletId = outletId, Material = material, Failed = true,
                NextAttemptTick = world.Tick + CarriedSupplyRetryTicks,
                BeltCells = Array.Empty<int>(), BeltFacings = Array.Empty<Facing>()
            };
            carriedSupplyDiagnostics[world.Buildings[buildingId - 1].FactionId - 1].FailedRoutes++;
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
                        && !building.Held && !world.Economies[faction - 1].AutoOff
                        && (route.Failed || !CarriedLineSuppliesMaterial(faction, route.BuildingId, route.Material));
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

        private static int ManhattanDistance(int a, int b, int width)
            => Math.Abs(a % width - b % width) + Math.Abs(a / width - b / width);

        private int ManhattanDistanceToBuilding(int cell, BuildingState building)
        {
            int width = world.Config.Map.WidthCells;
            int size = SizeOf(building.Kind);
            int x = cell % width, z = cell / width;
            int x0 = building.OriginCell % width, z0 = building.OriginCell / width;
            int dx = x < x0 ? x0 - x : x >= x0 + size ? x - (x0 + size - 1) : 0;
            int dz = z < z0 ? z0 - z : z >= z0 + size ? z - (z0 + size - 1) : 0;
            return dx + dz;
        }

        private bool TryFindCarriedSupplyStart(uint faction, BuildingState building, bool[] taken,
            out int start, out Facing startFacing, out uint outletId)
        {
            int foundStart = -1; Facing foundFacing = Facing.North; uint foundOutlet = 0;
            BigInteger bestDistance = default; bool found = false;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            void Consider(int cell)
            {
                if (cell < 0 || cell >= world.Belts.Length || !PortIsOpen(cell, faction) || taken[cell]) return;
                for (int sideIndex = 0; sideIndex < Sides.Length; sideIndex++)
                {
                    Facing facing = Sides[sideIndex];
                    int rear = BeltPrevious(cell, facing);
                    if (rear < 0 || !(FeedsOwnCore(rear, faction) || FeedsCompletedTown(rear, faction))
                        || FeedsOwnCore(cell, faction) || FeedsCompletedTown(cell, faction)) continue;
                    var distance = DistanceSquared(world.Map.Center(cell), BuildingCenter(building));
                    if (!found || distance < bestDistance || distance == bestDistance
                        && (cell < foundStart || cell == foundStart && sideIndex < Array.IndexOf(Sides, foundFacing)))
                    {
                        found = true; foundStart = cell; foundFacing = facing;
                        foundOutlet = CarriedOutletIdForCell(rear, faction); bestDistance = distance;
                    }
                }
            }
            void ConsiderOutlet(SimPoint point)
            {
                long size = Fix64.FromInt(world.Config.Map.CellSizeMeters).Raw;
                long radius = world.Config.Rules.CoreRadius.Raw;
                int x0 = (int)Math.Max(0, (point.X.Raw - radius) / size - 1);
                int x1 = (int)Math.Min(width - 1, (point.X.Raw + radius) / size + 1);
                int z0 = (int)Math.Max(0, (point.Z.Raw - radius) / size - 1);
                int z1 = (int)Math.Min(height - 1, (point.Z.Raw + radius) / size + 1);
                for (int x = x0; x <= x1; x++) { Consider(z0 * width + x); if (z1 != z0) Consider(z1 * width + x); }
                for (int z = z0 + 1; z < z1; z++) { Consider(z * width + x0); if (x1 != x0) Consider(z * width + x1); }
            }
            var core = OwnCore(faction);
            if (core.Hp > 0) ConsiderOutlet(core.Definition.Position);
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var town = world.Buildings[i];
                if (IsActualBuilding(town) && town.Complete && town.FactionId == faction && town.Kind == BuildingKind.Town)
                    ConsiderOutlet(BuildingCenter(town));
            }
            start = foundStart; startFacing = foundFacing; outletId = foundOutlet;
            return found;
        }

        private bool CanAffordCarriedSupplyRoute(uint faction, int start, BuildingState building)
        {
            var rules = world.Config.Economy;
            var economy = world.Economies[faction - 1];
            int cells = Math.Max(1, ManhattanDistanceToBuilding(start, building));
            bool craftRequired = CraftRequiredOn && economy.Age >= 1;
            int planks = craftRequired ? (cells + 1) / 2 : 0;
            int wood = craftRequired ? 0 : cells * rules.BeltWoodCost;
            int reserve = AgeFourAiWoodReserve(faction);
            // 始まりの絞り込み投入機の分も先に見る（歯車が無いのにベルトだけ敷いて板を使い切っていた）
            int fittings = CraftDepthOn && economy.Age >= 2 ? 1 : 0;
            return economy.Wood - reserve >= wood + rules.InserterWoodCost && economy.Planks >= planks
                && economy.Gears >= rules.FilterInserterGears && economy.Fittings >= fittings;
        }

        private bool TryLayCarriedSupplyRoute(uint faction, BuildingState building, ResourceKind material, bool[] taken,
            out uint outletId)
        {
            outletId = 0;
            if (!AiInserterPartsOn || !TryFindCarriedSupplyStart(faction, building, taken, out int start, out Facing startFacing, out outletId)) return false;
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
                BuildingId = building.Id, OutletId = outletId, Material = material,
                BeltCells = route.cells, BeltFacings = route.facings
            };
            return true;
        }

        private void DecideCarriedSupplyRoutes(uint faction)
        {
            if (!CarriedInputsOn || faction < 1 || faction > 2 || !InsertersOn) return;
            PruneCarriedSupplyRoutes(faction);
            var taken = TakenForOtherLines(faction, -1);
            bool routeLaid = false;
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (!IsCarriedSupplyTarget(building, faction)
                    || (pass == 0) != (building.Kind == BuildingKind.Sawmill)) continue;
                int count = CarriedMaterialRequirements(building, carriedMaterialWorkspace);
                for (int m = 0; m < count; m++)
                {
                    var requirement = carriedMaterialWorkspace[m];
                    if (CarriedLineSuppliesMaterial(faction, building.Id, requirement.Material)) { SetCarriedMaterialHauler(faction, building.Id, requirement.Material, 0); continue; }
                    if (MaterialSlot(building, requirement.Slot) >= world.Config.Economy.BufferLimit / 2) { SetCarriedMaterialHauler(faction, building.Id, requirement.Material, 0); continue; }
                    int existing = FindCarriedSupplyRoute(building.Id, requirement.Material);
                    if (existing >= 0)
                    {
                        var waiting = world.CarriedSupplyRoutes[existing];
                        if (!waiting.Failed || world.Tick < waiting.NextAttemptTick)
                        { SetCarriedMaterialHauler(faction, building.Id, requirement.Material, waiting.Failed ? 1 : 0); continue; }
                        RemoveCarriedSupplyRoute(existing);
                    }
                    if (routeLaid || !AiInserterPartsOn) { SetCarriedMaterialHauler(faction, building.Id, requirement.Material, 1); continue; }
                    if (!TryFindCarriedSupplyStart(faction, building, taken, out int start, out _, out uint outletId))
                    { RecordCarriedSupplyFailure(building.Id, requirement.Material, 0); SetCarriedMaterialHauler(faction, building.Id, requirement.Material, 1); continue; }
                    if (!CanAffordCarriedSupplyRoute(faction, start, building))
                    {
                        carriedSupplyDiagnostics[faction - 1].UnaffordableSkips++;
                        SetCarriedMaterialHauler(faction, building.Id, requirement.Material, 1); continue;
                    }
                    if (TryLayCarriedSupplyRoute(faction, building, requirement.Material, taken, out outletId))
                    {
                        foreach (int cell in world.CarriedSupplyRoutes[world.CarriedSupplyRoutes.Length - 1].BeltCells) if (cell >= 0 && cell < taken.Length) taken[cell] = true;
                        SetCarriedMaterialHauler(faction, building.Id, requirement.Material, 0);
                        routeLaid = true;
                        continue;
                    }
                    RecordCarriedSupplyFailure(building.Id, requirement.Material, outletId);
                    SetCarriedMaterialHauler(faction, building.Id, requirement.Material, 1);
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
                if (!IsCarriedSupplyTarget(building, faction) || building.Timer != 0) continue;
                int requirements = CarriedMaterialRequirements(building, carriedMaterialWorkspace);
                bool missing = false;
                for (int m = 0; m < requirements; m++)
                    if (!CarriedLineSuppliesMaterial(faction, building.Id, carriedMaterialWorkspace[m].Material)
                        && MaterialSlot(building, carriedMaterialWorkspace[m].Slot) < carriedMaterialWorkspace[m].Amount) { missing = true; break; }
                if (missing) count++;
            }
            return count;
        }

        internal int CarriedSupplyUnaffordableCount(uint faction)
            => CarriedInputsOn && faction >= 1 && faction <= 2 ? carriedSupplyDiagnostics[faction - 1].UnaffordableSkips : 0;

        internal int CarriedSupplyFailedCount(uint faction)
            => CarriedInputsOn && faction >= 1 && faction <= 2 ? carriedSupplyDiagnostics[faction - 1].FailedRoutes : 0;

        internal int CarriedPlanksProduced(uint faction)
            => CarriedInputsOn && faction >= 1 && faction <= 2 ? world.Economies[faction - 1].PlanksProduced : 0;
    }
}
