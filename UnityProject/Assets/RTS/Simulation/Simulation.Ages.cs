using System.Numerics;
using Rts.Contracts;
using Rts.Decision;

namespace Rts.Simulation
{
    /// <summary>
    /// V3-4 ages (technical-design-v3 26, 29). Everyone starts in the primitive age; advancing at the core costs food and
    /// wood, takes AdvanceTicks, stops villager training meanwhile, and ends in the chosen civilisation for good. Mines,
    /// smelters and the metal cost of infantry belong to the metallurgy civilisation. Without Ages nothing here applies.
    /// </summary>
    public sealed partial class Simulation
    {
        // Mapgen's guaranteed clear core radius is 16m = 8 cells. The mobility score starts outside that ring while
        // still staying inside the initial observation supplied by the starting soldiers.
        private const int CivOreReach = 44, CivFoodReach = 30, CivForestReach = 44, CivStoneReach = 44, CivCavalryCoreExclusion = 8,
            GuaranteedFoodPoints = 3, AdvanceVillagers = 8, Age2Villagers = 10;

        private bool AgesOn => world.Config.Economy.Enabled && world.Config.Economy.Ages;

        private bool ForestryOn => AgesOn && world.Config.Economy.Forestry;

        private bool MasonryOn => AgesOn && world.Config.Economy.Masonry;

        private bool CaravanOn => AgesOn && world.Config.Economy.Caravan;

        private bool CavalryOn => AgesOn && world.Config.Economy.Cavalry;

        private bool BridgeOn => AgesOn && world.Config.Economy.Bridge;

        private bool AcademyOn => AgesOn && world.Config.Economy.Academy && world.Config.Economy.GoldEnabled;

        private bool CultOn => AgesOn && world.Config.Economy.Cult && world.Config.Economy.MonksEnabled;

        private bool MountainOn => AgesOn && world.Config.Economy.Mountain;

        private bool TollgateOn => AgesOn && world.Config.Economy.Tollgate;

        private bool MetropolisOn => AgesOn && world.Config.Economy.Metropolis;

        private bool SanctuaryOn => AgesOn && world.Config.Economy.Sanctuary;

        private bool FishingOn => AgesOn && world.Config.Economy.FishingCiv && world.Config.Economy.FishingEnabled;

        /// <summary>S-3: towns are a shared second-age expansion, independent of civilisation.</summary>
        private bool TownsOn => world.Config.Economy.Enabled && world.Config.Economy.Towns;

        private bool TownsAllowed(uint faction)
            => TownsOn && AgesOn && world.Economies[faction - 1].Age >= 2;

        private bool CavalryAllowed(uint faction)
            => CavalryOn && world.Economies[faction - 1].Civ == CivKind.Cavalry;

        /// <summary>Mines and smelters: on an ages map only in the metallurgy civilisation.</summary>
        private bool MetalworkAllowed(uint faction) => !AgesOn || world.Economies[faction - 1].Civ == CivKind.Metallurgy;

        private bool ProcessingAvailable(uint faction)
            => ProcessingOn && AgesOn && world.Economies[faction - 1].Civ == CivKind.Metallurgy && world.Economies[faction - 1].Age >= 2;

        /// <summary>The metal an infantry costs this faction now.</summary>
        private int InfantryMetalFor(uint faction) => MetalworkAllowed(faction) ? world.Config.Economy.InfantryMetalCost : 0;

        /// <summary>Farms: the agrarian civilisation only.</summary>
        private bool FarmingAllowed(uint faction) => AgesOn && world.Economies[faction - 1].Civ == CivKind.Agrarian;

        private bool ForestryAllowed(uint faction)
            => ForestryOn && world.Economies[faction - 1].Civ == CivKind.Forestry && world.Economies[faction - 1].Age >= 1;

        private bool MasonryAllowed(uint faction)
            => MasonryOn && world.Economies[faction - 1].Civ == CivKind.Masonry && world.Economies[faction - 1].Age >= 1;

        private bool CaravanAllowed(uint faction)
            => CaravanOn && world.Economies[faction - 1].Civ == CivKind.Caravan && world.Economies[faction - 1].Age >= 1;

        private bool BridgeAllowed(uint faction)
            => BridgeOn && world.Economies[faction - 1].Civ == CivKind.Bridge && world.Economies[faction - 1].Age >= 1;

