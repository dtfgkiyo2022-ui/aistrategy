using System;
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
    public sealed class ResourceFogTests
    {
        private static ScenarioDefinition Scenario(bool resourceFog)
        {
            var scenario = MapGenerator.GenerateTerrain(1UL, false, true);
            scenario.Economy.ResourceFog = resourceFog;
            scenario.Extensions = Array.Empty<ScenarioExtensionData>();
            return scenario;
        }

        private static int Cell(ScenarioDefinition scenario, SimPoint point)
            => (int)((point.Z.Raw / 65536 / scenario.Map.CellSizeMeters) * scenario.Map.WidthCells
                + point.X.Raw / 65536 / scenario.Map.CellSizeMeters);

        [Test]
        public void ExtensionRoundTripsAndOffOmitsResourceFog()
        {
            var off = Scenario(false);
            var offBytes = ScenarioBinary.Encode(off);
            var offDecoded = ScenarioBinary.Decode(offBytes);
            Assert.That(offDecoded.Economy.ResourceFog, Is.False);
            Assert.That(offDecoded.Extensions.Any(x => x.Id == 39), Is.False);

            var on = Scenario(true);
            var onDecoded = ScenarioBinary.Decode(ScenarioBinary.Encode(on));
            Assert.That(onDecoded.Economy.ResourceFog, Is.True);
            Assert.That(onDecoded.Extensions.Single(x => x.Id == 39).Version, Is.EqualTo(1));
            Assert.That(onDecoded.Extensions.Single(x => x.Id == 39).Data.Length, Is.EqualTo(sizeof(int)));
        }

        [Test]
        public void ResourcesAreOnlyShownAfterExploration()
        {
            var scenario = Scenario(true);
            var simulation = new Battle(scenario);
            var frame = simulation.Capture(1);
            Assert.That(frame.Economy.Resources.All(resource => frame.Fog.ExploredCells[Cell(scenario, resource.Position)]), Is.True);

            var hidden = scenario.ResourceNodes.First(node => !frame.Fog.ExploredCells[Cell(scenario, node.Position)]);
            Assert.That(frame.Economy.Resources.Any(resource => resource.Id == hidden.Id), Is.False);
        }

        [Test]
        public void LastObservedAmountSurvivesHiddenDepletionUntilSeenAgain()
        {
            var scenario = Scenario(true);
            var simulation = new Battle(scenario);
            var initial = simulation.Capture(1);
            int nodeIndex = Array.FindIndex(scenario.ResourceNodes, node => !initial.Fog.ExploredCells[Cell(scenario, node.Position)]);
            Assert.That(nodeIndex, Is.GreaterThanOrEqualTo(0));

            int cell = Cell(scenario, scenario.ResourceNodes[nodeIndex].Position);
            SetVisible(simulation, 1, cell, true);
            Invoke(simulation, "UpdateResourceObservations");
            Invoke(simulation, "PublishFrames");
            int observed = simulation.Capture(1).Economy.Resources.Single(resource => resource.Id == nodeIndex + 1).Remaining;

            SetRemaining(simulation, nodeIndex, observed - 7);
            SetVisible(simulation, 1, cell, false);
            Invoke(simulation, "UpdateResourceObservations");
            Invoke(simulation, "PublishFrames");
            Assert.That(simulation.Capture(1).Economy.Resources.Single(resource => resource.Id == nodeIndex + 1).Remaining, Is.EqualTo(observed));

            SetVisible(simulation, 1, cell, true);
            Invoke(simulation, "UpdateResourceObservations");
            Invoke(simulation, "PublishFrames");
            Assert.That(simulation.Capture(1).Economy.Resources.Single(resource => resource.Id == nodeIndex + 1).Remaining, Is.EqualTo(observed - 7));
        }

        private static void SetVisible(Battle simulation, int faction, int cell, bool value)
        {
            object world = Field(simulation, "world").GetValue(simulation);
            Array factions = (Array)Field(world, "Factions").GetValue(world);
            object state = factions.GetValue(faction - 1);
            var visible = (bool[])Field(state, "VisibleCells").GetValue(state);
            visible[cell] = value;
        }

        private static void SetRemaining(Battle simulation, int nodeIndex, int amount)
        {
            object world = Field(simulation, "world").GetValue(simulation);
            Array nodes = (Array)Field(world, "Nodes").GetValue(world);
            object state = nodes.GetValue(nodeIndex);
            Field(state, "Remaining").SetValue(state, amount);
            nodes.SetValue(state, nodeIndex);
        }

        private static void Invoke(Battle simulation, string name)
            => typeof(Battle).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(simulation, null);

        private static FieldInfo Field(object instance, string name)
            => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static FieldInfo Field(Type type, string name)
            => type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        [Test]
        public void FlagOffPlaysTheSameEveryTickAfterARoundTrip()
        {
            // The played rules with only the resource fog off: no ID 39 in the bytes, and the decoded scenario plays the
            // same as the original for the whole match (30000 ticks or the end).
            var scenario = LiveGameRules.Create(5, false, true, false, false);
            scenario.Economy.ResourceFog = false;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Any(x => x.Id == 39), Is.False);
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
            var left = new Battle(scenario);
            var right = new Battle(decoded);
            for (long tick = 1; tick <= 30000 && !left.Capture(1).Result.HasEnded; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }

        [Test]
        public void PlayedRulesWithTheResourceFogRecordAndReplayEveryTick()
        {
            var scenario = LiveGameRules.Create(5, false, true, false, false);
            Assert.That(scenario.Economy.ResourceFog, Is.True);
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 12000, new BuildIdentity());
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }
    }
}
