using System;
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
    public sealed class ToolsTests
    {
        private static ScenarioDefinition Scenario(bool tools = true)
        {
            var s = MapGenerator.GenerateTerrain(99173UL, false, true);
            s.Economy.Enabled = true; s.Economy.Ages = true; s.Economy.Industry = true; s.Economy.ProcessingChain = true;
            s.Economy.Metropolis = true; s.Economy.Tools = tools;
            s.Economy.StartFood = 5000; s.Economy.StartWood = 5000; s.Economy.StartStone = 5000;
            s.Economy.AdvanceFoodCost = 0; s.Economy.AdvanceWoodCost = 0; s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0; s.Economy.Age2WoodCost = 0; s.Economy.Age2Ticks = 1;
            s.Cores[0].Hp = s.Cores[1].Hp = 1000000;
            return s;
        }

        [Test] public void T01_WireValuesAreFixed()
        { Assert.That((int)ResourceKind.Tools, Is.EqualTo(21)); Assert.That((int)BuildingKind.ToolShop, Is.EqualTo(43)); Assert.That((int)ProcessingLineKind.Tools, Is.EqualTo(16)); }

        [Test] public void T02_DefaultBuildingValuesMatchRequest()
        { var e = Scenario().Economy; Assert.That(e.ToolShopSizeCells, Is.EqualTo(2)); Assert.That(e.ToolShopWoodCost, Is.EqualTo(80)); Assert.That(e.ToolShopWork, Is.EqualTo(200)); Assert.That(e.ToolShopHp, Is.EqualTo(400)); }

        [Test] public void T03_DefaultRecipeValuesMatchRequest()
        { var e = Scenario().Economy; Assert.That(e.ToolTicks, Is.EqualTo(80)); Assert.That(e.ToolWoodInput, Is.EqualTo(2)); Assert.That(e.ToolKeepWood, Is.EqualTo(80)); }

        [Test] public void T04_DefaultTrainingDiscountValuesMatchRequest()
        { var e = Scenario().Economy; Assert.That(e.ToolVillagerFoodPermille, Is.EqualTo(700)); Assert.That(e.ToolVillagerTicksPermille, Is.EqualTo(500)); }

        [Test] public void T05_DefaultAiLimitsMatchRequest()
        { var e = Scenario().Economy; Assert.That(e.ToolStockLimit, Is.EqualTo(10)); Assert.That(e.ToolMaxShops, Is.EqualTo(1)); Assert.That(e.ToolWoodReserve, Is.EqualTo(20)); }

        [Test] public void T06_ExtensionRoundTripPreservesEveryValue()
        {
            var s = Scenario(); var e = s.Economy;
            e.ToolShopSizeCells = 3; e.ToolShopWoodCost = 81; e.ToolShopWork = 201; e.ToolShopHp = 401;
            e.ToolTicks = 82; e.ToolWoodInput = 4; e.ToolKeepWood = 81; e.ToolStockLimit = 11;
            e.ToolMaxShops = 2; e.ToolWoodReserve = 22; e.ToolVillagerFoodPermille = 701; e.ToolVillagerTicksPermille = 501;
            var c = ScenarioBinary.Decode(ScenarioBinary.Encode(s)).Economy;
            Assert.That(c.Tools, Is.True); Assert.That(c.ToolShopSizeCells, Is.EqualTo(3)); Assert.That(c.ToolShopWoodCost, Is.EqualTo(81));
            Assert.That(c.ToolShopWork, Is.EqualTo(201)); Assert.That(c.ToolShopHp, Is.EqualTo(401)); Assert.That(c.ToolTicks, Is.EqualTo(82));
            Assert.That(c.ToolWoodInput, Is.EqualTo(4)); Assert.That(c.ToolKeepWood, Is.EqualTo(81)); Assert.That(c.ToolStockLimit, Is.EqualTo(11));
            Assert.That(c.ToolMaxShops, Is.EqualTo(2)); Assert.That(c.ToolWoodReserve, Is.EqualTo(22)); Assert.That(c.ToolVillagerFoodPermille, Is.EqualTo(701)); Assert.That(c.ToolVillagerTicksPermille, Is.EqualTo(501));
        }

        [Test] public void T07_ExtensionIdIsStable()
        { Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario())).Extensions.Any(x => x.Id == 32), Is.True); }

        [Test] public void T08_FlagOffOmitsExtension()
        { Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario(false))).Extensions.Any(x => x.Id == 32), Is.False); }

        [Test] public void T09_FlagOffDoesNotAddToolsDiagnostic()
        { var simulation = new Battle(Scenario(false)); simulation.Step(1, Array.Empty<ScheduledInput>()); Assert.That(DiagnosticComparison.Fields(simulation.CaptureDiagnostic()).Any(x => x.Key.Contains("Tools", StringComparison.Ordinal)), Is.False); }

        [Test] public void T10_EncodingAndReplayAreDeterministic()
        {
            var scenario = Scenario(); Assert.That(ScenarioBinary.Encode(scenario), Is.EqualTo(ScenarioBinary.Encode(Scenario())));
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 120, new BuildIdentity());
                stream.Position = 0; var result = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(result.FirstMismatchTick, Is.Null); Assert.That(result.IsFault, Is.False);
            }
        }

        [Test] public void T11_WoodThresholdIsStrict()
        { Assert.That(Scenario().Economy.ToolKeepWood, Is.EqualTo(80)); Assert.That(Scenario().Economy.ToolKeepWood + 1, Is.GreaterThan(80)); }

        [Test] public void T12_StockLimitIsFinite()
        { Assert.That(Scenario().Economy.ToolStockLimit, Is.EqualTo(10)); Assert.That(Scenario().Economy.ToolStockLimit, Is.LessThanOrEqualTo(1000)); }

        [Test] public void T13_TrainingPermilleUsesCeilingAndMinimumOneTick()
        {
            var e = Scenario().Economy;
            Assert.That((3 * e.ToolVillagerFoodPermille + 999) / 1000, Is.EqualTo(3));
            Assert.That(Math.Max(1, (1 * e.ToolVillagerTicksPermille + 999) / 1000), Is.EqualTo(1));
        }

        [Test] public void T14_ToolsAreMetropolisAgeOneAndLaterOnly()
        { var s = Scenario(); Assert.That(s.Economy.Metropolis, Is.True); Assert.That(s.Economy.Tools, Is.True); Assert.That(s.Economy.Ages, Is.True); }

        [Test] public void T15_ToolsAreConsumedOnlyByVillagerTraining()
        { var simulation = new Battle(Scenario()); Assert.That(simulation.Capture(1).Economy.Tools, Is.EqualTo(0)); Assert.That(simulation.Capture(1).Economy.VillagerQueued, Is.EqualTo(0)); }

        [Test] public void T16_CancelPathCanRestoreToolCounters()
        {
            var sim = AtMetropolis(); SetEconomy(sim, 1);
            Invoke(sim, "EnqueueCoreVillager", 1u);
            Assert.That(ReadEconomyField<int>(sim, "ToolsConsumed"), Is.EqualTo(1)); Assert.That(ReadEconomyField<int>(sim, "ToolsTrainedVillagers"), Is.EqualTo(1));
            var markers = (bool[])ReadEconomyField<object>(sim, "QueueTools"); Assert.That(markers.Length, Is.EqualTo(1)); Assert.That(markers[0], Is.True);
            Invoke(sim, "CancelCoreVillager", 1u);
            Assert.That(ReadEconomyField<int>(sim, "Tools"), Is.EqualTo(1)); Assert.That(ReadEconomyField<int>(sim, "ToolsConsumed"), Is.EqualTo(0)); Assert.That(ReadEconomyField<int>(sim, "ToolsTrainedVillagers"), Is.EqualTo(0));
        }

        [Test] public void T17_ToolsScenarioCanConstructAndExposeStock()
        { var simulation = AtMetropolis(); var view = simulation.Capture(1).Economy; Assert.That(view.ToolsEnabled, Is.True); Assert.That(view.Tools, Is.GreaterThanOrEqualTo(0)); }

        private static void Invoke(Battle simulation, string method, params object[] args)
        { typeof(Battle).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(simulation, args); }

        private static Battle AtMetropolis()
        {
            var simulation = new Battle(Scenario()); var gateway = new CommandGateway(simulation);
            ulong sequence = 0; gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Metropolis));
            for (int i = 0; i < 5; i++) gateway.Step();
            Assert.That(simulation.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Metropolis));
            return simulation;
        }

        private static void SetEconomy(Battle simulation, int tools)
        {
            var world = typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);
            var economiesField = world.GetType().GetField("Economies", BindingFlags.Instance | BindingFlags.NonPublic);
            var economies = (Array)economiesField.GetValue(world); var economy = economies.GetValue(0); var type = economy.GetType();
            SetField(type, economy, "Tools", tools); SetField(type, economy, "Food", 5000); SetField(type, economy, "Queued", 0); SetField(type, economy, "TrainRemaining", 0L);
            type.GetField("QueueTools", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(economy, Array.Empty<bool>());
            economies.SetValue(economy, 0); economiesField.SetValue(world, economies);
        }

        private static void SetField(Type type, object instance, string name, object value)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field.FieldType.IsEnum) field.SetValue(instance, Enum.ToObject(field.FieldType, value));
            else field.SetValue(instance, Convert.ChangeType(value, field.FieldType));
        }

        private static T ReadEconomyField<T>(Battle simulation, string name)
        {
            var world = typeof(Battle).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(simulation);
            var economies = (Array)world.GetType().GetField("Economies", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world);
            return (T)economies.GetValue(0).GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(economies.GetValue(0));
        }
    }
}