        private bool AcademyAllowed(uint faction)
            => AcademyOn && world.Economies[faction - 1].Civ == CivKind.Academy && world.Economies[faction - 1].Age >= 1;

        private bool CultAllowed(uint faction)
            => CultOn && world.Economies[faction - 1].Civ == CivKind.Cult && world.Economies[faction - 1].Age >= 1;

        private bool MountainAllowed(uint faction)
            => MountainOn && world.Economies[faction - 1].Civ == CivKind.Mountain && world.Economies[faction - 1].Age >= 1;

        private bool TollgateAllowed(uint faction)
            => TollgateOn && world.Economies[faction - 1].Civ == CivKind.Tollgate && world.Economies[faction - 1].Age >= 1;

        private bool MetropolisAllowed(uint faction)
            => MetropolisOn && world.Economies[faction - 1].Civ == CivKind.Metropolis && world.Economies[faction - 1].Age >= 1;

        private bool SanctuaryAllowed(uint faction)
            => SanctuaryOn && world.Economies[faction - 1].Civ == CivKind.Sanctuary && world.Economies[faction - 1].Age >= 1;

        private bool FishingAllowed(uint faction)
            => FishingOn && world.Economies[faction - 1].Civ == CivKind.Fishing && world.Economies[faction - 1].Age >= 1;

        private bool Agrarian(uint faction) => AgesOn && world.Economies[faction - 1].Civ == CivKind.Agrarian;

        private int InfantryFoodFor(uint faction) => Agrarian(faction) ? world.Config.Economy.AgrarianInfantryFood : world.Config.Economy.InfantryFoodCost;
        private int InfantryWoodFor(uint faction) => Agrarian(faction) ? world.Config.Economy.AgrarianInfantryWood : world.Config.Economy.InfantryWoodCost;
        private int InfantryTicksFor(uint faction) => Agrarian(faction) ? world.Config.Economy.AgrarianInfantryTicks : world.Config.Economy.InfantryTrainTicks;

        private int VillagerFoodCostFor(uint faction)
        {
            var rules = world.Config.Economy;
            return MetropolisAllowed(faction) ? checked(rules.VillagerFoodCost * 2 / 3) : rules.VillagerFoodCost;
        }

        private int VillagerTrainTicksFor(uint faction)
        {
            var rules = world.Config.Economy;
            return MetropolisAllowed(faction) ? checked(rules.VillagerTrainTicks * 2 / 3) : rules.VillagerTrainTicks;
        }

        private int AutoVillagerTargetFor(uint faction)
        {
            var rules = world.Config.Economy;
            return EconomyDecision.VillagerTarget(EconomyScaleOn, world.Economies[faction - 1].Age, rules.AutoVillagerTarget,
                MetropolisAllowed(faction));
        }

        /// <summary>V3-17 #2: a researched metropolis villager answers a nearby raid with a small deterministic blow.</summary>
        private void MetropolisVillagerCounterattack(uint faction, int villagerIndex, int attackerIndex)
        {
            if (!HasTech(faction, MetropolisTech.CitizenMilitia)) return;
            var rules = world.Config.Economy;
            if (!InRange(world.Villagers[villagerIndex].Position, OwnCore(faction).Definition.Position,
                    Fix64.FromInt(rules.MetropolisMilitiaCoreRadius))) return;
            soldierDamage[attackerIndex] = checked(soldierDamage[attackerIndex] + rules.MetropolisMilitiaDamage);
        }

        /// <summary>Metallurgy (27): an infantry trained now is born forged - more HP and damage. Soldiers already out stay as they are.</summary>
        private void ForgeIfMetallurgy(uint faction, int index)
        {
            if (!AgesOn || world.Economies[faction - 1].Civ != CivKind.Metallurgy) return;
            var rules = world.Config.Economy;
            ref var s = ref world.Soldiers[index];
            s.Parameters.Hp = rules.ForgedInfantryHp;
            s.Parameters.Damage = rules.ForgedInfantryDamage;
            s.Hp = rules.ForgedInfantryHp;
            s.Initial.Hp = rules.ForgedInfantryHp;
        }

