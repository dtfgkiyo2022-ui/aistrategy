using System;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class AllocationTests
    {
        [Test]
        public void WideMapLatterThousandTicksStayBelowSixHundredKilobytesPerTick()
        {
            var scenario = LiveGameRules.Create(2, true, true, false, false);
            ScenarioScale.MultiplyForMeasurement(scenario, 4, 8);
            var simulation = new Battle(scenario);

            for (long tick = 1; tick <= 1000; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (long tick = 1001; tick <= 2000; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated / 1000.0, Is.LessThanOrEqualTo(0.6 * 1024 * 1024),
                "latter 1000 ticks allocated " + allocated + " bytes");
        }
    }
}
