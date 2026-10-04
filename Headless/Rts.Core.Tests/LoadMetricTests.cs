using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Headless.Cli;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    [TestFixture]
    public sealed class LoadMetricTests
    {
        [Test]
        public void SameSeedAndSettingsProduceTheSameCsv()
        {
            string first = Path.Combine(TestContext.CurrentContext.TestDirectory, "load-metric-first.csv");
            string second = Path.Combine(TestContext.CurrentContext.TestDirectory, "load-metric-second.csv");
            try
            {
                var options = new Dictionary<string, string>
                {
                    ["--map-seed"] = "1",
                    ["--ticks"] = "2400",
                    ["--west-preset"] = "maintain",
                    ["--east-preset"] = "maintain",
                    ["--out"] = first
                };
                Assert.That(LoadMetricCommand.Run(options), Is.EqualTo(0));
                options["--out"] = second;
                Assert.That(LoadMetricCommand.Run(options), Is.EqualTo(0));
                Assert.That(File.ReadAllBytes(first), Is.EqualTo(File.ReadAllBytes(second)));
            }
            finally
            {
                if (File.Exists(first)) File.Delete(first);
                if (File.Exists(second)) File.Delete(second);
            }
        }

        [Test]
        public void CoreDefenceOptionsOverrideOnlySpecifiedValuesWhenEnabled()
        {
            var scenario = LoadMetricCommand.CreateScenario(1, false);
            LoadMetricCommand.ApplyCoreDefenceOptions(scenario, new Dictionary<string, string>
            {
                ["--core-defence"] = "true",
                ["--core-defence-damage"] = "12",
                ["--core-defence-targets"] = "1",
                ["--core-defence-range"] = "8",
                ["--core-defence-interval"] = "40"
            });
            Assert.That(scenario.Economy.CoreDefenceDamage, Is.EqualTo(12));
            Assert.That(scenario.Economy.CoreDefenceMaxTargets, Is.EqualTo(1));
            Assert.That(scenario.Economy.CoreDefenceRange, Is.EqualTo(8));
            Assert.That(scenario.Economy.CoreDefenceIntervalTicks, Is.EqualTo(40));
        }

        [Test]
        public void CoreDefenceOptionsAreIgnoredWhenDefenceIsDisabled()
        {
            var scenario = LoadMetricCommand.CreateScenario(1, false);
            int damage = scenario.Economy.CoreDefenceDamage;
            LoadMetricCommand.ApplyCoreDefenceOptions(scenario, new Dictionary<string, string>
            {
                ["--core-defence-damage"] = "12"
            });
            Assert.That(scenario.Economy.CoreDefenceDamage, Is.EqualTo(damage));
        }

        [Test]
        public void CoreDefenceOptionsLeaveUnspecifiedDefaultsUnchanged()
        {
            var scenario = LoadMetricCommand.CreateScenario(1, false);
            int targets = scenario.Economy.CoreDefenceMaxTargets;
            int range = scenario.Economy.CoreDefenceRange;
            int interval = scenario.Economy.CoreDefenceIntervalTicks;
            LoadMetricCommand.ApplyCoreDefenceOptions(scenario, new Dictionary<string, string>
            {
                ["--core-defence"] = "true",
                ["--core-defence-damage"] = "12"
            });
            Assert.That(scenario.Economy.CoreDefenceDamage, Is.EqualTo(12));
            Assert.That(scenario.Economy.CoreDefenceMaxTargets, Is.EqualTo(targets));
            Assert.That(scenario.Economy.CoreDefenceRange, Is.EqualTo(range));
            Assert.That(scenario.Economy.CoreDefenceIntervalTicks, Is.EqualTo(interval));
        }

        [Test]
        public void LoadObservationLeavesThePerTickStateHashesEqualToRecord()
        {
            const long ticks = 3000;
            var scenario = LoadMetricCommand.CreateScenario(1, false);
            var inputs = PolicyPresets.RecordedInputs(scenario, "maintain", "maintain", ticks);
            var expected = new List<string>();
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, inputs, ticks, new BuildIdentity(), (state, hash, events) =>
                    expected.Add(Convert.ToHexString(hash)));
            }

            var actual = new List<string>();
            LoadMetricCommand.RunScenario(scenario, ticks, "maintain", "maintain", (tick, simulation) =>
                actual.Add(Convert.ToHexString(ReplayBinary.Hash(simulation.CaptureDiagnostic().CanonicalState))));

            Assert.That(actual, Is.EqualTo(expected.Skip(1).ToList()));
        }

        [Test]
        public void DifferenceCountsDisplayOnlyEconomyAndObservationEvents()
        {
            var old = Frame(1, 1, Economy(
                new[] { new VillagerView(1, true, default, VillagerActivity.Idle, 0, 0, 40) },
                new[] { new BuildingView(1, 1, BuildingKind.Barracks, default, 6, 100, 1000, false, 1, 400, 1, 50) },
                new[] { new ResourceView(1, ResourceKind.Food, default, 1) }),
                contacts: Array.Empty<EnemyContact>(), owner: 2);
            var current = Frame(1, 2, Economy(
                new[]
                {
                    new VillagerView(1, true, default, VillagerActivity.Idle, 0, 0, 40),
                    new VillagerView(2, true, default, VillagerActivity.Idle, 0, 0, 40)
                },
                new[] { new BuildingView(1, 1, BuildingKind.Barracks, default, 6, 1000, 1000, true, 400, 400, 0, 0) },
                Array.Empty<ResourceView>()),
                contacts: new[] { new EnemyContact(7, default, 2, 1, 1, true) }, owner: 1);

            var delta = LoadMetricDetector.Difference(old, current);
            Assert.That(delta.VillagerIdle, Is.EqualTo(0));
            Assert.That(delta.BuildingDone, Is.EqualTo(1));
            Assert.That(delta.NodeExhausted, Is.EqualTo(1));
            Assert.That(delta.NewContact, Is.EqualTo(1));
            Assert.That(delta.OutpostOwnerChanged, Is.EqualTo(1));
        }

        [Test]
        public void VillagerIdleCountsOnlyAfterAContinuousIdledStreakAndUsesVillagerId()
        {
            var detector = new LoadMetricDetector(20);
            int idleCount = 0;

            idleCount += detector.Observe(Frame(1, 1, Economy(
                new[]
                {
                    Villager(11, VillagerActivity.Idle),
                    Villager(22, VillagerActivity.ToResource)
                }, Array.Empty<BuildingView>(), Array.Empty<ResourceView>()), Array.Empty<EnemyContact>(), 1)).VillagerIdle;
            idleCount += detector.Observe(Frame(1, 2, Economy(
                new[]
                {
                    Villager(22, VillagerActivity.ToResource),
                    Villager(11, VillagerActivity.ToResource)
                }, Array.Empty<BuildingView>(), Array.Empty<ResourceView>()), Array.Empty<EnemyContact>(), 1)).VillagerIdle;
            Assert.That(idleCount, Is.EqualTo(0));

            for (long tick = 3; tick <= 21; tick++)
                idleCount += detector.Observe(Frame(1, tick, Economy(
                    new[]
                    {
                        Villager(22, VillagerActivity.ToResource),
                        Villager(11, VillagerActivity.Idle)
                    }, Array.Empty<BuildingView>(), Array.Empty<ResourceView>()), Array.Empty<EnemyContact>(), 1)).VillagerIdle;

            Assert.That(idleCount, Is.EqualTo(0));
            idleCount += detector.Observe(Frame(1, 22, Economy(
                new[]
                {
                    Villager(11, VillagerActivity.Idle),
                    Villager(22, VillagerActivity.ToResource)
                }, Array.Empty<BuildingView>(), Array.Empty<ResourceView>()), Array.Empty<EnemyContact>(), 1)).VillagerIdle;
            Assert.That(idleCount, Is.EqualTo(1));

            idleCount += detector.Observe(Frame(1, 23, Economy(
                new[]
                {
                    Villager(22, VillagerActivity.ToResource),
                    Villager(11, VillagerActivity.ToResource)
                }, Array.Empty<BuildingView>(), Array.Empty<ResourceView>()), Array.Empty<EnemyContact>(), 1)).VillagerIdle;
            for (long tick = 24; tick <= 43; tick++)
                idleCount += detector.Observe(Frame(1, tick, Economy(
                    new[]
                    {
                        Villager(22, VillagerActivity.ToResource),
                        Villager(11, VillagerActivity.Idle)
                    }, Array.Empty<BuildingView>(), Array.Empty<ResourceView>()), Array.Empty<EnemyContact>(), 1)).VillagerIdle;

            Assert.That(idleCount, Is.EqualTo(2));
        }

        [Test]
        public void IdleTicksCanBeConfigured()
        {
            var detector = new LoadMetricDetector(2);
            Assert.That(detector.Observe(Frame(1, 1, Economy(
                new[] { Villager(11, VillagerActivity.Idle) }, Array.Empty<BuildingView>(), Array.Empty<ResourceView>()),
                Array.Empty<EnemyContact>(), 1)).VillagerIdle, Is.EqualTo(0));
            Assert.That(detector.Observe(Frame(1, 2, Economy(
                new[] { Villager(11, VillagerActivity.Idle) }, Array.Empty<BuildingView>(), Array.Empty<ResourceView>()),
                Array.Empty<EnemyContact>(), 1)).VillagerIdle, Is.EqualTo(1));
        }

        [Test]
        public void CoreAttackCountsOneStartAndRestartsAfterTenSecondsWithoutDamage()
        {
            var detector = new LoadMetricDetector();
            int attacks = 0;
            attacks += detector.Observe(Frame(1, 1, Economy(Array.Empty<VillagerView>(), Array.Empty<BuildingView>(), Array.Empty<ResourceView>()), Array.Empty<EnemyContact>(), 1, 6000)).CoreAttacked;
            attacks += detector.Observe(Frame(1, 2, Economy(Array.Empty<VillagerView>(), Array.Empty<BuildingView>(), Array.Empty<ResourceView>()), Array.Empty<EnemyContact>(), 1, 5990)).CoreAttacked;
            for (long tick = 3; tick <= 202; tick++)
                attacks += detector.Observe(Frame(1, tick, Economy(Array.Empty<VillagerView>(), Array.Empty<BuildingView>(), Array.Empty<ResourceView>()), Array.Empty<EnemyContact>(), 1, 5990)).CoreAttacked;
            attacks += detector.Observe(Frame(1, 203, Economy(Array.Empty<VillagerView>(), Array.Empty<BuildingView>(), Array.Empty<ResourceView>()), Array.Empty<EnemyContact>(), 1, 5980)).CoreAttacked;
            Assert.That(attacks, Is.EqualTo(2));
        }

        private static VillagerView Villager(uint id, VillagerActivity activity)
            => new VillagerView(id, true, default, activity, 0, 0, 40);

        private static EconomyView Economy(IReadOnlyList<VillagerView> villagers, IReadOnlyList<BuildingView> buildings,
            IReadOnlyList<ResourceView> resources)
            => new EconomyView(100, 100, villagers.Count, 60, 0, 0, true, 3, 150, 50, 50, 20,
                villagers, buildings, resources);

        private static FactionFrame Frame(uint faction, long tick, EconomyView economy,
            IReadOnlyList<EnemyContact> contacts, uint owner, int coreHp = 6000)
        {
            var objectives = new[]
            {
                new KnownObjective(GoalKind.Core, faction, default, true, faction, true, coreHp, tick),
                new KnownObjective(GoalKind.Outpost, 1, default, true, owner, false, 0, tick)
            };
            var observation = new FactionObservation(faction, tick, Array.Empty<OwnArmyView>(), Array.Empty<VisibleEnemy>(),
                contacts, objectives);
            return new FactionFrame(tick, faction, Array.Empty<RenderUnit>(), observation, Array.Empty<CommandView>(),
                Array.Empty<GameEvent>(), new FogView(Array.Empty<bool>(), Array.Empty<bool>()),
                new MatchResult(false, 0, false, false, true), economy: economy);
        }
    }
}
