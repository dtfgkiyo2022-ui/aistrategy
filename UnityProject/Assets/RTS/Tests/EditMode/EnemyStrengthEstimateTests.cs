using System;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Decision;
using Rts.Simulation;

namespace Rts.Tests.EditMode
{
    public sealed class EnemyStrengthEstimateTests
    {
        [Test]
        public void FogAutoVersusAutoHasADeathWithinTwoThousandTicks()
        {
            var sim = new Rts.Simulation.Simulation(WeekTwoScenario.Create());
            var previous = new uint[] { 1, 2 }.SelectMany(f => sim.Capture(f).Units.Where(u => u.IsOwn).Select(u => u.Id)).ToArray();
            for (int tick = 1; tick <= 2000; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(sim.Capture(1).Result.IsFault, Is.False, "tick " + tick);
                var current = new uint[] { 1, 2 }.SelectMany(f => sim.Capture(f).Units.Where(u => u.IsOwn).Select(u => u.Id)).ToArray();
                if (previous.Except(current).Any()) { TestContext.WriteLine("First death tick: " + tick); return; }
                previous = current;
                if (tick % 500 == 0)
                {
                    TestContext.WriteLine("Tick " + tick + " " + string.Join("; ", sim.Capture(1).Observation.OwnArmies.Select(a => a.Id + ":" + a.Position.X + "," + a.Position.Z + " n=" + a.AliveCount + " estimate=" + PolicyDecision.Estimate(sim.Capture(1).Observation, a.Position))));
                    TestContext.WriteLine(string.Join("; ", Rts.Replay.DiagnosticComparison.Fields(sim.CaptureDiagnostic()).Where(p => p.Key.StartsWith("Ai.Factions[1].Offense.")).Select(p => p.Key + "=" + p.Value)));
                }
            }
            Assert.Fail("Fog auto versus auto produced no deaths within 2000 ticks.");
        }
    }
}
