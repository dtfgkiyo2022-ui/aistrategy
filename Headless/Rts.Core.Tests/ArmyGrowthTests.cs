using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class ArmyGrowthTests
    {
        private static ScenarioDefinition Scenario(bool growth)
        {
            var scenario = WeekOneScenario.Create();
            scenario.Outposts = Array.Empty<OutpostDefinition>();
            scenario.Cores[0].Hp = scenario.Cores[1].Hp = 1000000;
            scenario.Economy.ArmyGrowth = growth;
            return scenario;
        }

        private static void Run(Battle simulation, long lastTick)
        {
            for (long tick = 1; tick <= lastTick && !simulation.Capture(1).Result.HasEnded; tick++)
                simulation.Step(tick, Array.Empty<ScheduledInput>());
        }

        [Test]
        public void FlagOffKeepsTheOriginalArmyAndSoldierCeiling()
        {
            var simulation = new Battle(Scenario(false));
            Run(simulation, 4000);
            var state = simulation.CaptureDiagnostic();
            var fields = DiagnosticComparison.Fields(state).ToDictionary(p => p.Key, p => p.Value);
            Assert.That(fields["Armies.Count"], Is.EqualTo("8"));
            Assert.That(fields["NextSoldierId"], Is.EqualTo("79"));
        }

        [Test]
        public void FullArmiesCreateAFieldArmyWithAnUnassignedNearestObjective()
        {
            var scenario = Scenario(true);
            scenario.Outposts = new[]
            {
                new OutpostDefinition { Id = 1, Position = new SimPoint(Fix64.FromInt(60), Fix64.FromInt(64)) },
                new OutpostDefinition { Id = 2, Position = new SimPoint(Fix64.FromInt(200), Fix64.FromInt(64)) }
            };
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            var west = PolicyPresets.CreateController("maintain", 1, gateway);
            var east = PolicyPresets.CreateController("maintain", 2, gateway);
            west.Initialize();
            east.Initialize();
            for (long tick = 1; tick <= 4000 && !simulation.Capture(1).Result.HasEnded; tick++)
                gateway.Step();
            var observation = simulation.Capture(1).Observation;
            var added = observation.OwnArmies.First(a => a.Id > 8);
            Assert.That(observation.OwnArmies.Count, Is.GreaterThan(4));
            Assert.That(added.AliveCount, Is.GreaterThan(0));
            Assert.That(added.HomeObjective.Kind, Is.EqualTo(GoalKind.Outpost));
            Assert.That(added.HomeObjective.Id, Is.EqualTo(1u));
            Assert.That(added.AliveCount, Is.LessThanOrEqualTo(12));
        }

        [Test]
        public void GrowthArmiesParticipateInAutoMovementAndContact()
        {
            var scenario = Scenario(true);
            scenario.Outposts = new[]
            {
                new OutpostDefinition { Id = 1, Position = new SimPoint(Fix64.FromInt(60), Fix64.FromInt(64)) },
                new OutpostDefinition { Id = 2, Position = new SimPoint(Fix64.FromInt(200), Fix64.FromInt(64)) }
            };
            var simulation = new Battle(scenario);
            var gateway = new CommandGateway(simulation);
            var west = PolicyPresets.CreateController("concentrate", 1, gateway);
            var east = PolicyPresets.CreateController("concentrate", 2, gateway);
            west.Initialize();
            east.Initialize();
            for (long tick = 1; tick <= 8000 && !simulation.Capture(1).Result.HasEnded; tick++)
                gateway.Step();

            var observation = simulation.Capture(1).Observation;
            var added = observation.OwnArmies.First(a => a.Id > 8);
            Assert.That(added.Position, Is.Not.EqualTo(scenario.Cores[0].Position));
            Assert.That(observation.VisibleEnemies.Count + observation.Contacts.Count, Is.GreaterThan(0));
        }

        [Test]
        public void ArmyGrowthExtensionRoundTripsAndIsAbsentWhenOff()
        {
            var on = Scenario(true);
            var bytes = ScenarioBinary.Encode(on);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Economy.ArmyGrowth, Is.True);
            Assert.That(decoded.Extensions.Single(e => e.Id == 10).Version, Is.EqualTo(1));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            var off = Scenario(false);
            off.Extensions = new[] { new ScenarioExtensionData { Id = 10, Version = 1, Data = BitConverter.GetBytes(1) } };
            var offBytes = ScenarioBinary.Encode(off);
            Assert.That(ScenarioBinary.Decode(offBytes).Extensions.Any(e => e.Id == 10), Is.False);
        }

        [Test]
        public void ArmyGrowthReplayMatchesEveryTick()
        {
            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                ReplayRunner.Record(stream, Scenario(true), Array.Empty<ScheduledInput>(), 20000, build);
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, build);
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
                Assert.That(result.LastTick, Is.EqualTo(20000));
            }
        }
    }
}
