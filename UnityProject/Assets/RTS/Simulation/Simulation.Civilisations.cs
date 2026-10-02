using System;
using Rts.Contracts;
using Rts.Decision;

namespace Rts.Simulation
{
    public sealed partial class Simulation
    {
        /// <summary>
        /// The explicit civilisation order is part of the rules: lower Priority wins an equal score. Disabled rows
        /// remain in the table with score zero, so an optional civilisation cannot steal an older zero-score tie.
        /// </summary>
        private readonly struct CivRegistration
        {
            public CivKind Civ { get; }
            public int Priority { get; }
            public bool UsesFoodMarket { get; }
            public Func<Simulation, uint, bool> Enabled { get; }
            public Func<Simulation, uint, SimPoint, int, int, int> Score { get; }
            public Func<Simulation, uint, bool> LineStarted { get; }
            /// <summary>The civilisation's first own building (its foundation); 0 when it has none.</summary>
            public BuildingKind Foundation { get; }
            /// <summary>Whether the foundation could be placed now apart from its cost (null: always).</summary>
            public Func<Simulation, uint, bool> FoundationReady { get; }

            public CivRegistration(CivKind civ, int priority, bool usesFoodMarket,
                Func<Simulation, uint, bool> enabled,
                Func<Simulation, uint, SimPoint, int, int, int> score,
                Func<Simulation, uint, bool> lineStarted,
                BuildingKind foundation = 0,
                Func<Simulation, uint, bool> foundationReady = null)
            {
                Civ = civ;
                Priority = priority;
                UsesFoodMarket = usesFoodMarket;
                Enabled = enabled;
                Score = score;
                LineStarted = lineStarted;
                Foundation = foundation;
                FoundationReady = foundationReady;
            }
        }

