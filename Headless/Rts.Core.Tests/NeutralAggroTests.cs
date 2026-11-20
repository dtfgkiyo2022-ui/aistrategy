using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class NeutralAggroTests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static WorldState World(Battle battle)
            => (WorldState)typeof(Battle).GetField("world", PrivateInstance).GetValue(battle);

        private static void Invoke(Battle battle, string method, params object[] args)
            => typeof(Battle).GetMethod(method, PrivateInstance).Invoke(battle, args);

        private static Battle NewBattle(bool aggro = true)
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            scenario.Economy.NeutralAggro = aggro;
            return new Battle(scenario);
        }

        private static void PrepareTarget(WorldState world, int index, uint neutralId, SimPoint position)
        {
            var soldier = world.Soldiers[index];
            soldier.Alive = true;
            soldier.Hp = Math.Max(1, soldier.Hp);
            soldier.Position = position;
            soldier.TargetKind = 7;
            soldier.TargetId = neutralId;
            world.Soldiers[index] = soldier;
        }

        [Test]
        public void AlternatingHitsKeepTheFirstAggroTarget()
        {
            var battle = NewBattle();
            var world = World(battle);
            var neutral = world.Neutrals[0];
            neutral.Alive = true; neutral.Position = neutral.Home; neutral.Hp = neutral.MaxHp;
            world.Neutrals[0] = neutral;
            PrepareTarget(world, 0, neutral.Id, neutral.Home);
            PrepareTarget(world, 1, neutral.Id, neutral.Home);

            Invoke(battle, "AttackNeutralFromSoldier", 0);
            uint first = world.Neutrals[0].AggroSoldierId;
            Invoke(battle, "AttackNeutralFromSoldier", 1);

            Assert.That(first, Is.EqualTo(world.Soldiers[0].Initial.Id));
            Assert.That(world.Neutrals[0].AggroSoldierId, Is.EqualTo(first));
            Assert.That(world.Neutrals[0].LastHitSoldierId, Is.EqualTo(world.Soldiers[1].Initial.Id));
        }

        [Test]
        public void TargetInsideAttackRangeDoesNotMoveTheBeast()
        {
            var battle = NewBattle();
            var world = World(battle);
            var neutral = world.Neutrals[0];
            neutral.Alive = true; neutral.Position = neutral.Home; neutral.Hp = neutral.MaxHp;
            neutral.AggroSoldierId = world.Soldiers[0].Initial.Id;
            world.Neutrals[0] = neutral;
            PrepareTarget(world, 0, neutral.Id, neutral.Home);
            world.Tick = 1;
            Invoke(battle, "NeutralMoveStep");
            Assert.That(world.Neutrals[0].Position, Is.EqualTo(neutral.Position));
        }

        [Test]
        public void RespawnTickIsShownOnlyToTheClearingFaction()
        {
            var battle = NewBattle();
            var world = World(battle);
            int campIndex = 0;
            var camp = world.NeutralCamps[campIndex];
            foreach (uint id in camp.NeutralIds)
            {
                var neutral = world.Neutrals[id - 1]; neutral.Alive = false; world.Neutrals[id - 1] = neutral;
            }
            Invoke(battle, "ResolveNeutralCampReward", campIndex, 1u, camp.Position);
            Assert.That(world.NeutralCamps[campIndex].LastClearFaction, Is.EqualTo(1));
            Invoke(battle, "PublishFrames");
            var west = battle.Capture(1).NeutralCamps.Single(x => x.Id == camp.Id);
            var east = battle.Capture(2).NeutralCamps.Single(x => x.Id == camp.Id);
            Assert.That(west.RespawnTick, Is.GreaterThan(0));
            Assert.That(east.RespawnTick, Is.EqualTo(0));
        }

        [Test]
        public void FlagOffDoesNotWriteExtension136()
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            scenario.Economy.NeutralAggro = false;
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(scenario));
            Assert.That(decoded.Economy.NeutralAggro, Is.False);
            Assert.That(decoded.Extensions.Any(x => x.Id == 136), Is.False);
        }
    }
}
