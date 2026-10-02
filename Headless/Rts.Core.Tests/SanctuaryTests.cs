using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class SanctuaryTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(bool sanctuary)
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.Sanctuary = sanctuary;
            scenario.Economy.StartFood = 5000;
            scenario.Economy.StartWood = 5000;
            scenario.Economy.StartStone = 1000;
            scenario.Economy.AutoVillagerTarget = 3;
            scenario.Economy.AutoInfantryQueue = 0;
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 1000000;
            scenario.Outposts[0].OwnerFactionId = 1;
            for (int i = 0; i < scenario.UnitParameters.Length; i++) scenario.UnitParameters[i].Damage = 0;
            return scenario;
        }

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) AtSanctuaryAge()
            => AtCivAge(Scenario(true), CivKind.Sanctuary);

        private static (ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ulong sequence) AtCivAge(ScenarioDefinition scenario, CivKind civ)
        {
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++sequence, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, civ));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 2);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(civ));
            return (scenario, sim, gateway, sequence);
        }

        private static int FindSite(Battle sim, uint outpostId)
            => (int)typeof(Battle).GetMethod("FindSanctuarySite", Private).Invoke(sim, new object[] { 1u, outpostId });

        private static uint PlaceAndBuildShrine(ScenarioDefinition scenario, Battle sim, CommandGateway gateway, ref ulong sequence, uint outpostId)
        {
            int origin = FindSite(sim, outpostId);
            Assert.That(origin, Is.GreaterThanOrEqualTo(0), "a legal shrine site exists");
            uint before = (uint)sim.Capture(1).Economy.Buildings.Count;
            gateway.SubmitEconomy(EconomyCommand.Place(1, ++sequence, BuildingKind.Shrine, origin, Facing.North));
            Steps(gateway, sim, 1);
            var placed = sim.Capture(1).Economy.Buildings;
            Assert.That(placed.Count, Is.EqualTo(before + 1));
            uint id = placed[(int)before].Id;
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, id));
            // A shrine is deliberately placed near an outpost rather than beside the core;
            // allow the assigned villagers enough deterministic time to walk there first.
            Steps(gateway, sim, scenario.Economy.ShrineWork + 1000);
            Assert.That(sim.Capture(1).Economy.Buildings.Single(b => b.Id == id).Complete, Is.True);
            return id;
        }

        private static void SetOutpostOwner(Battle sim, uint outpostId, uint faction)
        {
            object world = typeof(Battle).GetField("world", Private).GetValue(sim);
            var field = world.GetType().GetField("Outposts", Private);
            var outposts = (Array)field.GetValue(world);
            object outpost = outposts.GetValue((int)outpostId - 1);
            outpost.GetType().GetField("OwnerFactionId", Private).SetValue(outpost, faction);
            outposts.SetValue(outpost, (int)outpostId - 1);
        }

        private static int SanctuaryDamage(Battle sim, uint faction, int damage)
            => (int)typeof(Battle).GetMethod("SanctuaryDamage", Private).Invoke(sim, new object[] { faction, damage });

        [Test]
        public void SanctuaryOffKeepsOldBytesAndStateStable()
        {
            var old = MapGenerator.GenerateTerrain(1);
            var off = MapGenerator.GenerateTerrain(1);
            off.Economy.Sanctuary = false;
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(old)));
            var left = new Battle(old);
            var right = new Battle(off);
            for (long tick = 1; tick <= 300; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void ShrineIsOnlyNearAnOwnedOutpostAndOnePerOutpost()
        {
            var state = AtSanctuaryAge();
            uint first = PlaceAndBuildShrine(state.scenario, state.sim, state.gateway, ref state.sequence, 1);
            var fields = DiagnosticComparison.Fields(state.sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);
            Assert.That(fields["Buildings[" + first + "].SanctuaryOutpostId"], Is.EqualTo("1"));

            int anotherSite = FindSite(state.sim, 1);
            Assert.That(anotherSite, Is.EqualTo(-1), "the same outpost cannot receive a second shrine");

            if (state.scenario.Outposts.Length > 1)
            {
                SetOutpostOwner(state.sim, 2, 1);
                uint second = PlaceAndBuildShrine(state.scenario, state.sim, state.gateway, ref state.sequence, 2);
                fields = DiagnosticComparison.Fields(state.sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);
                Assert.That(fields["Buildings[" + second + "].SanctuaryOutpostId"], Is.EqualTo("2"));
            }
        }

        [Test]
        public void OwnedShrinesScaleDamageAndOutpostLossStopsTheBonus()
        {
            var state = AtSanctuaryAge();
            uint shrine = PlaceAndBuildShrine(state.scenario, state.sim, state.gateway, ref state.sequence, 1);
            SetOutpostOwner(state.sim, 1, 1);
            bool allowed = (bool)typeof(Battle).GetMethod("SanctuaryAllowed", Private).Invoke(state.sim, new object[] { 1u });
            var fields = DiagnosticComparison.Fields(state.sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);
            Assert.That(allowed, Is.True, "sanctuary allowed; shrine=" + shrine + ", outpost=" + fields["Buildings[" + shrine + "].SanctuaryOutpostId"]);
            Assert.That(SanctuaryDamage(state.sim, 1, 100), Is.EqualTo(110));
            SetOutpostOwner(state.sim, 1, 2);
            Assert.That(SanctuaryDamage(state.sim, 1, 100), Is.EqualTo(100));
            SetOutpostOwner(state.sim, 1, 1);
            Assert.That(SanctuaryDamage(state.sim, 1, 100), Is.EqualTo(110));
            Assert.That(SanctuaryDamage(state.sim, 2, 100), Is.EqualTo(100));
        }

        [Test]
        public void SanctuaryAutomaticallyBuildsAResettledShrine()
        {
            var scenario = Scenario(true);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Sanctuary));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 5000);
            Assert.That(sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.Shrine), Is.True);
        }

        [Test]
        public void SanctuaryExtensionRoundTripsWithIdEight()
        {
            var scenario = Scenario(true);
            byte[] bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.Sanctuary, Is.True);
            Assert.That(decoded.Extensions.Any(e => e.Id == 8), Is.True);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void SanctuaryReplayIsDeterministicForTwentyThousandTicks()
        {
            var scenario = Scenario(true);
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 20000, identity);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, identity);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        // ---- V3-18 #2: outpost targeting, pilgrimage (26), holy relic (27), extension v2 ----

        private static object World(Battle sim) => typeof(Battle).GetField("world", Private).GetValue(sim);

        private static void SetEconomyField(Battle sim, uint faction, string name, object value)
        {
            object world = World(sim);
            var economies = (Array)world.GetType().GetField("Economies", Private).GetValue(world);
            object economy = economies.GetValue((int)faction - 1);
            economy.GetType().GetField(name, Private).SetValue(economy, value);
            economies.SetValue(economy, (int)faction - 1);
        }

        private static void SetTech(Battle sim, uint faction, TechKind tech, bool enabled)
        {
            object world = World(sim);
            var economies = (Array)world.GetType().GetField("Economies", Private).GetValue(world);
            object economy = economies.GetValue((int)faction - 1);
            var techs = economy.GetType().GetField("Techs", Private);
            ulong bits = (ulong)techs.GetValue(economy);
            ulong mask = 1UL << ((int)tech - 1);
            techs.SetValue(economy, enabled ? bits | mask : bits & ~mask);
            economies.SetValue(economy, (int)faction - 1);
        }

        private static int EconomyInt(Battle sim, uint faction, string name)
        {
            object world = World(sim);
            var economies = (Array)world.GetType().GetField("Economies", Private).GetValue(world);
            object economy = economies.GetValue((int)faction - 1);
            return (int)economy.GetType().GetField(name, Private).GetValue(economy);
        }

        private static TechKind BuildingResearching(Battle sim, uint id)
        {
            object world = World(sim);
            var buildings = (Array)world.GetType().GetField("Buildings", Private).GetValue(world);
            for (int i = 0; i < buildings.Length; i++)
            {
                object building = buildings.GetValue(i);
                if (building != null && (uint)building.GetType().GetField("Id", Private).GetValue(building) == id)
                    return (TechKind)building.GetType().GetField("Researching", Private).GetValue(building);
            }
            Assert.Fail("no building " + id);
            return 0;
        }

        private static void ReleaseBuilding(Battle sim, uint id)
        {
            object world = World(sim);
            var buildings = (Array)world.GetType().GetField("Buildings", Private).GetValue(world);
            for (int i = 0; i < buildings.Length; i++)
            {
                object building = buildings.GetValue(i);
                if (building == null || (uint)building.GetType().GetField("Id", Private).GetValue(building) != id) continue;
                building.GetType().GetField("Held", Private).SetValue(building, false);
                buildings.SetValue(building, i);
                return;
            }
            Assert.Fail("no building " + id);
        }

        private static bool HasTech(Battle sim, uint faction, TechKind tech)
            => (bool)typeof(Battle).GetMethod("HasTech", Private).Invoke(sim, new object[] { faction, tech });

        private static bool TechOpen(Battle sim, uint faction, TechKind tech)
            => (bool)typeof(Battle).GetMethod("TechOpen", Private).Invoke(sim, new object[] { faction, tech });

        private static int SoldierOf(Battle sim, uint faction, int skip = 0)
        {
            object world = World(sim);
            var soldiers = (Array)world.GetType().GetField("Soldiers", Private).GetValue(world);
            for (int i = 0; i < soldiers.Length; i++)
            {
                object soldier = soldiers.GetValue(i);
                if (soldier == null || !(bool)soldier.GetType().GetField("Alive", Private).GetValue(soldier)) continue;
                object initial = soldier.GetType().GetField("Initial", Private).GetValue(soldier);
                if ((uint)initial.GetType().GetField("FactionId").GetValue(initial) != faction) continue;
                if (skip-- == 0) return i;
            }
            Assert.Fail("no living soldier of faction " + faction);
            return -1;
        }

        private static int MaxHp(Battle sim, int index)
        {
            object world = World(sim);
            var soldiers = (Array)world.GetType().GetField("Soldiers", Private).GetValue(world);
            object soldier = soldiers.GetValue(index);
            object parameters = soldier.GetType().GetField("Parameters", Private).GetValue(soldier);
            return (int)parameters.GetType().GetField("Hp").GetValue(parameters);
        }

        private static int Hp(Battle sim, int index)
        {
            object world = World(sim);
            var soldiers = (Array)world.GetType().GetField("Soldiers", Private).GetValue(world);
            object soldier = soldiers.GetValue(index);
            return (int)soldier.GetType().GetField("Hp", Private).GetValue(soldier);
        }

        private static void PlaceSoldier(Battle sim, int index, SimPoint position, int hp)
        {
            object world = World(sim);
            var soldiers = (Array)world.GetType().GetField("Soldiers", Private).GetValue(world);
            object soldier = soldiers.GetValue(index);
            soldier.GetType().GetField("Position", Private).SetValue(soldier, position);
            soldier.GetType().GetField("Hp", Private).SetValue(soldier, hp);
            soldiers.SetValue(soldier, index);
        }

        private static void Pulse(Battle sim)
            => typeof(Battle).GetMethod("SanctuaryPilgrimagePulse", Private).Invoke(sim, null);

        private static FactionObservation Targets(Battle sim, uint faction, FactionObservation observation)
            => (FactionObservation)typeof(Battle).GetMethod("SanctuaryTargetObservation", Private).Invoke(sim, new object[] { faction, observation });

        private static bool Focus(Battle sim, uint faction, FactionObservation observation)
            => (bool)typeof(Battle).GetMethod("SanctuaryOutpostFocus", Private).Invoke(sim, new object[] { faction, observation });

        private static bool Skips(Battle sim, uint faction, bool focus, GoalKind kind, uint id)
            => (bool)typeof(Battle).GetMethod("SanctuarySkipsGoal", Private).Invoke(sim, new object[] { faction, focus, kind, id });

        private static SimPoint P(int x, int z) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));

        /// <summary>Runs the unchanged common allocation for one infantry army, with routes built the way DecideArmies builds them.</summary>
        private static PolicyGoal Allocate(Battle sim, uint faction, FactionObservation observation, IDictionary<(GoalKind, uint), int> distances)
        {
            var targets = Targets(sim, faction, observation);
            bool focus = Focus(sim, faction, targets);
            var army = new OwnArmyView(1, faction, UnitKind.Infantry, P(10, 10), 5, default);
            var routes = targets.Objectives.OrderBy(o => o.Kind).ThenBy(o => o.Id).Select(o => new ObjectiveRoute(
                new PolicyGoal(o.Kind, o.Id, default),
                Skips(sim, faction, focus, o.Kind, o.Id) ? int.MaxValue : distances[(o.Kind, o.Id)],
                new[] { army.Position, o.Position })).ToArray();
            var input = new ArmyDecisionInput(army, default, false, default, routes);
            return Rts.Decision.PolicyDecision.Allocate(targets, observation.Tick, new[] { input }, 0, Array.Empty<uint>(),
                Array.Empty<AttackMemory>(), Array.Empty<PolicyOrder>(), out _)[0].Goal;
        }

        [Test]
        public void SanctuaryAutoTargetsAnObservedEasyOutpostAndNeverAnUnseenOne()
        {
            var state = AtSanctuaryAge();
            var sim = state.sim;
            uint ownCore = state.scenario.Cores[0].Id, enemyCore = state.scenario.Cores[1].Id;
            const long tick = 1000;
            // A far visible enemy fills the public enemy cap, so the unseen outpost carries no doctrine warning and the
            // common ordering alone would prefer it for being nearer.
            var far = new VisibleEnemy(1, P(5000, 5000), (byte)UnitKind.Infantry);
            var farContact = new EnemyContact(1, P(5000, 5000), tick, 1, 1, true);
            var cores = new[]
            {
                new KnownObjective(GoalKind.Core, ownCore, P(10, 10), true, 1, true, 100, tick),
                new KnownObjective(GoalKind.Core, enemyCore, P(300, 10), true, 2, true, 100, tick),
            };
            var seenEnemyPost = new KnownObjective(GoalKind.Outpost, 1, P(60, 80), true, 2, false, 0, tick);
            var unseenPost = new KnownObjective(GoalKind.Outpost, 2, P(40, 10), false, 0, false, 0, 0);
            var observation = new FactionObservation(1, tick, Array.Empty<OwnArmyView>(), new[] { far }, new[] { farContact },
                cores.Concat(new[] { seenEnemyPost, unseenPost }).ToArray(), 1);
            var distances = new Dictionary<(GoalKind, uint), int>
            {
                [(GoalKind.Core, ownCore)] = 0, [(GoalKind.Core, enemyCore)] = 30,
                [(GoalKind.Outpost, 1)] = 50, [(GoalKind.Outpost, 2)] = 10,
            };

            var targets = Targets(sim, 1, observation);
            Assert.That(targets.Objectives.Where(o => o.Kind == GoalKind.Outpost).Select(o => o.Id), Is.EqualTo(new[] { 1u }), "見ていない拠点は候補から外す");
            var chosen = Allocate(sim, 1, observation, distances);
            Assert.That((chosen.Kind, chosen.Id), Is.EqualTo((GoalKind.Outpost, 1u)), "観測した拠点を取りに行く");
            // The common policy itself, given the unchanged observation, would have chosen the nearer unseen outpost.
            var army = new OwnArmyView(1, 1, UnitKind.Infantry, P(10, 10), 5, default);
            var plainRoutes = observation.Objectives.Select(o => new ObjectiveRoute(new PolicyGoal(o.Kind, o.Id, default),
                distances[(o.Kind, o.Id)], new[] { army.Position, o.Position })).ToArray();
            var plain = Rts.Decision.PolicyDecision.Allocate(observation, tick, new[] { new ArmyDecisionInput(army, default, false, default, plainRoutes) },
                0, Array.Empty<uint>(), Array.Empty<AttackMemory>(), Array.Empty<PolicyOrder>(), out _)[0].Goal;
            Assert.That((plain.Kind, plain.Id), Is.EqualTo((GoalKind.Outpost, 2u)), "共通の判断だけなら近い未観測の拠点を選ぶ（比較用）");

            // Once one outpost is already own, the sanctuary has its foothold and goes back to the common behaviour:
            // the enemy core is a candidate again, so an outpost it cannot take never keeps it from the core.
            var ownPost = new KnownObjective(GoalKind.Outpost, 1, P(60, 80), true, 1, false, 0, tick);
            var neutralPost = new KnownObjective(GoalKind.Outpost, 2, P(40, 10), true, 0, false, 0, tick);
            var open = new FactionObservation(1, tick, Array.Empty<OwnArmyView>(), new[] { far }, new[] { farContact },
                cores.Concat(new[] { ownPost, neutralPost }).ToArray(), 1);
            Assert.That(Targets(sim, 1, open), Is.SameAs(open), "拠点を1つ持っていれば観測はそのまま");
            Assert.That(Focus(sim, 1, open), Is.False, "拠点を1つ持ったら拠点だけを狙う状態は終わる");
            Assert.That(Skips(sim, 1, Focus(sim, 1, open), GoalKind.Core, enemyCore), Is.False, "拠点を持ったら敵のコアも候補に戻る");
            Assert.That(Focus(sim, 1, observation), Is.True, "拠点を1つも持たず、観測した拠点があれば拠点を狙う");
            Assert.That(Skips(sim, 1, true, GoalKind.Core, enemyCore), Is.True, "その間は敵のコアを候補から外す");

            var crowdedPost = new KnownObjective(GoalKind.Outpost, 1, P(60, 80), true, 2, false, 0, tick);
            var guards = Enumerable.Range(0, 3).Select(i => new VisibleEnemy((uint)(10 + i), P(60, 80), (byte)UnitKind.Infantry)).ToArray();
            var guardContacts = guards.Select(g => new EnemyContact(g.ContactId, g.Position, tick, 1, 1, true)).ToArray();
            var easy = new FactionObservation(1, tick, Array.Empty<OwnArmyView>(), guards, guardContacts,
                cores.Concat(new[] { crowdedPost, neutralPost }).ToArray(), 40);
            var easyDistances = new Dictionary<(GoalKind, uint), int>
            {
                [(GoalKind.Core, ownCore)] = 0, [(GoalKind.Core, enemyCore)] = 20,
                [(GoalKind.Outpost, 1)] = 30, [(GoalKind.Outpost, 2)] = 60,
            };
            var easyChoice = Allocate(sim, 1, easy, easyDistances);
            Assert.That((easyChoice.Kind, easyChoice.Id), Is.EqualTo((GoalKind.Outpost, 2u)), "見えている敵が少ない拠点を優先");

            // Another civilisation, and a sanctuary with nothing observed to take, get the observation unchanged.
            var other = new FactionObservation(2, tick, Array.Empty<OwnArmyView>(), Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(),
                cores.Concat(new[] { seenEnemyPost, unseenPost }).ToArray());
            Assert.That(Targets(sim, 2, other), Is.SameAs(other), "聖地でない陣営は変わらない");
            Assert.That(Focus(sim, 2, other), Is.False);
            var nothing = new FactionObservation(1, tick, Array.Empty<OwnArmyView>(), Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(),
                cores.Concat(new[] { ownPost, unseenPost }).ToArray());
            Assert.That(Targets(sim, 1, nothing), Is.SameAs(nothing), "取れる拠点を観測していなければ従来どおり");
            Assert.That(Focus(sim, 1, nothing), Is.False);
            Assert.That(Skips(sim, 1, true, GoalKind.Core, ownCore), Is.False, "自分のコアへの道は残す");
        }

        [Test]
        public void PilgrimageHealsOnlyOwnSoldiersNearAnOwnSanctuaryAfterResearch()
        {
            var state = AtSanctuaryAge();
            var sim = state.sim;
            PlaceAndBuildShrine(state.scenario, sim, state.gateway, ref state.sequence, 1);
            SetOutpostOwner(sim, 1, 1);
            var post = state.scenario.Outposts[0].Position;
            int near = SoldierOf(sim, 1), far = SoldierOf(sim, 1, 1), enemy = SoldierOf(sim, 2);
            int max = MaxHp(sim, near);
            var edge = new SimPoint(post.X + Fix64.FromInt(state.scenario.Economy.SanctuaryPilgrimageRadius), post.Z);
            var beyond = new SimPoint(post.X + Fix64.FromInt(state.scenario.Economy.SanctuaryPilgrimageRadius + 1), post.Z);

            PlaceSoldier(sim, near, edge, max - 5);
            PlaceSoldier(sim, far, beyond, max - 5);
            PlaceSoldier(sim, enemy, post, MaxHp(sim, enemy) - 5);
            Pulse(sim);
            Assert.That(Hp(sim, near), Is.EqualTo(max - 5), "未研究では回復しない");

            SetTech(sim, 1, SanctuaryTech.Pilgrimage, true);
            Pulse(sim);
            Assert.That(Hp(sim, near), Is.EqualTo(max - 5 + state.scenario.Economy.SanctuaryPilgrimageHeal), "聖地の16m以内");
            Assert.That(Hp(sim, far), Is.EqualTo(max - 5), "聖地から遠い");
            Assert.That(Hp(sim, enemy), Is.EqualTo(MaxHp(sim, enemy) - 5), "敵の兵は回復しない");

            PlaceSoldier(sim, near, post, max);
            Pulse(sim);
            Assert.That(Hp(sim, near), Is.EqualTo(max), "最大HPを超えない");

            // The tick gate: only on pilgrimage interval ticks.
            PlaceSoldier(sim, near, post, max - 5);
            object world = World(sim);
            var tickField = world.GetType().GetField("Tick", Private);
            long saved = (long)tickField.GetValue(world);
            int interval = state.scenario.Economy.SanctuaryPilgrimageIntervalTicks;
            tickField.SetValue(world, (long)interval * 7 + 1);
            typeof(Battle).GetMethod("AdvanceSanctuaryPilgrimage", Private).Invoke(sim, null);
            Assert.That(Hp(sim, near), Is.EqualTo(max - 5));
            tickField.SetValue(world, (long)interval * 7);
            typeof(Battle).GetMethod("AdvanceSanctuaryPilgrimage", Private).Invoke(sim, null);
            Assert.That(Hp(sim, near), Is.EqualTo(max - 4));
            tickField.SetValue(world, saved);

            // Losing the outpost stops the sanctuary, so the heal stops with it.
            SetOutpostOwner(sim, 1, 2);
            Pulse(sim);
            Assert.That(Hp(sim, near), Is.EqualTo(max - 4), "拠点を奪われたら止まる");
            SetOutpostOwner(sim, 1, 1);

            // The holy relic, on the default values: one sanctuary gives +15% instead of +10%.
            Assert.That(SanctuaryDamage(sim, 1, 100), Is.EqualTo(110));
            SetTech(sim, 1, SanctuaryTech.HolyRelic, true);
            Assert.That(SanctuaryDamage(sim, 1, 100), Is.EqualTo(115));
            Assert.That(SanctuaryDamage(sim, 2, 100), Is.EqualTo(100));

            // Another civilisation with the same tech bit and a soldier on the outpost is never healed.
            var otherState = AtCivAge(Scenario(true), CivKind.Agrarian);
            SetOutpostOwner(otherState.sim, 1, 1);
            SetTech(otherState.sim, 1, SanctuaryTech.Pilgrimage, true);
            int otherSoldier = SoldierOf(otherState.sim, 1);
            int otherMax = MaxHp(otherState.sim, otherSoldier);
            PlaceSoldier(otherState.sim, otherSoldier, post, otherMax - 5);
            Pulse(otherState.sim);
            Assert.That(Hp(otherState.sim, otherSoldier), Is.EqualTo(otherMax - 5), "他の文明は変わらない");
        }

        [Test]
        public void HolyRelicRaisesTheRateAndTheCapOfTheSanctuaryBonus()
        {
            var scenario = Scenario(true);
            scenario.Economy.SanctuaryAttackBonusPermille = 200;
            scenario.Economy.SanctuaryMaxBonusPermille = 300;
            scenario.Economy.SanctuaryRelicBonusPermille = 300;
            scenario.Economy.SanctuaryRelicMaxBonusPermille = 450;
            var state = AtCivAge(scenario, CivKind.Sanctuary);
            PlaceAndBuildShrine(state.scenario, state.sim, state.gateway, ref state.sequence, 1);
            SetOutpostOwner(state.sim, 1, 1);
            Assert.That(SanctuaryDamage(state.sim, 1, 100), Is.EqualTo(120));
            SetTech(state.sim, 1, SanctuaryTech.HolyRelic, true);
            Assert.That(SanctuaryDamage(state.sim, 1, 100), Is.EqualTo(130), "聖地1つあたりの上がり方が増える");
            Assert.That(SanctuaryDamage(state.sim, 2, 100), Is.EqualTo(100));
            Assume.That(state.scenario.Outposts.Length, Is.GreaterThan(1));
            SetTech(state.sim, 1, SanctuaryTech.HolyRelic, false);
            SetOutpostOwner(state.sim, 2, 1);
            PlaceAndBuildShrine(state.scenario, state.sim, state.gateway, ref state.sequence, 2);
            SetOutpostOwner(state.sim, 1, 1);
            SetOutpostOwner(state.sim, 2, 1);
            Assert.That(SanctuaryDamage(state.sim, 1, 100), Is.EqualTo(130), "研究前の上限30%");
            SetTech(state.sim, 1, SanctuaryTech.HolyRelic, true);
            Assert.That(SanctuaryDamage(state.sim, 1, 100), Is.EqualTo(145), "上限が45%に増える");
            Assert.That(SanctuaryDamage(state.sim, 2, 100), Is.EqualTo(100));
        }

        [Test]
        public void SanctuaryResearchIsShrineOnlyAgeGatedAndReplaysTheNewTechValues()
        {
            var state = AtSanctuaryAge();
            uint shrine = PlaceAndBuildShrine(state.scenario, state.sim, state.gateway, ref state.sequence, 1);
            Assert.That(TechOpen(state.sim, 1, SanctuaryTech.Pilgrimage), Is.False, "第1時代では巡礼は閉じる");
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, shrine, SanctuaryTech.Pilgrimage));
            Steps(state.gateway, state.sim, 1);
            Assert.That(state.sim.Capture(1).Economy.Buildings.First(b => b.Id == shrine).Researching, Is.EqualTo((TechKind)0));

            SetEconomyField(state.sim, 1, "Age", (byte)2);
            Assert.That(TechOpen(state.sim, 1, SanctuaryTech.Pilgrimage), Is.True);
            Assert.That(TechOpen(state.sim, 1, SanctuaryTech.HolyRelic), Is.False, "第2時代では聖遺物は閉じる");
            Assert.That(TechOpen(state.sim, 2, SanctuaryTech.Pilgrimage), Is.False, "聖地でない陣営は研究できない");
            // Food 0: a single tick's drop-off cannot reach the research price.
            SetEconomyField(state.sim, 1, "Food", 0);
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, shrine, SanctuaryTech.Pilgrimage));
            Steps(state.gateway, state.sim, 1);
            Assert.That(state.sim.Capture(1).Economy.Buildings.First(b => b.Id == shrine).Researching, Is.EqualTo((TechKind)0), "食料が足りない");

            SetEconomyField(state.sim, 1, "Food", 1000);
            SetEconomyField(state.sim, 1, "Wood", 1000);
            ReleaseBuilding(state.sim, shrine); // a player-placed building is held from automation until released
            Assert.That((bool)typeof(Battle).GetMethod("DecideSanctuaryResearch", Private).Invoke(state.sim, new object[] { 1u }), Is.True, "お任せが祠で研究を始める");
            Assert.That(BuildingResearching(state.sim, shrine), Is.EqualTo(SanctuaryTech.Pilgrimage));
            Assert.That(EconomyInt(state.sim, 1, "Food"), Is.EqualTo(1000 - state.scenario.Economy.SanctuaryPilgrimageFoodCost));
            Assert.That(EconomyInt(state.sim, 1, "Wood"), Is.EqualTo(1000 - state.scenario.Economy.SanctuaryPilgrimageWoodCost));
            Steps(state.gateway, state.sim, state.scenario.Economy.SanctuaryPilgrimageTicks + 1);
            Assert.That(HasTech(state.sim, 1, SanctuaryTech.Pilgrimage), Is.True);

            SetEconomyField(state.sim, 1, "Age", (byte)3);
            SetEconomyField(state.sim, 1, "Food", 1000);
            SetEconomyField(state.sim, 1, "Wood", 1000);
            state.gateway.SubmitEconomy(EconomyCommand.Research(1, ++state.sequence, shrine, SanctuaryTech.HolyRelic));
            Steps(state.gateway, state.sim, 1);
            Assert.That(state.sim.Capture(1).Economy.Buildings.First(b => b.Id == shrine).Researching, Is.EqualTo(SanctuaryTech.HolyRelic));
            Steps(state.gateway, state.sim, state.scenario.Economy.SanctuaryRelicTicks + 1);
            Assert.That(HasTech(state.sim, 1, SanctuaryTech.HolyRelic), Is.True);

            // Both new tech values pass through the replay record (its tech range now reaches 27).
            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                var record = ReplayRunner.Record(stream, state.scenario, state.gateway.Inputs, state.sim.Capture(1).Tick, build);
                Assert.That(record.IsFault, Is.False);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, build);
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
        }

        [Test]
        public void SanctuaryExtensionVersionOneIsReadableAndVersionTwoRoundTrips()
        {
            var scenario = Scenario(true);
            scenario.Economy.SanctuaryRelicBonusPermille = 160;
            scenario.Economy.SanctuaryPilgrimageRadius = 18;
            byte[] current = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(current);
            var extension = decoded.Extensions.Single(e => e.Id == 8);
            Assert.That(extension.Version, Is.EqualTo(2));
            Assert.That(extension.Data.Length, Is.EqualTo(20 * sizeof(int)));
            Assert.That(decoded.Economy.SanctuaryRelicBonusPermille, Is.EqualTo(160));
            Assert.That(decoded.Economy.SanctuaryPilgrimageRadius, Is.EqualTo(18));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(current));

            // Rebuild the same bytes as a version-1 record (the first nine integers only).
            byte[] marker = BitConverter.GetBytes(0x4E545845);
            int markerOffset = -1;
            for (int i = 0; i <= current.Length - marker.Length; i++)
                if (current.Skip(i).Take(marker.Length).SequenceEqual(marker)) { markerOffset = i; break; }
            Assert.That(markerOffset, Is.GreaterThanOrEqualTo(0));
            int sectionLength = BitConverter.ToInt32(current, markerOffset + 8);
            int recordOffset = markerOffset + 12;
            Assert.That(BitConverter.ToInt32(current, recordOffset), Is.EqualTo(8), "the sanctuary record is the only extension here");
            Assert.That(sectionLength, Is.EqualTo(12 + 20 * sizeof(int)));
            Assert.That(markerOffset + 12 + sectionLength, Is.EqualTo(current.Length));
            byte[] old = new byte[recordOffset + 12 + 9 * sizeof(int)];
            Array.Copy(current, old, old.Length);
            Array.Copy(BitConverter.GetBytes(12 + 9 * sizeof(int)), 0, old, markerOffset + 8, sizeof(int));
            Array.Copy(BitConverter.GetBytes(1), 0, old, recordOffset + 4, sizeof(int));
            Array.Copy(BitConverter.GetBytes(9 * sizeof(int)), 0, old, recordOffset + 8, sizeof(int));

            var fromOld = ScenarioBinary.Decode(old);
            Assert.That(fromOld.Economy.Sanctuary, Is.True);
            Assert.That(fromOld.Extensions.Single(e => e.Id == 8).Version, Is.EqualTo(1));
            Assert.That(fromOld.Economy.ShrineWork, Is.EqualTo(scenario.Economy.ShrineWork));
            var defaults = new EconomyRules();
            Assert.That(fromOld.Economy.SanctuaryRelicBonusPermille, Is.EqualTo(defaults.SanctuaryRelicBonusPermille), "版1には研究の値がないので既定値");
            Assert.That(fromOld.Economy.SanctuaryPilgrimageRadius, Is.EqualTo(defaults.SanctuaryPilgrimageRadius));
            byte[] upgraded = ScenarioBinary.Encode(fromOld);
            var reread = ScenarioBinary.Decode(upgraded);
            Assert.That(reread.Extensions.Single(e => e.Id == 8).Version, Is.EqualTo(2));
            Assert.That(ScenarioBinary.Encode(reread), Is.EqualTo(upgraded));
        }

        [Test]
        public void SanctuaryCivilisationReplayIsDeterministicForTwentyThousandTicks()
        {
            var scenario = Scenario(true);
            scenario.UnitParameters = MapGenerator.GenerateTerrain(1).UnitParameters;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong sequence = 0;
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++sequence, CivKind.Sanctuary));
            Steps(gateway, sim, 2);
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                var record = ReplayRunner.Record(stream, scenario, gateway.Inputs, 20000, identity);
                Assert.That(record.IsFault, Is.False);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, identity);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        // ---- V3-18 #3: civilisation choice score, the caravan difference, matches and the normal start ----

        private const int RouteReach = 120; // Simulation.CivSanctuaryRouteReach
        private static readonly MethodInfo NearOutposts = typeof(Battle).GetMethod("CountNearSanctuaryOutposts", Private);
        private static readonly MethodInfo SanctuaryScoreMethod = typeof(Battle).GetMethod("SanctuaryScore", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo CaravanOutposts = typeof(Battle).GetMethod("CountUsableCaravanOutposts", Private);
        private static readonly MethodInfo ChooseCivMethod = typeof(Battle).GetMethod("ChooseCiv", Private);

        private static ScenarioDefinition SanctuaryMatchScenario(ulong seed, bool sanctuary = true)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            s.Economy.Sanctuary = sanctuary;
            s.Economy.StartFood = 50000;
            s.Economy.StartWood = 50000;
            s.Economy.AdvanceFoodCost = 0;
            s.Economy.AdvanceWoodCost = 0;
            s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0;
            s.Economy.Age2WoodCost = 0;
            s.Economy.Age2Ticks = 1;
            s.Economy.Age3FoodCost = 0;
            s.Economy.Age3WoodCost = 0;
            s.Economy.Age3Ticks = 1;
            s.Cores[0].Hp = 1000000;
            s.Cores[1].Hp = 1000000;
            return s;
        }

        private static int NearOf(Battle sim, ScenarioDefinition s, uint faction)
            => (int)NearOutposts.Invoke(sim, new object[] { faction, s.Cores[faction - 1].Position });

        private static int SanctuaryScoreOf(Battle sim, ScenarioDefinition s, uint faction)
            => (int)SanctuaryScoreMethod.Invoke(null, new object[] { sim, faction, s.Cores[faction - 1].Position, 0, 0 });

        private static int CaravanOf(Battle sim, ScenarioDefinition s, uint faction)
            => (int)CaravanOutposts.Invoke(sim, new object[] { faction, s.Cores[faction - 1].Position });

        private static CivKind Choose(Battle sim, uint faction) => (CivKind)ChooseCivMethod.Invoke(sim, new object[] { faction });

        private static int CellOf(ScenarioDefinition s, SimPoint point)
            => (int)(point.Z.Raw / 65536 / s.Map.CellSizeMeters) * s.Map.WidthCells + (int)(point.X.Raw / 65536 / s.Map.CellSizeMeters);

        private static SimPoint CentreOf(ScenarioDefinition s, int cell)
        {
            int size = s.Map.CellSizeMeters;
            return new SimPoint(Fix64.FromInt(cell % s.Map.WidthCells * size + size / 2), Fix64.FromInt(cell / s.Map.WidthCells * size + size / 2));
        }

        private static bool[] BasePassable(ScenarioDefinition s)
        {
            var passable = new bool[s.Map.WidthCells * s.Map.HeightCells];
            for (int i = 0; i < passable.Length; i++) passable[i] = s.Map.DefaultPassable;
            foreach (int blocked in s.Map.BlockedCellIds) passable[blocked] = false;
            return passable;
        }

        /// <summary>Test-side route length in metres over the scenario terrain, or -1 when unreachable.</summary>
        private static int RouteMeters(ScenarioDefinition s, SimPoint from, SimPoint to)
        {
            int edges = Rts.Decision.RouteDistance.Measure(s.Map.WidthCells, s.Map.HeightCells, BasePassable(s), null,
                CellOf(s, from), CellOf(s, to), null);
            return edges < 0 ? -1 : edges * s.Map.CellSizeMeters;
        }

        private static long StraightSquared(SimPoint a, SimPoint b)
        {
            long dx = (a.X.Raw - b.X.Raw) / 65536, dz = (a.Z.Raw - b.Z.Raw) / 65536;
            return dx * dx + dz * dz;
        }

        /// <summary>The first passable cells (in cell order) whose route from <paramref name="from"/> lies in [min, max] metres.</summary>
        private static List<SimPoint> PointsAtRoute(ScenarioDefinition s, SimPoint from, int min, int max, int count)
        {
            var passable = BasePassable(s);
            var found = new List<SimPoint>();
            for (int cell = 0; cell < passable.Length && found.Count < count; cell++)
            {
                if (!passable[cell]) continue;
                var centre = CentreOf(s, cell);
                if (StraightSquared(centre, from) > (long)max * max) continue;
                int route = RouteMeters(s, from, centre);
                if (route < min || route > max) continue;
                bool apart = true;
                foreach (var other in found) if (StraightSquared(other, centre) < 30 * 30) apart = false;
                if (apart) found.Add(centre);
            }
            return found;
        }

        [Test]
        public void SanctuaryScoreTiersOnGeneratedMapsFollowTheNearOutpostCount()
        {
            Assert.That(NearOutposts, Is.Not.Null);
            Assert.That(SanctuaryScoreMethod, Is.Not.Null);
            var seen = new HashSet<int>();
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var s = SanctuaryMatchScenario(seed);
                var sim = new Battle(s);
                for (uint faction = 1; faction <= 2; faction++)
                {
                    var core = s.Cores[faction - 1].Position;
                    int expected = 0;
                    var routes = new List<int>();
                    foreach (var post in s.Outposts)
                    {
                        int route = RouteMeters(s, core, post.Position);
                        routes.Add(route);
                        if (route >= 0 && route <= RouteReach) expected++;
                    }
                    int near = NearOf(sim, s, faction), score = SanctuaryScoreOf(sim, s, faction);
                    TestContext.WriteLine("seed " + seed + " faction " + faction + ": routes=" + string.Join("/", routes)
                        + " near=" + near + " -> " + score + " (civ " + Choose(sim, faction) + ")");
                    Assert.That(near, Is.EqualTo(expected), "seed " + seed + " faction " + faction);
                    Assert.That(score, Is.EqualTo(near >= 2 ? 3 : near == 1 ? 2 : 0));
                    seen.Add(score);
                }
            }
            TestContext.WriteLine("tiers seen: " + string.Join(",", seen.OrderBy(t => t)));
            var off = SanctuaryMatchScenario(3, sanctuary: false);
            Assert.That(SanctuaryScoreOf(new Battle(off), off, 1), Is.EqualTo(0), "旗オフは0点");
        }

        [Test]
        public void SanctuaryScoreCountsZeroOneTwoNearOutpostsAndIgnoresUnreachableOnes()
        {
            var basis = SanctuaryMatchScenario(3);
            var core = basis.Cores[0].Position;
            var near = PointsAtRoute(basis, core, 30, 80, 2);
            var far = PointsAtRoute(basis, core, RouteReach + 20, 400, 2);
            Assert.That(near.Count, Is.EqualTo(2), "コアの近くに置ける場所が2つある");
            Assert.That(far.Count, Is.EqualTo(2), "遠くに置ける場所が2つある");

            (int near, int score) Run(SimPoint first, SimPoint second, Action<Battle, ScenarioDefinition> edit = null)
            {
                var s = SanctuaryMatchScenario(3);
                s.Outposts[0].Position = first;
                s.Outposts[1].Position = second;
                var sim = new Battle(s);
                edit?.Invoke(sim, s);
                return (NearOf(sim, s, 1), SanctuaryScoreOf(sim, s, 1));
            }

            Assert.That(Run(far[0], far[1]), Is.EqualTo((0, 0)), "近い拠点0個は0点");
            Assert.That(Run(near[0], far[1]), Is.EqualTo((1, 2)), "近い拠点1個は2点");
            Assert.That(Run(near[0], near[1]), Is.EqualTo((2, 3)), "近い拠点2個は3点");

            // Walling both near outposts in (on the score's terrain only) leaves outposts within the straight-line
            // reach that the core cannot reach: they do not count.
            void WallIn(Battle sim, ScenarioDefinition s)
            {
                object world = World(sim);
                var config = (ScenarioDefinition)world.GetType().GetField("Config", Private).GetValue(world);
                var blocked = new HashSet<int>(config.Map.BlockedCellIds);
                int width = s.Map.WidthCells, height = s.Map.HeightCells;
                foreach (var post in s.Outposts)
                {
                    int cell = CellOf(s, post.Position), x = cell % width, z = cell / width;
                    for (int dz = -2; dz <= 2; dz++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != 2) continue;
                            int nx = x + dx, nz = z + dz;
                            if (nx >= 0 && nz >= 0 && nx < width && nz < height) blocked.Add(nz * width + nx);
                        }
                }
                config.Map.BlockedCellIds = blocked.OrderBy(c => c).ToArray();
            }
            Assert.That(Run(near[0], near[1], WallIn), Is.EqualTo((0, 0)), "届かない拠点だけなら0点");

            // The other faction's core is far from both near outposts.
            var s2 = SanctuaryMatchScenario(3);
            s2.Outposts[0].Position = near[0];
            s2.Outposts[1].Position = near[1];
            var sim2 = new Battle(s2);
            Assert.That(NearOf(sim2, s2, 2), Is.EqualTo(0), "相手のコアからは遠い");
        }

        /// <summary>A map whose only competing scores are the caravan and the sanctuary: ore and food points removed.</summary>
        private static ScenarioDefinition CaravanAndSanctuaryScenario(SimPoint first, uint firstOwner, SimPoint second)
        {
            var s = SanctuaryMatchScenario(3);
            s.Economy.Caravan = true;
            s.ResourceNodes = s.ResourceNodes.Where(n => n.Kind != ResourceKind.Ore && n.Kind != ResourceKind.Food).ToArray();
            for (int i = 0; i < s.ResourceNodes.Length; i++) s.ResourceNodes[i].Id = (uint)(i + 1);
            s.Outposts[0].Position = first;
            s.Outposts[0].OwnerFactionId = firstOwner;
            s.Outposts[1].Position = second;
            s.Outposts[1].OwnerFactionId = 0;
            return s;
        }

        [Test]
        public void SanctuaryAndCaravanReadTheSameOutpostsDifferently()
        {
            Assert.That(CaravanOutposts, Is.Not.Null);
            // 1. On generated maps the two counts differ: the caravan counts every outpost with room for a
            //    caravanserai, the sanctuary only the outposts close to the core.
            string difference = null;
            for (ulong seed = 1; seed <= 30 && difference == null; seed++)
            {
                var s = SanctuaryMatchScenario(seed);
                s.Economy.Caravan = true;
                var sim = new Battle(s);
                for (uint faction = 1; faction <= 2 && difference == null; faction++)
                {
                    int caravan = CaravanOf(sim, s, faction), near = NearOf(sim, s, faction);
                    if (caravan != near) difference = "seed " + seed + " faction " + faction + ": caravan=" + caravan + " near=" + near;
                }
            }
            TestContext.WriteLine("difference: " + difference);
            Assert.That(difference, Is.Not.Null, "隊商の数と聖地の数は別々に動く");

            // 2. One fixed placement: one outpost near the west core, one far away.
            var basis = SanctuaryMatchScenario(3);
            var core = basis.Cores[0].Position;
            var near2 = PointsAtRoute(basis, core, 30, 80, 2);
            var far2 = PointsAtRoute(basis, core, RouteReach + 20, 400, 2);
            Assert.That(near2.Count, Is.EqualTo(2));
            Assert.That(far2.Count, Is.EqualTo(2));

            (int caravan, int sanctuary, CivKind civ) Read(ScenarioDefinition s)
            {
                var sim = new Battle(s);
                return (CaravanOf(sim, s, 1), SanctuaryScoreOf(sim, s, 1), Choose(sim, 1));
            }

            var neutral = Read(CaravanAndSanctuaryScenario(near2[0], 0, far2[0]));
            TestContext.WriteLine("near neutral + far: " + neutral);
            Assert.That(neutral.sanctuary, Is.EqualTo(2));
            Assert.That(neutral.caravan, Is.EqualTo(2), "どちらの拠点にも隊商宿を建てられる");
            Assert.That(neutral.civ, Is.EqualTo(CivKind.Caravan), "同点なら登録表の順位どおり隊商が先");

            // The same positions, but the near outpost is held by the other side: the caravan can no longer maintain it,
            // while it is still a near outpost for the sanctuary to take.
            var held = Read(CaravanAndSanctuaryScenario(near2[0], 2, far2[0]));
            TestContext.WriteLine("near enemy-held + far: " + held);
            Assert.That(held.caravan, Is.EqualTo(1));
            Assert.That(held.sanctuary, Is.EqualTo(2));
            Assert.That(held.civ, Is.EqualTo(CivKind.Sanctuary), "同じ配置で聖地が選ばれる");

            // Both far: the caravan still counts both, the sanctuary none.
            var bothFar = Read(CaravanAndSanctuaryScenario(far2[0], 0, far2[1]));
            TestContext.WriteLine("both far: " + bothFar);
            Assert.That(bothFar.sanctuary, Is.EqualTo(0));
            Assert.That(bothFar.civ, Is.Not.EqualTo(CivKind.Sanctuary));
        }

        [Test]
        public void SanctuaryOnlyChangesChoicesItWins()
        {
            int won = 0;
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var onScenario = SanctuaryMatchScenario(seed);
                var offScenario = SanctuaryMatchScenario(seed, sanctuary: false);
                var on = new Battle(onScenario);
                var off = new Battle(offScenario);
                for (uint faction = 1; faction <= 2; faction++)
                {
                    var withSanctuary = Choose(on, faction);
                    var without = Choose(off, faction);
                    TestContext.WriteLine("seed " + seed + " faction " + faction + ": on=" + withSanctuary + " off=" + without);
                    Assert.That(without, Is.Not.EqualTo(CivKind.Sanctuary));
                    if (withSanctuary == CivKind.Sanctuary) won++;
                    else Assert.That(withSanctuary, Is.EqualTo(without), "聖地が勝たない陣営の選択は変わらない");
                }
            }
            TestContext.WriteLine("sanctuary chosen: " + won + " / 60");
        }

        [TestCase(CivKind.Sanctuary, CivKind.Agrarian)]
        [TestCase(CivKind.Agrarian, CivKind.Sanctuary)]
        [TestCase(CivKind.Sanctuary, CivKind.Metallurgy)]
        [TestCase(CivKind.Metallurgy, CivKind.Sanctuary)]
        public void SanctuaryCombinationsReachTheSecondAgeAndReplay(CivKind west, CivKind east)
        {
            var scenario = SanctuaryMatchScenario(21);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, west));
            gateway.SubmitEconomy(EconomyCommand.Advance(2, 2, east));
            for (int i = 0; i < 20000 && !sim.Capture(1).Result.HasEnded; i++)
            {
                gateway.Step();
                Assert.That(sim.Capture(1).Result.IsFault, Is.False, "fault at tick " + sim.Capture(1).Tick);
            }
            var result = sim.Capture(1).Result;
            int shrines1 = sim.Capture(1).Economy.Buildings.Count(b => b.Kind == BuildingKind.Shrine);
            int shrines2 = sim.Capture(2).Economy.Buildings.Count(b => b.Kind == BuildingKind.Shrine);
            TestContext.WriteLine(west + " vs " + east + ": tick=" + sim.Capture(1).Tick + ", winner=" + result.WinnerFactionId
                + ", ended=" + result.HasEnded + ", undecided=" + result.IsUndecided
                + ", ages=" + sim.Capture(1).Economy.Age + "/" + sim.Capture(2).Economy.Age
                + ", shrines=" + shrines1 + "/" + shrines2
                + ", villagers=" + sim.Capture(1).Economy.Population + "/" + sim.Capture(2).Economy.Population);
            Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(west));
            Assert.That(sim.Capture(2).Economy.Civ, Is.EqualTo(east));
            Assert.That(sim.Capture(1).Economy.Age, Is.GreaterThanOrEqualTo(2), west + " reaches the second age");
            Assert.That(sim.Capture(2).Economy.Age, Is.GreaterThanOrEqualTo(2), east + " reaches the second age");
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, gateway.Inputs, sim.Capture(1).Tick, identity);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, identity);
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
        }

        private static bool StoneWanted(Battle sim, uint faction)
            => (bool)typeof(Battle).GetMethod("StoneWanted", Private).Invoke(sim, new object[] { faction });

        private static bool NeedsShrineStone(Battle sim, uint faction)
            => (bool)typeof(Battle).GetMethod("SanctuaryNeedsShrineStone", Private).Invoke(sim, new object[] { faction });

        [Test]
        public void SanctuaryWithoutStartingStoneGathersStoneOnlyForItsFirstShrine()
        {
            var noStone = Scenario(true);
            noStone.Economy.StartStone = 0;
            var state = AtCivAge(noStone, CivKind.Sanctuary);
            SetOutpostOwner(state.sim, 1, 1);
            SetOutpostOwner(state.sim, 2, 0);
            SetEconomyField(state.sim, 1, "Stone", 0);
            Assert.That(state.sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.Shrine), Is.False);
            Assert.That(NeedsShrineStone(state.sim, 1), Is.True, "拠点を持ち、祠がなく、石が足りない");
            Assert.That(StoneWanted(state.sim, 1), Is.True, "祠のための石を集める");
            SetEconomyField(state.sim, 1, "Stone", state.scenario.Economy.ShrineStoneCost);
            Assert.That(NeedsShrineStone(state.sim, 1), Is.False, "祠の分の石があれば集めない");
            SetEconomyField(state.sim, 1, "Stone", 0);
            SetOutpostOwner(state.sim, 1, 2);
            Assert.That(NeedsShrineStone(state.sim, 1), Is.False, "拠点を持っていなければ集めない");
            Assert.That(NeedsShrineStone(state.sim, 2), Is.False, "聖地でない陣営は変わらない");

            // Another civilisation in the same situation is unchanged.
            var other = AtCivAge(noStone, CivKind.Agrarian);
            SetOutpostOwner(other.sim, 1, 1);
            SetEconomyField(other.sim, 1, "Stone", 0);
            Assert.That(NeedsShrineStone(other.sim, 1), Is.False, "農耕は変わらない");

            // The automatic sanctuary, starting with no stone at all, still builds its shrine.
            // Stone goes to idle villagers (as for every civilisation once its line runs), so the economy must still be
            // training some: three villagers that are all already at work never become idle.
            var scenario = Scenario(true);
            scenario.Economy.StartStone = 0;
            scenario.Economy.AutoVillagerTarget = 8;
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Sanctuary));
            Steps(gateway, sim, scenario.Economy.AdvanceTicks + 8000);
            Assert.That(sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.Shrine), Is.True, "石0から祠を建てる");
        }

        [Test]
        public void NormalSanctuaryStartFindsASeedAndReplaysForTwentyThousandTicks()
        {
            ulong foundSeed = 0;
            CivKind foundWest = CivKind.Primitive, foundEast = CivKind.Primitive;
            for (ulong seed = 1; seed <= 200 && foundSeed == 0; seed++)
            {
                var probe = new Battle(SanctuaryMatchScenario(seed));
                var probeGateway = new CommandGateway(probe);
                for (int i = 0; i < 1500; i++)
                {
                    probeGateway.Step();
                    if (i % 20 != 19) continue;
                    var west = probe.Capture(1).Economy.Civ;
                    var east = probe.Capture(2).Economy.Civ;
                    if (west == CivKind.Sanctuary || east == CivKind.Sanctuary)
                    {
                        foundSeed = seed;
                        foundWest = west;
                        foundEast = east;
                        break;
                    }
                    if (west != CivKind.Primitive && east != CivKind.Primitive) break;
                }
            }
            if (foundSeed == 0)
            {
                TestContext.WriteLine("聖地が選ばれる種は seed 1..200 では見つからなかった");
                Assert.Inconclusive("聖地が選ばれる種が見つからない");
                return;
            }

            var scenario = SanctuaryMatchScenario(foundSeed);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            Steps(gateway, sim, 20000);
            TestContext.WriteLine("normal sanctuary seed " + foundSeed + ": civs=" + foundWest + "/" + foundEast
                + ", final civs=" + sim.Capture(1).Economy.Civ + "/" + sim.Capture(2).Economy.Civ
                + ", tick=" + sim.Capture(1).Tick + ", ages=" + sim.Capture(1).Economy.Age + "/" + sim.Capture(2).Economy.Age
                + ", shrines=" + sim.Capture(1).Economy.Buildings.Count(b => b.Kind == BuildingKind.Shrine)
                + "/" + sim.Capture(2).Economy.Buildings.Count(b => b.Kind == BuildingKind.Shrine));
            Assert.That(sim.Capture(1).Economy.Civ == CivKind.Sanctuary || sim.Capture(2).Economy.Civ == CivKind.Sanctuary, Is.True);
            Assert.That(sim.Capture(1).Economy.Age, Is.GreaterThanOrEqualTo(2), "通常開始から西が第2時代まで進む");
            Assert.That(sim.Capture(2).Economy.Age, Is.GreaterThanOrEqualTo(2), "通常開始から東が第2時代まで進む");
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, gateway.Inputs, sim.Capture(1).Tick, identity);
                stream.Position = 0;
                var replay = ReplayRunner.Replay(stream, identity);
                Assert.That(replay.FirstMismatchTick, Is.Null);
                Assert.That(replay.IsFault, Is.False);
            }
        }
    }
}
