using NUnit.Framework;
using Rts.Simulation;

namespace Rts.Core.Tests
{
    public sealed class LargeEconomyPlannerTests
    {
        [Test]
        public void PlannerUsesRPlusProductionFormulasAndTargets()
        {
            var rules = LiveGameRules.Create(1, true, true, false, false).Economy;
            var plan = LargeEconomyPlanner.Build(rules, 300, 20,
                new[] { 3000, 1200, 0, 0, 0, 300 },
                new[] { 200, 200, 200, 200, 200, 200 },
                32, 20, 20, 15, 1, 100, 3, 1);

            Assert.That(plan.R, Is.EqualTo(60));
            Assert.That(plan.RPlus, Is.EqualTo(60));
            Assert.That(plan.RequiredPerMinute[0], Is.EqualTo(3000));
            Assert.That(plan.RequiredPerMinute[1], Is.EqualTo(1200));
            Assert.That(plan.RequiredPerMinute[5], Is.EqualTo(300));
            Assert.That(plan.TargetFarms, Is.EqualTo(32));
            Assert.That(plan.TargetMines, Is.EqualTo(20));
            Assert.That(plan.TargetSmelters, Is.EqualTo(15));
            Assert.That(plan.TargetBarracks, Is.EqualTo(1));
            Assert.That(plan.Stage, Is.EqualTo(LargeEconomyPlanStage.ProduceArmy));
        }

        [Test]
        public void PlannerKeepsTownAndDropSiteTargetsDownstreamOfChain()
        {
            var rules = LiveGameRules.Create(2, true, true, false, false).Economy;
            var plan = LargeEconomyPlanner.Build(rules, 300, 20,
                new[] { 3000, 1200, 0, 0, 0, 300 },
                new[] { 200, 200, 200, 200, 200, 200 },
                32, 20, 20, 15, 1, 100, 0, 0);

            Assert.That(plan.Stage, Is.EqualTo(LargeEconomyPlanStage.ProduceArmy));
            Assert.That(plan.TargetTowns, Is.GreaterThan(0));
            Assert.That(plan.TargetDropSites, Is.GreaterThan(0));
        }
    }
}
