using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class BuildingMemoryTests
    {
        [Test]
        public void BuildingMemoryExtension115RoundTripsAndOffOmitsIt()
        {
            var off = MapGenerator.GenerateTerrain(1UL, false, true);
            off.Economy.BuildingMemory = false;
            off.Extensions = Array.Empty<ScenarioExtensionData>();
            var offDecoded = ScenarioBinary.Decode(ScenarioBinary.Encode(off));
            Assert.That(offDecoded.Economy.BuildingMemory, Is.False);
            Assert.That(offDecoded.Extensions.Any(x => x.Id == 115), Is.False);

            var on = MapGenerator.GenerateTerrain(1UL, false, true);
            on.Economy.BuildingMemory = true;
            on.Extensions = Array.Empty<ScenarioExtensionData>();
            var onDecoded = ScenarioBinary.Decode(ScenarioBinary.Encode(on));
            var extension = onDecoded.Extensions.Single(x => x.Id == 115);
            Assert.That(onDecoded.Economy.BuildingMemory, Is.True);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(extension.Data.Length, Is.EqualTo(sizeof(int)));
            Assert.That(ScenarioBinary.Encode(onDecoded), Is.EqualTo(ScenarioBinary.Encode(on)));
        }

        [Test]
        public void RememberedBuildingsAreCopiedIntoObservationAndFrame()
        {
            var remembered = new[] { new RememberedBuilding(7, BuildingKind.Barracks, new SimPoint(Fix64.FromInt(3), Fix64.FromInt(4)), 4, 20, true, 90) };
            var observation = new FactionObservation(1, 20, Array.Empty<OwnArmyView>(), Array.Empty<VisibleEnemy>(),
                Array.Empty<EnemyContact>(), Array.Empty<KnownObjective>(), rememberedBuildings: remembered);
            var frame = new FactionFrame(20, 1, Array.Empty<RenderUnit>(), observation, Array.Empty<CommandView>(),
                Array.Empty<GameEvent>(), new FogView(Array.Empty<bool>(), Array.Empty<bool>()), default);
            Assert.That(frame.RememberedBuildings.Count, Is.EqualTo(1));
            Assert.That(frame.RememberedBuildings[0].Id, Is.EqualTo(7));
            Assert.That(frame.RememberedBuildings[0].LastSeenTick, Is.EqualTo(20));
        }

        [Test]
        public void LargeLiveRulesEnableBuildingMemoryByDefault()
        {
            Assert.That(LiveGameRules.Create(1, true, true, false, false).Economy.BuildingMemory, Is.True);
            Assert.That(LiveGameRules.Create(1, false, true, false, false).Economy.BuildingMemory, Is.False);
        }

        [Test]
        public void VisibleBuildingRemainsRememberedAndVisibleDestructionClearsIt()
        {
            var simulation = new Battle(LiveGameRules.Create(1, true, true, false, false));
            for (long tick = 1; tick <= 6000; tick++) simulation.Step(tick, Array.Empty<ScheduledInput>());
            var initial = simulation.Capture(1);
            var enemy = initial.Economy.Buildings.FirstOrDefault(x => x.FactionId != 1 && !x.Reserved);
            Assert.That(enemy.Id, Is.Not.EqualTo(0), "the live economy must have an enemy building by tick 1200");

            SetAllVisible(simulation, 1, true);
            Invoke(simulation, "UpdateBuildingMemory");
            Invoke(simulation, "PublishFrames");
            SetAllVisible(simulation, 1, false);
            Invoke(simulation, "UpdateBuildingMemory");
            Invoke(simulation, "PublishFrames");
            var remembered = simulation.Capture(1).RememberedBuildings.Single(x => x.Id == enemy.Id);
            Assert.That(remembered.Kind, Is.EqualTo(enemy.Kind));
            Assert.That(remembered.LastSeenTick, Is.EqualTo(simulation.Capture(1).Tick));

            SetAlive(simulation, enemy.Id, false);
            Invoke(simulation, "UpdateBuildingMemory");
            Invoke(simulation, "PublishFrames");
            Assert.That(simulation.Capture(1).RememberedBuildings.Any(x => x.Id == enemy.Id), Is.True,
                "an unseen destruction must not erase the memory");

            SetAllVisible(simulation, 1, true);
            Invoke(simulation, "UpdateBuildingMemory");
            Invoke(simulation, "PublishFrames");
            Assert.That(simulation.Capture(1).RememberedBuildings.Any(x => x.Id == enemy.Id), Is.False,
                "revisiting the footprint with no building clears it");
        }

        private static void SetAllVisible(Battle simulation, int faction, bool value)
        {
            object world = Field(simulation, "world").GetValue(simulation);
            var factions = (Array)Field(world, "Factions").GetValue(world);
            object state = factions.GetValue(faction - 1);
            var visible = (bool[])Field(state, "VisibleCells").GetValue(state);
            for (int i = 0; i < visible.Length; i++) visible[i] = value;
        }

        private static void SetAlive(Battle simulation, uint id, bool alive)
        {
            object world = Field(simulation, "world").GetValue(simulation);
            var buildings = (Array)Field(world, "Buildings").GetValue(world);
            for (int i = 0; i < buildings.Length; i++)
            {
                object building = buildings.GetValue(i);
                if ((uint)Field(building, "Id").GetValue(building) != id) continue;
                Field(building, "Alive").SetValue(building, alive);
                buildings.SetValue(building, i);
                return;
            }
            Assert.Fail("building not found: " + id);
        }

        private static void Invoke(Battle simulation, string name)
            => simulation.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(simulation, null);

        private static FieldInfo Field(object target, string name)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    }
}
