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

        private static Battle LegacyLargeBattle()
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            scenario.Economy.AiHuntUntilCore = false;
            return new Battle(scenario);
        }

        private static int OwnWolf(WorldState world, uint faction)
        {
            for (int i = 0; i < world.NeutralCamps.Length; i++)
                if (world.NeutralCamps[i].Kind == NeutralKind.WolfPack
                    && world.NeutralCampOwnSide[(faction - 1) * world.NeutralCamps.Length + i]) return i;
            return -1;
        }

        private static void PrepareCamp(WorldState world, int activeIndex, int hp)
        {
            for (int campIndex = 0; campIndex < world.NeutralCamps.Length; campIndex++)
                foreach (uint id in world.NeutralCamps[campIndex].NeutralIds)
                {
                    var neutral = world.Neutrals[id - 1];
                    neutral.Alive = campIndex == activeIndex; neutral.Hp = campIndex == activeIndex ? hp : 0;
                    world.Neutrals[id - 1] = neutral;
                }
        }

        private static ArmyState PrepareArmy(WorldState world, uint armyId, SimPoint position, int hp)
        {
            var army = world.Armies[armyId - 1];
            foreach (uint id in army.SoldierIds)
            {
                var soldier = world.Soldiers[id - 1];
                soldier.Alive = true; soldier.Hp = hp; soldier.Position = position;
                world.Soldiers[id - 1] = soldier;
            }
            army.Decision = new ArmyDecisionMemory(AssignmentKind.Guard, new PolicyGoal(GoalKind.Core,
                world.Factions[army.Definition.FactionId - 1].CoreId, default(SimPoint)), false, 0, 0);
            army.Policy = default(PolicyKind);
            world.Armies[armyId - 1] = army;
            return army;
        }

        [Test]
        public void LargeLiveRulesEnableDeterministicSymmetricNeutralCamps()
        {
            var left = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var right = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var a = left.Capture(1).NeutralCamps;
            var b = right.Capture(1).NeutralCamps;
            Assert.That(a.Count, Is.EqualTo(23));
            Assert.That(b.Select(x => (x.Id, x.Kind, x.Position.X.Raw, x.Position.Z.Raw)),
                Is.EqualTo(a.Select(x => (x.Id, x.Kind, x.Position.X.Raw, x.Position.Z.Raw))));
            for (int i = 0; i < 18; i += 2)
            {
                Assert.That(a[i].Kind, Is.EqualTo(a[i + 1].Kind));
                Assert.That(a[i].Position.X.Raw + a[i + 1].Position.X.Raw,
                    Is.EqualTo(a[0].Position.X.Raw + a[1].Position.X.Raw));
                Assert.That(a[i].Position.Z.Raw, Is.EqualTo(a[i + 1].Position.Z.Raw));
            }
            Assert.That(a[18].Kind, Is.EqualTo(NeutralKind.AugmentGuardian));
            Assert.That(a[19].Kind, Is.EqualTo(NeutralKind.Dragon));
            Assert.That(a[20].Kind, Is.EqualTo(NeutralKind.SiegeBeast));
            Assert.That(a[21].Kind, Is.EqualTo(NeutralKind.SiegeBeast));
            Assert.That(a[22].Kind, Is.EqualTo(NeutralKind.GreatLord));
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

        private static bool CampOnClearGround(WorldState world, SimPoint position)
        {
            // Every cell centre within 4 m (NeutralCatalog.CampClearMeters) of the camp's cell must be passable.
            int cell = world.Map.Cell(position);
            if (cell < 0 || !world.Map.IsPassable(cell)) return false;
            var centre = world.Map.Center(cell);
            int w = world.Config.Map.WidthCells, h = world.Config.Map.HeightCells;
            for (int dz = -3; dz <= 3; dz++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    int x = cell % w + dx, z = cell / w + dz;
                    if (x < 0 || z < 0 || x >= w || z >= h) continue;
                    var other = world.Map.Center(z * w + x);
                    long ox = other.X.Raw - centre.X.Raw, oz = other.Z.Raw - centre.Z.Raw;
                    long r = Fix64.FromInt(4).Raw;
                    if ((System.Numerics.BigInteger)ox * ox + (System.Numerics.BigInteger)oz * oz > (System.Numerics.BigInteger)r * r) continue;
                    if (!world.Map.IsPassable(z * w + x)) return false;
                }
            return true;
        }

        [Test]
        public void EveryLiveCampSitsOnClearGround()
        {
            // 11-18 testplay: a wolf pack sat inside a mountain where no soldier could reach it.
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var world = World(new Battle(LiveGameRules.Create(seed, true, true, false, false)));
                foreach (var camp in world.NeutralCamps)
                    Assert.That(CampOnClearGround(world, camp.Position), Is.True, "seed " + seed + " camp " + camp.Id + " " + camp.Kind);
            }
        }

        [Test]
        public void CampOnBlockedGroundMovesToTheNearestClearSpotAndOthersStay()
        {
            var open = LiveGameRules.Create(1, true, true, false, false);
            var before = World(new Battle(open)).NeutralCamps.Select(c => c.Position).ToArray();
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            var map = scenario.Map;
            int size = map.CellSizeMeters, w = map.WidthCells;
            long raw = Fix64.FromInt(size).Raw;
            int cx = (int)(before[0].X.Raw / raw), cz = (int)(before[0].Z.Raw / raw);
            var blocked = map.BlockedCellIds.ToList();
            for (int z = cz - 4; z <= cz + 4; z++)
                for (int x = cx - 4; x <= cx + 4; x++)
                    if (!blocked.Contains(z * w + x)) blocked.Add(z * w + x);
            map.BlockedCellIds = blocked.ToArray();
            var world = World(new Battle(scenario));
            var moved = world.NeutralCamps[0].Position;
            Assert.That(moved, Is.Not.EqualTo(before[0]));
            Assert.That(CampOnClearGround(world, moved), Is.True);
            long dx = (moved.X.Raw - before[0].X.Raw) / raw, dz = (moved.Z.Raw - before[0].Z.Raw) / raw;
            Assert.That(dx * dx + dz * dz, Is.LessThanOrEqualTo(20L * 20L), "moved to a near clear spot, not far away");
            // Its east partner moves as the mirror image, so the two sides stay fair.
            var partner = world.NeutralCamps[1].Position;
            Assert.That(partner.X.Raw + moved.X.Raw, Is.EqualTo(Fix64.FromInt(world.Config.Map.WidthMeters).Raw));
            Assert.That(partner.Z.Raw, Is.EqualTo(moved.Z.Raw));
            for (int i = 2; i < before.Length; i++)
                Assert.That(world.NeutralCamps[i].Position, Is.EqualTo(before[i]), "camp " + i + " was already on clear ground");
        }

        [Test]
        public void HumanNeutralOrderEndsWhenTheCampIsCleared()
        {
            // 11-18 testplay: after killing the wolf the soldier could not move. The human order (no hunt marker)
            // was never cleared, so every tick pinned the soldier to the empty camp.
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle);
            int camp = OwnWolf(world, 1);
            Assert.That(camp, Is.GreaterThanOrEqualTo(0));
            PrepareCamp(world, -1, 0);
            int index = world.SoldierTraversal[0];
            var goal = new SimPoint(Fix64.FromInt(10), Fix64.FromInt(10));
            world.Soldiers[index].Alive = true;
            world.Soldiers[index].TargetKind = 0;
            world.Soldiers[index].MoveGoal = goal;
            world.NeutralOrders[index] = world.NeutralCamps[camp].Id;
            world.NeutralOrderCamps[index] = world.NeutralCamps[camp].Id;
            Invoke(battle, "AssignNeutralIntents");
            Assert.That(world.NeutralOrders[index], Is.EqualTo(0));
            Assert.That(world.NeutralOrderCamps[index], Is.EqualTo(0));
            Assert.That(world.Soldiers[index].TargetKind, Is.EqualTo(0));
            Assert.That(world.Soldiers[index].MoveGoal, Is.EqualTo(goal));
        }

        [Test]
        public void HumanNeutralOrderKeepsChasingWhileTheCampLives()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle);
            int camp = OwnWolf(world, 1);
            PrepareCamp(world, camp, 50);
            int index = world.SoldierTraversal[0];
            world.Soldiers[index].Alive = true;
            world.Soldiers[index].TargetKind = 0;
            world.NeutralOrders[index] = world.NeutralCamps[camp].Id;
            world.NeutralOrderCamps[index] = world.NeutralCamps[camp].Id;
            Invoke(battle, "AssignNeutralIntents");
            uint wolf = world.NeutralCamps[camp].NeutralIds[0];
            Assert.That(world.NeutralOrders[index], Is.EqualTo(world.NeutralCamps[camp].Id));
            Assert.That(world.Soldiers[index].TargetKind, Is.EqualTo(7));
            Assert.That(world.Soldiers[index].TargetId, Is.EqualTo(wolf));
            Assert.That(world.Soldiers[index].MoveGoal, Is.EqualTo(world.Neutrals[wolf - 1].Position));
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
            var battle = new Battle(AggroOff(LiveGameRules.Create(1, true, true, false, false)));
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
            var battle = new Battle(AggroOff(LiveGameRules.Create(1, true, true, false, false)));
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
            var battle = LegacyLargeBattle();
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
            world.Config.Economy.AiHuntUntilCore = false; // This is the legacy own-side selector; huntcore has separate memory tests.
            world.Config.NeutralRules.HuntIntervalTicks = 1; world.Config.Economy.AiHuntDecisionTicks = 1;
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
            foreach (uint id in ids)
            {
                var soldier = world.Soldiers[id - 1]; soldier.Position = camp.Position; world.Soldiers[id - 1] = soldier;
            }
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

        [Test]
        public void GuardingArmyHuntsNearbyCampThenReturnsToItsOriginalRole()
        {
            var battle = LegacyLargeBattle();
            var world = World(battle); world.Config.NeutralRules.HuntIntervalTicks = 1; world.Config.Economy.AiHuntDecisionTicks = 1; world.Config.NeutralRules.HuntStrengthPermille = 1200;
            int campIndex = OwnWolf(world, 1); Assert.That(campIndex, Is.GreaterThanOrEqualTo(0));
            var camp = world.NeutralCamps[campIndex]; var army = PrepareArmy(world, world.Factions[0].ArmyIds[0], camp.Position, 100);
            PrepareCamp(world, campIndex, 1);
            Invoke(battle, "NeutralThink");
            Assert.That(battle.NeutralHuntOrdersFor(1), Is.EqualTo(1));
            PrepareCamp(world, campIndex, 0);
            Invoke(battle, "NeutralThink");
            Assert.That(world.NeutralOrderCamps.All(value => value == 0), Is.True);
            Assert.That(world.Armies[army.Definition.Id - 1].Decision.Assignment, Is.EqualTo(AssignmentKind.Guard));
            Assert.That(world.Armies[army.Definition.Id - 1].Decision.Goal.Kind, Is.EqualTo(GoalKind.Core));
        }

        [Test]
        public void CoreDefenseAndEnemyAttackArmiesDoNotHunt()
        {
            var battle = LegacyLargeBattle();
            var world = World(battle); world.Config.NeutralRules.HuntIntervalTicks = 1; world.Config.Economy.AiHuntDecisionTicks = 1; world.Config.NeutralRules.HuntStrengthPermille = 0;
            int campIndex = OwnWolf(world, 1); PrepareCamp(world, campIndex, 1);
            uint firstId = world.Factions[0].ArmyIds[0], secondId = world.Factions[0].ArmyIds[1];
            var first = PrepareArmy(world, firstId, world.NeutralCamps[campIndex].Position, 100);
            first.Decision.Assignment = AssignmentKind.CoreDefense; world.Armies[firstId - 1] = first;
            var second = PrepareArmy(world, secondId, world.NeutralCamps[campIndex].Position, 100);
            second.Decision.Assignment = AssignmentKind.Advance;
            second.Decision.Goal = new PolicyGoal(GoalKind.Core, world.Factions[1].CoreId, default(SimPoint));
            world.Armies[secondId - 1] = second;
            Invoke(battle, "NeutralThink");
            Assert.That(battle.NeutralHuntOrdersFor(1), Is.EqualTo(0));
            Assert.That(battle.NeutralHuntFailuresFor(1, NeutralHuntFailureReason.Role), Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void ArmyBelowOnePointTwoTimesCampHpDoesNotHunt()
        {
            var battle = LegacyLargeBattle();
            var world = World(battle); world.Config.NeutralRules.HuntIntervalTicks = 1; world.Config.Economy.AiHuntDecisionTicks = 1; world.Config.NeutralRules.HuntStrengthPermille = 1200;
            int campIndex = OwnWolf(world, 1); PrepareCamp(world, campIndex, 10);
            var army = PrepareArmy(world, world.Factions[0].ArmyIds[0], world.NeutralCamps[campIndex].Position, 1);
            army.Decision.Assignment = AssignmentKind.Advance; army.Decision.Goal = default(PolicyGoal); world.Armies[army.Definition.Id - 1] = army;
            Invoke(battle, "NeutralThink");
            Assert.That(battle.NeutralHuntOrdersFor(1), Is.EqualTo(0));
            Assert.That(battle.NeutralHuntFailuresFor(1, NeutralHuntFailureReason.Weak), Is.GreaterThan(0));
        }

        [Test]
        public void OneCampIsReservedByOnlyOneArmy()
        {
            var battle = LegacyLargeBattle();
            var world = World(battle); world.Config.NeutralRules.HuntIntervalTicks = 1; world.Config.Economy.AiHuntDecisionTicks = 1; world.Config.NeutralRules.HuntStrengthPermille = 0;
            int campIndex = OwnWolf(world, 1); PrepareCamp(world, campIndex, 1); var point = world.NeutralCamps[campIndex].Position;
            PrepareArmy(world, world.Factions[0].ArmyIds[0], point, 100);
            PrepareArmy(world, world.Factions[0].ArmyIds[1], point, 100);
            foreach (uint armyId in world.Factions[0].ArmyIds)
                if (armyId > 2) { var other = world.Armies[armyId - 1]; foreach (uint id in other.SoldierIds) world.Soldiers[id - 1].Alive = false; }
            Invoke(battle, "NeutralThink");
            Assert.That(battle.NeutralHuntOrdersFor(1), Is.EqualTo(1));
        }

        [Test]
        public void SymmetricMapGivesBothFactionsTheSameNumberOfOwnSideCamps()
        {
            var scenario = WeekOneScenario.Create();
            scenario.Economy.Neutrals = true;
            scenario.NeutralRules.Camps = new[]
            {
                new NeutralCampDefinition { Id = 1, Kind = NeutralKind.WolfPack,
                    Position = new SimPoint(Fix64.FromInt(96), Fix64.FromInt(64)) },
                new NeutralCampDefinition { Id = 2, Kind = NeutralKind.WolfPack,
                    Position = new SimPoint(Fix64.FromInt(160), Fix64.FromInt(64)) }
            };
            var battle = new Battle(scenario);
            var world = World(battle); int west = 0, east = 0;
            for (int i = 0; i < world.NeutralCamps.Length; i++)
            {
                if (world.NeutralCampOwnSide[i]) west++;
                if (world.NeutralCampOwnSide[world.NeutralCamps.Length + i]) east++;
            }
            Assert.That(west, Is.EqualTo(east));
        }

        [Test]
        public void NeutralSideClassificationSurvivesScenarioBinaryRoundTrip()
        {
            var scenario = LiveGameRules.Create(9, true, true, false, false);
            var original = World(new Battle(scenario));
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(scenario));
            var roundTripped = World(new Battle(decoded));
            Assert.That(roundTripped.NeutralCampOwnSide, Is.EqualTo(original.NeutralCampOwnSide));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(ScenarioBinary.Encode(scenario)));
        }

        [Test]
        public void GreatNeutralCampsAreSymmetricAndSeparated()
        {
            var battle = new Battle(LiveGameRules.Create(4, true, true, false, false));
            var world = World(battle);
            int dragon = Camp(world, NeutralKind.Dragon);
            int lord = Camp(world, NeutralKind.GreatLord);
            int guardian = Camp(world, NeutralKind.AugmentGuardian);
            Assert.That(dragon, Is.GreaterThanOrEqualTo(0));
            Assert.That(lord, Is.GreaterThanOrEqualTo(0));
            Assert.That(world.NeutralCamps[dragon].Position.X.Raw, Is.EqualTo(world.NeutralCamps[lord].Position.X.Raw));
            Assert.That(Math.Abs(world.NeutralCamps[dragon].Position.Z.Raw - world.NeutralCamps[guardian].Position.Z.Raw), Is.GreaterThanOrEqualTo(Fix64.FromInt(25).Raw));
            var sieges = world.NeutralCamps.Where(camp => camp.Kind == NeutralKind.SiegeBeast).ToArray();
            Assert.That(sieges.Length, Is.EqualTo(2));
            Assert.That(sieges[0].Position.X.Raw + sieges[1].Position.X.Raw, Is.EqualTo(Fix64.FromInt(world.Config.Map.WidthMeters).Raw));
        }

        [Test]
        public void DragonElementsAreDeterministicAndNeverRepeat()
        {
            var type = typeof(Battle).Assembly.GetType("Rts.Simulation.NeutralCatalog");
            var method = type.GetMethod("DragonElementFor", BindingFlags.Static | BindingFlags.NonPublic);
            DragonElement previous = DragonElement.None;
            for (int count = 1; count <= 32; count++)
            {
                var first = (DragonElement)method.Invoke(null, new object[] { 99UL, count });
                var second = (DragonElement)method.Invoke(null, new object[] { 99UL, count });
                Assert.That(first, Is.EqualTo(second));
                Assert.That(first, Is.Not.EqualTo(previous));
                previous = first;
            }
        }

        [Test]
        public void DragonMarksAndSoulApplyAfterFourKills()
        {
            var battle = new Battle(LiveGameRules.Create(5, true, true, false, false));
            var world = World(battle);
            int dragonCamp = Camp(world, NeutralKind.Dragon);
            var dragon = world.Neutrals[world.NeutralCamps[dragonCamp].NeutralIds[0] - 1];
            dragon.DragonElement = DragonElement.Fire; world.Neutrals[dragon.Id - 1] = dragon;
            for (int i = 0; i < 4; i++) Invoke(battle, "ResolveNeutralCampReward", dragonCamp, 1u, dragon.Home);
            Assert.That(world.Factions[0].DragonMarks[(int)DragonElement.Fire], Is.EqualTo(4));
            Assert.That(world.Factions[0].DragonSoul, Is.True);
        }

        [Test]
        public void DirectSiegeBeastUsesBuildingTargetAndExpiresAt2400Ticks()
        {
            var battle = new Battle(LiveGameRules.Create(6, true, true, false, false));
            var world = World(battle);
            int siegeCamp = Camp(world, NeutralKind.SiegeBeast);
            uint beastId = world.NeutralCamps[siegeCamp].NeutralIds[0];
            var beast = world.Neutrals[beastId - 1];
            beast.Alive = true; beast.OwnerFactionId = 1; beast.Hp = 1500; beast.MaxHp = 1500; beast.LifeUntilTick = 2400;
            world.Neutrals[beastId - 1] = beast;
            var building = world.Buildings.FirstOrDefault(value => value.Alive && value.FactionId == 2);
            if (building.Id == 0)
            {
                int cell = world.Map.Cell(world.NeutralCamps[siegeCamp].Position);
                building = new BuildingState { Id = 1, FactionId = 2, Kind = BuildingKind.Tower, OriginCell = cell,
                    WorkCell = cell, Alive = true, Complete = true, Hp = 1000 };
                world.Buildings = new[] { building };
                world.NextBuildingId = 2;
            }
            Assert.That(building.Id, Is.GreaterThan(0));
            Invoke(battle, "ApplyEconomyCommand", EconomyCommand.DirectSiegeBeast(1, 1, beastId, building.Id));
            Assert.That(world.Neutrals[beastId - 1].SiegeTargetBuildingId, Is.EqualTo(building.Id));
            world.Tick = 2400;
            Invoke(battle, "NeutralMoveStep");
            Assert.That(world.Neutrals[beastId - 1].Alive, Is.False);
            Assert.That(world.NeutralCamps[siegeCamp].NextSpawnTick, Is.GreaterThan(2400));
        }

        [Test]
        public void GreatNeutralsExtensionRoundTrips()
        {
            var scenario = LiveGameRules.Create(7, true, true, false, false);
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.GreatNeutrals, Is.True);
            Assert.That(decoded.Extensions.Any(value => value.Id == 124), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        // 79章の旧来の動き（86章の旗オフ）を確かめる試験
        private static ScenarioDefinition AggroOff(ScenarioDefinition scenario)
        { scenario.Economy.NeutralAggro = false; return scenario; }
    }
}
