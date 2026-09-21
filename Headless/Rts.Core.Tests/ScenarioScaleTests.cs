using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;

namespace Rts.Tests.Headless
{
    /// <summary>Stage 5 measurement helper: a multiplied scenario must still be a valid, playable one.</summary>
    public sealed class ScenarioScaleTests
    {
        [Test]
        public void FactorOneLeavesTheScenarioAsItWas()
        {
            var scenario = WeekTwoScenario.Create();
            var scaled = ScenarioScale.Multiply(scenario, 1);
            Assert.That(scaled.ScenarioId, Is.EqualTo("week2-2routes"));
            Assert.That(scaled.Soldiers.Length, Is.EqualTo(40));
        }

        [TestCase(2, 80)]
        [TestCase(6, 240)]
        [TestCase(12, 480)]
        public void EverySoldierIsRepeatedAndTheCapsFollow(int factor, int expected)
        {
            var scaled = ScenarioScale.Multiply(WeekTwoScenario.Create(), factor);
            Assert.That(scaled.Soldiers.Length, Is.EqualTo(expected));
            Assert.That(scaled.Rules.FactionCap, Is.EqualTo(40 * factor));
            Assert.That(scaled.Soldiers.Select(d => d.Id), Is.EqualTo(Enumerable.Range(1, expected).Select(i => (uint)i)));
            // Nobody may share a starting spot with another soldier.
            var spots = scaled.Soldiers.Select(d => d.Position.X.Raw + "," + d.Position.Z.Raw).Distinct().Count();
            Assert.That(spots, Is.EqualTo(expected));
        }

        [Test]
        public void AMultipliedScenarioPassesValidationAndRuns()
        {
            var sim = new Rts.Simulation.Simulation(ScenarioScale.Multiply(WeekTwoScenario.Create(), 12));
            for (long tick = 1; tick <= 60; tick++) sim.Step(tick, System.Array.Empty<ScheduledInput>());
            Assert.That(sim.Capture(1).Result.IsFault, Is.False);
        }

        [Test]
        public void ARepeatedRunGivesTheSameFinalHash()
        {
            string Run()
            {
                var sim = new Rts.Simulation.Simulation(ScenarioScale.Multiply(WeekTwoScenario.Create(), 6));
                for (long tick = 1; tick <= 200; tick++) sim.Step(tick, System.Array.Empty<ScheduledInput>());
                return System.Convert.ToHexString(sim.CaptureDiagnostic().CanonicalState.ToArray());
            }
            Assert.That(Run(), Is.EqualTo(Run()));
        }
    }
}
