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
    public sealed class CraftTree1Tests
    {
        private static ScenarioDefinition Scenario(bool enabled)
        {
            var scenario = MapGenerator.GenerateTerrain(61041UL, false, true);
            scenario.Economy.Ages = true;
            scenario.Economy.Industry = true;
            scenario.Economy.ProcessingChain = true;
            scenario.Economy.CraftTree1 = enabled;
            scenario.Economy.CraftTreePlankWoodInput = 2;
            scenario.Economy.CraftTreePlankTicks = 60;
            scenario.Economy.CraftTreeCharcoalWoodInput = 2;
            scenario.Economy.CraftTreeCharcoalTicks = 60;
            scenario.Economy.StonecutterSizeCells = 2;
            scenario.Economy.StonecutterWoodCost = 80;
            scenario.Economy.CraftTreeStoneInput = 2;
            scenario.Economy.CraftTreeStonecutterTicks = 60;
            scenario.Economy.JoinerySizeCells = 2;
            scenario.Economy.JoineryWoodCost = 100;
            scenario.Economy.CraftTreeJoineryTicks = 80;
            scenario.Economy.CraftTreeJoineryPlankInput = 2;
            scenario.Economy.CraftTreeJoineryCutStoneInput = 1;
            scenario.Economy.CraftTreeFramesPerBuilding = 1;
            scenario.Economy.CraftTreeFrameWoodPermille = 700;
            scenario.Economy.CraftTreeFrameWorkPermille = 700;
            scenario.Economy.CraftTreeLevel2FramesCost = 2;
            return scenario;
        }

        [Test]
        public void Extension41RoundTripsAllCraftTreeRules()
        {
            var source = Scenario(true);
            source.Economy.CraftTreeJoineryTicks = 97;
            source.Economy.CraftTreeFrameWoodPermille = 733;
            var bytes = ScenarioBinary.Encode(source);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Any(x => x.Id == 41 && x.Version == 1), Is.True);
            Assert.That(decoded.Economy.CraftTree1, Is.True);
            Assert.That(decoded.Economy.CraftTreeJoineryTicks, Is.EqualTo(97));
            Assert.That(decoded.Economy.CraftTreeFrameWoodPermille, Is.EqualTo(733));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void OffStateDoesNotWriteCraftTreeExtension()
        {
            var decoded = ScenarioBinary.Decode(ScenarioBinary.Encode(Scenario(false)));
            Assert.That(decoded.Extensions.Any(x => x.Id == 41), Is.False);
            Assert.That(decoded.Economy.CraftTree1, Is.False);
        }

        [Test]
        public void LevelTwoUsesCeilingTwoThirdsForCraftTime()
        {
            var simulation = new Battle(Scenario(true));
            var method = typeof(Battle).GetMethod("CraftTicksFor", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var buildingType = typeof(Battle).Assembly.GetType("Rts.Simulation.BuildingState");
            var building = Activator.CreateInstance(buildingType);
            buildingType.GetField("Level", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(building, (byte)2);
            Assert.That(method.Invoke(simulation, new[] { building, (object)80 }), Is.EqualTo(54));
            Assert.That(method.Invoke(simulation, new[] { building, (object)60 }), Is.EqualTo(40));
        }

        [Test]
        public void ContractsReserveNewCraftTreeIds()
        {
            Assert.That((byte)ResourceKind.CutStone, Is.EqualTo(22));
            Assert.That((byte)ResourceKind.Frames, Is.EqualTo(23));
            Assert.That((byte)BuildingKind.Stonecutter, Is.EqualTo(44));
            Assert.That((byte)BuildingKind.Joinery, Is.EqualTo(45));
            Assert.That((byte)ProcessingLineKind.CutStone, Is.EqualTo(17));
            Assert.That((byte)ProcessingLineKind.Frames, Is.EqualTo(18));
        }

        [Test]
        public void FlagOffPlaysTheSameEveryTickAfterARoundTrip()
        {
            // The played rules with only the craft tree off: no ID 41 in the bytes, and the decoded scenario plays the
            // same as the original for the whole match (30000 ticks or the end).
            var scenario = LiveGameRules.Create(5, false, true, false, false);
            scenario.Economy.CraftTree1 = false;
            scenario.Economy.PrimitiveCraft = false; scenario.Economy.AiFactory = false;
            var bytes = ScenarioBinary.Encode(scenario);
            var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Any(x => x.Id == 41), Is.False);
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
        public void PlayedRulesWithTheCraftTreeRecordAndReplayEveryTick()
        {
            // Long enough for the second age, so the joinery, frames and level-2 upgrades are in the record.
            var scenario = LiveGameRules.Create(5, false, true, false, false);
            Assert.That(scenario.Economy.CraftTree1, Is.True);
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, scenario, Array.Empty<ScheduledInput>(), 16000, new BuildIdentity());
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, new BuildIdentity());
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }
    }
}
