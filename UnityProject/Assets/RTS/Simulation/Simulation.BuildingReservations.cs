using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private int ReservationCount(uint faction)
        {
            if (!BuildingReservationsOn) return 0;
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive && world.Buildings[i].Reserved && world.Buildings[i].FactionId == faction) count++;
            return count;
        }

        private bool IsReservedBuildingCell(int cell)
        {
            if (!BuildingReservationsOn) return false;
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive && world.Buildings[i].Reserved
                    && System.Array.IndexOf(Footprint(world.Buildings[i]), cell) >= 0) return true;
            return false;
        }

        private int ReservationOrderFor(BuildingState building)
        {
            if (!building.Reserved) return 0;
            int order = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var other = world.Buildings[i];
                if (other.Alive && other.Reserved && other.FactionId == building.FactionId && other.Id <= building.Id) order++;
            }
            return order;
        }

        private int HeadReservationIndex(uint faction)
        {
            int best = -1;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive || !b.Reserved || b.FactionId != faction) continue;
                if (best < 0 || b.Id < world.Buildings[best].Id) best = i;
            }
            return best;
        }

        private uint FindReservedBuilding(uint faction, BuildingKind kind)
        {
            int best = -1;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive || !b.Reserved || b.FactionId != faction || b.Kind != kind || IsProcessingLineBuildingReferenced(b.Id)) continue;
                if (best < 0 || b.Id < world.Buildings[best].Id) best = i;
            }
            return best < 0 ? 0u : world.Buildings[best].Id;
        }

        private int ReservedConstructionWood(uint faction)
        {
            int index = HeadReservationIndex(faction);
            if (index < 0) return 0;
            var reservation = world.Buildings[index];
            return BuildingCostForPlacement(reservation.Kind, faction, reservation.BridgeCells == null ? 0 : reservation.BridgeCells.Length).Wood;
        }

        private int ReservedConstructionStone(uint faction)
        {
            int index = HeadReservationIndex(faction);
            return index < 0 ? 0 : BuildingCostForPlacement(world.Buildings[index].Kind, faction,
                world.Buildings[index].BridgeCells == null ? 0 : world.Buildings[index].BridgeCells.Length).Stone;
        }

        private int ReservedConstructionPlanks(uint faction)
        {
            int index = HeadReservationIndex(faction);
            return index < 0 ? 0 : BuildingCostForPlacement(world.Buildings[index].Kind, faction,
                world.Buildings[index].BridgeCells == null ? 0 : world.Buildings[index].BridgeCells.Length).Planks;
        }

        private bool CanPayAutomaticConstruction(uint faction, BuildingMaterialCost cost)
        {
            var economy = world.Economies[faction - 1];
            int heldWood = automaticEconomyAction && BuildingReservationsOn ? ReservedConstructionWood(faction) : 0;
            int heldStone = automaticEconomyAction && BuildingReservationsOn ? ReservedConstructionStone(faction) : 0;
            int heldPlanks = automaticEconomyAction && BuildingReservationsOn ? ReservedConstructionPlanks(faction) : 0;
            heldWood = checked(heldWood + (automaticEconomyAction ? AgeFourAiWoodReserve(faction) : 0));
            if (automaticEconomyAction && BuildingReservationsOn
                && (economy.Wood - heldWood < cost.Wood || economy.Stone - heldStone < cost.Stone
                    || economy.Planks - heldPlanks < cost.Planks)) return false;
            if (automaticEconomyAction && AgeFourAiOn && economy.Wood - heldWood < cost.Wood) return false;
            return !NoNegativeStockOn || (economy.Wood >= cost.Wood && economy.Stone >= cost.Stone && economy.Planks >= cost.Planks
                && economy.Fittings >= cost.Fittings && economy.Rope >= cost.Rope);
        }

        private void CreateBuildingReservation(uint faction, BuildingKind kind, int origin, Facing facing, uint nodeId,
            int[] bridgeCells = null, int requestedWorkCell = -1,
            uint caravanOutpost = 0, uint caravanMarket = 0, Fix64 caravanDistance = default(Fix64), int caravanReward = 0,
            uint sanctuaryOutpost = 0)
        {
            if (!BuildingReservationsOn || ReservationCount(faction) >= world.Config.Economy.ReservationLimit) return;
            int index = world.BuildingCount;
            if (index == world.Buildings.Length) System.Array.Resize(ref world.Buildings, index == 0 ? 4 : checked(index * 2));
            int size = SizeOf(kind);
            int workCell = kind == BuildingKind.Town
                ? NearestReachablePassableCell(FootprintCenter(origin, size), faction)
                : NearestPassableCell(FootprintCenter(origin, size));
            world.Buildings[index] = new BuildingState
            {
                Id = world.NextBuildingId, FactionId = faction, Kind = kind, OriginCell = origin,
                BridgeCells = bridgeCells == null ? null : (int[])bridgeCells.Clone(),
                WorkCell = requestedWorkCell >= 0 ? requestedWorkCell : workCell, Alive = true, Complete = false, Reserved = true, Hp = 0,
                Progress = 0, Facing = facing, NodeId = nodeId, Held = IndustryOn,
                CaravanOutpostId = caravanOutpost, CaravanMarketId = caravanMarket, CaravanDistance = caravanDistance,
                CaravanWoodReward = caravanReward, SanctuaryOutpostId = sanctuaryOutpost
            };
            world.NextBuildingId = checked(world.NextBuildingId + 1);
        }

        private void AdvanceBuildingReservations()
        {
            if (!BuildingReservationsOn) return;
            int interval = world.Config.Economy.ReservationCheckTicks;
            if (interval <= 0 || world.Tick % interval != 0) return;
            for (uint faction = 1; faction <= 2; faction++)
            {
                int index = HeadReservationIndex(faction);
                if (index < 0) continue;
                ref var reservation = ref world.Buildings[index];
                if (!ReservationStillLegal(faction, index))
                {
                    CancelBuildingReservation(index, faction);
                    continue;
                }
                var reservationCost = BuildingCostForPlacement(reservation.Kind, faction,
                    reservation.BridgeCells == null ? 0 : reservation.BridgeCells.Length);
                if (world.Economies[faction - 1].Wood < reservationCost.Wood
                    || world.Economies[faction - 1].Stone < reservationCost.Stone
                    || world.Economies[faction - 1].Planks < reservationCost.Planks) continue;
                BuildingKind kind = reservation.Kind;
                int origin = reservation.OriginCell;
                Facing facing = reservation.Facing;
                uint node = reservation.NodeId;
                uint caravanOutpost = reservation.CaravanOutpostId, caravanMarket = reservation.CaravanMarketId;
                Fix64 caravanDistance = reservation.CaravanDistance;
                int caravanReward = reservation.CaravanWoodReward;
                uint sanctuaryOutpost = reservation.SanctuaryOutpostId;
                // The reservation was passable while it was queued. Recompute the work cell
                // after PlaceBuildingAt closes the footprint, just as a direct placement does.
                PlaceBuildingAt(faction, kind, origin, facing, node, reservation.BridgeCells, -1, index);
                ref var started = ref world.Buildings[index];
                started.Held = IndustryOn;
                started.CaravanOutpostId = caravanOutpost; started.CaravanMarketId = caravanMarket;
                started.CaravanDistance = caravanDistance; started.CaravanWoodReward = caravanReward;
                started.SanctuaryOutpostId = sanctuaryOutpost;
            }
        }

        private bool ReservationStillLegal(uint faction, int index)
        {
            var reservation = world.Buildings[index];
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells, size = SizeOf(reservation.Kind);
            if (reservation.OriginCell < 0 || reservation.OriginCell >= width * height
                || reservation.OriginCell % width + size > width || reservation.OriginCell / width + size > height) return false;
            ref var mutable = ref world.Buildings[index];
            mutable.Alive = false;
            if (reservation.Kind == BuildingKind.Bridge)
            {
                bool bridgeLegal = TryValidateBridge(faction, reservation.BridgeCells, reservation.Facing, out _, out _);
                mutable.Alive = true;
                return bridgeLegal && KeepsMapConnected(faction, reservation.BridgeCells);
            }
            if (reservation.Kind == BuildingKind.Tollgate)
            {
                int[] cells = TollgateFootprint(reservation.OriginCell, reservation.Facing);
                bool gateLegal = TollgateSiteIsClear(faction, cells) && KeepsMapConnected(faction, cells);
                mutable.Alive = true;
                return gateLegal;
            }
            uint node = 0;
            bool generic = reservation.Kind != BuildingKind.Town && reservation.Kind != BuildingKind.Mine
                && reservation.Kind != BuildingKind.Drill && reservation.Kind != BuildingKind.LumberCamp
                && reservation.Kind != BuildingKind.Quarry && reservation.Kind != BuildingKind.MineShaft
                && reservation.Kind != BuildingKind.Harbor && reservation.Kind != BuildingKind.Caravanserai
                && reservation.Kind != BuildingKind.Shrine;
            int clearance = generic && world.Config.Economy.AdjacentBuildings
                ? world.Config.Economy.AdjacentBuildingClearanceCells : BuildingClearanceCells;
            bool clear = reservation.Kind == BuildingKind.Town ? TownSiteIsClear(faction, reservation.OriginCell, size)
                : reservation.Kind == BuildingKind.Mine ? MineSiteIsClear(faction, reservation.OriginCell, out node)
                : reservation.Kind == BuildingKind.Drill ? DrillSiteIsClear(faction, reservation.OriginCell, out node)
                : reservation.Kind == BuildingKind.LumberCamp ? LumberCampSiteIsClear(faction, reservation.OriginCell, out node)
                : reservation.Kind == BuildingKind.Quarry ? QuarrySiteIsClear(faction, reservation.OriginCell, out node)
                : reservation.Kind == BuildingKind.MineShaft ? MountainShaftSiteIsClear(faction, reservation.OriginCell)
                : reservation.Kind == BuildingKind.Harbor ? HarborSiteIsClear(faction, reservation.OriginCell)
                : SiteIsClear(reservation.OriginCell, world.Map.Cell(OwnCore(faction).Definition.Position), size, clearance);
            bool ports = !world.Config.Economy.AdjacentBuildings || !generic
                || ManualPlacementPortsAreClear(reservation.Kind, reservation.OriginCell, size, reservation.Facing);
            bool connected = clear && ports && KeepsMapConnected(faction, reservation.OriginCell, size);
            mutable.Alive = true;
            return connected;
        }

        private void CancelBuildingReservation(int index, uint faction)
        {
            ref var reservation = ref world.Buildings[index];
            reservation.Alive = false;
            commandEvents.Add(new GameEvent(world.Tick, (uint)commandEvents.Count, EventKind.BuildingReservationCancelled,
                (byte)(1 << (int)(faction - 1)), reservation.Id, 0, BuildingCenter(reservation), 0,
                ReasonCode.BuildingReservationInvalid));
        }
    }
}
