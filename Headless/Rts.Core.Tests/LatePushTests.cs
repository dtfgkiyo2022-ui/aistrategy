using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class LatePushTests
    {
        [Test]
        public void DisabledLatePushKeepsBytesAndStateUnchanged()
        {
            var baseline = MapGenerator.GenerateTerrain(1);
            var disabled = MapGenerator.GenerateTerrain(1);
            disabled.Economy.LatePush = false;
            Assert.That(ScenarioBinary.Encode(disabled), Is.EqualTo(ScenarioBinary.Encode(baseline)));
            var left = new Battle(disabled); var right = new Battle(baseline);
            for (long tick = 1; tick <= 200; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void EachLatePushConditionHasItsBoundary()
        {
            var rules = new EconomyRules { LatePush = true, LatePushAfterTicks = 14400, LatePushAdvantageAfterTicks = 9600,
                LatePushEnemyMultiplierPermille = 1500 };
            Assert.That(Battle.LatePushCondition(Observation(14399, 2, 2, 1), rules), Is.False);
            Assert.That(Battle.LatePushCondition(Observation(14400, 0, 0, 0), rules), Is.True);
            Assert.That(Battle.LatePushCondition(Observation(9599, 3, 1, 1), rules), Is.False);
            // No visible enemy is not an advantage (the fog hides them).
            Assert.That(Battle.LatePushCondition(Observation(9600, 3, 0, 0), rules), Is.False);
            Assert.That(Battle.LatePushCondition(Observation(9599, 3, 0, 0), rules), Is.False);
            Assert.That(Battle.LatePushCondition(Observation(9600, 3, 2, 1), rules), Is.True);
            Assert.That(Battle.LatePushCondition(Observation(9600, 2, 2, 0), rules), Is.False);
            Assert.That(Battle.LatePushCondition(Observation(9600, 3, 2, 0), rules), Is.True);
        }

        [Test]
        public void LatePushCreatesCoreFocusAndHumanWins()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.LatePush = true;
            scenario.Economy.LatePushAfterTicks = 1;
            scenario.Economy.LatePushAdvantageAfterTicks = 100000;
            var sim = new Battle(scenario);
            sim.Step(1, Array.Empty<ScheduledInput>());
            sim.Step(2, Array.Empty<ScheduledInput>());
            var automatic = sim.Capture(1).Commands.FirstOrDefault(c => c.Source == CommandSource.Doctrine
                && c.Kind == PolicyKind.Focus && c.Goal.Kind == GoalKind.Core && c.Status == CommandStatus.Executing);
            Assert.That(automatic.CommandId, Is.Not.Zero);

            var target = new ScopeKey(1, ScopeKind.All, 0);
            var humanOrder = new PolicyOrder(1, 0, CommandSource.Human, target, PolicyKind.Focus,
                new PolicyGoal(GoalKind.Core, 2, default), 1, new LossBudget(300), new EndCondition(EndKind.UntilReplaced, 0), 0,
                0, Array.Empty<PolicyVersion>(), 2, new Expiration(long.MaxValue, 0, ExpireFlags.None));
            Rts.Tests.EditMode.CommandTestInput.Step(sim, 3, new[] { new ScheduledInput(1, InputKind.Proposal, 2, 3, 1, 1, new[] { humanOrder }) });
            Assert.That(sim.Capture(1).Commands.Any(c => c.Source == CommandSource.Human && c.Kind == PolicyKind.Focus
                && c.Status == CommandStatus.Executing), Is.True);
        }

        [Test]
        public void LatePushExtensionRoundTripsAndReplayIsDeterministic()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.LatePush = true;
            scenario.Economy.LatePushAfterTicks = 12000;
            scenario.Economy.LatePushAdvantageAfterTicks = 9000;
            scenario.Economy.LatePushEnemyMultiplierPermille = 1500;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.LatePush, Is.True);
            Assert.That(decoded.Economy.LatePushAfterTicks, Is.EqualTo(12000));
            Assert.That(decoded.Economy.LatePushAdvantageAfterTicks, Is.EqualTo(9000));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var left = new Battle(decoded); var right = new Battle(ScenarioBinary.Decode(bytes));
            for (long tick = 1; tick <= 20000; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void PushWavesExtensionId18RoundTripsItsTuningValues()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            var e = scenario.Economy;
            e.LatePush = true; e.SiegeByAge = true; e.PushWaves = true;
            e.WaveStagingDistance = 31; e.WaveSize = 27; e.WaveMaxWaitTicks = 1234;
            e.WaveMinSize = 8; e.WaveRetreatPermille = 375;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.PushWaves, Is.True);
            Assert.That(decoded.Economy.WaveStagingDistance, Is.EqualTo(31));
            Assert.That(decoded.Economy.WaveSize, Is.EqualTo(27));
            Assert.That(decoded.Economy.WaveMaxWaitTicks, Is.EqualTo(1234));
            Assert.That(decoded.Economy.WaveMinSize, Is.EqualTo(8));
            Assert.That(decoded.Economy.WaveRetreatPermille, Is.EqualTo(375));
            Assert.That(decoded.Extensions.Any(x => x.Id == 18), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void PushWavesWaitThenReplaceStagingFocusWithCoreFocus()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            var e = scenario.Economy;
            e.LatePush = true; e.SiegeByAge = true; e.PushWaves = true; e.AgeClock = true;
            e.LatePushAfterTicks = 1; e.LatePushAdvantageAfterTicks = 100000;
            e.WaveSize = 1000; e.WaveMinSize = 6; e.WaveMaxWaitTicks = 20;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 1000000;
            var sim = new Battle(scenario);
            bool sawStaging = false, sawAssault = false;
            for (long tick = 1; tick <= 16000; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                var commands = sim.Capture(1).Commands;
                sawStaging |= commands.Any(c => c.Source == CommandSource.Doctrine && c.Kind == PolicyKind.Focus
                    && c.Goal.Kind == GoalKind.Point && c.Status == CommandStatus.Executing);
                sawAssault |= commands.Any(c => c.Source == CommandSource.Doctrine && c.Kind == PolicyKind.Focus
                    && c.Goal.Kind == GoalKind.Core && c.Status == CommandStatus.Executing);
            }
            Assert.That(sawStaging, Is.True);
            Assert.That(sawAssault, Is.True);
        }

        [Test]
        public void PushWavesDoNotAssaultBeforeSizeAndTimeoutStartsTheWave()
        {
            var scenario = WaveScenario();
            scenario.Economy.WaveSize = 20;
            scenario.Economy.WaveMinSize = 6;
            scenario.Economy.WaveMaxWaitTicks = 5;
            var sim = new Battle(scenario);
            var events = new List<GameEvent>();
            for (long tick = 1; tick <= 10; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                events.AddRange(WaveEvents(sim));
                long gathering = events.Where(e => e.Value == 1).Select(e => e.Tick).DefaultIfEmpty(long.MaxValue).Min();
                if (tick < gathering + scenario.Economy.WaveMaxWaitTicks)
                    Assert.That(events.Any(e => e.Value == 2), Is.False, "tick " + tick);
            }
            Assert.That(events.Any(e => e.Value == 1), Is.True, "gathering was not reported");
            Assert.That(events.Any(e => e.Value == 2), Is.True,
                "timeout did not start an eligible wave: " + string.Join(",", events.Select(e => e.Tick + ":" + e.Value)));
        }

        [Test]
        public void PushWavesStartInAgeTwoWhenTheLatePushLatchFires()
        {
            var scenario = WaveScenario();
            scenario.Economy.AgeClockTicks1 = 1000;
            scenario.Economy.AgeClockTicks2 = 2000;
            scenario.Economy.AgeClockTicks3 = 3000;
            scenario.Economy.WaveSize = 1000;
            var sim = new Battle(scenario);
            var events = new List<GameEvent>();
            for (long tick = 1; tick <= 4; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                events.AddRange(WaveEvents(sim));
            }
            Assert.That(sim.Capture(1).Economy.Age, Is.LessThan(3));
            Assert.That(events.Any(e => e.Value == 1), Is.True,
                "the late-push latch must start gathering before age 3: "
                + string.Join(",", events.Select(e => e.Tick + ":" + e.Value)));
        }

        [Test]
        public void PushWavesAssaultKeepsNormalSoldierEngagement()
        {
            var scenario = WaveScenario();
            scenario.Economy.AgeClockTicks1 = 1000;
            scenario.Economy.AgeClockTicks2 = 2000;
            scenario.Economy.AgeClockTicks3 = 3000;
            scenario.Economy.WaveSize = 1;
            scenario.Economy.WaveMinSize = 1;
            SetAdjacentCombatants(scenario);
            var sim = new Battle(scenario);
            for (long tick = 1; tick <= 3; tick++) sim.Step(tick, Array.Empty<ScheduledInput>());

            var phase = DiagnosticComparison.Fields(sim.CaptureDiagnostic())
                .First(p => p.Key == "PushWaves.Faction1.Phase").Value;
            Assert.That(phase, Is.EqualTo("2"), "the pair must be in assault");
            Assert.That(sim.Capture(1).Units.Any(u => u.IsOwn && u.IsAttacking), Is.True,
                "an assault soldier in range must attack the enemy soldier, not only advance to the core");
        }

        [Test]
        public void PushWavesGatheringAlsoFightsAUnitThatComesIntoRange()
        {
            var scenario = WaveScenario();
            scenario.Economy.AgeClockTicks1 = 1000;
            scenario.Economy.AgeClockTicks2 = 2000;
            scenario.Economy.AgeClockTicks3 = 3000;
            scenario.Economy.WaveSize = 1000;
            SetAdjacentCombatants(scenario);
            var sim = new Battle(scenario);
            bool sawGatheringAttack = false;
            for (long tick = 1; tick <= 3; tick++) sim.Step(tick, Array.Empty<ScheduledInput>());
            for (long tick = 4; tick <= 25; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                var fields = DiagnosticComparison.Fields(sim.CaptureDiagnostic());
                string currentPhase = fields.First(p => p.Key == "PushWaves.Faction1.Phase").Value;
                sawGatheringAttack |= currentPhase == "1" && fields.Any(p => p.Key.StartsWith("Soldiers[", StringComparison.Ordinal)
                    && p.Key.EndsWith(".TargetKind", StringComparison.Ordinal) && p.Value == "1");
            }
            var phase = DiagnosticComparison.Fields(sim.CaptureDiagnostic())
                .First(p => p.Key == "PushWaves.Faction1.Phase").Value;
            Assert.That(phase, Is.EqualTo("1"), "the pair must still be gathering");
            Assert.That(sawGatheringAttack, Is.True,
                "gathering soldiers must fight an enemy that reaches the engagement range");
        }

        [Test]
        public void PushWavesKeepWaitingWhenTheTimedOutWaveIsBelowMinimum()
        {
            var scenario = WaveScenario();
            scenario.Economy.WaveSize = 20;
            scenario.Economy.WaveMinSize = 20;
            scenario.Economy.WaveMaxWaitTicks = 5;
            var sim = new Battle(scenario);
            var events = new List<GameEvent>();
            for (long tick = 1; tick <= 12; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                events.AddRange(WaveEvents(sim));
            }
            Assert.That(events.Any(e => e.Value == 1), Is.True, "gathering was not reported");
            Assert.That(events.Any(e => e.Value == 2), Is.False, "a wave below WaveMinSize assaulted");
        }

        [Test]
        public void PushWavesRetreatAndReturnToGatheringAcrossMultipleTicks()
        {
            var scenario = WaveScenario();
            scenario.Economy.WaveSize = 6;
            scenario.Economy.WaveMinSize = 6;
            scenario.Economy.WaveMaxWaitTicks = 100;
            scenario.Economy.WaveRetreatPermille = 1000;
            var sim = new Battle(scenario);
            var events = new List<GameEvent>();
            for (long tick = 1; tick <= 320; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                events.AddRange(WaveEvents(sim));
            }
            var gathering = events.Where(e => e.Value == 1).Select(e => e.Tick).ToArray();
            var assault = events.Where(e => e.Value == 2).Select(e => e.Tick).ToArray();
            var reforming = events.Where(e => e.Value == 3).Select(e => e.Tick).ToArray();
            Assert.That(gathering.Length, Is.GreaterThanOrEqualTo(2), string.Join(",", events.Select(e => e.Tick + ":" + e.Value)));
            Assert.That(assault.Length, Is.GreaterThanOrEqualTo(1), string.Join(",", events.Select(e => e.Tick + ":" + e.Value)));
            Assert.That(reforming.Length, Is.GreaterThanOrEqualTo(1), string.Join(",", events.Select(e => e.Tick + ":" + e.Value)));
            Assert.That(assault[0], Is.GreaterThanOrEqualTo(gathering[0]));
            Assert.That(reforming[0], Is.GreaterThan(assault[0]));
            Assert.That(gathering[1], Is.GreaterThan(reforming[0]));
            Assert.That(gathering[1] - reforming[0], Is.GreaterThan(1));
        }

        [Test]
        public void PushWavesRecordTheAssaultCountInDiagnosticState()
        {
            var scenario = WaveScenario();
            scenario.Economy.WaveSize = 6;
            scenario.Economy.WaveMinSize = 6;
            var sim = new Battle(scenario);
            for (long tick = 1; tick <= 4; tick++) sim.Step(tick, Array.Empty<ScheduledInput>());
            var fields = DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);
            Assert.That(fields.ContainsKey("PushWaves.Faction1.AssaultStartCount"), Is.True);
            Assert.That(int.Parse(fields["PushWaves.Faction1.AssaultStartCount"]), Is.GreaterThanOrEqualTo(6),
                string.Join(",", fields.Where(p => p.Key.StartsWith("PushWaves.Faction1", StringComparison.Ordinal))
                    .Select(p => p.Key + "=" + p.Value)));
        }

        [TestCase(CommandSource.Human)]
        [TestCase(CommandSource.Tactic)]
        public void PushWavesLeaveACommandedArmyAlone(CommandSource source)
        {
            var scenario = WaveScenario();
            scenario.Economy.WaveSize = 20;
            scenario.Economy.WaveMinSize = 6;
            scenario.Economy.WaveMaxWaitTicks = 1000;
            var sim = new Battle(scenario);
            sim.Step(1, Array.Empty<ScheduledInput>());
            sim.Step(2, Array.Empty<ScheduledInput>());
            var armyId = sim.Capture(1).Observation.OwnArmies.OrderBy(a => a.Id).First().Id;
            var target = new ScopeKey(1, ScopeKind.Army, armyId);
            ulong commandId = 1000;
            var order = new PolicyOrder(commandId, commandId, source, target, PolicyKind.Focus,
                new PolicyGoal(GoalKind.Core, 2, default), 50, new LossBudget(300),
                new EndCondition(EndKind.UntilReplaced, 0), 0, sim.Revision(target),
                sim.Versions(target).Where(v => !v.Scope.Equals(target)).ToArray(), 2,
                new Expiration(long.MaxValue, 20, ExpireFlags.ObservationTooOld));
            if (source == CommandSource.Human)
                Rts.Tests.EditMode.CommandTestInput.Step(sim, 3, new[] { new ScheduledInput(1, InputKind.Proposal, 2, 3, 1, 1, new[] { order }) });
            else
                sim.Step(3, new[] { new ScheduledInput(1, InputKind.Proposal, 2, 3, 1, 1, new[] { order }) });
            var commands = sim.Capture(1).Commands;
            Assert.That(commands.Any(c => c.Source == source && c.Target.Equals(target)
                && c.Status == CommandStatus.Executing), Is.True,
                source + ": " + string.Join(",", commands.Where(c => c.Target.Equals(target))
                    .Select(c => c.Source + "/" + c.Status + "/" + c.Reason)));
            Assert.That(commands.Any(c => c.Source == CommandSource.Doctrine && c.Target.Equals(target)
                && c.Status == CommandStatus.Executing), Is.False, source.ToString());
        }

        [Test]
        public void PushWavesReplayHasTheSameHashAtEveryTick()
        {
            var bytes = ScenarioBinary.Encode(WaveScenario());
            var left = new Battle(ScenarioBinary.Decode(bytes));
            var right = new Battle(ScenarioBinary.Decode(bytes));
            for (long tick = 1; tick <= 80; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        private static ScenarioDefinition WaveScenario()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            var e = scenario.Economy;
            e.LatePush = true;
            e.SiegeByAge = true;
            e.PushWaves = true;
            e.AgeClock = true;
            e.AgeClockTicks1 = 1;
            e.AgeClockTicks2 = 2;
            e.AgeClockTicks3 = 3;
            e.LatePushAfterTicks = 1;
            e.LatePushAdvantageAfterTicks = 100000;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 1000000;
            return scenario;
        }

        private static void SetAdjacentCombatants(ScenarioDefinition scenario)
        {
            var first = scenario.Soldiers[0];
            var second = scenario.Soldiers[20];
            second.Position = new SimPoint(first.Position.X + Fix64.FromInt(1), first.Position.Z);
            scenario.Soldiers[20] = second;
        }

        private static IEnumerable<GameEvent> WaveEvents(Battle sim)
            => sim.Capture(1).Events.Where(e => e.Kind == EventKind.AiReport && e.Reason == ReasonCode.None
                && (e.Value == 1 || e.Value == 2 || e.Value == 3));

        private static FactionObservation Observation(long tick, int own, int enemy, int ownedOutposts)
        {
            var armies = Enumerable.Range(0, own == 0 ? 0 : 1)
                .Select(i => new OwnArmyView((uint)(i + 1), 1, UnitKind.Infantry, default, own, default)).ToArray();
            var visible = Enumerable.Range(0, enemy).Select(i => new VisibleEnemy((uint)(i + 1), default, (byte)UnitKind.Infantry)).ToArray();
            var objectives = Enumerable.Range(0, 2).Select(i => new KnownObjective(GoalKind.Outpost, (uint)(i + 1), default,
                true, i < ownedOutposts ? 1U : 2U, false, 0, tick)).ToArray();
            return new FactionObservation(1, tick, armies, visible, Array.Empty<EnemyContact>(), objectives);
        }
    }
}
