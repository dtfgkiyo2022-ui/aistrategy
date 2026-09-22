using System;
using Rts.Contracts;
using Rts.Decision;

namespace Rts.Simulation
{
    /// <summary>
    /// Ver.3 buildings (technical-design-v3 5.1-5.4, V3-1 PR3a): the automatic economy places one barracks near the core,
    /// villagers build it, and it trains infantry that join the armies through the Ver.1 reinforcement assignment.
    /// A footprint is impassable from placement on, so the terrain changes: every cached route is dropped and every
    /// army and unit plans again. Buildings cannot be seen or attacked by the enemy yet (PR3b).
    /// </summary>
    public sealed partial class Simulation
    {
        private const int SiteSearchRadiusCells = 30, CoreClearanceCells = 4, NodeClearanceCells = 2, BuildingClearanceCells = 2;

        /// <summary>AI phase, after villager training (5.4 steps 2 and 3).</summary>
        private void DecideBuildings(uint faction)
        {
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            bool hasBarracks = false, ready = false;
            int barracks = -1;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive || b.FactionId != faction || b.Kind != BuildingKind.Barracks) continue;
                hasBarracks = true;
                // V3-3: a barracks the player operates is not queued by the automatic economy.
                if (b.Complete && !b.Held && barracks < 0) { ready = true; barracks = i; }
            }
            if (EconomyDecision.ShouldBuildBarracks(hasBarracks, economy.Wood, rules.BarracksWoodCost))
                PlaceBarracks(faction);
            if (barracks < 0) return;
            ref var building = ref world.Buildings[barracks];
            int population = LivingVillagers(faction) + LivingSoldiers(faction) + economy.Queued + QueuedInfantry(faction);
            if (!EconomyDecision.ShouldTrainInfantry(ready, building.Queued, Math.Min(PlanOf(faction).InfantryQueue, rules.QueueLimit),
                economy.Food, economy.Wood, rules.InfantryFoodCost, rules.InfantryWoodCost, population, rules.PopulationCap, HasInfantryRoom(faction))
                || economy.Metal < InfantryMetalFor(faction) || SavingToAdvance(faction)) return;
            economy.Food = checked(economy.Food - rules.InfantryFoodCost);
            economy.Wood = checked(economy.Wood - rules.InfantryWoodCost);
            economy.Metal = checked(economy.Metal - InfantryMetalFor(faction));
            building.QueuedMetal = checked(building.QueuedMetal + InfantryMetalFor(faction));
            if (building.Queued == 0) building.TrainRemaining = rules.InfantryTrainTicks;
            building.Queued++;
        }

        private void PlaceBarracks(uint faction)
        {
            int origin = FindBarracksSite(faction);
            if (origin < 0) return; // no room near the core: try again next cycle
            PlaceBuildingAt(faction, BuildingKind.Barracks, origin, Facing.North, 0);
        }

        /// <summary>Pays, closes the footprint, and sends the builders. The caller has checked the site and the wood.</summary>
        private void PlaceBuildingAt(uint faction, BuildingKind kind, int origin, Facing facing, uint nodeId)
        {
            ref var economy = ref world.Economies[faction - 1];
            economy.Wood = checked(economy.Wood - WoodOf(kind));
            int index = world.BuildingCount;
            if (index == world.Buildings.Length) Array.Resize(ref world.Buildings, index == 0 ? 4 : checked(index * 2));
            var footprint = Footprint(origin, SizeOf(kind));
            foreach (int cell in footprint) world.Map.SetPassable(cell, false);
            world.Buildings[index] = new BuildingState { Id = world.NextBuildingId, FactionId = faction, Kind = kind, OriginCell = origin,
                WorkCell = NearestPassableCell(FootprintCenter(origin, SizeOf(kind))), Alive = true, Hp = HpOf(kind), Facing = facing, NodeId = nodeId };
            world.NextBuildingId = checked(world.NextBuildingId + 1);
            if (nodeId != 0) ReleaseNode(nodeId);
            EvacuateFootprint(footprint);
            TerrainChanged();
            AssignBuilders(ref world.Buildings[index]);
        }

        /// <summary>
        /// Rings around the core cell, nearest first; within a ring by z then x. The first footprint that is on free
        /// ground, keeps its distance from the core, resources and buildings, and does not cut the map apart wins.
        /// </summary>
        private int FindBarracksSite(uint faction)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells, size = world.Config.Economy.BarracksSizeCells;
            int core = world.Map.Cell(OwnCore(faction).Definition.Position), cx = core % width, cz = core / width;
            for (int r = 0; r <= SiteSearchRadiusCells; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int x0 = cx + dx - size / 2, z0 = cz + dz - size / 2;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (SiteIsClear(origin, core, size) && KeepsMapConnected(faction, origin, size)) return origin;
                    }
            return -1;
        }

        private bool SiteIsClear(int origin, int coreCell, int size)
        {
            foreach (int cell in Footprint(origin, size))
            {
                if (!world.Map.IsPassable(cell) || Chebyshev(cell, coreCell) < CoreClearanceCells) return false;
                if (world.Belts.Length != 0 && world.Belts[cell].FactionId != 0) return false; // V3-2: never on a belt
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

        /// <summary>
        /// With the footprint closed, the own core still reaches the enemy core, both outposts and every resource - except
        /// a point under a footprint (a mine's own ore point, or one under this footprint), which nobody walks to.
        /// </summary>
        private bool KeepsMapConnected(uint faction, int origin, int size)
        {
            int cells = world.Config.Map.WidthCells * world.Config.Map.HeightCells, width = world.Config.Map.WidthCells;
            var closed = new bool[cells];
            foreach (int cell in Footprint(origin, size)) closed[cell] = true;
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
            void Visit(int next)
            {
                if (next < 0 || next >= cells || seen[next] || closed[next] || !world.Map.IsPassable(next)) return;
                seen[next] = true; queue[tail++] = next;
            }
            if (!Reached(world.Cores[world.Factions[2 - faction].CoreId - 1].Definition.Position)) return false;
            foreach (var post in world.Outposts) if (!Reached(post.Definition.Position)) return false;
            foreach (var node in world.Nodes)
            {
                int c = world.Map.Cell(node.Definition.Position);
                if (c < 0) return false;
                if (closed[c] || !world.Map.IsPassable(c)) continue;
                if (!seen[c]) return false;
            }
            return true;
            bool Reached(SimPoint p) { int c = world.Map.Cell(p); return c >= 0 && seen[c]; }
        }

        /// <summary>Anyone standing on a new footprint steps to the nearest free cell (distance, then cell id).</summary>
        private void EvacuateFootprint(int[] footprint)
        {
            foreach (int i in world.SoldierTraversal)
            {
                ref var s = ref world.Soldiers[i];
                if (!s.Alive || Array.IndexOf(footprint, world.Map.Cell(s.Position)) < 0) continue;
                s.Position = s.MoveGoal = world.Map.Center(NearestPassableCell(s.Position));
            }
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var v = ref world.Villagers[i];
                if (!v.Alive || Array.IndexOf(footprint, world.Map.Cell(v.Position)) < 0) continue;
                v.Position = v.MoveGoal = world.Map.Center(NearestPassableCell(v.Position));
            }
        }

        /// <summary>Every route was planned on the old terrain: armies, soldiers' local routes and villagers plan again.</summary>
        private void TerrainChanged()
        {
            foreach (int a in world.ArmyTraversal) { world.Armies[a].HasPathGoal = false; world.Armies[a].PathCursor = 0; }
            foreach (int i in world.SoldierTraversal)
            {
                ref var s = ref world.Soldiers[i];
                s.LocalPath = null; s.TacticalRoute = false; s.Joining = false;
            }
            for (int i = 0; i < world.VillagerCount; i++) { world.Villagers[i].Route = Array.Empty<int>(); world.Villagers[i].RouteCursor = 0; }
        }

        /// <summary>The nearest villagers (distance to the work cell, then id) stop what they do and go to build.</summary>
        private void AssignBuilders(ref BuildingState building)
        {
            var spot = world.Map.Center(building.WorkCell);
            for (int n = 0; n < world.Config.Economy.Builders; n++)
            {
                int best = -1;
                for (int i = 0; i < world.VillagerCount; i++)
                {
                    var v = world.Villagers[i];
                    if (!v.Alive || v.FactionId != building.FactionId || v.Held || v.Task == VillagerTask.ToBuild || v.Task == VillagerTask.Building) continue;
                    if (best < 0 || DistanceSquared(v.Position, spot) < DistanceSquared(world.Villagers[best].Position, spot)) best = i;
                }
                if (best < 0) return;
                world.Villagers[best].HaulFrom = 0; world.Villagers[best].HaulTo = 0;
                world.Villagers[best].Task = VillagerTask.ToBuild;
                world.Villagers[best].BuildingId = building.Id;
            }
        }

        /// <summary>Economy step: builders arrive, work adds up, and finished barracks train infantry.</summary>
        private void AdvanceBuildings()
        {
            var rules = world.Config.Economy;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                ref var v = ref world.Villagers[i];
                if (!v.Alive || (v.Task != VillagerTask.ToBuild && v.Task != VillagerTask.Building)) continue;
                var b = world.Buildings[v.BuildingId - 1];
                if (!b.Alive || b.Complete) { v.Task = VillagerTask.Idle; continue; }
                if (v.Task == VillagerTask.ToBuild && InRange(v.Position, world.Map.Center(b.WorkCell), GatherReach)) v.Task = VillagerTask.Building;
            }
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!b.Alive || b.Complete) continue;
                for (int j = 0; j < world.VillagerCount; j++)
                {
                    var v = world.Villagers[j];
                    if (v.Alive && v.Task == VillagerTask.Building && v.BuildingId == b.Id) b.Progress++;
                }
                if (b.Progress < WorkOf(b.Kind)) continue;
                b.Progress = WorkOf(b.Kind);
                b.Complete = true;
                for (int j = 0; j < world.VillagerCount; j++)
                    if (world.Villagers[j].BuildingId == b.Id && (world.Villagers[j].Task == VillagerTask.Building || world.Villagers[j].Task == VillagerTask.ToBuild))
                        world.Villagers[j].Task = VillagerTask.Idle;
            }
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!b.Alive || !b.Complete || b.Queued == 0) continue;
                if (b.TrainRemaining > 0) b.TrainRemaining--;
                if (b.TrainRemaining > 0) continue;
                // A full population or full armies hold the finished soldier at the door until there is room.
                if (LivingVillagers(b.FactionId) + LivingSoldiers(b.FactionId) >= rules.PopulationCap) continue;
                if (!Spawn(b.FactionId, GoalKind.None, 0, world.Map.Center(b.WorkCell))) continue;
                b.Queued--;
                b.QueuedMetal = b.Queued == 0 ? 0 : Math.Max(0, b.QueuedMetal - InfantryMetalFor(b.FactionId));
                b.TrainRemaining = b.Queued > 0 ? rules.InfantryTrainTicks : 0;
            }
        }

        private int QueuedInfantry(uint faction)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++) if (world.Buildings[i].Alive && world.Buildings[i].FactionId == faction) count += world.Buildings[i].Queued;
            return count;
        }

        /// <summary>The same rule the reinforcement assignment uses: any non-scout army with a free slot.</summary>
        private bool HasInfantryRoom(uint faction)
        {
            int queued = QueuedInfantry(faction), free = 0;
            foreach (uint id in world.Factions[faction - 1].ArmyIds)
            {
                var a = world.Armies[id - 1];
                if (a.Definition.Role == "scout") continue;
                int count = 0;
                foreach (uint soldier in a.SoldierIds) if (world.Soldiers[soldier - 1].Alive) count++;
                free += a.Definition.Capacity - count;
            }
            return free > queued;
        }

        private int SizeOf(BuildingKind kind)
        {
            var e = world.Config.Economy;
            return kind == BuildingKind.Mine ? e.MineSizeCells : kind == BuildingKind.Smelter ? e.SmelterSizeCells : e.BarracksSizeCells;
        }

        private int HpOf(BuildingKind kind)
        {
            var e = world.Config.Economy;
            return kind == BuildingKind.Mine ? e.MineHp : kind == BuildingKind.Smelter ? e.SmelterHp : e.BarracksHp;
        }

        private int WorkOf(BuildingKind kind)
        {
            var e = world.Config.Economy;
            return kind == BuildingKind.Mine ? e.MineWork : kind == BuildingKind.Smelter ? e.SmelterWork : e.BarracksWork;
        }

        private int WoodOf(BuildingKind kind)
        {
            var e = world.Config.Economy;
            return kind == BuildingKind.Mine ? e.MineWoodCost : kind == BuildingKind.Smelter ? e.SmelterWoodCost : e.BarracksWoodCost;
        }

        private int[] Footprint(BuildingState b) => Footprint(b.OriginCell, SizeOf(b.Kind));

        private int[] Footprint(int origin, int size)
        {
            int width = world.Config.Map.WidthCells;
            var cells = new int[size * size];
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++) cells[z * size + x] = origin + z * width + x;
            return cells;
        }

        private SimPoint FootprintCenter(int origin, int size)
        {
            var corner = world.Map.Center(origin);
            long half = Fix64.FromInt(world.Config.Map.CellSizeMeters).Raw * (size - 1) / 2;
            return new SimPoint(Fix64.FromRaw(corner.X.Raw + half), Fix64.FromRaw(corner.Z.Raw + half));
        }

        private int NearestPassableCell(SimPoint from)
        {
            int best = -1;
            for (int i = 0; i < world.Config.Map.WidthCells * world.Config.Map.HeightCells; i++)
                if (world.Map.IsPassable(i) && (best < 0 || DistanceSquared(from, world.Map.Center(i)) < DistanceSquared(from, world.Map.Center(best)))) best = i;
            return best;
        }

        private int Chebyshev(int a, int b)
        {
            int width = world.Config.Map.WidthCells;
            return Math.Max(Math.Abs(a % width - b % width), Math.Abs(a / width - b / width));
        }
    }
}