        /// <summary>
        /// Ticks per food of a farm on <paramref name="origin"/> (27): FarmStepTicks faster for each food point within
        /// FarmFoodReach of its centre and once more if river lies within FarmRiverReach, never under FarmMinTicks.
        /// </summary>
        private int FarmInterval(int origin)
        {
            var rules = world.Config.Economy;
            var centre = FootprintCenter(origin, rules.FarmSizeCells);
            int steps = 0;
            foreach (var node in world.Nodes)
                if (node.Definition.Kind == ResourceKind.Food && InRange(node.Definition.Position, centre, Fix64.FromInt(rules.FarmFoodReach))) steps++;
            // River: only the cells around the farm can be in reach, so only they are looked at.
            var terrain = world.Config.Map.Terrain;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells, cellSize = world.Config.Map.CellSizeMeters;
            int reach = rules.FarmRiverReach / cellSize + rules.FarmSizeCells + 1, ox = origin % width, oz = origin / width;
            bool river = false;
            for (int z = System.Math.Max(0, oz - reach); z <= System.Math.Min(height - 1, oz + reach) && !river; z++)
                for (int x = System.Math.Max(0, ox - reach); x <= System.Math.Min(width - 1, ox + reach) && !river; x++)
                {
                    int cell = z * width + x;
                    if (terrain.Length != 0 && terrain[cell] == (byte)TerrainKind.River && InRange(world.Map.Center(cell), centre, Fix64.FromInt(rules.FarmRiverReach))) river = true;
                }
            if (river) steps++;
            return System.Math.Max(rules.FarmMinTicks, rules.FarmBaseTicks - rules.FarmStepTicks * steps);
        }

        /// <summary>
        /// Advancing into <paramref name="civ"/>: out of the primitive age into either civilisation, or (32 #7) into the
        /// second age of the civilisation already taken. Nothing else is ever possible.
        /// </summary>
        private bool CanAdvance(uint faction, CivKind civ)
        {
            if (!CanAdvanceWithoutCiv(faction)) return false;
            var e = world.Economies[faction - 1];
            if (e.Civ == CivKind.Primitive)
                return CivEnabled(faction, civ);
            // V3-5 (32 #10): and on from the second age into the third one of the same civilisation.
            return (e.Age == 1 || e.Age == 2) && civ == e.Civ;
        }

        /// <summary>Checks every advance prerequisite that does not depend on the civilisation choice.</summary>
        private bool CanAdvanceWithoutCiv(uint faction)
        {
            if (!AgesOn) return false;
            var e = world.Economies[faction - 1];
            if (e.AdvanceRemaining != 0 || e.Queued != 0) return false;
            var (food, wood, gold, _) = AdvancePrice(faction, e);
            return e.Food >= food && e.Wood >= wood && e.Gold >= gold;
        }

        private (int food, int wood, int gold, int ticks) AdvancePrice(uint faction, FactionEconomy e)
        {
            var rules = world.Config.Economy;
            if (e.Civ == CivKind.Primitive) return (rules.AdvanceFoodCost, rules.AdvanceWoodCost, 0, rules.AdvanceTicks);
            return e.Age == 1 ? (rules.Age2FoodCost, rules.Age2WoodCost, 0, rules.Age2Ticks)
                : (rules.Age3FoodCost, rules.Age3WoodCost, AcademyAge3GoldCost(faction, e, rules), rules.Age3Ticks);
        }

        private int AcademyAge3GoldCost(uint faction, FactionEconomy e, EconomyRules rules)
        {
            if (!rules.GoldEnabled) return 0;
            int gold = Age3GoldCost(rules, e.Civ);
            // Losing the visible gold line must stop academy research, not the civilisation's ordinary growth. If
            // some stock remains, it is still spent normally; only an unfundable academy is allowed to continue
            // into the next age without turning a lost mine into a permanent economic dead-end.
            return e.Civ == CivKind.Academy && e.Gold < gold && !HasUsableGoldSource(faction) ? 0 : gold;
        }

        private static int Age3GoldCost(EconomyRules rules, CivKind civ)
            => civ == CivKind.Metallurgy ? rules.Age3GoldCostMetallurgy : rules.Age3GoldCostAgrarian;

