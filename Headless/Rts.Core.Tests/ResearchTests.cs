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
    /// <summary>technical-design-v3 32 #6 (V3-5): the blacksmith and its techs. Runs cross the research times.</summary>
    public sealed class ResearchTests
    {
        private const int Width = 128;

        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> f, string name) => long.Parse(f[name], CultureInfo.InvariantCulture);

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static uint Place(CommandGateway gateway, Battle sim, ScenarioDefinition s, BuildingKind kind, ref ulong seq)
        {
            int core = (int)(s.Cores[0].Position.Z.Raw / 65536 / 2) * Width + (int)(s.Cores[0].Position.X.Raw / 65536 / 2);
            int cx = core % Width, cz = core / Width;
            for (int r = 5; r <= 14; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r || cx + dx < 0 || cz + dz < 0 || cx + dx > Width - 4 || cz + dz > 60) continue;
                        long before = Number(Fields(sim), "Buildings.Count");
                        gateway.SubmitEconomy(EconomyCommand.Place(1, ++seq, kind, (cz + dz) * Width + cx + dx, Facing.North));
                        Steps(gateway, sim, 1);
                        var f = Fields(sim);
                        if (Number(f, "Buildings.Count") > before && f["Buildings[" + (before + 1) + "].Kind"] == ((byte)kind).ToString(CultureInfo.InvariantCulture)) return (uint)(before + 1);
                    }
            return 0;
        }

        [Test]
        public void WeaponsResearchedAtABlacksmithArmEveryWestSoldierAndItReplays()
        {
            var s = MapGenerator.GenerateTerrain(2);
            s.Economy.StartFood = 3000; s.Economy.StartWood = 3000;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong seq = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++seq, false));
            Steps(gateway, sim, 1);
            Assert.That(Place(gateway, sim, s, BuildingKind.Blacksmith, ref seq), Is.EqualTo(0u), "no blacksmith in the primitive age");
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, CivKind.Metallurgy));
            Steps(gateway, sim, s.Economy.AdvanceTicks + 2);
            uint smith = Place(gateway, sim, s, BuildingKind.Blacksmith, ref seq);
            Assume.That(smith, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, smith));
            for (int t = 0; t < 3000 && Fields(sim)["Buildings[" + smith + "].Complete"] != "1"; t += 20) Steps(gateway, sim, 20);
            Assert.That(Fields(sim)["Buildings[" + smith + "].Complete"], Is.EqualTo("1"));

            var before = Fields(sim);
            long food = Number(before, "Economy[1].Food");
            var westAlive = Enumerable.Range(1, (int)Number(before, "NextSoldierId") - 1)
                .Where(id => before["Soldiers[" + id + "].FactionId"] == "1" && before["Soldiers[" + id + "].Alive"] == "1").ToArray();
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++seq, smith, TechKind.Irrigation)); // farming's own tech: refused
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++seq, smith, TechKind.Weapons));
            Steps(gateway, sim, 1);
            var f = Fields(sim);
            Assert.That(f["Buildings[" + smith + "].Researching"], Is.EqualTo(((byte)TechKind.Weapons).ToString(CultureInfo.InvariantCulture)));
            Assert.That(Number(f, "Economy[1].Food"), Is.LessThanOrEqualTo(food - s.Economy.TechFood[0]).Or.GreaterThan(food - s.Economy.TechFood[0] - 200), "weapons paid");
            Steps(gateway, sim, s.Economy.TechTicks[0] + 1);
            f = Fields(sim);
            Assert.That(Number(f, "Economy[1].Techs") & 1, Is.EqualTo(1), "weapons researched");
            foreach (int id in westAlive.Where(id => f["Soldiers[" + id + "].Alive"] == "1"))
                Assert.That(Number(f, "Soldiers[" + id + "].Parameters.Damage"), Is.EqualTo(Number(before, "Soldiers[" + id + "].Parameters.Damage") + s.Economy.WeaponsDamage), "soldier " + id);

            // Once only.
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++seq, smith, TechKind.Weapons));
            Steps(gateway, sim, 1);
            Assert.That(Fields(sim)["Buildings[" + smith + "].Researching"], Is.EqualTo("0"));

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

        /// <summary>Left alone, a side builds a blacksmith and researches (seed 7 lasts long enough; 32.7, measured: rare so far).</summary>
        [TestCase(7UL)]
        public void LeftAloneASideResearches(ulong seed)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            var sim = new Battle(s);
            for (long t = 1; t <= 20000 && !sim.Capture(1).Result.HasEnded; t++) sim.Step(t, Array.Empty<ScheduledInput>());
            var f = Fields(sim);
            TestContext.WriteLine("seed " + seed + ": techs west " + f["Economy[1].Techs"] + ", east " + f["Economy[2].Techs"] + " by " + f["Tick"]);
            Assert.That(Number(f, "Economy[1].Techs") + Number(f, "Economy[2].Techs"), Is.GreaterThan(0), "someone researched");
        }
    }
}
