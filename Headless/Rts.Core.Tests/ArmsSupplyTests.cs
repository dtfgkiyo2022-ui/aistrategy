using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class ArmsSupplyTests
    {
        [Test]
        public void LargeLiveRulesUseTheNewClockAndEnableArmsSupply()
        {
            var enabled = LiveGameRules.Create(1, true, true, false, false).Economy;
            Assert.That(enabled.ArmsSupply, Is.True);
            Assert.That(new[] { enabled.AgeClockTicks1, enabled.AgeClockTicks2, enabled.AgeClockTicks3, enabled.AgeClockTicks4 },
                Is.EqualTo(new[] { 3600, 7200, 10800, 14400 }));
            Assert.That(LiveGameRules.Create(1, true, true, false, false, armsSupplyOff: true).Economy.ArmsSupply, Is.False);
        }

        [Test]
        public void Extension121RoundTripsAndOffProfileOmitsIt()
        {
            var enabled = LiveGameRules.Create(2, true, true, false, false);
            var bytes = ScenarioBinary.Encode(enabled);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.ArmsSupply, Is.True);
            Assert.That(decoded.Extensions.Single(x => x.Id == 121).Version, Is.EqualTo(1));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var disabled = LiveGameRules.Create(2, true, true, false, false, armsSupplyOff: true);
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(disabled)).Extensions.Any(x => x.Id == 121), Is.False);
        }

        [Test]
        public void FlagOffRoundTripKeepsCanonicalHashesIdentical()
        {
            var source = LiveGameRules.Create(3, true, true, false, false, armsSupplyOff: true);
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            var left = new Battle(source);
            var right = new Battle(decoded);
            for (long tick = 1; tick <= 300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }
    }
}
