using System;
using System.Numerics;
using Rts.Contracts;

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
        private static readonly Facing[] Sides = { Facing.North, Facing.East, Facing.South, Facing.West };

        /// <summary>AI phase, after the barracks and infantry (13 steps 1-4).</summary>
        private void DecideIndustry(uint faction)
        {
            if (!IndustryOn || !MetalworkAllowed(faction)) return;
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
            // V3-3: the player took this industry over; the automatic economy lays no line and calls its carriers back.
            if (m.Held || s.Held) { SetHaulers(faction, m.Id, s.Id, 0); return; }
            bool whole = LayLine(faction, m, s);
            SetHaulers(faction, m.Id, s.Id, whole ? 0 : Haulers);
        }

        private int OwnBuildingIndex(uint faction, BuildingKind kind)
        {
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.FactionId == faction && b.Kind == kind) return i;
            }
            return -1;
        }

        /// <summary>Ore points by distance to the core, then id; on each, the four footprints and the sides toward the core first.</summary>
        private void PlaceMine(uint faction)
        {
            var core = OwnCore(faction).Definition.Position;
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
                if (node.Definition.Kind != ResourceKind.Ore || node.Remaining <= 0) continue;
                int cell = world.Map.Cell(node.Definition.Position), nx = cell % width, nz = cell / width;
                for (int dz = 0; dz < size; dz++)
                    for (int dx = 0; dx < size; dx++)
                    {
                        int x0 = nx - dx, z0 = nz - dz;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (!MineSiteIsClear(origin, out uint id) || !KeepsMapConnected(faction, origin, size)) continue;
                        foreach (var side in SidesToward(FootprintCenter(origin, size), core))
                        {
                            if (!PortIsOpen(OutputCell(origin, size, side), faction)) continue;
                            PlaceBuildingAt(faction, BuildingKind.Mine, origin, side, id);
                            return;
                        }
                    }
            }
        }

        /// <summary>Rings around the point halfway from the mine to the core, like the barracks search.</summary>
        private void PlaceSmelter(uint faction, BuildingState mine)
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
                            PlaceBuildingAt(faction, BuildingKind.Smelter, origin, side, 0);
                            return;
                        }
                    }
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

        /// <summary>
        /// Two routes, mine output -> smelter and smelter output -> core, each the shortest 4-neighbour run of open cells
        /// (own belts count as open, so the line laid last cycle is found again). Lays the missing belts while wood lasts.
        /// True when every cell of both routes carries an own belt facing along the route.
        /// </summary>
        private bool LayLine(uint faction, BuildingState mine, BuildingState smelter)
        {
            int cells = world.Belts.Length;
            var taken = new bool[cells];
            var toSmelter = BeltRoute(faction, OutputCell(mine), taken, next => InFootprint(smelter, next));
            if (toSmelter.cells == null) return false;
            foreach (int c in toSmelter.cells) taken[c] = true;
            var toCore = BeltRoute(faction, OutputCell(smelter), taken, next => FeedsOwnCore(next, faction));
            if (toCore.cells == null) return false;
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            int owned = 0;
            for (int i = 0; i < cells; i++) if (world.Belts[i].FactionId == faction) owned++;
            bool whole = true;
            foreach (var (route, facings) in new[] { toSmelter, toCore })
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

        private bool InFootprint(BuildingState b, int cell)
        {
            int width = world.Config.Map.WidthCells, size = SizeOf(b.Kind), x0 = b.OriginCell % width, z0 = b.OriginCell / width;
            int x = cell % width, z = cell / width;
            return x >= x0 && x < x0 + size && z >= z0 && z < z0 + size;
        }

        /// <summary>Breadth-first from <paramref name="start"/> in N, E, S, W order; null when no route exists.</summary>
        private (int[] cells, Facing[] facings) BeltRoute(uint faction, int start, bool[] taken, Func<int, bool> into)
        {
            if (!PortIsOpen(start, faction) || taken[start]) return (null, null);
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
                    if (into(next))
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

        /// <summary>
        /// Keeps <paramref name="wanted"/> villagers carrying by hand: the first from the mine, the second from the smelter.
        /// New carriers are the nearest to the mine among those gathering or idle (distance, then id); with none wanted,
        /// every carrier of this line stops.
        /// </summary>
        private void SetHaulers(uint faction, uint mine, uint smelter, int wanted)
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
                    var v = world.Villagers[i];
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