        private void StartAdvance(uint faction, CivKind civ)
        {
            ref var e = ref world.Economies[faction - 1];
            var (food, wood, gold, ticks) = AdvancePrice(faction, e);
            e.Food = checked(e.Food - food);
            e.Wood = checked(e.Wood - wood);
            e.Gold = checked(e.Gold - gold);
            e.AdvancingTo = civ;
            e.AdvanceRemaining = ticks;
            e.ReservedCiv = CivKind.Primitive;
        }

        /// <summary>
        /// AI phase, every tick before the automatic economy: a civilisation the player reserved in the primitive age
        /// starts advancing on the first tick it can - also for a core run by hand or with the automatic economy off,
        /// since the player chose it. Without a reservation nothing here changes any state.
        /// </summary>
        private void AdvanceReserved()
        {
            if (!AgesOn) return;
            for (int f = 0; f < 2; f++)
            {
                uint faction = (uint)f + 1;
                var e = world.Economies[f];
                if (e.ReservedCiv == CivKind.Primitive) continue;
                if (e.Civ != CivKind.Primitive) { world.Economies[f].ReservedCiv = CivKind.Primitive; continue; }
                if (world.Cores[world.Factions[f].CoreId - 1].Hp <= 0) continue;
                if (CanAdvance(faction, e.ReservedCiv)) StartAdvance(faction, e.ReservedCiv);
            }
        }

        /// <summary>Economy step: the advancing clock; at zero the civilisation is taken.</summary>
        private void AdvanceAges()
        {
            if (!AgesOn) return;
            for (int f = 0; f < 2; f++)
            {
                ref var e = ref world.Economies[f];
                if (e.AdvanceRemaining == 0) continue;
                if (--e.AdvanceRemaining > 0) continue;
                e.Age = checked((byte)(e.Age + 1));
                e.Civ = e.AdvancingTo;
                e.AdvancingTo = CivKind.Primitive;
            }
        }

        /// <summary>
        /// The automatic economy advances once the primitive base stands - a finished barracks and AdvanceVillagers
        /// villagers - and saves for it meanwhile (SavingToAdvance). The civilisation follows the ground (29).
        /// </summary>
        private void DecideAdvance(uint faction)
        {
            // The core the player runs by hand (V3-3, 19) is theirs to advance too.
            var e = world.Economies[faction - 1];
            if (e.CoreHeld || !SavingToAdvance(faction) || !CanAdvanceWithoutCiv(faction)) return;
            // A civilisation the player reserved comes before the one the ground would choose.
            var civ = e.Civ != CivKind.Primitive ? e.Civ
                : e.ReservedCiv != CivKind.Primitive && CivEnabled(faction, e.ReservedCiv) ? e.ReservedCiv : ChooseCiv(faction);
            if (!CanAdvance(faction, civ)) return;
            StartAdvance(faction, civ);
        }

        /// <summary>True while the automatic economy keeps food and wood for advancing (no infantry is queued then).</summary>
        private bool SavingToAdvance(uint faction)
        {
            if (!AgesOn) return false;
            var e = world.Economies[faction - 1];
            if (e.AdvanceRemaining > 0 || e.Age >= 3) return false;
            // The second and third ages wait for the civilisation's own line, and for the stock to be half way there (32.8).
            if (e.Civ != CivKind.Primitive)
            {
                var (food, wood, gold, _) = AdvancePrice(faction, e);
                if (!CivLineStarted(faction)) return false;
                if (world.Config.Economy.Age2SaveArmyFloor == 0
                    ? 2 * (e.Food + e.Wood + e.Gold) < food + wood + gold
                    : LivingSoldiers(faction) < world.Config.Economy.Age2SaveArmyFloor) return false;
            }
            bool barracks = false;
            for (int i = 0; i < world.BuildingCount; i++)
            {
                var b = world.Buildings[i];
                if (b.Alive && b.Complete && b.FactionId == faction && b.Kind == BuildingKind.Barracks) { barracks = true; break; }
            }
            return barracks && LivingVillagers(faction) >= (e.Civ == CivKind.Primitive ? AdvanceVillagers : Age2Villagers);
        }

