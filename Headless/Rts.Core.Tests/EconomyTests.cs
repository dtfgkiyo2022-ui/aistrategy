using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>
    /// technical-design-v3 5 (V3-1 PR2): gathering, dropping off and training villagers. The runs cross the gather
    /// cycle (20 ticks x 10 plus the walks), the training time (200) and the AI cycle (20) many times, because a check of
    /// one tick only shows the opening state (the sentry lesson).
    /// </summary>
    public sealed class EconomyTests
    {
        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> fields, string name) => long.Parse(fields[name], CultureInfo.InvariantCulture);

        private static void Run(Battle sim, long from, long to, Action<long> each = null)
        {
            for (long t = from; t <= to && !sim.Capture(1).Result.HasEnded; t++)
            {
                sim.Step(t, Array.Empty<ScheduledInput>());
                each?.Invoke(t);
            }
        }

        [Test]
        public void VerOneScenariosWriteNoEconomyState()
        {
            var fields = Fields(new Battle(WeekTwoScenario.Create()));
            Assert.That(fields.Keys.Any(k => k.StartsWith("Economy[", StringComparison.Ordinal) || k.StartsWith("Villagers", StringComparison.Ordinal)), Is.False);
            var generated = Fields(new Battle(MapGenerator.Generate(3)));
            Assert.That(generated.ContainsKey("Economy[1].Food"), Is.False, "a map without the economy writes none either");
        }

        // Every unit of resource is somewhere: still in a node, carried by a villager, in the stock, or spent on a villager.
        [TestCase(1UL)]
        [TestCase(2UL)]
        [TestCase(7UL)]
        public void ResourcesAreConservedEveryTickAcrossManyGatherCycles(ulong seed)
        {
            var scenario = MapGenerator.Generate(seed, true);
            var rules = scenario.Economy;
            long nodesAtStart = scenario.ResourceNodes.Sum(n => (long)n.Amount);
            var sim = new Battle(scenario);
            Run(sim, 1, 6000, t =>
            {
                var f = Fields(sim);
                long nodes = 0, carried = 0, stock = 0, trained = 0, queued = 0;
                for (int i = 1; i <= scenario.ResourceNodes.Length; i++)
                {
                    long left = Number(f, "ResourceNodes[" + i + "].Remaining");
                    Assert.That(left, Is.GreaterThanOrEqualTo(0));
                    nodes += left;
                }
                long villagers = Number(f, "Villagers.Count");
                for (int i = 1; i <= villagers; i++)
                {
                    long carry = Number(f, "Villagers[" + i + "].Carry");
                    Assert.That(carry, Is.InRange(0, rules.CarryCapacity), "tick " + t);
                    carried += carry;
                }
                for (int faction = 1; faction <= 2; faction++)
                {
                    stock += Number(f, "Economy[" + faction + "].Food") + Number(f, "Economy[" + faction + "].Wood");
                    queued += Number(f, "Economy[" + faction + "].Queued");
                }
                trained = villagers - scenario.Villagers.Length;
                long buildings = Number(f, "Buildings.Count"), infantryQueued = 0;
                for (int i = 1; i <= buildings; i++) infantryQueued += Number(f, "Buildings[" + i + "].Queued");
                long infantryTrained = Number(f, "NextSoldierId") - 1 - scenario.Soldiers.Length; // no free reinforcements
                long spent = (trained + queued) * rules.VillagerFoodCost + buildings * rules.BarracksWoodCost
                    + (infantryTrained + infantryQueued) * (rules.InfantryFoodCost + rules.InfantryWoodCost);
                long startStock = 2L * (rules.StartFood + rules.StartWood);
                Assert.That(nodesAtStart - nodes, Is.EqualTo(stock + carried + spent - startStock), "conservation at tick " + t);
            });
        }

        [Test]
        public void VillagersGatherBothResourcesAndTheCoreTrainsUpToTheTarget()
        {
            var scenario = MapGenerator.Generate(1, true);
            var sim = new Battle(scenario);
            Run(sim, 1, 6000);
            var f = Fields(sim);
            for (int faction = 1; faction <= 2; faction++)
            {
                int alive = 0;
                for (int i = 1; i <= Number(f, "Villagers.Count"); i++)
                    if (f["Villagers[" + i + "].FactionId"] == faction.ToString(CultureInfo.InvariantCulture) && f["Villagers[" + i + "].Alive"] == "1") alive++;
                Assert.That(alive, Is.EqualTo(scenario.Economy.AutoVillagerTarget), "faction " + faction + " villagers");
                // Food: 200 at start, minus 7 villagers at 50, plus gathering. Wood only ever grows in PR2.
                Assert.That(Number(f, "Economy[" + faction + "].Wood"), Is.GreaterThan(scenario.Economy.StartWood), "faction " + faction + " gathered wood");
                Assert.That(Number(f, "Economy[" + faction + "].Food"), Is.GreaterThan(scenario.Economy.StartFood - 7 * scenario.Economy.VillagerFoodCost), "faction " + faction + " gathered food");
            }
        }

        [Test]
        public void ThePopulationCapHoldsTrainingBack()
        {
            var scenario = MapGenerator.Generate(1, true);
            scenario.Economy.PopulationCap = 20 + 5; // 20 soldiers and 3 villagers per side: room for 2 more
            var sim = new Battle(scenario);
            bool heldBack = false;
            Run(sim, 1, 4000, t =>
            {
                var f = Fields(sim);
                for (int faction = 1; faction <= 2; faction++)
                {
                    string id = faction.ToString(CultureInfo.InvariantCulture);
                    int villagers = 0, soldiers = 0;
                    for (int i = 1; i <= Number(f, "Villagers.Count"); i++)
                        if (f["Villagers[" + i + "].FactionId"] == id && f["Villagers[" + i + "].Alive"] == "1") villagers++;
                    for (int i = 1; i <= Number(f, "Soldiers.Count"); i++)
                        if (f["Soldiers[" + i + "].FactionId"] == id && f["Soldiers[" + i + "].Alive"] == "1") soldiers++;
                    // Deaths in battle free room, so the cap is on the sum, not on the villagers alone.
                    Assert.That(villagers + soldiers, Is.LessThanOrEqualTo(25), "tick " + t + " faction " + faction);
                    if (villagers + soldiers == 25) heldBack = true;
                }
            });
            Assert.That(heldBack, "the cap was reached at least once, so it was actually tested");
        }

        [Test]
        public void FreeReinforcementsStopWhenTheEconomyIsOn()
        {
            // Free reinforcements would add one at tick 100; a barracks needs at least 200 to build and 300 to train.
            var sim = new Battle(MapGenerator.Generate(4, true));
            Run(sim, 1, 450);
            Assert.That(Number(Fields(sim), "NextSoldierId"), Is.EqualTo(41), "no soldier appears before production can deliver one");
            var off = new Battle(MapGenerator.Generate(4));
            Run(off, 1, 1000);
            Assert.That(Number(Fields(off), "NextSoldierId"), Is.GreaterThan(41), "the Ver.1 map still reinforces");
        }

        // 5.4 steps 2-3 and 5.2: a barracks goes up near the core, its footprint closes, builders finish it, and it trains
        // infantry that join a Ver.1 army. Checked every tick from placement on, well past the build (400) and training
        // (300) times.
        [TestCase(1UL)]
        [TestCase(2UL)]
        public void ABarracksIsBuiltAndItsInfantryJoinTheArmies(ulong seed)
        {
            var scenario = MapGenerator.Generate(seed, true);
            var sim = new Battle(scenario);
            long placedAt = 0, completedAt = 0, firstSoldierAt = 0;
            Run(sim, 1, 12000, t =>
            {
                var f = Fields(sim);
                if (Number(f, "Buildings.Count") == 0) return;
                if (placedAt == 0) placedAt = t;
                if (completedAt == 0 && f["Buildings[1].Complete"] == "1") completedAt = t;
                if (firstSoldierAt == 0 && Number(f, "NextSoldierId") > scenario.Soldiers.Length + 1) firstSoldierAt = t;
                // Nobody stands inside a footprint.
                for (int b = 1; b <= Number(f, "Buildings.Count"); b++)
                {
                    int origin = (int)Number(f, "Buildings[" + b + "].OriginCell");
                    var cells = new HashSet<int>();
                    for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++) cells.Add(origin + z * 128 + x);
                    for (int i = 1; i <= Number(f, "Soldiers.Count"); i++)
                        if (f["Soldiers[" + i + "].Alive"] == "1") Assert.That(cells.Contains(Cell(f, "Soldiers[" + i + "].Position")), Is.False, "soldier " + i + " inside at tick " + t);
                    for (int i = 1; i <= Number(f, "Villagers.Count"); i++)
                        if (f["Villagers[" + i + "].Alive"] == "1") Assert.That(cells.Contains(Cell(f, "Villagers[" + i + "].Position")), Is.False, "villager " + i + " inside at tick " + t);
                }
            });
            Assert.That(placedAt, Is.GreaterThan(0), "a barracks was placed");
            Assert.That(completedAt, Is.GreaterThanOrEqualTo(placedAt + scenario.Economy.BarracksWork / scenario.Economy.Builders), "two builders need at least 200 ticks");
            Assert.That(firstSoldierAt, Is.GreaterThanOrEqualTo(completedAt + scenario.Economy.InfantryTrainTicks), "training takes its time");
            var end = Fields(sim);
            // Produced soldiers were assigned to an army (Ver.1 reinforcement assignment), never to none.
            for (long i = scenario.Soldiers.Length + 1; i < Number(end, "NextSoldierId"); i++)
                Assert.That(Number(end, "Soldiers[" + i + "].ArmyId"), Is.InRange(1, 8), "soldier " + i);
        }

        private static int Cell(Dictionary<string, string> f, string point)
            => (int)(Number(f, point + ".Z.Raw") / 65536 / 2) * 128 + (int)(Number(f, point + ".X.Raw") / 65536 / 2);

        [Test]
        public void AnEconomyMatchReplaysWithEveryTickMatching()
        {
            var scenario = MapGenerator.Generate(2, true);
            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 8000, build);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, build);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
                Assert.That(outcome.LastTick, Is.GreaterThanOrEqualTo(3000), "long enough to build and train");
            }
        }

        [Test]
        public void TheEconomyScenarioRoundTripsThroughTheBinary()
        {
            var bytes = ScenarioBinary.Encode(MapGenerator.Generate(9, true));
            Assert.That(ScenarioBinary.Encode(ScenarioBinary.Decode(bytes)), Is.EqualTo(bytes));
            // The ground is the same as without the economy: the villagers take no draws.
            Assert.That(MapGenerator.Generate(9, true).Map.BlockedCellIds, Is.EqualTo(MapGenerator.Generate(9).Map.BlockedCellIds));
        }

        [Test]
        public void VillagersWithoutAnEconomyAreRejected()
        {
            var s = MapGenerator.Generate(9, true);
            s.Economy.Enabled = false;
            Assert.Throws<ArgumentException>(() => new Battle(s));
        }
    }
}
