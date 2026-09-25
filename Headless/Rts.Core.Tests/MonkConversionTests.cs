using System;
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
    /// <summary>V3-5 #22: monks convert by removing the old soldier and spawning a fresh soldier.</summary>
    public sealed class MonkConversionTests
    {
        private static ScenarioDefinition Scenario(int conversionTicks, bool enabled, bool enemyMonk = false,
            bool enemyKillsMonk = false, int factionCap = 40)
        {
            var s = WeekOneScenario.Create();
            s.Rules.FactionCap = factionCap;
            s.Rules.CoreReinforcementIntervalTicks = 1000000;
            s.Rules.OutpostReinforcementIntervalTicks = 1000000;
            s.Outposts = Array.Empty<OutpostDefinition>();
            s.Economy.MonksEnabled = enabled;
            s.Economy.ConversionTicks = conversionTicks;
            s.UnitParameters = new[]
            {
                new UnitParameters { Kind = UnitKind.Infantry, Hp = 100, Speed = Fix64.FromInt(0), Vision = Fix64.FromInt(100),
                    Range = Fix64.FromInt(4), Damage = enemyKillsMonk ? 10 : 0, AttackIntervalTicks = 1 },
                new UnitParameters { Kind = UnitKind.Scout, Hp = 40, Speed = Fix64.FromInt(0), Vision = Fix64.FromInt(100),
                    Range = Fix64.FromInt(2), Damage = 0, AttackIntervalTicks = 1 },
                new UnitParameters { Kind = UnitKind.Monk, Hp = enemyKillsMonk ? 20 : 100, Speed = Fix64.FromInt(0), Vision = Fix64.FromInt(100),
                    Range = Fix64.FromInt(4), Damage = 0, AttackIntervalTicks = 1 }
            };
            for (int i = 0; i < s.Soldiers.Length; i++)
            {
                s.Soldiers[i].Alive = false;
                s.Soldiers[i].Hp = 0;
            }
            s.Soldiers[0] = new SoldierDefinition { Id = 1, FactionId = 1, ArmyId = 1, Kind = UnitKind.Monk,
                Alive = true, Position = new SimPoint(Fix64.FromInt(24), Fix64.FromInt(64)), Hp = enemyKillsMonk ? 20 : 100 };
            s.Soldiers[10] = new SoldierDefinition { Id = 11, FactionId = 2, ArmyId = 5,
                Kind = enemyMonk ? UnitKind.Monk : UnitKind.Infantry, Alive = true,
                Position = new SimPoint(Fix64.FromInt(26), Fix64.FromInt(64)), Hp = enemyMonk ? 100 : 100 };
            return s;
        }

        private static void Steps(CommandGateway gateway, int count)
        {
            for (int i = 0; i < count; i++) gateway.Step();
        }

        [Test]
        public void DisabledMonksDoNotConvertOrAttack()
        {
            var scenario = Scenario(3, false);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            Steps(gateway, 8);
            Assert.That(sim.Capture(2).Units.Any(u => u.Id == 11), Is.True);
            Assert.That(sim.Capture(1).Units.Any(u => u.Kind == UnitKind.Monk && u.IsAttacking), Is.False);
        }

        [Test]
        public void AContinuousMonkContactConvertsIntoAFreshSoldierOfTheSameKind()
        {
            var scenario = Scenario(4, true);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            Steps(gateway, scenario.Economy.ConversionTicks);
            Assert.That(sim.Capture(2).Units.Any(u => u.Id == 11), Is.False);
            Assert.That(sim.Capture(1).Units.Any(u => u.Id > scenario.Soldiers.Length && u.Kind == UnitKind.Infantry), Is.True);
        }

        [Test]
        public void KillingTheMonkBreaksTheConversionAndResetsProgress()
        {
            var scenario = Scenario(3, true, enemyKillsMonk: true);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            Steps(gateway, 8);
            Assert.That(sim.Capture(1).Units.Any(u => u.Kind == UnitKind.Monk), Is.False);
            Assert.That(sim.Capture(2).Units.Any(u => u.Id == 11), Is.True);
        }

        [Test]
        public void ConversionIsAllowedToFailWhenTheReceivingFactionIsAtItsCap()
        {
            var scenario = Scenario(2, true, factionCap: 1);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            Steps(gateway, 5);
            Assert.That(sim.Capture(2).Units.Any(u => u.Id == 11), Is.False);
            Assert.That(sim.Capture(1).Units.Count(u => u.Kind == UnitKind.Monk), Is.EqualTo(1));
            Assert.That(sim.Capture(1).Units.Any(u => u.Id > scenario.Soldiers.Length), Is.False);
        }

        [Test]
        public void MonksCannotConvertOtherMonks()
        {
            var scenario = Scenario(2, true, enemyMonk: true);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            Steps(gateway, 5);
            Assert.That(sim.Capture(1).Units.Any(u => u.Kind == UnitKind.Monk), Is.True);
            Assert.That(sim.Capture(2).Units.Any(u => u.Id == 11), Is.True);
        }

        [Test]
        public void ConversionReplaysDeterministically()
        {
            var scenario = Scenario(4, true);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            Steps(gateway, 8);
            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, gateway.Inputs, sim.Capture(1).Tick, build);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, build);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }
    }
}