        /// <summary>
        /// True while the automatic economy holds back its cheap groundwork too - houses, drop sites, research, markets.
        /// Only the first step out of the primitive age is worth that: saving for the second age lasts long, and stopping
        /// the groundwork for it cost the side its research and its defence (32.9, measured).
        /// </summary>
        private bool SavingHard(uint faction) => SavingToAdvance(faction) && world.Economies[faction - 1].Civ == CivKind.Primitive;

        private CivKind ChooseCiv(uint faction)
        {
            var core = OwnCore(faction).Definition.Position;
            int ore = 0, food = 0;
            // The ground, not what is left of it: points gathered empty in the primitive age still count.
            foreach (var node in world.Nodes)
            {
                if (node.Definition.Kind == ResourceKind.Ore && InRange(node.Definition.Position, core, Fix64.FromInt(CivOreReach))) ore++;
                else if (node.Definition.Kind == ResourceKind.Food && InRange(node.Definition.Position, core, Fix64.FromInt(CivFoodReach))) food++;
            }
            // Keep the old pure two-score decision, including its exact tie rule, when all optional flags are off.
            if (!ForestryOn && !MasonryOn && !CaravanOn && !CavalryOn && !BridgeOn && !AcademyOn && !CultOn && !FishingOn && !MountainOn && !TollgateOn && !MetropolisOn && !SanctuaryOn) return EconomyDecision.ChooseCiv(ore, food, GuaranteedFoodPoints);

            // A civilisation whose flag is off scores zero, which never steals a tie from an older one. Scores are
            // intentionally not normalised: cavalry remains 0..5 and bridge remains 0..3.
            var candidates = new EconomyDecision.CivScore[CivRegistrations.Length];
            for (int i = 0; i < CivRegistrations.Length; i++)
            {
                var row = CivRegistrations[i];
                candidates[i] = new EconomyDecision.CivScore(row.Civ,
                    row.Enabled(this, faction) ? row.Score(this, faction, core, ore, food) : 0, row.Priority);
            }
            return EconomyDecision.ChooseCiv((System.Collections.Generic.IReadOnlyList<EconomyDecision.CivScore>)candidates);
        }

