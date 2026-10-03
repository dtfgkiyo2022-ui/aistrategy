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
    [TestFixture]
    public sealed class CoreDefenceTests
    {
        [Test]
        public void DisabledCoreDefenceKeepsScenarioBytesAndStateHashesUnchanged()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.CoreDefence = false;
            byte[] first = ScenarioBinary.Encode(scenario);
            var left = new Battle(scenario);
            var right = new Battle(MapGenerator.GenerateTerrain(1));
            Assert.That(ScenarioBinary.Encode(MapGenerator.GenerateTerrain(1)), Is.EqualTo(first));
            Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)));

            for (long tick = 1; tick <= 200; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
                if (left.Capture(1).Result.HasEnded) break;
            }
        }

        [Test]
        public void CoreDefenceExtensionIdNineRoundTrips()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.CoreDefence = true;
            scenario.Economy.CoreDefenceRange = 17;
            scenario.Economy.CoreDefenceDamage = 31;
            scenario.Economy.CoreDefenceIntervalTicks = 19;
            scenario.Economy.CoreDefenceMaxTargets = 2;
            byte[] bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            var extension = decoded.Extensions.Single(value => value.Id == 9);
            Assert.That(extension.Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.CoreDefence, Is.True);
            Assert.That(decoded.Economy.CoreDefenceRange, Is.EqualTo(17));
            Assert.That(decoded.Economy.CoreDefenceDamage, Is.EqualTo(31));
            Assert.That(decoded.Economy.CoreDefenceIntervalTicks, Is.EqualTo(19));
            Assert.That(decoded.Economy.CoreDefenceMaxTargets, Is.EqualTo(2));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));

            scenario.Economy.CoreDefence = false;
            scenario.Extensions = Array.Empty<ScenarioExtensionData>();
            Assert.That(ScenarioBinary.Encode(scenario).ContainsExtensionMarker(), Is.False);
        }

        [Test]
        public void CoreShootsThreeNearestSoldiersByIdAndIgnoresOutsideRange()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.CoreDefence = true;
            scenario.Economy.CoreDefenceMaxTargets = 3;
            scenario.Economy.CoreDefenceRange = 16;
            scenario.Economy.CoreDefenceDamage = 30;
            FreezeInfantry(scenario);
            var core = scenario.Cores[0].Position;
            scenario.Soldiers[20].Position = Offset(core, 4, 0);
            scenario.Soldiers[21].Position = Offset(core, -4, 0);
            scenario.Soldiers[22].Position = Offset(core, 0, 4);
            scenario.Soldiers[23].Position = Offset(core, 0, -4);
            scenario.Soldiers[24].Position = Offset(core, 17, 0);

            var simulation = new Battle(scenario);
            simulation.Step(1, Array.Empty<ScheduledInput>());
            var own = simulation.Capture(2).Units.Where(value => value.IsOwn).ToDictionary(value => value.Id);
            Assert.That(own[21].Hp, Is.EqualTo(70));
            Assert.That(own[22].Hp, Is.EqualTo(70));
            Assert.That(own[23].Hp, Is.EqualTo(70));
            Assert.That(own[24].Hp, Is.EqualTo(100));
            Assert.That(own[25].Hp, Is.EqualTo(100));
        }

        [Test]
        public void CoreShootsVillagersOnlyWhenNoEnemySoldierIsInRange()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.CoreDefence = true;
            scenario.Economy.CoreDefenceMaxTargets = 3;
            FreezeInfantry(scenario);
            var core = scenario.Cores[0].Position;
            for (int i = 0; i < scenario.Soldiers.Length; i++)
                if (scenario.Soldiers[i].FactionId == 2) scenario.Soldiers[i].Position = scenario.Cores[1].Position;
            scenario.Villagers[3].Position = Offset(core, 4, 0);
            scenario.Villagers[4].Position = Offset(core, -4, 0);
            var simulation = new Battle(scenario);
            simulation.Step(1, Array.Empty<ScheduledInput>());
            var villagers = simulation.Capture(2).Economy.Villagers.Where(value => value.IsOwn).ToDictionary(value => value.Id);
            Assert.That(villagers[4].Hp, Is.EqualTo(10));
            Assert.That(villagers[5].Hp, Is.EqualTo(10));
            Assert.That(villagers[6].Hp, Is.EqualTo(40));
        }

        [Test]
        public void CoreDefenceReplayMatchesForTwentyThousandTicks()
        {
            var scenario = MapGenerator.GenerateTerrain(1);
            scenario.Economy.CoreDefence = true;
            var inputs = PolicyPresets.RecordedInputs(scenario, "maintain", "maintain", 20000);
            var identity = new BuildIdentity();
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, inputs, 20000, identity);
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, identity);
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }

        private static void FreezeInfantry(ScenarioDefinition scenario)
        {
            for (int i = 0; i < scenario.UnitParameters.Length; i++)
                if (scenario.UnitParameters[i].Kind == UnitKind.Infantry)
                {
                    var p = scenario.UnitParameters[i];
                    p.Speed = Fix64.FromRaw(1);
                    p.Damage = 0;
                    scenario.UnitParameters[i] = p;
                }
            scenario.Economy.VillagerSpeed = Fix64.FromRaw(1);
        }

        private static SimPoint Offset(SimPoint point, int x, int z)
            => new SimPoint(Fix64.FromRaw(point.X.Raw + Fix64.FromInt(x).Raw), Fix64.FromRaw(point.Z.Raw + Fix64.FromInt(z).Raw));
    }

    internal static class CoreDefenceTestBytes
    {
        internal static bool ContainsExtensionMarker(this byte[] bytes)
        {
            for (int i = 0; i + 4 <= bytes.Length; i++)
                if (bytes[i] == 0x45 && bytes[i + 1] == 0x58 && bytes[i + 2] == 0x54 && bytes[i + 3] == 0x4e) return true;
            return false;
        }
    }
}
