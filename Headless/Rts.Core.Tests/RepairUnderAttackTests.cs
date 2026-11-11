using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class RepairUnderAttackTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private static ScenarioDefinition Scenario(bool enabled)
        {
            var scenario = MapGenerator.GenerateTerrain(49001);
            scenario.Economy.Fortify = true;
            scenario.Economy.RepairUnderAttack = enabled;
            scenario.Economy.StartFood = 100000;
            scenario.Economy.StartWood = 100000;
            scenario.Economy.StartStone = 100000;
            scenario.Economy.GatherIntervalTicks = 1000000;
            scenario.Economy.RepairAtPermille = 1000;
            scenario.Economy.RepairFirstMilliHp = 1000;
            scenario.Economy.RepairCostPermille = 0;
            return scenario;
        }

        private static WorldState State(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", Hidden).GetValue(simulation);

        private static (Battle simulation, WorldState state, int buildingIndex, int villagerIndex) RepairScene(bool enabled)
        {
            var simulation = new Battle(Scenario(enabled));
            var gateway = new Rts.Application.CommandGateway(simulation);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, 2, false));
            gateway.Step();
            var state = State(simulation);
            int core = state.Map.Cell(state.Cores[state.Factions[0].CoreId - 1].Definition.Position);
            int cell = core + 3;
            int cellCount = state.Config.Map.WidthCells * state.Config.Map.HeightCells;
            while (cell < cellCount && !state.Map.IsPassable(cell)) cell++;
            Assert.That(cell, Is.LessThan(cellCount));
            gateway.SubmitEconomy(EconomyCommand.PlaceWall(1, 3, new[] { cell }));
            gateway.Step();
            int buildingIndex = Array.FindIndex(state.Buildings, value => value.Alive && value.FactionId == 1 && value.Kind == BuildingKind.Wall);
            Assert.That(buildingIndex, Is.GreaterThanOrEqualTo(0));
            int villagerIndex = Array.FindIndex(state.Villagers, value => value.Alive && value.FactionId == 1);
            Assert.That(villagerIndex, Is.GreaterThanOrEqualTo(0));
            state.Villagers[villagerIndex].Task = VillagerTask.Building;
            state.Villagers[villagerIndex].BuildingId = state.Buildings[buildingIndex].Id;
            return (simulation, state, buildingIndex, villagerIndex);
        }

        private static void MarkDamaged((Battle simulation, WorldState state, int buildingIndex, int villagerIndex) scene)
        {
            ref var building = ref scene.state.Buildings[scene.buildingIndex];
            building.Hp = Math.Max(1, building.Hp - 100);
            var damage = (long[])typeof(Battle).GetField("buildingDamage", Hidden).GetValue(scene.simulation);
            if (damage.Length <= scene.buildingIndex)
            {
                damage = new long[scene.state.BuildingCount];
                typeof(Battle).GetField("buildingDamage", Hidden).SetValue(scene.simulation, damage);
            }
            damage[scene.buildingIndex] = 1;
            scene.state.Tick = 10;
            typeof(Battle).GetMethod("ApplyRaidDamage", Hidden).Invoke(scene.simulation, null);
        }

        private static int RepairFor((Battle simulation, WorldState state, int buildingIndex, int villagerIndex) scene, int firstTick, int count)
        {
            var repair = typeof(Battle).GetMethod("RepairBuildings", Hidden);
            for (int i = 0; i < count; i++)
            {
                scene.state.Tick = firstTick + i;
                repair.Invoke(scene.simulation, null);
            }
            return scene.state.Buildings[scene.buildingIndex].Hp;
        }

        [Test]
        public void ARepairIsQuarterSpeedForOneHundredTicksThenReturnsToFullSpeed()
        {
            var on = RepairScene(true);
            MarkDamaged(on);
            int onStart = on.state.Buildings[on.buildingIndex].Hp;
            int onAfter100 = RepairFor(on, 10, 100);

            var off = RepairScene(false);
            MarkDamaged(off);
            int offStart = off.state.Buildings[off.buildingIndex].Hp;
            int offAfter100 = RepairFor(off, 10, 100);

            Assert.That(on.state.Buildings[on.buildingIndex].LastDamagedTick, Is.EqualTo(11));
            Assert.That(off.state.Buildings[off.buildingIndex].LastDamagedTick, Is.EqualTo(0));
            Assert.That(onAfter100 - onStart, Is.EqualTo((offAfter100 - offStart) / 4));
            int onBefore101 = on.state.Buildings[on.buildingIndex].Hp;
            int onAfter101 = RepairFor(on, 110, 1);
            Assert.That(onAfter101 - onBefore101, Is.EqualTo(1));
            Assert.That(on.simulation.RepairSlowedTicks(1), Is.EqualTo(100));
        }

        [Test]
        public void BNonAttackHpLossDoesNotStartTheSlowWindow()
        {
            var scene = RepairScene(true);
            int start = scene.state.Buildings[scene.buildingIndex].Hp;
            scene.state.Buildings[scene.buildingIndex].Hp = start - 100;
            int damaged = scene.state.Buildings[scene.buildingIndex].Hp;
            int repaired = RepairFor(scene, 10, 1);
            Assert.That(scene.state.Buildings[scene.buildingIndex].LastDamagedTick, Is.EqualTo(0));
            Assert.That(repaired - damaged, Is.EqualTo(1));
            Assert.That(scene.simulation.RepairSlowedTicks(1), Is.EqualTo(0));
        }

        [Test]
        public void CExtension106RoundTripsAndOffAddsNoBytes()
        {
            var on = Scenario(true);
            on.Economy.RepairUnderAttackTicks = 123;
            on.Economy.RepairUnderAttackPermille = 275;
            byte[] bytes = ScenarioBinary.Encode(on);
            var copy = ScenarioBinary.Decode(bytes);
            var extension = copy.Extensions.Single(value => value.Id == 106);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(extension.Data.Length, Is.EqualTo(3 * sizeof(int)));
            Assert.That(copy.Economy.RepairUnderAttack, Is.True);
            Assert.That(copy.Economy.RepairUnderAttackTicks, Is.EqualTo(123));
            Assert.That(copy.Economy.RepairUnderAttackPermille, Is.EqualTo(275));
            Assert.That(ScenarioBinary.Encode(copy), Is.EqualTo(bytes));

            var off = Scenario(false);
            var baseline = Scenario(false);
            Assert.That(off.Extensions.Any(value => value.Id == 106), Is.False);
            Assert.That(ScenarioBinary.Encode(off), Is.EqualTo(ScenarioBinary.Encode(baseline)));
        }
    }
}
