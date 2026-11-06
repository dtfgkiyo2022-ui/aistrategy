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
    public sealed class RaidVillagersFirstTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private static WorldState State(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", Hidden).GetValue(simulation);

        private static (Battle simulation, WorldState state, SoldierState soldier, ArmyState army) RetargetScene(bool enabled = true)
        {
            var scenario = MapGenerator.GenerateTerrain(9501);
            scenario.Economy.RaidVillagersFirst = enabled;
            scenario.Economy.StartFood = 100000;
            scenario.Economy.StartWood = 100000;
            var simulation = new Battle(scenario);
            var state = State(simulation);
            var soldierIndex = Enumerable.Range(0, state.SoldierCount)
                .First(index => state.Soldiers[index].Alive && state.Soldiers[index].Initial.FactionId == 1);
            var villagerIndex = Enumerable.Range(0, state.VillagerCount)
                .First(index => state.Villagers[index].Alive && state.Villagers[index].FactionId == 2);
            var soldier = state.Soldiers[soldierIndex];
            soldier.TargetKind = 4;
            soldier.TargetId = 1;
            soldier.IsRetreating = false;
            soldier.Position = state.Villagers[villagerIndex].Position;
            state.Soldiers[soldierIndex] = soldier;
            state.Villagers[villagerIndex].Position = soldier.Position;
            Array.Fill(state.Factions[0].VisibleCells, true);
            simulation.RebuildSpatialCombatIndexForTest();
            state.Tick = (10 - soldier.Initial.Id % 10) % 10;
            return (simulation, state, soldier, state.Armies[soldier.Initial.ArmyId - 1]);
        }

        private static SoldierState Retarget(Battle simulation, SoldierState soldier, ArmyState army, bool canChase = true)
        {
            var args = new object[] { soldier, army, canChase };
            typeof(Battle).GetMethod("RetargetRaidVillagerFirst", Hidden).Invoke(simulation, args);
            return (SoldierState)args[0];
        }

        [Test]
        public void Extension95RoundTripsAsOneEnabledInt32()
        {
            var source = LiveGameRules.Create(9502, true, true, false, false);
            byte[] bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(value => value.Id == 95);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(extension.Data.Length, Is.EqualTo(sizeof(int)));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void LiveLargeMapEnablesTheFlagButNormalMapDoesNot()
        {
            Assert.That(LiveGameRules.Create(9503, true, true, false, false).Economy.RaidVillagersFirst, Is.True);
            Assert.That(LiveGameRules.Create(9504, false, true, false, false).Economy.RaidVillagersFirst, Is.False);
        }

        [Test]
        public void RetargetChoosesAVisibleVillagerAtTheNearestTick()
        {
            var scene = RetargetScene();
            var result = Retarget(scene.simulation, scene.soldier, scene.army);
            Assert.That(result.TargetKind, Is.EqualTo(3));
            Assert.That(scene.state.Economies[0].RaidVillagerRetargets, Is.EqualTo(1));
        }

        [Test]
        public void RetargetDoesNotSwitchBetweenTenTickChecks()
        {
            var scene = RetargetScene();
            scene.state.Tick++;
            var result = Retarget(scene.simulation, scene.soldier, scene.army);
            Assert.That(result.TargetKind, Is.EqualTo(4));
            Assert.That(scene.state.Economies[0].RaidVillagerRetargets, Is.EqualTo(0));
        }

        [Test]
        public void VillagerJustOutOfRangeDrawsTheSoldierCloser()
        {
            var scene = RetargetScene();
            var soldier = scene.soldier;
            int keep = Enumerable.Range(0, scene.state.VillagerCount)
                .First(index => scene.state.Villagers[index].Alive && scene.state.Villagers[index].FactionId == 2);
            for (int index = 0; index < scene.state.VillagerCount; index++)
                if (index != keep && scene.state.Villagers[index].FactionId == 2) scene.state.Villagers[index].Alive = false;
            var villager = scene.state.Villagers[keep];
            soldier.Position = new SimPoint(villager.Position.X + soldier.Parameters.Range + Fix64.FromInt(2), villager.Position.Z);
            scene.simulation.RebuildSpatialCombatIndexForTest();
            var result = Retarget(scene.simulation, soldier, scene.army);
            Assert.That(result.TargetKind, Is.EqualTo(4));
            Assert.That(result.MoveGoal, Is.EqualTo(villager.Position));
            Assert.That(scene.state.Economies[0].RaidVillagerChases, Is.EqualTo(1));

            var held = Retarget(scene.simulation, soldier, scene.army, false);
            Assert.That(held.MoveGoal, Is.EqualTo(soldier.MoveGoal));
        }

        [Test]
        public void FlagOffDoesNotRetargetOrIncrementTheDiagnostic()
        {
            var scene = RetargetScene(false);
            var result = Retarget(scene.simulation, scene.soldier, scene.army);
            Assert.That(result.TargetKind, Is.EqualTo(4));
            Assert.That(scene.state.Economies[0].RaidVillagerRetargets, Is.EqualTo(0));
        }

        [Test]
        public void RamDoesNotChangeItsBuildingTarget()
        {
            var scene = RetargetScene();
            var soldier = scene.soldier;
            soldier.Initial.Kind = UnitKind.Ram;
            var result = Retarget(scene.simulation, soldier, scene.army);
            Assert.That(result.TargetKind, Is.EqualTo(4));
            Assert.That(scene.state.Economies[0].RaidVillagerRetargets, Is.EqualTo(0));
        }

        [Test]
        public void CoreTargetIsLeftAloneEvenWithAVillagerInRange()
        {
            var scene = RetargetScene();
            var soldier = scene.soldier;
            soldier.TargetKind = 2;
            soldier.TargetId = 2;
            var result = Retarget(scene.simulation, soldier, scene.army);
            Assert.That(result.TargetKind, Is.EqualTo(2));
            Assert.That(scene.state.Economies[0].RaidVillagerRetargets, Is.EqualTo(0));
        }

        [Test]
        public void FlagOffCanonicalHashesRemainEqualAfterEncoding()
        {
            var left = MapGenerator.GenerateTerrain(9505);
            left.Economy.RaidVillagersFirst = false;
            var right = ScenarioBinary.Decode(ScenarioBinary.Encode(left));
            var a = new Battle(left);
            var b = new Battle(right);
            for (long tick = 1; tick <= 100; tick++)
            {
                a.Step(tick, Array.Empty<ScheduledInput>());
                b.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(a.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(b.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }
    }
}
