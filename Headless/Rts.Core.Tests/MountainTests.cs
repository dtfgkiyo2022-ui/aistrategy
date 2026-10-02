using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>V3-15 #1: the mountain civilisation and renewable mine shafts.</summary>
    public sealed class MountainTests
    {
        private const int Width = 128;
        private static readonly MethodInfo Site = typeof(Battle).GetMethod("MountainShaftSiteIsClear", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo MountainScore = typeof(Battle).GetMethod("MountainScore", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo MountainEdgeUnits = typeof(Battle).GetMethod("CountUsableMountainShaftEdgeUnits", BindingFlags.Instance | BindingFlags.NonPublic);

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static ScenarioDefinition MountainScenario(ulong seed)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            s.Economy.Mountain = true;
            s.Economy.StartFood = 50000;
            s.Economy.StartWood = 50000;
            s.Economy.StartStone = 50000;
            s.Economy.AdvanceFoodCost = 0;
            s.Economy.AdvanceWoodCost = 0;
            s.Economy.AdvanceTicks = 1;
            s.Economy.GatherIntervalTicks = 1000000;
            s.Economy.MountainWork = 1;
            s.Economy.MountainBaseIntervalTicks = 4;
            s.Economy.MountainIntervalStepTicks = 0;
            s.Economy.MountainMinIntervalTicks = 4;
            s.Cores[0].Hp = 1000000;
            s.Cores[1].Hp = 1000000;
            return s;
        }

        private static ScenarioDefinition MountainMatchScenario(ulong seed)
        {
            var s = MountainScenario(seed);
            s.Economy.Age2FoodCost = 0;
            s.Economy.Age2WoodCost = 0;
            s.Economy.Age2Ticks = 1;
            s.Economy.Age3FoodCost = 0;
            s.Economy.Age3WoodCost = 0;
            s.Economy.Age3Ticks = 1;
            return s;
        }

        private static (Battle sim, CommandGateway gateway, ScenarioDefinition scenario, ulong sequence) EnterMountain(ulong seed)
            => EnterMountain(MountainScenario(seed));

        private static (Battle sim, CommandGateway gateway, ScenarioDefinition scenario, ulong sequence) EnterMountain(ScenarioDefinition scenario)
        {
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Mountain));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 2);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Mountain));
            return (sim, gateway, scenario, sequence);
        }

        private static uint PlaceBuilding(Battle sim, CommandGateway gateway, ScenarioDefinition scenario, BuildingKind kind, ref ulong sequence)
        {
            int before = sim.Capture(1).Economy.Buildings.Count;
            int width = scenario.Map.WidthCells, height = scenario.Map.HeightCells;
            int core = (int)(scenario.Cores[0].Position.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * width
                + (int)(scenario.Cores[0].Position.X.Raw / 65536 / scenario.Map.CellSizeMeters);
            int coreX = core % width, coreZ = core / width;
            int size = kind == BuildingKind.Wall ? 1 : kind == BuildingKind.Tower ? scenario.Economy.TowerSizeCells
                : kind == BuildingKind.Market ? scenario.Economy.MarketSizeCells : 3;
            for (int radius = 5; radius <= 30; radius++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius) continue;
                        int x = coreX + dx, z = coreZ + dz;
                        if (x < 0 || z < 0 || x + size > width || z + size > height) continue;
                        int origin = z * width + x;
                        gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, kind, origin, Facing.North));
                        Steps(gateway, sim, 1);
                        var buildings = sim.Capture(1).Economy.Buildings;
                        if (buildings.Count > before && buildings.Last().Kind == kind) return buildings.Last().Id;
                    }
            }
            return 0;
        }

        private static void AdvanceMountainAge(ref (Battle sim, CommandGateway gateway, ScenarioDefinition scenario, ulong sequence) state)
        {
            state.gateway.SubmitEconomy(EconomyCommand.Advance(1, ++state.sequence, CivKind.Mountain));
            Steps(state.gateway, state.sim, state.scenario.Economy.Age2Ticks + 2);
        }

        private static void Build(ref (Battle sim, CommandGateway gateway, ScenarioDefinition scenario, ulong sequence) state, uint building)
        {
            state.gateway.SubmitEconomy(EconomyCommand.Assign(1, ++state.sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, building));
            Steps(state.gateway, state.sim, 5000);
            Assert.That(state.sim.Capture(1).Economy.Buildings.Single(b => b.Id == building).Complete, Is.True);
        }

        private static bool IsPassable(ScenarioDefinition s, int cell)
            => !s.Map.BlockedCellIds.Contains(cell);

        private static int FindSite(Battle sim, ScenarioDefinition s, uint faction)
        {
            int coreCell = (int)(s.Cores[faction - 1].Position.Z.Raw / 65536 / s.Map.CellSizeMeters) * s.Map.WidthCells
                + (int)(s.Cores[faction - 1].Position.X.Raw / 65536 / s.Map.CellSizeMeters);
            int best = -1;
            long bestDistance = long.MaxValue;
            for (int origin = 0; origin < s.Map.WidthCells * s.Map.HeightCells; origin++)
            {
                int x = origin % s.Map.WidthCells, z = origin / s.Map.WidthCells;
                if (x + s.Economy.MountainSizeCells > s.Map.WidthCells || z + s.Economy.MountainSizeCells > s.Map.HeightCells) continue;
                if (!(bool)Site.Invoke(sim, new object[] { faction, origin })) continue;
                long dx = x - coreCell % s.Map.WidthCells, dz = z - coreCell / s.Map.WidthCells;
                long distance = dx * dx + dz * dz;
                if (distance < bestDistance) { best = origin; bestDistance = distance; }
            }
            return best;
        }

        private static int FindPlainSite(ScenarioDefinition s)
        {
            int size = s.Economy.MountainSizeCells;
            for (int origin = 0; origin < s.Map.WidthCells * s.Map.HeightCells; origin++)
            {
                int x = origin % s.Map.WidthCells, z = origin / s.Map.WidthCells;
                if (x + size > s.Map.WidthCells || z + size > s.Map.HeightCells) continue;
                bool clear = true;
                for (int dz = 0; dz < size; dz++)
                    for (int dx = 0; dx < size; dx++)
                        if (!IsPassable(s, origin + dz * Width + dx)) clear = false;
                if (!clear || TouchesMountain(s, origin, size)) continue;
                if (s.ResourceNodes.Any(n => n.Position.X.Raw / 65536 / s.Map.CellSizeMeters >= x
                    && n.Position.X.Raw / 65536 / s.Map.CellSizeMeters < x + size
                    && n.Position.Z.Raw / 65536 / s.Map.CellSizeMeters >= z
                    && n.Position.Z.Raw / 65536 / s.Map.CellSizeMeters < z + size)) continue;
                return origin;
            }
            return -1;
        }

        private static bool TouchesMountain(ScenarioDefinition s, int origin, int size)
        {
            int width = s.Map.WidthCells, height = s.Map.HeightCells;
            for (int z = origin / width; z < origin / width + size; z++)
                for (int x = origin % width; x < origin % width + size; x++)
                {
                    if (x > 0 && s.Map.Terrain[(z * width) + x - 1] == (byte)TerrainKind.Mountain) return true;
                    if (x + 1 < width && s.Map.Terrain[(z * width) + x + 1] == (byte)TerrainKind.Mountain) return true;
                    if (z > 0 && s.Map.Terrain[((z - 1) * width) + x] == (byte)TerrainKind.Mountain) return true;
                    if (z + 1 < height && s.Map.Terrain[((z + 1) * width) + x] == (byte)TerrainKind.Mountain) return true;
                }
            return false;
        }

        [Test]
        public void MountainOffKeepsOldBytesAndStateHashes()
        {
            var old = MapGenerator.GenerateTerrain(1);
            var off = MapGenerator.GenerateTerrain(1);
            off.Economy.Mountain = false;
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(old)));
            var left = new Battle(old);
            var right = new Battle(off);
            for (long tick = 1; tick <= 300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void MountainFactionCanBuildOnlyAtMountainEdgeAndHasLimit()
        {
            var state = EnterMountain(2);
            int plain = FindPlainSite(state.scenario);
            Assert.That(plain, Is.GreaterThanOrEqualTo(0));
            state.gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.sequence, BuildingKind.MineShaft, plain));
            Steps(state.gateway, state.sim, 1);
            Assert.That(state.sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.MineShaft), Is.False);

            var sites = new List<int>();
            for (int i = 0; i < state.scenario.Map.WidthCells * state.scenario.Map.HeightCells && sites.Count < 4; i++)
            {
                int site = FindSite(state.sim, state.scenario, 1);
                if (site < 0 || sites.Contains(site)) break;
                sites.Add(site);
                state.gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.sequence, BuildingKind.MineShaft, site));
                Steps(state.gateway, state.sim, 1);
            }
            Assert.That(state.sim.Capture(1).Economy.Buildings.Count(b => b.Kind == BuildingKind.MineShaft), Is.EqualTo(3));
        }

        [Test]
        public void CompletedShaftAlternatesRenewableStoneAndOreWithoutDepletingNodes()
        {
            var state = EnterMountain(3);
            int site = FindSite(state.sim, state.scenario, 1);
            Assert.That(site, Is.GreaterThanOrEqualTo(0));
            state.gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.sequence, BuildingKind.MineShaft, site));
            Steps(state.gateway, state.sim, 5000);
            var completed = state.sim.Capture(1).Economy;
            Assert.That(completed.Buildings.Single(b => b.Kind == BuildingKind.MineShaft).Complete, Is.True);
            int stoneBefore = completed.Stone;
            int oreBefore = completed.Ore;
            var mineralNodesBefore = completed.Resources
                .Where(r => r.Kind == ResourceKind.Stone || r.Kind == ResourceKind.Ore)
                .ToDictionary(r => r.Id, r => r.Remaining);
            Steps(state.gateway, state.sim, 8);
            var economy = state.sim.Capture(1).Economy;
            Assert.That(economy.Stone, Is.GreaterThan(stoneBefore));
            Assert.That(economy.Ore, Is.GreaterThan(oreBefore));
            foreach (var node in economy.Resources.Where(r => mineralNodesBefore.ContainsKey(r.Id)))
                Assert.That(node.Remaining, Is.EqualTo(mineralNodesBefore[node.Id]));
        }

        [Test]
        public void MountainAutoBuildsShaftsAfterTheCivilisationIsChosen()
        {
            var scenario = MountainScenario(4);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Mountain));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 2500);
            var economy = sim.Capture(1).Economy;
            Assert.That(economy.Civ, Is.EqualTo(CivKind.Mountain));
            Assert.That(economy.Buildings.Any(b => b.Kind == BuildingKind.MineShaft && b.Complete), Is.True);
            Assert.That(economy.Stone, Is.GreaterThan(0));
            Assert.That(economy.Ore, Is.GreaterThan(0));
        }

        [Test]
        public void MountainExtensionRoundTripsWithAcademyAndExistingCivilisationFlags()
        {
            var scenario = MountainScenario(5);
            scenario.Economy.Academy = true;
            scenario.Economy.GoldEnabled = true;
            scenario.Economy.Forestry = true;
            scenario.Economy.Masonry = true;
            scenario.Economy.Caravan = true;
            scenario.Economy.Cavalry = true;
            scenario.Economy.Bridge = true;
            byte[] bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.Mountain, Is.True);
            Assert.That(decoded.Extensions.Any(e => e.Id == 5), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void MountainScenarioReplaysForTwentyThousandTicks()
        {
            var scenario = MountainScenario(6);
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 20000, identity);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, identity);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        [Test]
        public void MountainChoiceScoreCountsOnlyObservedBuildableEdgesInSmallTiers()
        {
            Assert.That(MountainScore, Is.Not.Null);
            Assert.That(MountainEdgeUnits, Is.Not.Null);
            bool sawZero = false, sawOneToTwo = false, sawThreeOrMore = false, sawWide = false, sawJustBelowWide = false;
            for (ulong seed = 1; seed <= 100; seed++)
            {
                var scenario = MountainScenario(seed);
                var sim = new Battle(scenario);
                sim.Step(1, Array.Empty<ScheduledInput>());
                for (uint faction = 1; faction <= 2; faction++)
                {
                    int units = (int)MountainEdgeUnits.Invoke(sim, new object[] { faction, scenario.Cores[faction - 1].Position });
                    int score = (int)MountainScore.Invoke(null, new object[] { sim, faction, scenario.Cores[faction - 1].Position, 0, 0 });
                    if (units == 0) sawZero = true;
                    if (units > 0 && units < 3) sawOneToTwo = true;
                    if (units >= 3) sawThreeOrMore = true;
                    Assert.That(score, Is.EqualTo(ExpectedMountainScore(units)));

                    // The same ground seen in full (the footprints and their mountain cells all explored) reaches the
                    // longer edges, where the 4-point tier starts at 24 edge units.
                    var explored = ExploredOf(sim, faction);
                    var saved = (bool[])explored.Clone();
                    for (int i = 0; i < explored.Length; i++) explored[i] = true;
                    int fullUnits = (int)MountainEdgeUnits.Invoke(sim, new object[] { faction, scenario.Cores[faction - 1].Position });
                    int fullScore = (int)MountainScore.Invoke(null, new object[] { sim, faction, scenario.Cores[faction - 1].Position, 0, 0 });
                    Array.Copy(saved, explored, saved.Length);
                    Assert.That(fullScore, Is.EqualTo(ExpectedMountainScore(fullUnits)), "seed " + seed + " faction " + faction);
                    if (fullUnits >= 24) sawWide = true;
                    if (fullUnits >= 3 && fullUnits < 24) sawJustBelowWide = true;
                }
            }
            Assert.That(sawZero, Is.True, "山の縁0か所");
            Assert.That(sawOneToTwo, Is.True, "山の縁1〜2か所相当");
            Assert.That(sawThreeOrMore, Is.True, "山の縁3か所以上相当");
            Assert.That(sawWide, Is.True, "山の縁24以上（4点）");
            Assert.That(sawJustBelowWide, Is.True, "山の縁3〜23（3点）");
        }

        private static int ExpectedMountainScore(int units) => units >= 24 ? 4 : units >= 3 ? 3 : units > 0 ? 2 : 0;

        private static bool[] ExploredOf(Battle sim, uint faction)
        {
            var world = typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sim);
            var factions = (Array)world.GetType().GetField("Factions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world);
            var state = factions.GetValue((int)faction - 1);
            return (bool[])state.GetType().GetField("ExploredCells", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state);
        }

        [Test]
        public void MountainWideEdgeTierIsFourAndOnlyTheMountainScoreChanges()
        {
            // Scores 0..3 keep their meaning; only 24 edge units or more becomes 4. With the flag off the mountain is
            // never chosen, and the old two-score choice is used.
            Assert.That(ExpectedMountainScore(23), Is.EqualTo(3));
            Assert.That(ExpectedMountainScore(24), Is.EqualTo(4));
            var choose = typeof(Battle).GetMethod("ChooseCiv", BindingFlags.Instance | BindingFlags.NonPublic);
            int wide = 0;
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var on = MapGenerator.GenerateTerrain(seed);
                on.Economy.Mountain = true;
                var off = MapGenerator.GenerateTerrain(seed);
                var simOn = new Battle(on);
                var simOff = new Battle(off);
                for (uint faction = 1; faction <= 2; faction++)
                {
                    var explored = ExploredOf(simOn, faction);
                    for (int i = 0; i < explored.Length; i++) explored[i] = true;
                    int score = (int)MountainScore.Invoke(null, new object[] { simOn, faction, on.Cores[faction - 1].Position, 0, 0 });
                    var offExplored = ExploredOf(simOff, faction);
                    for (int i = 0; i < offExplored.Length; i++) offExplored[i] = true;
                    Assert.That((int)MountainScore.Invoke(null, new object[] { simOff, faction, off.Cores[faction - 1].Position, 0, 0 }),
                        Is.EqualTo(0), "旗オフは0点");
                    var withMountain = (CivKind)choose.Invoke(simOn, new object[] { faction });
                    var without = (CivKind)choose.Invoke(simOff, new object[] { faction });
                    Assert.That(without, Is.Not.EqualTo(CivKind.Mountain));
                    if (score == 4) wide++;
                    if (withMountain != CivKind.Mountain)
                        Assert.That(withMountain, Is.EqualTo(without), "山岳が勝たない陣営の選択は変わらない seed " + seed);
                }
            }
            TestContext.WriteLine("4-point mountain sides over seeds 1..30 (fully explored): " + wide);
            Assert.That(wide, Is.GreaterThan(0));
        }

        [Test]
        public void MountainChoiceScoreIgnoresUnseenAndUnbuildableMountainEdges()
        {
            ScenarioDefinition observedScenario = null;
            int observedFaction = 0;
            for (ulong seed = 1; seed <= 100; seed++)
            {
                var scenario = MountainScenario(seed);
                var sim = new Battle(scenario);
                sim.Step(1, Array.Empty<ScheduledInput>());
                for (uint faction = 1; faction <= 2; faction++)
                    if ((int)MountainEdgeUnits.Invoke(sim, new object[] { faction, scenario.Cores[faction - 1].Position }) > 0)
                    {
                        observedScenario = scenario;
                        observedFaction = (int)faction;
                        break;
                    }
                if (observedScenario != null) break;
            }
            Assert.That(observedScenario, Is.Not.Null, "観測済みで建てられる山の縁がある種");

            var observed = new Battle(observedScenario);
            observed.Step(1, Array.Empty<ScheduledInput>());
            var world = typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(observed);
            var factions = (Array)world.GetType().GetField("Factions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world);
            var factionState = factions.GetValue(observedFaction - 1);
            var explored = (bool[])factionState.GetType().GetField("ExploredCells", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(factionState);
            for (int i = 0; i < observedScenario.Map.Terrain.Length; i++)
                if (observedScenario.Map.Terrain[i] == (byte)TerrainKind.Mountain) explored[i] = false;
            Assert.That((int)MountainScore.Invoke(null, new object[] { observed, (uint)observedFaction, observedScenario.Cores[observedFaction - 1].Position, 0, 0 }), Is.EqualTo(0), "見たことのない山だけ");

            var blockedScenario = MountainScenario(101);
            var blocked = new HashSet<int>(blockedScenario.Map.BlockedCellIds);
            int width = blockedScenario.Map.WidthCells, height = blockedScenario.Map.HeightCells;
            for (int cell = 0; cell < blockedScenario.Map.Terrain.Length; cell++)
                if (blockedScenario.Map.Terrain[cell] == (byte)TerrainKind.Mountain)
                {
                    int x = cell % width, z = cell / width;
                    if (x > 0) blocked.Add(cell - 1);
                    if (x + 1 < width) blocked.Add(cell + 1);
                    if (z > 0) blocked.Add(cell - width);
                    if (z + 1 < height) blocked.Add(cell + width);
                }
            blockedScenario.Map.BlockedCellIds = new List<int>(blocked).ToArray();
            var unbuildable = new Battle(blockedScenario);
            unbuildable.Step(1, Array.Empty<ScheduledInput>());
            Assert.That((int)MountainScore.Invoke(null, new object[] { unbuildable, 1U, blockedScenario.Cores[0].Position, 0, 0 }), Is.EqualTo(0), "建てられない山の縁だけ");
        }

        [TestCase(CivKind.Mountain, CivKind.Agrarian)]
        [TestCase(CivKind.Agrarian, CivKind.Mountain)]
        [TestCase(CivKind.Mountain, CivKind.Metallurgy)]
        [TestCase(CivKind.Metallurgy, CivKind.Mountain)]
        public void MountainCombinationsReachTheSecondAgeWithoutFault(CivKind west, CivKind east)
        {
            var scenario = MountainMatchScenario(21);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, west));
            gateway.SubmitEconomy(EconomyCommand.Advance(2, 2, east));
            for (int i = 0; i < 20000 && !sim.Capture(1).Result.HasEnded; i++)
            {
                gateway.Step();
                Assert.That(sim.Capture(1).Result.IsFault, Is.False, "fault at tick " + sim.Capture(1).Tick);
            }
            var result = sim.Capture(1).Result;
            TestContext.WriteLine(west + " vs " + east + ": tick=" + sim.Capture(1).Tick + ", winner=" + result.WinnerFactionId
                + ", ended=" + result.HasEnded + ", ages=" + sim.Capture(1).Economy.Age + "/" + sim.Capture(2).Economy.Age);
            Assert.That(sim.Capture(1).Economy.Age, Is.GreaterThanOrEqualTo(2), west + " reaches the second age");
            Assert.That(sim.Capture(2).Economy.Age, Is.GreaterThanOrEqualTo(2), east + " reaches the second age");
        }

        [Test]
        public void NormalMountainStartFindsASeedAndReplaysForTwentyThousandTicks()
        {
            ulong foundSeed = 0;
            CivKind foundWest = CivKind.Primitive, foundEast = CivKind.Primitive;
            // The normal start chooses its civilisation only once the barracks and villagers stand (about tick 1000), so
            // each seed is followed until both sides have chosen (as the sanctuary and metropolis searches do).
            for (ulong seed = 1; seed <= 1000 && foundSeed == 0; seed++)
            {
                var probe = new Battle(MountainMatchScenario(seed));
                var probeGateway = new CommandGateway(probe);
                for (int i = 0; i < 1500; i++)
                {
                    probeGateway.Step();
                    if (i % 20 != 19) continue;
                    var west = probe.Capture(1).Economy.Civ;
                    var east = probe.Capture(2).Economy.Civ;
                    if (west == CivKind.Mountain || east == CivKind.Mountain)
                    {
                        foundSeed = seed;
                        foundWest = west;
                        foundEast = east;
                        break;
                    }
                    if (west != CivKind.Primitive && east != CivKind.Primitive) break;
                }
            }
            if (foundSeed == 0)
            {
                TestContext.WriteLine("山岳が選ばれる種は seed 1..1000 では見つからなかった");
                Assert.Inconclusive("山岳が選ばれる種が見つからない");
                return;
            }

            var scenario = MountainMatchScenario(foundSeed);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            Steps(gateway, sim, 20000);
            TestContext.WriteLine("normal mountain seed " + foundSeed + ": civs=" + foundWest + "/" + foundEast
                + ", tick=" + sim.Capture(1).Tick + ", ages=" + sim.Capture(1).Economy.Age + "/" + sim.Capture(2).Economy.Age);
            Assert.That(sim.Capture(1).Economy.Civ == CivKind.Mountain || sim.Capture(2).Economy.Civ == CivKind.Mountain, Is.True);
            Assert.That(sim.Capture(1).Economy.Age, Is.GreaterThanOrEqualTo(2), "通常開始から西が第2時代まで進む");
            Assert.That(sim.Capture(2).Economy.Age, Is.GreaterThanOrEqualTo(2), "通常開始から東が第2時代まで進む");
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, gateway.Inputs, sim.Capture(1).Tick, identity);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, identity);
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
        }

        [Test]
        public void MountainOreCanBeTradedForFoodAndWoodAtTheExistingMarketRate()
        {
            var state = EnterMountain(7);
            int shaftSite = FindSite(state.sim, state.scenario, 1);
            state.gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.sequence, BuildingKind.MineShaft, shaftSite));
            Steps(state.gateway, state.sim, 5000);
            uint market = PlaceBuilding(state.sim, state.gateway, state.scenario, BuildingKind.Market, ref state.sequence);
            Assert.That(market, Is.Not.EqualTo(0u));
            Build(ref state, market);
            var before = state.sim.Capture(1).Economy;
            state.gateway.SubmitEconomy(EconomyCommand.Trade(1, ++state.sequence, ResourceKind.Ore, ResourceKind.Food));
            Steps(state.gateway, state.sim, 1);
            var food = state.sim.Capture(1).Economy;
            Assert.That(food.Ore, Is.EqualTo(before.Ore - state.scenario.Economy.TradeLot));
            Assert.That(food.Food, Is.EqualTo(before.Food + state.scenario.Economy.TradeReturn));

            before = food;
            state.gateway.SubmitEconomy(EconomyCommand.Trade(1, ++state.sequence, ResourceKind.Ore, ResourceKind.Wood));
            Steps(state.gateway, state.sim, 1);
            var wood = state.sim.Capture(1).Economy;
            Assert.That(wood.Ore, Is.EqualTo(before.Ore - state.scenario.Economy.TradeLot));
            Assert.That(wood.Wood, Is.EqualTo(before.Wood + state.scenario.Economy.TradeReturn));
        }

        [Test]
        public void MountainBlacksmithPaysMetalResearchWithOreAndOtherCivilisationsStillPayMetal()
        {
            var mountain = MountainScenario(8);
            mountain.Economy.TechMetal[0] = 5;
            var state = EnterMountain(mountain);
            int shaftSite = FindSite(state.sim, state.scenario, 1);
            state.gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.sequence, BuildingKind.MineShaft, shaftSite));
            Steps(state.gateway, state.sim, 5000);
            uint smith = PlaceBuilding(state.sim, state.gateway, state.scenario, BuildingKind.Blacksmith, ref state.sequence);
            Assert.That(smith, Is.Not.EqualTo(0u));
            Build(ref state, smith);
            var before = state.sim.Capture(1).Economy;
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, smith, TechKind.Weapons));
            Steps(state.gateway, state.sim, 1);
            var after = state.sim.Capture(1).Economy;
            Assert.That(after.Metal, Is.EqualTo(before.Metal));
            Assert.That(after.Ore, Is.EqualTo(before.Ore - mountain.Economy.TechMetal[0]));

            var ordinary = MountainScenario(9);
            ordinary.Economy.Mountain = false;
            ordinary.Economy.TechMetal[0] = 5;
            ordinary.Economy.StartMetal = 5;
            var other = new Battle(ordinary);
            var otherGateway = new CommandGateway(other);
            ulong otherSequence = 0;
            otherGateway.SubmitEconomy(EconomyCommand.Auto(1, ++otherSequence, false));
            otherGateway.SubmitEconomy(EconomyCommand.Advance(1, ++otherSequence, CivKind.Metallurgy));
            Steps(otherGateway, other, ordinary.Economy.AdvanceTicks + 2);
            uint otherSmith = PlaceBuilding(other, otherGateway, ordinary, BuildingKind.Blacksmith, ref otherSequence);
            Assert.That(otherSmith, Is.Not.EqualTo(0u));
            otherGateway.SubmitEconomy(EconomyCommand.Assign(1, ++otherSequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, otherSmith));
            Steps(otherGateway, other, 1000);
            var otherBefore = other.Capture(1).Economy;
            otherGateway.SubmitEconomy(EconomyCommand.Research(1, ++otherSequence, otherSmith, TechKind.Weapons));
            Steps(otherGateway, other, 1);
            var otherAfter = other.Capture(1).Economy;
            Assert.That(otherAfter.Metal, Is.EqualTo(otherBefore.Metal - ordinary.Economy.TechMetal[0]));
            Assert.That(otherAfter.Ore, Is.EqualTo(otherBefore.Ore));
        }

        [Test]
        public void DeepShaftShortensMiningAndRaisesTheShaftLimit()
        {
            var state = EnterMountain(10);
            var sites = new List<int>();
            for (int i = 0; i < 3; i++)
            {
                int site = FindSite(state.sim, state.scenario, 1);
                Assert.That(site, Is.GreaterThanOrEqualTo(0));
                sites.Add(site);
                state.gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.sequence, BuildingKind.MineShaft, site));
                Steps(state.gateway, state.sim, 1000);
            }
            Assert.That(state.sim.Capture(1).Economy.Buildings.Count(b => b.Kind == BuildingKind.MineShaft), Is.EqualTo(3));
            AdvanceMountainAge(ref state);
            Assert.That(state.sim.Capture(1).Economy.Age, Is.EqualTo(2));
            uint shaft = state.sim.Capture(1).Economy.Buildings.Last(b => b.Kind == BuildingKind.MineShaft).Id;
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, shaft, MountainTech.DeepShaft));
            Steps(state.gateway, state.sim, 1);
            Assert.That(state.sim.Capture(1).Economy.Buildings.Last(b => b.Kind == BuildingKind.MineShaft).Researching, Is.EqualTo(MountainTech.DeepShaft));
            Steps(state.gateway, state.sim, state.scenario.Economy.MountainDeepShaftTicks + 2);
            var after = state.sim.Capture(1).Economy;
            Assert.That(after.Techs & (1UL << 17), Is.Not.EqualTo(0UL));

            int fourth = FindSite(state.sim, state.scenario, 1);
            Assert.That(fourth, Is.GreaterThanOrEqualTo(0));
            state.gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.sequence, BuildingKind.MineShaft, fourth));
            Steps(state.gateway, state.sim, 2);
            Assert.That(state.sim.Capture(1).Economy.Buildings.Count(b => b.Kind == BuildingKind.MineShaft), Is.EqualTo(4));
        }

        [Test]
        public void MountainFortOnlyStrengthensDefencesTouchingAMountain()
        {
            var state = EnterMountain(11);
            int shaft = FindSite(state.sim, state.scenario, 1);
            state.gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.sequence, BuildingKind.MineShaft, shaft));
            Steps(state.gateway, state.sim, 5000);
            AdvanceMountainAge(ref state);

            uint mountainTower = PlaceMountainAdjacentBuilding(ref state, BuildingKind.Tower);
            uint plainTower = PlaceBuilding(state.sim, state.gateway, state.scenario, BuildingKind.Tower, ref state.sequence);
            Assert.That(mountainTower, Is.Not.EqualTo(0u));
            Assert.That(plainTower, Is.Not.EqualTo(0u));
            Steps(state.gateway, state.sim, 1);
            var before = state.sim.Capture(1).Economy.Buildings.ToDictionary(b => b.Id, b => b.MaxHp);

            state.gateway.SubmitEconomy(EconomyCommand.Advance(1, ++state.sequence, CivKind.Mountain));
            Steps(state.gateway, state.sim, state.scenario.Economy.Age3Ticks + 2);
            var age3 = state.sim.Capture(1).Economy;
            Assert.That(age3.Age, Is.EqualTo(3));
            var age3ShaftView = age3.Buildings.Last(b => b.Kind == BuildingKind.MineShaft);
            Assert.That(age3ShaftView.Complete, Is.True, "progress=" + age3ShaftView.Progress + " work=" + age3ShaftView.Work);
            Assert.That(age3.Food, Is.GreaterThanOrEqualTo(state.scenario.Economy.MountainFortFoodCost));
            Assert.That(age3.Wood, Is.GreaterThanOrEqualTo(state.scenario.Economy.MountainFortWoodCost));
            uint age3Shaft = age3ShaftView.Id;
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, age3Shaft, MountainTech.MountainFort));
            Steps(state.gateway, state.sim, 1);
            Assert.That(state.sim.Capture(1).Economy.Buildings.Last(b => b.Kind == BuildingKind.MineShaft).Researching, Is.EqualTo(MountainTech.MountainFort));
            Steps(state.gateway, state.sim, state.scenario.Economy.MountainFortTicks + 2);
            Assert.That(state.sim.Capture(1).Economy.Techs & (1UL << 18), Is.Not.EqualTo(0UL));
            var after = state.sim.Capture(1).Economy.Buildings.ToDictionary(b => b.Id, b => b.MaxHp);
            Assert.That(after[mountainTower], Is.EqualTo(before[mountainTower] * 3 / 2));
            Assert.That(after[plainTower], Is.EqualTo(before[plainTower]));
        }

        private static uint PlaceMountainAdjacentBuilding(ref (Battle sim, CommandGateway gateway, ScenarioDefinition scenario, ulong sequence) state, BuildingKind kind)
        {
            int width = state.scenario.Map.WidthCells, height = state.scenario.Map.HeightCells;
            int before = state.sim.Capture(1).Economy.Buildings.Count;
            for (int origin = 0; origin < width * height; origin++)
            {
                int x = origin % width, z = origin / width, size = kind == BuildingKind.Wall ? 1 : state.scenario.Economy.TowerSizeCells;
                if (x + size > width || z + size > height || !TouchesMountain(state.scenario, origin, size)) continue;
                state.gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.sequence, kind, origin, Facing.North));
                Steps(state.gateway, state.sim, 1);
                var buildings = state.sim.Capture(1).Economy.Buildings;
                if (buildings.Count > before && buildings.Last().Kind == kind) return buildings.Last().Id;
            }
            return 0;
        }

        [Test]
        public void MountainExtensionVersionOneIsReadableAndVersionTwoRoundTrips()
        {
            var scenario = MountainScenario(12);
            byte[] current = ScenarioBinary.Encode(scenario);
            int marker = BitConverter.GetBytes(0x4E545845).Length == 4 ? FindInt32(current, 0x4E545845) : -1;
            Assert.That(marker, Is.GreaterThanOrEqualTo(0));
            int sectionLength = BitConverter.ToInt32(current, marker + 8);
            int record = marker + 12;
            Assert.That(BitConverter.ToInt32(current, record), Is.EqualTo(5));
            Assert.That(BitConverter.ToInt32(current, record + 4), Is.EqualTo(2));
            byte[] old = new byte[current.Length - 40];
            Buffer.BlockCopy(current, 0, old, 0, record + 12 + 48);
            Buffer.BlockCopy(current, record + 12 + 88, old, record + 12 + 48, current.Length - (record + 12 + 88));
            Array.Copy(BitConverter.GetBytes(sectionLength - 40), 0, old, marker + 8, 4);
            Array.Copy(BitConverter.GetBytes(1), 0, old, record + 4, 4);
            Array.Copy(BitConverter.GetBytes(48), 0, old, record + 8, 4);
            var decoded = ScenarioBinary.Decode(old);
            Assert.That(decoded.Economy.Mountain, Is.True);
            byte[] upgraded = ScenarioBinary.Encode(decoded);
            Assert.That(BitConverter.ToInt32(upgraded, FindInt32(upgraded, 0x4E545845) + 20), Is.EqualTo(88));
            Assert.That(ScenarioBinary.Encode(ScenarioBinary.Decode(upgraded)), Is.EqualTo(upgraded));
        }

        private static int FindInt32(byte[] bytes, int value)
        {
            byte[] pattern = BitConverter.GetBytes(value);
            for (int i = 0; i <= bytes.Length - pattern.Length; i++)
                if (bytes[i] == pattern[0] && bytes[i + 1] == pattern[1] && bytes[i + 2] == pattern[2] && bytes[i + 3] == pattern[3]) return i;
            return -1;
        }
    }
}
