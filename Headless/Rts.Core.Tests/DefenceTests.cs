using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <summary>technical-design-v3 32 #5 (V3-5): stone, walls and towers. Runs cross whole matches where towers are built.</summary>
    public sealed class DefenceTests
    {
        private const int Width = 128;

        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> f, string name) => long.Parse(f[name], CultureInfo.InvariantCulture);

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        [Test]
        public void TheTerrainMapHasStoneNearEachCore()
        {
            for (ulong seed = 1; seed <= 4; seed++)
            {
                var s = MapGenerator.GenerateTerrain(seed);
                foreach (var core in s.Cores)
                {
                    int near = s.ResourceNodes.Count(n => n.Kind == ResourceKind.Stone
                        && Math.Pow((n.Position.X.Raw - core.Position.X.Raw) / 65536.0, 2) + Math.Pow((n.Position.Z.Raw - core.Position.Z.Raw) / 65536.0, 2) <= 30 * 30);
                    Assert.That(near, Is.GreaterThanOrEqualTo(2), "seed " + seed);
                }
                new Battle(s);
            }
        }

        [Test]
        public void AWallRunStandsAtOnceCostsStoneAndBlocksTheWay()
        {
            var s = MapGenerator.GenerateTerrain(1);
            s.Economy.StartStone = 100;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            int core = (int)(s.Cores[0].Position.Z.Raw / 65536 / 2) * Width + (int)(s.Cores[0].Position.X.Raw / 65536 / 2);
            // A short run of 5 cells, 5 cells east of the core, north to south.
            var run = Enumerable.Range(-2, 5).Select(dz => core + 5 + dz * Width).ToArray();
            var blocked = new HashSet<int>(s.Map.BlockedCellIds);
            Assume.That(run.All(c => !blocked.Contains(c)), "open ground east of the core");
            gateway.SubmitEconomy(EconomyCommand.PlaceWall(1, 1, run));
            Steps(gateway, sim, 1);
            var f = Fields(sim);
            int walls = Enumerable.Range(1, (int)Number(f, "Buildings.Count")).Count(b => f["Buildings[" + b + "].Kind"] == "7");
            Assert.That(walls, Is.GreaterThan(0));
            Assert.That(Number(f, "Economy[1].Stone"), Is.EqualTo(100 - walls * s.Economy.WallStoneCost));
            for (int b = 1; b <= Number(f, "Buildings.Count"); b++)
                if (f["Buildings[" + b + "].Kind"] == "7") Assert.That(f["Buildings[" + b + "].Complete"], Is.EqualTo("1"), "a wall stands at once");

            // A wall far from the own base is refused.
            int far = (int)(s.Cores[1].Position.Z.Raw / 65536 / 2) * Width + (int)(s.Cores[1].Position.X.Raw / 65536 / 2) - 8;
            gateway.SubmitEconomy(EconomyCommand.PlaceWall(1, 2, new[] { far }));
            Steps(gateway, sim, 1);
            Assert.That(Enumerable.Range(1, (int)Number(Fields(sim), "Buildings.Count")).Count(b => Fields(sim)["Buildings[" + b + "].Kind"] == "7"), Is.EqualTo(walls));

            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                ReplayRunner.Record(stream, s, gateway.Inputs, sim.Capture(1).Tick, build);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, build);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        /// <summary>
        /// Left alone, a side gathers stone once in a civilisation, puts up towers, and they shoot (seed 7 by 40000,
        /// measured; it was seed 6 until the second age moved the course of that match - 32.9).
        /// </summary>
        [Test]
        public void TheAutomaticEconomyGathersStoneAndItsTowersShoot()
        {
            var s = MapGenerator.GenerateTerrain(7);
            var sim = new Battle(s);
            for (long t = 1; t <= 40000 && !sim.Capture(1).Result.HasEnded; t++) sim.Step(t, Array.Empty<ScheduledInput>());
            var f = Fields(sim);
            long shots = 0; int towers = 0;
            for (int b = 1; b <= Number(f, "Buildings.Count"); b++)
                if (f["Buildings[" + b + "].Kind"] == "8") { towers++; shots += Number(f, "Buildings[" + b + "].Shots"); }
            TestContext.WriteLine("seed 7: towers " + towers + ", shots " + shots + ", stone W" + f["Economy[1].Stone"] + " E" + f["Economy[2].Stone"]);
            Assert.That(towers, Is.GreaterThan(0));
            Assert.That(shots, Is.GreaterThan(0));
        }

        [Test]
        public void AMapWithoutAgesHasNoStoneWallsOrTowers()
        {
            var s = MapGenerator.Generate(1, true, true);
            Assert.That(s.ResourceNodes.Any(n => n.Kind == ResourceKind.Stone), Is.False);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            int core = (int)(s.Cores[0].Position.Z.Raw / 65536 / 2) * Width + (int)(s.Cores[0].Position.X.Raw / 65536 / 2);
            gateway.SubmitEconomy(EconomyCommand.PlaceWall(1, 1, new[] { core + 5 }));
            Steps(gateway, sim, 1);
            var f = Fields(sim);
            Assert.That(f.Keys.Any(k => k.EndsWith("].Stone", StringComparison.Ordinal)), Is.False);
            Assert.That(Number(f, "Buildings.Count"), Is.EqualTo(0));
        }
    }
}
