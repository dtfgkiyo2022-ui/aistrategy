using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class AiHuntUntilCoreTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private static WorldState World(Battle battle)
            => (WorldState)typeof(Battle).GetField("world", Hidden).GetValue(battle);

        private static object Invoke(Battle battle, string name, params object[] args)
            => typeof(Battle).GetMethod(name, Hidden).Invoke(battle, args);

        private static SimPoint Core(WorldState world, uint faction)
            => world.Cores[world.Factions[faction - 1].CoreId - 1].Definition.Position;

        private static bool MoreThan(SimPoint a, SimPoint b, int meters)
        {
            long dx = a.X.Raw - b.X.Raw, dz = a.Z.Raw - b.Z.Raw;
            long radius = Fix64.FromInt(meters).Raw;
            return dx * dx + dz * dz > radius * radius;
        }

        private static void SetArmy(WorldState world, uint faction, uint armyId, SimPoint position, int hp = 100)
        {
            var army = world.Armies[armyId - 1];
            foreach (uint id in army.SoldierIds)
            {
                var soldier = world.Soldiers[id - 1];
                soldier.Alive = true; soldier.Hp = hp; soldier.Position = position;
                world.Soldiers[id - 1] = soldier;
            }
            army.Decision = new ArmyDecisionMemory(AssignmentKind.Advance, default(PolicyGoal), false, 0, 0);
            army.Policy = default(PolicyKind); army.Goal = default(PolicyGoal);
            world.Armies[armyId - 1] = army;
            foreach (uint otherId in world.Factions[faction - 1].ArmyIds)
                if (otherId != armyId)
                    foreach (uint id in world.Armies[otherId - 1].SoldierIds)
                    {
                        var soldier = world.Soldiers[id - 1]; soldier.Alive = false; world.Soldiers[id - 1] = soldier;
                    }
        }

        private static void SetTwoArmies(WorldState world, uint faction, uint firstId, SimPoint first, uint secondId, SimPoint second)
        {
            foreach (uint armyId in world.Factions[faction - 1].ArmyIds)
                foreach (uint id in world.Armies[armyId - 1].SoldierIds)
                {
                    var soldier = world.Soldiers[id - 1]; soldier.Alive = false; world.Soldiers[id - 1] = soldier;
                }
            SetArmyWithoutDisablingOthers(world, firstId, first);
            SetArmyWithoutDisablingOthers(world, secondId, second);
        }

        private static void SetArmyWithoutDisablingOthers(WorldState world, uint armyId, SimPoint position, int hp = 100)
        {
            var army = world.Armies[armyId - 1];
            foreach (uint id in army.SoldierIds)
            {
                var soldier = world.Soldiers[id - 1];
                soldier.Alive = true; soldier.Hp = hp; soldier.Position = position;
                world.Soldiers[id - 1] = soldier;
            }
            army.Decision = new ArmyDecisionMemory(AssignmentKind.Advance, default(PolicyGoal), false, 0, 0);
            army.Policy = default(PolicyKind); army.Goal = default(PolicyGoal);
            world.Armies[armyId - 1] = army;
        }

        private static int Camp(WorldState world, Predicate<NeutralCampState> predicate)
            => Array.FindIndex(world.NeutralCamps, predicate);

        private static void ClearCamps(WorldState world)
        {
            for (int i = 0; i < world.NeutralCamps.Length; i++)
                foreach (uint id in world.NeutralCamps[i].NeutralIds)
                {
                    var neutral = world.Neutrals[id - 1]; neutral.Alive = false; neutral.Hp = 0; world.Neutrals[id - 1] = neutral;
                }
        }

        private static void PrepareCamp(WorldState world, int campIndex, int hp)
        {
            ClearCamps(world);
            foreach (uint id in world.NeutralCamps[campIndex].NeutralIds)
            {
                var neutral = world.Neutrals[id - 1]; neutral.Alive = true; neutral.Hp = hp; world.Neutrals[id - 1] = neutral;
            }
        }

        private static void SetObservation(Battle battle, uint faction, params VisibleEnemy[] enemies)
        {
            var frames = (FactionFrame[])typeof(Battle).GetField("frames", Hidden).GetValue(battle);
            var observation = new FactionObservation(faction, 1, Array.Empty<OwnArmyView>(), enemies,
                Array.Empty<EnemyContact>(), Array.Empty<KnownObjective>());
            frames[faction - 1] = new FactionFrame(1, faction, Array.Empty<RenderUnit>(), observation,
                Array.Empty<CommandView>(), Array.Empty<GameEvent>(), new FogView(Array.Empty<bool>(), Array.Empty<bool>()),
                new MatchResult(false, 0, false, false, true));
        }

        [Test]
        public void EnemyCoreFootprintSightIsLatchedAndOutsideFootprintIsIgnored()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle);
            var cache = (int[][])typeof(Battle).GetField("aiHuntCoreFootprintCells", Hidden).GetValue(battle);
            Assert.That(cache[0], Is.Not.Empty); Assert.That(cache[0], Is.Ordered);
            Assert.That(cache[0].Length, Is.LessThan(world.Factions[0].VisibleCells.Length));
            SimPoint enemyCore = Core(world, 2); long radius = world.Config.Rules.CoreRadius.Raw;
            int inside = -1, outside = -1;
            for (int cell = 0; cell < world.Factions[0].VisibleCells.Length; cell++)
            {
                SimPoint point = world.Map.Center(cell); long dx = point.X.Raw - enemyCore.X.Raw, dz = point.Z.Raw - enemyCore.Z.Raw;
                long distance = checked(dx * dx + dz * dz);
                if (distance <= radius * radius && inside < 0) inside = cell;
                if (distance > radius * radius && outside < 0) outside = cell;
            }
            Assert.That(inside, Is.GreaterThanOrEqualTo(0)); Assert.That(outside, Is.GreaterThanOrEqualTo(0));
            Array.Clear(world.Factions[0].VisibleCells, 0, world.Factions[0].VisibleCells.Length);
            world.Tick = 1; world.Factions[0].VisibleCells[outside] = true; Invoke(battle, "UpdateAiHuntCoreKnowledge");
            Assert.That(battle.EnemyCoreSeenTickFor(1), Is.EqualTo(0));
            world.Factions[0].VisibleCells[inside] = true; Invoke(battle, "UpdateAiHuntCoreKnowledge");
            Assert.That(battle.EnemyCoreSeenTickFor(1), Is.EqualTo(1));
            Array.Clear(world.Factions[0].VisibleCells, 0, world.Factions[0].VisibleCells.Length);
            world.Factions[0].VisibleCells[outside] = true; world.Tick = 2; Invoke(battle, "UpdateAiHuntCoreKnowledge");
            Assert.That(battle.EnemyCoreSeenTickFor(1), Is.EqualTo(1));
        }

        [Test]
        public void BeforeCoreHuntUsesFarEnemySideCampButRejectsWeakCampAndEnemyCore()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle); world.Config.NeutralRules.HuntIntervalTicks = 1; world.Config.NeutralRules.HuntStrengthPermille = 1200; world.Tick = 1;
            SimPoint ownCore = Core(world, 1);
            int target = Camp(world, c => c.Kind == NeutralKind.WolfPack && !world.NeutralCampOwnSide[Array.IndexOf(world.NeutralCamps, c)] && MoreThan(c.Position, ownCore, 60));
            int weak = Camp(world, c => c.Kind == NeutralKind.WolfPack && world.NeutralCampOwnSide[Array.IndexOf(world.NeutralCamps, c)] && Array.IndexOf(world.NeutralCamps, c) != target);
            Assert.That(target, Is.GreaterThanOrEqualTo(0)); Assert.That(weak, Is.GreaterThanOrEqualTo(0));
            PrepareCamp(world, target, 1);
            foreach (uint id in world.NeutralCamps[weak].NeutralIds)
            {
                var neutral = world.Neutrals[id - 1]; neutral.Alive = true; neutral.Hp = 10000; world.Neutrals[id - 1] = neutral;
            }
            uint armyId = world.Factions[0].ArmyIds[0]; SetArmy(world, 1, armyId, ownCore, 100); Invoke(battle, "NeutralThink");
            var army = world.Armies[armyId - 1];
            Assert.That(army.Decision.Goal.Kind, Is.EqualTo(GoalKind.Point));
            Assert.That(army.Decision.Goal.Point.X.Raw, Is.EqualTo(world.NeutralCamps[target].Position.X.Raw));
            Assert.That(army.Decision.Goal.Point.Z.Raw, Is.EqualTo(world.NeutralCamps[target].Position.Z.Raw));
            Assert.That(army.Decision.Goal.Kind == GoalKind.Core && army.Decision.Goal.Id == world.Factions[1].CoreId, Is.False);
        }

        [Test]
        public void NoHuntableCampUsesEarliestRespawnCamp()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle); world.Config.NeutralRules.HuntIntervalTicks = 1; world.Config.NeutralRules.HuntStrengthPermille = 0; world.Tick = 1;
            ClearCamps(world); int first = Camp(world, c => c.Kind == NeutralKind.WolfPack); int second = Camp(world, c => c.Kind == NeutralKind.RockBeast);
            Assert.That(first, Is.GreaterThanOrEqualTo(0)); Assert.That(second, Is.GreaterThanOrEqualTo(0));
            var firstState = world.NeutralCamps[first]; firstState.NextSpawnTick = 30; world.NeutralCamps[first] = firstState;
            var secondState = world.NeutralCamps[second]; secondState.NextSpawnTick = 60; world.NeutralCamps[second] = secondState;
            uint armyId = world.Factions[0].ArmyIds[0]; SetArmy(world, 1, armyId, Core(world, 1), 100); Invoke(battle, "NeutralThink");
            var army = world.Armies[armyId - 1];
            Assert.That(army.Decision.Goal.Kind, Is.EqualTo(GoalKind.Point));
            Assert.That(army.Decision.Goal.Point.X.Raw, Is.EqualTo(world.NeutralCamps[first].Position.X.Raw));
            Assert.That(army.Decision.Goal.Point.Z.Raw, Is.EqualTo(world.NeutralCamps[first].Position.Z.Raw));
            Assert.That(army.Decision.Assignment, Is.EqualTo(AssignmentKind.Guard));
        }

        [Test]
        public void FiveVisibleThreatSoldiersReturnBeforeCoreButFourDoNot()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle); world.Config.NeutralRules.HuntIntervalTicks = 1; world.Tick = 1;
            int camp = Camp(world, c => c.Kind == NeutralKind.WolfPack && world.NeutralCampOwnSide[Array.IndexOf(world.NeutralCamps, c)]);
            PrepareCamp(world, camp, 1); uint armyId = world.Factions[0].ArmyIds[0]; SetArmy(world, 1, armyId, world.NeutralCamps[camp].Position, 100);
            SimPoint ownCore = Core(world, 1);
            SetObservation(battle, 1, Enumerable.Range(1, 5).Select(id => new VisibleEnemy((uint)id, ownCore, (byte)UnitKind.Infantry)).ToArray());
            Invoke(battle, "NeutralThink"); Assert.That(world.Armies[armyId - 1].Decision.Assignment, Is.EqualTo(AssignmentKind.Guard));
            Assert.That(world.Armies[armyId - 1].Decision.Goal.Point, Is.EqualTo(ownCore));
            var reset = world.Armies[armyId - 1]; reset.Decision = new ArmyDecisionMemory(AssignmentKind.Advance, default(PolicyGoal), false, 0, 0); reset.Goal = default(PolicyGoal); world.Armies[armyId - 1] = reset;
            SetObservation(battle, 1, Enumerable.Range(1, 4).Select(id => new VisibleEnemy((uint)id, ownCore, (byte)UnitKind.Infantry)).ToArray()); world.Tick = 2;
            Invoke(battle, "NeutralThink"); Assert.That(world.Armies[armyId - 1].Decision.Assignment, Is.EqualTo(AssignmentKind.Advance));
            Assert.That(world.Armies[armyId - 1].Decision.Goal.Point, Is.EqualTo(world.NeutralCamps[camp].Position));
        }

        [Test]
        public void AfterCoreSeenThreatResponseChoosesCoreWhenItIsCloserOtherwiseDefense()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle); world.Config.NeutralRules.HuntIntervalTicks = 1; world.Tick = 1; world.Factions[0].EnemyCoreSeenTick = 1;
            uint firstId = world.Factions[0].ArmyIds[0], secondId = world.Factions[0].ArmyIds[1]; SimPoint ownCore = Core(world, 1), enemyCore = Core(world, 2);
            SetTwoArmies(world, 1, firstId, enemyCore, secondId, ownCore);
            SetObservation(battle, 1, Enumerable.Range(1, 5).Select(id => new VisibleEnemy((uint)id, ownCore, (byte)UnitKind.Infantry)).ToArray());
            Invoke(battle, "NeutralThink");
            Assert.That(world.Armies[firstId - 1].Decision.Goal.Kind, Is.EqualTo(GoalKind.Core)); Assert.That(world.Armies[firstId - 1].Decision.Goal.Id, Is.EqualTo(world.Factions[1].CoreId));
            Assert.That(world.Armies[secondId - 1].Decision.Assignment, Is.EqualTo(AssignmentKind.Guard)); Assert.That(world.Armies[secondId - 1].Decision.Goal.Point, Is.EqualTo(ownCore));
        }

        [Test]
        public void GiveUpAfterConfiguredTickIsHandledAsKnown()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false)); var world = World(battle);
            world.Tick = world.Config.Economy.AiHuntGiveUpTicks + 1L; Array.Clear(world.Factions[0].VisibleCells, 0, world.Factions[0].VisibleCells.Length);
            Invoke(battle, "UpdateAiHuntCoreKnowledge"); Assert.That(battle.EnemyCoreSeenTickFor(1), Is.EqualTo(0)); Assert.That(battle.AiHuntGaveUpFor(1), Is.True);
            Assert.That((bool)Invoke(battle, "AiHuntBeforeCore", 1u), Is.False);
        }

        [Test]
        public void LargeLiveRulesDefaultOnAndExplicitMeasurementSwitchTurnsItOff()
        {
            Assert.That(LiveGameRules.Create(1, true, true, false, false).Economy.AiHuntUntilCore, Is.True);
            Assert.That(LiveGameRules.Create(1, true, true, false, false, aiHuntUntilCoreOff: true).Economy.AiHuntUntilCore, Is.False);
            Assert.That(LiveGameRules.Create(1, false, true, false, false).Economy.AiHuntUntilCore, Is.False);
            var off = new Battle(LiveGameRules.Create(1, true, true, false, false, aiHuntUntilCoreOff: true)); Assert.That(off.EnemyCoreSeenTickFor(1), Is.EqualTo(0)); Assert.That(off.AiHuntGaveUpFor(1), Is.False);
        }

        [Test]
        public void AiHuntExtensionRoundTripsOnlyRulesAndFlag()
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            scenario.Economy.AiHuntGiveUpTicks = 3210; scenario.Economy.AiHuntThreatMeters = 31; scenario.Economy.AiHuntThreatSoldiers = 6;
            byte[] bytes = ScenarioBinary.Encode(scenario); var decoded = ScenarioBinary.Decode(bytes); var extension = decoded.Extensions.Single(x => x.Id == 127);
            Assert.That(extension.Version, Is.EqualTo(1)); Assert.That(extension.Data.Length, Is.EqualTo(sizeof(byte) + 3 * sizeof(int)));
            Assert.That(decoded.Economy.AiHuntUntilCore, Is.True); Assert.That(decoded.Economy.AiHuntGiveUpTicks, Is.EqualTo(3210)); Assert.That(decoded.Economy.AiHuntThreatMeters, Is.EqualTo(31)); Assert.That(decoded.Economy.AiHuntThreatSoldiers, Is.EqualTo(6));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
            var battle = new Battle(decoded); var world = World(battle); world.Factions[0].EnemyCoreSeenTick = 99; world.Factions[0].AiHuntGaveUp = true;
            Assert.That(ScenarioBinary.Decode(ScenarioBinary.Encode(decoded)).Economy.AiHuntUntilCore, Is.True);
        }
    }
}
