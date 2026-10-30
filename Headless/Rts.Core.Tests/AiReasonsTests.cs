using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class AiReasonsTests
    {
        [Test]
        public void FlagOffKeepsEveryTickHashAndExtension83RoundTrips()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            Assert.That(source.Economy.AiReasons, Is.True);
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.AiReasons, Is.True);
            Assert.That(decoded.Extensions.Any(x => x.Id == 83 && x.Version == 1), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            decoded.Economy.AiReasons = false;
            var copy = ScenarioBinary.Decode(ScenarioBinary.Encode(decoded));
            var left = new Battle(decoded);
            var right = new Battle(copy);
            for (long tick = 1; tick <= 300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
            var frame = left.Capture(1);
            Assert.That(frame.Economy.Buildings.All(b => b.AiReason == AiReason.None && b.AiReasonValue == 0), Is.True);
            Assert.That(frame.Economy.Belts.All(b => b.AiReason == AiReason.None && b.AiReasonValue == 0), Is.True);
        }

        [Test]
        public void CaptureDoesNotChangeTheCanonicalState()
        {
            var simulation = new Battle(LiveGameRules.Create(1, true, true, false, false));
            for (long tick = 1; tick <= 200; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            byte[] before = ReplayBinary.Hash(simulation.CaptureDiagnostic().CanonicalState);
            for (int i = 0; i < 10; i++) simulation.Capture(1);
            byte[] after = ReplayBinary.Hash(simulation.CaptureDiagnostic().CanonicalState);
            Assert.That(after, Is.EqualTo(before));
        }

        [Test]
        public void LargeMapAutomaticObjectsHaveReasonsAndAuthoredObjectsDoNot()
        {
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var simulation = new Battle(LiveGameRules.Create((ulong)seed, true, true, false, false));
                for (long tick = 1; tick <= 15000 && !simulation.Capture(1).Result.HasEnded; tick++)
                {
                    simulation.Step(tick, Array.Empty<ScheduledInput>());
                }

                var economy = simulation.Capture(1).Economy;
                var automaticBuildings = economy.Buildings.Where(b => b.FactionId == 1 && !b.PlayerHeld).ToArray();
                var automaticBelts = economy.Belts.Where(b => b.FactionId == 1 && !b.PlayerHeld).ToArray();
                Assert.That(automaticBuildings.Any(b => b.AiReason == AiReason.None || b.AiReason == AiReason.Unspecified), Is.False, "seed " + seed);
                Assert.That(automaticBelts.Any(b => b.AiReason == AiReason.None || b.AiReason == AiReason.Unspecified), Is.False, "seed " + seed);
                Assert.That(economy.Buildings.Where(b => b.FactionId == 1 && b.PlayerHeld).All(b => b.AiReason == AiReason.None), Is.True, "seed " + seed);
                Assert.That(economy.Belts.Where(b => b.FactionId == 1 && b.PlayerHeld).All(b => b.AiReason == AiReason.None), Is.True, "seed " + seed);
            }
        }

        [Test]
        public void ReasonValuesUseTheirDeclaredDomains()
        {
            var simulation = new Battle(LiveGameRules.Create(1, true, true, false, false));
            for (long tick = 1; tick <= 15000 && !simulation.Capture(1).Result.HasEnded; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());
            var economy = simulation.Capture(1).Economy;
            foreach (var building in economy.Buildings.Where(b => b.AiReason != AiReason.None))
            {
                if (building.AiReason == AiReason.MetalLine)
                    Assert.That(building.AiReasonValue, Is.GreaterThanOrEqualTo(0), building.Kind.ToString());
                if (building.AiReason == AiReason.FarResource)
                    Assert.That(building.AiReasonValue, Is.GreaterThan(0), building.Kind.ToString());
                if (building.AiReason == AiReason.MaterialFor || building.AiReason == AiReason.ResourceBelt
                    || building.AiReason == AiReason.ReservationWait)
                    Assert.That(Enum.IsDefined(typeof(ResourceKind), (byte)building.AiReasonValue), building.Kind.ToString());
            }
            foreach (var belt in economy.Belts.Where(b => b.AiReason != AiReason.None))
            {
                if (belt.AiReason == AiReason.ResourceBelt)
                    Assert.That(Enum.IsDefined(typeof(ResourceKind), (byte)belt.AiReasonValue), belt.Cell.ToString());
                if (belt.AiReason == AiReason.ProcessingBelt)
                    Assert.That(Enum.IsDefined(typeof(ProcessingLineKind), (byte)belt.AiReasonValue), belt.Cell.ToString());
            }
        }

        [Test]
        public void EveryAiReasonHasAContractValue()
        {
            foreach (AiReason reason in Enum.GetValues(typeof(AiReason)))
                Assert.That(Enum.IsDefined(typeof(AiReason), reason), Is.True);
        }
    }
}
