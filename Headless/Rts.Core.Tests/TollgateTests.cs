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

        private static (Battle sim, CommandGateway gateway, ScenarioDefinition scenario, ulong sequence) Enter(ulong seed)
        {
            var scenario = Scenario(seed);
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
    }
}
