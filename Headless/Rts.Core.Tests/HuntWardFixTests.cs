using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class HuntWardFixTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private static WorldState World(Battle battle)
            => (WorldState)typeof(Battle).GetField("world", Hidden).GetValue(battle);

        private static object Invoke(Battle battle, string name, params object[] args)
            => typeof(Battle).GetMethod(name, Hidden).Invoke(battle, args);

        [Test]
        public void Extension131RoundTripsAndIsAbsentWhenOff()
        {
            var on = LiveGameRules.Create(1311, true, true, false, false);
            on.Extensions = Array.Empty<ScenarioExtensionData>();
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(on));
            Assert.That(decoded.Economy.HuntWardFix, Is.True);
            Assert.That(decoded.Extensions.Single(x => x.Id == 131).Data.Length, Is.EqualTo(sizeof(byte) + 2 * sizeof(int)));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(ScenarioBinary.Encode(on)));

            on.Economy.HuntWardFix = false;
            var off = ScenarioBinary.Decode(ScenarioBinary.Encode(on));
            Assert.That(off.Economy.HuntWardFix, Is.False);
            Assert.That(off.Extensions.Any(x => x.Id == 131), Is.False);
        }

        [Test]
        public void FlagOffKeepsEveryTickHashStableAfterScenarioRoundTrip()
        {
            var source = LiveGameRules.Create(1312, true, true, false, false);
            source.Economy.HuntWardFix = false;
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(source));
            var left = new Battle(source); var right = new Battle(decoded);
            for (long tick = 1; tick <= 120; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>()); right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void ReserveArmyCanChooseHuntCampOnlyWhenFixIsOn()
        {
            var on = new Battle(LiveGameRules.Create(1313, true, true, false, false));
            var onWorld = World(on);
            var onArmy = onWorld.Armies.First(a => a.Definition.FactionId == 1 && a.Definition.Role == "reserve");
            int campIndex = Array.FindIndex(onWorld.NeutralCamps, camp => camp.Kind == NeutralKind.WolfPack);
            Assert.That(campIndex, Is.GreaterThanOrEqualTo(0));
            for (int i = 0; i < onWorld.NeutralCamps.Length; i++)
                foreach (uint neutralId in onWorld.NeutralCamps[i].NeutralIds)
                { var neutral = onWorld.Neutrals[neutralId - 1]; neutral.Alive = i == campIndex; neutral.Hp = i == campIndex ? 1 : 0; onWorld.Neutrals[neutralId - 1] = neutral; }
            onWorld.NeutralCampMemories[campIndex] = new NeutralCampMemoryState { LastKnownAlive = true, LastKnownHp = 1, LastKnownMaxHp = 1, LastSeenTick = 1 };
            foreach (uint soldierId in onArmy.SoldierIds)
            { var soldier = onWorld.Soldiers[soldierId - 1]; soldier.Alive = true; soldier.Hp = soldier.Parameters.Hp; soldier.Position = onWorld.NeutralCamps[campIndex].Position; onWorld.Soldiers[soldierId - 1] = soldier; }
            onArmy.Decision = new ArmyDecisionMemory(AssignmentKind.Advance, default(PolicyGoal), false, 0, 0);
            onWorld.Armies[onArmy.Definition.Id - 1] = onArmy;
            var onArgs = new object[] { 1U, onArmy, null };
            var onCamp = (uint)Invoke(on, "ChooseHuntCamp", onArgs);
            Assert.That(onCamp, Is.GreaterThan(0));

            var off = new Battle(LiveGameRules.Create(1313, true, true, false, false));
            var offWorld = World(off);
            offWorld.Config.Economy.HuntWardFix = false;
            var offArmy = offWorld.Armies.First(a => a.Definition.FactionId == 1 && a.Definition.Role == "reserve");
            Assert.That((bool)Invoke(off, "HuntRoleBlocked", 1U, offArmy), Is.True);
        }

        [Test]
        public void OnePendingWardPerFactionAndKindTimesOutToIdle()
        {
            var scenario = LiveGameRules.Create(1314, false, true, false, false);
            scenario.Economy.Wards = true; scenario.Economy.HuntWardFix = true;
            scenario.Economy.WardHiddenWoodCost = 0; scenario.Economy.HuntWardPlacementGiveUpTicks = 600;
            var battle = new Battle(scenario); var world = World(battle);
            var first = world.Villagers.First(v => v.FactionId == 1 && v.Alive);
            int targetCell = Enumerable.Range(0, world.Config.Map.WidthCells * world.Config.Map.HeightCells)
                .First(cell => world.Map.IsPassableFor(cell, 1) && cell != world.Map.Cell(first.Position));
            Assert.That((bool)Invoke(battle, "QueueAutomaticWardPlacement", 1U, WardKind.Hidden, first.Id, targetCell, false), Is.True);
            battle.UpdateHuntWardMetrics();
            Assert.That((bool)Invoke(battle, "TryAutomaticWard", 1U, WardKind.Hidden, world.Map.Center(targetCell)), Is.False);
            world.Tick = 601; Invoke(battle, "MoveVillagers");
            var after = world.Villagers[first.Id - 1];
            Assert.That(after.WardPlacePending, Is.False);
            Assert.That(after.Task, Is.EqualTo(VillagerTask.Idle));
            Assert.That(battle.WardAbandonedCountFor(1), Is.EqualTo(1));
            Assert.That(battle.WardPendingVillagerMaxFor(1), Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void PendingVillagerUsesRouteTargetInsteadOfStraightClipMove()
        {
            var scenario = LiveGameRules.Create(1315, false, true, false, false);
            scenario.Economy.Wards = true; scenario.Economy.HuntWardFix = true;
            scenario.Economy.WardHiddenWoodCost = 0;
            var battle = new Battle(scenario); var world = World(battle);
            var villager = world.Villagers.First(v => v.FactionId == 1 && v.Alive);
            int start = world.Map.Cell(villager.Position);
            int target = Enumerable.Range(0, world.Config.Map.WidthCells * world.Config.Map.HeightCells)
                .Where(cell => world.Map.IsPassableFor(cell, 1))
                .OrderByDescending(cell => Math.Abs(cell % world.Config.Map.WidthCells - start % world.Config.Map.WidthCells)
                    + Math.Abs(cell / world.Config.Map.WidthCells - start / world.Config.Map.WidthCells))
                .First();
            Assert.That((bool)Invoke(battle, "QueueAutomaticWardPlacement", 1U, WardKind.Hidden, villager.Id, target, false), Is.True);
            world.Tick = 1; Invoke(battle, "MoveVillagers");
            var after = world.Villagers[villager.Id - 1];
            Assert.That(after.WardPlacePending, Is.True);
            Assert.That(after.RouteGoal, Is.EqualTo(world.Map.Center(target)));
            Assert.That(after.Route, Is.Not.Null);
            Assert.That(after.Route.Length, Is.GreaterThan(0));
        }
    }
}
