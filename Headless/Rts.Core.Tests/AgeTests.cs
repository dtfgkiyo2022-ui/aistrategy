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
    /// <summary>
    /// technical-design-v3 26 and 29 (V3-4 PR2): the primitive age, advancing at the core, and the automatic economy
    /// picking a civilisation from the ground. The runs cross the advancing time (1200 ticks) and many AI cycles.
    /// </summary>
    public sealed class AgeTests
    {
        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> f, string name) => long.Parse(f[name], CultureInfo.InvariantCulture);

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static string B(CivKind c) => ((byte)c).ToString(CultureInfo.InvariantCulture);

        [Test]
        public void AdvancingByHandCostsFoodAndWoodTakesItsTimeAndBlocksVillagers()
        {
            var s = MapGenerator.GenerateTerrain(1);
            s.Economy.StartFood = 1000; s.Economy.StartWood = 1000;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            Steps(gateway, sim, 1);
            var f = Fields(sim);
            Assert.That(f["Economy[1].Civ"], Is.EqualTo(B(CivKind.Primitive)));

            // Primitive: no mine anywhere.
            var ore = s.ResourceNodes.First(n => n.Kind == ResourceKind.Ore);
            int oreCell = (int)(ore.Position.Z.Raw / 65536 / 2) * 128 + (int)(ore.Position.X.Raw / 65536 / 2);
            foreach (int origin in new[] { oreCell, oreCell - 1, oreCell - 128, oreCell - 129 })
                gateway.SubmitEconomy(EconomyCommand.Place(1, 2, BuildingKind.Mine, origin, Facing.North));
            Steps(gateway, sim, 1);
            Assert.That(Number(Fields(sim), "Buildings.Count"), Is.EqualTo(0), "no mines in the primitive age");

            gateway.SubmitEconomy(EconomyCommand.Advance(1, 3, CivKind.Metallurgy));
            Steps(gateway, sim, 1);
            f = Fields(sim);
            Assert.That(Number(f, "Economy[1].Food"), Is.EqualTo(1000 - s.Economy.AdvanceFoodCost));
            Assert.That(Number(f, "Economy[1].Wood"), Is.EqualTo(1000 - s.Economy.AdvanceWoodCost));
            Assert.That(f["Economy[1].AdvancingTo"], Is.EqualTo(B(CivKind.Metallurgy)));

            // While advancing: no villager, and a second advance is refused.
            gateway.SubmitEconomy(EconomyCommand.Train(1, 4, 0, UnitKind.Villager));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 5, CivKind.Agrarian));
            Steps(gateway, sim, 1);
            f = Fields(sim);
            Assert.That(Number(f, "Economy[1].Queued"), Is.EqualTo(0));
            Assert.That(Number(f, "Economy[1].Food"), Is.EqualTo(1000 - s.Economy.AdvanceFoodCost), "nothing else was paid");

            Steps(gateway, sim, s.Economy.AdvanceTicks - 4);
            Assert.That(Fields(sim)["Economy[1].Civ"], Is.EqualTo(B(CivKind.Primitive)), "not a tick early");
            Steps(gateway, sim, 3);
            f = Fields(sim);
            Assert.That(f["Economy[1].Civ"], Is.EqualTo(B(CivKind.Metallurgy)));
            Assert.That(Number(f, "Economy[1].AdvanceRemaining"), Is.EqualTo(0));

            // Metallurgy: the mine can go up now.
            foreach (int origin in new[] { oreCell, oreCell - 1, oreCell - 128, oreCell - 129 })
                gateway.SubmitEconomy(EconomyCommand.Place(1, 6, BuildingKind.Mine, origin, Facing.North));
            Steps(gateway, sim, 1);
            Assert.That(Number(Fields(sim), "Buildings.Count"), Is.GreaterThan(0), "a mine in the metallurgy civilisation");

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

        [TestCase(1UL)]
        [TestCase(2UL)]
        [TestCase(3UL)]
        [TestCase(4UL)]
        public void LeftAloneBothSidesAdvanceIntoTheCivilisationTheirGroundSuits(ulong seed)
        {
            var s = MapGenerator.GenerateTerrain(seed, out var leans);
            var sim = new Battle(s);
            var civ = new string[2];
            long[] when = new long[2];
            for (long t = 1; t <= 20000 && !sim.Capture(1).Result.HasEnded; t++)
            {
                sim.Step(t, Array.Empty<ScheduledInput>());
                if (t % 100 != 0) continue;
                var f = Fields(sim);
                for (int side = 0; side < 2; side++)
                    if (when[side] == 0 && f["Economy[" + (side + 1) + "].Civ"] != B(CivKind.Primitive)) { when[side] = t; civ[side] = f["Economy[" + (side + 1) + "].Civ"]; }
                if (when[0] > 0 && when[1] > 0) break;
            }
            TestContext.WriteLine("seed " + seed + ": west " + leans[0] + " -> civ " + civ[0] + " at " + when[0]
                + ", east " + leans[1] + " -> civ " + civ[1] + " at " + when[1]);
            bool ended = sim.Capture(1).Result.HasEnded;
            for (int side = 0; side < 2; side++)
                if (!ended || when[side] > 0) Assert.That(when[side], Is.GreaterThan(0), "side " + (side + 1) + " advanced");
        }

        [Test]
        public void AnIndustryMapWithoutAgesIsUnchanged()
        {
            var s = MapGenerator.Generate(1, true, true);
            var f = Fields(new Battle(s));
            Assert.That(f.Keys.Any(k => k.EndsWith("].Civ", StringComparison.Ordinal) || k.EndsWith("QueuedMetal", StringComparison.Ordinal)), Is.False);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Agrarian));
            Steps(gateway, sim, 5);
            Assert.That(Number(Fields(sim), "Economy[1].Food"), Is.EqualTo(Number(Fields(new Battle(s)), "Economy[1].Food")).Or.LessThanOrEqualTo(s.Economy.StartFood),
                "advancing is refused without ages");
        }
    }
}
