using System.Numerics;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-5 stone and defences (technical-design-v3 32 #5). Walls stand at once, a cell each, near the own base and never
    /// so that the own core loses its way to the enemy core; towers are built like other buildings, shoot the nearest
    /// enemy soldier (else villager) in range and see around them. The automatic economy gathers some stone once it has
    /// a civilisation, and puts up to AutoTowers towers by its core.
    /// </summary>
    public sealed partial class Simulation
    {
        private const int AutoTowers = 2, StoneGatherers = 2;

        /// <summary>
        /// PlaceWall: each cell on its own, in order - on the map, open ground with no belt or resource point, outside every
        /// core, within WallReach of the own core or a finished own building, the map still connected - while stone lasts.
        /// </summary>
        private void PlaceWalls(uint faction, EconomyCommand c)
        {
            if (!AgesOn) return;
            var rules = world.Config.Economy;
            ref var economy = ref world.Economies[faction - 1];
            bool changed = false;
            foreach (int cell in c.Cells)
            {
                if (economy.Stone < rules.WallStoneCost) break;
                if (cell < 0 || cell >= world.Belts.Length || !world.Map.IsPassable(cell) || world.Belts[cell].FactionId != 0
                    || IsNodeCell(cell) || InsideAnyCore(cell) || !NearOwnBase(faction, cell, rules.WallReach) || !KeepsMapConnected(faction, cell, 1)) continue;
                economy.Stone = checked(economy.Stone - rules.WallStoneCost);
                int index = world.BuildingCount;
                if (index == world.Buildings.Length) System.Array.Resize(ref world.Buildings, index == 0 ? 4 : checked(index * 2));
                world.Map.SetPassable(cell, false);
                world.Buildings[index] = new BuildingState { Id = world.NextBuildingId, FactionId = faction, Kind = BuildingKind.Wall, OriginCell = cell,
                    WorkCell = cell, Alive = true, Complete = true, Hp = rules.WallHp, Progress = 1, Held = IndustryOn, Facing = Facing.North };
                world.NextBuildingId = checked(world.NextBuildingId + 1);
                EvacuateFootprint(new[] { cell });
                changed = true;
            }
            if (changed) TerrainChanged();
        }

        private bool NearOwnBase(uint faction, int cell, int reach)
        {
            var at = world.Map.Center(cell);
            if (InRange(at, OwnCore(faction).Definition.Position, Fix64.FromInt(reach))) return true;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.Complete && b.FactionId == faction && b.Kind != BuildingKind.Wall
                    && InRange(at, FootprintCenter(b.OriginCell, SizeOf(b.Kind)), Fix64.FromInt(reach))) return true;
            }
            return false;
        }

        /// <summary>What one tower shot takes off: its damage, heavier with masonry (V3-5, 32 #10).</summary>
        private int TowerShot(uint faction)
        {
            var rules = world.Config.Economy;
            return rules.TowerDamage + (HasTech(faction, TechKind.Masonry) ? rules.MasonryTowerDamage : 0);
        }

        /// <summary>Attack phase, before damage is applied: every finished tower whose clock is up shoots once.</summary>
        private void TowersShoot()
        {
            var rules = world.Config.Economy;
            var range = Fix64.FromInt(rules.TowerRange);
            for (int i = 0; i < world.BuildingCount; i++)
            {
                ref var b = ref world.Buildings[i];
                if (!b.Alive || !b.Complete || b.Kind != BuildingKind.Tower) continue;
                if (b.Timer > 0) { b.Timer--; continue; }
                var centre = FootprintCenter(b.OriginCell, SizeOf(b.Kind));
                int best = -1; BigInteger bestDistance = 0;
                foreach (int s in world.SoldierTraversal)
                {
                    var enemy = world.Soldiers[s];
                    if (!enemy.Alive || enemy.Initial.FactionId == b.FactionId || !IsVisibleTo(b.FactionId, enemy.Position) || !InRange(centre, enemy.Position, range)) continue;
                    var d = DistanceSquared(centre, enemy.Position);
                    if (best < 0 || d < bestDistance) { best = s; bestDistance = d; }
                }
                if (best >= 0)
                {
                    soldierDamage[best] = checked(soldierDamage[best] + TowerShot(b.FactionId));
                    b.Timer = rules.TowerIntervalTicks - 1;
                    b.Shots++;
                    continue;
                }
                for (int v = 0; v < world.VillagerCount; v++)
                {
                    var enemy = world.Villagers[v];
                    if (!enemy.Alive || enemy.FactionId == b.FactionId || !IsVisibleTo(b.FactionId, enemy.Position) || !InRange(centre, enemy.Position, range)) continue;
                    var d = DistanceSquared(centre, enemy.Position);
                    if (best < 0 || d < bestDistance) { best = v; bestDistance = d; }
                }
                if (best < 0) continue;
                villagerDamage[best] = checked(villagerDamage[best] + TowerShot(b.FactionId));
                b.Timer = rules.TowerIntervalTicks - 1;
                b.Shots++;
            }
        }

        private void RevealTowers(ref FactionState faction)
        {
            var vision = Fix64.FromInt(world.Config.Economy.TowerVision);
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.Complete && b.FactionId == faction.Id && b.Kind == BuildingKind.Tower)
                    Reveal(faction.VisibleCells, FootprintCenter(b.OriginCell, SizeOf(b.Kind)), vision);
            }
        }

        /// <summary>Once in a civilisation, the automatic economy keeps StoneGatherers villagers on stone (for its towers).</summary>
        private bool StoneWanted(uint faction)
        {
            if (!AgesOn || !CivLineStarted(faction)) return false;
            int onStone = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                var v = world.Villagers[i];
                if (v.Alive && v.FactionId == faction && v.NodeId != 0 && world.Nodes[v.NodeId - 1].Definition.Kind == ResourceKind.Stone
                    && (v.Task == VillagerTask.ToNode || v.Task == VillagerTask.Gathering || v.Task == VillagerTask.ToDropOff)) onStone++;
            }
            return onStone < StoneGatherers;
        }

        /// <summary>
        /// The civilisation's own line is under way: metallurgy has a mine and a smelter, farming a farm. The automatic
        /// economy puts stone, towers and research after it, so wood and villagers go to the line first.
        /// </summary>
        private bool CivLineStarted(uint faction)
        {
            var civ = world.Economies[faction - 1].Civ;
            if (civ == CivKind.Metallurgy) return OwnBuildingIndex(faction, BuildingKind.Mine) >= 0 && OwnBuildingIndex(faction, BuildingKind.Smelter) >= 0;
            return civ == CivKind.Agrarian && OwnBuildingIndex(faction, BuildingKind.Farm) >= 0;
        }

        /// <summary>AI phase: up to AutoTowers towers by the core, one at a time, once in a civilisation and not saving.</summary>
        private void DecideTower(uint faction)
        {
            if (!AgesOn || !CivLineStarted(faction)) return;
            var rules = world.Config.Economy;
            var economy = world.Economies[faction - 1];
            if (economy.Wood < rules.TowerWoodCost || economy.Stone < rules.TowerStoneCost) return;
            int towers = 0;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (!b.Alive || b.FactionId != faction || b.Kind != BuildingKind.Tower) continue;
                if (!b.Complete) return; // one at a time
                towers++;
            }
            if (towers >= AutoTowers) return;
            int origin = FindSite(faction, rules.TowerSizeCells);
            if (origin >= 0) PlaceBuildingAt(faction, BuildingKind.Tower, origin, Facing.North, 0);
        }
    }
}
