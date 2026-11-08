using System;
using System.Collections.Generic;
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
        private readonly CarriedMaterialRequirement[] carriedViewWorkspace = new CarriedMaterialRequirement[3];
        private const long CarriedSupplyRetryTicks = 600;

        private sealed class CarriedSupplyDiagnostics
        {
            internal int UnaffordableSkips;
            internal int FailedRoutes;
        }

        private readonly System.Collections.Generic.List<(uint BuildingId, ResourceKind Material)> carriedStarvedWorkspace = new System.Collections.Generic.List<(uint, ResourceKind)>();
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
                case BuildingKind.BlastFurnace:
                    if (ByproductsOn)
                    { Add(ResourceKind.Ore, 0, rules.BlastOreInput); Add(ResourceKind.Charcoal, 1, rules.BlastCharcoalInput); }
                    break;
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
                case BuildingKind.GlassWorks:
                    if (GlassworksOn) { Add(ResourceKind.Sand, 0, rules.GlassSandInput); Add(ResourceKind.Charcoal, 1, rules.GlassCharcoalInput); }
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
                    if (JoineryAllowed(building.FactionId)) { Add(ResourceKind.Planks, 0, CraftInputFor(building, rules.CraftTreeJoineryPlankInput)); Add(ResourceKind.CutStone, 1, CraftInputFor(building, rules.CraftTreeJoineryCutStoneInput)); if (CivGoodsUsageOn(building.FactionId)) Add(ResourceKind.Bricks, 2, 1); }
                    break;
                case BuildingKind.Hut:
                    if (PrimitiveCraftOn && building.Recipe == RecipeKind.StoneTools)
                    { Add(ResourceKind.Stone, 0, rules.StoneToolsStoneInput); Add(ResourceKind.Wood, 1, rules.StoneToolsWoodInput); }
                    else if (PrimitiveCraftOn && building.Recipe == RecipeKind.Jerky) Add(ResourceKind.Food, 0, rules.JerkyFoodInput);
                    break;
                case BuildingKind.BrickKiln:
                    if (BricksAllowed(building.FactionId)) { Add(building.InputKind == ResourceKind.Slag ? ResourceKind.Slag : ResourceKind.Stone, 0, CraftInputFor(building, building.InputKind == ResourceKind.Slag ? rules.BrickSlagInput : rules.BrickStoneInput)); Add(ResourceKind.Charcoal, 1, CraftInputFor(building, rules.BrickCharcoalInput)); }
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
                        Add(ResourceKind.CutStone, 1, CraftInputFor(building, rules.CraftTreeJoineryCutStoneInput));
                        if (CivGoodsUsageOn(building.FactionId)) Add(ResourceKind.Bricks, 2, 1);
                        else if (CraftDepthOn) Add(ResourceKind.Rope, 2, rules.CraftDepthFramesRopeInput);
                    }
                    else if (building.Recipe == RecipeKind.ArmsKit)
                    {
                        Add(ResourceKind.Planks, 0, CraftInputFor(building, rules.ArmsKitPlankInput));
                        ResourceKind secondary = building.InputSecondaryKind != 0 ? building.InputSecondaryKind
                            : world.Economies[building.FactionId - 1].Steel >= rules.ArmsKitSteelInput ? ResourceKind.Steel
                            : CraftDepthOn && world.Economies[building.FactionId - 1].Fittings > 0 ? ResourceKind.Fittings : ResourceKind.Charcoal;
                        Add(secondary, 1, CraftInputFor(building, secondary == ResourceKind.Steel ? rules.ArmsKitSteelInput : secondary == ResourceKind.Fittings ? 1 : rules.ArmsKitCharcoalInput));
                        if (CivGoodsUsageOn(building.FactionId)) Add(ResourceKind.Tack, 2, 1);
                        else if (secondary == ResourceKind.Fittings) Add(ResourceKind.Charcoal, 2, 1);
                    }
                    else if (building.Recipe == RecipeKind.Lens)
                    {
                        if (GlassworksOn) { Add(ResourceKind.Glass, 0, rules.LensGlassInput); Add(ResourceKind.Gears, 1, rules.LensGearsInput); }
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

        /// <summary>組立所が 1 つ作り終えたとき：材料の表の 1 回分だけを置き場から減らす。2 つ目の置き場が空になったら種類を決め直せるようにする。</summary>
        private void ConsumeCarriedCraftInputs(ref BuildingState building)
        {
            int count = CarriedMaterialRequirements(building, carriedMaterialWorkspace);
            for (int m = 0; m < count; m++)
            {
                var requirement = carriedMaterialWorkspace[m];
                if (requirement.Slot == 0) building.Input -= Math.Min(requirement.Amount, building.Input);
                else if (requirement.Slot == 1) building.InputSecondary -= Math.Min(requirement.Amount, building.InputSecondary);
                else building.InputTertiary -= Math.Min(requirement.Amount, building.InputTertiary);
            }
            if (building.InputSecondary == 0) building.InputSecondaryKind = 0;
        }

        private static int MaterialSlot(BuildingState building, byte slot)
            => slot == 0 ? building.Input : slot == 1 ? building.InputSecondary : building.InputTertiary;

        /// <summary>
        /// お任せの取り出し口で在庫に残す数。原料（建物・兵の支払いに使う）は今までどおり AiInserterCoreReserve、
        /// 板はベルトの分だけ、ほかの中間の品は残さない（100 残すと板・木炭・鋼がほぼ取り出せず武具が 0 だった）。
        /// </summary>
        private int CarriedOutletReserve(uint faction, ResourceKind kind)
        {
            var rules = world.Config.Economy;
            if (kind == ResourceKind.Food || kind == ResourceKind.Wood || kind == ResourceKind.Stone
                || kind == ResourceKind.Gold || kind == ResourceKind.Ore) return rules.AiInserterCoreReserve;
            if (kind != ResourceKind.Planks) return 0;
            // 木の届かない製材所があるあいだは、そのベルトの板を先に貯める（組立所が板を使い切り、製材所のベルトが払えず板が細いままだった）
            return CarriedSawmillNeedsRoute(faction) ? rules.CarriedPlankReserve + rules.CarriedSawmillRoutePlanks : rules.CarriedPlankReserve;
        }

        private bool CarriedSawmillNeedsRoute(uint faction)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (building.Kind != BuildingKind.Sawmill || !IsCarriedSupplyTarget(building, faction)
                    || building.Input >= world.Config.Economy.BufferLimit / 2) continue;
                int route = FindCarriedSupplyRoute(building.Id, ResourceKind.Wood);
                if (route >= 0 && !world.CarriedSupplyRoutes[route].Failed) continue;
                if (CarriedLineSuppliesMaterial(faction, building.Id, ResourceKind.Wood)) continue;
                return true;
            }
            return false;
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
                if (line.SmelterId == buildingId && line.MineId != 0 && ByproductsOn && material == ResourceKind.Ore
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
            // 遠い建物にはベルトを引かず運び役にする（長いベルトが板の大半を使い、組立所に板が回らなかった）
            if (cells > rules.CarriedBeltMaxCells) return false;
            bool craftRequired = CraftRequiredOn && economy.Age >= 1;
            int planks = craftRequired ? (cells + 1) / 2 : 0;
            int wood = craftRequired ? 0 : cells * rules.BeltWoodCost;
            int reserve = AgeFourAiWoodReserve(faction);
            // 始まりの絞り込み投入機の分も先に見る（歯車が無いのにベルトだけ敷いて板を使い切っていた）
            // 歯車は見ない：無ければ普通の投入機（歯車なし）を置く
            int fittings = CraftDepthOn && economy.Age >= 2 ? 1 : 0;
            return economy.Wood - reserve >= wood + rules.InserterWoodCost && economy.Planks >= planks
                && economy.Fittings >= fittings;
        }

        private bool TryLayCarriedSupplyRoute(uint faction, BuildingState building, ResourceKind material, bool[] taken,
            out uint outletId)
        {
            outletId = 0;
            if (!AiInserterPartsOn || !TryFindCarriedSupplyStart(faction, building, taken, out int start, out Facing startFacing, out outletId)) return false;
            var route = BeltRoute(faction, start, taken, next => InFootprint(building, next));
            if (route.cells == null) return false;
            // 向きの違う自分のベルトを通る道は、LayRoutes が向きを変えないのでつながらない。何も敷かずに失敗にする
            // （核のそばの荷下ろしのベルトなどを通り、新しく敷いた分の板が毎回消えていた。空いたマスだけに限ると始まりが遠すぎて道がほぼ引けない）
            if (CarriedRouteCrossesOwnBelt(faction, route.cells, route.facings)) return false;
            // LayRoutes は新しく 1 マスでも敷くと false を返す（もとから全部あったかの印）。敷いたあとのマスで確かめる
            // （返り値を失敗と読み、敷いたベルトを捨てて 600 tick 後に別の道を敷き直し、板がベルトに消えていた）
            LayRoutes(faction, AiReason.ProcessingBelt, (int)material, route);
            if (!CarriedRouteLaid(faction, route.cells, route.facings)) return false;
            int componentCell = -1; BeltComponentKind component = BeltComponentKind.FilterInserter;
            var first = world.Belts[route.cells[0]];
            if (first.Component == BeltComponentKind.None && first.Item == 0) componentCell = route.cells[0];
            else if (route.cells.Length > 1 && route.cells[1] == BeltNext(route.cells[0], route.facings[0]))
            {
                var second = world.Belts[route.cells[1]];
                if (second.Component == BeltComponentKind.None && second.Item == 0) { componentCell = route.cells[1]; component = BeltComponentKind.LongInserter; }
            }
            if (componentCell < 0) return false;
            if (!LayAutomaticInserterPart(faction, componentCell, world.Belts[componentCell].Facing, material, component)
                && (component != BeltComponentKind.FilterInserter || !LayCarriedPlainInserter(faction, componentCell, material))) return false;
            Array.Resize(ref world.CarriedSupplyRoutes, world.CarriedSupplyRoutes.Length + 1);
            world.CarriedSupplyRoutes[world.CarriedSupplyRoutes.Length - 1] = new CarriedSupplyRouteState
            {
                BuildingId = building.Id, OutletId = outletId, Material = material,
                BeltCells = route.cells, BeltFacings = route.facings
            };
            return true;
        }

        private bool CarriedRouteCrossesOwnBelt(uint faction, int[] cells, Facing[] facings)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                int cell = cells[i];
                if (cell < 0 || cell >= world.Belts.Length) return true;
                var belt = world.Belts[cell];
                if (belt.FactionId == faction && belt.Component == BeltComponentKind.None && belt.Facing != facings[i]) return true;
            }
            return false;
        }

        private bool CarriedRouteLaid(uint faction, int[] cells, Facing[] facings)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                int cell = cells[i];
                if (cell < 0 || cell >= world.Belts.Length) return false;
                var belt = world.Belts[cell];
                if (belt.FactionId != faction) return false;
                if (belt.Component == BeltComponentKind.None && belt.Facing != facings[i]) return false;
            }
            return cells.Length != 0;
        }

        /// <summary>
        /// 歯車が足りず絞り込み投入機を置けないときの始まりの部品：選んだ種類を出す普通の投入機（お任せの製材所の木の投入機と同じ置き方・値段）。
        /// 歯車の少ない陣営が道を 1 本も引けず、運び役だけになっていた。
        /// </summary>
        private bool LayCarriedPlainInserter(uint faction, int cell, ResourceKind material)
        {
            ref var belt = ref world.Belts[cell];
            if (belt.FactionId != faction || belt.Held || belt.Component != BeltComponentKind.None || belt.Item != 0) return false;
            ref var economy = ref world.Economies[faction - 1];
            var rules = world.Config.Economy;
            bool fittings = CraftDepthOn && economy.Age >= 2;
            if (economy.Wood < rules.InserterWoodCost || fittings && economy.Fittings < 1) return false;
            belt.Component = BeltComponentKind.Inserter; belt.SorterKind = material; belt.Progress = 0;
            economy.Wood -= rules.InserterWoodCost;
            RecordLargeUse(faction, ResourceKind.Wood, LargeEconomyUseKind.Belt, rules.InserterWoodCost);
            if (fittings) { economy.Fittings--; economy.FittingsConsumed = checked(economy.FittingsConsumed + 1); }
            RecordAiBeltPartPlaced(faction, BeltComponentKind.Inserter);
            InvalidateBeltCaches();
            return true;
        }

        /// <summary>運び役の上限に当たるので、板（製材所）→ 武具など（組立所・その木炭の炭焼き窯）→ そのほか の順に扱う。</summary>
        private static int CarriedSupplyPass(BuildingState building)
            => building.Kind == BuildingKind.Sawmill ? 0
                : building.Kind == BuildingKind.Assembler && building.Recipe == RecipeKind.Lens ? 0
                : building.Kind == BuildingKind.Assembler || building.Kind == BuildingKind.CharcoalKiln || building.Kind == BuildingKind.GlassWorks ? 1 : 2;

        private void DecideCarriedSupplyRoutes(uint faction)
        {
            if (!CarriedInputsOn || faction < 1 || faction > 2 || !InsertersOn) return;
            PruneCarriedSupplyRoutes(faction);
            var taken = TakenForOtherLines(faction, -1);
            carriedStarvedWorkspace.Clear();
            bool routeLaid = false;
            for (int pass = 0; pass < 3; pass++)
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (!IsCarriedSupplyTarget(building, faction)
                    || CarriedSupplyPass(building) != pass) continue;
                int count = CarriedMaterialRequirements(building, carriedMaterialWorkspace);
                for (int m = 0; m < count; m++)
                {
                    var requirement = carriedMaterialWorkspace[m];
                    if (CarriedLineSuppliesMaterial(faction, building.Id, requirement.Material)) { SetCarriedMaterialHauler(faction, building.Id, requirement.Material, 0); continue; }
                    // 置き場が半分たまっているあいだは運び役を足さないが、外しもしない（外すと、減ったときに別の村人が取り出し口から
                    // 歩き直し、そのあいだに材料が切れていた）。道を引く試みは続ける（飛ばすと運び役で足りている建物に道が引かれなくなった）
                    bool stocked = MaterialSlot(building, requirement.Slot) >= world.Config.Economy.BufferLimit / 2;
                    // 材料が足りずに止まっている建物は、全部の建物に 1 人ずつ付けたあとで運び役を増やす（下の 2 段目）
                    // （1 人では往復のあいだに使い切り、在庫から運ぶ建物が半分の時間止まっていた）
                    if (building.Timer == 0 && MaterialSlot(building, requirement.Slot) < requirement.Amount)
                        carriedStarvedWorkspace.Add((building.Id, requirement.Material));
                    int existing = FindCarriedSupplyRoute(building.Id, requirement.Material);
                    if (existing >= 0)
                    {
                        var waiting = world.CarriedSupplyRoutes[existing];
                        if (!waiting.Failed || world.Tick < waiting.NextAttemptTick)
                        {
                            if (waiting.Failed) KeepOrAddCarriedHauler(faction, building.Id, requirement.Material, stocked);
                            else SetCarriedMaterialHauler(faction, building.Id, requirement.Material, 0);
                            continue;
                        }
                        RemoveCarriedSupplyRoute(existing);
                    }
                    if (routeLaid || !AiInserterPartsOn) { KeepOrAddCarriedHauler(faction, building.Id, requirement.Material, stocked); continue; }
                    if (!TryFindCarriedSupplyStart(faction, building, taken, out int start, out _, out uint outletId))
                    { RecordCarriedSupplyFailure(building.Id, requirement.Material, 0); KeepOrAddCarriedHauler(faction, building.Id, requirement.Material, stocked); continue; }
                    if (!CanAffordCarriedSupplyRoute(faction, start, building))
                    {
                        carriedSupplyDiagnostics[faction - 1].UnaffordableSkips++;
                        KeepOrAddCarriedHauler(faction, building.Id, requirement.Material, stocked); continue;
                    }
                    if (TryLayCarriedSupplyRoute(faction, building, requirement.Material, taken, out outletId))
                    {
                        foreach (int cell in world.CarriedSupplyRoutes[world.CarriedSupplyRoutes.Length - 1].BeltCells) if (cell >= 0 && cell < taken.Length) taken[cell] = true;
                        SetCarriedMaterialHauler(faction, building.Id, requirement.Material, 0);
                        routeLaid = true;
                        continue;
                    }
                    RecordCarriedSupplyFailure(building.Id, requirement.Material, outletId);
                    KeepOrAddCarriedHauler(faction, building.Id, requirement.Material, stocked);
                }
            }
            // 2 段目：運び役の上限に余りがあれば、材料が足りずに止まっている建物に運び役を足す（ほかの建物の運び役は奪わない）
            foreach (var (buildingId, material) in carriedStarvedWorkspace)
                if (FindWorkingCarriedSupplyRoute(buildingId, material) < 0 && CarriedHaulersFor(faction, buildingId, material) > 0)
                    SetCarriedMaterialHauler(faction, buildingId, material, world.Config.Economy.CarriedStarvedHaulers);
        }

        /// <summary>道で届かない組の運び役：置き場がたまっていれば今のまま、足りなければ 1 人にする。</summary>
        private void KeepOrAddCarriedHauler(uint faction, uint buildingId, ResourceKind material, bool stocked)
        {
            if (!stocked) SetCarriedMaterialHauler(faction, buildingId, material, 1);
        }

        /// <summary>
        /// 38.25 その4（表示だけ）：材料の表の順で 1 回分に足りない最初の材料と、それをどう届けているか。
        /// 人が置いた建物にも出す（運び役・道は付かないので「運ぶ手段なし」になる）。
        /// </summary>
        private void CarriedSupplyView(BuildingState building, out ResourceKind missing, out MaterialSupplyView supply)
        {
            missing = 0; supply = MaterialSupplyView.None;
            if (!CarriedInputsOn || !IsActualBuilding(building) || !building.Complete) return;
            uint faction = building.FactionId;
            if (faction < 1 || faction > 2) return;
            int count = CarriedMaterialRequirements(building, carriedViewWorkspace);
            for (int m = 0; m < count; m++)
            {
                var requirement = carriedViewWorkspace[m];
                if (MaterialSlot(building, requirement.Slot) >= requirement.Amount) continue;
                missing = requirement.Material;
                int route = FindCarriedSupplyRoute(building.Id, requirement.Material);
                if (CarriedLineSuppliesMaterial(faction, building.Id, requirement.Material)) supply = MaterialSupplyView.Line;
                else if (route >= 0 && !world.CarriedSupplyRoutes[route].Failed) supply = MaterialSupplyView.Belt;
                else if (CarriedHaulersFor(faction, building.Id, requirement.Material) > 0) supply = MaterialSupplyView.Hauler;
                else if (route >= 0) supply = MaterialSupplyView.Waiting;
                else supply = MaterialSupplyView.NoSupply;
                return;
            }
        }

        /// <summary>38.25 その4（表示だけ）：供給の道のマス → その道が届ける建物。旗オフでは null。</summary>
        private Dictionary<int, uint> CarriedSupplyBuildingsByCell()
        {
            if (!CarriedInputsOn) return null;
            var result = new Dictionary<int, uint>();
            foreach (var route in world.CarriedSupplyRoutes)
                if (!route.Failed)
                    foreach (int cell in route.BeltCells) result[cell] = route.BuildingId;
            return result;
        }

        private int CarriedHaulersFor(uint faction, uint buildingId, ResourceKind material)
        {
            int count = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (v.Alive && v.FactionId == faction && !v.Held && v.HaulFrom == 0 && v.HaulTo == buildingId && v.HaulKind == material) count++;
            }
            return count;
        }

        private int FindWorkingCarriedSupplyRoute(uint buildingId, ResourceKind material)
        {
            int index = FindCarriedSupplyRoute(buildingId, material);
            return index >= 0 && !world.CarriedSupplyRoutes[index].Failed ? index : -1;
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
