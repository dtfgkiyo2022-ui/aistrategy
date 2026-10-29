using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Rts.Presentation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class CraftFlowTests
    {
        private static ScenarioDefinition Scenario(bool craftFlow)
        {
            var scenario = MapGenerator.GenerateTerrain(7701UL, false, true);
            scenario.Economy.Industry = true;
            scenario.Economy.CraftFlow = craftFlow;
            scenario.Economy.StartFood = 10000;
            scenario.Economy.StartWood = 10000;
            scenario.Economy.VillagerFoodCost = 0;
            scenario.Economy.Ages = true;
            scenario.Economy.ProcessingChain = true;
            return scenario;
        }

        private static Dictionary<string, string> Fields(Battle simulation)
            => DiagnosticComparison.Fields(simulation.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static void AssertStateEqualExceptConfiguration(Battle left, Battle right, int tick)
        {
            var a = Fields(left);
            var b = Fields(right);
            foreach (var pair in a)
            {
                if (pair.Key.StartsWith("Config.", StringComparison.Ordinal)) continue;
                Assert.That(b[pair.Key], Is.EqualTo(pair.Value), pair.Key + " at tick " + tick);
            }
        }

        [Test]
        public void CraftFlowExtension77RoundTripsAndOffOmitsIt()
        {
            var off = Scenario(false);
            var offBytes = ScenarioBinary.Encode(off);
            Assert.That(ScenarioBinary.Decode(offBytes).Extensions.Any(e => e.Id == 77), Is.False);

            var on = Scenario(true);
            var bytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(e => e.Id == 77);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.CraftFlow, Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void CraftFlowOffKeepsEveryTickCanonicalStateAfterEncodeDecode()
        {
            var leftScenario = Scenario(false);
            var left = new Battle(leftScenario);
            var right = new Battle(ScenarioBinary.Decode(ScenarioBinary.Encode(leftScenario)));
            for (long tick = 1; tick <= 30000; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
                if (left.Capture(1).Result.HasEnded) break;
            }
        }

        [Test]
        public void CraftFlowOnIsObservationOnlyForCanonicalSimulationState()
        {
            var left = new Battle(Scenario(false));
            var right = new Battle(Scenario(true));
            for (long tick = 1; tick <= 30000; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                AssertStateEqualExceptConfiguration(left, right, (int)tick);
                if (left.Capture(1).Result.HasEnded) break;
            }
            Assert.That(right.Capture(1).Economy.CraftFlowEnabled, Is.True);
        }

        [Test]
        public void CraftFlowDisplayUsesPreviousMinuteValuesAndOffIsEmpty()
        {
            var inputs = new[] { new CraftFlowInputView(ResourceKind.Wood, 12), new CraftFlowInputView(ResourceKind.Charcoal, 24) };
            var building = new BuildingView(1, 1, BuildingKind.Sawmill, default, 2, 1, 1, true, 0, 0, 0, 0,
                Facing.North, input: 0, output: 0, playerHeld: false, researching: (TechKind)0, researchRemaining: 0,
                inputSecondary: 0, queuedBowGear: 0, caravanMarketId: 0, caravanOutpostId: 0, caravanDistance: default,
                caravanWoodReward: 0, caravanStopReason: CaravanStopReason.None,
                producedPerMinute: 12, inputsPerMinute: inputs, starvedTicksLastMinute: 240, blockedTicksLastMinute: 0, fullRatePerMinute: 15);
            string text = CraftFlowDisplayText.BuildingLines(building);
            Assert.That(text, Does.Contain("12"));
            Assert.That(CraftFlowDisplayText.IsWarning(building), Is.False);
            Assert.That(CraftFlowDisplayText.Production(null), Is.EqualTo(""));
        }
    }
}
