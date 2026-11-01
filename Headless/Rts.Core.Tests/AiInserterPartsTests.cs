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
    public sealed class AiInserterPartsTests
    {
        [Test]
        public void AutomaticPriorityPrefersProcessingBranchButHeldSplitterDoesNotChange()
        {
            var scenario = Scenario(5001);
            var simulation = new Battle(scenario);
            var world = World(simulation);
            SetExplored(world, 1);
            var feed = Method<bool>("FeedsOwnCore");
            int width = world.Config.Map.WidthCells;
            int splitter = -1, side = -1, forward = -1;
            Facing facing = Facing.East;
            for (int cell = 2; cell < world.Belts.Length - width - 4 && splitter < 0; cell++)
                foreach (Facing candidateFacing in new[] { Facing.East, Facing.North, Facing.West, Facing.South })
                {
                    int candidateForward = Next(cell, candidateFacing, width, world.Config.Map.HeightCells);
                    int candidateSide = Next(cell, TurnLeft(candidateFacing), width, world.Config.Map.HeightCells);
                    if (candidateForward < 0 || candidateSide < 0 || !world.Map.IsPassable(cell) || !world.Map.IsPassable(candidateSide)) continue;
                    if (!(bool)feed.Invoke(simulation, new object[] { candidateForward, 1U }) || (bool)feed.Invoke(simulation, new object[] { cell, 1U })) continue;
                    splitter = cell; side = candidateSide; forward = candidateForward; facing = candidateFacing; break;
                }
            Assert.That(splitter, Is.GreaterThanOrEqualTo(0));
            int sideFacingCell = side;
            Facing sideFacing = TurnLeft(facing);
            int side1 = Next(sideFacingCell, sideFacing, width, world.Config.Map.HeightCells);
            int side2 = Next(side1, sideFacing, width, world.Config.Map.HeightCells);
            int origin = Next(side2, sideFacing, width, world.Config.Map.HeightCells);
            Assert.That(origin, Is.GreaterThanOrEqualTo(0));
            world.Belts[splitter] = new BeltState { FactionId = 1, Facing = facing, Component = BeltComponentKind.Splitter, Hp = world.Config.Economy.BeltHp };
            foreach (int cell in new[] { side, side1, side2 })
                world.Belts[cell] = new BeltState { FactionId = 1, Facing = sideFacing, Hp = world.Config.Economy.BeltHp };
            world.Buildings = new[] { new BuildingState { Id = 1, FactionId = 1, Kind = BuildingKind.Sawmill, OriginCell = origin, WorkCell = side2, Alive = true, Complete = true } };
            world.NextBuildingId = 2;
            Invoke(simulation, "DecideAutomaticSplitterPriority", 1U);
            Assert.That(world.Belts[splitter].Priority, Is.EqualTo(BeltPriority.Left));

            world.Belts[splitter].Priority = BeltPriority.None;
            world.Belts[splitter].Held = true;
            Invoke(simulation, "DecideAutomaticSplitterPriority", 1U);
            Assert.That(world.Belts[splitter].Priority, Is.EqualTo(BeltPriority.None));
        }

        [Test]
        public void FilterInserterFeedsProcessingBuildingAfterDecisionInterval()
        {
            var simulation = new Battle(Scenario(5002));
            var world = PrepareProcessingLine(simulation, false);
            Invoke(simulation, "DecideInserters", 1U);
            int inserter = world.ProcessingLines[0].BeltCells[0];
            Assert.That(world.Belts[inserter].Component, Is.EqualTo(BeltComponentKind.FilterInserter));
            Assert.That(world.Belts[inserter].SorterKind, Is.EqualTo(ResourceKind.Planks));
            Advance(simulation, 180);
            Assert.That(world.Buildings[0].Input, Is.GreaterThan(0));
        }

        [Test]
        public void LongInserterUsesTheNextCellWhenCoreSideIsOccupied()
        {
            var simulation = new Battle(Scenario(5003));
            var world = PrepareProcessingLine(simulation, true);
            int first = world.ProcessingLines[0].BeltCells[0];
            world.Belts[first].Component = BeltComponentKind.Splitter;
            Invoke(simulation, "DecideInserters", 1U);
            int inserter = world.ProcessingLines[0].BeltCells[1];
            Assert.That(world.Belts[inserter].Component, Is.EqualTo(BeltComponentKind.LongInserter));
            Assert.That(world.Belts[inserter].SorterKind, Is.EqualTo(ResourceKind.Planks));
            Advance(simulation, 220);
            Assert.That(world.Buildings[0].Input, Is.GreaterThan(0));
        }

        [Test]
        public void FlagOffKeepsEveryTickHashEqual()
        {
            var left = Scenario(5004);
            var right = Scenario(5004);
            left.Economy.AiInserterParts = false;
            right.Economy.AiInserterParts = false;
            var a = new Battle(left); var b = new Battle(right);
            for (long tick = 1; tick <= 500; tick++)
            {
                a.Step(tick, Array.Empty<ScheduledInput>());
                b.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(a.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(b.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void Extension87RoundTripsAiInserterPartsRules()
        {
            var scenario = Scenario(5005);
            scenario.Economy.AiInserterParts = true;
            scenario.Economy.AiInserterPartsMinStock = 77;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(value => value.Id == 87);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.AiInserterParts, Is.True);
            Assert.That(decoded.Economy.AiInserterPartsMinStock, Is.EqualTo(77));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        private static ScenarioDefinition Scenario(ulong seed)
        {
            var scenario = LiveGameRules.Create(seed, true, true, false, false);
            scenario.Economy.AiBeltParts = true;
            scenario.Economy.AiInserterParts = true;
            scenario.Economy.AiInserterPartsMinStock = 50;
            scenario.Economy.InserterIntervalTicks = 1;
            scenario.Economy.BeltTicksPerCell = 1;
            return scenario;
        }

        private static WorldState PrepareProcessingLine(Battle simulation, bool blocked)
        {
            var world = World(simulation);
            SetExplored(world, 1);
            ref var economy = ref world.Economies[0];
            economy.Age = 1; economy.Wood = 10000; economy.Planks = 10000; economy.Gears = 10000; economy.Fittings = 10000;
            var feed = Method<bool>("FeedsOwnCore");
            int width = world.Config.Map.WidthCells, height = world.Config.Map.HeightCells;
            int first = -1;
            for (int cell = 2; cell < world.Belts.Length - 8 && first < 0; cell++)
            {
                int rear = cell - 1, front = cell + 1;
                if (!world.Map.IsPassable(cell) || !world.Map.IsPassable(front) || !world.Map.IsPassable(front + 1) || !world.Map.IsPassable(front + 2)) continue;
                if ((bool)feed.Invoke(simulation, new object[] { rear, 1U })) first = cell;
            }
            Assert.That(first, Is.GreaterThanOrEqualTo(0));
            int origin = first + 4;
            var belts = world.Belts;
            for (int cell = first; cell <= first + 3; cell++)
                belts[cell] = new BeltState { FactionId = 1, Facing = Facing.East, Hp = world.Config.Economy.BeltHp };
            if (blocked) belts[first].Component = BeltComponentKind.Splitter;
            world.Buildings = new[] { new BuildingState { Id = 1, FactionId = 1, Kind = BuildingKind.Assembler, Recipe = RecipeKind.ArmsKit, OriginCell = origin, WorkCell = first + 3, Alive = true, Complete = true } };
            world.NextBuildingId = 2;
            world.ProcessingLines = new[] { new ProcessingLineState { Id = 1, FactionId = 1, Kind = ProcessingLineKind.Assembler,
                Manager = LineManager.Automatic, AssemblerId = 1, BeltCells = new[] { first, first + 1, first + 2, first + 3 }, BeltFacings = new[] { Facing.East, Facing.East, Facing.East, Facing.East } } };
            typeof(Battle).GetField("automaticEconomyAction", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(simulation, true);
            return world;
        }

        private static void Advance(Battle simulation, int ticks)
        {
            var world = World(simulation);
            var inserters = typeof(Battle).GetMethod("AdvanceInserters", BindingFlags.Instance | BindingFlags.NonPublic);
            var belts = typeof(Battle).GetMethod("AdvanceBelts", BindingFlags.Instance | BindingFlags.NonPublic);
            for (long tick = 1; tick <= ticks; tick++)
            {
                world.Tick = tick;
                inserters.Invoke(simulation, null);
                belts.Invoke(simulation, null);
            }
        }

        private static WorldState World(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);

        private static void SetExplored(WorldState world, uint faction)
        {
            var state = world.Factions[faction - 1];
            state.ExploredCells = Enumerable.Repeat(true, world.Belts.Length).ToArray();
            state.ExploredCellCount = state.ExploredCells.Length;
            world.Factions[faction - 1] = state;
        }

        private static void Invoke(Battle simulation, string name, uint faction)
            => typeof(Battle).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(simulation, new object[] { faction });

        private static MethodInfo Method<T>(string name)
            => typeof(Battle).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);

        private static int Next(int cell, Facing facing, int width, int height)
        {
            int x = cell % width, z = cell / width;
            return facing == Facing.North ? z + 1 < height ? cell + width : -1
                : facing == Facing.East ? x + 1 < width ? cell + 1 : -1
                : facing == Facing.South ? z > 0 ? cell - width : -1
                : x > 0 ? cell - 1 : -1;
        }

        private static Facing TurnLeft(Facing facing)
            => (Facing)(((int)facing + 3) % 4);
    }
}
