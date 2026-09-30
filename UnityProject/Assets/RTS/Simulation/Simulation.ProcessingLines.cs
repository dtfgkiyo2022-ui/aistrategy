using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private int LineIndex(uint faction, ProcessingLineKind kind)
        {
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                var line = world.ProcessingLines[i];
                if (line.FactionId == faction && line.Kind == kind) return i;
            }
            return -1;
        }

        private int CreateLine(uint faction, ProcessingLineKind kind)
        {
            int index = world.ProcessingLines.Length;
            Array.Resize(ref world.ProcessingLines, index + 1);
            world.ProcessingLines[index] = new ProcessingLineState
            {
                Id = world.NextProcessingLineId++, FactionId = faction, Kind = kind,
                Manager = LineManager.Automatic, BeltCells = Array.Empty<int>(), BeltFacings = Array.Empty<Facing>()
            };
            return index;
        }

        private int GetOrCreateLine(uint faction, ProcessingLineKind kind)
        {
            int index = LineIndex(faction, kind);
            return index >= 0 ? index : CreateLine(faction, kind);
        }

        private bool IsAutoLine(int index)
            => index >= 0 && index < world.ProcessingLines.Length && world.ProcessingLines[index].Manager == LineManager.Automatic;

        private int LineForBuilding(uint faction, uint buildingId)
        {
            if (buildingId == 0) return -1;
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                var line = world.ProcessingLines[i];
                if (line.FactionId != faction) continue;
                if (line.MineId == buildingId || line.SmelterId == buildingId || line.KilnId == buildingId || line.SteelworksId == buildingId
                    || line.LumberCampId == buildingId || line.FletcherId == buildingId) return i;
            }
            return -1;
        }

        private void MarkLinesForBuilding(uint faction, uint buildingId)
        {
            int line = LineForBuilding(faction, buildingId);
            if (line >= 0) MarkLineManual(line);
        }

        private void MarkLinesForBelt(uint faction, int cell)
        {
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                var line = world.ProcessingLines[i];
                if (line.FactionId != faction) continue;
                for (int j = 0; j < line.BeltCells.Length; j++)
                    if (line.BeltCells[j] == cell) { MarkLineManual(i); break; }
            }
        }

        private void MarkLineManual(int index)
        {
            if (index < 0 || index >= world.ProcessingLines.Length) return;
            ref var line = ref world.ProcessingLines[index];
            if (line.Manager == LineManager.Manual) return;
            line.Manager = LineManager.Manual;
            SetLineHaulers(line.FactionId, line, 0);
        }

        private void ReturnLineToAuto(uint faction, uint lineId)
        {
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                ref var line = ref world.ProcessingLines[i];
                if (line.FactionId != faction || line.Id != lineId) continue;
                line.Manager = LineManager.Automatic;
                ClearLineHeld(faction, line);
                return;
            }
        }

        private void ClearLineHeld(uint faction, ProcessingLineState line)
        {
            uint[] ids = { line.MineId, line.SmelterId, line.KilnId, line.SteelworksId, line.LumberCampId, line.FletcherId };
            foreach (uint id in ids)
                if (id != 0 && id <= world.BuildingCount && world.Buildings[id - 1].FactionId == faction) world.Buildings[id - 1].Held = false;
            foreach (int cell in line.BeltCells)
                if (cell >= 0 && cell < world.Belts.Length && world.Belts[cell].FactionId == faction) world.Belts[cell].Held = false;
        }

        private bool LineBuildingsReady(ProcessingLineState line)
        {
            if (line.Kind == ProcessingLineKind.CoreMetal)
                return BuildingReady(line.MineId) && BuildingReady(line.SmelterId);
            if (line.Kind == ProcessingLineKind.CoreWood)
                return BuildingReady(line.LumberCampId);
            if (line.Kind == ProcessingLineKind.BowGear)
                return BuildingReady(line.LumberCampId) && BuildingReady(line.FletcherId);
            return BuildingReady(line.MineId) && BuildingReady(line.SmelterId) && BuildingReady(line.KilnId) && BuildingReady(line.SteelworksId);
        }

        private bool BuildingReady(uint id)
            => id != 0 && id <= world.BuildingCount && world.Buildings[id - 1].Alive && world.Buildings[id - 1].Complete;

        private bool BuildingPending(uint id)
            => id != 0 && id <= world.BuildingCount && world.Buildings[id - 1].Alive && !world.Buildings[id - 1].Complete;

        private void SetLineBuilding(int index, BuildingKind kind, uint id)
        {
            ref var line = ref world.ProcessingLines[index];
            if (kind == BuildingKind.Mine) line.MineId = id;
            else if (kind == BuildingKind.Smelter) line.SmelterId = id;
            else if (kind == BuildingKind.CharcoalKiln) line.KilnId = id;
            else if (kind == BuildingKind.Steelworks) line.SteelworksId = id;
            else if (kind == BuildingKind.LumberCamp) line.LumberCampId = id;
            else if (kind == BuildingKind.Fletcher) line.FletcherId = id;
        }

        private bool LineContainsBelt(int index, int cell)
        {
            if (index < 0 || index >= world.ProcessingLines.Length) return false;
            foreach (int c in world.ProcessingLines[index].BeltCells) if (c == cell) return true;
            return false;
        }

        private void SetLineBelts(int index, params (int[] cells, Facing[] facings)[] routes)
        {
            var cells = new List<int>();
            var facings = new List<Facing>();
            foreach (var route in routes)
                for (int i = 0; i < route.cells.Length; i++)
                {
                    if (cells.Contains(route.cells[i])) continue;
                    cells.Add(route.cells[i]); facings.Add(route.facings[i]);
                }
            ref var line = ref world.ProcessingLines[index];
            line.BeltCells = cells.ToArray(); line.BeltFacings = facings.ToArray();
        }

        private bool LineBeltsWhole(int index)
        {
            var line = world.ProcessingLines[index];
            for (int i = 0; i < line.BeltCells.Length; i++)
            {
                int cell = line.BeltCells[i];
                if (cell < 0 || cell >= world.Belts.Length || world.Belts[cell].FactionId != line.FactionId || world.Belts[cell].Facing != line.BeltFacings[i]) return false;
            }
            return line.BeltCells.Length != 0;
        }

        private void SetLineHaulers(uint faction, ProcessingLineState line, int wanted)
        {
            if (line.Kind == ProcessingLineKind.CoreMetal)
            {
                if (line.MineId != 0 && line.SmelterId != 0) SetHaulers(faction, line.MineId, line.SmelterId, wanted);
                return;
            }
            if (line.Kind == ProcessingLineKind.CoreWood)
            {
                SetSimpleHaulers(faction, line.LumberCampId, 0, wanted);
                return;
            }
            if (line.Kind == ProcessingLineKind.BowGear)
            {
                SetSimpleHaulers(faction, line.LumberCampId, line.FletcherId, wanted);
                SetSimpleHaulers(faction, line.FletcherId, 0, wanted);
                return;
            }
            int current = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var v = ref world.Villagers[i];
                if (!v.Alive || v.FactionId != faction || v.Held || v.HaulTo != line.KilnId) continue;
                if (wanted == 0) StopHauling(ref v); else current++;
            }
            if (current >= wanted || line.KilnId == 0) return;
            var kiln = world.Buildings[line.KilnId - 1];
            var spot = world.Map.Center(kiln.WorkCell);
            while (current < wanted)
            {
                int best = -1;
                for (int i = 0; i < world.VillagerCount; i++)
                {
                    var v = world.Villagers[i];
                    if (!v.Alive || v.FactionId != faction || v.Held || v.HaulTo != 0 || v.HaulFrom != 0 || v.Carry > 0
                        || (v.Task != VillagerTask.Idle && v.Task != VillagerTask.ToNode && v.Task != VillagerTask.Gathering)) continue;
                    if (best < 0 || DistanceSquared(v.Position, spot) < DistanceSquared(world.Villagers[best].Position, spot)
                        || DistanceSquared(v.Position, spot) == DistanceSquared(world.Villagers[best].Position, spot) && v.Id < world.Villagers[best].Id) best = i;
                }
                if (best < 0) return;
                ref var chosen = ref world.Villagers[best];
                chosen.NodeId = 0; chosen.HaulFrom = 0; chosen.HaulTo = line.KilnId; chosen.HaulNodeId = 0;
                chosen.Task = chosen.Carry > 0 ? VillagerTask.ToDropOff : VillagerTask.Idle;
                AssignKilnWood(ref chosen, line.KilnId);
                current++;
            }
        }

        private void SetSimpleHaulers(uint faction, uint sourceId, uint destinationId, int wanted)
        {
            if (sourceId == 0) return;
            int current = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var v = ref world.Villagers[i];
                if (!v.Alive || v.FactionId != faction || v.Held || v.HaulFrom != sourceId) continue;
                if (wanted == 0) StopHauling(ref v); else if (v.HaulTo == destinationId) current++;
            }
            if (current >= wanted) return;
            var spot = world.Map.Center(world.Buildings[sourceId - 1].WorkCell);
            while (current < wanted)
            {
                int best = -1;
                for (int i = 0; i < world.VillagerCount; i++)
                {
                    var v = world.Villagers[i];
                    if (!v.Alive || v.FactionId != faction || v.Held || v.HaulFrom != 0 || v.HaulTo != 0 || v.Carry > 0
                        || (v.Task != VillagerTask.Idle && v.Task != VillagerTask.ToNode && v.Task != VillagerTask.Gathering)) continue;
                    if (best < 0 || DistanceSquared(v.Position, spot) < DistanceSquared(world.Villagers[best].Position, spot)
                        || DistanceSquared(v.Position, spot) == DistanceSquared(world.Villagers[best].Position, spot) && v.Id < world.Villagers[best].Id) best = i;
                }
                if (best < 0) return;
                ref var chosen = ref world.Villagers[best];
                chosen.NodeId = 0; chosen.HaulFrom = sourceId; chosen.HaulTo = destinationId; chosen.HaulNodeId = 0;
                chosen.Task = VillagerTask.ToPickup;
                current++;
            }
        }

        private IEnumerable<LineView> LineViews(uint faction)
        {
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                var line = world.ProcessingLines[i];
                if (line.FactionId == faction) yield return new LineView(line.Id, faction, line.Manager);
            }
        }
    }
}
