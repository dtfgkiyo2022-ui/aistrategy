using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class VillagerRebalanceTests
    {
        private sealed class Fixture
        {
            public ScenarioDefinition Scenario;
            public Battle Simulation;
            public CommandGateway Gateway;
            public uint WoodNode, FoodNode, OtherFoodNode;
        }

        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static int Number(Dictionary<string, string> fields, string name)
            => int.Parse(fields[name], CultureInfo.InvariantCulture);

        private static Fixture Create(bool ages, bool twoFoodSupporters, bool holdFirst)
        {
            var s = ages ? MapGenerator.GenerateTerrain(1) : MapGenerator.Generate(1, true, false);
            s.Economy.StartFood = 449;
            s.Economy.StartWood = 149;
            s.Economy.AutoVillagerTarget = 3;
            // One gather is enough to exercise the unload transition, while the node still has stock for the return.
            s.Economy.CarryCapacity = 1;

            var core = s.Cores[0].Position;
            var wood = s.ResourceNodes.First(n => n.Kind == ResourceKind.Wood);
            var foods = s.ResourceNodes.Where(n => n.Kind == ResourceKind.Food)
                .OrderByDescending(n => DistanceSquared(n.Position, core)).ToArray();
            var otherWood = s.ResourceNodes.Where(n => n.Kind == ResourceKind.Wood && n.Id != wood.Id)
                .OrderByDescending(n => DistanceSquared(n.Position, core)).First();
            s.ResourceNodes[wood.Id - 1].Amount = 2;

            // The first villager starts at wood. The two supporting villagers have a long walk, so they only count
            // toward the allocation decision during the first villager's short gather/drop cycle.
            s.Villagers[0].Position = wood.Position;
            s.Villagers[1].Position = core;
            s.Villagers[2].Position = core;

            var fixture = new Fixture
            {
                Scenario = s,
                Simulation = new Battle(s),
                Gateway = null,
                WoodNode = wood.Id,
                FoodNode = foods[0].Id,
                OtherFoodNode = foods[1].Id
            };
            fixture.Gateway = new CommandGateway(fixture.Simulation);
            ulong sequence = 0;
            fixture.Gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new uint[] { 2 }, EconomyTargetKind.ResourceNode, fixture.FoodNode));
            fixture.Gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new uint[] { 3 }, EconomyTargetKind.ResourceNode,
                twoFoodSupporters ? fixture.OtherFoodNode : otherWood.Id));
            if (holdFirst)
                fixture.Gateway.SubmitEconomy(EconomyCommand.Assign(1, ++sequence, new uint[] { 1 }, EconomyTargetKind.ResourceNode, fixture.WoodNode));
            return fixture;
        }

        private static long DistanceSquared(SimPoint a, SimPoint b)
        {
            long dx = a.X.Raw - b.X.Raw, dz = a.Z.Raw - b.Z.Raw;
            return checked(dx * dx + dz * dz);
        }

        private static Dictionary<string, string> StepUntil(Fixture fixture, Func<Dictionary<string, string>, bool> condition, int limit = 400)
        {
            for (int i = 0; i < limit; i++)
            {
                var fields = Fields(fixture.Simulation);
                if (condition(fields)) return fields;
                fixture.Gateway.Step();
            }
            Assert.Fail("condition was not reached within " + limit + " ticks");
            return null;
        }

        [Test]
        public void AnAgesVillagerSwitchesFromWoodToFoodAfterUnloading()
        {
            var fixture = Create(true, false, false);
            var before = StepUntil(fixture, f => Number(f, "Villagers[1].Task") == 3);
            int original = Number(before, "Villagers[1].NodeId");
            Assert.That(fixture.Scenario.ResourceNodes[original - 1].Kind, Is.EqualTo(ResourceKind.Wood));

            var after = StepUntil(fixture, f => Number(f, "Villagers[1].Task") == 1
                && Number(f, "Villagers[1].NodeId") != original);
            int next = Number(after, "Villagers[1].NodeId");
            Assert.That(fixture.Scenario.ResourceNodes[next - 1].Kind, Is.EqualTo(ResourceKind.Food));
        }

        [Test]
        public void AnAgesVillagerReturnsToTheSameNodeWhenItsKindStillWins()
        {
            var fixture = Create(true, true, false);
            var after = StepUntil(fixture, f => Number(f, "Villagers[1].Task") == 1
                && Number(f, "Economy[1].Wood") > fixture.Scenario.Economy.StartWood);
            Assert.That(Number(after, "Villagers[1].NodeId"), Is.EqualTo(fixture.WoodNode));
        }

        [Test]
        public void AMapWithoutAgesKeepsTheOldUnloadDestination()
        {
            var fixture = Create(false, true, false);
            var after = StepUntil(fixture, f => Number(f, "Villagers[1].Task") == 1
                && Number(f, "Economy[1].Wood") > fixture.Scenario.Economy.StartWood);
            Assert.That(Number(after, "Villagers[1].NodeId"), Is.EqualTo(fixture.WoodNode));
        }

        [Test]
        public void HeldAndAutoOffVillagersDoNotRebalanceAfterUnloading()
        {
            var held = Create(true, false, true);
            var heldAfter = StepUntil(held, f => Number(f, "Villagers[1].Task") == 1
                && Number(f, "Economy[1].Wood") > held.Scenario.Economy.StartWood);
            Assert.That(Number(heldAfter, "Villagers[1].NodeId"), Is.EqualTo(held.WoodNode));

            var autoOff = Create(true, false, false);
            var beforeDrop = StepUntil(autoOff, f => Number(f, "Villagers[1].Task") == 3);
            int original = Number(beforeDrop, "Villagers[1].NodeId");
            autoOff.Gateway.SubmitEconomy(EconomyCommand.Auto(1, 3, false));
            var afterDrop = StepUntil(autoOff, f => Number(f, "Villagers[1].Task") == 1
                && Number(f, "Economy[1].AutoOff") == 1);
            Assert.That(Number(afterDrop, "Villagers[1].NodeId"), Is.EqualTo(original));
        }
    }
}
