using System;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class EasyRebuildTests
    {
        private static ScenarioDefinition Scenario(bool enabled)
        {
            var scenario = MapGenerator.GenerateTerrain(4917UL, false, true);
            scenario.Economy.Industry = true;
            scenario.Economy.EasyRebuild = enabled;
            scenario.Economy.MoveBuildingPauseTicks = 60;
            scenario.Economy.StartWood = 1000;
            return scenario;
        }

        [Test]
        public void Extension49RoundTripsAndOffAddsNoExtension()
        {
            var off = Scenario(false);
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(Scenario(false))));
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(off)).Extensions.Any(x => x.Id == 49), Is.False);

            var on = Scenario(true);
            on.Economy.MoveBuildingPauseTicks = 73;
            var bytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(x => x.Id == 49).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.EasyRebuild, Is.True);
            Assert.That(decoded.Economy.MoveBuildingPauseTicks, Is.EqualTo(73));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void MoveAndRemoveCommandsAreNoOpsWhenFlagIsOff()
        {
            var scenario = Scenario(false);
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            gateway.SubmitEconomy(EconomyCommand.MoveBuilding(1, 1, 1, 20, Facing.East));
            gateway.SubmitEconomy(EconomyCommand.RemoveBuilding(1, 2, 1));
            gateway.Step();
            var control = new Battle(Scenario(false));
            var controlGateway = new CommandGateway(control);
            controlGateway.SubmitEconomy(EconomyCommand.MoveBuilding(1, 1, 1, 20, Facing.East));
            controlGateway.SubmitEconomy(EconomyCommand.RemoveBuilding(1, 2, 1));
            controlGateway.Step();
            Assert.That(simulation.CaptureDiagnostic().CanonicalState, Is.EqualTo(control.CaptureDiagnostic().CanonicalState));
        }

        [Test]
        public void MoveCommandRoundTripsThroughInputBinary()
        {
            var input = new ScheduledInput(7, 0, 1, EconomyCommand.MoveBuilding(1, 4, 12, 345, Facing.West));
            var bytes = InputBinary.Encode(input);
            Assert.That(InputBinary.Encode(InputBinary.Decode(bytes)), Is.EqualTo(bytes));
            Assert.That(InputBinary.Decode(bytes).Economy.Kind, Is.EqualTo(EconomyCommandKind.MoveBuilding));
        }

        [Test]
        public void FlagOffMatchesTheHistoricalBytesAndHashesThroughThirtyThousandTicks()
        {
            var historical = MapGenerator.GenerateTerrain(4917UL, false, true);
            historical.Economy.StartWood = 1000;
            var flagOff = Scenario(false);
            Assert.That(ScenarioBinary.Encode(flagOff), Is.EqualTo(ScenarioBinary.Encode(historical)));

            var left = new Battle(historical);
            var right = new Battle(flagOff);
            var leftGateway = new CommandGateway(left);
            var rightGateway = new CommandGateway(right);
            leftGateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            rightGateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            leftGateway.SubmitEconomy(EconomyCommand.PlaceBelt(1, 2, new[] { 1000 }, new[] { Facing.East }));
            rightGateway.SubmitEconomy(EconomyCommand.PlaceBelt(1, 2, new[] { 1000 }, new[] { Facing.East }));
            leftGateway.SubmitEconomy(EconomyCommand.RemoveBelt(1, 3, 1000));
            rightGateway.SubmitEconomy(EconomyCommand.RemoveBelt(1, 3, 1000));
            for (int tick = 0; tick < 30000; tick++)
            {
                leftGateway.Step();
                rightGateway.Step();
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
            Assert.That(left.Capture(1).Economy.Wood, Is.EqualTo(historical.Economy.StartWood - historical.Economy.BeltWoodCost));
        }

        [Test]
        public void EasyRebuildRefundsEveryBeltKindOnceAndReturnsItems()
        {
            var s = Scenario(true);
            s.Economy.BeltComponents = true;
            s.Economy.StartWood = 0;
            var cells = OpenCells(s, 6, 5);
            var normal = cells[0];
            var fast = cells[1];
            var splitter = cells[2];
            var sorter = cells[3];
            var entrance = cells[4];
            var exit = cells[5];
            s.Belts = new[]
            {
                new BeltDefinition { Cell = normal, FactionId = 1, Facing = Facing.East, Item = ResourceKind.Wood },
                new BeltDefinition { Cell = fast, FactionId = 1, Facing = Facing.East, Speed = BeltSpeed.Fast },
                new BeltDefinition { Cell = splitter, FactionId = 1, Facing = Facing.East, Component = BeltComponentKind.Splitter },
                new BeltDefinition { Cell = sorter, FactionId = 1, Facing = Facing.East, Component = BeltComponentKind.Sorter, SorterKind = ResourceKind.Ore, Item = ResourceKind.Ore },
                new BeltDefinition { Cell = entrance, FactionId = 1, Facing = Facing.East, Component = BeltComponentKind.UndergroundEntrance, PairCell = exit, Item = ResourceKind.Food },
                new BeltDefinition { Cell = exit, FactionId = 1, Facing = Facing.East, Component = BeltComponentKind.UndergroundExit, PairCell = entrance }
            };
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, 2, false));
            gateway.SubmitEconomy(EconomyCommand.RemoveBelt(1, 3, normal));
            gateway.SubmitEconomy(EconomyCommand.RemoveBelt(1, 4, fast));
            gateway.SubmitEconomy(EconomyCommand.RemoveBelt(1, 5, splitter));
            gateway.SubmitEconomy(EconomyCommand.RemoveBelt(1, 6, sorter));
            gateway.SubmitEconomy(EconomyCommand.RemoveBelt(1, 7, entrance));
            gateway.Step();
            var economy = sim.Capture(1).Economy;
            Assert.That(economy.Wood, Is.EqualTo(s.Economy.BeltWoodCost + s.Economy.FastBeltWoodCost
                + s.Economy.SplitterWoodCost + s.Economy.SorterWoodCost + s.Economy.UndergroundBeltWoodCost + 1));
            Assert.That(economy.Ore, Is.EqualTo(1));
            Assert.That(economy.Food, Is.EqualTo(s.Economy.StartFood + 1));
            Assert.That(economy.Belts.Any(b => b.Cell == entrance || b.Cell == exit && b.FactionId != 0), Is.False);
        }

        [Test]
        public void EasyRebuildRefundsACompletedBuildingAndAnUnfinishedBuilding()
        {
            var complete = PlaceBarracks(true);
            int paidWood = complete.Scenario.Economy.BarracksWoodCost;
            int afterPlacement = complete.Simulation.Capture(1).Economy.Wood;
            while (!complete.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == complete.Building.Id).Complete)
                complete.Gateway.Step();
            complete.Gateway.SubmitEconomy(EconomyCommand.RemoveBuilding(1, ++complete.Sequence, complete.Building.Id));
            complete.Gateway.Step();
            Assert.That(complete.Simulation.Capture(1).Economy.Wood, Is.EqualTo(afterPlacement + paidWood));

            var unfinished = PlaceBarracks(true);
            int beforeRemoval = unfinished.Simulation.Capture(1).Economy.Wood;
            unfinished.Gateway.SubmitEconomy(EconomyCommand.RemoveBuilding(1, ++unfinished.Sequence, unfinished.Building.Id));
            unfinished.Gateway.Step();
            Assert.That(unfinished.Simulation.Capture(1).Economy.Wood, Is.EqualTo(unfinished.Scenario.Economy.StartWood));
            Assert.That(beforeRemoval, Is.LessThan(unfinished.Scenario.Economy.StartWood));
        }

        [Test]
        public void MoveIsImmediateAndTrainingIsPausedForExactlySixtyTicks()
        {
            var fixture = PlaceBarracks(true);
            while (!fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == fixture.Building.Id).Complete)
                fixture.Gateway.Step();
            fixture.Gateway.SubmitEconomy(EconomyCommand.Train(1, ++fixture.Sequence, fixture.Building.Id, UnitKind.Infantry));
            fixture.Gateway.Step();
            var before = fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == fixture.Building.Id);
            bool moved = false;
            for (int cell = 0; cell < fixture.Scenario.Map.WidthCells * fixture.Scenario.Map.HeightCells && !moved; cell++)
            {
                fixture.Gateway.SubmitEconomy(EconomyCommand.MoveBuilding(1, ++fixture.Sequence, before.Id, cell, Facing.East));
                fixture.Gateway.Step();
                var current = fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == fixture.Building.Id);
                moved = current.Center.X.Raw != before.Center.X.Raw || current.Center.Z.Raw != before.Center.Z.Raw;
            }
            Assume.That(moved, Is.True, "a legal relocation site");
            var paused = fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == fixture.Building.Id).TrainRemaining;
            for (int i = 0; i < 60; i++)
            {
                fixture.Gateway.Step();
                Assert.That(fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == fixture.Building.Id).TrainRemaining, Is.EqualTo(paused));
            }
            fixture.Gateway.Step();
            Assert.That(fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == fixture.Building.Id).TrainRemaining, Is.LessThan(paused));
        }

        [Test]
        public void ReservedBuildingCannotMoveAndRemovalReturnsNoConstructionCost()
        {
            var s = Scenario(true);
            s.Economy.BuildingReservations = true;
            s.Economy.ReservationLimit = 1;
            s.Economy.ReservationCheckTicks = 1;
            s.Economy.StartWood = 0;
            s.Economy.BarracksWoodCost = 100000;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, 2, false));
            gateway.Step();
            BuildingView reserved = default(BuildingView);
            for (int cell = 0; cell < s.Map.WidthCells * s.Map.HeightCells && reserved.Id == 0; cell++)
            {
                gateway.SubmitEconomy(EconomyCommand.Place(1, 3, BuildingKind.Barracks, cell));
                gateway.Step();
                reserved = sim.Capture(1).Economy.Buildings.FirstOrDefault(b => b.Reserved);
            }
            Assume.That(reserved.Id, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.MoveBuilding(1, 4, reserved.Id, reserved.Id > 0 ? 1 : 0, Facing.East));
            gateway.SubmitEconomy(EconomyCommand.RemoveBuilding(1, 5, reserved.Id));
            gateway.Step();
            Assert.That(sim.Capture(1).Economy.Buildings.Any(b => b.Id == reserved.Id), Is.False);
            Assert.That(sim.Capture(1).Economy.Wood, Is.EqualTo(0));
        }

        [Test]
        public void InvalidMoveTargetLeavesBuildingAndEconomyUntouched()
        {
            var fixture = PlaceBarracks(true);
            var before = fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == fixture.Building.Id);
            int wood = fixture.Simulation.Capture(1).Economy.Wood;
            int blockedCell = fixture.Scenario.Map.BlockedCellIds.First();

            fixture.Gateway.SubmitEconomy(EconomyCommand.MoveBuilding(1, ++fixture.Sequence, before.Id, blockedCell, Facing.East));
            fixture.Gateway.Step();

            var after = fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == fixture.Building.Id);
            Assert.That(after.Center.X.Raw, Is.EqualTo(before.Center.X.Raw));
            Assert.That(after.Center.Z.Raw, Is.EqualTo(before.Center.Z.Raw));
            Assert.That(after.Facing, Is.EqualTo(before.Facing));
            Assert.That(fixture.Simulation.Capture(1).Economy.Wood, Is.EqualTo(wood));
        }

        [Test]
        public void RemovingAfterMoveRefundsTheRecordedConstructionCost()
        {
            var fixture = PlaceBarracks(true);
            while (!fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == fixture.Building.Id).Complete)
                fixture.Gateway.Step();
            var before = fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == fixture.Building.Id);
            bool moved = false;
            for (int cell = 0; cell < fixture.Scenario.Map.WidthCells * fixture.Scenario.Map.HeightCells && !moved; cell++)
            {
                fixture.Gateway.SubmitEconomy(EconomyCommand.MoveBuilding(1, ++fixture.Sequence, before.Id, cell, Facing.East));
                fixture.Gateway.Step();
                var current = fixture.Simulation.Capture(1).Economy.Buildings.Single(b => b.Id == fixture.Building.Id);
                moved = current.Center.X.Raw != before.Center.X.Raw || current.Center.Z.Raw != before.Center.Z.Raw;
            }
            Assume.That(moved, Is.True, "a legal relocation site");

            fixture.Gateway.SubmitEconomy(EconomyCommand.RemoveBuilding(1, ++fixture.Sequence, before.Id));
            fixture.Gateway.Step();
            Assert.That(fixture.Simulation.Capture(1).Economy.Wood, Is.EqualTo(fixture.Scenario.Economy.StartWood));
            Assert.That(fixture.Simulation.Capture(1).Economy.Buildings.Any(b => b.Id == before.Id), Is.False);
        }

        private static (ScenarioDefinition Scenario, Battle Simulation, CommandGateway Gateway, BuildingView Building, ulong Sequence) PlaceBarracks(bool easy)
        {
            var scenario = Scenario(easy);
            scenario.Economy.BuildingReservations = false;
            scenario.Economy.StartWood = 10000;
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.Step();
            for (int cell = 0; cell < scenario.Map.WidthCells * scenario.Map.HeightCells; cell++)
            {
                gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Barracks, cell));
                gateway.Step();
                var building = simulation.Capture(1).Economy.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Barracks);
                if (building.Id != 0) return (scenario, simulation, gateway, building, sequence);
            }
            Assert.Fail("a barracks site");
            return default((ScenarioDefinition, Battle, CommandGateway, BuildingView, ulong));
        }

        private static int[] OpenCells(ScenarioDefinition scenario, int count, int spacing)
        {
            var result = new System.Collections.Generic.List<int>();
            int cells = scenario.Map.WidthCells * scenario.Map.HeightCells;
            for (int cell = 0; cell < cells && result.Count < count; cell++)
            {
                int x = cell % scenario.Map.WidthCells, z = cell / scenario.Map.WidthCells;
                if (scenario.Map.BlockedCellIds.Contains(cell)) continue;
                if (scenario.Map.Terrain != null && scenario.Map.Terrain.Length == cells
                    && (scenario.Map.Terrain[cell] == (byte)TerrainKind.River || scenario.Map.Terrain[cell] == (byte)TerrainKind.Mountain)) continue;
                if (scenario.ResourceNodes.Any(n => (int)(n.Position.X.Raw / 65536 / scenario.Map.CellSizeMeters) +
                    (int)(n.Position.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * scenario.Map.WidthCells == cell)) continue;
                if (scenario.Cores.Any(core => Math.Abs((core.Position.X.Raw / 65536 - (x * scenario.Map.CellSizeMeters + 1))) < 8
                    && Math.Abs(core.Position.Z.Raw / 65536 - (z * scenario.Map.CellSizeMeters + 1)) < 8)) continue;
                if (result.All(old => Math.Abs(old % scenario.Map.WidthCells - x) + Math.Abs(old / scenario.Map.WidthCells - z) >= spacing)) result.Add(cell);
            }
            Assert.That(result.Count, Is.EqualTo(count), "open belt cells");
            return result.ToArray();
        }
    }
}
