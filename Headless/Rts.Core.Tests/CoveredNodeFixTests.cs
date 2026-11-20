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
    public sealed class CoveredNodeFixTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void Extension134RoundTripsAndFlagOffOmitsIt()
        {
            var scenario = LiveGameRules.Create(1341UL, true, true, false, false);
            Assert.That(scenario.Economy.CoveredNodeFix, Is.True);
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.CoveredNodeFix, Is.True);
            Assert.That(decoded.Extensions.Single(extension => extension.Id == 134).Version, Is.EqualTo(1));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            scenario.Economy.CoveredNodeFix = false;
            var off = ScenarioBinary.Decode(ScenarioBinary.Encode(scenario));
            Assert.That(off.Economy.CoveredNodeFix, Is.False);
            Assert.That(off.Extensions.Any(extension => extension.Id == 134), Is.False);
        }

        [Test]
        public void CoveredResourceIsNotSelectedWhenFlagIsOn()
        {
            var scenario = MapGenerator.Generate(1342UL, true, true);
            scenario.Economy.CoveredNodeFix = true;
            var simulation = new Battle(scenario);
            var world = GetWorld(simulation);
            var node = world.Nodes.First(candidate => candidate.Remaining > 0
                && (candidate.Definition.Kind == ResourceKind.Food || candidate.Definition.Kind == ResourceKind.Wood));
            int nodeCell = world.Map.Cell(node.Definition.Position);
            world.Buildings = new[] { new BuildingState { Id = 1, FactionId = 1, Kind = BuildingKind.House,
                OriginCell = nodeCell, WorkCell = nodeCell, Alive = true, Complete = true } };
            world.NextBuildingId = 2;
            world.Map.SetPassable(nodeCell, false);

            var nearest = typeof(Battle).GetMethod("NearestWorkNode", Hidden);
            int selected = (int)nearest.Invoke(simulation, new object[] { world.Cores[0].Definition.Position,
                node.Definition.Kind, 1U });
            Assert.That(selected, Is.Not.EqualTo(Array.IndexOf(world.Nodes, node)));
        }

        [Test]
        public void ExistingCoveredAssignmentIsReassignedOrMadeIdle()
        {
            var scenario = MapGenerator.Generate(1343UL, true, true);
            scenario.Economy.CoveredNodeFix = true;
            var simulation = new Battle(scenario);
            var world = GetWorld(simulation);
            int nodeIndex = Array.FindIndex(world.Nodes, candidate => candidate.Remaining > 0
                && (candidate.Definition.Kind == ResourceKind.Food || candidate.Definition.Kind == ResourceKind.Wood));
            Assert.That(nodeIndex, Is.GreaterThanOrEqualTo(0));
            var node = world.Nodes[nodeIndex];
            int nodeCell = world.Map.Cell(node.Definition.Position);
            world.Buildings = new[] { new BuildingState { Id = 1, FactionId = 1, Kind = BuildingKind.House,
                OriginCell = nodeCell, WorkCell = nodeCell, Alive = true, Complete = true } };
            world.NextBuildingId = 2;
            world.Map.SetPassable(nodeCell, false);
            ref var villager = ref world.Villagers[0];
            villager.NodeId = node.Definition.Id;
            villager.Task = VillagerTask.ToNode;
            villager.Held = false;
            villager.Position = world.Cores[0].Definition.Position;

            typeof(Battle).GetMethod("MoveVillagers", Hidden).Invoke(simulation, null);

            Assert.That(villager.NodeId == 0 || villager.NodeId != node.Definition.Id, Is.True);
            if (villager.NodeId == 0)
                Assert.That(villager.Task, Is.Not.EqualTo(VillagerTask.ToNode).And.Not.EqualTo(VillagerTask.Gathering));
        }

        [Test]
        public void FlagOffLeavesScenarioBytesAndHashesUnchanged()
        {
            var left = MapGenerator.Generate(1344UL, true, true);
            var right = MapGenerator.Generate(1344UL, true, true);
            left.Economy.CoveredNodeFix = false;
            right.Economy.CoveredNodeFix = false;
            Assert.That(ScenarioBinary.Encode(left), Is.EqualTo(ScenarioBinary.Encode(right)));
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

        private static WorldState GetWorld(Battle simulation)
            => (WorldState)typeof(Battle).GetField("world", Hidden).GetValue(simulation);
    }
}
