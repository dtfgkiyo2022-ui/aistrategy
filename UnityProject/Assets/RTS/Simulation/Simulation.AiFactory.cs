using System;
using System.Numerics;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>V3-37.8: deterministic factory templates and deliberately slow barracks expansion.</summary>
    public sealed partial class Simulation
    {
        private bool[] aiFactoryTakenWorkspace, aiFactorySeenWorkspace;
        private int[] aiFactoryRouteFromWorkspace, aiFactoryRouteQueueWorkspace;
        private int[] aiMergeSeenWorkspace, aiMergeQueueWorkspace, aiMergeDepthWorkspace;
        private readonly long[] aiFactoryNextDecisionTick = { 0, 0 };
        private const int AiFactoryDecisionIntervalTicks = 100;

        private void EnsureAiFactoryWorkspace()
        {
            int cells = world.Belts.Length;
            if (aiFactoryTakenWorkspace == null || aiFactoryTakenWorkspace.Length != cells)
            {
                aiFactoryTakenWorkspace = new bool[cells];
                aiFactorySeenWorkspace = new bool[cells];
                aiFactoryRouteFromWorkspace = new int[cells];
                aiFactoryRouteQueueWorkspace = new int[cells];
            }
        }

        private void EnsureAiMergeWorkspace(int cells)
        {
            if (aiMergeSeenWorkspace == null || aiMergeSeenWorkspace.Length < cells)
            {
                aiMergeSeenWorkspace = new int[cells];
                aiMergeQueueWorkspace = new int[cells];
                aiMergeDepthWorkspace = new int[cells];
            }
        }

        private void DecideAiFactoryPrimitive(uint faction)
        {
            if (!AiFactoryDecisionDue(faction)) return;
            // Each template is independent: an unavailable resource point or route skips only that template and
            // leaves the following template eligible on the same decision pass.
            if (FreeTownsOn && !AiExpandHasCompletedTown(faction)) return;
            EnsureAiFactoryWoodLine(faction);
            EnsureAiFactoryStoneLine(faction);
            EnsureAiFactoryPrimitiveCraftLine(faction);
        }

        private bool AiFactoryDecisionDue(uint faction)
        {
            if (faction < 1 || faction > 2 || world.Tick < aiFactoryNextDecisionTick[faction - 1]) return false;
            aiFactoryNextDecisionTick[faction - 1] = checked(world.Tick + AiFactoryDecisionIntervalTicks);
            return true;
        }

        /// <summary>V3-37.8: one civilisation-specific processing chain in age 1.</summary>
        private void DecideAiFactoryAge1(uint faction)
        {
            if (!AiFactoryDecisionDue(faction)) return;
            if (AiExpandOn)
            {
                if (!FreeTownsOn || AiExpandHasCompletedTown(faction))
                {
                    EnsureAiFactoryWoodLine(faction);
                    EnsureAiFactoryStoneLine(faction);
                    EnsureAiFactoryMetalLine(faction);
                }
            }
            switch (world.Economies[faction - 1].Civ)
            {
                case CivKind.Agrarian: DecideRations(faction); break;
                case CivKind.Metallurgy: DecideMetalwork(faction); break;
                case CivKind.Forestry: DecideLumberCamp(faction); break;
                case CivKind.Masonry: DecideBricks(faction); break;
                case CivKind.Caravan: DecideTradeGoods(faction); break;
                case CivKind.Cavalry: DecideTack(faction); break;
                case CivKind.Bridge: DecidePlanks(faction); break;
                case CivKind.Academy: DecideBooks(faction); break;
                case CivKind.Cult: DecideIncense(faction); break;
                case CivKind.Fishing: DecidePreserves(faction); break;
                case CivKind.Mountain: DecideJewels(faction); break;
                case CivKind.Tollgate: DecideRepairKits(faction); break;
                case CivKind.Metropolis: DecideCharms(faction); break;
                case CivKind.Sanctuary: DecideBooks(faction); break;
                default: DecidePlanks(faction); break;
            }
        }

        /// <summary>V3-37.8: merge the common plank and cut-stone exits into the building-material line.</summary>
        private void DecideAiFactoryAge2(uint faction)
        {
            if (!AiFactoryDecisionDue(faction)) return;
            if (FreeTownsOn && AiExpandOn && !AiExpandHasCompletedTown(faction)) return;
            EnsureAiFactoryWoodLine(faction);
            EnsureAiFactoryStoneLine(faction);
            EnsureAiFactoryMetalLine(faction);
            EnsureAiFactoryBuildingMaterialLine(faction);
        }

        private void EnsureAiFactoryMetalLine(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            var rules = world.Config.Economy;
            int index = GetOrCreateLine(faction, ProcessingLineKind.CoreMetal);
            ref var line = ref world.ProcessingLines[index];
            if (BuildingReady(line.MineId) && BuildingReady(line.SmelterId)
                && AiExpandProcessorNeedsRemoteBase(faction, ResourceKind.Ore, world.Buildings[line.SmelterId - 1]))
            {
                if (economy.Wood < rules.MineWoodCost) return;
                uint remoteMine = PlaceMine(faction, AiExpandResourceCell(faction, ResourceKind.Ore));
                if (remoteMine != 0) { line.MineId = remoteMine; line.SmelterId = 0; line.BeltCells = Array.Empty<int>(); }
                return;
            }
            if (!BuildingReady(line.MineId))
            {
                if (BuildingPending(line.MineId) || economy.Wood < rules.MineWoodCost) return;
                uint id = PlaceMine(faction, AiExpandOn ? AiExpandResourceCell(faction, ResourceKind.Ore) : -1);
                if (id == 0) { RecordAiFactorySkip(faction, 0, 1); return; }
                line.MineId = id;
                return;
            }
            if (!BuildingReady(line.SmelterId))
            {
                if (BuildingPending(line.SmelterId) || economy.Wood < rules.SmelterWoodCost) return;
                uint id = PlaceSmelter(faction, world.Buildings[line.MineId - 1]);
                if (id == 0) { RecordAiFactorySkip(faction, 0, 1); return; }
                line.SmelterId = id;
                return;
            }
            var mine = world.Buildings[line.MineId - 1];
            var smelter = world.Buildings[line.SmelterId - 1];
            if (!mine.Complete || !smelter.Complete) return;
            bool whole = LayCoreLineIfDue(faction, index, mine, smelter);
            SetLineHaulers(faction, line, whole ? 0 : HaulersForAiFactory);
            if (!whole) RecordAiFactorySkip(faction, 0, 2);
        }

        private const int HaulersForAiFactory = 1;

        private int AiExpandBaseCellForSource(uint faction, SimPoint source)
        {
            int best = world.Map.Cell(OwnCore(faction).Definition.Position);
            BigInteger bestDistance = DistanceSquared(source, OwnCore(faction).Definition.Position);
            uint bestId = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var town = world.Buildings[i];
                if (!IsActualBuilding(town) || !town.Complete || town.FactionId != faction || town.Kind != BuildingKind.Town) continue;
                var centre = BuildingCenter(town);
                var distance = DistanceSquared(source, centre);
                if (distance < bestDistance || distance == bestDistance && town.Id < bestId)
                { best = world.Map.Cell(centre); bestDistance = distance; bestId = town.Id; }
            }
            return best;
        }

        private int AiExpandResourceCell(uint faction, ResourceKind kind)
        {
            int preferredTown = AiExpandPreferredTownCell(faction);
            int best = -1;
            BigInteger bestDistance = default;
            uint bestId = 0;
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                var node = world.Nodes[i];
                if (!ResourceKnownToFaction(faction, i) || node.Remaining <= 0 || node.Definition.Kind != kind) continue;
                var distance = preferredTown >= 0
                    ? DistanceSquared(node.Definition.Position, world.Map.Center(preferredTown))
                    : DistanceSquared(node.Definition.Position, world.Map.Center(AiExpandBaseCellForSource(faction, node.Definition.Position)));
                if (best < 0 || distance < bestDistance || distance == bestDistance && node.Definition.Id < bestId)
                { best = i; bestDistance = distance; bestId = node.Definition.Id; }
            }
            return best < 0 ? -1 : world.Map.Cell(world.Nodes[best].Definition.Position);
        }

        private int AiExpandPreferredTownCell(uint faction)
        {
            int best = -1;
            uint bestId = uint.MaxValue;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var town = world.Buildings[i];
                if (!IsActualBuilding(town) || !town.Complete || town.FactionId != faction || town.Kind != BuildingKind.Town) continue;
                if (town.Id < bestId) { best = world.Map.Cell(BuildingCenter(town)); bestId = town.Id; }
            }
            return best;
        }

        private int AiExpandResourceBaseCell(uint faction, ResourceKind kind)
        {
            int resourceCell = AiExpandResourceCell(faction, kind);
            return resourceCell < 0 ? world.Map.Cell(OwnCore(faction).Definition.Position)
                : AiExpandBaseCellForSource(faction, world.Map.Center(resourceCell));
        }

        private bool AiExpandHasCompletedTown(uint faction)
        {
            for (int i = 0; i < world.BuildingCount; i++)
                if (IsActualBuilding(world.Buildings[i]) && world.Buildings[i].Complete
                    && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == BuildingKind.Town) return true;
            return false;
        }

        private bool AiExpandProcessorNeedsRemoteBase(uint faction, ResourceKind kind, BuildingState processor)
        {
            if (!AiExpandOn || !AiExpandHasCompletedTown(faction)) return false;
            int baseCell = AiExpandResourceBaseCell(faction, kind);
            return !InRange(BuildingCenter(processor), world.Map.Center(baseCell), Fix64.FromInt(world.Config.Economy.TownResourceReach));
        }

        /// <summary>The base an output belt ends at depends only on the processor, never on the cell being looked at, so it
        /// is chosen once per route search instead of scanning every building for each cell the search reaches.</summary>
        private Func<int, bool> AiExpandBaseFeed(uint faction, SimPoint source)
        {
            if (!AiExpandOn) return next => FeedsOwnCore(next, faction);
            int baseCell = AiExpandBaseCellForSource(faction, source);
            int coreCell = world.Map.Cell(OwnCore(faction).Definition.Position);
            if (baseCell == coreCell) return next => FeedsOwnCore(next, faction);
            var baseCentre = world.Map.Center(baseCell);
            return next => InRange(world.Map.Center(next), baseCentre, world.Config.Rules.CoreRadius);
        }

        private int AiExpandBaseCellForBuilding(uint faction, BuildingState building)
            => AiExpandBaseCellForSource(faction, BuildingCenter(building));

        private void EnsureAiFactoryBuildingMaterialLine(uint faction)
        {
            int plankIndex = LineIndex(faction, ProcessingLineKind.Planks);
            int stoneIndex = LineIndex(faction, ProcessingLineKind.CutStone);
            if (plankIndex < 0 || stoneIndex < 0) return;
            ref var plankLine = ref world.ProcessingLines[plankIndex];
            ref var stoneLine = ref world.ProcessingLines[stoneIndex];
            if (!BuildingReady(plankLine.SawmillId) || !BuildingReady(stoneLine.StonecutterId)) return;
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            int index = GetOrCreateLine(faction, ProcessingLineKind.Frames);
            ref var line = ref world.ProcessingLines[index];
            if (!BuildingReady(line.JoineryId))
            {
                if (BuildingPending(line.JoineryId) || economy.Wood < rules.JoineryWoodCost) return;
                int baseCell = AiExpandOn ? AiExpandBaseCellForBuilding(faction, world.Buildings[plankLine.SawmillId - 1]) : -1;
                uint id = PlaceJoinery(faction, baseCell);
                if (id == 0) { RecordAiFactorySkip(faction, 0, 1); return; }
                line.JoineryId = id;
                return;
            }
            var sawmill = world.Buildings[plankLine.SawmillId - 1];
            var stonecutter = world.Buildings[stoneLine.StonecutterId - 1];
            var joinery = world.Buildings[line.JoineryId - 1];
            if (!sawmill.Complete || !stonecutter.Complete || !joinery.Complete) return;
            bool wasWhole = FreeTownsOn && LineBeltsWhole(index);
            if (AiFactoryRouteDue(faction, index))
            {
                var taken = TakenForOtherLines(faction, index);
                var planks = BeltRoute(faction, OutputCell(sawmill), taken, next => InFootprint(joinery, next));
                if (planks.cells == null) { RecordAiFactorySkip(faction, 0, 2); CompleteFreeTownsRouteRecheck(faction, index, wasWhole); return; }
                foreach (int cell in planks.cells) taken[cell] = true;
                var stone = BeltRoute(faction, OutputCell(stonecutter), taken, next => InFootprint(joinery, next));
                if (stone.cells == null) { RecordAiFactorySkip(faction, 0, 2); CompleteFreeTownsRouteRecheck(faction, index, wasWhole); return; }
                foreach (int cell in stone.cells) taken[cell] = true;
                var core = BeltRoute(faction, OutputCell(joinery), taken, AiExpandBaseFeed(faction, BuildingCenter(joinery)));
                if (core.cells == null) { RecordAiFactorySkip(faction, 0, 2); CompleteFreeTownsRouteRecheck(faction, index, wasWhole); return; }
                SetLineBelts(index, planks, stone, core);
                if (!LayRoutes(faction, planks, stone, core)) RecordAiFactorySkip(faction, 0, 2);
            }
            CompleteFreeTownsRouteRecheck(faction, index, wasWhole);
            SetSimpleHaulers(faction, sawmill.Id, joinery.Id, 1);
            SetSimpleHaulers(faction, stonecutter.Id, joinery.Id, 1);
            SetSimpleHaulers(faction, joinery.Id, 0, LineBeltsWhole(index) ? 0 : 1);
        }

        /// <summary>V3-37.8: the third-age siege-parts assembler sends its output to the core.</summary>
        private void DecideAiFactoryAge3(uint faction)
        {
            if (!AiFactoryDecisionDue(faction)) return;
            int index = LineIndex(faction, ProcessingLineKind.Assembler);
            if (index < 0) return;
            ref var line = ref world.ProcessingLines[index];
            if (!BuildingReady(line.AssemblerId)) return;
            SetAssemblerRecipe(faction, line.AssemblerId, RecipeKind.SiegeParts);
            var assembler = world.Buildings[line.AssemblerId - 1];
            if (!assembler.Complete || !AiFactoryRouteDue(faction, index)) return;
            bool wasWhole = FreeTownsOn && LineBeltsWhole(index);
            var route = BeltRoute(faction, OutputCell(assembler), TakenForOtherLines(faction, index), AiExpandBaseFeed(faction, BuildingCenter(assembler)));
            if (route.cells == null) { RecordAiFactorySkip(faction, 0, 2); CompleteFreeTownsRouteRecheck(faction, index, wasWhole); return; }
            SetLineBelts(index, route);
            if (!LayRoutes(faction, route)) RecordAiFactorySkip(faction, 0, 2);
            CompleteFreeTownsRouteRecheck(faction, index, wasWhole);
        }

        private void RecordAiFactorySkip(uint faction, int unknownResource, int reason)
        {
            if (!AiFactoryOn || faction < 1 || faction > 2) return;
            ref var economy = ref world.Economies[faction - 1];
            if (unknownResource != 0) economy.AiFactorySkippedUnknownResource++;
            else if (reason == 1) economy.AiFactorySkippedNoSite++;
            else economy.AiFactorySkippedNoRoute++;
        }

        private void EnsureAiFactoryWoodLine(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            var rules = world.Config.Economy;
            int index = GetOrCreateLine(faction, ProcessingLineKind.Planks);
            ref var line = ref world.ProcessingLines[index];
            if (BuildingReady(line.LumberCampId) && BuildingReady(line.SawmillId)
                && AiExpandProcessorNeedsRemoteBase(faction, ResourceKind.Wood, world.Buildings[line.SawmillId - 1]))
            {
                if (economy.Wood < rules.LumberCampWoodCost) return;
                uint remoteCamp = PlaceLumberCamp(faction, AiExpandResourceCell(faction, ResourceKind.Wood));
                if (remoteCamp != 0) { line.LumberCampId = remoteCamp; line.SawmillId = 0; line.BeltCells = Array.Empty<int>(); }
                return;
            }
            if (!BuildingReady(line.LumberCampId))
            {
                if (BuildingPending(line.LumberCampId) || economy.Wood < rules.LumberCampWoodCost) return;
                line.LumberCampId = PlaceLumberCamp(faction, AiExpandOn ? AiExpandResourceCell(faction, ResourceKind.Wood) : -1);
                return;
            }
            if (!BuildingReady(line.SawmillId))
            {
                if (BuildingPending(line.SawmillId) || economy.Wood < rules.SawmillWoodCost) return;
                var camp = world.Buildings[line.LumberCampId - 1];
                int baseCell = AiExpandOn ? AiExpandBaseCellForBuilding(faction, camp) : -1;
                line.SawmillId = PlaceSawmill(faction, camp, baseCell);
                return;
            }
            if (!line.LumberCampId.Equals(0u) && !line.SawmillId.Equals(0u))
            {
                var camp = world.Buildings[line.LumberCampId - 1];
                var sawmill = world.Buildings[line.SawmillId - 1];
                if (camp.Complete && sawmill.Complete && AiFactoryRouteDue(faction, index)) LayPlanksLine(faction, index, camp, sawmill);
                if (LineBeltsWhole(index))
                {
                    SetSimpleHaulers(faction, camp.Id, sawmill.Id, LineBeltsWhole(index) ? 0 : 1);
                    SetSimpleHaulers(faction, sawmill.Id, 0, LineBeltsWhole(index) ? 0 : 1);
                }
            }
        }

        private void EnsureAiFactoryStoneLine(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            var rules = world.Config.Economy;
            int index = GetOrCreateLine(faction, ProcessingLineKind.CutStone);
            ref var line = ref world.ProcessingLines[index];
            if (!BuildingReady(line.DrillId))
            {
                if (BuildingPending(line.DrillId) || economy.Wood < rules.DrillWoodCost) return;
                line.DrillId = PlaceDrill(faction, AiExpandOn ? AiExpandResourceCell(faction, ResourceKind.Stone) : -1);
                return;
            }
            if (!BuildingReady(line.StonecutterId))
            {
                if (BuildingPending(line.StonecutterId) || economy.Wood < rules.StonecutterWoodCost) return;
                int baseCell = AiExpandOn ? AiExpandBaseCellForBuilding(faction, world.Buildings[line.DrillId - 1]) : -1;
                line.StonecutterId = PlaceStonecutter(faction, baseCell);
                return;
            }
            var drill = world.Buildings[line.DrillId - 1];
            var stonecutter = world.Buildings[line.StonecutterId - 1];
            if (!drill.Complete || !stonecutter.Complete) return;
            bool wasWhole = FreeTownsOn && LineBeltsWhole(index);
            if (AiFactoryRouteDue(faction, index))
            {
                var taken = TakenForOtherLines(faction, index);
                var toStonecutter = BeltRoute(faction, OutputCell(drill), taken, next => InFootprint(stonecutter, next));
                if (toStonecutter.cells != null)
                {
                    foreach (int cell in toStonecutter.cells) taken[cell] = true;
                    var toCore = BeltRoute(faction, OutputCell(stonecutter), taken, AiExpandBaseFeed(faction, BuildingCenter(stonecutter)));
                    if (toCore.cells != null)
                    {
                        SetLineBelts(index, toStonecutter, toCore);
                        LayRoutes(faction, toStonecutter, toCore);
                    }
                }
            }
            CompleteFreeTownsRouteRecheck(faction, index, wasWhole);
            if (LineBeltsWhole(index))
            {
                SetSimpleHaulers(faction, drill.Id, stonecutter.Id, LineBeltsWhole(index) ? 0 : 1);
                SetSimpleHaulers(faction, stonecutter.Id, 0, LineBeltsWhole(index) ? 0 : 1);
            }
            else
            {
                // A route failure is local to this template; hand hauling is the specified fallback.
                SetSimpleHaulers(faction, drill.Id, stonecutter.Id, 1);
                SetSimpleHaulers(faction, stonecutter.Id, 0, 1);
            }
        }

        private void EnsureAiFactoryPrimitiveCraftLine(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            var rules = world.Config.Economy;
            int index = GetOrCreateLine(faction, ProcessingLineKind.PrimitiveCraft);
            ref var line = ref world.ProcessingLines[index];
            if (!BuildingReady(line.HutId))
            {
                if (BuildingPending(line.HutId) || economy.Wood < rules.HutWoodCost) return;
                line.HutId = PlaceHut(faction);
                return;
            }
            ref var hut = ref world.Buildings[line.HutId - 1];
            if (!hut.Complete) return;
            if (hut.Recipe == RecipeKind.None) SetBuildingRecipe(faction, hut.Id, RecipeKind.StoneTools);
            bool wasWhole = FreeTownsOn && LineBeltsWhole(index);
            if (AiFactoryRouteDue(faction, index))
            {
                var route = BeltRoute(faction, OutputCell(hut), TakenForOtherLines(faction, index), next => FeedsOwnCore(next, faction));
                if (route.cells != null)
                {
                    SetLineBelts(index, route);
                    LayRoutes(faction, route);
                }
            }
            CompleteFreeTownsRouteRecheck(faction, index, wasWhole);
            if (LineBeltsWhole(index)) SetSimpleHaulers(faction, hut.Id, 0, 1);
            else SetSimpleHaulers(faction, hut.Id, 0, 1);
        }

        private void UpdateAiFactoryBarracksIdle()
        {
            int window = Math.Max(1, world.Config.Economy.AiFactoryBarracksIdleWindowTicks);
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var barracks = ref world.Buildings[i];
                if (!IsActualBuilding(barracks) || barracks.Kind != BuildingKind.Barracks) continue;
                if (world.Tick - barracks.AiFactoryIdleWindowStartTick >= window)
                {
                    barracks.AiFactoryIdleWindowStartTick = world.Tick;
                    barracks.AiFactoryIdleTicks = 0;
                }
                if (AiFactoryBarracksResourceWaiting(barracks.FactionId, barracks)) barracks.AiFactoryIdleTicks++;
            }
        }

        private bool AiFactoryBarracksResourceWaiting(uint faction, BuildingState barracks)
        {
            if (!barracks.Complete || barracks.Held || barracks.Queued >= world.Config.Economy.QueueLimit) return false;
            return HasRoomFor(faction, UnitKind.Infantry) && !CanPay(faction, UnitKind.Infantry, BuildingKind.Barracks);
        }

        private bool AiFactoryShouldBuildBarracks(uint faction, int count)
        {
            var rules = world.Config.Economy;
            var economy = world.Economies[faction - 1];
            int age = economy.Age;
            int cap = age <= 0 ? rules.AiFactoryPrimitiveBarracksMaxBuildings
                : age == 1 ? rules.AiFactoryAge1BarracksMaxBuildings
                : age == 2 ? rules.AiFactoryAge2BarracksMaxBuildings
                : age == 3 ? rules.AiFactoryAge3BarracksMaxBuildings : rules.AiFactoryAge4BarracksMaxBuildings;
            if (count >= cap || economy.Wood < rules.BarracksWoodCost) return false;
            if (count == 0) return true;
            int window = Math.Max(1, rules.AiFactoryBarracksIdleWindowTicks);
            int limit = Math.Max(0, rules.AiFactoryBarracksIdlePermille);
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!IsActualBuilding(b) || b.FactionId != faction || b.Kind != BuildingKind.Barracks || !b.Complete) continue;
                if ((long)b.AiFactoryIdleTicks * 1000 >= (long)window * limit) return false;
            }
            return AiFactoryTemplatesComplete(faction, age);
        }

        private bool AiFactoryTemplatesComplete(uint faction, int age)
        {
            int required = age <= 0 ? world.Config.Economy.AiFactoryPrimitiveLineCount
                : age == 1 ? world.Config.Economy.AiFactoryAge1LineCount
                : age == 2 ? world.Config.Economy.AiFactoryAge2LineCount : world.Config.Economy.AiFactoryAge3LineCount;
            int complete = 0;
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                var line = world.ProcessingLines[i];
                if (line.FactionId != faction || !LineBeltsWhole(i)) continue;
                if (age <= 0 && (line.Kind == ProcessingLineKind.Planks || line.Kind == ProcessingLineKind.CutStone || line.Kind == ProcessingLineKind.PrimitiveCraft)) complete++;
                else if (age > 0 && line.Kind != ProcessingLineKind.PrimitiveCraft) complete++;
            }
            return complete >= required;
        }
    }
}
