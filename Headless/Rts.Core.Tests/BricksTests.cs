using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Simulation;
using Rts.Replay;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class BricksTests
    {
        private static ScenarioDefinition Scenario(bool bricks)
        {
            var s = MapGenerator.GenerateTerrain(8811UL);
            s.Economy.Ages = true; s.Economy.Industry = true; s.Economy.ProcessingChain = true;
            s.Economy.Masonry = true; s.Economy.Bricks = bricks;
            s.Economy.StartFood = 50000; s.Economy.StartWood = 50000; s.Economy.StartStone = 50000;
            s.Economy.AdvanceFoodCost = 0; s.Economy.AdvanceWoodCost = 0; s.Economy.AdvanceTicks = 1;
            s.Economy.Age2FoodCost = 0; s.Economy.Age2WoodCost = 0; s.Economy.Age2Ticks = 1;
            s.Economy.BarracksWoodCost = 1; s.Economy.BarracksWork = 1; s.Economy.VillagerFoodCost = 0;
            s.Economy.QuarryWork = 1; s.Economy.QuarryIntervalTicks = 1; s.Economy.CharcoalKilnWork = 1;
            s.Economy.BrickKilnWork = 1; s.Economy.BrickTicks = 3; s.Economy.BrickStoneInput = 1; s.Economy.BrickCharcoalInput = 1;
            s.Economy.CharcoalTicks = 1;
            s.Economy.BeltTicksPerCell = 1; s.Economy.BeltLimit = 4096;
            s.Cores[0].Hp = 1000000; s.Cores[1].Hp = 1000000;
            return s;
        }

        private static void Step(CommandGateway g, Battle s, int count)
        {
            for (int i = 0; i < count && !s.Capture(1).Result.HasEnded; i++) g.Step();
        }

        [Test]
        public void BricksExtensionUsesIdTwentyOneAndRoundTrips()
        {
            var s = Scenario(true); s.Economy.BrickTicks = 117; s.Economy.BrickHpPermille = 1500;
            var bytes = ScenarioBinary.Encode(s); var decoded = ScenarioBinary.Decode(bytes);
            Assert.That(decoded.Extensions.Single(x => x.Id == 21).Version, Is.EqualTo(1));
            Assert.That(decoded.Economy.Bricks, Is.True);
            Assert.That(decoded.Economy.BrickTicks, Is.EqualTo(117));
            Assert.That(ScenarioBinary.Encode(decoded), Is.EqualTo(bytes));
        }

        [Test]
        public void BricksOffRejectsTheKilnAndLeavesNoBrickStockField()
        {
            var s = Scenario(false); var sim = new Battle(s); var g = new CommandGateway(sim);
            g.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Masonry));
            Step(g, sim, 3);
            g.SubmitEconomy(EconomyCommand.Place(1, 2, BuildingKind.BrickKiln, 0, Facing.North));
            Step(g, sim, 1);
            Assert.That(sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.BrickKiln), Is.False);
            var fields = DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);
            Assert.That(fields.Keys.Any(k => k.EndsWith(".Bricks", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void NonMasonryCannotRequestBricksLine()
        {
            var s = Scenario(true); var sim = new Battle(s); var g = new CommandGateway(sim);
            g.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Agrarian));
            g.SubmitEconomy(EconomyCommand.RequestLineAt(1, 2, ProcessingLineKind.Bricks, 0));
            Step(g, sim, 3);
            Assert.That(sim.Capture(1).Economy.Lines.Any(l => l.Id != 0), Is.False);
        }

        [Test]
        public void MasonryAutomaticLineCanProduceBricks()
        {
            var s = Scenario(true); var sim = new Battle(s); var g = new CommandGateway(sim);
            g.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Masonry));
            Step(g, sim, 12000);
            Assert.That(sim.Capture(1).Economy.Buildings.Any(b => b.Kind == BuildingKind.BrickKiln && b.Complete), Is.True);
            var fields = DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);
            Assert.That(int.Parse(fields["Economy[1].BricksProduced"]), Is.GreaterThan(0));
        }

        [Test]
        public void BricksScenarioReplayHasNoHashMismatch()
        {
            using (var stream = new MemoryStream())
            {
                var identity = new BuildIdentity();
                ReplayRunner.Record(stream, Scenario(true), Array.Empty<ScheduledInput>(), 1200, identity);
                stream.Position = 0;
                var result = ReplayRunner.Replay(stream, identity);
                Assert.That(result.FirstMismatchTick, Is.Null);
                Assert.That(result.IsFault, Is.False);
            }
        }
    }
}
