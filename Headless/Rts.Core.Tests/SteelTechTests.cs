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
    /// technical-design-v3 32 #14 (V3-5): the steel weapons and armour. They are the only techs paid in metal, so the
    /// metal metallurgy piles up has somewhere to go, and they want the plain weapons or armour researched first.
    /// </summary>
    public sealed class SteelTechTests
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
        public void SteelNeedsTheSecondAgeThePlainTechAndTheMetalAndThenArmsEverySoldierAndItReplays()
        {
            var s = MapGenerator.GenerateTerrain(2);
            s.Economy.StartFood = 6000; s.Economy.StartWood = 6000; s.Economy.StartMetal = 0;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong seq = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++seq, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, CivKind.Metallurgy));
            Steps(gateway, sim, s.Economy.AdvanceTicks + 2);
            uint smith = Place(gateway, sim, s, BuildingKind.Blacksmith, ref seq);
            Assume.That(smith, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, smith));
            for (int t = 0; t < 4000 && Fields(sim)["Buildings[" + smith + "].Complete"] != "1"; t += 20) Steps(gateway, sim, 20);
            Assume.That(Fields(sim)["Buildings[" + smith + "].Complete"], Is.EqualTo("1"));

            // In the first age it is closed whatever the stock.
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++seq, smith, TechKind.SteelWeapons));
            Steps(gateway, sim, 1);
            Assert.That(Fields(sim)["Buildings[" + smith + "].Researching"], Is.EqualTo("0"), "steel waits for the second age");

            for (int i = 0; i < 5; i++) gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, ++seq, 0));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, CivKind.Metallurgy));
            Steps(gateway, sim, s.Economy.Age2Ticks + 2);
            Assume.That(sim.Capture(1).Economy.Age, Is.EqualTo(2));

            // In the second age it still wants the plain weapons first.
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++seq, smith, TechKind.SteelWeapons));
            Steps(gateway, sim, 1);
            Assert.That(Fields(sim)["Buildings[" + smith + "].Researching"], Is.EqualTo("0"), "steel follows the plain tech");
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++seq, smith, TechKind.Weapons));
            Steps(gateway, sim, s.Economy.TechTicks[(int)TechKind.Weapons - 1] + 2);
            Assume.That(Number(Fields(sim), "Economy[1].Techs") & 1L, Is.Not.EqualTo(0));

            // And it wants the metal: with none in store nothing starts.
            Assume.That(Number(Fields(sim), "Economy[1].Metal"), Is.LessThan(s.Economy.TechMetal[(int)TechKind.SteelWeapons - 1]));
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++seq, smith, TechKind.SteelWeapons));
            Steps(gateway, sim, 1);
            Assert.That(Fields(sim)["Buildings[" + smith + "].Researching"], Is.EqualTo("0"), "steel is paid in metal");

            // The metal is given at the start of a second run instead of waiting for a smelter: waiting on mining made
            // the test depend on how the match around it went, and it came out inconclusive in a full run (32.14).
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++seq, smith, TechKind.SteelArmour));
            Steps(gateway, sim, 1);
            Assert.That(Fields(sim)["Buildings[" + smith + "].Researching"], Is.EqualTo("0"), "the armour side is paid in metal too");
            RichInMetalItResearchesSteelWeaponsAndArmsEverySoldier();
            return;
        }

        /// <summary>The same blacksmith with metal in store: the research runs, the metal is paid, every soldier is armed.</summary>
        private static void RichInMetalItResearchesSteelWeaponsAndArmsEverySoldier()
        {
            var s = MapGenerator.GenerateTerrain(2);
            s.Economy.StartFood = 6000; s.Economy.StartWood = 6000; s.Economy.StartMetal = 400;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong seq = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++seq, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, CivKind.Metallurgy));
            Steps(gateway, sim, s.Economy.AdvanceTicks + 2);
            uint smith = Place(gateway, sim, s, BuildingKind.Blacksmith, ref seq);
            Assume.That(smith, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, smith));
            for (int t = 0; t < 4000 && Fields(sim)["Buildings[" + smith + "].Complete"] != "1"; t += 20) Steps(gateway, sim, 20);
            Assume.That(Fields(sim)["Buildings[" + smith + "].Complete"], Is.EqualTo("1"));
            for (int i = 0; i < 5; i++) gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, ++seq, 0));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, CivKind.Metallurgy));
            Steps(gateway, sim, s.Economy.Age2Ticks + 2);
            Assume.That(sim.Capture(1).Economy.Age, Is.EqualTo(2));
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++seq, smith, TechKind.Weapons));
            Steps(gateway, sim, s.Economy.TechTicks[(int)TechKind.Weapons - 1] + 2);
            Assume.That(Number(Fields(sim), "Economy[1].Techs") & 1L, Is.Not.EqualTo(0));
            long metal = Number(Fields(sim), "Economy[1].Metal");
            Assume.That(metal, Is.GreaterThanOrEqualTo(s.Economy.TechMetal[(int)TechKind.SteelWeapons - 1]));

            var before = Fields(sim);
            var alive = Enumerable.Range(1, (int)Number(before, "NextSoldierId") - 1)
                .Where(id => before["Soldiers[" + id + "].FactionId"] == "1" && before["Soldiers[" + id + "].Alive"] == "1").ToArray();
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++seq, smith, TechKind.SteelWeapons));
            Steps(gateway, sim, 1);
            var f = Fields(sim);
            Assert.That(f["Buildings[" + smith + "].Researching"], Is.EqualTo(((byte)TechKind.SteelWeapons).ToString(CultureInfo.InvariantCulture)));
            Assert.That(Number(f, "Economy[1].Metal"), Is.LessThanOrEqualTo(metal - s.Economy.TechMetal[(int)TechKind.SteelWeapons - 1]), "the metal was paid");
            Steps(gateway, sim, s.Economy.TechTicks[(int)TechKind.SteelWeapons - 1] + 1);
            f = Fields(sim);
            Assert.That(Number(f, "Economy[1].Techs") & (1L << ((int)TechKind.SteelWeapons - 1)), Is.Not.EqualTo(0), "steel weapons researched");
            foreach (int id in alive.Where(id => f["Soldiers[" + id + "].Alive"] == "1"))
                Assert.That(Number(f, "Soldiers[" + id + "].Parameters.Damage"),
                    Is.EqualTo(Number(before, "Soldiers[" + id + "].Parameters.Damage") + s.Economy.SteelWeaponsDamage), "soldier " + id);

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
    }
}
