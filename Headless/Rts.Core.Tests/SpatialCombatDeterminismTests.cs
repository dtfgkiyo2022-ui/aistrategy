using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class SpatialCombatDeterminismTests
    {
        [Test]
        public void SpatialCombatIndexKeepsTwoIdenticalRunsBitStable()
        {
            var left = new Battle(LiveGameRules.Create(1, false, true, false, false));
            var right = new Battle(LiveGameRules.Create(1, false, true, false, false));
            for (long tick = 1; tick <= 1200; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState.ToArray()),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState.ToArray())), "tick " + tick);
            }
        }
    }
}
