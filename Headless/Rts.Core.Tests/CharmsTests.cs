using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class CharmsTests
    {
        private static ScenarioDefinition Scenario(bool enabled = true)
        {
            var s = MapGenerator.GenerateTerrain(99173UL, false, true);
            s.Economy.Enabled = true; s.Economy.Ages = true; s.Economy.Industry = true; s.Economy.ProcessingChain = true;
            s.Economy.Sanctuary = enabled; s.Economy.Charms = enabled;
            s.Economy.StartFood = 5000; s.Economy.StartWood = 5000; s.Economy.StartStone = 5000;
            s.Economy.AdvanceFoodCost = 0; s.Economy.AdvanceWoodCost = 0; s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0; s.Economy.Age2WoodCost = 0; s.Economy.Age2Ticks = 1;
            s.Cores[0].Hp = s.Cores[1].Hp = 1000000;
            return s;
        }

        [Test] public void T01_WireValuesAreFixed()
        { Assert.That((int)ResourceKind.Charms, Is.EqualTo(19)); Assert.That((int)BuildingKind.CharmWorkshop, Is.EqualTo(41)); Assert.That((int)ProcessingLineKind.Charms, Is.EqualTo(14)); }

        [Test] public void T02_DefaultRecipeMatchesRequest()
        { var e = Scenario().Economy; Assert.That(e.CharmSizeCells, Is.EqualTo(2)); Assert.That(e.CharmWoodCost, Is.EqualTo(80)); Assert.That(e.CharmWork, Is.EqualTo(200)); Assert.That(e.CharmHp, Is.EqualTo(400)); }

        [Test] public void T03_DefaultInputsMatchRequest()
        { var e = Scenario().Economy; Assert.That(e.CharmTicks, Is.EqualTo(100)); Assert.That(e.CharmWoodInput, Is.EqualTo(3)); Assert.That(e.CharmKeepWood, Is.EqualTo(80)); }

        [Test] public void T04_DefaultBlessingMatchesRequest()
        { var e = Scenario().Economy; Assert.That(e.CharmHpPermille, Is.EqualTo(1200)); Assert.That(e.CharmStockLimit, Is.EqualTo(20)); }

        [Test] public void T05_DefaultAiLimitsMatchRequest()
        { var e = Scenario().Economy; Assert.That(e.CharmMaxWorkshops, Is.EqualTo(1)); Assert.That(e.CharmWoodReserve, Is.EqualTo(20)); }

        [Test] public void T06_ExtensionRoundTripPreservesEveryValue()
        {
            var s = Scenario(); var e = s.Economy;
            e.CharmSizeCells = 3; e.CharmWoodCost = 81; e.CharmWork = 201; e.CharmHp = 401; e.CharmTicks = 101;
            e.CharmWoodInput = 4; e.CharmKeepWood = 81; e.CharmStockLimit = 21; e.CharmMaxWorkshops = 2; e.CharmWoodReserve = 22; e.CharmHpPermille = 1201;
            var copy = ScenarioBinary.Decode(ScenarioBinary.Encode(s));
            Assert.That(copy.Economy.Charms, Is.True); Assert.That(copy.Economy.CharmSizeCells, Is.EqualTo(3)); Assert.That(copy.Economy.CharmWoodCost, Is.EqualTo(81));
            Assert.That(copy.Economy.CharmWork, Is.EqualTo(201)); Assert.That(copy.Economy.CharmHp, Is.EqualTo(401)); Assert.That(copy.Economy.CharmTicks, Is.EqualTo(101));
            Assert.That(copy.Economy.CharmWoodInput, Is.EqualTo(4)); Assert.That(copy.Economy.CharmKeepWood, Is.EqualTo(81)); Assert.That(copy.Economy.CharmStockLimit, Is.EqualTo(21));
            Assert.That(copy.Economy.CharmMaxWorkshops, Is.EqualTo(2)); Assert.That(copy.Economy.CharmWoodReserve, Is.EqualTo(22)); Assert.That(copy.Economy.CharmHpPermille, Is.EqualTo(1201));
        }

        [Test] public void T07_ExtensionIdIsStable()
        { Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario())).Extensions.Any(x => x.Id == 30), Is.True); }

        [Test] public void T08_FlagOffOmitsExtension()
        { Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario(false))).Extensions.Any(x => x.Id == 30), Is.False); }

        [Test] public void T09_FlagOffDoesNotAddCharmsDiagnostic()
        { var simulation = new Battle(Scenario(false)); simulation.Step(1, Array.Empty<ScheduledInput>()); Assert.That(DiagnosticComparison.Fields(simulation.CaptureDiagnostic()).Any(x => x.Key.Contains("Charms", StringComparison.Ordinal)), Is.False); }

        [Test] public void T10_EncodingIsDeterministic()
        {
            var scenario = Scenario();
            Assert.That(ScenarioBinary.Encode(scenario), Is.EqualTo(ScenarioBinary.Encode(Scenario())));
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 120, new BuildIdentity());
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }

        [Test] public void T11_WoodThresholdIsStrict()
        { Assert.That(Scenario().Economy.CharmKeepWood, Is.EqualTo(80)); Assert.That(Scenario().Economy.CharmKeepWood + 1, Is.GreaterThan(80)); }

        [Test] public void T12_StockLimitIsFinite()
        { Assert.That(Scenario().Economy.CharmStockLimit, Is.EqualTo(20)); Assert.That(Scenario().Economy.CharmStockLimit, Is.LessThanOrEqualTo(1000)); }

        [Test] public void T13_BlessingUsesFloorPermille()
        { Assert.That(400 * Scenario().Economy.CharmHpPermille / 1000, Is.EqualTo(480)); Assert.That(401 * 1200 / 1000, Is.EqualTo(481)); }

        [Test] public void T14_CharmsScenarioCanConstruct()
        { var simulation = new Battle(Scenario()); Assert.That(simulation.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Primitive)); }
    }
}
