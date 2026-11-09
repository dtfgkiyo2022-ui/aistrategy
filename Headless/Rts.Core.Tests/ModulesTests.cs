using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    [TestFixture]
    public sealed class ModulesTests
    {
        private static WorldState State(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);

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
                Gateway.SubmitEconomy(EconomyCommand.Auto(1, ++Sequence, false));
                Gateway.SubmitEconomy(EconomyCommand.Auto(2, ++Sequence, false));
                Step(1);
            }

            internal void Step(int count)
            {
                for (int i = 0; i < count && !Simulation.Capture(1).Result.HasEnded; i++) Gateway.Step();
            }
        }

        private static ScenarioDefinition ProductionScenario()
        {
            var scenario = MapGenerator.GenerateTerrain(98321UL);
            scenario.Economy.Industry = true;
            scenario.Economy.ProcessingChain = true;
            scenario.Economy.CarriedInputs = true;
            scenario.Economy.Modules = true;
            scenario.Economy.Glassworks = true;
            scenario.Economy.PrecisionTicks = 3;
            scenario.Economy.PrecisionLensInputCapacity = 5;
            scenario.Economy.PrecisionGearsInputCapacity = 10;
            scenario.Economy.PrecisionMetalInputCapacity = 5;
            return scenario;
        }

        private static uint Place(Fixture fixture, BuildingKind kind)
        {
            WorldState state = State(fixture.Simulation);
            int before = state.BuildingCount;
            int cells = fixture.Scenario.Map.WidthCells * fixture.Scenario.Map.HeightCells;
            for (int cell = 0; cell < cells; cell++)
            {
                fixture.Gateway.SubmitEconomy(EconomyCommand.Place(1, ++fixture.Sequence, kind, cell, Facing.North));
                fixture.Step(1);
                if (state.BuildingCount == before + 1 && state.Buildings[before].Kind == kind) return (uint)(before + 1);
            }
            Assert.Fail("building placement failed: " + kind);
            return 0;
        }

        [Test]
        public void ContractValuesAreAppended()
        {
            Assert.That((byte)ResourceKind.Precision, Is.EqualTo(40));
            Assert.That((byte)RecipeKind.Precision, Is.EqualTo(11));
            Assert.That((byte)EconomyCommandKind.InstallModule, Is.EqualTo(33));
        }

        [Test]
        public void Extension97RoundTripsTheExactSixteenInt32Payload()
        {
            var source = ProductionScenario();
            var e = source.Economy;
            e.Modules = true;
            e.PrecisionLensInput = 1; e.PrecisionGearsInput = 2; e.PrecisionMetalInput = 3; e.PrecisionTicks = 4;
            e.PrecisionLensInputCapacity = 5; e.PrecisionGearsInputCapacity = 6; e.PrecisionMetalInputCapacity = 7;
            e.ModuleSlots = 8; e.ModuleSpeedPermille = 9; e.ModuleMinAge = 10; e.CannonPrecisionCost = 11;
            e.CannonPrecisionMinAge = 12; e.PrecisionMaxLines = 13; e.AiModulesPerDecision = 14; e.AiPrecisionReserve = 15;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.Modules, Is.True);
            Assert.That(new[] { decoded.Economy.PrecisionLensInput, decoded.Economy.PrecisionGearsInput, decoded.Economy.PrecisionMetalInput,
                decoded.Economy.PrecisionTicks, decoded.Economy.PrecisionLensInputCapacity, decoded.Economy.PrecisionGearsInputCapacity,
                decoded.Economy.PrecisionMetalInputCapacity, decoded.Economy.ModuleSlots, decoded.Economy.ModuleSpeedPermille,
                decoded.Economy.ModuleMinAge, decoded.Economy.CannonPrecisionCost, decoded.Economy.CannonPrecisionMinAge,
                decoded.Economy.PrecisionMaxLines, decoded.Economy.AiModulesPerDecision, decoded.Economy.AiPrecisionReserve },
                Is.EqualTo(Enumerable.Range(1, 15).ToArray()));
            Assert.That(decoded.Extensions.Any(x => x.Id == 97 && x.Version == 1 && x.Data.Length == 16 * sizeof(int)), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void PrecisionConsumesLensGearsAndMetalThroughInputSlots()
        {
            var fixture = new Fixture(ProductionScenario());
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Age = 1;
            state.Economies[0].Wood = 10000;
            uint id = Place(fixture, BuildingKind.Assembler);
            ref BuildingState assembler = ref state.Buildings[(int)id - 1];
            assembler.Complete = true;
            assembler.Recipe = RecipeKind.Precision;
            assembler.Input = fixture.Scenario.Economy.PrecisionLensInput;
            assembler.InputSecondary = fixture.Scenario.Economy.PrecisionGearsInput;
            assembler.InputTertiary = fixture.Scenario.Economy.PrecisionMetalInput;
            int before = state.Economies[0].PrecisionProduced;
            fixture.Step(fixture.Scenario.Economy.PrecisionTicks + 1);
            Assert.That(state.Economies[0].PrecisionProduced, Is.EqualTo(before + 1));
            Assert.That(assembler.Input, Is.EqualTo(0));
            Assert.That(assembler.InputSecondary, Is.EqualTo(0));
            Assert.That(assembler.InputTertiary, Is.EqualTo(0));
        }

        [Test]
        public void InstallModuleConsumesOnePrecisionAndIncrementsBuildingOnly()
        {
            var fixture = new Fixture(ProductionScenario());
            WorldState state = State(fixture.Simulation);
            state.Economies[0].Age = 1;
            state.Economies[0].Wood = 10000;
            state.Economies[0].Precision = 1;
            uint id = Place(fixture, BuildingKind.Assembler);
            ref BuildingState assembler = ref state.Buildings[(int)id - 1];
            assembler.Complete = true;
            fixture.Gateway.SubmitEconomy(EconomyCommand.InstallModule(1, ++fixture.Sequence, id));
            fixture.Step(1);
            Assert.That(assembler.ModuleCount, Is.EqualTo(1));
            Assert.That(state.Economies[0].Precision, Is.EqualTo(0));
            Assert.That(state.Economies[0].ModulesInstalled, Is.EqualTo(1));
        }

        [Test]
        public void ModuleCraftTimeUsesCeilingAndFlagOffKeepsOriginalTime()
        {
            var scenario = ProductionScenario();
            var simulation = new Battle(scenario);
            WorldState state = State(simulation);
            var method = typeof(Battle).GetMethod("CraftTicksFor", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var building = new BuildingState { Kind = BuildingKind.Assembler, FactionId = 1, Level = 1, ModuleCount = 1 };
            Assert.That((int)method.Invoke(simulation, new object[] { building, 150 }), Is.EqualTo(120));
            scenario.Economy.Modules = false;
            var off = new Battle(scenario);
            Assert.That((int)method.Invoke(off, new object[] { building, 150 }), Is.EqualTo(150));
            Assert.That(state.Economies[0].Precision, Is.EqualTo(0));
        }

        [Test]
        public void ModuleSpeedDoesNotChangeAmounts()
        {
            var scenario = ProductionScenario();
            var simulation = new Battle(scenario);
            var method = typeof(Battle).GetMethod("ApplyModuleSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var building = new BuildingState { Kind = BuildingKind.Drill, FactionId = 1, ModuleCount = 1 };
            Assert.That((int)method.Invoke(simulation, new object[] { building, 40 }), Is.EqualTo(32));
        }
    }
}
