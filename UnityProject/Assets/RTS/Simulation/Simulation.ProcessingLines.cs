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

        private void RequestProcessingLine(uint faction, EconomyCommand command)
        {
            if ((byte)command.Line < (byte)ProcessingLineKind.CoreMetal || (byte)command.Line > (byte)ProcessingLineKind.Preserves)
            {
                RejectLineRequest(faction, command, ReasonCode.LineUnavailable, -1);
                return;
            }
            if (!LineRequestAllowed(faction, command.Line))
            {
                RejectLineRequest(faction, command, ReasonCode.LineUnavailable, -1);
                return;
            }
            int requestedCell;
            uint requestedRegion = 0;
            if (command.TargetKind == EconomyTargetKind.Region)
            {
                if (!ValidRegion(command.RegionId) || !world.Regions[command.RegionId - 1].HasCenter)
                {
                    RejectLineRequest(faction, command, ReasonCode.LineUnavailable, -1);
                    return;
                }
                requestedRegion = command.RegionId;
                requestedCell = world.Map.Cell(world.Regions[command.RegionId - 1].Center);
            }
            else
            {
                requestedCell = command.Cell;
                if (requestedCell < 0 || requestedCell >= world.Config.Map.WidthCells * world.Config.Map.HeightCells)
                {
                    RejectLineRequest(faction, command, ReasonCode.LineUnavailable, requestedCell);
                    return;
                }
            }
            if (command.Line != ProcessingLineKind.Tack && command.Line != ProcessingLineKind.Books && command.Line != ProcessingLineKind.Preserves && FindRequestedResourceNode(command.Line, requestedCell) < 0)
            {
                RejectLineRequest(faction, command, ReasonCode.LineResourceMissing, requestedCell);
                return;
            }
            int existing = LineIndex(faction, command.Line);
            if (existing >= 0)
            {
                var current = world.ProcessingLines[existing];
                if (current.Manager != LineManager.Automatic || current.MineId != 0 || current.LumberCampId != 0 || current.MillId != 0 || current.SawmillId != 0 || current.ScriptoriumId != 0 || current.DryingRackId != 0)
                {
                    RejectLineRequest(faction, command, ReasonCode.LineUnavailable, requestedCell);
                    return;
                }
            }
            ref var economy = ref world.Economies[faction - 1];
            if (economy.Wood < InitialLineWoodCost(faction, command.Line))
            {
                RejectLineRequest(faction, command, ReasonCode.LineWoodShortfall, requestedCell);
                return;
            }
            int index = existing >= 0 ? existing : CreateLine(faction, command.Line);
            ref var line = ref world.ProcessingLines[index];
            line.RequestPending = true;
            line.RequestedCell = requestedCell;
            line.RequestedRegionId = requestedRegion;
        }

        private bool LineRequestAllowed(uint faction, ProcessingLineKind kind)
        {
            if (kind == ProcessingLineKind.Bricks) return BricksAllowed(faction);
            if (kind == ProcessingLineKind.Rations) return RationsAllowed(faction);
            if (kind == ProcessingLineKind.Tack) return TackAllowed(faction);
            if (kind == ProcessingLineKind.Planks) return PlanksAllowed(faction);
            if (kind == ProcessingLineKind.Books) return BooksAllowed(faction);
            if (kind == ProcessingLineKind.Preserves) return PreservesAllowed(faction);
            if (kind == ProcessingLineKind.CoreWood || kind == ProcessingLineKind.BowGear)
                return ForestryAllowed(faction) && (kind != ProcessingLineKind.BowGear || world.Economies[faction - 1].Age >= 2);
            if (!IndustryOn || !ProcessingOn || !MetalworkAllowed(faction)) return false;
            return kind != ProcessingLineKind.Steel || ProcessingAvailable(faction);
        }

        private int InitialLineWoodCost(uint faction, ProcessingLineKind kind)
        {
            return kind == ProcessingLineKind.Bricks ? world.Config.Economy.BrickKilnWoodCost
                : kind == ProcessingLineKind.Tack ? world.Config.Economy.SaddleryWoodCost
                : kind == ProcessingLineKind.Planks ? world.Config.Economy.SawmillWoodCost
                : kind == ProcessingLineKind.Books ? world.Config.Economy.ScriptoriumWoodCost
                : kind == ProcessingLineKind.Preserves ? world.Config.Economy.DryingRackWoodCost
                : kind == ProcessingLineKind.Rations ? world.Config.Economy.MillWoodCost
                : kind == ProcessingLineKind.CoreWood || kind == ProcessingLineKind.BowGear
                ? world.Config.Economy.LumberCampWoodCost
                : world.Config.Economy.MineWoodCost;
        }

        private int FindRequestedResourceNode(ProcessingLineKind kind, int requestedCell)
        {
            ResourceKind resource = kind == ProcessingLineKind.Bricks ? ResourceKind.Stone
                : kind == ProcessingLineKind.Rations || kind == ProcessingLineKind.CoreWood || kind == ProcessingLineKind.BowGear || kind == ProcessingLineKind.Planks
                ? ResourceKind.Food : ResourceKind.Ore;
            if (kind == ProcessingLineKind.CoreWood || kind == ProcessingLineKind.BowGear || kind == ProcessingLineKind.Planks) resource = ResourceKind.Wood;
            var point = world.Map.Center(requestedCell);
            int best = -1;
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                var node = world.Nodes[i];
                if (node.Definition.Kind != resource || node.Remaining <= 0) continue;
                var distance = DistanceSquared(node.Definition.Position, point);
                if (best < 0 || distance < DistanceSquared(world.Nodes[best].Definition.Position, point)
                    || distance == DistanceSquared(world.Nodes[best].Definition.Position, point)
                    && node.Definition.Id < world.Nodes[best].Definition.Id) best = i;
            }
            return best;
        }

        private void RejectLineRequest(uint faction, EconomyCommand command, ReasonCode reason, int cell)
        {
            SimPoint position = cell >= 0 && cell < world.Config.Map.WidthCells * world.Config.Map.HeightCells ? world.Map.Center(cell) : OwnCore(faction).Definition.Position;
            commandEvents.Add(new GameEvent(world.Tick, (uint)commandEvents.Count, EventKind.EconomyLineRejected,
                (byte)(1 << (int)(faction - 1)), 0, command.IssuerSequence, position, (int)command.Line, reason));
        }

        private void RejectPendingLine(uint faction, ref ProcessingLineState line, ReasonCode reason)
        {
            line.RequestPending = false;
            var request = line.RequestedRegionId == 0
                ? EconomyCommand.RequestLineAt(faction, 0, line.Kind, line.RequestedCell)
                : EconomyCommand.RequestLine(faction, 0, line.Kind, line.RequestedRegionId);
            RejectLineRequest(faction, request, reason, line.RequestedCell);
        }

        private bool TryDecideRequestedLine(uint faction)
        {
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                if (world.ProcessingLines[i].FactionId != faction || !world.ProcessingLines[i].RequestPending) continue;
                if (!IsAutoLine(i)) { world.ProcessingLines[i].RequestPending = false; return true; }
                if (LineRebuildBlocked(i)) { StopLineHaulers(i); return true; }
                if (DecideRequestedLine(faction, i)) return true;
            }
            return false;
        }

        private bool DecideRequestedLine(uint faction, int index)
        {
            ref var line = ref world.ProcessingLines[index];
            ref var economy = ref world.Economies[faction - 1];
            if (line.Kind == ProcessingLineKind.Bricks)
            {
                if (!BuildingReady(line.QuarryId))
                {
                    if (BuildingPending(line.QuarryId)) return true;
                    if (economy.Wood < world.Config.Economy.QuarryWoodCost) { RejectPendingLine(faction, ref line, ReasonCode.LineWoodShortfall); return true; }
                    uint id = PlaceQuarry(faction, line.RequestedCell);
                    if (id == 0) { RejectPendingLine(faction, ref line, ReasonCode.LinePlacementUnavailable); return true; }
                    line.QuarryId = id; return true;
                }
                if (!BuildingReady(line.KilnId))
                {
                    if (BuildingPending(line.KilnId)) return true;
                    if (economy.Wood < world.Config.Economy.CharcoalKilnWoodCost) { RejectPendingLine(faction, ref line, ReasonCode.LineWoodShortfall); return true; }
                    uint id = PlaceKiln(faction, line.RequestedCell);
                    if (id == 0) { RejectPendingLine(faction, ref line, ReasonCode.LinePlacementUnavailable); return true; }
                    line.KilnId = id; return true;
                }
                if (!BuildingReady(line.BrickKilnId))
                {
                    if (BuildingPending(line.BrickKilnId)) return true;
                    if (economy.Wood < world.Config.Economy.BrickKilnWoodCost) { RejectPendingLine(faction, ref line, ReasonCode.LineWoodShortfall); return true; }
                    uint id = PlaceBrickKiln(faction, line.RequestedCell);
                    if (id == 0) { RejectPendingLine(faction, ref line, ReasonCode.LinePlacementUnavailable); return true; }
                    line.BrickKilnId = id; return true;
                }
                LayBricksLine(faction, index, world.Buildings[line.QuarryId - 1], world.Buildings[line.KilnId - 1], world.Buildings[line.BrickKilnId - 1]);
            }
            else if (line.Kind == ProcessingLineKind.Rations)
            {
                if (!BuildingReady(line.MillId))
                {
                    if (BuildingPending(line.MillId)) return true;
                    if (economy.Wood < world.Config.Economy.MillWoodCost) { RejectPendingLine(faction, ref line, ReasonCode.LineWoodShortfall); return true; }
                    uint id = PlaceMill(faction, line.RequestedCell);
                    if (id == 0) { RejectPendingLine(faction, ref line, ReasonCode.LinePlacementUnavailable); return true; }
                    line.MillId = id; return true;
                }
                LayRationsLine(faction, index, world.Buildings[line.MillId - 1]);
            }
            else if (line.Kind == ProcessingLineKind.Tack)
            {
                if (!BuildingReady(line.SaddleryId))
                {
                    if (BuildingPending(line.SaddleryId)) return true;
                    if (economy.Wood < world.Config.Economy.SaddleryWoodCost) { RejectPendingLine(faction, ref line, ReasonCode.LineWoodShortfall); return true; }
                    uint id = PlaceSaddlery(faction, line.RequestedCell);
                    if (id == 0) { RejectPendingLine(faction, ref line, ReasonCode.LinePlacementUnavailable); return true; }
                    line.SaddleryId = id; return true;
                }
                LayTackLine(faction, index, world.Buildings[line.SaddleryId - 1]);
            }
            else if (line.Kind == ProcessingLineKind.Planks)
            {
                if (!BuildingReady(line.LumberCampId))
                {
                    if (BuildingPending(line.LumberCampId)) return true;
                    if (economy.Wood < world.Config.Economy.LumberCampWoodCost) { RejectPendingLine(faction, ref line, ReasonCode.LineWoodShortfall); return true; }
                    uint id = PlaceLumberCamp(faction, line.RequestedCell);
                    if (id == 0) { RejectPendingLine(faction, ref line, ReasonCode.LinePlacementUnavailable); return true; }
                    line.LumberCampId = id; return true;
                }
                if (!BuildingReady(line.SawmillId))
                {
                    if (BuildingPending(line.SawmillId)) return true;
                    if (economy.Wood < world.Config.Economy.SawmillWoodCost) { RejectPendingLine(faction, ref line, ReasonCode.LineWoodShortfall); return true; }
                    uint id = PlaceSawmill(faction, world.Buildings[line.LumberCampId - 1]);
                    if (id == 0) { RejectPendingLine(faction, ref line, ReasonCode.LinePlacementUnavailable); return true; }
                    line.SawmillId = id; return true;
                }
                LayPlanksLine(faction, index, world.Buildings[line.LumberCampId - 1], world.Buildings[line.SawmillId - 1]);
            }
            else if (line.Kind == ProcessingLineKind.Books)
            {
                if (!BuildingReady(line.ScriptoriumId))
                {
                    if (BuildingPending(line.ScriptoriumId)) return true;
                    if (economy.Wood < world.Config.Economy.ScriptoriumWoodCost) { RejectPendingLine(faction, ref line, ReasonCode.LineWoodShortfall); return true; }
                    uint id = PlaceScriptorium(faction, line.RequestedCell);
                    if (id == 0) { RejectPendingLine(faction, ref line, ReasonCode.LinePlacementUnavailable); return true; }
                    line.ScriptoriumId = id; return true;
                }
                LayBooksLine(faction, index, world.Buildings[line.ScriptoriumId - 1]);
            }
            else if (line.Kind == ProcessingLineKind.Preserves)
            {
                if (!BuildingReady(line.DryingRackId))
                {
                    if (BuildingPending(line.DryingRackId)) return true;
                    if (economy.Wood < world.Config.Economy.DryingRackWoodCost) { RejectPendingLine(faction, ref line, ReasonCode.LineWoodShortfall); return true; }
                    uint id = PlaceDryingRack(faction, line.RequestedCell);
                    if (id == 0) { RejectPendingLine(faction, ref line, ReasonCode.LinePlacementUnavailable); return true; }
                    line.DryingRackId = id; return true;
                }
                LayPreservesLine(faction, index, world.Buildings[line.DryingRackId - 1]);
            }
            else if (line.Kind == ProcessingLineKind.CoreMetal || line.Kind == ProcessingLineKind.Steel)
            {
                if (line.Kind == ProcessingLineKind.Steel)
                    TryBindSharedMetalLine(LineIndex(faction, ProcessingLineKind.CoreMetal), index);
                if (!BuildingReady(line.MineId))
                {
                    if (BuildingPending(line.MineId)) return true;
                    if (economy.Wood < world.Config.Economy.MineWoodCost)
                    {
                        line.RequestPending = false;
                        var request = line.RequestedRegionId == 0
                            ? EconomyCommand.RequestLineAt(faction, 0, line.Kind, line.RequestedCell)
                            : EconomyCommand.RequestLine(faction, 0, line.Kind, line.RequestedRegionId);
                        RejectLineRequest(faction, request, ReasonCode.LineWoodShortfall, line.RequestedCell);
                        return true;
                    }
                    uint id = PlaceMine(faction, line.RequestedCell);
                    if (id == 0) { RejectPendingLine(faction, ref line, ReasonCode.LinePlacementUnavailable); return true; }
                    line.MineId = id; return true;
                }
                if (!BuildingReady(line.SmelterId))
                {
                    if (BuildingPending(line.SmelterId)) return true;
                    if (economy.Wood < world.Config.Economy.SmelterWoodCost) { RejectPendingLine(faction, ref line, ReasonCode.LineWoodShortfall); return true; }
                    uint id = PlaceSmelter(faction, world.Buildings[line.MineId - 1]);
                    if (id == 0) { RejectPendingLine(faction, ref line, ReasonCode.LinePlacementUnavailable); return true; }
                    line.SmelterId = id; return true;
                }
                if (line.Kind == ProcessingLineKind.Steel)
                {
                    if (!BuildingReady(line.KilnId))
                    {
                        if (BuildingPending(line.KilnId)) return true;
                        if (economy.Wood < world.Config.Economy.CharcoalKilnWoodCost) { RejectPendingLine(faction, ref line, ReasonCode.LineWoodShortfall); return true; }
                        uint id = PlaceKiln(faction, line.RequestedCell);
                        if (id == 0) { RejectPendingLine(faction, ref line, ReasonCode.LinePlacementUnavailable); return true; }
                        line.KilnId = id; return true;
                    }
                    if (!BuildingReady(line.SteelworksId))
                    {
                        if (BuildingPending(line.SteelworksId)) return true;
                        if (economy.Wood < world.Config.Economy.SteelworksWoodCost) { RejectPendingLine(faction, ref line, ReasonCode.LineWoodShortfall); return true; }
                        uint id = PlaceSteelworks(faction, line);
                        if (id == 0) { RejectPendingLine(faction, ref line, ReasonCode.LinePlacementUnavailable); return true; }
                        line.SteelworksId = id; return true;
                    }
                    var steelMine = world.Buildings[line.MineId - 1];
                    var steelSmelter = world.Buildings[line.SmelterId - 1];
                    var kiln = world.Buildings[line.KilnId - 1];
                    var steelworks = world.Buildings[line.SteelworksId - 1];
                    LaySteelLine(faction, index, steelMine, steelSmelter, kiln, steelworks);
                }
                else
                {
                    var mine = world.Buildings[line.MineId - 1];
                    var smelter = world.Buildings[line.SmelterId - 1];
                    LayCoreLine(faction, index, mine, smelter);
                }
            }
            else
            {
                if (!BuildingReady(line.LumberCampId))
                {
                    if (BuildingPending(line.LumberCampId)) return true;
                    if (economy.Wood < world.Config.Economy.LumberCampWoodCost) { RejectPendingLine(faction, ref line, ReasonCode.LineWoodShortfall); return true; }
                    uint id = PlaceLumberCamp(faction, line.RequestedCell);
                    if (id == 0) { RejectPendingLine(faction, ref line, ReasonCode.LinePlacementUnavailable); return true; }
                    line.LumberCampId = id; return true;
                }
                var camp = world.Buildings[line.LumberCampId - 1];
                if (line.Kind == ProcessingLineKind.BowGear)
                {
                    if (!BuildingReady(line.FletcherId))
                    {
                        if (BuildingPending(line.FletcherId)) return true;
                        if (economy.Wood < world.Config.Economy.FletcherWoodCost) { RejectPendingLine(faction, ref line, ReasonCode.LineWoodShortfall); return true; }
                        uint id = PlaceFletcher(faction, camp);
                        if (id == 0) { RejectPendingLine(faction, ref line, ReasonCode.LinePlacementUnavailable); return true; }
                        line.FletcherId = id; return true;
                    }
                    LayBowLine(faction, index, camp, world.Buildings[line.FletcherId - 1]);
                }
                else LayForestryCoreLine(faction, index, camp);
            }
            line.RequestPending = false;
            return true;
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
                    || line.LumberCampId == buildingId || line.FletcherId == buildingId || line.MillId == buildingId
                    || line.QuarryId == buildingId || line.BrickKilnId == buildingId || line.SaddleryId == buildingId || line.SawmillId == buildingId || line.DryingRackId == buildingId) return i;
            }
            return -1;
        }

        private int LineForBelt(uint faction, int cell)
        {
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                var line = world.ProcessingLines[i];
                if (line.FactionId != faction) continue;
                for (int j = 0; j < line.BeltCells.Length; j++)
                    if (line.BeltCells[j] == cell) return i;
            }
            return -1;
        }

        private bool LineRebuildBlocked(int index)
            => world.Config.Economy.LineRebuildDelayTicks > 0 && IsAutoLine(index)
                && world.Tick < world.ProcessingLines[index].RebuildAvailableTick;

        private void StopLineHaulers(int index)
        {
            if (index < 0 || index >= world.ProcessingLines.Length) return;
            var line = world.ProcessingLines[index];
            if (line.Manager == LineManager.Automatic) SetLineHaulers(line.FactionId, line, 0);
        }

        /// <summary>Enemy damage pauses an automatic line without changing its manager to Manual.</summary>
        private void DelayLineAfterRaid(uint faction, uint buildingId, SimPoint position)
        {
            int index = LineForBuilding(faction, buildingId);
            if (index >= 0) DelayLineAfterRaid(index, position);
        }

        /// <summary>Enemy damage pauses an automatic line without changing its manager to Manual.</summary>
        private void DelayLineAfterRaid(uint faction, int cell)
        {
            int index = LineForBelt(faction, cell);
            if (index >= 0) DelayLineAfterRaid(index, world.Map.Center(cell));
        }

        private void DelayLineAfterRaid(int index, SimPoint position)
        {
            int delay = world.Config.Economy.LineRebuildDelayTicks;
            if (delay <= 0 || !IsAutoLine(index)) return;
            long available = checked(world.Tick + delay);
            ref var line = ref world.ProcessingLines[index];
            if (available <= line.RebuildAvailableTick) return;
            line.RebuildAvailableTick = available;
            StopLineHaulers(index);
            // Reuse the existing line event contract. CommandId == ulong.MaxValue distinguishes this automatic cut
            // from a rejected line request without adding a Contract enum value.
            commandEvents.Add(new GameEvent(world.Tick, (uint)commandEvents.Count, EventKind.EconomyLineRejected,
                (byte)(1 << (int)(line.FactionId - 1)), line.Id, ulong.MaxValue, position, delay, ReasonCode.None));
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
            uint faction = world.ProcessingLines[index].FactionId;
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                if (i != index && !LinesShareAutomaticMetalRoute(index, i)) continue;
                ref var line = ref world.ProcessingLines[i];
                if (line.FactionId != faction) continue;
                if (line.Manager == LineManager.Manual) continue;
                line.Manager = LineManager.Manual;
                SetLineHaulers(faction, line, 0);
            }
        }

        private void ReturnLineToAuto(uint faction, uint lineId)
        {
            int target = -1;
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                if (world.ProcessingLines[i].FactionId == faction && world.ProcessingLines[i].Id == lineId) { target = i; break; }
            }
            if (target < 0) return;
            // A splitter-coupled core/steel pair is one human-owned unit. Returning either side returns both;
            // otherwise a player could unknowingly leave half of the shared route under automatic control.
            for (int i = 0; i < world.ProcessingLines.Length; i++)
            {
                if (i != target && !LinesShareAutomaticMetalRoute(target, i)) continue;
                ref var line = ref world.ProcessingLines[i];
                if (line.FactionId != faction) continue;
                line.Manager = LineManager.Automatic;
                ClearLineHeld(faction, line);
            }
        }

        private bool LinesShareAutomaticMetalRoute(int first, int second)
        {
            if (first < 0 || second < 0 || first >= world.ProcessingLines.Length || second >= world.ProcessingLines.Length) return false;
            var a = world.ProcessingLines[first];
            var b = world.ProcessingLines[second];
            if (a.FactionId != b.FactionId || a.Kind != ProcessingLineKind.CoreMetal && a.Kind != ProcessingLineKind.Steel
                || b.Kind != ProcessingLineKind.CoreMetal && b.Kind != ProcessingLineKind.Steel) return false;
            if (a.MineId != 0 && a.MineId == b.MineId || a.SmelterId != 0 && a.SmelterId == b.SmelterId) return true;
            for (int i = 0; i < a.BeltCells.Length; i++)
                for (int j = 0; j < b.BeltCells.Length; j++)
                    if (a.BeltCells[i] == b.BeltCells[j]) return true;
            return false;
        }

        private void ClearLineHeld(uint faction, ProcessingLineState line)
        {
            uint[] ids = { line.MineId, line.SmelterId, line.KilnId, line.SteelworksId, line.LumberCampId, line.FletcherId, line.MillId, line.QuarryId, line.BrickKilnId, line.SaddleryId, line.SawmillId, line.DryingRackId };
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
            if (line.Kind == ProcessingLineKind.Rations)
                return BuildingReady(line.MillId);
            if (line.Kind == ProcessingLineKind.Bricks)
                return BuildingReady(line.QuarryId) && BuildingReady(line.KilnId) && BuildingReady(line.BrickKilnId);
            if (line.Kind == ProcessingLineKind.Tack)
                return BuildingReady(line.SaddleryId);
            if (line.Kind == ProcessingLineKind.Planks)
                return BuildingReady(line.LumberCampId) && BuildingReady(line.SawmillId);
            if (line.Kind == ProcessingLineKind.Preserves)
                return BuildingReady(line.DryingRackId);
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
            else if (kind == BuildingKind.Mill) line.MillId = id;
            else if (kind == BuildingKind.Quarry) line.QuarryId = id;
            else if (kind == BuildingKind.BrickKiln) line.BrickKilnId = id;
            else if (kind == BuildingKind.Saddlery) line.SaddleryId = id;
            else if (kind == BuildingKind.Sawmill) line.SawmillId = id;
            else if (kind == BuildingKind.DryingRack) line.DryingRackId = id;
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
            if (line.Kind == ProcessingLineKind.Rations)
            {
                SetSimpleHaulers(faction, line.MillId, 0, wanted);
                return;
            }
            if (line.Kind == ProcessingLineKind.Bricks)
            {
                SetKilnWoodHaulers(faction, line.KilnId, 1);
                SetSimpleHaulers(faction, line.QuarryId, line.BrickKilnId, wanted);
                SetSimpleHaulers(faction, line.KilnId, line.BrickKilnId, wanted);
                SetSimpleHaulers(faction, line.BrickKilnId, 0, wanted);
                return;
            }
            if (line.Kind == ProcessingLineKind.Tack)
            {
                SetSimpleHaulers(faction, line.SaddleryId, 0, wanted);
                return;
            }
            if (line.Kind == ProcessingLineKind.Planks)
            {
                SetSimpleHaulers(faction, line.LumberCampId, line.SawmillId, wanted);
                SetSimpleHaulers(faction, line.SawmillId, 0, wanted);
                return;
            }
            if (line.Kind == ProcessingLineKind.Preserves)
            {
                SetSimpleHaulers(faction, line.DryingRackId, 0, wanted);
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

        private void SetKilnWoodHaulers(uint faction, uint kilnId, int wanted)
        {
            if (kilnId == 0) return;
            int current = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var v = ref world.Villagers[i];
                if (!v.Alive || v.FactionId != faction || v.Held || v.HaulTo != kilnId) continue;
                if (wanted == 0) StopHauling(ref v); else current++;
            }
            if (current >= wanted) return;
            var spot = world.Map.Center(world.Buildings[kilnId - 1].WorkCell);
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
                chosen.NodeId = 0; chosen.HaulFrom = 0; chosen.HaulTo = kilnId; chosen.HaulNodeId = 0;
                chosen.Task = VillagerTask.Idle;
                AssignKilnWood(ref chosen, kilnId);
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
