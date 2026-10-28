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
    public sealed class HouseFlowTests
    {
        [Test]
        public void HouseFlowExtension74RoundTripsItsFields()
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            scenario.Economy.HouseFlowStallTicks = 601;
            scenario.Economy.HouseFlowMarginPerProducer = 9;
            scenario.Economy.HouseFlowMaxConcurrent = 4;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.HouseFlow, Is.True);
            Assert.That((decoded.Economy.HouseFlowStallTicks, decoded.Economy.HouseFlowMarginPerProducer,
                decoded.Economy.HouseFlowMaxConcurrent), Is.EqualTo((601, 9, 4)));
            Assert.That(decoded.Extensions.Count(extension => extension.Id == 74), Is.EqualTo(1));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void LiveRulesEnableHouseFlowOnlyOnLargeMaps()
        {
            Assert.That(LiveGameRules.Create(2, false, true, false, false).Economy.HouseFlow, Is.False);
            Assert.That(LiveGameRules.Create(2, true, true, false, false).Economy.HouseFlow, Is.True);
        }

        [Test, Explicit("30,000 tick determinism measurement")]
        public void HouseFlowOffKeepsPerTickCanonicalHashes()
        {
            var leftScenario = LiveGameRules.Create(2, true, false, false, false);
            var rightScenario = LiveGameRules.Create(2, true, false, false, false);
            leftScenario.Economy.HouseFlow = false;
            rightScenario.Economy.HouseFlow = false;
            var left = new Battle(leftScenario);
            var right = new Battle(rightScenario);
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void HouseFlowRecordsHousingObservationAndRunsPastStallTimer()
        {
            var simulation = new Battle(LiveGameRules.Create(3, true, false, false, false));
            for (long tick = 1; tick <= 14000 && !simulation.Capture(1).Result.HasEnded; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());
            var observation = simulation.CaptureLargeEconomyObservation();
            Assert.That(observation.Factions.All(f => f.PopulationLimit > 0), Is.True);
            Assert.That(observation.Factions.All(f => f.Houses >= 0 && f.PlannedHouses >= 0), Is.True);
            Assert.That(observation.Factions.All(f => f.HouseLimitStopTicks >= 0), Is.True);
            Assert.That(simulation.Capture(1).Result.IsFault, Is.False);
        }

        [Test]
        public void HouseFlowRecordingAndPlaybackMatch()
        {
            var scenario = LiveGameRules.Create(4, true, false, false, false);
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 1200, new BuildIdentity());
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, new BuildIdentity(), allowBuildMismatch: true);
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }
    }
}
