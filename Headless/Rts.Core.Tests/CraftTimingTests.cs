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
    public sealed class CraftTimingTests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void CraftTimingExtension34RoundTripsAllValues()
        {
            var scenario = MapGenerator.GenerateTerrain(7341UL);
            scenario.Economy.Ages = true;
            scenario.Economy.Industry = true;
            scenario.Economy.CraftTiming = true;
            scenario.Economy.CraftTimingFoodPerWood = 7;
            scenario.Extensions = Array.Empty<ScenarioExtensionData>();

            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(x => x.Id == 34);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.CraftTiming, Is.True);
            Assert.That(decoded.Economy.CraftTimingFoodPerWood, Is.EqualTo(7));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void CraftTimingOffDoesNotWriteExtensionAndMatchesEveryTick()
        {
            var scenario = LiveGameRules.Create(7342UL, false, false, false, false);
            scenario.Economy.CraftTiming = false;
            scenario.Extensions = Array.Empty<ScenarioExtensionData>();
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Any(x => x.Id == 34), Is.False);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var left = new Battle(scenario);
            var right = new Battle(decoded);
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(Hash(left), Is.EqualTo(Hash(right)), "tick " + tick);
            }
        }

        [Test]
        public void CraftTimingChangesOnlyAutomaticWoodAllocationUntilProcessingBuildingIsComplete()
        {
            var off = new Battle(TimingScenario(false));
            var on = new Battle(TimingScenario(true));
            SetAgeAndCiv(off, CivKind.Caravan, 1);
            SetAgeAndCiv(on, CivKind.Caravan, 1);
            SetGathererPattern(off);
            SetGathererPattern(on);

            Assert.That(InvokeWorkKind(off), Is.EqualTo(ResourceKind.Food));
            Assert.That(InvokeWorkKind(on), Is.EqualTo(ResourceKind.Wood));
            Assert.That(InvokeNeedsWood(on), Is.True);

            SetEconomyField(on, "AutoOff", true);
            Assert.That(InvokeNeedsWood(on), Is.False);
        }

        [Test]
        public void CraftTimingReplaysTwoReservedCivilisationsAtEveryTick()
        {
            var scenario = LiveGameRules.Create(7343UL, false, true, false, false);
            scenario.Economy.CraftTiming = true;
            var inputs = new[]
            {
                new ScheduledInput(1, 0, 1, EconomyCommand.Advance(1, 1, CivKind.Caravan)),
                new ScheduledInput(2, 0, 2, EconomyCommand.Advance(2, 1, CivKind.Metropolis))
            };
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, inputs, 12000, new BuildIdentity());
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }

        private static ScenarioDefinition TimingScenario(bool enabled)
        {
            var scenario = MapGenerator.GenerateTerrain(7344UL);
            scenario.Economy.Ages = true;
            scenario.Economy.Industry = true;
            scenario.Economy.ProcessingChain = true;
            scenario.Economy.CraftTiming = enabled;
            scenario.Economy.CraftTimingFoodPerWood = 1;
            return scenario;
        }

        private static void SetAgeAndCiv(Battle simulation, CivKind civ, int age)
        {
            SetEconomyField(simulation, "Age", age);
            SetEconomyField(simulation, "Civ", civ);
        }

        private static void SetGathererPattern(Battle simulation)
        {
            var world = World(simulation);
            var villagers = (Array)world.GetType().GetField("Villagers", PrivateInstance).GetValue(world);
            var nodes = (Array)world.GetType().GetField("Nodes", PrivateInstance).GetValue(world);
            int food = FirstNode(nodes, ResourceKind.Food);
            int wood = FirstNode(nodes, ResourceKind.Wood);
            for (int i = 0; i < villagers.Length && i < 4; i++)
            {
                var villager = villagers.GetValue(i);
                villager.GetType().GetField("FactionId", PrivateInstance).SetValue(villager, (uint)1);
                var taskField = villager.GetType().GetField("Task", PrivateInstance);
                taskField.SetValue(villager, Enum.ToObject(taskField.FieldType, 2));
                villager.GetType().GetField("NodeId", PrivateInstance).SetValue(villager, (uint)(i == 3 ? wood : food));
                villagers.SetValue(villager, i);
            }
            world.GetType().GetField("Villagers", PrivateInstance).SetValue(world, villagers);
        }

        private static int FirstNode(Array nodes, ResourceKind kind)
        {
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes.GetValue(i);
                var definition = node.GetType().GetField("Definition", PrivateInstance).GetValue(node);
                var kindField = definition.GetType().GetField("Kind", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if ((ResourceKind)kindField.GetValue(definition) == kind) return i + 1;
            }
            Assert.Fail("資源ノードがありません: " + kind);
            return 0;
        }

        private static ResourceKind InvokeWorkKind(Battle simulation)
        {
            var world = World(simulation);
            var villagers = (Array)world.GetType().GetField("Villagers", PrivateInstance).GetValue(world);
            var villager = villagers.GetValue(0);
            return (ResourceKind)typeof(Battle).GetMethod("WorkKindFor", PrivateInstance).Invoke(simulation, new[] { villager });
        }

        private static bool InvokeNeedsWood(Battle simulation)
            => (bool)typeof(Battle).GetMethod("CraftTimingNeedsWood", PrivateInstance).Invoke(simulation, new object[] { 1u });

        private static object World(Battle simulation) => typeof(Battle).GetField("world", PrivateInstance).GetValue(simulation);

        private static void SetEconomyField(Battle simulation, string name, object value)
        {
            var world = World(simulation);
            var field = world.GetType().GetField("Economies", PrivateInstance);
            var economies = (Array)field.GetValue(world);
            var economy = economies.GetValue(0);
            var economyField = economy.GetType().GetField(name, PrivateInstance);
            object converted = economyField.FieldType.IsEnum ? Enum.ToObject(economyField.FieldType, value)
                : Convert.ChangeType(value, economyField.FieldType);
            economyField.SetValue(economy, converted);
            economies.SetValue(economy, 0);
            field.SetValue(world, economies);
        }

        private static string Hash(Battle simulation)
            => Convert.ToHexString(ReplayBinary.Hash(simulation.CaptureDiagnostic().CanonicalState));
    }
}