        /// <summary>
        /// One row per selectable civilisation. Score callbacks only point at the existing scoring functions; the
        /// scoring functions themselves stay unchanged. Agrarian and metallurgy are enabled by Ages alone.
        /// </summary>
        private static readonly CivRegistration[] CivRegistrations =
        {
            new CivRegistration(CivKind.Agrarian, 0, false, (s, f) => s.AgesOn, AgrarianScore,
                (s, f) => s.OwnBuildingIndex(f, BuildingKind.Farm) >= 0),
            new CivRegistration(CivKind.Metallurgy, 1, true, (s, f) => s.AgesOn, MetallurgyScore,
                (s, f) => s.OwnBuildingIndex(f, BuildingKind.Mine) >= 0 && s.OwnBuildingIndex(f, BuildingKind.Smelter) >= 0),
            new CivRegistration(CivKind.Forestry, 2, true, (s, f) => s.ForestryOn, ForestryScore,
                (s, f) => s.ForestryAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.LumberCamp) >= 0),
            new CivRegistration(CivKind.Masonry, 3, true, (s, f) => s.MasonryOn, MasonryScore,
                (s, f) => s.MasonryAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.Quarry) >= 0),
            new CivRegistration(CivKind.Caravan, 4, true, (s, f) => s.CaravanOn, CaravanScore,
                (s, f) => s.CaravanAllowed(f) && s.OwnFinishedMarketIndex(f) >= 0),
            new CivRegistration(CivKind.Cavalry, 5, true, (s, f) => s.CavalryOn, CavalryScore,
                (s, f) => s.CavalryAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.Stable) >= 0),
            new CivRegistration(CivKind.Bridge, 6, true, (s, f) => s.BridgeOn, BridgeScore,
                (s, f) => s.BridgeAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.EngineerCamp) >= 0),
            new CivRegistration(CivKind.Academy, 7, true, (s, f) => s.AcademyOn, AcademyScore,
                (s, f) => s.AcademyAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.Academy) >= 0),
            new CivRegistration(CivKind.Cult, 8, true, (s, f) => s.CultOn, CultScore,
                (s, f) => s.CultAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.Monastery) >= 0),
            new CivRegistration(CivKind.Fishing, 9, false, (s, f) => s.FishingOn, FishingScore,
                (s, f) => s.FishingAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.Harbor) >= 0),
            new CivRegistration(CivKind.Mountain, 10, true, (s, f) => s.MountainOn, MountainScore,
                (s, f) => s.MountainAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.MineShaft) >= 0),
            new CivRegistration(CivKind.Tollgate, 11, true, (s, f) => s.TollgateOn, TollgateScore,
                (s, f) => s.TollgateAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.Tollgate) >= 0,
                BuildingKind.Tollgate, (s, f) => s.TollgateFoundationReady(f)),
            new CivRegistration(CivKind.Metropolis, 12, true, (s, f) => s.MetropolisOn, MetropolisScore,
                (s, f) => s.MetropolisAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.GrandHouse) >= 0),
            new CivRegistration(CivKind.Sanctuary, 13, true, (s, f) => s.SanctuaryOn, SanctuaryScore,
                (s, f) => s.SanctuaryAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.Shrine) >= 0,
                BuildingKind.Shrine, (s, f) => s.SanctuaryFoundationReady(f))
        };

        private static int AgrarianScore(Simulation s, uint faction, SimPoint core, int ore, int food)
            => food > GuaranteedFoodPoints ? food - GuaranteedFoodPoints : 0;

        private static int MetallurgyScore(Simulation s, uint faction, SimPoint core, int ore, int food) => ore;

        private static int ForestryScore(Simulation s, uint faction, SimPoint core, int ore, int food)
            => s.CountUsableForestWood(faction, core);

        private static int MasonryScore(Simulation s, uint faction, SimPoint core, int ore, int food)
            => s.CountUsableMasonryStone(faction, core);

        private static int CaravanScore(Simulation s, uint faction, SimPoint core, int ore, int food)
            => s.CountUsableCaravanOutposts(faction, core);

        private static int CavalryScore(Simulation s, uint faction, SimPoint core, int ore, int food)
            => s.CountCavalryMobility(faction, core);

        private static int BridgeScore(Simulation s, uint faction, SimPoint core, int ore, int food)
            => s.CountUsableBridgeSaving(faction, core);

        /// <summary>
        /// Academy's choice score is deliberately a small tier, not a raw distance or cell count. Only gold that
        /// the faction has explored, can reach from its core through explored passable cells, can still fund the first
        /// academy research, and is not near a currently visible enemy is useful at choice time.
        /// </summary>
        private static int AcademyScore(Simulation s, uint faction, SimPoint core, int ore, int food)
        {
            if (!s.AcademyOn) return 0;
            int usable = s.CountUsableAcademyGold(faction, core);
            return usable >= 2 ? 3 : usable == 1 ? 2 : 0;
        }

        private int CountUsableAcademyGold(uint faction, SimPoint core)
        {
            var rules = world.Config.Economy;
            var explored = world.Factions[faction - 1].ExploredCells;
            var visible = world.Factions[faction - 1].VisibleCells;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int cells = checked(width * height);
            int coreCell = world.Map.Cell(core);
            var passable = new bool[cells];
            Array.Fill(passable, world.Config.Map.DefaultPassable);
            foreach (int blocked in world.Config.Map.BlockedCellIds) passable[blocked] = false;

            int danger = rules.GoldDangerMeters;
            long dangerSquared = checked(Fix64.FromInt(danger).Raw * Fix64.FromInt(danger).Raw);
            int usable = 0;
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                var node = world.Nodes[i];
                if (node.Definition.Kind != ResourceKind.Gold || node.Remaining < rules.AcademyToolsGoldCost) continue;
                int nodeCell = world.Map.Cell(node.Definition.Position);
                if (nodeCell < 0 || nodeCell >= cells || !explored[nodeCell]) continue;
                if (RouteDistance.Measure(width, height, passable, explored, coreCell, nodeCell, null) < 0) continue;

                bool enemyNear = false;
                foreach (int soldierIndex in world.SoldierTraversal)
                {
                    var enemy = world.Soldiers[soldierIndex];
                    if (!enemy.Alive || enemy.Initial.FactionId == faction) continue;
                    int enemyCell = world.Map.Cell(enemy.Position);
                    if (enemyCell < 0 || enemyCell >= visible.Length || !visible[enemyCell]) continue;
                    long dx = enemy.Position.X.Raw - node.Definition.Position.X.Raw;
                    long dz = enemy.Position.Z.Raw - node.Definition.Position.Z.Raw;
                    if (checked(dx * dx + dz * dz) <= dangerSquared) { enemyNear = true; break; }
                }
                if (!enemyNear) usable++;
            }
            return usable;
        }

        // Route length (metres) from the own core to the enemy core: at most the first is 3 points, at most the second 2.
        // Measured on generated maps (seeds 1..60, 2m cells): the core-to-core route is 180..268m; 204m keeps the
        // shortest ~22% of maps (13/60) and 218m the next ~20% (12/60).
        private const int CultShortRouteMeters = 204, CultMiddleRouteMeters = 218;

        /// <summary>
        /// The cult wants enemies that arrive soon (more chances to convert): the choice score is a small tier from the
        /// route length between the two cores over the scenario terrain (cores and ground are public, as for the
        /// sanctuary score), so it is known at choice time. A valid observation of a high-value enemy soldier (V3-13 #4
        /// memory, kept in the canonical state) adds one point, never above 3.
        /// </summary>
        private static int CultScore(Simulation s, uint faction, SimPoint core, int ore, int food)
        {
            if (!s.CultOn) return 0;
            int meters = s.CultCoreRouteMeters(faction, core);
            int tier = meters < 0 ? 0 : meters <= CultShortRouteMeters ? 3 : meters <= CultMiddleRouteMeters ? 2 : 0;
            if (s.CountValidCultObservations(faction) > 0) tier++;
            return Math.Min(3, tier);
        }

        /// <summary>
        /// Route length in metres from <paramref name="core"/> to the enemy core over the scenario's terrain passability
        /// (player buildings are not subtracted), or -1 when unreachable. One <see cref="RouteDistance.Measure"/>.
        /// </summary>
        private int CultCoreRouteMeters(uint faction, SimPoint core)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int coreCell = world.Map.Cell(core);
            int enemyCell = world.Map.Cell(world.Cores[world.Factions[2 - faction].CoreId - 1].Definition.Position);
            if (coreCell < 0 || enemyCell < 0) return -1;
            var passable = new bool[checked(width * height)];
            Array.Fill(passable, world.Config.Map.DefaultPassable);
            foreach (int blocked in world.Config.Map.BlockedCellIds) passable[blocked] = false;
            int edges = RouteDistance.Measure(width, height, passable, null, coreCell, enemyCell, null);
            return edges < 0 ? -1 : checked(edges * world.Config.Map.CellSizeMeters);
        }

        private const int CultObservationValidityTicks = 600;

        private int CountValidCultObservations(uint faction)
        {
            var memory = world.Factions[faction - 1];
            int count = 0;
            for (int i = 0; i < memory.CultObservedKinds.Length; i++)
            {
                if (memory.CultObservedValidUntilTicks[i] < world.Tick) continue;
                if (CultObservedValue(memory.CultObservedKinds[i]) > 0) count++;
            }
            return count;
        }

        private static int CultObservedValue(UnitKind kind)
            => kind == UnitKind.HeavyInfantry ? 6 : kind == UnitKind.Cavalry ? 5 : kind == UnitKind.LightCavalry ? 4
                : kind == UnitKind.Mercenary ? 3 : kind == UnitKind.Archer || kind == UnitKind.SkirmishArcher ? 2 : 0;

        // V3-18 #3: an outpost counts for the sanctuary when its route from the core is at most this many metres.
        private const int CivSanctuaryRouteReach = 120;

        /// <summary>
        /// V3-18 #3: the sanctuary choice score is a small tier from the number of public outposts close to the core
        /// (route length from the core at most <see cref="CivSanctuaryRouteReach"/>): none 0, one 2, two or more 3.
        /// The caravan counts outposts that can host a caravanserai, wherever they are; this counts outposts the core
        /// can reach quickly, whether or not a building fits beside them, so the two read the same outposts differently.
        /// </summary>
        private static int SanctuaryScore(Simulation s, uint faction, SimPoint core, int ore, int food)
        {
            if (!s.SanctuaryOn) return 0;
            int near = s.CountNearSanctuaryOutposts(faction, core);
            return near >= 2 ? 3 : near == 1 ? 2 : 0;
        }

        /// <summary>
        /// Counts outposts whose four-way route from the core, over the scenario's terrain passability (outposts and
        /// the ground are public; player buildings are not subtracted, so the faction's own early houses do not lower
        /// it), is at most <see cref="CivSanctuaryRouteReach"/> metres. A straight-line check comes first (a four-way
        /// route is never shorter), and <see cref="RouteDistance.Measure"/> runs once per remaining outpost - never per
        /// map cell. The map is only read.
        /// </summary>
        private int CountNearSanctuaryOutposts(uint faction, SimPoint core)
        {
            int count = 0;
            for (int i = 0; i < world.Outposts.Length; i++)
            {
                int meters = SanctuaryOutpostRouteMeters(core, i);
                if (meters >= 0 && meters <= CivSanctuaryRouteReach) count++;
            }
            return count;
        }

        /// <summary>Route length in metres from the core to outpost <paramref name="index"/>, or -1 when it is beyond
        /// the straight-line reach or cannot be reached.</summary>
        private int SanctuaryOutpostRouteMeters(SimPoint core, int index)
        {
            var post = world.Outposts[index].Definition.Position;
            if (!InRange(post, core, Fix64.FromInt(CivSanctuaryRouteReach))) return -1;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int coreCell = world.Map.Cell(core), postCell = world.Map.Cell(post);
            if (coreCell < 0 || postCell < 0) return -1;
            var passable = new bool[checked(width * height)];
            Array.Fill(passable, world.Config.Map.DefaultPassable);
            foreach (int blocked in world.Config.Map.BlockedCellIds) passable[blocked] = false;
            int edges = RouteDistance.Measure(width, height, passable, null, coreCell, postCell, null);
            return edges < 0 ? -1 : checked(edges * world.Config.Map.CellSizeMeters);
        }

        /// <summary>
        /// Mountain's choice score is based on the first legal shaft edges the faction could have known at
        /// civilisation choice. A site with several observed mountain neighbours is worth several edge units;
        /// this keeps a compact, high-value cliff from being treated like a single isolated mountain cell while
        /// still returning a small 0/2/3 score scale, with 4 for a very long edge (MountainWideEdgeUnits).
        /// </summary>
        private static int MountainScore(Simulation s, uint faction, SimPoint core, int ore, int food)
        {
            if (!s.MountainOn) return 0;
            int edgeUnits = s.CountUsableMountainShaftEdgeUnits(faction, core);
            return edgeUnits >= MountainWideEdgeUnits ? 4 : edgeUnits >= 3 ? 3 : edgeUnits > 0 ? 2 : 0;
        }

        // A very long usable mountain edge is worth 4. Measured at the normal choice (seeds 1..60, both factions): the
        // mountain was usually 3 and lost to ore or food of 3 or more (it is tenth in the tie order); 24 edge units or more
        // is the top ~22% (26/120). Cavalry still reaches 5 and the ore/food/forest/stone counts are unbounded.
        private const int MountainWideEdgeUnits = 24;

        /// <summary>
        /// Fishing's terrain score is a small tier based on usable fish points, not on raw food or distance.
        /// A point must already be explored, be within the same 30m starting-food reach used by agrarian, and have
        /// at least one legal, connected harbour site within the existing fish reach. This keeps fishing distinct from
        /// agrarian: food beyond the guaranteed three points favours farming, while river fish that can actually feed a
        /// harbour favours fishing.
        /// </summary>
        private static int FishingScore(Simulation s, uint faction, SimPoint core, int ore, int food)
        {
            if (!s.FishingOn) return 0;
            int usable = s.CountUsableFishingFish(faction, core);
            return usable >= 3 ? 3 : usable >= 1 ? 2 : 0;
        }

        /// <summary>
        /// V3-16 #3: the number of narrow places on the route from the own core to the enemy core and outposts whose
        /// closing lengthens (but does not cut) that route. The terrain and the positions of the cores and outposts are
        /// public (as for the sanctuary score), so the whole map is known to the scoring and the score exists at choice
        /// time. Terrain only (no building), and one route search per narrow stretch, never one per map cell.
        /// </summary>
        private static int TollgateScore(Simulation s, uint faction, SimPoint core, int ore, int food)
        {
            if (!s.TollgateOn) return 0;
            return TollgateTerrainScoring.Points(s.CountTollgateChokeSites(faction, core));
        }

        private int CountTollgateChokeSites(uint faction, SimPoint core)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int count = checked(width * height), coreCell = world.Map.Cell(core);
            var known = new bool[count];
            Array.Fill(known, true);
            var passable = new bool[count];
            Array.Fill(passable, world.Config.Map.DefaultPassable);
            foreach (int blocked in world.Config.Map.BlockedCellIds) passable[blocked] = false;
            var objectives = new System.Collections.Generic.List<int>();
            int enemyCell = world.Map.Cell(world.Cores[world.Factions[2 - faction].CoreId - 1].Definition.Position);
            if (enemyCell >= 0) objectives.Add(enemyCell);
            for (int i = 0; i < world.Outposts.Length; i++)
            {
                int cell = world.Map.Cell(world.Outposts[i].Definition.Position);
                if (cell >= 0 && !objectives.Contains(cell)) objectives.Add(cell);
            }
            objectives.Sort();
            return TollgateTerrainScoring.CountSites(width, height, passable, known, coreCell, objectives, CoreClearanceCells);
        }

        // V3-17 #3: the metropolis looks only at the ground right around its core (24m), not at routes leaving it.
        private const int CivMetropolisReach = 24, MetropolisNormalCells = 270, MetropolisWideCells = 320;

        /// <summary>
        /// V3-17 #3: the metropolis choice score is a small tier from the number of explored, flat, buildable cells
        /// within <see cref="CivMetropolisReach"/> of the core. Cavalry scores the routes reaching out of the core; this
        /// scores the room around it, so the two read different ground (the cavalry ring starts outside 16m, and it
        /// reads visible passability, not building clearance).
        /// </summary>
        private static int MetropolisScore(Simulation s, uint faction, SimPoint core, int ore, int food)
        {
            if (!s.MetropolisOn) return 0;
            int cells = s.CountMetropolisBuildableCells(faction, core);
            return cells >= MetropolisWideCells ? 3 : cells >= MetropolisNormalCells ? 2 : 0;
        }

        /// <summary>
        /// Counts the cells a building could stand on near the core, using the ground part of SiteIsClear: passable,
        /// not river (forest and mountain cells are always blocked), outside the core clearance and outside the
        /// clearance of every resource point. Only cells this faction has explored count. Buildings and belts are not
        /// subtracted: the score is about the ground, so the faction's own early houses do not lower it. Only the
        /// square around the core is visited, and resource points are marked once, so the cost is
        /// O(square + nodes) - no per-cell connectivity search.
        /// </summary>
        private int CountMetropolisBuildableCells(uint faction, SimPoint core)
        {
            var explored = world.Factions[faction - 1].ExploredCells;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int coreCell = world.Map.Cell(core);
            if (coreCell < 0) return 0;
            int coreX = coreCell % width, coreZ = coreCell / width;
            int radius = CivMetropolisReach / world.Config.Map.CellSizeMeters + 1;
            int minX = Math.Max(0, coreX - radius), maxX = Math.Min(width - 1, coreX + radius);
            int minZ = Math.Max(0, coreZ - radius), maxZ = Math.Min(height - 1, coreZ + radius);
            int spanX = maxX - minX + 1, spanZ = maxZ - minZ + 1;

            var nearNode = new bool[checked(spanX * spanZ)];
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                int nodeCell = world.Map.Cell(world.Nodes[i].Definition.Position);
                if (nodeCell < 0) continue;
                int nx = nodeCell % width, nz = nodeCell / width;
                for (int z = Math.Max(minZ, nz - NodeClearanceCells + 1); z <= Math.Min(maxZ, nz + NodeClearanceCells - 1); z++)
                    for (int x = Math.Max(minX, nx - NodeClearanceCells + 1); x <= Math.Min(maxX, nx + NodeClearanceCells - 1); x++)
                        nearNode[(z - minZ) * spanX + (x - minX)] = true;
            }

            Fix64 reach = Fix64.FromInt(CivMetropolisReach);
            int count = 0;
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                {
                    int cell = z * width + x;
                    if (!explored[cell] || nearNode[(z - minZ) * spanX + (x - minX)]) continue;
                    if (Math.Max(Math.Abs(x - coreX), Math.Abs(z - coreZ)) < CoreClearanceCells) continue;
                    if (!world.Map.IsPassable(cell) || IsRiverCell(cell)) continue;
                    if (!InRange(world.Map.Center(cell), core, reach)) continue;
                    count++;
                }
            return count;
        }

        private bool TryGetCivRegistration(uint faction, out CivRegistration registration)
        {
            CivKind civ = world.Economies[faction - 1].Civ;
            for (int i = 0; i < CivRegistrations.Length; i++)
                if (CivRegistrations[i].Civ == civ)
                {
                    registration = CivRegistrations[i];
                    return true;
                }
            registration = default(CivRegistration);
            return false;
        }

        private bool CivUsesFoodMarket(uint faction)
            => TryGetCivRegistration(faction, out CivRegistration row) && row.UsesFoodMarket;

        private bool CivEnabled(uint faction, CivKind civ)
        {
            for (int i = 0; i < CivRegistrations.Length; i++)
                if (CivRegistrations[i].Civ == civ) return CivRegistrations[i].Enabled(this, faction);
            return false;
        }

        private bool CivLineStartedFromRegistry(uint faction)
            => TryGetCivRegistration(faction, out CivRegistration row) && row.LineStarted(this, faction);

        /// <summary>
        /// The automatic economy only sends villagers to stone once the civilisation's line has started. A civilisation
        /// whose foundation itself costs stone (the sanctuary's shrine, the tollgate) would then wait for ever when it
        /// starts without stone. So stone is wanted while the faction is in such a civilisation, has no foundation yet,
        /// cannot pay its stone, and the foundation could otherwise be placed (the registry's FoundationReady). Every
        /// civilisation without a registered foundation, or whose foundation costs no stone, gets false.
        /// </summary>
        private bool FoundationNeedsStone(uint faction)
        {
            if (!AgesOn || !TryGetCivRegistration(faction, out CivRegistration row) || row.Foundation == 0) return false;
            var economy = world.Economies[faction - 1];
            if (economy.Age < 1 || !row.Enabled(this, faction)) return false;
            int stone = StoneOf(row.Foundation, faction);
            if (stone <= 0 || economy.Stone >= stone) return false;
            if (OwnBuildingIndex(faction, row.Foundation) >= 0) return false;
            return row.FoundationReady == null || row.FoundationReady(this, faction);
        }
    }
}
