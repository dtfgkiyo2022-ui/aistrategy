using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <summary>38.16 A: gears, component prices, mergers, priorities, and very-fast belts.</summary>
    public sealed class BeltCraftTests
    {
        private static ScenarioDefinition Scenario(bool beltCraft)
        {
            var scenario = MapGenerator.GenerateTerrain(3816UL, false, true);
            scenario.Economy.Ages = true;
            scenario.Economy.Industry = true;
            scenario.Economy.BeltComponents = true;
            scenario.Economy.BeltStack = true;
            scenario.Economy.CraftTree1 = true;
            scenario.Economy.CraftTree2 = true;
            scenario.Economy.CraftTree3 = true;
            scenario.Economy.CraftRequired = true;
            scenario.Economy.BeltCraft = beltCraft;
            return scenario;
        }

        private static ScenarioDefinition ControlledScenario()
        {
            var scenario = Scenario(true);
            var e = scenario.Economy;
            e.StartFood = 100000;
            e.StartWood = 100000;
            e.StartStone = 100000;
            e.StartMetal = 100000;
            e.AdvanceFoodCost = e.AdvanceWoodCost = e.Age2FoodCost = e.Age2WoodCost = e.Age3FoodCost = e.Age3WoodCost = 0;
            e.AdvanceTicks = e.Age2Ticks = e.Age3Ticks = 1;
            e.Planks = true;
            e.FastBeltAge = 1;
            e.AutoVillagerTarget = 0;
            e.AutoInfantryQueue = 0;
            return scenario;
        }

        private static Dictionary<string, string> Fields(Battle simulation)
            => DiagnosticComparison.Fields(simulation.CaptureDiagnostic()).ToDictionary(value => value.Key, value => value.Value);

        private static int Number(Dictionary<string, string> fields, string name)
            => int.Parse(fields[name], CultureInfo.InvariantCulture);

        private static void Steps(CommandGateway gateway, Battle simulation, int count)
        {
            for (int i = 0; i < count && !simulation.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static int CellOf(ScenarioDefinition scenario, SimPoint point)
            => (int)(point.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * scenario.Map.WidthCells
                + (int)(point.X.Raw / 65536 / scenario.Map.CellSizeMeters);

        private static int FindFreeSquare(ScenarioDefinition scenario, int size)
        {
            var blocked = new HashSet<int>(scenario.Map.BlockedCellIds);
            foreach (var node in scenario.ResourceNodes) blocked.Add(CellOf(scenario, node.Position));
            int width = scenario.Map.WidthCells, height = scenario.Map.HeightCells;
            int core = CellOf(scenario, scenario.Cores[0].Position), cx = core % width, cz = core / width;
            for (int radius = 8; radius < Math.Min(width, height) / 2; radius++)
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius) continue;
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x + size > width || z + size > height) continue;
                        bool free = true;
                        for (int oz = 0; oz < size && free; oz++)
                            for (int ox = 0; ox < size; ox++)
                                if (blocked.Contains((z + oz) * width + x + ox)) { free = false; break; }
                        if (free) return z * width + x;
                    }
            throw new InvalidOperationException("no free square");
        }

        private static int FindPassableRun(ScenarioDefinition scenario, int length)
        {
            var blocked = new HashSet<int>(scenario.Map.BlockedCellIds);
            foreach (var node in scenario.ResourceNodes) blocked.Add(CellOf(scenario, node.Position));
            int width = scenario.Map.WidthCells, height = scenario.Map.HeightCells;
            int core = CellOf(scenario, scenario.Cores[0].Position), coreX = core % width, coreZ = core / width;
            for (int z = 4; z < height - 4; z++)
                for (int x = 4; x + length <= width - 4; x++)
                {
                    if (Math.Max(Math.Abs(x - coreX), Math.Abs(z - coreZ)) < 8) continue;
                    bool free = true;
                    for (int dx = 0; dx < length && free; dx++)
                    {
                        int cell = z * width + x + dx;
                        if (blocked.Contains(cell) || scenario.Map.Terrain.Length == width * height && scenario.Map.Terrain[cell] != (byte)TerrainKind.Plain)
                            free = false;
                    }
                    if (free) return z * width + x;
                }
            throw new InvalidOperationException("no passable run");
        }

        private static (Battle simulation, CommandGateway gateway) AgeOne(ScenarioDefinition scenario)
        {
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Metallurgy));
            Steps(gateway, simulation, scenario.Economy.AdvanceTicks + 3);
            Assert.That(simulation.Capture(1).Economy.Age, Is.GreaterThanOrEqualTo(1));
            return (simulation, gateway);
        }

        private static int CountBelts(Battle simulation, uint faction = 1)
            => simulation.Capture(faction).Economy.Belts.Count(belt => belt.FactionId == faction);

        private static void SetStock(Battle simulation, ResourceKind kind, int amount)
        {
            var field = typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic);
            var world = (WorldState)field.GetValue(simulation);
            var economy = world.Economies[0];
            switch (kind)
            {
                case ResourceKind.Planks: economy.Planks = amount; break;
                case ResourceKind.Gears: economy.Gears = amount; break;
                case ResourceKind.Steel: economy.Steel = amount; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            world.Economies[0] = economy;
        }

        private static int BeltItems(Battle simulation)
        {
            var fields = Fields(simulation);
            return fields.Count(value => value.Key.StartsWith("Belts[", StringComparison.Ordinal)
                && value.Key.EndsWith("].Item", StringComparison.Ordinal) && value.Value != "0");
        }

        [Test]
        public void FlagOffIsHashStableEveryTickAndExtension80IsOmitted()
        {
            var source = Scenario(false);
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            Assert.That(decoded.Extensions.Any(extension => extension.Id == 80), Is.False);
            var left = new Battle(source);
            var right = new Battle(decoded);
            for (long tick = 1; tick <= 400; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void WireValuesAndCommandPayloadsAreAppended()
        {
            Assert.That((int)ResourceKind.Gears, Is.EqualTo(35));
            Assert.That((int)BuildingKind.GearWorkshop, Is.EqualTo(51));
            Assert.That((int)ProcessingLineKind.Gears, Is.EqualTo(23));
            Assert.That((int)EconomyCommandKind.SetBeltPriority, Is.EqualTo(29));
            Assert.That((int)BeltComponentKind.Merger, Is.EqualTo(6));
            Assert.That((int)BeltSpeed.VeryFast, Is.EqualTo(2));
            var fast = EconomyCommand.PlaceFastBelt(1, 1, new[] { 10 }, new[] { Facing.East });
            var veryFast = EconomyCommand.PlaceBelt(1, 2, new[] { 10 }, new[] { Facing.East }, BeltSpeed.VeryFast);
            Assert.That(fast.Enabled, Is.True);
            Assert.That(fast.TargetId, Is.EqualTo(0));
            Assert.That(veryFast.Enabled, Is.False);
            Assert.That(veryFast.TargetId, Is.EqualTo((uint)BeltSpeed.VeryFast));
            var merger = EconomyCommand.PlaceMerger(1, 3, 10, Facing.East, BeltPriority.Right);
            Assert.That(merger.IsMerger, Is.True);
            Assert.That(merger.BeltPriority, Is.EqualTo(BeltPriority.Right));
            var filter = EconomyCommand.PlaceFilterInserter(1, 4, 10, Facing.East, ResourceKind.Ore);
            Assert.That(filter.IsInserter, Is.True);
            Assert.That(filter.InserterComponent, Is.EqualTo(BeltComponentKind.FilterInserter));
            Assert.That(filter.SorterKind, Is.EqualTo(ResourceKind.Ore));
            var longInserter = EconomyCommand.PlaceLongInserter(1, 5, 10, Facing.East);
            Assert.That(longInserter.InserterComponent, Is.EqualTo(BeltComponentKind.LongInserter));
            var catapult = EconomyCommand.PlaceCatapult(1, 6, 10, 20, Facing.East, Facing.West);
            Assert.That(catapult.IsCatapult, Is.True);
            Assert.That(catapult.Cells, Is.EqualTo(new[] { 10, 20 }));
        }

        [Test]
        public void Extension80RoundTripsAllBeltCraftRules()
        {
            var source = Scenario(true);
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(value => value.Id == 80);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.BeltCraft, Is.True);
            Assert.That(decoded.Economy.GearWorkshopSizeCells, Is.EqualTo(2));
            Assert.That(decoded.Economy.GearWorkshopWoodCost, Is.EqualTo(80));
            Assert.That(decoded.Economy.GearMetalInput, Is.EqualTo(1));
            Assert.That(decoded.Economy.GearsPerBatch, Is.EqualTo(2));
            Assert.That(decoded.Economy.GearTicks, Is.EqualTo(40));
            Assert.That(decoded.Economy.MergerGears, Is.EqualTo(2));
            Assert.That(decoded.Economy.VeryFastBeltSteel, Is.EqualTo(1));
            Assert.That(decoded.Economy.FilterInserterGears, Is.EqualTo(3));
            Assert.That(decoded.Economy.CatapultRangeCells, Is.EqualTo(30));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void Extension80V2RoundTripsBeltCraftBFields()
        {
            var source = Scenario(true);
            source.Economy.FilterInserterGears = 9;
            source.Economy.CatapultBuffer = 31;
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            var extension = decoded.Extensions.Single(value => value.Id == 80);
            Assert.That(extension.Version, Is.EqualTo(2));
            Assert.That(decoded.Economy.FilterInserterGears, Is.EqualTo(9));
            Assert.That(decoded.Economy.CatapultBuffer, Is.EqualTo(31));
        }

        [Test]
        public void EconomyFrameExposesGearsOnlyWhenEnabled()
        {
            var on = new Battle(Scenario(true)).Capture(1).Economy;
            var off = new Battle(Scenario(false)).Capture(1).Economy;
            Assert.That(on.BeltCraftEnabled, Is.True);
            Assert.That(on.Gears, Is.EqualTo(0));
            Assert.That(off.BeltCraftEnabled, Is.False);
            Assert.That(off.Gears, Is.EqualTo(0));
        }

        [Test]
        public void FilterAndLongInsertersUseTheirNewReachAndSelector()
        {
            var scenario = Scenario(true);
            scenario.Economy.Inserters = true;
            scenario.Economy.BeltTicksPerCell = 1;
            scenario.Economy.InserterIntervalTicks = 1;
            scenario.Economy.BeltStack = true;
            int run = FindPassableRun(scenario, 10);
            int filter = run + 3, longInserter = run + 7;
            scenario.Belts = new[]
            {
                new BeltDefinition { Cell = filter - 1, FactionId = 1, Facing = Facing.East, Item = ResourceKind.Ore },
                new BeltDefinition { Cell = filter, FactionId = 1, Facing = Facing.East, Component = BeltComponentKind.FilterInserter, SorterKind = ResourceKind.Metal },
                new BeltDefinition { Cell = filter + 1, FactionId = 1, Facing = Facing.East },
                new BeltDefinition { Cell = longInserter - 2, FactionId = 1, Facing = Facing.East, Item = ResourceKind.Metal },
                new BeltDefinition { Cell = longInserter, FactionId = 1, Facing = Facing.East, Component = BeltComponentKind.LongInserter },
                new BeltDefinition { Cell = longInserter + 2, FactionId = 1, Facing = Facing.East }
            };
            var simulation = new Battle(scenario);
            simulation.Step(1, Array.Empty<ScheduledInput>());
            var belts = simulation.Capture(1).Economy.Belts.ToDictionary(b => b.Cell);
            Assert.That(belts[filter - 1].Item, Is.EqualTo(ResourceKind.Ore), "filter rejects the non-selected item");
            Assert.That(belts[longInserter + 2].Item, Is.EqualTo(ResourceKind.Metal), "long inserter reaches two cells");
        }

        [Test]
        public void CatapultDeliversAfterTimeoutAndRemovingReceiverRemovesPair()
        {
            var scenario = Scenario(true);
            scenario.Economy.BeltTicksPerCell = 1;
            int run = FindPassableRun(scenario, 8);
            int launcher = run + 2, receiver = run + 5;
            scenario.Belts = new[]
            {
                new BeltDefinition { Cell = launcher - 1, FactionId = 1, Facing = Facing.East, Item = ResourceKind.Ore },
                new BeltDefinition { Cell = launcher, FactionId = 1, Facing = Facing.East, Component = BeltComponentKind.Catapult, PairCell = receiver },
                new BeltDefinition { Cell = receiver, FactionId = 1, Facing = Facing.East, Component = BeltComponentKind.CatapultReceiver, PairCell = launcher },
                new BeltDefinition { Cell = receiver + 1, FactionId = 1, Facing = Facing.East }
            };
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            for (long tick = 1; tick <= scenario.Economy.CatapultMaxWaitTicks + scenario.Economy.CatapultFlightTicks + 5; tick++)
                gateway.Step();
            var belts = simulation.Capture(1).Economy.Belts.ToDictionary(b => b.Cell);
            Assert.That(belts[receiver + 1].Item, Is.EqualTo(ResourceKind.Ore));
            gateway.SubmitEconomy(EconomyCommand.RemoveBelt(1, 1, receiver));
            gateway.Step();
            Assert.That(simulation.Capture(1).Economy.Belts.Any(b => b.Cell == launcher || b.Cell == receiver), Is.False);
        }

        [Test]
        public void GearWorkshopTurnsOneMetalIntoTwoGearsAfterFortyTicks()
        {
            var scenario = ControlledScenario();
            var state = AgeOne(scenario);
            int origin = FindFreeSquare(scenario, scenario.Economy.GearWorkshopSizeCells);
            state.gateway.SubmitEconomy(EconomyCommand.Place(1, 10, BuildingKind.GearWorkshop, origin, Facing.North));
            int completeTick = -1;
            for (int i = 0; i < 1000; i++)
            {
                state.gateway.Step();
                var workshop = state.simulation.Capture(1).Economy.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.GearWorkshop);
                if (workshop.Id != 0 && workshop.Complete) { completeTick = (int)state.simulation.Capture(1).Tick; break; }
            }
            Assert.That(completeTick, Is.GreaterThan(0), "gear workshop completed");
            int before = state.simulation.Capture(1).Economy.Gears;
            Steps(state.gateway, state.simulation, scenario.Economy.GearTicks + 1);
            int after = state.simulation.Capture(1).Economy.Gears;
            Assert.That(after - before, Is.EqualTo(scenario.Economy.GearsPerBatch),
                "before=" + before + " after=" + after + " gears=" + state.simulation.Capture(1).Economy.Gears
                + " metal=" + state.simulation.Capture(1).Economy.Metal);
            Assert.That(state.simulation.Capture(1).Economy.Gears, Is.GreaterThanOrEqualTo(scenario.Economy.GearsPerBatch));
        }

        [Test]
        public void GearShortageBlocksFastComponentsAndNormalBeltsNeedNoGear()
        {
            var scenario = ControlledScenario();
            var state = AgeOne(scenario);
            int first = FindFreeSquare(scenario, 1);
            state.gateway.SubmitEconomy(EconomyCommand.PlaceBelt(1, 10, new[] { first }, new[] { Facing.East }));
            state.gateway.SubmitEconomy(EconomyCommand.PlaceFastBelt(1, 11, new[] { first + 2 }, new[] { Facing.East }));
            Steps(state.gateway, state.simulation, 1);
            // At age one CraftRequired also keeps the old plank price. An authored ordinary belt isolates the
            // no-gear part, while the placement command below checks the new gear shortage.
            Assert.That(CountBelts(state.simulation), Is.EqualTo(0), "the command has no plank stock yet; it must not be mistaken for a gear failure");

            scenario = ControlledScenario();
            state = AgeOne(scenario);
            SetStock(state.simulation, ResourceKind.Planks, 2);
            first = FindFreeSquare(scenario, 1);
            state.gateway.SubmitEconomy(EconomyCommand.PlaceBelt(1, 13, new[] { first }, new[] { Facing.East }));
            Steps(state.gateway, state.simulation, 1);
            Assert.That(CountBelts(state.simulation), Is.EqualTo(1), "ordinary belt has no gear price");

            SetStock(state.simulation, ResourceKind.Gears, 0);
            int planksBefore = state.simulation.Capture(1).Economy.Planks;
            state.gateway.SubmitEconomy(EconomyCommand.PlaceFastBelt(1, 14, new[] { first + 2 }, new[] { Facing.East }));
            Steps(state.gateway, state.simulation, 1);
            Assert.That(CountBelts(state.simulation), Is.EqualTo(1),
                "fast belt is refused without gears; enabled=" + state.simulation.Capture(1).Economy.BeltCraftEnabled
                + " gears=" + state.simulation.Capture(1).Economy.Gears
                + " belts=" + string.Join(",", state.simulation.Capture(1).Economy.Belts.Select(b => b.Cell + ":" + b.Speed)));
            SetStock(state.simulation, ResourceKind.Gears, scenario.Economy.FastBeltGears);
            state.gateway.SubmitEconomy(EconomyCommand.PlaceFastBelt(1, 15, new[] { first + 2 }, new[] { Facing.East }));
            Steps(state.gateway, state.simulation, 1);
            Assert.That(CountBelts(state.simulation), Is.EqualTo(2));
            Assert.That(state.simulation.Capture(1).Economy.Gears, Is.EqualTo(0));
            Assert.That(state.simulation.Capture(1).Economy.Planks, Is.EqualTo(planksBefore - 1));
        }

        [Test]
        public void MergerCombinesTwoInputsAndLeftPriorityMakesRightWait()
        {
            var scenario = Scenario(true);
            scenario.Economy.BeltTicksPerCell = 1;
            int width = scenario.Map.WidthCells;
            int merger = FindFreeSquare(scenario, 1) + 3;
            int left = merger + width, rear = merger - 1, output = merger + 1;
            scenario.Belts = new[]
            {
                new BeltDefinition { Cell = left, FactionId = 1, Facing = Facing.South, Item = ResourceKind.Ore },
                new BeltDefinition { Cell = rear, FactionId = 1, Facing = Facing.East, Item = ResourceKind.Metal },
                new BeltDefinition { Cell = merger, FactionId = 1, Facing = Facing.East, Component = BeltComponentKind.Merger, Priority = BeltPriority.Left },
                new BeltDefinition { Cell = output, FactionId = 1, Facing = Facing.East }
            };
            var simulation = new Battle(scenario);
            for (long tick = 1; tick <= 20; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            Assert.That(BeltItems(simulation), Is.EqualTo(2));
            var fields = Fields(simulation);
            Assert.That(fields[$"Belts[{output}].Item"], Is.Not.EqualTo("0"));
            Assert.That(fields[$"Belts[{rear}].Item"], Is.EqualTo("0"), "the rear input is admitted");
        }

        [Test]
        public void SplitterLeftPriorityUsesLeftUntilBlockedWithoutLoss()
        {
            var scenario = Scenario(true);
            scenario.Economy.BeltTicksPerCell = 1;
            int width = scenario.Map.WidthCells;
            int splitter = FindFreeSquare(scenario, 1) + 4;
            scenario.Belts = new[]
            {
                new BeltDefinition { Cell = splitter - 1, FactionId = 1, Facing = Facing.East, Item = ResourceKind.Ore },
                new BeltDefinition { Cell = splitter, FactionId = 1, Facing = Facing.East, Component = BeltComponentKind.Splitter, Priority = BeltPriority.Left },
                new BeltDefinition { Cell = splitter + width, FactionId = 1, Facing = Facing.North },
                new BeltDefinition { Cell = splitter + 1, FactionId = 1, Facing = Facing.East }
            };
            var simulation = new Battle(scenario);
            for (long tick = 1; tick <= 3; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            var fields = Fields(simulation);
            Assert.That(fields[$"Belts[{splitter + width}].Item"], Is.EqualTo(((byte)ResourceKind.Ore).ToString(CultureInfo.InvariantCulture)));

            scenario.Belts[0] = new BeltDefinition { Cell = splitter - 1, FactionId = 1, Facing = Facing.East, Item = ResourceKind.Metal };
            scenario.Belts[2] = new BeltDefinition { Cell = splitter + width, FactionId = 1, Facing = Facing.North, Item = ResourceKind.Ore };
            simulation = new Battle(scenario);
            for (long tick = 1; tick <= 3; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            fields = Fields(simulation);
            Assert.That(fields[$"Belts[{splitter + 1}].Item"], Is.EqualTo(((byte)ResourceKind.Metal).ToString(CultureInfo.InvariantCulture)));
        }

        [Test]
        public void VeryFastBeltRequiresThirdAgeAndMovesInTwoTicks()
        {
            var scenario = ControlledScenario();
            var state = AgeOne(scenario);
            int cell = FindFreeSquare(scenario, 1);
            state.gateway.SubmitEconomy(EconomyCommand.PlaceBelt(1, 10, new[] { cell }, new[] { Facing.East }, BeltSpeed.VeryFast));
            Steps(state.gateway, state.simulation, 1);
            Assert.That(CountBelts(state.simulation), Is.EqualTo(0));

            int next = cell + 1;
            scenario.Belts = new[]
            {
                new BeltDefinition { Cell = cell, FactionId = 1, Facing = Facing.East, Speed = BeltSpeed.VeryFast, Item = ResourceKind.Ore },
                new BeltDefinition { Cell = next, FactionId = 1, Facing = Facing.East }
            };
            var fast = new Battle(scenario);
            fast.Step(1, Array.Empty<ScheduledInput>());
            Assert.That(fast.Capture(1).Economy.Belts.Single(b => b.Cell == cell).Item, Is.EqualTo(ResourceKind.Ore));
            fast.Step(2, Array.Empty<ScheduledInput>());
            Assert.That(fast.Capture(1).Economy.Belts.Single(b => b.Cell == next).Item, Is.EqualTo(ResourceKind.Ore));
        }

        [Test]
        public void AiDoesNotBuildGearWorkshopWithoutAGearStockTarget()
        {
            // The automatic economy places no gear-costing parts yet, so by default it must not spend metal on gears.
            var scenario = LiveGameRules.Create(101UL, true, true, false, false);
            scenario.Economy.AiBeltParts = false;
            Assert.That(scenario.Economy.BeltCraftAiGearsStockTarget, Is.EqualTo(0));
            scenario.VerificationTickLimit = 8000;
            var simulation = new Battle(scenario);
            for (long tick = 1; tick <= 8000 && !simulation.Capture(1).Result.HasEnded; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());
            var economy = simulation.Capture(1).Economy;
            Assert.That(economy.Buildings.Any(building => building.Kind == BuildingKind.GearWorkshop), Is.False);
            Assert.That(simulation.CaptureLargeEconomyObservation().Factions[0].GearsProduced, Is.EqualTo(0));
            Assert.That(CountBelts(simulation), Is.GreaterThan(0));
        }

        [Test]
        public void AiBuildsGearWorkshopKeepsStockAndContinuesBelts()
        {
            var scenario = LiveGameRules.Create(101UL, true, true, false, false);
            scenario.Economy.AiBeltParts = false;
            scenario.Economy.BeltCraftAiGearsStockTarget = 20;
            scenario.VerificationTickLimit = 8000;
            var simulation = new Battle(scenario);
            for (long tick = 1; tick <= 8000 && !simulation.Capture(1).Result.HasEnded; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());
            var economy = simulation.Capture(1).Economy;
            var observation = simulation.CaptureLargeEconomyObservation().Factions[0];
            Assert.That(economy.Buildings.Any(building => building.Kind == BuildingKind.GearWorkshop), Is.True);
            Assert.That(observation.GearsProduced, Is.GreaterThan(0));
            Assert.That(economy.Gears, Is.GreaterThanOrEqualTo(0));
            Assert.That(CountBelts(simulation), Is.GreaterThan(0));
        }
    }
}
