using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        private const int TollgateSearchPathMargin = 3;

        // Derived from the buildings (never saved): the faction owning a finished tollgate on each cell, 0 elsewhere.
        // Rebuilt at the start of every tick; the route caches are dropped only when it actually changes.
        private uint[] tollgateOwners;

        private void RefreshTollgateOwners()
        {
            int cells = world.Config.Map.WidthCells * world.Config.Map.HeightCells;
            var next = new uint[cells];
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var building = world.Buildings[i];
                if (!building.Alive || !building.Complete || building.Kind != BuildingKind.Tollgate) continue;
                foreach (int cell in Footprint(building)) if (cell >= 0 && cell < cells) next[cell] = building.FactionId;
            }
            bool changed = tollgateOwners == null || tollgateOwners.Length != cells;
            for (int i = 0; !changed && i < cells; i++) changed = tollgateOwners[i] != next[i];
            tollgateOwners = next;
            if (changed) world.Map.InvalidateRoutes();
        }

        private bool IsPassableForFaction(int cell, uint faction)
        {
            if (!world.Map.IsPassable(cell)) return false;
            if (faction < 1 || faction > 2 || tollgateOwners == null) return true;
            uint owner = tollgateOwners[cell];
            return owner == 0 || owner == faction;
        }

        private void TryPlaceTollgate(uint faction, EconomyCommand command)
        {
            var rules = world.Config.Economy;
            if (!TollgateAllowed(faction) || ActiveTollgateCount(faction) >= TollgateMaxBuildingsFor(faction)
                || (byte)command.Facing > 3 || world.Economies[faction - 1].Wood < rules.TollgateWoodCost
                || world.Economies[faction - 1].Stone < rules.TollgateStoneCost) return;
            int[] cells = TollgateFootprint(command.Cell, command.Facing);
            if (!TollgateSiteIsClear(faction, cells) || !KeepsMapConnected(faction, cells)) return;
            PlaceBuildingAt(faction, BuildingKind.Tollgate, command.Cell, command.Facing, 0);
            world.Buildings[world.BuildingCount - 1].Held = true;
        }

        private int ActiveTollgateCount(uint faction)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == BuildingKind.Tollgate) count++;
            return count;
        }

        private int TollgateMaxBuildingsFor(uint faction)
            => world.Config.Economy.TollgateMaxBuildings
                + (HasTech(faction, TollgateTech.GateNetwork) ? world.Config.Economy.TollgateGateNetworkMaxBuildingsBonus : 0);

        private int TollgateHpFor(int baseHp, uint faction)
            => HasTech(faction, TollgateTech.GateDefence)
                ? checked(baseHp * world.Config.Economy.TollgateGateDefenceHpPermille / 1000) : baseHp;

        /// <summary>V3-16 #2: one fee pulse counts enemy soldiers stopped within the gate's fee radius.</summary>
        private void AdvanceTollgateFees()
        {
            var rules = world.Config.Economy;
            if (!TollgateOn || rules.TollgateFeeIntervalTicks <= 0 || world.Tick % rules.TollgateFeeIntervalTicks != 0) return;
            long radius = Fix64.FromInt(rules.TollgateFeeRadiusMeters).Raw;
            long radiusSquared = checked(radius * radius);
            long networkRadius = Fix64.FromInt(rules.TollgateNetworkRadiusMeters).Raw;
            long networkRadiusSquared = checked(networkRadius * networkRadius);
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var gate = world.Buildings[i];
                if (!gate.Alive || !gate.Complete || gate.Kind != BuildingKind.Tollgate) continue;
                int enemyCount = 0;
                var gateCenter = BuildingCenter(gate);
                foreach (int soldierIndex in world.SoldierTraversal)
                {
                    var enemy = world.Soldiers[soldierIndex];
                    if (!enemy.Alive || enemy.Initial.FactionId == gate.FactionId || !WithinSquared(enemy.Position, gateCenter, radiusSquared)) continue;
                    enemyCount++;
                }
                if (enemyCount == 0) continue;
                int nearbyGates = 0;
                for (int j = 0; j < world.BuildingCount; j++)
                {
                    var other = world.Buildings[j];
                    if (i == j || !other.Alive || !other.Complete || other.FactionId != gate.FactionId || other.Kind != BuildingKind.Tollgate
                        || !WithinSquared(BuildingCenter(other), gateCenter, networkRadiusSquared)) continue;
                    nearbyGates++;
                }
                int multiplier = checked(1000 + nearbyGates * (HasTech(gate.FactionId, TollgateTech.GateNetwork) ? rules.TollgateNetworkFeeBonusPermille : 0));
                ref var economy = ref world.Economies[gate.FactionId - 1];
                economy.Wood = checked(economy.Wood + checked(enemyCount * rules.TollgateWoodPerEnemy * multiplier / 1000));
                economy.Food = checked(economy.Food + checked(enemyCount * rules.TollgateFoodPerEnemy * multiplier / 1000));
            }
        }

        private static bool WithinSquared(SimPoint a, SimPoint b, long radiusSquared)
        {
            long dx = a.X.Raw - b.X.Raw, dz = a.Z.Raw - b.Z.Raw;
            return checked(dx * dx + dz * dz) <= radiusSquared;
        }

        /// <summary>V3-16 #2: only tollgate-civilisation soldiers beside their own completed gate get this reduction.</summary>
        private int TollgateDefenceDamage(uint defenderFaction, SimPoint position, int damage)
        {
            if (damage <= 0 || !TollgateAllowed(defenderFaction) || !HasTech(defenderFaction, TollgateTech.GateDefence)) return damage;
            int cell = world.Map.Cell(position);
            if (cell < 0) return damage;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var gate = world.Buildings[i];
                if (!gate.Alive || !gate.Complete || gate.FactionId != defenderFaction || gate.Kind != BuildingKind.Tollgate) continue;
                foreach (int gateCell in Footprint(gate))
                    if (Chebyshev(cell, gateCell) <= 1)
                        return checked(damage * (1000 - world.Config.Economy.TollgateGateDefenceDamageReductionPermille) / 1000);
            }
            return damage;
        }

        private bool TollgateSiteIsClear(uint faction, int[] cells)
        {
            if (cells == null || cells.Length != world.Config.Economy.TollgateLengthCells) return false;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            var seen = new HashSet<int>();
            int core = world.Map.Cell(OwnCore(faction).Definition.Position);
            foreach (int cell in cells)
            {
                if (cell < 0 || cell >= width * height || !seen.Add(cell) || !world.Map.IsPassable(cell)
                    || IsRiverCell(cell) || Chebyshev(cell, core) < CoreClearanceCells || IsNodeCell(cell) || InsideAnyCore(cell)) return false;
                if (world.Belts.Length != 0 && world.Belts[cell].FactionId != 0) return false;
                foreach (var node in world.Nodes)
                    if (Chebyshev(cell, world.Map.Cell(node.Definition.Position)) < NodeClearanceCells) return false;
                for (int i = 0; i < world.BuildingCount; i++)
                {
                    if (!world.Buildings[i].Alive) continue;
                    foreach (int other in Footprint(world.Buildings[i]))
                        if (Chebyshev(cell, other) < BuildingClearanceCells) return false;
                }
            }
            return true;
        }

        /// <summary>V3-16 #1: the automatic choice is the narrowest cell pair on the own-core to enemy-core route.</summary>
        private void DecideTollgate(uint faction)
        {
            if (!TollgateAllowed(faction) || ActiveTollgateCount(faction) >= TollgateMaxBuildingsFor(faction)) return;
            var economy = world.Economies[faction - 1];
            var rules = world.Config.Economy;
            if (economy.Wood < rules.TollgateWoodCost || economy.Stone < rules.TollgateStoneCost) return;
            int start = world.Map.Cell(OwnCore(faction).Definition.Position);
            var enemy = world.Cores[world.Factions[2 - faction].CoreId - 1].Definition.Position;
            int[] path = world.Map.FindPath(start, enemy, faction);
            if (path.Length < TollgateSearchPathMargin * 2 + 1) return;

            int[] chosen = null;
            Facing chosenFacing = Facing.North;
            int chosenNarrowness = int.MaxValue, chosenPathIndex = int.MaxValue, chosenOrigin = int.MaxValue;
            for (int i = TollgateSearchPathMargin; i < path.Length - TollgateSearchPathMargin; i++)
            {
                int pathCell = path[i];
                for (int f = 0; f < 4; f++)
                {
                    var facing = (Facing)f;
                    var cells = TollgateFootprint(pathCell, facing);
                    if (Array.IndexOf(cells, pathCell) < 0 || !TollgateSiteIsClear(faction, cells) || !KeepsMapConnected(faction, cells)) continue;
                    int narrowness = OpenNeighbours(cells);
                    if (narrowness < chosenNarrowness || narrowness == chosenNarrowness && i < chosenPathIndex
                        || narrowness == chosenNarrowness && i == chosenPathIndex && pathCell < chosenOrigin)
                    {
                        chosen = cells; chosenFacing = facing; chosenNarrowness = narrowness; chosenPathIndex = i; chosenOrigin = pathCell;
                    }
                }
            }
            if (chosen == null) return;
            PlaceBuildingAt(faction, BuildingKind.Tollgate, chosen[0], chosenFacing, 0);
            world.Buildings[world.BuildingCount - 1].Held = false;
        }

        /// <summary>
        /// The parts of DecideTollgate other than the cost and the site search (see FoundationNeedsStone): room for a
        /// gate under the limit, and a route to the enemy core long enough to have gate candidates between its margins.
        /// The route is the same cached FindPath DecideTollgate reads; no site is searched here.
        /// </summary>
        private bool TollgateFoundationReady(uint faction)
        {
            if (ActiveTollgateCount(faction) >= TollgateMaxBuildingsFor(faction)) return false;
            int start = world.Map.Cell(OwnCore(faction).Definition.Position);
            var enemy = world.Cores[world.Factions[2 - faction].CoreId - 1].Definition.Position;
            return world.Map.FindPath(start, enemy, faction).Length >= TollgateSearchPathMargin * 2 + 1;
        }

        private int OpenNeighbours(int[] cells)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells, result = 0;
            foreach (int cell in cells)
            {
                int x = cell % width, z = cell / width;
                int[] neighbours = { z + 1 < height ? cell + width : -1, x + 1 < width ? cell + 1 : -1,
                    z > 0 ? cell - width : -1, x > 0 ? cell - 1 : -1 };
                foreach (int next in neighbours)
                    if (next >= 0 && Array.IndexOf(cells, next) < 0 && world.Map.IsPassable(next)) result++;
            }
            return result;
        }

        private bool KeepsMapConnected(uint faction, int[] closedCells)
        {
            int cells = world.Config.Map.WidthCells * world.Config.Map.HeightCells, width = world.Config.Map.WidthCells;
            var closed = new bool[cells];
            foreach (int cell in closedCells) if (cell >= 0 && cell < cells) closed[cell] = true;
            var seen = new bool[cells];
            var queue = new int[cells];
            int head = 0, tail = 0, start = world.Map.Cell(OwnCore(faction).Definition.Position);
            if (!world.Map.IsPassable(start) || closed[start]) return false;
            queue[tail++] = start; seen[start] = true;
            while (head < tail)
            {
                int cell = queue[head++], x = cell % width;
                Visit(cell + width); if (x + 1 < width) Visit(cell + 1); Visit(cell - width); if (x > 0) Visit(cell - 1);
            }
            bool Reached(SimPoint point) { int cell = world.Map.Cell(point); return cell >= 0 && seen[cell]; }
            if (!Reached(world.Cores[world.Factions[2 - faction].CoreId - 1].Definition.Position)) return false;
            foreach (var post in world.Outposts) if (!Reached(post.Definition.Position)) return false;
            foreach (var node in world.Nodes)
            {
                int cell = world.Map.Cell(node.Definition.Position);
                if (cell < 0) return false;
                if (closed[cell] || !world.Map.IsPassable(cell)) continue;
                if (!seen[cell]) return false;
            }
            return true;
            void Visit(int next)
            {
                if (next < 0 || next >= cells || seen[next] || closed[next] || !world.Map.IsPassable(next)) return;
                seen[next] = true; queue[tail++] = next;
            }
        }
    }
}
