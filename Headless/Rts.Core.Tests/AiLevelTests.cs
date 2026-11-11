using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class AiLevelTests
    {
        [Test]
        public void Extension108RoundTripsBothFactionLevels()
        {
            var source = WeekOneScenario.Create();
            source.Economy.AiLevelWest = 1;
            source.Economy.AiLevelEast = 2;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(value => value.Id == 108);

            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(extension.Data.Length, Is.EqualTo(2 * sizeof(int)));
            Assert.That((decoded.Economy.AiLevelWest, decoded.Economy.AiLevelEast), Is.EqualTo((1, 2)));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void AiLevelAtLeastUsesTheMatchingFactionAndRejectsOtherFactions()
        {
            var rules = new EconomyRules { AiLevelWest = 2, AiLevelEast = 1 };

            Assert.That(rules.AiLevelAtLeast(1, 2), Is.True);
            Assert.That(rules.AiLevelAtLeast(1, 3), Is.False);
            Assert.That(rules.AiLevelAtLeast(2, 1), Is.True);
            Assert.That(rules.AiLevelAtLeast(2, 2), Is.False);
            Assert.That(rules.AiLevelAtLeast(0, 0), Is.False);
            Assert.That(rules.AiLevelAtLeast(3, 0), Is.False);
        }

        [Test]
        public void BothZeroOmitExtensionAndKeepEveryTickHashUnchanged()
        {
            var baseline = WeekOneScenario.Create();
            var stageZero = WeekOneScenario.Create();
            stageZero.Economy.AiLevelWest = 0;
            stageZero.Economy.AiLevelEast = 0;

            Assert.That(ScenarioBinary.Encode(stageZero), Is.EqualTo(ScenarioBinary.Encode(baseline)));
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(stageZero)).Extensions.Any(value => value.Id == 108), Is.False);

            var left = new Battle(baseline);
            var right = new Battle(stageZero);
            for (long tick = 1; tick <= 32; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }
    }
}
