using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class AiBeltPartsTests
    {
        [Test]
        public void Extension85RoundTripsAiBeltPartsRules()
        {
            var scenario = LiveGameRules.Create(85UL, true, true, false, false);
            scenario.Economy.AiBeltParts = true;
            scenario.Economy.AiBeltClusterTries = 5;
            scenario.Economy.AiBeltClusterRetryTicks = 4321;
            scenario.Economy.AiMergeMaxCells = 17;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(value => value.Id == 85);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.AiBeltParts, Is.True);
            Assert.That(decoded.Economy.AiBeltClusterTries, Is.EqualTo(5));
            Assert.That(decoded.Economy.AiBeltClusterRetryTicks, Is.EqualTo(4321));
            Assert.That(decoded.Economy.AiMergeMaxCells, Is.EqualTo(17));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void FlagOffOmitsExtensionAndKeepsTickHashesStable()
        {
            var left = LiveGameRules.Create(86UL, true, true, false, false);
            var right = LiveGameRules.Create(86UL, true, true, false, false);
            left.Economy.AiBeltParts = false;
            right.Economy.AiBeltParts = false;
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(left)).Extensions.Any(value => value.Id == 85), Is.False);
            var a = new Battle(left);
            var b = new Battle(right);
            for (long tick = 1; tick <= 500; tick++)
            {
                a.Step(tick, Array.Empty<ScheduledInput>());
                b.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(a.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(b.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void LiveRulesEnableAiBeltPartsWithBeltCraftOnly()
        {
            var large = LiveGameRules.Create(87UL, true, true, false, false);
            var small = LiveGameRules.Create(87UL, false, true, false, false);
            Assert.That(large.Economy.BeltCraft, Is.True);
            Assert.That(large.Economy.AiBeltParts, Is.True);
            Assert.That(small.Economy.AiBeltParts, Is.False);
        }
    }
}
