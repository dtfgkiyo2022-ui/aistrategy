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

            public CivRegistration(CivKind civ, int priority, bool usesFoodMarket,
                Func<Simulation, uint, bool> enabled,
                Func<Simulation, uint, SimPoint, int, int, int> score,
                Func<Simulation, uint, bool> lineStarted)
            {
                Civ = civ;
                Priority = priority;
                UsesFoodMarket = usesFoodMarket;
                Enabled = enabled;
                Score = score;
                LineStarted = lineStarted;
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
                (s, f) => s.TollgateAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.Tollgate) >= 0),
            new CivRegistration(CivKind.Metropolis, 12, true, (s, f) => s.MetropolisOn, MetropolisScore,
                (s, f) => s.MetropolisAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.GrandHouse) >= 0),
            new CivRegistration(CivKind.Sanctuary, 13, true, (s, f) => s.SanctuaryOn, SanctuaryScore,
                (s, f) => s.SanctuaryAllowed(f) && s.OwnBuildingIndex(f, BuildingKind.Shrine) >= 0)
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

        /// <summary>
        /// V3-13 #4: a cult choice is justified only by high-value enemy soldiers that this faction has actually
        /// observed. The record is per soldier slot, so repeated sightings of one individual do not add points and
        /// expired observations do not delay the ordinary age decision.
        /// </summary>
        private static int CultScore(Simulation s, uint faction, SimPoint core, int ore, int food)
        {
            if (!s.CultOn) return 0;
            int observed = s.CountValidCultObservations(faction);
            return observed >= 3 ? 3 : observed > 0 ? 2 : 0;
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

        private static int SanctuaryScore(Simulation s, uint faction, SimPoint core, int ore, int food) => 0;

        /// <summary>
        /// Mountain's choice score is based on the first legal shaft edges the faction could have known at
        /// civilisation choice. A site with several observed mountain neighbours is worth several edge units;
        /// this keeps a compact, high-value cliff from being treated like a single isolated mountain cell while
        /// still returning the same small 0/2/3 score scale used by the registration table.
        /// </summary>
        private static int MountainScore(Simulation s, uint faction, SimPoint core, int ore, int food)
        {
            int edgeUnits = s.CountUsableMountainShaftEdgeUnits(faction, core);
            return edgeUnits >= 3 ? 3 : edgeUnits > 0 ? 2 : 0;
        }

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

        private static int TollgateScore(Simulation s, uint faction, SimPoint core, int ore, int food) => 0;

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
    }
}
