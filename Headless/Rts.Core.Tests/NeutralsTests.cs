using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class NeutralsTests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static WorldState World(Battle battle)
            => (WorldState)typeof(Battle).GetField("world", PrivateInstance).GetValue(battle);

        private static int Camp(WorldState world, NeutralKind kind)
            => Array.FindIndex(world.NeutralCamps, x => x.Kind == kind);

        private static void Invoke(Battle battle, string method, params object[] args)
            => typeof(Battle).GetMethod(method, PrivateInstance).Invoke(battle, args);

        [Test]
        public void LargeLiveRulesEnableDeterministicSymmetricNeutralCamps()
        {
            var left = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var right = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var a = left.Capture(1).NeutralCamps;
            var b = right.Capture(1).NeutralCamps;
            Assert.That(a.Count, Is.EqualTo(18));
            Assert.That(b.Select(x => (x.Id, x.Kind, x.Position.X.Raw, x.Position.Z.Raw)),
                Is.EqualTo(a.Select(x => (x.Id, x.Kind, x.Position.X.Raw, x.Position.Z.Raw))));
            for (int i = 0; i < a.Count; i += 2)
            {
                Assert.That(a[i].Kind, Is.EqualTo(a[i + 1].Kind));
                Assert.That(a[i].Position.X.Raw + a[i + 1].Position.X.Raw,
                    Is.EqualTo(a[0].Position.X.Raw + a[1].Position.X.Raw));
                Assert.That(a[i].Position.Z.Raw, Is.EqualTo(a[i + 1].Position.Z.Raw));
            }
        }

        [Test]
        public void NeutralsOffPublishesNoCampsAndDoesNotWriteExtension()
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            scenario.Economy.Neutrals = false;
            scenario.Extensions = Array.Empty<ScenarioExtensionData>();
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(scenario)).Economy.Neutrals, Is.False);
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(scenario)).Extensions.Any(x => x.Id == 122), Is.False);
            var simulation = new Battle(scenario);
            Assert.That(simulation.Capture(1).NeutralCamps, Is.Empty);
            Assert.That(simulation.Capture(1).VisibleNeutrals, Is.Empty);
        }

        [Test]
        public void NeutralsExtensionRoundTripsByteForByte()
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.Neutrals, Is.True);
            Assert.That(decoded.Extensions.Any(x => x.Id == 122), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void LastSeenNeutralCampMemoryIsPerFactionAndObservationIsReadOnly()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle);
            var camp = world.NeutralCamps[0];
            var neutral = world.Neutrals[camp.NeutralIds[0] - 1];
            neutral.Alive = true; neutral.Hp = 77; world.Neutrals[neutral.Id - 1] = neutral;
            Array.Clear(world.Factions[0].VisibleCells, 0, world.Factions[0].VisibleCells.Length);
            Array.Clear(world.Factions[1].VisibleCells, 0, world.Factions[1].VisibleCells.Length);
            Array.Fill(world.Factions[0].VisibleCells, true);
            Invoke(battle, "UpdateNeutralObservationMemory");
            Assert.That(world.NeutralCampMemories[0].LastKnownHp, Is.EqualTo(77));
            Invoke(battle, "PublishFrames");
            Assert.That(battle.Capture(1).NeutralCamps[0].LastKnownHp, Is.EqualTo(77));
            Assert.That(battle.Capture(2).NeutralCamps[0].LastSeenTick, Is.EqualTo(0));
            Assert.That(world.NeutralCamps[0].LastKnownHp, Is.EqualTo(0));
        }

        [Test]
        public void FlameBlessingAppliesRefreshableBurnAndSlowAndWolfRewardStaysInKillerStock()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle);
            int wolf = Camp(world, NeutralKind.WolfPack);
            int flame = Camp(world, NeutralKind.FlameHornBeast);
            int food = world.Economies[0].Food, wood = world.Economies[0].Wood;
            Invoke(battle, "ResolveNeutralCampReward", wolf, 1u, world.NeutralCamps[wolf].Position);
            Assert.That(world.Economies[0].Food, Is.EqualTo(food + 120));
            Assert.That(world.Economies[0].Wood, Is.EqualTo(wood + 60));
            world.Soldiers[0].Alive = true;
            world.Soldiers[0].Position = world.NeutralCamps[flame].Position;
            Invoke(battle, "ResolveNeutralCampReward", flame, 1u, world.NeutralCamps[flame].Position);
            Assert.That(world.NeutralSoldierBlessings[0] & NeutralBlessing.Flame, Is.Not.EqualTo(NeutralBlessing.None));
            object[] flameArgs = { world.Neutrals[0], 1u, world.Soldiers[0].Initial.Id };
            typeof(Battle).GetMethod("ApplyFlameBlessingToNeutral", PrivateInstance).Invoke(battle, flameArgs);
            world.Neutrals[0] = (NeutralState)flameArgs[0];
            Assert.That(world.Neutrals[0].BurnDamage, Is.EqualTo(4));
            Assert.That(world.Neutrals[0].SlowPermille, Is.EqualTo(200));
        }

        [Test]
        public void ShellRewardAddsFactionVisionRevealForItsDuration()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle);
            int shell = Camp(world, NeutralKind.ShellBeast);
            var point = world.NeutralCamps[shell].Position;
            Invoke(battle, "ResolveNeutralCampReward", shell, 1u, point);
            Assert.That(world.NeutralShellVisionUntil[shell], Is.EqualTo(1800));
            Assert.That((bool)typeof(Battle).GetMethod("IsVisibleTo", PrivateInstance).Invoke(battle, new object[] { 1u, point }), Is.True);
            Assert.That(world.Factions[0].VisibleCells[world.Map.Cell(point)], Is.True);
        }

        [Test]
        public void FlameBlessingAppliesBurnAndSlowToAnEnemySoldier()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle);
            world.Soldiers[0].Alive = true;
            world.Soldiers[1].Alive = true;
            world.NeutralSoldierBlessings[0] = NeutralBlessing.Flame;
            world.NeutralSoldierBlessingUntil[0] = 100;
            Invoke(battle, "ApplyFlameBlessingToSoldier", 0, 1);
            Assert.That(world.Soldiers[1].NeutralBurnDamage, Is.EqualTo(4));
            Assert.That(world.Soldiers[1].NeutralBurnUntil, Is.EqualTo(60));
            Assert.That(world.Soldiers[1].NeutralSlowPermille, Is.EqualTo(200));
            Assert.That(world.Soldiers[1].NeutralSlowUntil, Is.EqualTo(20));
        }

        [Test]
        public void ClearingAutoHuntAlsoClearsTheStaleNeutralTarget()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle);
            world.Soldiers[0].TargetKind = 7;
            world.Soldiers[0].TargetId = 1;
            world.Soldiers[0].MoveGoal = world.NeutralCamps[0].Position;
            world.NeutralOrders[0] = world.NeutralCamps[0].Id;
            world.NeutralOrderCamps[0] = 0x80000000U | world.NeutralCamps[0].Id;
            Invoke(battle, "ClearNeutralOrder", 0);
            Assert.That(world.NeutralOrders[0], Is.EqualTo(0));
            Assert.That(world.NeutralOrderCamps[0], Is.EqualTo(0));
            Assert.That(world.Soldiers[0].TargetKind, Is.EqualTo(0));
            Assert.That(world.Soldiers[0].TargetId, Is.EqualTo(0));
        }

        [Test]
        public void AzureBlessingReducesGatherIntervalByTwentyPercent()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle);
            var gather = typeof(Battle).GetMethod("GatherTicksFor", PrivateInstance);
            int before = (int)gather.Invoke(battle, new object[] { 1u, 0u });
            world.NeutralFactionBlessings[0] = NeutralBlessing.Azure;
            world.NeutralFactionBlessingUntil[0] = 100;
            int after = (int)gather.Invoke(battle, new object[] { 1u, 0u });
            Assert.That(after, Is.EqualTo((before * 800 + 999) / 1000));
        }

        [Test]
        public void BeastRetaliationAssignsNeutralTargetWhenSoldierWasIdle()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle);
            var neutral = world.Neutrals[0];
            neutral.Alive = true; neutral.Position = neutral.Home; neutral.LastHitFaction = 1; neutral.LastHitSoldierId = 1;
            neutral.NextAttackTick = 0; neutral.AttackDamage = 1; neutral.Range = Fix64.FromInt(4);
            world.Neutrals[0] = neutral;
            var soldier = world.Soldiers[0];
            soldier.Alive = true; soldier.Hp = Math.Max(1, soldier.Hp); soldier.Position = neutral.Position;
            soldier.TargetKind = 0; soldier.TargetId = 0;
            world.Soldiers[0] = soldier;
            Invoke(battle, "NeutralAttackStep");
            Assert.That(world.Soldiers[0].TargetKind, Is.EqualTo(7));
            Assert.That(world.Soldiers[0].TargetId, Is.EqualTo(neutral.Id));
        }

        [Test]
        public void NeutralSwitchesToRecentInCampAttackerWhenCurrentAttackerDies()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle);
            world.Tick = 100;
            var neutral = world.Neutrals[0];
            neutral.Alive = true; neutral.Position = neutral.Home; neutral.LastHitFaction = 1; neutral.LastHitSoldierId = 1;
            neutral.LastHitTick = 99; neutral.LastCombatTick = 100; neutral.PreviousHitFaction = 1; neutral.PreviousHitSoldierId = 2;
            neutral.PreviousHitTick = 98;
            for (int i = 1; i < world.Neutrals.Length; i++) { var other = world.Neutrals[i]; other.Alive = false; world.Neutrals[i] = other; }
            var dead = world.Soldiers[0]; dead.Alive = false; world.Soldiers[0] = dead;
            var live = world.Soldiers[1]; live.Alive = true; live.Hp = Math.Max(1, live.Hp); live.Position = neutral.Home; world.Soldiers[1] = live;
            world.Neutrals[0] = neutral;
            Invoke(battle, "NeutralMoveStep");
            Assert.That(world.Neutrals[0].LastHitSoldierId, Is.EqualTo(2));
            Assert.That(world.Neutrals[0].Returning, Is.False);
        }

        [Test]
        public void AutoHuntDoesNotAssignArmyWithExistingRole()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle);
            var army = world.Armies[0];
            army.Decision.Assignment = AssignmentKind.Advance;
            army.Decision.Goal = new PolicyGoal(GoalKind.Core, 1, default(SimPoint));
            world.Armies[0] = army;
            Invoke(battle, "NeutralThink");
            Assert.That(battle.NeutralHuntOrdersFor(1), Is.EqualTo(0));
        }

        [Test]
        public void AutoHuntChoosesOnlyAnOwnSideCampForAnUnassignedArmy()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle);
            world.Config.NeutralRules.HuntIntervalTicks = 1;
            world.Config.NeutralRules.HuntStrengthPermille = 0;
            var army = world.Armies[world.Factions[0].ArmyIds[0] - 1];
            var ids = world.Soldiers.Where(s => s.Initial.FactionId == 1).Take(4).Select(s => s.Initial.Id).ToArray();
            Assert.That(ids.Length, Is.GreaterThan(0));
            foreach (uint id in ids)
            {
                var soldier = world.Soldiers[id - 1];
                soldier.Alive = true; soldier.Hp = Math.Max(1, soldier.Hp);
                world.Soldiers[id - 1] = soldier;
            }
            army.SoldierIds = ids; army.Decision.Assignment = AssignmentKind.Advance; army.Decision.Goal = default(PolicyGoal);
            army.Decision.Returning = false; army.Policy = default(PolicyKind);
            world.Armies[army.Definition.Id - 1] = army;
            var ownSide = typeof(Battle).GetMethod("OwnSideCamp", PrivateInstance);
            int campIndex = Array.FindIndex(world.NeutralCamps, camp => camp.Kind == NeutralKind.WolfPack
                && (bool)ownSide.Invoke(battle, new object[] { 1u, camp.Position }));
            Assert.That(campIndex, Is.GreaterThanOrEqualTo(0));
            var camp = world.NeutralCamps[campIndex];
            foreach (uint id in camp.NeutralIds)
            {
                var neutral = world.Neutrals[id - 1]; neutral.Alive = true; neutral.Hp = 1; world.Neutrals[id - 1] = neutral;
            }
            Invoke(battle, "NeutralThink");
            Assert.That(battle.NeutralHuntOrdersFor(1), Is.GreaterThan(0));
            Assert.That(world.NeutralOrderCamps[ids[0]], Is.EqualTo(camp.Id | 0x80000000U));
            Assert.That(world.NeutralCamps.Any(value => value.Kind == NeutralKind.WolfPack
                && !(bool)ownSide.Invoke(battle, new object[] { 1u, value.Position })), Is.True);
        }
    }
}
