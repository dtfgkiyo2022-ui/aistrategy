using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private readonly long[] rememberedBuildingTargetSelections = new long[2];

        public long RememberedBuildingTargetCount(uint faction)
            => faction >= 1 && faction <= 2 ? rememberedBuildingTargetSelections[faction - 1] : 0;

        private void RecordRememberedBuildingTargets(uint faction, FactionObservation observation)
        {
            if (!BuildingMemoryOn || observation == null || observation.RememberedBuildings.Count == 0) return;
            foreach (uint armyId in world.Factions[faction - 1].ArmyIds)
            {
                var goal = world.Armies[armyId - 1].Goal;
                if (goal.Kind != GoalKind.Point) continue;
                for (int i = 0; i < observation.RememberedBuildings.Count; i++)
                    if (goal.Id == (observation.RememberedBuildings[i].Id | 0x80000000u))
                    {
                        rememberedBuildingTargetSelections[faction - 1] = checked(rememberedBuildingTargetSelections[faction - 1] + 1);
                        break;
                    }
            }
        }

        private bool BuildingMemoryOn => world.Config.Economy.Enabled && world.Config.Economy.BuildingMemory;

        private void UpdateBuildingMemory()
        {
            if (!BuildingMemoryOn) return;
            for (uint faction = 1; faction <= 2; faction++)
            {
                var state = world.Factions[faction - 1];
                if (state.BuildingMemory == null || state.BuildingMemory.Length < world.Buildings.Length)
                    Array.Resize(ref state.BuildingMemory, Math.Max(world.Buildings.Length, 4));

                // Clear an old record only when its last footprint is now visible. The subsequent scan can
                // immediately recreate it if the same building is still present at a newly observed location.
                for (int i = 0; i < world.BuildingCount; i++)
                {
                    var memory = state.BuildingMemory[i];
                    if (!memory.Seen || memory.OwnerFactionId == faction || !MemoryFootprintVisible(state, memory)) continue;
                    state.BuildingMemory[i] = default(RememberedBuildingState);
                }
                for (int i = 0; i < world.BuildingCount; i++)
                {
                    var building = world.Buildings[i];
                    if (!IsActualBuilding(building) || building.FactionId == faction || !BuildingVisibleTo(faction, building)) continue;
                    var previous = state.BuildingMemory[i];
                    // A bridge or tollgate keeps its footprint copy while it stays the same building, so a visible
                    // enemy bridge does not allocate a new array every tick.
                    int[] footprintCells = previous.Seen && previous.BuildingId == building.Id && previous.FootprintCells != null
                        ? previous.FootprintCells
                        : building.Kind == BuildingKind.Bridge || building.Kind == BuildingKind.Tollgate
                            ? (int[])Footprint(building).Clone() : null;
                    state.BuildingMemory[i] = new RememberedBuildingState
                    {
                        Seen = true, BuildingId = building.Id, OwnerFactionId = building.FactionId,
                        Kind = building.Kind, OriginCell = building.OriginCell, SizeCells = SizeOf(building.Kind),
                        FootprintCells = footprintCells,
                        Position = BuildingCenter(building), Hp = building.Hp, HpKnown = true, LastSeenTick = world.Tick
                    };
                }
                world.Factions[faction - 1] = state;
            }
        }

        private bool MemoryFootprintVisible(FactionState state, RememberedBuildingState memory)
        {
            if (memory.FootprintCells != null)
            {
                foreach (int cell in memory.FootprintCells)
                    if (cell >= 0 && cell < state.VisibleCells.Length && state.VisibleCells[cell]) return true;
                return false;
            }
            int width = world.Config.Map.WidthCells;
            int x0 = memory.OriginCell % width, z0 = memory.OriginCell / width;
            int size = Math.Max(1, memory.SizeCells);
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                {
                    int cell = (z0 + z) * width + x0 + x;
                    if (cell >= 0 && cell < state.VisibleCells.Length && state.VisibleCells[cell]) return true;
                }
            return false;
        }

        private IReadOnlyList<RememberedBuilding> RememberedBuildingsFor(uint faction)
        {
            var result = new List<RememberedBuilding>();
            if (!BuildingMemoryOn) return result;
            var state = world.Factions[faction - 1];
            if (state.BuildingMemory == null) return result;
            int rememberedCount = Math.Min(world.BuildingCount, state.BuildingMemory.Length);
            for (int i = 0; i < rememberedCount; i++)
            {
                var memory = state.BuildingMemory[i];
                if (!memory.Seen || memory.OwnerFactionId == faction || MemoryFootprintVisible(state, memory)) continue;
                result.Add(new RememberedBuilding(memory.BuildingId, memory.Kind, memory.Position,
                    checked(Math.Max(1, memory.SizeCells) * world.Config.Map.CellSizeMeters), memory.LastSeenTick,
                    memory.HpKnown, memory.Hp));
            }
            return result;
        }

        private void AddRememberedBuildingRaidTargets(uint faction, List<ObservedRaidTarget> targets)
        {
            if (!BuildingMemoryOn) return;
            foreach (var building in RememberedBuildingsFor(faction))
                targets.Add(new ObservedRaidTarget(building.Id | 0x80000000u, RaidTargetKind.Building,
                    building.Position, building.IsHpKnown ? building.Hp : 0));
        }
    }
}
