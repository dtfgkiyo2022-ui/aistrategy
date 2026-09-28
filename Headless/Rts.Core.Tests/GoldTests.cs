using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class GoldTests
    {
        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) AtSecondAge(CivKind civ)
        {
            var scenario = MapGenerator.GenerateTerrain(1, gold: true);
            scenario.Economy.StartFood = 6000;
            scenario.Economy.StartWood = 6000;
            scenario.Economy.StartStone = 600;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, civ));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 2);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, civ));
            Steps(gateway, sim, scenario.Economy.Age2Ticks + 2);
            Assert.That(sim.Capture(1).Economy.Age, Is.EqualTo(2));
            return (scenario, sim, gateway, sequence);
        }

        [Test]
        public void OldAndExplicitlyOffTerrainRemainByteAndTickIdentical()
        {
            for (ulong seed = 1; seed <= 3; seed++)
            {
                var old = MapGenerator.GenerateTerrain(seed);
                var off = MapGenerator.GenerateTerrain(seed, gold: false);
                Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(old)));
                var left = new Battle(old); var right = new Battle(off);
                for (long tick = 1; tick <= 300; tick++)
                {
                    left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                    Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState), Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "seed " + seed + ", tick " + tick);
                }
            }
        }

        [Test]
        public void GoldMapsCommitFourNodesOnlyAsOneAtomicLayout()
        {
            int placed = 0;
            for (ulong seed = 1; seed <= 50; seed++)
            {
                var scenario = MapGenerator.GenerateTerrain(seed, gold: true);
                var gold = scenario.ResourceNodes.Where(n => n.Kind == ResourceKind.Gold).ToArray();
                if (gold.Length == 0)
                {
                    Assert.That(scenario.Economy.GoldEnabled, Is.False, "failed placement must leave no flag, seed " + seed);
                    continue;
                }
                placed++;
                Assert.That(gold, Has.Length.EqualTo(4));
                Assert.That(gold.All(n => n.Amount == 400), Is.True);
                Assert.That(scenario.Economy.Age3GoldCostAgrarian, Is.EqualTo(0));
                Assert.That(scenario.Economy.Age3GoldCostMetallurgy, Is.EqualTo(200));
                Assert.That(gold.Select(n => n.Id), Is.EqualTo(Enumerable.Range(scenario.ResourceNodes.Length - 3, 4).Select(i => (uint)i)));
            }
            TestContext.WriteLine("gold placement: " + placed + "/50 (" + (placed * 100 / 50).ToString(CultureInfo.InvariantCulture) + "%)");
        }

        [Test]
        public void RemovingCorePathDifferenceAllowsTheFormerlyRejectedFairnessCase()
        {
            Assert.That(MapGenerator.MeasureGoldPlacement(3).Placed, Is.True);
        }

        [Test]
        public void OnlyMetallurgyPaysGoldForTheThirdAge()
        {
            var agrarian = AtSecondAge(CivKind.Agrarian);
            int agrarianFood = agrarian.sim.Capture(1).Economy.Food;
            agrarian.gateway.SubmitEconomy(EconomyCommand.Advance(1, ++agrarian.sequence, CivKind.Agrarian));
            Steps(agrarian.gateway, agrarian.sim, 1);
            Assert.That(agrarian.sim.Capture(1).Economy.AdvanceRemaining, Is.GreaterThan(0));
            Assert.That(agrarian.sim.Capture(1).Economy.Gold, Is.EqualTo(0));
            Assert.That(agrarian.sim.Capture(1).Economy.Food, Is.EqualTo(agrarianFood - agrarian.scenario.Economy.Age3FoodCost));

            var metallurgy = AtSecondAge(CivKind.Metallurgy);
            int metallurgyFood = metallurgy.sim.Capture(1).Economy.Food;
            metallurgy.gateway.SubmitEconomy(EconomyCommand.Advance(1, ++metallurgy.sequence, CivKind.Metallurgy));
            Steps(metallurgy.gateway, metallurgy.sim, 1);
            Assert.That(metallurgy.sim.Capture(1).Economy.AdvanceRemaining, Is.EqualTo(0));
            Assert.That(metallurgy.sim.Capture(1).Economy.Gold, Is.EqualTo(0));
            Assert.That(metallurgy.sim.Capture(1).Economy.Food, Is.EqualTo(metallurgyFood));
        }

        [Test]
        public void GoldPlacementFailureReasonsAreMeasuredForSeedsOneToFifty()
        {
            var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
            var reports = new List<GoldPlacementReport>();
            int placed = 0;
            for (ulong seed = 1; seed <= 50; seed++)
            {
                var report = MapGenerator.MeasureGoldPlacement(seed);
                if (report.Placed) { placed++; continue; }
                reports.Add(report);
                reasons[report.FailureReason] = reasons.TryGetValue(report.FailureReason, out int count) ? count + 1 : 1;
            }
            TestContext.WriteLine("gold placement success: " + placed + "/50");
            foreach (var pair in reasons.OrderBy(p => p.Key)) TestContext.WriteLine(pair.Key + ": " + pair.Value);
            TestContext.WriteLine("seed | 最初に落ちた条件");
            foreach (var report in reports)
                TestContext.WriteLine(report.Seed + " | " + report.FailureReason);
            var successful = Enumerable.Range(1, 50).Select(seed => MapGenerator.MeasureGoldPlacement((ulong)seed)).Where(r => r.Placed).ToArray();
            Assert.That(successful.Length, Is.EqualTo(placed));
            Assert.That(successful.Length, Is.GreaterThan(0));
            TestContext.WriteLine("配置成功地図の本拠地から最近金までの経路差: 最大 "
                + successful.Max(r => r.NearestGoldPathDifferenceMeters) + "m、平均 "
                + successful.Average(r => r.NearestGoldPathDifferenceMeters).ToString("F2", CultureInfo.InvariantCulture) + "m");
        }

        [Test]
        public void GoldRulesRoundTripAndEconomyViewExposeAge3AndMonkCosts()
        {
            var scenario = MapGenerator.GenerateTerrain(1, gold: true);
            scenario.Economy.MonksEnabled = true;
            var sim = new Battle(scenario);
            var economy = sim.Capture(1).Economy;
            Assert.That(scenario.Economy.GoldEnabled, Is.True);
            Assert.That(scenario.Economy.Age3GoldCostAgrarian, Is.EqualTo(0));
            Assert.That(scenario.Economy.Age3GoldCostMetallurgy, Is.EqualTo(200));
            Assert.That(economy.MonkGoldCost, Is.EqualTo(scenario.Economy.MonkGoldCost));
            Assert.That(economy.Gold, Is.EqualTo(0));
            var bytes = ScenarioBinary.Encode(scenario);
            Assert.That(ScenarioBinary.Encode(ScenarioBinary.Decode(bytes)), Is.EqualTo(bytes));
        }

        [Test]
        public void AGoldMapReplaysEveryTick()
        {
            var scenario = MapGenerator.GenerateTerrain(1, gold: true);
            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 1200, build);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, build);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        [Test]
        public void GoldAutoEconomyRunsWithConfiguredPerKindFloors()
        {
            var scenario = MapGenerator.GenerateTerrain(1, gold: true);
            scenario.Economy.StartFood = 10000;
            scenario.Economy.StartWood = 10000;
            scenario.Economy.AutoVillagerTarget = 6;
            scenario.Economy.GoldGatherers = 2;
            var sim = new Battle(scenario);
            for (long tick = 1; tick <= 2000; tick++) sim.Step(tick, Array.Empty<ScheduledInput>());
            Assert.That(sim.Capture(1).Economy.Gold, Is.GreaterThanOrEqualTo(0));
            Assert.That(scenario.Economy.GoldGatherers, Is.EqualTo(2));
        }
    }
}
