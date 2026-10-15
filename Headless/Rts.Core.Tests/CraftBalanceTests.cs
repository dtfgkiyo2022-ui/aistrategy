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
    public sealed class CraftBalanceTests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void CraftBalanceOffDoesNotWriteExtensionAndMatchesEveryTick()
        {
            var scenario = LiveGameRules.Create(7351UL, false, true, false, false);
            scenario.Economy.CraftBalance = false;
            scenario.Extensions = Array.Empty<ScenarioExtensionData>();
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Any(x => x.Id == 36), Is.False);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var left = new Battle(scenario);
            var right = new Battle(decoded);
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(Hash(left), Is.EqualTo(Hash(right)), "tick " + tick);
            }
        }

        [Test]
        public void CraftBalanceExtension36RoundTripsAllValues()
        {
            var scenario = LiveGameRules.Create(7352UL, false, true, false, false);
            scenario.Economy.CraftBalance = true;
            scenario.Economy.CraftBalanceMarketSaveTicks = 3333;
            scenario.Economy.CraftBalanceTollgateTiming = 1;
            scenario.Extensions = Array.Empty<ScenarioExtensionData>();
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(x => x.Id == 36);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.CraftBalance, Is.True);
            Assert.That(decoded.Economy.CraftBalanceMarketSaveTicks, Is.EqualTo(3333));
            Assert.That(decoded.Economy.CraftBalanceTollgateTiming, Is.EqualTo(1));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void AutomaticEconomyReservesMarketWoodButBarracksContinueAndTimeoutRestores()
        {
            var scenario = BalanceScenario(7353UL);
            scenario.Economy.StartWood = 180;
            scenario.Economy.CraftBalanceMarketSaveTicks = 10;
            var sim = new Battle(scenario);
            SetEconomy(sim, "Age", 1);
            AddStock(sim, ResourceKind.Tack, 15);
            SetTick(sim, 100);

            Assert.That(InvokeBool(sim, "CraftBalanceShouldDelayWood", 1u), Is.True);
            int woodBefore = EconomyValue<int>(sim, "Wood");
            InvokeVoid(sim, "DecideHouse", 1u);
            Assert.That(EconomyValue<int>(sim, "Wood"), Is.EqualTo(woodBefore));
            InvokeVoid(sim, "DecideBuildings", 1u);
            Assert.That(BuildingCount(sim, BuildingKind.Barracks), Is.GreaterThanOrEqualTo(1));

            SetTick(sim, 111);
            Assert.That(InvokeBool(sim, "CraftBalanceShouldDelayWood", 1u), Is.False);
            Assert.That(EconomyValue<long>(sim, "CraftBalanceMarketSaveStartedTick"), Is.EqualTo(-1));
        }

        [Test]
        public void MarketConstructionClearsCraftBalanceReservation()
        {
            var scenario = BalanceScenario(7354UL);
            scenario.Economy.StartWood = 180;
            var sim = new Battle(scenario);
            SetEconomy(sim, "Age", 1);
            AddStock(sim, ResourceKind.Tack, 15);
            SetTick(sim, 100);
            Assert.That(InvokeBool(sim, "CraftBalanceShouldDelayWood", 1u), Is.True);
            SetEconomy(sim, "Wood", scenario.Economy.MarketWoodCost + scenario.Economy.GoodsMarketWoodReserve);
            InvokeVoid(sim, "DecideGoodsMarket", 1u);
            Assert.That(BuildingCount(sim, BuildingKind.Market), Is.EqualTo(1));
            Assert.That(InvokeBool(sim, "CraftBalanceShouldDelayWood", 1u), Is.False);
        }

        [Test]
        public void TollgateIsOutsideCraftTimingByDefaultAndCanBeEnabled()
        {
            var scenario = BalanceScenario(7355UL);
            scenario.Economy.CraftTiming = true;
            scenario.Economy.Tollgate = true;
            var sim = new Battle(scenario);
            SetEconomy(sim, "Age", 1);
            SetEconomy(sim, "Civ", CivKind.Tollgate);
            Assert.That(InvokeBool(sim, "CraftTimingNeedsWood", 1u), Is.False);
            scenario.Economy.CraftBalanceTollgateTiming = 1;
            var enabled = new Battle(scenario);
            SetEconomy(enabled, "Age", 1);
            SetEconomy(enabled, "Civ", CivKind.Tollgate);
            Assert.That(InvokeBool(enabled, "CraftTimingNeedsWood", 1u), Is.True);
        }

        [Test]
        public void CraftBalanceDoesNotAffectHumanEconomy()
        {
            var scenario = BalanceScenario(7356UL);
            var sim = new Battle(scenario);
            SetEconomy(sim, "Age", 1);
            SetEconomy(sim, "AutoOff", true);
            AddStock(sim, ResourceKind.Tack, 15);
            SetTick(sim, 100);
            Assert.That(InvokeBool(sim, "CraftBalanceShouldDelayWood", 1u), Is.False);
            Assert.That(EconomyValue<long>(sim, "CraftBalanceMarketSaveStartedTick"), Is.EqualTo(0));
        }

        [Test]
        public void CraftBalanceCavalryAndTollgateReplayEveryTick()
        {
            var scenario = LiveGameRules.Create(7357UL, false, true, false, false);
            scenario.Economy.CraftBalance = true;
            scenario.Economy.CraftTiming = true;
            var inputs = new[]
            {
                new ScheduledInput(1, 0, 1, EconomyCommand.Advance(1, 1, CivKind.Cavalry)),
                new ScheduledInput(2, 0, 2, EconomyCommand.Advance(2, 1, CivKind.Tollgate))
            };
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, inputs, 12000, new BuildIdentity());
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }

        private static ScenarioDefinition BalanceScenario(ulong seed)
        {
            var scenario = MapGenerator.GenerateTerrain(seed);
            scenario.Economy.Ages = true;
            scenario.Economy.Industry = true;
            scenario.Economy.ProcessingChain = true;
            scenario.Economy.GoodsMarket = true;
            scenario.Economy.CraftBalance = true;
            scenario.Economy.Tack = true;
            scenario.Economy.CraftBalanceMarketSaveTicks = 6000;
            scenario.Extensions = Array.Empty<ScenarioExtensionData>();
            return scenario;
        }

        private static void AddStock(Battle simulation, ResourceKind kind, int amount)
            => SetEconomy(simulation, kind.ToString(), amount);

        private static int BuildingCount(Battle simulation, BuildingKind kind)
            => simulation.CaptureMatchPackSnapshot().Factions[0].Buildings.Single(x => x.Kind == kind.ToString()).Count;

        private static void SetTick(Battle simulation, long tick)
            => typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation).GetType().GetField("Tick", PrivateInstance).SetValue(typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation), tick);

        private static void SetEconomy(Battle simulation, string name, object value)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var economiesField = world.GetType().GetField("Economies", PrivateInstance);
            var economies = (Array)economiesField.GetValue(world);
            var economy = economies.GetValue(0);
            var field = economy.GetType().GetField(name, PrivateInstance);
            object converted = field.FieldType.IsEnum ? Enum.ToObject(field.FieldType, value) : Convert.ChangeType(value, field.FieldType);
            field.SetValue(economy, converted);
            economies.SetValue(economy, 0);
            economiesField.SetValue(world, economies);
        }

        private static T EconomyValue<T>(Battle simulation, string name)
        {
            var world = typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);
            var economies = (Array)world.GetType().GetField("Economies", PrivateInstance).GetValue(world);
            var economy = economies.GetValue(0);
            return (T)Convert.ChangeType(economy.GetType().GetField(name, PrivateInstance).GetValue(economy), typeof(T));
        }

        private static bool InvokeBool(Battle simulation, string name, params object[] args)
            => (bool)typeof(Battle).GetMethod(name, PrivateInstance).Invoke(simulation, args);

        private static void InvokeVoid(Battle simulation, string name, params object[] args)
            => typeof(Battle).GetMethod(name, PrivateInstance).Invoke(simulation, args);

        private static string Hash(Battle simulation)
            => Convert.ToHexString(ReplayBinary.Hash(simulation.CaptureDiagnostic().CanonicalState));
    }
}
