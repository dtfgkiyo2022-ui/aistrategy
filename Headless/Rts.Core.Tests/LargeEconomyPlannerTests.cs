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
            Assert.That(plan.RequiredPerMinute[1], Is.GreaterThan(1200));
            Assert.That(plan.ConstructionWoodPerMinute, Is.GreaterThan(0));
            Assert.That(plan.RequiredPerMinute[5], Is.EqualTo(300));
            Assert.That(plan.TargetFarms, Is.EqualTo(32));
            Assert.That(plan.TargetMines, Is.EqualTo(20));
            Assert.That(plan.TargetSmelters, Is.EqualTo(15));
            Assert.That(plan.TargetBarracks, Is.EqualTo(2));
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

        [Test]
        public void StageOneUsesQuarterTargetAndStartsMetalChainAndDefence()
        {
            var rules = LiveGameRules.Create(3, true, true, false, false).Economy;
            var plan = LargeEconomyPlanner.Build(rules, new LargeEconomyPlanningInput
            {
                Villagers = 80,
                Soldiers = 20,
                IncomePerMinute = new int[6],
                WorkerRoundTripTicks = new[] { 200, 200, 200, 200, 200, 200 },
                KnownResourceCounts = new[] { 4, 4, 2, 2, 0, 0 },
                CurrentStage = LargeEconomyPlanStage.GrowVillage
            });

            Assert.That(plan.Stage, Is.EqualTo(LargeEconomyPlanStage.GrowVillage));
            Assert.That(plan.RPlus, Is.EqualTo(15));
            Assert.That(plan.TargetMines, Is.GreaterThanOrEqualTo(2));
            Assert.That(plan.TargetSmelters, Is.GreaterThanOrEqualTo(1));
            Assert.That(plan.TargetBarracks, Is.EqualTo(1));
            Assert.That(plan.RequiredPerMinute[1], Is.GreaterThan(20 * 15));
        }

        [Test]
        public void StageAdvancesOnVillagerStallOrClockAndThenMetalOrClock()
        {
            var rules = LiveGameRules.Create(4, true, true, false, false).Economy;
            Assert.That(LargeEconomyPlanner.AdvanceStage(rules, LargeEconomyPlanStage.GrowVillage,
                100, rules.LargeEconomyStage1TimeoutTicks - 1, rules.LargeEconomyVillagerStallTicks - 1, 0),
                Is.EqualTo(LargeEconomyPlanStage.GrowVillage));
            Assert.That(LargeEconomyPlanner.AdvanceStage(rules, LargeEconomyPlanStage.GrowVillage,
                100, rules.LargeEconomyVillagerStallMinimumTicks - 1, rules.LargeEconomyVillagerStallTicks, 0),
                Is.EqualTo(LargeEconomyPlanStage.GrowVillage));
            Assert.That(LargeEconomyPlanner.AdvanceStage(rules, LargeEconomyPlanStage.GrowVillage,
                100, rules.LargeEconomyVillagerStallMinimumTicks, rules.LargeEconomyVillagerStallTicks, 0),
                Is.EqualTo(LargeEconomyPlanStage.BuildChain));
            Assert.That(LargeEconomyPlanner.AdvanceStage(rules, LargeEconomyPlanStage.GrowVillage,
                100, rules.LargeEconomyStage1TimeoutTicks, 0, 0),
                Is.EqualTo(LargeEconomyPlanStage.BuildChain));
            Assert.That(LargeEconomyPlanner.AdvanceStage(rules, LargeEconomyPlanStage.BuildChain,
                100, 0, 0, 150), Is.EqualTo(LargeEconomyPlanStage.ProduceArmy));
            Assert.That(LargeEconomyPlanner.AdvanceStage(rules, LargeEconomyPlanStage.BuildChain,
                100, rules.LargeEconomyStage2TimeoutTicks, 0, 0),
                Is.EqualTo(LargeEconomyPlanStage.ProduceArmy));
        }

        [Test]
        public void OverfullStockRedirectsKnownWorkersToTheMostBlockedResource()
        {
            var rules = LiveGameRules.Create(5, true, true, false, false).Economy;
            var plan = LargeEconomyPlanner.Build(rules, new LargeEconomyPlanningInput
            {
                Villagers = 100,
                Soldiers = 20,
                Stock = new[] { 5000, 0, 0, 0, 0, 0 },
                IncomePerMinute = new int[6],
                WorkerRoundTripTicks = new[] { 200, 200, 200, 200, 200, 200 },
                KnownResourceCounts = new[] { 4, 4, 2, 2, 0, 0 }
            });

            Assert.That(plan.WorkerTargets[0], Is.EqualTo(0));
            Assert.That(plan.WorkerTargets[1] + plan.WorkerTargets[2] + plan.WorkerTargets[3], Is.GreaterThan(0));
        }
    }
}
