using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class AiBeltPartsTests
    {
        [Test]
        public void Extension85RoundTripsAiBeltPartsRules()
        {
            var scenario = LiveGameRules.Create(85UL, true, true, false, false);
            scenario.Economy.AiBeltParts = true;
            scenario.Economy.AiBeltClusterTries = 5;
            scenario.Economy.AiBeltClusterRetryTicks = 4321;
            scenario.Economy.AiMergeMaxCells = 17;
            scenario.Economy.AiUndergroundDetourPermille = 1600;
            scenario.Economy.AiCatapultMinRouteCells = 41;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(value => value.Id == 85);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.AiBeltParts, Is.True);
            Assert.That(decoded.Economy.AiBeltClusterTries, Is.EqualTo(5));
            Assert.That(decoded.Economy.AiBeltClusterRetryTicks, Is.EqualTo(4321));
            Assert.That(decoded.Economy.AiMergeMaxCells, Is.EqualTo(17));
            Assert.That(decoded.Economy.AiUndergroundDetourPermille, Is.EqualTo(1600));
            Assert.That(decoded.Economy.AiCatapultMinRouteCells, Is.EqualTo(41));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void MergerCanJoinAnAutomaticBeltMoreThanTwelveCellsFromTheStart()
        {
            var scenario = LiveGameRules.Create(8801UL, true, true, false, false);
            var simulation = new Battle(scenario);
            var world = World(simulation);
            SetExplored(world, 1);
            int width = world.Config.Map.WidthCells;
            int start = FindOpenRun(world, scenario, 24);
            int merger = start + 20, destination = merger + width;
            world.Belts[merger] = new BeltState { FactionId = 1, Facing = Facing.North, Hp = world.Config.Economy.BeltHp };
            var taken = Enumerable.Repeat(true, world.Belts.Length).ToArray();
            for (int i = 0; i <= 20; i++) taken[start + i] = false;
            taken[destination] = false;
            var routeMethod = typeof(Battle).GetMethod("BeltRoute", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(uint), typeof(int), typeof(bool[]), typeof(Func<int, int, bool>) }, null);
            var route = ((int[] cells, Facing[] facings))routeMethod.Invoke(simulation,
                new object[] { 1U, start, taken, (Func<int, int, bool>)((from, next) => next == destination) });
            Assert.That(route.cells, Does.Contain(merger));
            Assert.That(route.cells.Length, Is.EqualTo(21), "the route reaches the existing automatic belt after more than 12 cells");
        }

        [Test]
        public void UndergroundRouteCrossesRiverAndCarriesCargoToTheCore()
        {
            var scenario = LiveGameRules.Create(8802UL, true, true, false, false);
            var core = CellOf(scenario, scenario.Cores[0].Position);
            int width = scenario.Map.WidthCells, coreX = core % width, coreZ = core / width;
            var river = new List<int>();
            for (int x = coreX - 7; x <= coreX - 5; x++) river.Add(coreZ * width + x);
            scenario.Map.BlockedCellIds = scenario.Map.BlockedCellIds.Concat(river).Distinct().ToArray();
            scenario.Map.Terrain = new byte[width * scenario.Map.HeightCells];
            foreach (int cell in river) scenario.Map.Terrain[cell] = (byte)TerrainKind.River;
            var simulation = new Battle(scenario);
            var world = PreparePartsWorld(simulation, 1, 2);
            int source = coreX - 10 + coreZ * width;
            var method = typeof(Battle).GetMethod("TryLayAiUndergroundRoute", BindingFlags.Instance | BindingFlags.NonPublic);
            bool placed = (bool)method.Invoke(simulation, new object[] { 1U, source, ResourceKind.Wood, 0, 999 });
            Assert.That(placed, Is.True);
            int entrance = Array.FindIndex(world.Belts, belt => belt.Component == BeltComponentKind.UndergroundEntrance);
            int exit = world.Belts[entrance].PairCell;
            Assert.That(entrance, Is.GreaterThanOrEqualTo(0));
            Assert.That(exit, Is.GreaterThanOrEqualTo(0));
            int before = world.Economies[0].Wood;
            int feed = BeltPrevious(entrance, world.Belts[entrance].Facing, width);
            world.Belts[feed].Item = ResourceKind.Wood;
            world.Belts[feed].StackCount = 1;
            AdvanceCargo(simulation, 120);
            Assert.That(world.Economies[0].Wood, Is.GreaterThan(before));
        }

        [Test]
        public void LongRouteUsesCatapultAndReceiverAndDeliversCargo()
        {
            var scenario = LiveGameRules.Create(8803UL, true, true, false, false);
            var simulation = new Battle(scenario);
            var world = PreparePartsWorld(simulation, 1, 2);
            int core = world.Map.Cell(world.Cores[0].Definition.Position), width = world.Config.Map.WidthCells;
            int source = core - 20;
            var method = typeof(Battle).GetMethod("TryLayAiCatapultRoute", BindingFlags.Instance | BindingFlags.NonPublic);
            bool placed = (bool)method.Invoke(simulation, new object[] { 1U, source, ResourceKind.Wood, 0, 100 });
            Assert.That(placed, Is.True);
            int launcher = Array.FindIndex(world.Belts, belt => belt.Component == BeltComponentKind.Catapult);
            int receiver = world.Belts[launcher].PairCell;
            Assert.That(launcher, Is.GreaterThanOrEqualTo(0));
            Assert.That(world.Belts[receiver].Component, Is.EqualTo(BeltComponentKind.CatapultReceiver));
            int before = world.Economies[0].Wood;
            int rear = BeltPrevious(launcher, world.Belts[launcher].Facing, width);
            world.Belts[rear].Item = ResourceKind.Wood;
            world.Belts[rear].StackCount = 1;
            AdvanceCargo(simulation, 120);
            Assert.That(world.Economies[0].Wood, Is.GreaterThan(before));
        }

        [Test]
        public void FlagOffOmitsExtensionAndKeepsTickHashesStable()
        {
            var left = LiveGameRules.Create(86UL, true, true, false, false);
            var right = LiveGameRules.Create(86UL, true, true, false, false);
            left.Economy.AiBeltParts = false;
            right.Economy.AiBeltParts = false;
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(left)).Extensions.Any(value => value.Id == 85), Is.False);
            var a = new Battle(left);
            var b = new Battle(right);
            for (long tick = 1; tick <= 500; tick++)
            {
                a.Step(tick, Array.Empty<ScheduledInput>());
                b.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(a.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(b.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void LiveRulesEnableAiBeltPartsWithBeltCraftOnly()
        {
            var large = LiveGameRules.Create(87UL, true, true, false, false);
            var small = LiveGameRules.Create(87UL, false, true, false, false);
            Assert.That(large.Economy.BeltCraft, Is.True);
            Assert.That(large.Economy.AiBeltParts, Is.True);
            Assert.That(small.Economy.AiBeltParts, Is.False);
        }

        [Test]
        public void ComponentRouteBudgetIsAtMostTwelvePerDecision()
        {
            var scenario = LiveGameRules.Create(901UL, true, true, false, false);
            scenario.Economy.AiBeltClusterTries = 3;
            scenario.VerificationTickLimit = 3000;
            var simulation = new Battle(scenario);
            for (long tick = 1; tick <= 3000 && !simulation.Capture(1).Result.HasEnded; tick++)
            {
                simulation.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(simulation.LastAiBeltDecisionRouteCalls, Is.LessThanOrEqualTo(12), "tick " + tick);
            }
        }

        private static WorldState World(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);

        private static WorldState PreparePartsWorld(Battle simulation, uint faction, int age)
        {
            var world = World(simulation);
            SetExplored(world, faction);
            var economy = world.Economies[faction - 1];
            economy.Age = (byte)age;
            economy.Wood = 100000;
            economy.Planks = 100000;
            economy.Gears = 100000;
            economy.Fittings = 100000;
            economy.Rope = 100000;
            economy.Steel = 100000;
            world.Economies[faction - 1] = economy;
            typeof(Battle).GetField("automaticEconomyAction", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(simulation, true);
            return world;
        }

        private static void SetExplored(WorldState world, uint faction)
        {
            var state = world.Factions[faction - 1];
            state.ExploredCells = Enumerable.Repeat(true, world.Belts.Length).ToArray();
            state.ExploredCellCount = state.ExploredCells.Length;
            world.Factions[faction - 1] = state;
        }

        private static int FindOpenRun(WorldState world, ScenarioDefinition scenario, int length)
        {
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            for (int z = 2; z < height - 2; z++)
                for (int x = 2; x + length < width - 2; x++)
                {
                    bool open = true;
                    for (int dx = 0; dx < length && open; dx++)
                    {
                        int cell = z * width + x + dx;
                        open = world.Map.IsPassable(cell) && !scenario.ResourceNodes.Any(node => CellOf(scenario, node.Position) == cell);
                    }
                    if (open) return z * width + x;
                }
            throw new InvalidOperationException("no open belt run");
        }

        private static int CellOf(ScenarioDefinition scenario, SimPoint point)
            => (int)(point.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * scenario.Map.WidthCells
                + (int)(point.X.Raw / 65536 / scenario.Map.CellSizeMeters);

        private static int BeltPrevious(int cell, Facing facing, int width)
            => facing == Facing.North ? cell - width : facing == Facing.East ? cell - 1
                : facing == Facing.South ? cell + width : cell + 1;

        private static void AdvanceCargo(Battle simulation, int ticks)
        {
            var belts = typeof(Battle).GetMethod("AdvanceBelts", BindingFlags.Instance | BindingFlags.NonPublic);
            var catapults = typeof(Battle).GetMethod("AdvanceCatapults", BindingFlags.Instance | BindingFlags.NonPublic);
            var world = World(simulation);
            for (long tick = 1; tick <= ticks; tick++)
            {
                world.Tick = tick;
                belts.Invoke(simulation, null);
                catapults.Invoke(simulation, null);
            }
        }
    }
}