        /// <summary>
        /// V3-10 #3: only terrain and objectives visible from the starting core are used. Resources and outposts are
        /// de-duplicated by cell in sorted order; the scoring itself is the deterministic integer BFS in Decision.
        /// </summary>
        private int CountCavalryMobility(uint faction, SimPoint core)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int count = checked(width * height), coreCell = world.Map.Cell(core);
            var observed = world.Factions[faction - 1].VisibleCells;
            var passable = new bool[count];
            System.Array.Fill(passable, world.Config.Map.DefaultPassable);
            foreach (int blocked in world.Config.Map.BlockedCellIds) passable[blocked] = false;
            var objectives = new System.Collections.Generic.List<int>();
            for (int i = 0; i < world.Outposts.Length; i++)
            {
                int cell = world.Map.Cell(world.Outposts[i].Definition.Position);
                if (cell >= 0 && observed[cell] && !objectives.Contains(cell)) objectives.Add(cell);
            }
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                var node = world.Nodes[i];
                if (node.Remaining <= 0) continue;
                int cell = world.Map.Cell(node.Definition.Position);
                if (cell >= 0 && observed[cell] && !objectives.Contains(cell)) objectives.Add(cell);
            }
            objectives.Sort();
            return CavalryTerrainScoring.Score(width, height, passable, observed, coreCell, objectives, CivCavalryCoreExclusion).Points;
        }

        /// <summary>
        /// Counts only wood points that are useful for forestry: the point is near the core, touches a forest cell,
        /// and at least one of the same lumber-camp placements used by PlaceLumberCamp is currently valid. The
        /// placement check also verifies that closing the camp footprint does not cut the map off from the core's
        /// destinations. This keeps the terrain score about a buildable, connected line rather than guaranteed wood.
        /// </summary>
        private int CountUsableForestWood(uint faction, SimPoint core)
        {
            int count = 0;
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                var node = world.Nodes[i];
                if (node.Definition.Kind != ResourceKind.Wood || node.Remaining <= 0
                    || !InRange(node.Definition.Position, core, Fix64.FromInt(CivForestReach))) continue;
                int cell = world.Map.Cell(node.Definition.Position);
                if (!TouchesForest(cell) || !HasUsableLumberCampSite(faction, cell, node.Definition.Id)) continue;
                count++;
            }
            return count;
        }

        private bool HasUsableLumberCampSite(uint faction, int nodeCell, uint nodeId)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int size = world.Config.Economy.LumberCampSizeCells;
            int nx = nodeCell % width, nz = nodeCell / width;
            var core = OwnCore(faction).Definition.Position;
            for (int dz = 0; dz < size; dz++)
                for (int dx = 0; dx < size; dx++)
                {
                    int x0 = nx - dx, z0 = nz - dz;
                    if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                    int origin = z0 * width + x0;
                    if (!LumberCampSiteIsClear(origin, out uint covered) || covered != nodeId
                        || !KeepsMapConnected(faction, origin, size)) continue;
                    foreach (var side in SidesToward(FootprintCenter(origin, size), core))
                        if (PortIsOpen(OutputCell(origin, size, side), faction)) return true;
                }
            return false;
        }

        private bool TouchesForest(int cell)
        {
            var terrain = world.Config.Map.Terrain;
            if (terrain.Length == 0 || cell < 0 || cell >= terrain.Length) return false;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int x = cell % width, z = cell / width;
            return (x > 0 && terrain[cell - 1] == (byte)TerrainKind.Forest)
                || (x + 1 < width && terrain[cell + 1] == (byte)TerrainKind.Forest)
                || (z > 0 && terrain[cell - width] == (byte)TerrainKind.Forest)
                || (z + 1 < height && terrain[cell + width] == (byte)TerrainKind.Forest);
        }

        /// <summary>
        /// Counts only nearby stone points whose quarry can actually be placed now. This is deliberately the masonry
        /// counterpart of CountUsableForestWood: guaranteed stone that has no legal, connected quarry site is not a reason
        /// to choose masonry. The narrow-passage score is left for a later pass because it needs a separate stable path
        /// metric; keeping this first pass to the existing quarry legality rules avoids changing map generation or routing.
        /// </summary>
        private int CountUsableMasonryStone(uint faction, SimPoint core)
        {
            int count = 0;
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                var node = world.Nodes[i];
                if (node.Definition.Kind != ResourceKind.Stone || node.Remaining <= 0
                    || !InRange(node.Definition.Position, core, Fix64.FromInt(CivStoneReach))) continue;
                int cell = world.Map.Cell(node.Definition.Position);
                if (!HasUsableQuarrySite(faction, cell, node.Definition.Id)) continue;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Counts the starting fish points that can support the fishing civilisation. Only points in cells already
        /// explored by this faction are considered. The placement search deliberately uses HarborSiteIsClear and
        /// KeepsMapConnected, the same legality checks as the automatic harbour finder, so a visible fish behind a
        /// blocked bank or in a disconnected pocket does not inflate the civilisation score.
        /// </summary>
        private int CountUsableFishingFish(uint faction, SimPoint core)
        {
            var explored = world.Factions[faction - 1].ExploredCells;
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int cellCount = checked(width * height);
            var rules = world.Config.Economy;
            int count = 0;
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                var node = world.Nodes[i];
                if (!node.Fishing || node.Remaining <= 0
                    || !InRange(node.Definition.Position, core, Fix64.FromInt(CivFoodReach))) continue;
                int cell = world.Map.Cell(node.Definition.Position);
                if (cell < 0 || cell >= cellCount || !explored[cell]) continue;
                if (HasUsableHarborSiteForFish(faction, node.Definition.Position, width, height, rules.HarborSizeCells,
                    rules.FishReach)) count++;
            }
            return count;
        }

        private bool HasUsableHarborSiteForFish(uint faction, SimPoint fish, int width, int height, int size, int fishReach)
        {
            long reach = Fix64.FromInt(checked(fishReach * 2)).Raw;
            System.Numerics.BigInteger limit = new System.Numerics.BigInteger(reach) * reach;
            for (int origin = 0; origin < width * height; origin++)
            {
                if (origin % width + size > width || origin / width + size > height) continue;
                if (DistanceSquared(FootprintCenter(origin, size), fish) > limit) continue;
                if (HarborSiteIsClear(faction, origin) && KeepsMapConnected(faction, origin, size)) return true;
            }
            return false;
        }

        /// <summary>
        /// V3-11 #4: the engineer terrain score is the best legal one-bridge shortening from this core to a public
        /// outpost or an observed resource. The candidate search temporarily opens the exact river cells and restores
        /// their previous passability in a finally block; the returned value is a 0..3 tier, never a raw distance.
        /// </summary>
        private int CountUsableBridgeSaving(uint faction, SimPoint core)
        {
            if (!BridgeOn) return 0;
            if (!TryFindBridgeCandidate(faction, requireCamp: false, requireCiv: false, avoidDanger: false,
                out _, out _, out _, out int savingCells)) return 0;
            return EngineerBridgeScore(savingCells);
        }

        // Kept as a descriptive counterpart to CountUsableForestWood and CountUsableMasonryStone for headless probes.
        private int CountUsableBridgeScore(uint faction, SimPoint core) => CountUsableBridgeSaving(faction, core);

        private bool HasUsableQuarrySite(uint faction, int nodeCell, uint nodeId)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int size = world.Config.Economy.QuarrySizeCells;
            int nx = nodeCell % width, nz = nodeCell / width;
            var core = OwnCore(faction).Definition.Position;
            for (int dz = 0; dz < size; dz++)
                for (int dx = 0; dx < size; dx++)
                {
                    int x0 = nx - dx, z0 = nz - dz;
                    if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                    int origin = z0 * width + x0;
                    if (!QuarrySiteIsClear(origin, out uint covered) || covered != nodeId
                        || !KeepsMapConnected(faction, origin, size)) continue;
                    foreach (var side in SidesToward(FootprintCenter(origin, size), core))
                        if (PortIsOpen(OutputCell(origin, size, side), faction)) return true;
                }
            return false;
        }

        /// <summary>
        /// Counts maintainable (neutral or already-owned) outposts that have at least one legal caravanserai site. The score is evaluated before the
        /// civilisation is selected, so a finished market cannot exist yet; the market-independent part of the
        /// placement rule is therefore used here (outpost reach, clear footprint and map connectivity). Once the
        /// caravan civilisation is selected, TryCaravanseraiPlacement applies the fixed market and minimum-distance
        /// checks to the same candidate cells. This keeps the choice about maintainable outposts rather than merely
        /// the number of outposts.
        /// </summary>
        private int CountUsableCaravanOutposts(uint faction, SimPoint core)
        {
            int count = 0;
            for (int i = 0; i < world.Outposts.Length; i++)
            {
                var post = world.Outposts[i];
                // At civilisation choice the map's outposts are normally neutral. They are valid future
                // maintenance candidates; an outpost already owned by the other faction is not.
                if (post.OwnerFactionId != 0 && post.OwnerFactionId != faction) continue;
                if (HasUsableCaravanseraiSite(faction, post.Definition.Id, core)) count++;
            }
            return count;
        }

        private bool HasUsableCaravanseraiSite(uint faction, uint outpostId, SimPoint core)
        {
            if (outpostId == 0 || outpostId > world.Outposts.Length) return false;

            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int size = world.Config.Economy.CaravanseraiSizeCells;
            int postCell = world.Map.Cell(world.Outposts[outpostId - 1].Definition.Position);
            int cx = postCell % width, cz = postCell / width;
            int coreCell = world.Map.Cell(core);

            for (int r = 0; r <= SiteSearchRadiusCells; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) != r) continue;
                        int x0 = cx + dx - size / 2, z0 = cz + dz - size / 2;
                        if (x0 < 0 || z0 < 0 || x0 + size > width || z0 + size > height) continue;
                        int origin = z0 * width + x0;
                        if (!SiteIsClear(origin, coreCell, size) || !KeepsMapConnected(faction, origin, size)) continue;

                        var centre = FootprintCenter(origin, size);
                        if (!InRange(centre, world.Outposts[outpostId - 1].Definition.Position,
                            Fix64.FromInt(world.Config.Economy.CaravanOutpostReach))) continue;

                        bool occupied = false;
                        for (int b = 0; b < world.BuildingCount; b++)
                        {
                            var existing = world.Buildings[b];
                            if (existing.Alive && existing.FactionId == faction && existing.Kind == BuildingKind.Caravanserai
                                && existing.CaravanOutpostId == outpostId) { occupied = true; break; }
                        }
                        if (!occupied) return true;
                    }
            return false;
        }
    }
}
