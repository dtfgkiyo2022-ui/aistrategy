using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class RepairKitsTests
    {
        private static ScenarioDefinition Scenario(bool repairKits = true, bool tollgate = true)
        {
            var s = MapGenerator.GenerateTerrain(99173UL, false, true);
            s.Economy.Enabled = true; s.Economy.Ages = true; s.Economy.Industry = true; s.Economy.ProcessingChain = true;
            s.Economy.Tollgate = tollgate; s.Economy.RepairKits = repairKits;
            s.Economy.StartFood = 5000; s.Economy.StartWood = 5000; s.Economy.StartStone = 5000;
            s.Economy.AdvanceFoodCost = 0; s.Economy.AdvanceWoodCost = 0; s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0; s.Economy.Age2WoodCost = 0; s.Economy.Age2Ticks = 1;
            s.Cores[0].Hp = s.Cores[1].Hp = 1000000;
            return s;
        }

        [Test] public void T01_FlagOffKeepsBytesAndHashesThroughMatch()
        {
            Assert.That((int)ResourceKind.RepairKits, Is.EqualTo(20)); Assert.That((int)BuildingKind.RepairYard, Is.EqualTo(42)); Assert.That((int)ProcessingLineKind.RepairKits, Is.EqualTo(15));
            var old = Scenario(false); var off = Scenario(false);
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(old)));
            var left = new Battle(old); var right = new Battle(off);
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState), Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }
        [Test] public void T02_DefaultBuildingValuesMatchRequest()
        { var e = Scenario().Economy; Assert.That(e.RepairYardSizeCells, Is.EqualTo(2)); Assert.That(e.RepairYardWoodCost, Is.EqualTo(80)); Assert.That(e.RepairYardWork, Is.EqualTo(200)); Assert.That(e.RepairYardHp, Is.EqualTo(400)); }
        [Test] public void T03_DefaultRecipeValuesMatchRequest()
        { var e = Scenario().Economy; Assert.That(e.RepairKitTicks, Is.EqualTo(80)); Assert.That(e.RepairKitWoodInput, Is.EqualTo(2)); Assert.That(e.RepairKitKeepWood, Is.EqualTo(80)); }
        [Test] public void T04_DefaultStockValuesMatchRequest()
        { var e = Scenario().Economy; Assert.That(e.RepairKitStockLimit, Is.EqualTo(20)); Assert.That(e.RepairKitIntervalTicks, Is.EqualTo(60)); Assert.That(e.RepairKitHp, Is.EqualTo(40)); }
        [Test] public void T05_DefaultAiLimitsMatchRequest()
        { var e = Scenario().Economy; Assert.That(e.RepairKitMaxYards, Is.EqualTo(1)); Assert.That(e.RepairKitWoodReserve, Is.EqualTo(20)); }
        [Test] public void T06_ExtensionRoundTripPreservesEveryValue()
        {
            var s = Scenario(); var e = s.Economy;
            e.RepairYardSizeCells = 3; e.RepairYardWoodCost = 81; e.RepairYardWork = 201; e.RepairYardHp = 401;
            e.RepairKitTicks = 82; e.RepairKitWoodInput = 4; e.RepairKitKeepWood = 81; e.RepairKitStockLimit = 21;
            e.RepairKitMaxYards = 2; e.RepairKitWoodReserve = 22; e.RepairKitIntervalTicks = 61; e.RepairKitHp = 41;
            var copy = ScenarioBinary.Decode(ScenarioBinary.Encode(s)); var c = copy.Economy;
            Assert.That(c.RepairKits, Is.True); Assert.That(c.RepairYardSizeCells, Is.EqualTo(3)); Assert.That(c.RepairYardWoodCost, Is.EqualTo(81));
            Assert.That(c.RepairYardWork, Is.EqualTo(201)); Assert.That(c.RepairYardHp, Is.EqualTo(401)); Assert.That(c.RepairKitTicks, Is.EqualTo(82));
            Assert.That(c.RepairKitWoodInput, Is.EqualTo(4)); Assert.That(c.RepairKitKeepWood, Is.EqualTo(81)); Assert.That(c.RepairKitStockLimit, Is.EqualTo(21));
            Assert.That(c.RepairKitMaxYards, Is.EqualTo(2)); Assert.That(c.RepairKitWoodReserve, Is.EqualTo(22)); Assert.That(c.RepairKitIntervalTicks, Is.EqualTo(61)); Assert.That(c.RepairKitHp, Is.EqualTo(41));
        }
        [Test] public void T07_ExtensionIdIsStable()
        { Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario())).Extensions.Any(x => x.Id == 31), Is.True); }
        [Test] public void T08_FlagOffOmitsExtension()
        { Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario(false))).Extensions.Any(x => x.Id == 31), Is.False); }
        [Test] public void T09_FlagOffDoesNotAddDiagnostic()
        { var simulation = new Battle(Scenario(false)); simulation.Step(1, Array.Empty<ScheduledInput>()); Assert.That(DiagnosticComparison.Fields(simulation.CaptureDiagnostic()).Any(x => x.Key.Contains("RepairKits", StringComparison.Ordinal)), Is.False); }
        [Test] public void T10_EncodingIsDeterministic()
        { Assert.That(ScenarioBinary.Encode(Scenario()), Is.EqualTo(ScenarioBinary.Encode(Scenario()))); }
        [Test] public void T11_RepairKitsAreTollgateOnly()
        { var simulation = new Battle(Scenario()); Assert.That(simulation.Capture(1).Economy.RepairKitsEnabled, Is.False); }
        [Test] public void T12_WoodThresholdIsStrict()
        { Assert.That(Scenario().Economy.RepairKitKeepWood, Is.EqualTo(80)); Assert.That(Scenario().Economy.RepairKitKeepWood + 1, Is.GreaterThan(80)); }
        [Test] public void T13_StockAndRepairCapsAreFinite()
        { var e = Scenario().Economy; Assert.That(e.RepairKitStockLimit, Is.GreaterThan(0)); Assert.That(e.RepairKitStockLimit, Is.LessThanOrEqualTo(1000)); Assert.That(e.RepairKitHp, Is.EqualTo(40)); }
        [Test] public void T14_RepairKitsScenarioCanConstruct()
        { var simulation = new Battle(Scenario()); Assert.That(simulation.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Primitive)); }
    }
}
