using System;
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
    [TestFixture]
    public sealed class ByproductsTests
    {
        private sealed class Fixture
        {
            internal readonly ScenarioDefinition Scenario;
            internal readonly Battle Simulation;
            internal readonly CommandGateway Gateway;
            internal ulong Sequence;

            internal Fixture(ScenarioDefinition scenario)
            {
                Scenario = scenario;
                Simulation = new Battle(scenario);
                Gateway = new CommandGateway(Simulation);
                Send(EconomyCommand.Auto(1, ++Sequence, false));
                Send(EconomyCommand.Auto(2, ++Sequence, false));
                Step(1);
            }

            internal void Send(EconomyCommand command) => Gateway.SubmitEconomy(command);

            internal void Step(int count)
            {
                for (int i = 0; i < count && !Simulation.Capture(1).Result.HasEnded; i++) Gateway.Step();
            }
        }

        private static ScenarioDefinition Scenario(bool enabled)
        {
            var s = MapGenerator.GenerateTerrain(9401UL);
            s.Economy.Industry = true;
            s.Economy.ProcessingChain = true;
            s.Economy.Bricks = true;
            s.Economy.Masonry = true;
            s.Economy.Byproducts = enabled;
            return s;
        }

        private static WorldState State(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(simulation);

        private static void Invoke(Battle simulation, string name, params object[] arguments)
            => typeof(Battle).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(simulation, arguments);

        private static ScenarioDefinition AiByproductsScenario()
        {
            var scenario = Scenario(true);
            scenario.Economy.Masonry = false;
            scenario.Economy.CraftTree1 = true;
            scenario.Economy.CivGoodsRequired = false;
            scenario.Economy.AutoVillagerTarget = 0;
            scenario.Economy.AutoInfantryQueue = 0;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000000;
            return scenario;
        }

        private static ProcessingLineState CoreMetalLine(WorldState state)
            => state.ProcessingLines.First(line => line.FactionId == 1 && line.Kind == ProcessingLineKind.CoreMetal && line.Manager == LineManager.Automatic);

        private static void EnableAi(Fixture fixture)
            => fixture.Send(EconomyCommand.Auto(1, ++fixture.Sequence, true));

        private static uint Place(Fixture fixture, BuildingKind kind)
        {
            WorldState state = State(fixture.Simulation);
            int before = state.BuildingCount;
            int cells = fixture.Scenario.Map.WidthCells * fixture.Scenario.Map.HeightCells;
            for (int cell = 0; cell < cells; cell++)
            {
                fixture.Send(EconomyCommand.Place(1, ++fixture.Sequence, kind, cell, Facing.North));
                fixture.Step(1);
                if (state.BuildingCount == before + 1 && state.Buildings[before].Kind == kind) return (uint)(before + 1);
            }
            Assert.Fail("building placement failed: " + kind);
            return 0;
        }

        private static uint PlaceWall(Fixture fixture, bool gate)
        {
            WorldState state = State(fixture.Simulation);
            int before = state.BuildingCount;
            int cells = fixture.Scenario.Map.WidthCells * fixture.Scenario.Map.HeightCells;
            for (int cell = 0; cell < cells; cell++)
            {
                if (gate) fixture.Send(EconomyCommand.PlaceGate(1, ++fixture.Sequence, cell));
                else fixture.Send(EconomyCommand.PlaceWall(1, ++fixture.Sequence, new[] { cell }));
                fixture.Step(1);
                if (gate && state.Buildings.Take(state.BuildingCount).Any(b => b.Kind == BuildingKind.Gate))
                    return (uint)Array.FindIndex(state.Buildings, b => b.Kind == BuildingKind.Gate) + 1u;
                if (state.BuildingCount == before + 1) return (uint)(before + 1);
            }
            Assert.Fail("defence placement failed");
            return 0;
        }

        [Test]
        public void ContractValuesAreAppended()
        {
            Assert.That((byte)ResourceKind.Slag, Is.EqualTo(36));
            Assert.That((byte)BuildingKind.BlastFurnace, Is.EqualTo(53));
        }

        [Test]
        public void Extension94RoundTripsTheEighteenInt32Payload()
        {
            var source = Scenario(true);
            source.Economy.GateSlagCost = 7;
            source.Economy.BlastTicks = 91;
            source.Economy.BlastOreInput = 4;
            source.Economy.BlastCharcoalInput = 2;
            source.Economy.BlastMetalOutput = 3;
            source.Economy.BlastSlagOutput = 2;
            source.Economy.BrickSlagInput = 3;
            source.Economy.WallSlagCost = 4;
            source.Economy.BlastOreInputCapacity = 16;
            source.Economy.BlastCharcoalInputCapacity = 6;
            source.Economy.BlastMetalOutputCapacity = 11;
            source.Economy.BlastSlagOutputCapacity = 12;
            source.Economy.BlastFurnaceSizeCells = 4;
            source.Economy.BlastFurnaceWoodCost = 201;
            source.Economy.BlastFurnaceBrickCost = 11;
            source.Economy.BlastFurnaceWork = 401;
            source.Economy.BlastFurnaceHp = 801;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.Byproducts, Is.True);
            Assert.That(decoded.Economy.GateSlagCost, Is.EqualTo(7));
            Assert.That(decoded.Economy.BlastTicks, Is.EqualTo(91));
            Assert.That(decoded.Economy.BlastOreInput, Is.EqualTo(4));
            Assert.That(decoded.Economy.BlastCharcoalInput, Is.EqualTo(2));
            Assert.That(decoded.Economy.BlastMetalOutput, Is.EqualTo(3));
            Assert.That(decoded.Economy.BlastSlagOutput, Is.EqualTo(2));
            Assert.That(decoded.Economy.BrickSlagInput, Is.EqualTo(3));
            Assert.That(decoded.Economy.WallSlagCost, Is.EqualTo(4));
            Assert.That(decoded.Economy.BlastOreInputCapacity, Is.EqualTo(16));
            Assert.That(decoded.Economy.BlastCharcoalInputCapacity, Is.EqualTo(6));
            Assert.That(decoded.Economy.BlastMetalOutputCapacity, Is.EqualTo(11));
            Assert.That(decoded.Economy.BlastSlagOutputCapacity, Is.EqualTo(12));
            Assert.That(decoded.Economy.BlastFurnaceSizeCells, Is.EqualTo(4));
            Assert.That(decoded.Economy.BlastFurnaceWoodCost, Is.EqualTo(201));
            Assert.That(decoded.Economy.BlastFurnaceBrickCost, Is.EqualTo(11));
            Assert.That(decoded.Economy.BlastFurnaceWork, Is.EqualTo(401));
            Assert.That(decoded.Economy.BlastFurnaceHp, Is.EqualTo(801));
            Assert.That(decoded.Extensions.Any(e => e.Id == 94 && e.Version == 1), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void FlagOffDoesNotAddByproductsExtension()
        {
            var source = Scenario(false);
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            Assert.That(decoded.Economy.Byproducts, Is.False);
            Assert.That(decoded.Extensions.Any(e => e.Id == 94), Is.False);
        }

        [Test]
        public void BlastFurnaceConstructionDeductsWoodAndBricksOnce()
        {
            var fixture = new Fixture(Scenario(true));
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Age = 2;
            state.Economies[0].Wood = 10000;
            state.Economies[0].Bricks = 10000;
            int wood = state.Economies[0].Wood, bricks = state.Economies[0].Bricks;
            uint id = Place(fixture, BuildingKind.BlastFurnace);
            Assert.That(id, Is.GreaterThan(0u));
            Assert.That(state.Economies[0].Wood, Is.EqualTo(wood - fixture.Scenario.Economy.BlastFurnaceWoodCost));
            Assert.That(state.Economies[0].Bricks, Is.EqualTo(bricks - fixture.Scenario.Economy.BlastFurnaceBrickCost));
        }

        [Test]
        public void BlastFurnaceConsumesExactInputsProducesBothOutputsStallsAndRestarts()
        {
            var scenario = Scenario(true);
            scenario.Economy.BlastTicks = 3;
            var fixture = new Fixture(scenario);
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Age = 2;
            state.Economies[0].Wood = 10000;
            state.Economies[0].Bricks = 10000;
            uint id = Place(fixture, BuildingKind.BlastFurnace);
            ref BuildingState furnace = ref state.Buildings[(int)id - 1];
            furnace.Complete = true;
            furnace.Input = scenario.Economy.BlastOreInput;
            furnace.InputSecondary = scenario.Economy.BlastCharcoalInput;
            int beforeMetal = state.Economies[0].BlastMetalProduced;
            int beforeSlag = state.Economies[0].SlagProduced;
            fixture.Step(scenario.Economy.BlastTicks);
            Assert.That(state.Economies[0].BlastMetalProduced, Is.EqualTo(beforeMetal + scenario.Economy.BlastMetalOutput));
            Assert.That(state.Economies[0].SlagProduced, Is.EqualTo(beforeSlag + scenario.Economy.BlastSlagOutput));
            Assert.That(furnace.Input, Is.EqualTo(0));
            Assert.That(furnace.InputSecondary, Is.EqualTo(0));
            Assert.That(furnace.Output, Is.EqualTo(scenario.Economy.BlastMetalOutput));
            Assert.That(furnace.OutputSecondary, Is.EqualTo(scenario.Economy.BlastSlagOutput));
            furnace.OutputSecondary = scenario.Economy.BlastSlagOutputCapacity;
            furnace.Input = scenario.Economy.BlastOreInput;
            furnace.InputSecondary = scenario.Economy.BlastCharcoalInput;
            int stallBefore = state.Economies[0].BlastSlagStallTicks;
            fixture.Step(1);
            Assert.That(state.Economies[0].BlastSlagStallTicks, Is.EqualTo(stallBefore + 1));
            furnace.OutputSecondary = 0;
            fixture.Step(scenario.Economy.BlastTicks);
            Assert.That(state.Economies[0].BlastMetalProduced, Is.EqualTo(beforeMetal + scenario.Economy.BlastMetalOutput * 2));
            Assert.That(state.Economies[0].SlagProduced, Is.EqualTo(beforeSlag + scenario.Economy.BlastSlagOutput * 2));
        }

        [Test]
        public void BlastFurnaceAlternatesMetalAndSlagOnItsOutputBelt()
        {
            var scenario = Scenario(true);
            var fixture = new Fixture(scenario);
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Age = 2;
            state.Economies[0].Wood = 10000;
            state.Economies[0].Bricks = 10000;
            uint id = Place(fixture, BuildingKind.BlastFurnace);
            ref BuildingState furnace = ref state.Buildings[(int)id - 1];
            furnace.Complete = true;
            furnace.Output = 1;
            furnace.OutputSecondary = 1;
            furnace.BlastNextMetal = true;
            int width = scenario.Map.WidthCells;
            int size = scenario.Economy.BlastFurnaceSizeCells;
            int port = furnace.OriginCell + width * size + size / 2;
            state.Belts[port] = new BeltState { FactionId = 1, Facing = Facing.North, Hp = scenario.Economy.BeltHp };
            fixture.Step(1);
            Assert.That(state.Belts[port].Item, Is.EqualTo(ResourceKind.Metal));
            state.Belts[port].Item = 0;
            state.Belts[port].StackCount = 0;
            fixture.Step(1);
            Assert.That(state.Belts[port].Item, Is.EqualTo(ResourceKind.Slag));
        }

        [Test]
        public void BrickKilnConsumesSlagInsteadOfStoneAndCountsIt()
        {
            var scenario = Scenario(true);
            scenario.Economy.BrickTicks = 3;
            var fixture = new Fixture(scenario);
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Civ = CivKind.Masonry;
            state.Economies[0].Age = 2;
            state.Economies[0].Wood = 10000;
            state.Economies[0].Bricks = 10000;
            uint id = Place(fixture, BuildingKind.BrickKiln);
            ref BuildingState kiln = ref state.Buildings[(int)id - 1];
            kiln.Complete = true;
            kiln.Input = scenario.Economy.BrickSlagInput;
            kiln.InputKind = ResourceKind.Slag;
            kiln.InputSecondary = scenario.Economy.BrickCharcoalInput;
            int before = state.Economies[0].BricksFromSlag;
            fixture.Step(scenario.Economy.BrickTicks);
            Assert.That(state.Economies[0].BricksFromSlag, Is.EqualTo(before + 1));
            Assert.That(state.Economies[0].BricksProduced, Is.EqualTo(1));
        }

        [Test]
        public void BroadMapAiBuildsHighFurnaceAndHasAByproductRoute()
        {
            var scenario = LiveGameRules.Create(1UL, true, true, false, false);
            scenario.Economy.BlastTicks = 3;
            scenario.Economy.BrickTicks = 3;
            var simulation = new Battle(scenario);
            State(simulation).Economies[0].Bricks = 100000;
            State(simulation).Economies[0].Planks = 100000;
            for (long tick = 1; tick <= 13000 && !simulation.Capture(1).Result.HasEnded; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());
            int highFurnaces = 0, slagRoutes = 0;
            for (uint faction = 1; faction <= 2; faction++)
            {
                var economy = simulation.Capture(faction).Economy;
                highFurnaces += economy.Buildings.Count(b => b.FactionId == faction && b.Kind == BuildingKind.BlastFurnace && b.Complete);
            }
            var fields = DiagnosticComparison.Fields(simulation.CaptureDiagnostic())
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            slagRoutes = fields.Count(pair => pair.Key.EndsWith(".BrickKilnId", StringComparison.Ordinal) && pair.Value != "0");
            Assert.That(highFurnaces, Is.GreaterThan(0), "seed 1 broad map should place a high furnace");
            Assert.That(slagRoutes, Is.GreaterThan(0), "a deterministic brick route should exist");
        }

        [Test]
        public void AgeOneCharcoalKilnBuildsSmelterMetalLineAndProducesMetal()
        {
            var scenario = AiByproductsScenario();
            var fixture = new Fixture(scenario);
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Civ = CivKind.Metallurgy;
            state.Economies[0].Age = 1;
            state.Economies[0].Wood = 100000;
            state.Economies[0].Bricks = 0;
            uint kiln = Place(fixture, BuildingKind.CharcoalKiln);
            state.Buildings[kiln - 1].Complete = true;
            EnableAi(fixture);
            fixture.Step(6000);
            var line = CoreMetalLine(state);
            Assert.That(line.SmelterId, Is.InRange(1u, uint.MaxValue));
            Assert.That(state.Buildings[line.SmelterId - 1].Kind, Is.EqualTo(BuildingKind.Smelter));
            Assert.That(state.Economies[0].Metal, Is.InRange(1, int.MaxValue));
        }

        [Test]
        public void AgeTwoCharcoalKilnWithoutBricksKeepsSmelterMetalLine()
        {
            var scenario = AiByproductsScenario();
            var fixture = new Fixture(scenario);
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Civ = CivKind.Metallurgy;
            state.Economies[0].Age = 2;
            state.Economies[0].Wood = 100000;
            state.Economies[0].Bricks = 0;
            uint kiln = Place(fixture, BuildingKind.CharcoalKiln);
            state.Buildings[kiln - 1].Complete = true;
            EnableAi(fixture);
            fixture.Step(6000);
            var line = CoreMetalLine(state);
            Assert.That(line.SmelterId, Is.InRange(1u, uint.MaxValue));
            Assert.That(state.Buildings[line.SmelterId - 1].Kind, Is.EqualTo(BuildingKind.Smelter));
            Assert.That(state.Buildings.Count(building => building.FactionId == 1 && building.Kind == BuildingKind.BlastFurnace), Is.EqualTo(0));
        }

        [Test]
        public void AgeTwoCharcoalKilnWithBricksBuildsBlastFurnaceAndProducesMetalAndSlag()
        {
            var scenario = AiByproductsScenario();
            scenario.Economy.BlastTicks = 3;
            var fixture = new Fixture(scenario);
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Civ = CivKind.Metallurgy;
            state.Economies[0].Age = 2;
            state.Economies[0].Wood = 100000;
            state.Economies[0].Bricks = 100000;
            uint kiln = Place(fixture, BuildingKind.CharcoalKiln);
            state.Buildings[kiln - 1].Complete = true;
            EnableAi(fixture);
            fixture.Step(6000);
            var line = CoreMetalLine(state);
            Assert.That(line.SmelterId, Is.InRange(1u, uint.MaxValue));
            Assert.That(state.Buildings[line.SmelterId - 1].Kind, Is.EqualTo(BuildingKind.BlastFurnace));
            Assert.That(state.Economies[0].BlastMetalProduced, Is.InRange(1, int.MaxValue));
            Assert.That(state.Economies[0].SlagProduced, Is.InRange(1, int.MaxValue));
        }

        [Test]
        public void SlagHaulerFeedsBrickKilnAndProducesBrickFromSlag()
        {
            var scenario = Scenario(true);
            scenario.Economy.BlastTicks = 3;
            scenario.Economy.BrickTicks = 3;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 100000000;
            var fixture = new Fixture(scenario);
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Civ = CivKind.Masonry;
            state.Economies[0].Age = 2;
            state.Economies[0].Wood = 100000;
            state.Economies[0].Bricks = 100000;
            uint kiln = Place(fixture, BuildingKind.CharcoalKiln);
            uint brickKiln = Place(fixture, BuildingKind.BrickKiln);
            uint blast = Place(fixture, BuildingKind.BlastFurnace);
            state.Buildings[kiln - 1].Complete = true;
            state.Buildings[brickKiln - 1].Complete = true;
            state.Buildings[blast - 1].Complete = true;
            ref BuildingState furnace = ref state.Buildings[blast - 1];
            ref BuildingState bricks = ref state.Buildings[brickKiln - 1];
            state.Economies[0].Slag = scenario.Economy.BrickSlagInput;
            furnace.BlastNextMetal = false;
            bricks.InputSecondary = scenario.Economy.BrickCharcoalInput;
            Invoke(fixture.Simulation, "SetSimpleHaulers", 1u, blast, brickKiln, 1);
            fixture.Step(2000);
            Assert.That(state.Economies[0].BricksFromSlag, Is.InRange(1, int.MaxValue));
            Assert.That(state.Economies[0].BricksProduced, Is.InRange(1, int.MaxValue));
        }

        [Test]
        public void WallAndGateUseSlagOnlyWhenByproductsAndFortifyAreOn()
        {
            var on = Scenario(true);
            on.Economy.Fortify = true;
            var fixture = new Fixture(on);
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Slag = 100;
            uint wall = PlaceWall(fixture, false);
            uint gate = PlaceWall(fixture, true);
            Assert.That(wall, Is.GreaterThan(0u));
            Assert.That(gate, Is.GreaterThan(0u));
            Assert.That(state.Economies[0].FortifyWallSlag, Is.EqualTo(1));
            Assert.That(state.Economies[0].FortifyGateSlag, Is.EqualTo(1));

            var off = Scenario(false);
            off.Economy.Fortify = true;
            var offFixture = new Fixture(off);
            WorldState offState = State(offFixture.Simulation);
            offState.Economies[0].Slag = 100;
            offState.Economies[0].Stone = 100;
            PlaceWall(offFixture, false);
            Assert.That(offState.Economies[0].Slag, Is.EqualTo(100));
            Assert.That(offState.Economies[0].FortifyWallSlag, Is.EqualTo(0));
        }

        [Test]
        public void FlagOffMatchesLiveRulesHashForThirteenHundredTicks()
        {
            var source = LiveGameRules.Create(9401, false, true, false, false);
            source.Economy.Byproducts = false;
            var roundTrip = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            var left = new Battle(source);
            var right = new Battle(roundTrip);
            for (long tick = 1; tick <= 1300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
            Assert.That(roundTrip.Extensions.Any(e => e.Id == 94), Is.False);
        }
    }
}
