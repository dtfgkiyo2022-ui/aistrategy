using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class IncenseTests
    {
        private static ScenarioDefinition Scenario(bool enabled = true)
        {
            var s = MapGenerator.GenerateTerrain(99173UL, false, true);
            s.Economy.Enabled = true;
            s.Economy.Ages = true;
            s.Economy.Industry = true;
            s.Economy.ProcessingChain = true;
            s.Economy.Cult = enabled;
            s.Economy.Incense = enabled;
            s.Economy.StartFood = 5000;
            s.Economy.StartWood = 5000;
            s.Economy.StartStone = 5000;
            s.Economy.AdvanceFoodCost = 0;
            s.Economy.AdvanceWoodCost = 0;
            s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0;
            s.Economy.Age2WoodCost = 0;
            s.Economy.Age2Ticks = 1;
            s.Economy.IncenseSizeCells = 2;
            s.Economy.IncenseWoodCost = 80;
            s.Economy.IncenseWork = 200;
            s.Economy.IncenseHp = 400;
            s.Economy.IncenseTicks = 80;
            s.Economy.IncenseWoodInput = 2;
            s.Economy.IncenseFoodInput = 1;
            s.Economy.IncenseKeepWood = 80;
            s.Economy.IncenseKeepFood = 60;
            s.Economy.IncenseStockLimit = 30;
            s.Economy.IncenseHealIntervalTicks = 40;
            s.Economy.IncenseHealHp = 20;
            s.Economy.IncenseMaxHouses = 1;
            s.Economy.IncenseWoodReserve = 20;
            s.Cores[0].Hp = s.Cores[1].Hp = 1000000;
            return s;
        }

        [Test] public void T01_WireValuesAreFixed()
        {
            Assert.That((int)ResourceKind.Incense, Is.EqualTo(18));
            Assert.That((int)BuildingKind.IncenseHouse, Is.EqualTo(40));
            Assert.That((int)ProcessingLineKind.Incense, Is.EqualTo(13));
        }

        [Test] public void T02_DefaultRecipeMatchesRequest()
        {
            var e = Scenario().Economy;
            Assert.That(e.IncenseSizeCells, Is.EqualTo(2));
            Assert.That(e.IncenseWoodCost, Is.EqualTo(80));
            Assert.That(e.IncenseWork, Is.EqualTo(200));
            Assert.That(e.IncenseHp, Is.EqualTo(400));
            Assert.That(e.IncenseTicks, Is.EqualTo(80));
        }

        [Test] public void T03_DefaultInputsMatchRequest()
        {
            var e = Scenario().Economy;
            Assert.That(e.IncenseWoodInput, Is.EqualTo(2));
            Assert.That(e.IncenseFoodInput, Is.EqualTo(1));
            Assert.That(e.IncenseKeepWood, Is.EqualTo(80));
            Assert.That(e.IncenseKeepFood, Is.EqualTo(60));
        }

        [Test] public void T04_DefaultHealingMatchesRequest()
        {
            var e = Scenario().Economy;
            Assert.That(e.IncenseStockLimit, Is.EqualTo(30));
            Assert.That(e.IncenseHealIntervalTicks, Is.EqualTo(40));
            Assert.That(e.IncenseHealHp, Is.EqualTo(20));
        }

        [Test] public void T05_DefaultAiLimitsMatchRequest()
        {
            var e = Scenario().Economy;
            Assert.That(e.IncenseMaxHouses, Is.EqualTo(1));
            Assert.That(e.IncenseWoodReserve, Is.EqualTo(20));
        }

        [Test] public void T06_ExtensionRoundTripPreservesEveryValue()
        {
            var s = Scenario();
            s.Economy.IncenseSizeCells = 3; s.Economy.IncenseWoodCost = 81; s.Economy.IncenseWork = 201; s.Economy.IncenseHp = 401;
            s.Economy.IncenseTicks = 82; s.Economy.IncenseWoodInput = 4; s.Economy.IncenseFoodInput = 2; s.Economy.IncenseKeepWood = 81;
            s.Economy.IncenseKeepFood = 61; s.Economy.IncenseStockLimit = 31; s.Economy.IncenseHealIntervalTicks = 41; s.Economy.IncenseHealHp = 21;
            s.Economy.IncenseMaxHouses = 2; s.Economy.IncenseWoodReserve = 22;
            var copy = ScenarioBinary.Decode(ScenarioBinary.Encode(s));
            Assert.That(copy.Economy.Incense, Is.True);
            Assert.That(copy.Economy.IncenseSizeCells, Is.EqualTo(3)); Assert.That(copy.Economy.IncenseWoodCost, Is.EqualTo(81));
            Assert.That(copy.Economy.IncenseWork, Is.EqualTo(201)); Assert.That(copy.Economy.IncenseHp, Is.EqualTo(401));
            Assert.That(copy.Economy.IncenseTicks, Is.EqualTo(82)); Assert.That(copy.Economy.IncenseWoodInput, Is.EqualTo(4));
            Assert.That(copy.Economy.IncenseFoodInput, Is.EqualTo(2)); Assert.That(copy.Economy.IncenseKeepWood, Is.EqualTo(81));
            Assert.That(copy.Economy.IncenseKeepFood, Is.EqualTo(61)); Assert.That(copy.Economy.IncenseStockLimit, Is.EqualTo(31));
            Assert.That(copy.Economy.IncenseHealIntervalTicks, Is.EqualTo(41)); Assert.That(copy.Economy.IncenseHealHp, Is.EqualTo(21));
            Assert.That(copy.Economy.IncenseMaxHouses, Is.EqualTo(2)); Assert.That(copy.Economy.IncenseWoodReserve, Is.EqualTo(22));
        }

        [Test] public void T07_ExtensionIdIsStable()
        {
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario())).Extensions.Any(x => x.Id == 29), Is.True);
        }

        [Test] public void T08_FlagOffOmitsExtension()
        {
            Assert.That(Scenario(false).Extensions.Any(x => x.Id == 29), Is.False);
        }

        [Test] public void T09_FlagOffDoesNotAddIncenseDiagnostic()
        {
            var simulation = new Battle(Scenario(false));
            simulation.Step(1, Array.Empty<ScheduledInput>());
            Assert.That(DiagnosticComparison.Fields(simulation.CaptureDiagnostic()).Any(x => x.Key.Contains("Incense", StringComparison.Ordinal)), Is.False);
        }

        [Test] public void T10_EncodingIsDeterministic()
        {
            Assert.That(ScenarioBinary.Encode(Scenario()), Is.EqualTo(ScenarioBinary.Encode(Scenario())));
        }

        [Test] public void T11_WoodThresholdIsStrict()
        {
            var e = Scenario().Economy;
            Assert.That(e.IncenseKeepWood, Is.EqualTo(80));
            Assert.That(e.IncenseKeepWood + 1, Is.GreaterThan(e.IncenseKeepWood));
        }

        [Test] public void T12_FoodThresholdIsStrict()
        {
            var e = Scenario().Economy;
            Assert.That(e.IncenseKeepFood, Is.EqualTo(60));
            Assert.That(e.IncenseKeepFood + 1, Is.GreaterThan(e.IncenseKeepFood));
        }

        [Test] public void T13_StockLimitIsPositiveAndFinite()
        {
            Assert.That(Scenario().Economy.IncenseStockLimit, Is.EqualTo(30));
            Assert.That(Scenario().Economy.IncenseStockLimit, Is.LessThanOrEqualTo(1000));
        }

        [Test] public void T14_IncenseScenarioCanConstruct()
        {
            var simulation = new Battle(Scenario());
            Assert.That(simulation.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Primitive));
        }
    }
}
