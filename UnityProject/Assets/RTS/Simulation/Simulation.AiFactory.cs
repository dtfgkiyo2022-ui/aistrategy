using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>V3-37.8: deterministic factory templates and deliberately slow barracks expansion.</summary>
    public sealed partial class Simulation
    {
        private bool[] aiFactoryTakenWorkspace, aiFactorySeenWorkspace;
        private int[] aiFactoryRouteFromWorkspace, aiFactoryRouteQueueWorkspace;

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

        private void DecideAiFactoryPrimitive(uint faction)
        {
            // Each template is independent: an unavailable resource point or route skips only that template and
            // leaves the following template eligible on the same decision pass.
            EnsureAiFactoryWoodLine(faction);
            EnsureAiFactoryStoneLine(faction);
            EnsureAiFactoryPrimitiveCraftLine(faction);
        }

        private void EnsureAiFactoryWoodLine(uint faction)
        {
            ref var economy = ref world.Economies[faction - 1];
            var rules = world.Config.Economy;
            int index = GetOrCreateLine(faction, ProcessingLineKind.Planks);
            ref var line = ref world.ProcessingLines[index];
            if (!BuildingReady(line.LumberCampId))
            {
                if (BuildingPending(line.LumberCampId) || economy.Wood < rules.LumberCampWoodCost) return;
                line.LumberCampId = PlaceLumberCamp(faction);
                return;
            }
            if (!BuildingReady(line.SawmillId))
            {
                if (BuildingPending(line.SawmillId) || economy.Wood < rules.SawmillWoodCost) return;
                line.SawmillId = PlaceSawmill(faction, world.Buildings[line.LumberCampId - 1]);
                return;
            }
            if (!line.LumberCampId.Equals(0u) && !line.SawmillId.Equals(0u))
            {
                var camp = world.Buildings[line.LumberCampId - 1];
                var sawmill = world.Buildings[line.SawmillId - 1];
                if (camp.Complete && sawmill.Complete && !LineBeltsWhole(index)) LayPlanksLine(faction, index, camp, sawmill);
                if (LineBeltsWhole(index))
                {
                    SetSimpleHaulers(faction, camp.Id, sawmill.Id, 1);
                    SetSimpleHaulers(faction, sawmill.Id, 0, 1);
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
                line.DrillId = PlaceDrill(faction);
                return;
            }
            if (!BuildingReady(line.StonecutterId))
            {
                if (BuildingPending(line.StonecutterId) || economy.Wood < rules.StonecutterWoodCost) return;
                line.StonecutterId = PlaceStonecutter(faction);
                return;
            }
            var drill = world.Buildings[line.DrillId - 1];
            var stonecutter = world.Buildings[line.StonecutterId - 1];
            if (!drill.Complete || !stonecutter.Complete) return;
            if (!LineBeltsWhole(index))
            {
                var taken = TakenForOtherLines(faction, index);
                var toStonecutter = BeltRoute(faction, OutputCell(drill), taken, next => InFootprint(stonecutter, next));
                if (toStonecutter.cells != null)
                {
                    foreach (int cell in toStonecutter.cells) taken[cell] = true;
                    var toCore = BeltRoute(faction, OutputCell(stonecutter), taken, next => FeedsOwnCore(next, faction));
                    if (toCore.cells != null)
                    {
                        SetLineBelts(index, toStonecutter, toCore);
                        LayRoutes(faction, toStonecutter, toCore);
                    }
                }
            }
            if (LineBeltsWhole(index))
            {
                SetSimpleHaulers(faction, drill.Id, stonecutter.Id, 1);
                SetSimpleHaulers(faction, stonecutter.Id, 0, 1);
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
            if (!LineBeltsWhole(index))
            {
                var route = BeltRoute(faction, OutputCell(hut), TakenForOtherLines(faction, index), next => FeedsOwnCore(next, faction));
                if (route.cells != null)
                {
                    SetLineBelts(index, route);
                    LayRoutes(faction, route);
                }
            }
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
