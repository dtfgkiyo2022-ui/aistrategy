using System;
using System.Collections.Generic;
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
        // V3-11 #4 provisional simultaneous bridge limit. It is deliberately a simulation rule rather than a
        // contract field, so enabling bridges does not alter the old scenario bytes.
        private const int MaxActiveBridges = 3;

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
            if (CultAllowed(faction) && !DecideCultMonastery(faction)) return;
            if (CultAllowed(faction) && DecideCultMonk(faction)) return;
            if (DecideMonk(faction, ref building)) return;
            if (DecideScout(faction, ref building)) return;
            int population = LivingVillagers(faction) + LivingSoldiers(faction) + economy.Queued + QueuedInfantry(faction);
            if (!EconomyDecision.ShouldTrainInfantry(ready, building.Queued, Math.Min(PlanOf(faction).InfantryQueue, rules.QueueLimit),
                economy.Food, economy.Wood, InfantryFoodFor(faction), InfantryWoodFor(faction), population, PopCapFor(faction), HasInfantryRoom(faction))
                || economy.Metal < InfantryMetalFor(faction) || (SavingToAdvance(faction) && economy.Civ == CivKind.Primitive)) return;
            // V3-5 (32 #8): in the second age, one in three is the civilisation's own unit when it can be paid.
            UnitKind kind;
            // V3-6: steel is the automatic economy's explicit signal to replace an infantry with a heavy infantry.
            // It is checked before the civilisation's one-in-three special unit so the stock is never silently ignored.
            if (ProcessingAvailable(faction) && economy.Steel >= world.Config.Economy.HeavyInfantrySteelCost
                && HasRoomFor(faction, UnitKind.HeavyInfantry) && CanPay(faction, UnitKind.HeavyInfantry)) kind = UnitKind.HeavyInfantry;
            else
            {
                var special = SpecialUnit(faction);
                kind = special != 0 && CanPay(faction, special) && 2 * CountClass(faction, special) < CountClass(faction, UnitKind.Infantry) ? special : UnitKind.Infantry;
            }
            Enqueue(faction, ref building, kind);
        }

        /// <summary>Living line soldiers trained (or queued) as <paramref name="kind"/>; infantry counts those of no class.</summary>
        private int CountClass(uint faction, UnitKind kind)
        {
            int count = QueuedOf(faction, kind);
            foreach (int i in world.SoldierTraversal)
            {
                var s = world.Soldiers[i];
                if (!s.Alive || s.Initial.FactionId != faction || s.Initial.Kind != UnitKind.Infantry) continue;
                if (kind == UnitKind.Infantry ? s.Class == 0 : s.Class == kind) count++;
            }
            return count;
        }

        private void PlaceBarracks(uint faction)
        {
            int origin = FindBarracksSite(faction);
            if (origin < 0) return; // no room near the core: try again next cycle
            PlaceBuildingAt(faction, BuildingKind.Barracks, origin, Facing.North, 0);
        }

        /// <summary>V3-13 #1: a cult keeps one monastery before its automatic monk queue is opened.</summary>
        private bool DecideCultMonastery(uint faction)
        {
            int monastery = OwnBuildingIndex(faction, BuildingKind.Monastery);
            if (monastery >= 0) return world.Buildings[monastery].Complete && !world.Buildings[monastery].Held;
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            if (economy.Wood < rules.MonasteryWoodCost) return false;
            int origin = FindSite(faction, rules.MonasterySizeCells);
            if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.Monastery, origin, Facing.North, 0);
            return false;
        }

        private bool DecideCultMonk(uint faction)
        {
            int monastery = OwnBuildingIndex(faction, BuildingKind.Monastery);
            if (monastery < 0) return false;
            ref var building = ref world.Buildings[monastery];
            if (!building.Complete || building.Held || building.Queued >= world.Config.Economy.QueueLimit) return false;
            if (QueuedOf(faction, UnitKind.Monk) + LivingClass(faction, UnitKind.Monk) >= 2) return false;
            int population = LivingVillagers(faction) + LivingSoldiers(faction) + world.Economies[faction - 1].Queued + QueuedInfantry(faction);
            if (population >= PopCapFor(faction) || !HasRoomFor(faction, UnitKind.Monk) || !CanPay(faction, UnitKind.Monk, BuildingKind.Monastery)) return false;
            Enqueue(faction, ref building, UnitKind.Monk);
            return true;
        }

        /// <summary>Pays, closes the footprint, and sends the builders. The caller has checked the site and the wood.</summary>
        private void PlaceBuildingAt(uint faction, BuildingKind kind, int origin, Facing facing, uint nodeId)
            => PlaceBuildingAt(faction, kind, origin, facing, nodeId, null, -1);

        private void PlaceBuildingAt(uint faction, BuildingKind kind, int origin, Facing facing, uint nodeId, int[] bridgeCells, int requestedWorkCell = -1)
        {
            ref var economy = ref world.Economies[faction - 1];
            economy.Wood = checked(economy.Wood - WoodOf(kind, faction));
            economy.Stone = checked(economy.Stone - StoneOf(kind, faction));
            int index = world.BuildingCount;
            if (index == world.Buildings.Length) Array.Resize(ref world.Buildings, index == 0 ? 4 : checked(index * 2));
            var footprint = kind == BuildingKind.Bridge ? bridgeCells : Footprint(origin, SizeOf(kind));
            if (footprint == null || footprint.Length == 0) return;
            foreach (int cell in footprint) world.Map.SetPassable(cell, false);
            world.Buildings[index] = new BuildingState { Id = world.NextBuildingId, FactionId = faction, Kind = kind, OriginCell = origin,
                BridgeCells = kind == BuildingKind.Bridge ? (int[])bridgeCells.Clone() : null,
                WorkCell = requestedWorkCell >= 0 ? requestedWorkCell : NearestPassableCell(kind == BuildingKind.Bridge ? BridgeCenter(bridgeCells) : FootprintCenter(origin, SizeOf(kind))),
                Alive = true, Hp = HpOf(kind, faction, origin), Facing = facing, NodeId = nodeId };
            world.NextBuildingId = checked(world.NextBuildingId + 1);
            if (nodeId != 0) ReleaseNode(nodeId);
            if (kind == BuildingKind.Farm) world.Buildings[index].Interval = FarmInterval(origin);
            else if (kind == BuildingKind.LumberCamp) world.Buildings[index].Interval = world.Config.Economy.LumberCampIntervalTicks;
            else if (kind == BuildingKind.Quarry) world.Buildings[index].Interval = world.Config.Economy.QuarryIntervalTicks;
            EvacuateFootprint(footprint);
            TerrainChanged();
            AssignBuilders(ref world.Buildings[index]);
        }

        private bool TryPlaceBridge(uint faction, EconomyCommand command)
        {
            if (!TryValidateBridge(faction, command.Cells, command.Facing, out int[] cells, out int workCell)) return false;
            if (world.Economies[faction - 1].Wood < WoodOf(BuildingKind.Bridge, faction)) return false;
            PlaceBuildingAt(faction, BuildingKind.Bridge, cells[0], command.Facing, 0, cells, workCell);
            world.Buildings[world.BuildingCount - 1].Held = true;
            return true;
        }

        /// <summary>V3-11 #2: the hand-placement rules are also the rules used by the automatic candidate search.</summary>
        private bool TryValidateBridge(uint faction, IReadOnlyList<int> requested, Facing facing, out int[] cells, out int workCell,
            bool requireCamp = true, bool requireCiv = true)
        {
            cells = null;
            workCell = -1;
            bool campReady = false;
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive && world.Buildings[i].Complete && world.Buildings[i].FactionId == faction
                    && world.Buildings[i].Kind == BuildingKind.EngineerCamp) { campReady = true; break; }
            if ((requireCiv ? !BridgeAllowed(faction) : !BridgeOn) || requireCamp && !campReady
                || requireCamp && ActiveBridgeCount(faction) >= MaxActiveBridges || (byte)facing > 3 || requested == null
                || requested.Count == 0 || requested.Count > world.Config.Economy.MaxBridgeLength) return false;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            cells = new int[requested.Count];
            for (int i = 0; i < cells.Length; i++) cells[i] = requested[i];
            int first = cells[0], second = cells.Length == 1 ? first + 1 : cells[1], delta = second - first;
            bool horizontal = delta == 1 || delta == -1;
            bool vertical = delta == width || delta == -width;
            if (!horizontal && !vertical) { cells = null; return false; }
            var seen = new HashSet<int>();
            for (int i = 0; i < cells.Length; i++)
            {
                int cell = cells[i];
                if (cell < 0 || cell >= width * height || !seen.Add(cell) || !IsRiverCell(cell) || world.Map.IsPassable(cell)) { cells = null; return false; }
                if (i > 0 && cells[i] != cells[i - 1] + delta) { cells = null; return false; }
            }
            int before = first - delta, after = cells[cells.Length - 1] + delta;
            if (before < 0 || before >= width * height || after < 0 || after >= width * height
                || horizontal && (before / width != first / width || after / width != first / width)
                || vertical && (before % width != first % width || after % width != first % width)
                || !world.Map.IsPassable(before) || !world.Map.IsPassable(after)
                || IsRiverCell(before) || IsRiverCell(after)) { cells = null; return false; }
            foreach (int cell in cells)
            {
                foreach (var node in world.Nodes)
                    if (world.Map.Cell(node.Definition.Position) == cell) { cells = null; return false; }
                for (int i = 0; i < world.BuildingCount; i++)
                        if (world.Buildings[i].Alive && Array.IndexOf(Footprint(world.Buildings[i]), cell) >= 0) { cells = null; return false; }
            }
            for (int i = 0; i < world.VillagerCount && workCell < 0; i++)
            {
                var villager = world.Villagers[i];
                if (!villager.Alive || villager.FactionId != faction) continue;
                int start = world.Map.Cell(villager.Position);
                if (world.Map.FindPath(start, world.Map.Center(before)).Length > 0) workCell = before;
                else if (world.Map.FindPath(start, world.Map.Center(after)).Length > 0) workCell = after;
            }
            if (workCell < 0) { cells = null; return false; }
            return true;
        }

        private int ActiveBridgeCount(uint faction)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive && world.Buildings[i].FactionId == faction && world.Buildings[i].Kind == BuildingKind.Bridge) count++;
            return count;
        }

        /// <summary>
        /// Rings around the core cell, nearest first; within a ring by z then x. The first footprint that is on free
        /// ground, keeps its distance from the core, resources and buildings, and does not cut the map apart wins.
        /// </summary>
        private int FindBarracksSite(uint faction) => FindSite(faction, world.Config.Economy.BarracksSizeCells);

        /// <summary>The same ring search for any square footprint of <paramref name="size"/> cells.</summary>
        private int FindSite(uint faction, int size) => FindSiteNear(faction, size, world.Map.Cell(OwnCore(faction).Definition.Position));

        /// <summary>The ring search around any <paramref name="centre"/> cell (the clearance from the own core still applies).</summary>
        private int FindSiteNear(uint faction, int size, int centre)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int core = world.Map.Cell(OwnCore(faction).Definition.Position), cx = centre % width, cz = centre / width;
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
                if (!world.Map.IsPassable(cell) || IsRiverCell(cell) || Chebyshev(cell, coreCell) < CoreClearanceCells) return false;
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

        /// <summary>
        /// V3-5 (32.4): an own unfinished building nobody is building gets builders. A building placed while every
        /// villager was busy building something else was otherwise left unbuilt for good. Maps with ages only, so the
        /// older maps keep every tick; a building the player placed is theirs to staff.
        /// </summary>
        private void ResumeUnbuilt(uint faction)
        {
            if (!AgesOn) return;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive || b.Complete || b.Held || b.FactionId != faction) continue;
                bool staffed = false;
                for (int j = 0; j < world.VillagerCount && !staffed; j++)
                {
                    var v = world.Villagers[j];
                    staffed = v.Alive && v.BuildingId == b.Id && (v.Task == VillagerTask.ToBuild || v.Task == VillagerTask.Building);
                }
                if (!staffed) AssignBuilders(ref world.Buildings[i]);
            }
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
                // V3-5 (32 #15): a finished building that is hurt is still work - the villager repairs it instead.
                if (!b.Alive || (b.Complete && !NeedsRepair(b))) { v.Task = VillagerTask.Idle; continue; }
                if (v.Task == VillagerTask.ToBuild && InRange(v.Position, world.Map.Center(b.WorkCell), GatherReach)) v.Task = VillagerTask.Building;
            }
            RepairBuildings();
            bool opened = false;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!b.Alive || b.Complete) continue;
                for (int j = 0; j < world.VillagerCount; j++)
                {
                    var v = world.Villagers[j];
                    if (v.Alive && v.Task == VillagerTask.Building && v.BuildingId == b.Id) b.Progress++;
                }
                if (b.Progress < WorkOf(b.Kind, b.FactionId)) continue;
                b.Progress = WorkOf(b.Kind, b.FactionId);
                b.Complete = true;
                if (b.Kind == BuildingKind.Bridge)
                {
                    foreach (int cell in Footprint(b)) world.Map.SetPassable(cell, true);
                    opened = true;
                }
                for (int j = 0; j < world.VillagerCount; j++)
                    if (world.Villagers[j].BuildingId == b.Id && (world.Villagers[j].Task == VillagerTask.Building || world.Villagers[j].Task == VillagerTask.ToBuild))
                        world.Villagers[j].Task = VillagerTask.Idle;
            }
            if (opened) TerrainChanged();
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!b.Alive || !b.Complete || b.Queued == 0) continue;
                if (b.TrainRemaining > 0) b.TrainRemaining--;
                if (b.TrainRemaining > 0) continue;
                // A full population or full armies hold the finished soldier at the door until there is room.
                if (LivingVillagers(b.FactionId) + LivingSoldiers(b.FactionId) >= PopCapFor(b.FactionId)) continue;
                var unit = QueueAt(b, 0);
                // Forged only when its metal was paid (a soldier queued in the primitive age paid none).
                int metal = InfantryMetalFor(b.FactionId);
                bool paid = unit == UnitKind.Infantry && metal > 0 && b.QueuedMetal >= metal;
                var spawnKind = unit == UnitKind.Scout || unit == UnitKind.Monk ? unit : UnitKind.Infantry;
                if (!Spawn(b.FactionId, GoalKind.None, 0, world.Map.Center(b.WorkCell), spawnKind, unit)) continue;
                ApplyClass(world.SoldierCount - 1, unit);
                if (paid) ForgeIfMetallurgy(b.FactionId, world.SoldierCount - 1);
                ApplySoldierTechs(b.FactionId, world.SoldierCount - 1);
                Dequeue(b.FactionId, ref b, unit);
            }
        }

        /// <summary>Every unit queued in the faction's buildings (for the population).</summary>
        private int QueuedInfantry(uint faction)
        {
            int count = 0;
            for (int i = 0; i < world.BuildingCount; i++) if (world.Buildings[i].Alive && world.Buildings[i].FactionId == faction) count += world.Buildings[i].Queued;
            return count;
        }

        /// <summary>The same rule the reinforcement assignment uses: any non-scout army with a free slot.</summary>
        private bool HasInfantryRoom(uint faction) => HasRoomFor(faction, UnitKind.Infantry);

        private int SizeOf(BuildingKind kind)
        {
            var e = world.Config.Economy;
            return kind == BuildingKind.Mine ? e.MineSizeCells : kind == BuildingKind.LumberCamp ? e.LumberCampSizeCells : kind == BuildingKind.Quarry ? e.QuarrySizeCells : kind == BuildingKind.Smelter ? e.SmelterSizeCells : kind == BuildingKind.CharcoalKiln ? e.CharcoalKilnSizeCells
                : kind == BuildingKind.Steelworks ? e.SteelworksSizeCells : kind == BuildingKind.Farm ? e.FarmSizeCells
                : kind == BuildingKind.Fletcher ? e.FletcherSizeCells
                : kind == BuildingKind.House ? e.HouseSizeCells : kind == BuildingKind.DropSite ? e.DropSiteSizeCells
                : kind == BuildingKind.Wall ? 1 : kind == BuildingKind.Tower ? e.TowerSizeCells : kind == BuildingKind.Blacksmith ? e.BlacksmithSizeCells
                : kind == BuildingKind.Market ? e.MarketSizeCells : kind == BuildingKind.SiegeWorkshop ? e.WorkshopSizeCells
                : kind == BuildingKind.ArcheryRange ? e.RangeSizeCells : kind == BuildingKind.Stable ? e.StableSizeCells
                : kind == BuildingKind.Castle ? e.CastleSizeCells : kind == BuildingKind.Caravanserai ? e.CaravanseraiSizeCells
                : kind == BuildingKind.EngineerCamp ? e.EngineerCampSizeCells
                : kind == BuildingKind.Academy ? e.AcademySizeCells
                : kind == BuildingKind.Monastery ? e.MonasterySizeCells
                : kind == BuildingKind.Harbor ? e.HarborSizeCells
                : kind == BuildingKind.MineShaft ? e.MountainSizeCells
                : kind == BuildingKind.Bridge ? 1 : e.BarracksSizeCells;
        }

        private int HpOf(BuildingKind kind, uint faction, int origin = -1)
        {
            var e = world.Config.Economy;
            int hp = kind == BuildingKind.Mine ? e.MineHp : kind == BuildingKind.LumberCamp ? e.LumberCampHp : kind == BuildingKind.Quarry ? e.QuarryHp : kind == BuildingKind.Smelter ? e.SmelterHp : kind == BuildingKind.CharcoalKiln ? e.CharcoalKilnHp
                : kind == BuildingKind.Steelworks ? e.SteelworksHp : kind == BuildingKind.Farm ? e.FarmHp
                : kind == BuildingKind.Fletcher ? e.FletcherHp
                : kind == BuildingKind.House ? e.HouseHp : kind == BuildingKind.DropSite ? e.DropSiteHp
                : kind == BuildingKind.Wall ? e.WallHp : kind == BuildingKind.Tower ? e.TowerHp : kind == BuildingKind.Blacksmith ? e.BlacksmithHp
                : kind == BuildingKind.Market ? e.MarketHp : kind == BuildingKind.SiegeWorkshop ? e.WorkshopHp
                : kind == BuildingKind.ArcheryRange ? e.RangeHp : kind == BuildingKind.Stable ? e.StableHp
                : kind == BuildingKind.Castle ? e.CastleHp : kind == BuildingKind.Caravanserai ? e.CaravanseraiHp
                : kind == BuildingKind.EngineerCamp ? e.EngineerCampHp : kind == BuildingKind.Academy ? e.AcademyHp
                : kind == BuildingKind.Monastery ? e.MonasteryHp : kind == BuildingKind.Harbor ? e.HarborHp : kind == BuildingKind.MineShaft ? e.MountainHp : kind == BuildingKind.Bridge ? BridgeHpForBuilding(faction) : e.BarracksHp;
            if (origin >= 0 && (kind == BuildingKind.Wall || kind == BuildingKind.Tower))
                hp = MountainFortHp(hp, faction, origin, SizeOf(kind));
            return hp;
        }

        private bool IsMasonryDefence(uint faction, BuildingKind kind)
            => MasonryAllowed(faction) && (kind == BuildingKind.Wall || kind == BuildingKind.Tower || kind == BuildingKind.Castle);

        private int MasonryDiscount(int value, int permille)
        {
            // All defence discounts use integer multiplication followed by floor division by 1000; no floating point or rounding is involved.
            return checked(value * permille / 1000);
        }

        private int WorkOf(BuildingKind kind, uint faction)
        {
            var e = world.Config.Economy;
            int work = kind == BuildingKind.Mine ? e.MineWork : kind == BuildingKind.LumberCamp ? e.LumberCampWork : kind == BuildingKind.Quarry ? e.QuarryWork : kind == BuildingKind.Smelter ? e.SmelterWork : kind == BuildingKind.CharcoalKiln ? e.CharcoalKilnWork
                : kind == BuildingKind.Steelworks ? e.SteelworksWork : kind == BuildingKind.Farm ? e.FarmWork
                : kind == BuildingKind.Fletcher ? e.FletcherWork
                : kind == BuildingKind.House ? e.HouseWork : kind == BuildingKind.DropSite ? e.DropSiteWork
                : kind == BuildingKind.Wall ? 1 : kind == BuildingKind.Tower ? e.TowerWork : kind == BuildingKind.Blacksmith ? e.BlacksmithWork
                : kind == BuildingKind.Market ? e.MarketWork : kind == BuildingKind.SiegeWorkshop ? e.WorkshopWork
                : kind == BuildingKind.ArcheryRange ? e.RangeWork : kind == BuildingKind.Stable ? e.StableWork
                : kind == BuildingKind.Castle ? e.CastleWork : kind == BuildingKind.Caravanserai ? e.CaravanseraiWork
                : kind == BuildingKind.EngineerCamp ? e.EngineerCampWork : kind == BuildingKind.Academy ? e.AcademyWork
                : kind == BuildingKind.Monastery ? e.MonasteryWork : kind == BuildingKind.Harbor ? e.HarborWork : kind == BuildingKind.MineShaft ? e.MountainWork : kind == BuildingKind.Bridge ? BridgeWorkFor(faction) : e.BarracksWork;
            return IsMasonryDefence(faction, kind) ? MasonryDiscount(work, e.MasonryDefenceWorkPermille) : work;
        }

        private int WoodOf(BuildingKind kind, uint faction)
        {
            var e = world.Config.Economy;
            int wood = kind == BuildingKind.Mine ? e.MineWoodCost : kind == BuildingKind.LumberCamp ? e.LumberCampWoodCost : kind == BuildingKind.Quarry ? e.QuarryWoodCost : kind == BuildingKind.Smelter ? e.SmelterWoodCost : kind == BuildingKind.CharcoalKiln ? e.CharcoalKilnWoodCost
                : kind == BuildingKind.Steelworks ? e.SteelworksWoodCost : kind == BuildingKind.Farm ? e.FarmWoodCost
                : kind == BuildingKind.Fletcher ? e.FletcherWoodCost
                : kind == BuildingKind.House ? e.HouseWoodCost : kind == BuildingKind.DropSite ? e.DropSiteWoodCost
                : kind == BuildingKind.Wall ? 0 : kind == BuildingKind.Tower ? e.TowerWoodCost : kind == BuildingKind.Blacksmith ? e.BlacksmithWoodCost
                : kind == BuildingKind.Market ? e.MarketWoodCost : kind == BuildingKind.SiegeWorkshop ? e.WorkshopWoodCost
                : kind == BuildingKind.ArcheryRange ? e.RangeWoodCost : kind == BuildingKind.Stable ? e.StableWoodCost
                : kind == BuildingKind.Castle ? e.CastleWoodCost : kind == BuildingKind.Caravanserai ? e.CaravanseraiWoodCost
                 : kind == BuildingKind.EngineerCamp ? e.EngineerCampWoodCost : kind == BuildingKind.Academy ? e.AcademyWoodCost
                 : kind == BuildingKind.Monastery ? e.MonasteryWoodCost : kind == BuildingKind.Harbor ? e.HarborWoodCost : kind == BuildingKind.MineShaft ? e.MountainWoodCost : kind == BuildingKind.Bridge ? e.BridgeWoodCost : e.BarracksWoodCost;
            return IsMasonryDefence(faction, kind) ? MasonryDiscount(wood, e.MasonryDefenceCostPermille) : wood;
        }

        // Keep the age-tech effect local to bridge buildings. Existing non-bridge building rules,
        // including flag-off maps, continue to use their original values unchanged.
        private int BridgeHpForBuilding(uint faction)
            => BridgeOn && world.Economies[faction - 1].Civ == CivKind.Bridge ? BridgeHpFor(faction) : world.Config.Economy.BridgeHp;

        /// <summary>V3-5: the stone a building costs (walls and towers).</summary>
        private int StoneOf(BuildingKind kind, uint faction)
        {
            var e = world.Config.Economy;
            int stone = kind == BuildingKind.Wall ? e.WallStoneCost : kind == BuildingKind.Tower ? e.TowerStoneCost
                : kind == BuildingKind.Castle ? e.CastleStoneCost : 0;
            return IsMasonryDefence(faction, kind) ? MasonryDiscount(stone, e.MasonryDefenceCostPermille) : stone;
        }

        private int[] Footprint(BuildingState b) => b.Kind == BuildingKind.Bridge ? b.BridgeCells ?? System.Array.Empty<int>() : Footprint(b.OriginCell, SizeOf(b.Kind));

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

        private SimPoint BridgeCenter(int[] cells)
        {
            if (cells == null || cells.Length == 0) return world.Map.Center(0);
            var first = world.Map.Center(cells[0]);
            var last = world.Map.Center(cells[cells.Length - 1]);
            return new SimPoint(Fix64.FromRaw((first.X.Raw + last.X.Raw) / 2), Fix64.FromRaw((first.Z.Raw + last.Z.Raw) / 2));
        }

        private SimPoint BuildingCenter(BuildingState building)
            => building.Kind == BuildingKind.Bridge ? BridgeCenter(building.BridgeCells) : FootprintCenter(building.OriginCell, SizeOf(building.Kind));

        private bool IsRiverCell(int cell)
            => world.Config.Map.Terrain.Length != 0 && cell >= 0 && cell < world.Config.Map.Terrain.Length
                && world.Config.Map.Terrain[cell] == (byte)TerrainKind.River;

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
