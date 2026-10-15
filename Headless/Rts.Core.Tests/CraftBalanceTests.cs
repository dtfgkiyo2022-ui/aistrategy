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
        public void CraftBalanceExtension36RoundTripsTollgateValue()
        {
            var scenario = LiveGameRules.Create(7352UL, false, true, false, false);
            scenario.Economy.CraftBalance = true;
            scenario.Economy.CraftBalanceTollgateTiming = 1;
            scenario.Extensions = Array.Empty<ScenarioExtensionData>();
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(x => x.Id == 36);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(extension.Data.Length, Is.EqualTo(sizeof(int)));
            Assert.That(decoded.Economy.CraftBalance, Is.True);
            Assert.That(decoded.Economy.CraftBalanceTollgateTiming, Is.EqualTo(1));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
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
            scenario.Extensions = Array.Empty<ScenarioExtensionData>();
            return scenario;
        }

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

        private static bool InvokeBool(Battle simulation, string name, params object[] args)
            => (bool)typeof(Battle).GetMethod(name, PrivateInstance).Invoke(simulation, args);

        private static string Hash(Battle simulation)
            => Convert.ToHexString(ReplayBinary.Hash(simulation.CaptureDiagnostic().CanonicalState));
    }
}
