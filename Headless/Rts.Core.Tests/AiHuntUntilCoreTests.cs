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

        private static void Invoke(Battle battle, string name)
            => typeof(Battle).GetMethod(name, Hidden).Invoke(battle, null);

        [Test]
        public void EnemyCoreFootprintSightIsLatchedAndOutsideFootprintIsIgnored()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle);
            uint enemyCoreId = world.Factions[1].CoreId;
            SimPoint enemyCore = world.Cores[enemyCoreId - 1].Definition.Position;
            long radius = world.Config.Rules.CoreRadius.Raw;
            int inside = -1, outside = -1;
            for (int cell = 0; cell < world.Factions[0].VisibleCells.Length; cell++)
            {
                SimPoint point = world.Map.Center(cell);
                long dx = point.X.Raw - enemyCore.X.Raw, dz = point.Z.Raw - enemyCore.Z.Raw;
                long distance = checked(dx * dx + dz * dz);
                if (distance <= radius * radius && inside < 0) inside = cell;
                if (distance > radius * radius && outside < 0) outside = cell;
            }
            Assert.That(inside, Is.GreaterThanOrEqualTo(0));
            Assert.That(outside, Is.GreaterThanOrEqualTo(0));

            world.Tick = 1;
            world.Factions[0].VisibleCells[inside] = true;
            Invoke(battle, "UpdateAiHuntCoreKnowledge");
            Assert.That(battle.EnemyCoreSeenTickFor(1), Is.EqualTo(1));

            Array.Clear(world.Factions[0].VisibleCells, 0, world.Factions[0].VisibleCells.Length);
            world.Factions[0].VisibleCells[outside] = true;
            world.Tick = 2;
            Invoke(battle, "UpdateAiHuntCoreKnowledge");
            Assert.That(battle.EnemyCoreSeenTickFor(1), Is.EqualTo(1));
        }

        [Test]
        public void AiHuntExtensionRoundTripsRulesAndFactionKnowledge()
        {
            var scenario = LiveGameRules.Create(1, true, true, false, false);
            scenario.Economy.AiHuntGiveUpTicks = 3210;
            scenario.Economy.AiHuntThreatMeters = 31;
            scenario.Economy.AiHuntThreatSoldiers = 6;
            scenario.AiHuntEnemyCoreSeenTicks = new[] { 123L, 456L };
            scenario.AiHuntGaveUp = new[] { false, true };
            byte[] bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);

            Assert.That(decoded.Economy.AiHuntUntilCore, Is.True);
            Assert.That(decoded.Economy.AiHuntGiveUpTicks, Is.EqualTo(3210));
            Assert.That(decoded.Economy.AiHuntThreatMeters, Is.EqualTo(31));
            Assert.That(decoded.Economy.AiHuntThreatSoldiers, Is.EqualTo(6));
            Assert.That(decoded.AiHuntEnemyCoreSeenTicks, Is.EqualTo(new[] { 123L, 456L }));
            Assert.That(decoded.AiHuntGaveUp, Is.EqualTo(new[] { false, true }));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void GiveUpAfterConfiguredTickChangesModeWithoutInventingSeenTick()
        {
            var battle = new Battle(LiveGameRules.Create(1, true, true, false, false));
            var world = World(battle);
            world.Tick = world.Config.Economy.AiHuntGiveUpTicks + 1L;
            Array.Clear(world.Factions[0].VisibleCells, 0, world.Factions[0].VisibleCells.Length);
            Invoke(battle, "UpdateAiHuntCoreKnowledge");

            Assert.That(battle.EnemyCoreSeenTickFor(1), Is.EqualTo(0));
            Assert.That(battle.AiHuntGaveUpFor(1), Is.True);
        }

        [Test]
        public void LargeLiveRulesDefaultOnAndExplicitMeasurementSwitchTurnsItOff()
        {
            Assert.That(LiveGameRules.Create(1, true, true, false, false).Economy.AiHuntUntilCore, Is.True);
            Assert.That(LiveGameRules.Create(1, true, true, false, false, aiHuntUntilCoreOff: true).Economy.AiHuntUntilCore, Is.False);
            Assert.That(LiveGameRules.Create(1, false, true, false, false).Economy.AiHuntUntilCore, Is.False);
        }
    }
}
