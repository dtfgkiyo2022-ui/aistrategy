using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class BreachAssaultTests
    {
        [Test]
        public void StageTwoWallFallbackIsGatedByTheSoldiersFactionAndUsesABoundedSearch()
        {
            var rules = new EconomyRules { AiLevelWest = 2, AiLevelEast = 1 };
            Assert.That(rules.AiLevelAtLeast(1, 2), Is.True);
            Assert.That(rules.AiLevelAtLeast(2, 2), Is.False);

            string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "UnityProject", "Assets", "RTS",
                "Simulation", "Simulation.Raids.cs"));
            Assert.That(source, Does.Contain(
                "if (world.Config.Economy.AiLevelAtLeast(soldier.Initial.FactionId, 2)) PickReachableWall(ref soldier);"));
            Assert.That(source, Does.Contain("public const int AiLevelTwoWallSearchCells = 12;"));
        }

        [Test]
        public void StageZeroAndOneDoNotEnableTheNewBreachFallback()
        {
            var rules = new EconomyRules { AiLevelWest = 1, AiLevelEast = 0 };
            Assert.That(rules.AiLevelAtLeast(1, 2), Is.False);
            Assert.That(rules.AiLevelAtLeast(2, 2), Is.False);
        }

        [TestCase(3, 1, 13250)]
        [TestCase(7, 2, 18000)]
        public void StageTwoLatePushArmyReachesTheEnemyCore(int seed, int faction, long targetTick)
        {
            var scenario = LiveGameRules.Create((ulong)seed, true, true, false, false);
            scenario.Economy.AiLevelWest = faction == 1 ? 2 : 1;
            scenario.Economy.AiLevelEast = faction == 2 ? 2 : 1;
            scenario.VerificationTickLimit = 14000;
            var simulation = new Battle(scenario);

            for (long tick = 1; tick <= targetTick; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());

            uint factionId = (uint)faction;
            var frame = simulation.Capture(factionId);
            var enemyCore = frame.Objectives.Single(value => value.Kind == GoalKind.Core && value.OwnerFactionId != factionId);
            long radius = Fix64.FromInt(60).Raw;
            long radiusSquared = radius * radius;
            int atEnemyCore = frame.Units.Count(unit => unit.IsOwn
                && unit.Kind != UnitKind.Villager && unit.Kind != UnitKind.Scout
                && DistanceSquared(unit.Position, enemyCore.Position) <= radiusSquared);

            TestContext.WriteLine($"seed={seed} faction={faction} tick={targetTick} atEnemyCore={atEnemyCore}");
            Assert.That(atEnemyCore, Is.GreaterThan(0));
        }

        [Test]
        public void StageOneSeedOneKeepsItsHistoricalCoreOutcome()
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            scenario.Economy.AiLevelWest = 1;
            scenario.Economy.AiLevelEast = 1;
            scenario.VerificationTickLimit = 15000;
            var simulation = new Battle(scenario);

            for (long tick = 1; tick <= 15000 && !simulation.Capture(1).Result.HasEnded; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());

            Assert.That(simulation.Capture(1).Result.HasEnded, Is.True);
            Assert.That(simulation.Capture(1).Result.WinnerFactionId, Is.EqualTo(2u));
        }

        [Test]
        public void StageTwoFortifyPrioritizesStoneAndClosesTheReachableRing()
        {
            var scenario = LiveGameRules.Create(7, true, true, false, false);
            scenario.Economy.AiLevelWest = 1;
            scenario.Economy.AiLevelEast = 2;
            scenario.VerificationTickLimit = 12000;
            var simulation = new Battle(scenario);

            for (long tick = 1; tick <= 12000; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());

            var fortify = simulation.FortifyMetrics(2);
            Assert.That(fortify.WallCells + fortify.GateCells, Is.GreaterThan(70));
            Assert.That(fortify.RingGaps, Is.EqualTo(0));
        }

        [Test]
        public void StageZeroAndOneDoNotUseStageTwoFortifyStonePriority()
        {
            var scenario = LiveGameRules.Create(7, true, true, false, false);
            scenario.Economy.AiLevelWest = 0;
            scenario.Economy.AiLevelEast = 1;
            var simulation = new Battle(scenario);
            var state = (WorldState)typeof(Battle).GetField("world", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(simulation);
            state.Economies[0].Age = 1;
            state.Economies[0].FortifyRingGaps = 10;
            state.Economies[0].Stone = 0;
            var method = typeof(Battle).GetMethod("StageTwoFortifyStoneNeeded", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Assert.That((bool)method.Invoke(simulation, new object[] { 1u, 0 }), Is.False);
            Assert.That((bool)method.Invoke(simulation, new object[] { 2u, 0 }), Is.False);
        }

        [Test]
        public void StageZeroSimulationKeepsTheSameTickHashesAfterRoundTrip()
        {
            var source = LiveGameRules.Create(1, true, true, false, false);
            source.Economy.AiLevelWest = 0;
            source.Economy.AiLevelEast = 0;
            var first = new Battle(source);
            var second = new Battle(ScenarioBinary.Decode(ScenarioBinary.Encode(source)));

            for (long tick = 1; tick <= 128; tick++)
            {
                first.Step(tick, Array.Empty<ScheduledInput>());
                second.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(first.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(second.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        private static long DistanceSquared(SimPoint left, SimPoint right)
        {
            long dx = left.X.Raw - right.X.Raw;
            long dz = left.Z.Raw - right.Z.Raw;
            return checked(dx * dx + dz * dz);
        }

        private static string FindRepositoryRoot()
        {
            for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Headless", "Rts.Headless.slnx")))
                    return directory.FullName;
            throw new DirectoryNotFoundException("Cannot locate the repository source tree.");
        }
    }
}
