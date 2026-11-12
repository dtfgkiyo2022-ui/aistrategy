using System.Reflection;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>52 (AI level 1): a metal line with no whole belt also gets a hauler at the smelter.</summary>
    [TestFixture]
    public sealed class AiLevelOneSmelterHaulerTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static (int fromMine, int fromSmelter) Haulers(int westLevel, int eastLevel, uint faction, int wanted)
        {
            var scenario = LiveGameRules.Create(55201UL, true, true, false, false);
            scenario.Economy.AiLevelWest = westLevel;
            scenario.Economy.AiLevelEast = eastLevel;
            var simulation = new Battle(scenario);
            var world = (WorldState)typeof(Battle).GetField("world", Private).GetValue(simulation);
            int cell = world.Map.Cell(world.Villagers[0].Position);
            world.Buildings = new BuildingState[2];
            world.Buildings[0] = new BuildingState { Id = 1, FactionId = faction, Kind = BuildingKind.Mine, Alive = true, Complete = true, WorkCell = cell };
            world.Buildings[1] = new BuildingState { Id = 2, FactionId = faction, Kind = BuildingKind.Smelter, Alive = true, Complete = true, WorkCell = cell };
            world.NextBuildingId = 3;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                world.Villagers[i].Task = VillagerTask.Idle;
                world.Villagers[i].HaulFrom = 0;
                world.Villagers[i].Carry = 0;
            }
            var line = new ProcessingLineState { Id = 1, FactionId = faction, Kind = ProcessingLineKind.CoreMetal, MineId = 1, SmelterId = 2 };
            typeof(Battle).GetMethod("SetLineHaulers", Private).Invoke(simulation, new object[] { faction, line, wanted });
            int fromMine = 0, fromSmelter = 0;
            for (int i = 0; i < world.VillagerCount; i++)
            {
                if (!world.Villagers[i].Alive || world.Villagers[i].FactionId != faction) continue;
                if (world.Villagers[i].HaulFrom == 1) fromMine++;
                if (world.Villagers[i].HaulFrom == 2) fromSmelter++;
            }
            return (fromMine, fromSmelter);
        }

        [Test]
        public void LevelOneGivesTheSmelterAHaulerWhenOnlyOneWasAsked()
        {
            Assert.That(Haulers(1, 0, 1, 1), Is.EqualTo((1, 1)));
            Assert.That(Haulers(0, 1, 2, 1), Is.EqualTo((1, 1)));
        }

        [Test]
        public void LevelZeroKeepsTheOldSingleMineHauler()
        {
            Assert.That(Haulers(0, 1, 1, 1), Is.EqualTo((1, 0)));
            Assert.That(Haulers(1, 0, 2, 1), Is.EqualTo((1, 0)));
        }

        [Test]
        public void OtherHaulerCountsAreUnchangedAtLevelOne()
        {
            Assert.That(Haulers(1, 1, 1, 0), Is.EqualTo((0, 0)));
            Assert.That(Haulers(1, 1, 1, 2), Is.EqualTo((1, 1)));
        }
    }
}
