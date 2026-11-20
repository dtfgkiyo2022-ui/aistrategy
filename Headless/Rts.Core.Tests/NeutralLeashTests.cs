using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class NeutralLeashTests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static WorldState World(Battle battle)
            => (WorldState)typeof(Battle).GetField("world", PrivateInstance).GetValue(battle);

        private static void Invoke(Battle battle, string method, params object[] args)
            => typeof(Battle).GetMethod(method, PrivateInstance).Invoke(battle, args);

        private static SimPoint Offset(SimPoint point, int metres)
            => new SimPoint(Fix64.FromRaw(point.X.Raw + Fix64.FromInt(metres).Raw), point.Z);

        private static NeutralState Activate(WorldState world, int metresFromHome = 0)
        {
            var neutral = world.Neutrals[0];
            neutral.Alive = true;
            neutral.Hp = neutral.MaxHp = 1000;
            neutral.Position = Offset(neutral.Home, metresFromHome);
            neutral.LastHitFaction = 1;
            neutral.LastHitSoldierId = 1;
            neutral.LastHitTick = world.Tick;
            neutral.LastCombatTick = world.Tick;
            world.Neutrals[0] = neutral;
            var soldier = world.Soldiers[0];
            soldier.Alive = true;
            soldier.Hp = Math.Max(1, soldier.Hp);
            soldier.Position = neutral.Position;
            world.Soldiers[0] = soldier;
            return neutral;
        }

        [Test]
        public void LiveRulesAndExtension128RoundTripLeashSettings()
        {
            var on = LiveGameRules.Create(1, true, true, false, false);
            Assert.That(on.Economy.NeutralLeash, Is.True);
            Assert.That(on.NeutralRules.NeutralLeashAreaMeters, Is.EqualTo(16));
            Assert.That(on.NeutralRules.NeutralLeashPatienceTicks, Is.EqualTo(80));
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(on));
            Assert.That(decoded.Economy.NeutralLeash, Is.True);
            Assert.That(decoded.NeutralRules.NeutralLeashAreaMeters, Is.EqualTo(16));
            Assert.That(decoded.NeutralRules.NeutralLeashPatienceTicks, Is.EqualTo(80));
            Assert.That(decoded.Extensions.Single(x => x.Id == 128).Data.Length, Is.EqualTo(9));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(ScenarioBinary.Encode(on)));

            var off = LiveGameRules.Create(1, true, true, false, false, neutralLeashOff: true);
            Assert.That(off.Economy.NeutralLeash, Is.False);
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(off)).Extensions.Any(x => x.Id == 128), Is.False);
        }

        [Test]
        public void PatienceReachesReturnAt80AndDecreasesInsideTerritory()
        {
            var battle = new Battle(AggroOff(LiveGameRules.Create(1, true, true, false, false)));
            var world = World(battle);
            Activate(world, 20);
            for (int tick = 1; tick <= 79; tick++)
            {
                world.Tick = tick;
                var hit = world.Neutrals[0]; hit.LastCombatTick = tick; hit.LastHitTick = tick; world.Neutrals[0] = hit;
                Invoke(battle, "NeutralMoveStep");
            }
            Assert.That(world.Neutrals[0].LeashPatience, Is.EqualTo(79));
            Assert.That(world.Neutrals[0].Returning, Is.False);
            world.Tick = 80;
            var finalHit = world.Neutrals[0]; finalHit.LastCombatTick = 80; finalHit.LastHitTick = 80; world.Neutrals[0] = finalHit;
            Invoke(battle, "NeutralMoveStep");
            Assert.That(world.Neutrals[0].LeashPatience, Is.EqualTo(0));
            Assert.That(world.Neutrals[0].Returning, Is.True);
            var neutral = world.Neutrals[0];
            neutral.Position = neutral.Home;
            neutral.Returning = false;
            neutral.LeashPatience = 10;
            neutral.LastHitFaction = 1;
            neutral.LastHitSoldierId = 1;
            neutral.LastHitTick = 81;
            neutral.LastCombatTick = 81;
            world.Neutrals[0] = neutral;
            world.Tick = 81;
            Invoke(battle, "NeutralMoveStep");
            Assert.That(world.Neutrals[0].LeashPatience, Is.EqualTo(9));
        }

        [Test]
        public void TerritoryKeepsChasingAndDoesNotHealWhileLegacyOffReturnsAt12Metres()
        {
            var onBattle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var onWorld = World(onBattle);
            Activate(onWorld);
            var onNeutral = onWorld.Neutrals[0];
            onNeutral.Hp = 1;
            onNeutral.LastCombatTick = onWorld.Tick;
            onWorld.Soldiers[0].Position = Offset(onNeutral.Home, 15);
            onWorld.Neutrals[0] = onNeutral;
            Invoke(onBattle, "NeutralMoveStep");
            Assert.That(onWorld.Neutrals[0].Returning, Is.False);
            Assert.That(onWorld.Neutrals[0].Hp, Is.EqualTo(1));

            var offBattle = new Battle(LiveGameRules.Create(1, true, true, false, false, neutralLeashOff: true));
            var offWorld = World(offBattle);
            Activate(offWorld);
            var offNeutral = offWorld.Neutrals[0];
            offNeutral.Hp = 1;
            offNeutral.LastCombatTick = offWorld.Tick;
            offWorld.Soldiers[0].Position = Offset(offNeutral.Home, 15);
            offWorld.Neutrals[0] = offNeutral;
            Invoke(offBattle, "NeutralMoveStep");
            Assert.That(offWorld.Neutrals[0].Returning, Is.True);
        }

        [Test]
        public void ReturningNeutralBlocksNormalSkillAndBurnDamageAndArrivesFull()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle);
            Activate(world);
            var neutral = world.Neutrals[0];
            neutral.Returning = true;
            neutral.Hp = 100;
            neutral.Position = neutral.Home;
            neutral.BurnDamage = 4;
            neutral.BurnFaction = 1;
            neutral.BurnNextTick = 0;
            neutral.BurnUntil = 100;
            world.Neutrals[0] = neutral;
            var soldier = world.Soldiers[0];
            soldier.Position = neutral.Position;
            soldier.TargetKind = 7;
            soldier.TargetId = neutral.Id;
            world.Soldiers[0] = soldier;
            world.Tick = 1;
            Invoke(battle, "AttackNeutralFromSoldier", 0);
            Assert.That(world.Neutrals[0].Hp, Is.EqualTo(100));
            var skill = new SkillDefinition { Range = Fix64.FromInt(4) };
            var effect = new SkillEffect { Kind = SkillEffectKind.Damage, DamageType = SkillDamageType.Physical, Base = 50 };
            Assert.That(InvokeSkill(battle, 0, skill, effect), Is.True);
            Invoke(battle, "NeutralEffectStep");
            Assert.That(world.Neutrals[0].Hp, Is.EqualTo(100));
            Assert.That(battle.NeutralLeashBlockedDamageFor(1), Is.GreaterThan(0));

            neutral = world.Neutrals[0];
            neutral.Position = Offset(neutral.Home, 3);
            neutral.Hp = 100;
            world.Neutrals[0] = neutral;
            Invoke(battle, "NeutralMoveStep");
            Invoke(battle, "NeutralMoveStep");
            Assert.That(world.Neutrals[0].Returning, Is.False);
            Assert.That(world.Neutrals[0].Hp, Is.EqualTo(world.Neutrals[0].MaxHp));
        }

        private static bool InvokeSkill(Battle battle, int caster, SkillDefinition skill, SkillEffect effect)
            => (bool)typeof(Battle).GetMethod("ApplySkillEffectToNeutral", PrivateInstance).Invoke(battle, new object[] { caster, skill, effect });

        [Test]
        public void ContractsPublishTerritoryGaugeAndReturningState()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle);
            var neutral = Activate(world);
            neutral.LeashPatience = 40;
            neutral.Returning = true;
            world.Neutrals[0] = neutral;
            Array.Fill(world.Factions[0].VisibleCells, true);
            Invoke(battle, "PublishFrames");
            var visible = battle.Capture(1).VisibleNeutrals.Single(x => x.Id == neutral.Id);
            Assert.That(visible.LeashPatiencePermille, Is.EqualTo(500));
            Assert.That(visible.Returning, Is.True);
            Assert.That(battle.Capture(1).NeutralCamps[0].LeashMeters, Is.EqualTo(16));
        }

        // 79章の旧来の動き（86章の旗オフ）を確かめる試験
        private static ScenarioDefinition AggroOff(ScenarioDefinition scenario)
        { scenario.Economy.NeutralAggro = false; return scenario; }
    }
}
