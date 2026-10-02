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
    /// <summary>V3-16 #1: the tollgate civilisation and faction-specific passage.</summary>
    public sealed class TollgateTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo Site = typeof(Battle).GetMethod("TollgateSiteIsClear", Hidden);
        private static readonly MethodInfo Connected = typeof(Battle).GetMethod("KeepsMapConnected", Hidden, null,
            new[] { typeof(uint), typeof(int[]) }, null);
        private static readonly MethodInfo Decide = typeof(Battle).GetMethod("DecideTollgate", Hidden);

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static ScenarioDefinition Scenario(ulong seed)
        {
            MapGenerator.CoreLean[] ignored;
            var s = MapGenerator.GenerateTerrain(seed, out ignored);
            s.Economy.Tollgate = true;
            s.Economy.StartFood = 100000;
            s.Economy.StartWood = 100000;
            s.Economy.StartStone = 100000;
            s.Economy.AdvanceFoodCost = 0;
            s.Economy.AdvanceWoodCost = 0;
            s.Economy.AdvanceTicks = 1;
            s.Economy.GatherIntervalTicks = 1000000;
            s.Economy.TollgateWork = 1;
            s.Economy.TollgateMaxBuildings = 3;
            s.Cores[0].Hp = 1000000;
            s.Cores[1].Hp = 1000000;
            return s;
        }

        private static (Battle sim, CommandGateway gateway, ScenarioDefinition scenario, ulong sequence) Enter(ulong seed, bool ages = true)
        {
            var scenario = Scenario(seed);
            scenario.Economy.Ages = ages;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Tollgate));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 3);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Tollgate));
            return (sim, gateway, scenario, sequence);
        }

        private static GridMap Map(Battle sim)
        {
            var world = typeof(Battle).GetField("world", Hidden).GetValue(sim);
            return (GridMap)world.GetType().GetField("Map", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(world);
        }

        private static int[] FindSite(Battle sim, ScenarioDefinition scenario, uint faction)
        {
            int width = scenario.Map.WidthCells, height = scenario.Map.HeightCells;
            for (int cell = 0; cell < width * height; cell++)
            {
                int x = cell % width, z = cell / width;
                if (x + 1 >= width) continue;
                var cells = new[] { cell, cell + 1 };
                if ((bool)Site.Invoke(sim, new object[] { faction, cells })
                    && (bool)Connected.Invoke(sim, new object[] { faction, cells })) return cells;
            }
            return null;
        }

        [Test]
        public void TollgateOffKeepsOldBytesAndStateHashes()
        {
            MapGenerator.CoreLean[] oldLean, offLean;
            var old = MapGenerator.GenerateTerrain(1, out oldLean);
            var off = MapGenerator.GenerateTerrain(1, out offLean);
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
        public void AMapWithoutTollgatesHasTheSameFactionPassabilityAndRoutes()
        {
            var scenario = Scenario(2);
            scenario.Economy.Tollgate = false;
            var simulation = new Battle(scenario);
            var map = Map(simulation);
            int start = map.Cell(scenario.Cores[0].Position), goal = map.Cell(scenario.Cores[1].Position);
            Assert.That(map.IsPassableFor(start, 1), Is.EqualTo(map.IsPassable(start)));
            Assert.That(map.IsPassableFor(goal, 2), Is.EqualTo(map.IsPassable(goal)));
            Assert.That(map.FindPath(start, scenario.Cores[1].Position, 1),
                Is.EqualTo(map.FindPath(start, scenario.Cores[1].Position)));
            Assert.That(map.SharedRoute(start, scenario.Cores[1].Position, 2),
                Is.EqualTo(map.SharedRoute(start, scenario.Cores[1].Position)));
        }

        [Test]
        public void CompletedTollgatePassesOwnFactionAndBlocksEnemy()
        {
            var state = Enter(3);
            int[] cells = FindSite(state.sim, state.scenario, 1);
            Assert.That(cells, Is.Not.Null);
            state.gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.sequence, BuildingKind.Tollgate, cells[0], Facing.East));
            var map = Map(state.sim);
            Steps(state.gateway, state.sim, 1);
            Assert.That(cells.All(cell => !map.IsPassableFor(cell, 1) && !map.IsPassableFor(cell, 2)), Is.True);
            Steps(state.gateway, state.sim, 5000);
            var completed = state.sim.Capture(1).Economy.Buildings.Single(b => b.Kind == BuildingKind.Tollgate);
            Assert.That(completed.Complete, Is.True, "progress=" + completed.Progress + "/" + completed.Work);
            Assert.That(cells.All(cell => map.IsPassableFor(cell, 1)), Is.True);
            Assert.That(cells.All(cell => !map.IsPassableFor(cell, 2)), Is.True);

            int width = state.scenario.Map.WidthCells;
            int[] neighbours = { cells[0] - 1, cells[0] + 1, cells[0] - width, cells[0] + width };
            int from = -1;
            foreach (int cell in neighbours)
                if (cell >= 0 && cell < width * state.scenario.Map.HeightCells && !cells.Contains(cell) && map.IsPassable(cell))
                { from = cell; break; }
            Assert.That(from, Is.GreaterThanOrEqualTo(0), "a passable cell adjacent to the gate is required");
            var gateCenter = map.Center(cells[0]);
            Assert.That(map.ClipMove(map.Center(from), gateCenter, 1), Is.EqualTo(gateCenter));
            Assert.That(map.ClipMove(map.Center(from), gateCenter, 2), Is.Not.EqualTo(gateCenter));
        }

        [Test]
        public void ConstructionClosesBothSidesAndRemovalOpensTheFootprint()
        {
            var state = Enter(4);
            int[] cells = FindSite(state.sim, state.scenario, 1);
            Assert.That(cells, Is.Not.Null);
            state.gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.sequence, BuildingKind.Tollgate, cells[0], Facing.East));
            var map = Map(state.sim);
            Steps(state.gateway, state.sim, 1);
            Assert.That(cells.All(cell => !map.IsPassableFor(cell, 1) && !map.IsPassableFor(cell, 2)), Is.True);
            Steps(state.gateway, state.sim, 5000);
            uint id = state.sim.Capture(1).Economy.Buildings.Single(b => b.Kind == BuildingKind.Tollgate).Id;
            var completed = state.sim.Capture(1).Economy.Buildings.Single(b => b.Kind == BuildingKind.Tollgate);
            Assert.That(cells.All(cell => map.IsPassableFor(cell, 1) && !map.IsPassableFor(cell, 2)), Is.True,
                "complete=" + completed.Complete + ", progress=" + completed.Progress + "/" + completed.Work);
            state.gateway.SubmitEconomy(EconomyCommand.RemoveBuilding(1, ++state.sequence, id));
            Steps(state.gateway, state.sim, 1);
            Assert.That(cells.All(cell => map.IsPassableFor(cell, 1) && map.IsPassableFor(cell, 2)), Is.True);
        }

        [Test]
        public void AutomaticChoiceBuildsACompletedTollgateOnTheCoreRoute()
        {
            BuildingView building = default(BuildingView);
            CommandGateway gateway = null;
            Battle sim = null;
            ulong sequence = 0;
            for (ulong seed = 1; seed <= 20 && building.Id == 0; seed++)
            {
                var state = Enter(seed);
                gateway = state.gateway; sim = state.sim; sequence = state.sequence;
                gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, true));
                Decide.Invoke(sim, new object[] { 1u });
                Steps(gateway, sim, 1);
                building = sim.Capture(1).Economy.Buildings.SingleOrDefault(b => b.Kind == BuildingKind.Tollgate);
            }
            Assert.That(building.Id, Is.Not.EqualTo(0u), "automatic candidate was not placed for seeds 1..20");
            Assert.That(building.Complete, Is.False);
            Steps(gateway, sim, 5000);
            Assert.That(sim.Capture(1).Economy.Buildings.Single(b => b.Id == building.Id).Complete, Is.True);
        }


        [Test]
        public void TollgateExtensionRoundTripsAndTwentyThousandTickReplayIsDeterministic()
        {
            var scenario = Scenario(6);
            byte[] bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.Tollgate, Is.True);
            Assert.That(decoded.Extensions.Any(e => e.Id == 6), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
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
        public void TollgateFeeArrivesOnlyForNearbyEnemySoldiers()
        {
            var state = Enter(7);
            int[] cells = FindSite(state.sim, state.scenario, 1);
            Assert.That(cells, Is.Not.Null);
            state.gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.sequence, BuildingKind.Tollgate, cells[0], Facing.East));
            Steps(state.gateway, state.sim, 5000);
            var gate = state.sim.Capture(1).Economy.Buildings.Single(b => b.Kind == BuildingKind.Tollgate);
            var world = typeof(Battle).GetField("world", Hidden).GetValue(state.sim);
            var soldiers = (Array)world.GetType().GetField("Soldiers", Hidden).GetValue(world);
            int enemyIndex = -1;
            for (int i = 0; i < soldiers.Length; i++)
            {
                var soldier = soldiers.GetValue(i);
                var initial = soldier.GetType().GetField("Initial", Hidden).GetValue(soldier);
                if ((uint)initial.GetType().GetField("FactionId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(initial) == 2) { enemyIndex = i; break; }
            }
            Assert.That(enemyIndex, Is.GreaterThanOrEqualTo(0));
            SetSoldierPosition(world, soldiers, enemyIndex, gate.Center);
            var economy = (Array)world.GetType().GetField("Economies", Hidden).GetValue(world);
            var before = economy.GetValue(0);
            int woodBefore = (int)before.GetType().GetField("Wood", Hidden).GetValue(before);
            int foodBefore = (int)before.GetType().GetField("Food", Hidden).GetValue(before);
            world.GetType().GetField("Tick", Hidden).SetValue(world, 100L);
            typeof(Battle).GetMethod("AdvanceTollgateFees", Hidden).Invoke(state.sim, null);
            var after = economy.GetValue(0);
            Assert.That((int)after.GetType().GetField("Wood", Hidden).GetValue(after), Is.EqualTo(woodBefore + 1));
            Assert.That((int)after.GetType().GetField("Food", Hidden).GetValue(after), Is.EqualTo(foodBefore + 1));

            SetSoldierPosition(world, soldiers, enemyIndex, state.scenario.Cores[1].Position);
            world.GetType().GetField("Tick", Hidden).SetValue(world, 200L);
            typeof(Battle).GetMethod("AdvanceTollgateFees", Hidden).Invoke(state.sim, null);
            var away = economy.GetValue(0);
            Assert.That((int)away.GetType().GetField("Wood", Hidden).GetValue(away), Is.EqualTo(woodBefore + 1));
            Assert.That((int)away.GetType().GetField("Food", Hidden).GetValue(away), Is.EqualTo(foodBefore + 1));
        }

        [Test]
        public void TollgateResearchChangesOnlyTheTollgateFaction()
        {
            var state = Enter(8, true);
            int[] cells = FindSite(state.sim, state.scenario, 1);
            Assert.That(cells, Is.Not.Null);
            state.gateway.SubmitEconomy(EconomyCommand.Place(1, ++state.sequence, BuildingKind.Tollgate, cells[0], Facing.East));
            Steps(state.gateway, state.sim, 5000);
            var world = typeof(Battle).GetField("world", Hidden).GetValue(state.sim);
            SetTech(world, 1, TollgateTech.GateDefence);
            SetTech(world, 1, TollgateTech.GateNetwork);
            Assert.That((bool)typeof(Battle).GetMethod("HasTech", Hidden).Invoke(state.sim,
                new object[] { 1u, TollgateTech.GateDefence }), Is.True);
            state.gateway.Step();
            var gate = state.sim.Capture(1).Economy.Buildings.Single(b => b.Kind == BuildingKind.Tollgate);
            Assert.That(gate.MaxHp, Is.EqualTo(state.scenario.Economy.TollgateHp * 3 / 2));
            Assert.That((int)typeof(Battle).GetMethod("TollgateDefenceDamage", Hidden).Invoke(state.sim,
                new object[] { 1u, gate.Center, 10 }), Is.EqualTo(8));
            Assert.That((int)typeof(Battle).GetMethod("TollgateDefenceDamage", Hidden).Invoke(state.sim,
                new object[] { 2u, gate.Center, 10 }), Is.EqualTo(10));
            Assert.That((int)typeof(Battle).GetMethod("TollgateMaxBuildingsFor", Hidden).Invoke(state.sim, new object[] { 1u }), Is.EqualTo(5));
        }

        [Test]
        public void TollgateExtensionVersionOneIsReadableAndUpgradesOnWrite()
        {
            var source = Scenario(9);
            byte[] versionTwo = ScenarioBinary.Encode(source);
            byte[] marker = BitConverter.GetBytes(0x4E545845);
            int markerOffset = -1;
            for (int i = 0; i <= versionTwo.Length - marker.Length; i++)
                if (versionTwo.Skip(i).Take(marker.Length).SequenceEqual(marker)) { markerOffset = i; break; }
            Assert.That(markerOffset, Is.GreaterThanOrEqualTo(0));
            int recordOffset = markerOffset + 12;
            Assert.That(BitConverter.ToInt32(versionTwo, recordOffset), Is.EqualTo(6));
            byte[] versionOne = new byte[recordOffset + 12 + 7 * sizeof(int)];
            Array.Copy(versionTwo, versionOne, versionOne.Length);
            Array.Copy(BitConverter.GetBytes(12 + 7 * sizeof(int)), 0, versionOne, markerOffset + 8, sizeof(int));
            Array.Copy(BitConverter.GetBytes(1), 0, versionOne, recordOffset + 4, sizeof(int));
            Array.Copy(BitConverter.GetBytes(7 * sizeof(int)), 0, versionOne, recordOffset + 8, sizeof(int));
            var decoded = ScenarioBinary.Decode(versionOne);
            Assert.That(decoded.Economy.Tollgate, Is.True);
            Assert.That(decoded.Extensions.Single(e => e.Id == 6).Version, Is.EqualTo(1));
            byte[] upgraded = ScenarioBinary.Encode(decoded);
            var reread = ScenarioBinary.Decode(upgraded);
            Assert.That(reread.Extensions.Single(e => e.Id == 6).Version, Is.EqualTo(2));
            Assert.That(ScenarioBinary.Encode(reread), Is.EqualTo(upgraded));
        }

        // ---- V3-16 #3: the choice score, the cavalry mirror, and the matches ----

        private static readonly MethodInfo ScoreMethod = typeof(Battle).GetMethod("TollgateScore", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo SiteCount = typeof(Battle).GetMethod("CountTollgateChokeSites", Hidden);
        private static readonly MethodInfo ChooseMethod = typeof(Battle).GetMethod("ChooseCiv", Hidden);

        /// <summary>'#' is a wall, anything else is open. All rows must have the same length.</summary>
        private static (int width, int height, bool[] passable, bool[] known) Grid(params string[] rows)
        {
            int width = rows[0].Length, height = rows.Length;
            var passable = new bool[width * height];
            var known = new bool[width * height];
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                {
                    passable[z * width + x] = rows[z][x] != '#';
                    known[z * width + x] = true;
                }
            return (width, height, passable, known);
        }

        private static string Row(int length, char fill) => new string(fill, length);

        // One 1-wide corridor (row 7, x 15..19) is the short way; a wide gap (rows 11..14) is a longer way round.
        private static string[] OneChokeRows()
        {
            var rows = new string[15];
            for (int z = 0; z < 15; z++)
            {
                string left = Row(15, '.'), right = Row(20, '.');
                string middle = z == 7 || z >= 11 ? Row(5, '.') : Row(5, '#');
                rows[z] = left + middle + right;
            }
            return rows;
        }

        // Two such corridors in a row (x 15..19 and x 30..34), each with its own wide way round.
        private static string[] TwoChokeRows()
        {
            var rows = new string[15];
            for (int z = 0; z < 15; z++)
            {
                string gap = z == 7 || z >= 11 ? Row(5, '.') : Row(5, '#');
                rows[z] = Row(15, '.') + gap + Row(10, '.') + gap + Row(10, '.');
            }
            return rows;
        }

        private static string[] OpenRows(int width) => Enumerable.Range(0, 15).Select(_ => Row(width, '.')).ToArray();

        private static string[] OnlyCorridorRows()
        {
            var rows = new string[15];
            for (int z = 0; z < 15; z++) rows[z] = Row(15, '.') + (z == 7 ? Row(5, '.') : Row(5, '#')) + Row(20, '.');
            return rows;
        }

        private static int Sites(string[] rows, int core, int[] objectives, bool[] known = null)
        {
            var g = Grid(rows);
            return Rts.Decision.TollgateTerrainScoring.CountSites(g.width, g.height, g.passable, known ?? g.known, core, objectives, 4);
        }

        private static int Cell(int width, int x, int z) => z * width + x;

        [Test]
        public void ChokeSitesAreCountedAsZeroOneAndTwo()
        {
            Assert.That(Sites(OpenRows(40), Cell(40, 2, 7), new[] { Cell(40, 37, 7) }), Is.EqualTo(0), "開けた地形");
            Assert.That(Sites(OneChokeRows(), Cell(40, 2, 7), new[] { Cell(40, 37, 7) }), Is.EqualTo(1));
            Assert.That(Sites(TwoChokeRows(), Cell(45, 2, 7), new[] { Cell(45, 43, 7) }), Is.EqualTo(2));
            Assert.That(Rts.Decision.TollgateTerrainScoring.Points(0), Is.EqualTo(0));
            Assert.That(Rts.Decision.TollgateTerrainScoring.Points(1), Is.EqualTo(2));
            Assert.That(Rts.Decision.TollgateTerrainScoring.Points(2), Is.EqualTo(3));
            Assert.That(Rts.Decision.TollgateTerrainScoring.Points(5), Is.EqualTo(3));
        }

        [Test]
        public void UnseenCellsAndOwnRouteCuttingPlacesAreNotCounted()
        {
            // The enemy side was never seen: the objective is unknown, so nothing counts.
            var g = Grid(OneChokeRows());
            var known = (bool[])g.known.Clone();
            for (int z = 0; z < g.height; z++) for (int x = 20; x < g.width; x++) known[Cell(g.width, x, z)] = false;
            Assert.That(Sites(OneChokeRows(), Cell(40, 2, 7), new[] { Cell(40, 37, 7) }, known), Is.EqualTo(0), "見たことのない場所だけ");

            // The wide way round was never seen either: closing the corridor would look like it cuts the owner off.
            known = (bool[])g.known.Clone();
            for (int z = 11; z < g.height; z++) for (int x = 15; x < 20; x++) known[Cell(g.width, x, z)] = false;
            Assert.That(Sites(OneChokeRows(), Cell(40, 2, 7), new[] { Cell(40, 37, 7) }, known), Is.EqualTo(0));

            // The only corridor: closing it would cut the owner's own route (a gate there could never be built).
            Assert.That(Sites(OnlyCorridorRows(), Cell(40, 2, 7), new[] { Cell(40, 37, 7) }), Is.EqualTo(0), "自陣の経路も塞ぐ場所だけ");
        }

        [Test]
        public void ChokeScoringDoesNotChangeItsInputs()
        {
            var g = Grid(OneChokeRows());
            var passable = (bool[])g.passable.Clone();
            var known = (bool[])g.known.Clone();
            Rts.Decision.TollgateTerrainScoring.CountSites(g.width, g.height, g.passable, g.known, Cell(40, 2, 7), new[] { Cell(40, 37, 7) }, 4);
            Assert.That(g.passable, Is.EqualTo(passable));
            Assert.That(g.known, Is.EqualTo(known));
        }

        [Test]
        public void CavalryAndTollgateMoveInOppositeDirectionsOnTheSameTerrain()
        {
            int[] objectives = { Cell(40, 30, 3), Cell(40, 30, 7), Cell(40, 30, 12) };
            var open = Grid(OpenRows(40));
            var choke = Grid(OneChokeRows());
            // Objectives behind the corridor sit at x=30: keep them in the right-hand room.
            int cavalryOpen = Rts.Decision.CavalryTerrainScoring.Score(open.width, open.height, open.passable, open.known, Cell(40, 2, 7), objectives, 8).Points;
            int cavalryChoke = Rts.Decision.CavalryTerrainScoring.Score(choke.width, choke.height, choke.passable, choke.known, Cell(40, 2, 7), objectives, 8).Points;
            int gateOpen = Rts.Decision.TollgateTerrainScoring.Score(open.width, open.height, open.passable, open.known, Cell(40, 2, 7), objectives, 4);
            int gateChoke = Rts.Decision.TollgateTerrainScoring.Score(choke.width, choke.height, choke.passable, choke.known, Cell(40, 2, 7), objectives, 4);
            TestContext.WriteLine("cavalry open/choke=" + cavalryOpen + "/" + cavalryChoke + ", tollgate open/choke=" + gateOpen + "/" + gateChoke);
            Assert.That(cavalryOpen, Is.GreaterThan(cavalryChoke), "騎馬は隘路に頼らない地形を好む");
            Assert.That(gateChoke, Is.GreaterThan(gateOpen), "関所は敵が隘路に頼る地形を好む");
        }

        [Test]
        public void TollgateScoreIsZeroWithTheFlagOffAndChoiceIsUnchangedWhenTheScoreIsZero()
        {
            int sawZero = 0;
            for (ulong seed = 1; seed <= 25; seed++)
            {
                var on = Scenario(seed);
                var off = Scenario(seed);
                off.Economy.Tollgate = false;
                var simOn = new Battle(on);
                var simOff = new Battle(off);
                simOn.Step(1, Array.Empty<ScheduledInput>());
                simOff.Step(1, Array.Empty<ScheduledInput>());
                for (uint faction = 1; faction <= 2; faction++)
                {
                    var core = on.Cores[faction - 1].Position;
                    Assert.That((int)ScoreMethod.Invoke(null, new object[] { simOff, faction, core, 0, 0 }), Is.EqualTo(0), "旗オフは0点");
                    int score = (int)ScoreMethod.Invoke(null, new object[] { simOn, faction, core, 0, 0 });
                    if (score != 0) continue;
                    sawZero++;
                    Assert.That((CivKind)ChooseMethod.Invoke(simOn, new object[] { faction }),
                        Is.EqualTo((CivKind)ChooseMethod.Invoke(simOff, new object[] { faction })), "seed " + seed + " faction " + faction);
                }
            }
            Assert.That(sawZero, Is.GreaterThan(0));
        }

        /// <summary>
        /// The choice score reads the public terrain (the whole map, the enemy core and every outpost), so it already
        /// exists at tick 1 and does not depend on what the faction has explored. Rewritten from the earlier
        /// "unexplored cells score zero" version when the score moved to public information.
        /// </summary>
        [Test]
        public void ChoiceScoreOnRealMapsIsSmallTieredAndUsesThePublicTerrain()
        {
            var tally = new Dictionary<int, int>();
            ulong foundSeed = 0;
            uint foundFaction = 0;
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var scenario = Scenario(seed);
                var sim = new Battle(scenario);
                sim.Step(1, Array.Empty<ScheduledInput>());
                for (uint faction = 1; faction <= 2; faction++)
                {
                    var core = scenario.Cores[faction - 1].Position;
                    int sites = (int)SiteCount.Invoke(sim, new object[] { faction, core });
                    int score = (int)ScoreMethod.Invoke(null, new object[] { sim, faction, core, 0, 0 });
                    Assert.That(score, Is.EqualTo(sites >= 2 ? 3 : sites == 1 ? 2 : 0));
                    Assert.That(sites, Is.EqualTo(PublicSites(scenario, faction)), "seed " + seed + " faction " + faction);
                    tally[score] = tally.TryGetValue(score, out int n) ? n + 1 : 1;
                    if (score > 0 && foundSeed == 0) { foundSeed = seed; foundFaction = faction; }
                }
            }
            TestContext.WriteLine("tollgate scores over seeds 1..60: " + string.Join(", ", tally.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value)));
            Assert.That(foundSeed, Is.Not.EqualTo(0UL), "公開の地形で点がつく実際の地図がある");
            var again = new Battle(Scenario(foundSeed));
            again.Step(1, Array.Empty<ScheduledInput>());
            var world = typeof(Battle).GetField("world", Hidden).GetValue(again);
            var factions = (Array)world.GetType().GetField("Factions", Hidden).GetValue(world);
            var state = factions.GetValue((int)foundFaction - 1);
            var explored = (bool[])state.GetType().GetField("ExploredCells", Hidden).GetValue(state);
            int before = (int)ScoreMethod.Invoke(null, new object[] { again, foundFaction, Scenario(foundSeed).Cores[foundFaction - 1].Position, 0, 0 });
            Array.Clear(explored, 0, explored.Length);
            Assert.That((int)ScoreMethod.Invoke(null, new object[] { again, foundFaction, Scenario(foundSeed).Cores[foundFaction - 1].Position, 0, 0 }),
                Is.EqualTo(before).And.GreaterThan(0), "見たことのある範囲に関係なく、公開の地形で数える");
        }

        /// <summary>Test-side count over the scenario terrain with every cell known: enemy core and outposts.</summary>
        private static int PublicSites(ScenarioDefinition s, uint faction)
        {
            int width = s.Map.WidthCells, height = s.Map.HeightCells;
            var passable = new bool[width * height];
            var known = new bool[width * height];
            for (int i = 0; i < passable.Length; i++) { passable[i] = s.Map.DefaultPassable; known[i] = true; }
            foreach (int blocked in s.Map.BlockedCellIds) passable[blocked] = false;
            int CellOf(SimPoint p) => (int)(p.Z.Raw / 65536 / s.Map.CellSizeMeters) * width + (int)(p.X.Raw / 65536 / s.Map.CellSizeMeters);
            var objectives = new List<int> { CellOf(s.Cores[2 - faction].Position) };
            foreach (var post in s.Outposts) if (!objectives.Contains(CellOf(post.Position))) objectives.Add(CellOf(post.Position));
            objectives.Sort();
            return Rts.Decision.TollgateTerrainScoring.CountSites(width, height, passable, known, CellOf(s.Cores[faction - 1].Position), objectives, 4);
        }

        private static bool FoundationNeedsStone(Battle sim, uint faction)
            => (bool)typeof(Battle).GetMethod("FoundationNeedsStone", Hidden).Invoke(sim, new object[] { faction });

        private static bool StoneWanted(Battle sim, uint faction)
            => (bool)typeof(Battle).GetMethod("StoneWanted", Hidden).Invoke(sim, new object[] { faction });

        private static void SetStone(Battle sim, uint faction, int stone)
        {
            var world = typeof(Battle).GetField("world", Hidden).GetValue(sim);
            var economies = (Array)world.GetType().GetField("Economies", Hidden).GetValue(world);
            var boxed = economies.GetValue((int)faction - 1);
            boxed.GetType().GetField("Stone", Hidden).SetValue(boxed, stone);
            economies.SetValue(boxed, (int)faction - 1);
        }

        /// <summary>A normal-economy tollgate start (ordinary gathering) without any starting stone.</summary>
        private static ScenarioDefinition NoStoneScenario(ulong seed)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            s.Economy.Tollgate = true;
            s.Economy.StartFood = 5000;
            s.Economy.StartWood = 5000;
            s.Economy.StartStone = 0;
            s.Economy.AdvanceFoodCost = 0;
            s.Economy.AdvanceWoodCost = 0;
            s.Economy.AdvanceTicks = 1;
            s.Economy.AutoVillagerTarget = 8;
            s.Cores[0].Hp = 1000000;
            s.Cores[1].Hp = 1000000;
            return s;
        }

        [Test]
        public void TollgateWithoutStartingStoneGathersStoneForItsFirstGateAndBuildsIt()
        {
            // The rule itself: the tollgate faction wants stone only while it has no gate and cannot pay one.
            var scenario = NoStoneScenario(1);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Tollgate));
            gateway.SubmitEconomy(EconomyCommand.Advance(2, 2, CivKind.Agrarian));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 3);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Tollgate));
            Assert.That(sim.Capture(2).Economy.Civ, Is.EqualTo(CivKind.Agrarian));
            SetStone(sim, 1, 0);
            SetStone(sim, 2, 0);
            Assert.That(FoundationNeedsStone(sim, 1), Is.True, "関所がなく、石が足りない");
            Assert.That(StoneWanted(sim, 1), Is.True, "関所のための石を集める");
            SetStone(sim, 1, scenario.Economy.TollgateStoneCost);
            Assert.That(FoundationNeedsStone(sim, 1), Is.False, "関所の分の石があれば集めない");
            SetStone(sim, 1, 0);
            Assert.That(FoundationNeedsStone(sim, 2), Is.False, "農耕は基盤に石が要らない");
            if (sim.Capture(2).Economy.Buildings.All(b => b.Kind != BuildingKind.Farm))
                Assert.That(StoneWanted(sim, 2), Is.False, "農耕は畑の前に石を集めに行かない");

            // The automatic tollgate, starting with no stone at all, gathers it and builds its gate.
            Steps(gateway, sim, 8000);
            var gates = sim.Capture(1).Economy.Buildings.Where(b => b.Kind == BuildingKind.Tollgate).ToArray();
            TestContext.WriteLine("tollgates=" + gates.Length + " complete=" + gates.Count(b => b.Complete) + " stone=" + sim.Capture(1).Economy.Stone);
            Assert.That(gates.Length, Is.GreaterThan(0), "石0から関所を建てる");
            Assert.That(FoundationNeedsStone(sim, 1), Is.False, "関所が建ったら基盤のための石は求めない");
        }

        private static ScenarioDefinition MatchScenario(ulong seed)
        {
            var s = Scenario(seed);
            s.Economy.Age2FoodCost = 0;
            s.Economy.Age2WoodCost = 0;
            s.Economy.Age2Ticks = 1;
            s.Economy.Age3FoodCost = 0;
            s.Economy.Age3WoodCost = 0;
            s.Economy.Age3Ticks = 1;
            return s;
        }

        [TestCase(CivKind.Tollgate, CivKind.Agrarian)]
        [TestCase(CivKind.Agrarian, CivKind.Tollgate)]
        [TestCase(CivKind.Tollgate, CivKind.Metallurgy)]
        [TestCase(CivKind.Metallurgy, CivKind.Tollgate)]
        public void TollgateMatchesReachTheSecondAgeWithoutFaultAndReplay(CivKind west, CivKind east)
        {
            var scenario = MatchScenario(21);
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
        public void NormalTollgateStartFindsASeedAndReplaysForTwentyThousandTicks()
        {
            ulong foundSeed = 0;
            CivKind foundWest = CivKind.Primitive, foundEast = CivKind.Primitive;
            // The normal start chooses its civilisation only once the barracks and villagers stand (about tick 1000), so
            // each seed is followed until both sides have chosen (as the sanctuary and metropolis searches do).
            for (ulong seed = 1; seed <= 300 && foundSeed == 0; seed++)
            {
                var probe = new Battle(MatchScenario(seed));
                var probeGateway = new CommandGateway(probe);
                for (int i = 0; i < 1500; i++)
                {
                    probeGateway.Step();
                    if (i % 20 != 19) continue;
                    var west = probe.Capture(1).Economy.Civ;
                    var east = probe.Capture(2).Economy.Civ;
                    if (west == CivKind.Tollgate || east == CivKind.Tollgate)
                    {
                        foundSeed = seed; foundWest = west; foundEast = east;
                        break;
                    }
                    if (west != CivKind.Primitive && east != CivKind.Primitive) break;
                }
            }
            if (foundSeed == 0)
            {
                TestContext.WriteLine("関所が選ばれる種は seed 1..300 では見つからなかった");
                Assert.Inconclusive("関所が選ばれる種が見つからない");
                return;
            }
            var scenario = MatchScenario(foundSeed);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            Steps(gateway, sim, 20000);
            TestContext.WriteLine("normal tollgate seed " + foundSeed + ": civs=" + foundWest + "/" + foundEast
                + ", tick=" + sim.Capture(1).Tick + ", ages=" + sim.Capture(1).Economy.Age + "/" + sim.Capture(2).Economy.Age);
            Assert.That(sim.Capture(1).Economy.Civ == CivKind.Tollgate || sim.Capture(2).Economy.Civ == CivKind.Tollgate, Is.True);
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

        private static void SetSoldierPosition(object world, Array soldiers, int index, SimPoint position)
        {
            var boxed = soldiers.GetValue(index);
            boxed.GetType().GetField("Position", Hidden).SetValue(boxed, position);
            soldiers.SetValue(boxed, index);
        }

        private static void SetTech(object world, uint faction, TechKind tech)
        {
            var economies = (Array)world.GetType().GetField("Economies", Hidden).GetValue(world);
            var boxed = economies.GetValue((int)faction - 1);
            var field = boxed.GetType().GetField("Techs", Hidden);
            field.SetValue(boxed, (ulong)field.GetValue(boxed) | (1UL << ((int)tech - 1)));
            economies.SetValue(boxed, (int)faction - 1);
        }
    }
}
