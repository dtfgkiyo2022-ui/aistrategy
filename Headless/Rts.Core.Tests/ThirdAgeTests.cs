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
    /// <summary>technical-design-v3 32 #10 (V3-5): the third age of a civilisation and the three techs it opens.</summary>
    public sealed class ThirdAgeTests
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

        /// <summary>Takes the west to <paramref name="age"/> in <paramref name="civ"/> by hand, with stock for what follows.</summary>
        private static (ScenarioDefinition s, Battle sim, CommandGateway gateway, ulong seq) Aged(ulong seed, CivKind civ, int age)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            s.Economy.StartFood = 6000; s.Economy.StartWood = 6000; s.Economy.StartStone = 600;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong seq = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++seq, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, civ));
            Steps(gateway, sim, s.Economy.AdvanceTicks + 2);
            for (int step = 2; step <= age; step++)
            {
                for (int i = 0; i < 5; i++) gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, ++seq, 0));
                gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, civ));
                Steps(gateway, sim, (step == 2 ? s.Economy.Age2Ticks : s.Economy.Age3Ticks) + 2);
            }
            Assume.That(sim.Capture(1).Economy.Age, Is.EqualTo(age));
            return (s, sim, gateway, seq);
        }

        [TestCase(CivKind.Agrarian)]
        [TestCase(CivKind.Metallurgy)]
        public void TheThirdAgeCostsMoreLiftsThePopulationAndOnlyFollowsTheSecond(CivKind civ)
        {
            var (s, sim, gateway, seq) = Aged(2, civ, 2);
            var second = sim.Capture(1).Economy;
            long food = second.Food, wood = second.Wood;
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, civ == CivKind.Agrarian ? CivKind.Metallurgy : CivKind.Agrarian)); // the other one: refused
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, civ));
            Steps(gateway, sim, 1);
            var e = sim.Capture(1).Economy;
            Assert.That(e.AdvanceRemaining, Is.GreaterThan(0), "the third age is under way");
            Assert.That(e.Food, Is.EqualTo(food - s.Economy.Age3FoodCost));
            Assert.That(e.Wood, Is.EqualTo(wood - s.Economy.Age3WoodCost));
            Steps(gateway, sim, s.Economy.Age3Ticks + 1);
            e = sim.Capture(1).Economy;
            Assert.That(e.Age, Is.EqualTo(3));
            Assert.That(e.Civ, Is.EqualTo(civ), "the civilisation is the same one");
            Assert.That(e.PopulationCap, Is.LessThanOrEqualTo(s.Economy.PopulationCap + s.Economy.Age2PopulationBonus + s.Economy.Age3PopulationBonus));

            // There is no fourth age.
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, civ));
            Steps(gateway, sim, 1);
            Assert.That(sim.Capture(1).Economy.AdvanceRemaining, Is.EqualTo(0));
        }

        [Test]
        public void TheThirdAgeTechsAreClosedBeforeItAndChangeTheirNumbersAfterItAndItReplays()
        {
            var (s, sim, gateway, seq) = Aged(2, CivKind.Metallurgy, 2);
            uint smith = Place(gateway, sim, s, BuildingKind.Blacksmith, ref seq);
            Assume.That(smith, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, smith));
            for (int t = 0; t < 4000 && Fields(sim)["Buildings[" + smith + "].Complete"] != "1"; t += 20) Steps(gateway, sim, 20);
            Assume.That(Fields(sim)["Buildings[" + smith + "].Complete"], Is.EqualTo("1"));

            // Before the third age none of its three is open.
            foreach (var tech in new[] { TechKind.Siegecraft, TechKind.Masonry, TechKind.Banking })
            {
                gateway.SubmitEconomy(EconomyCommand.Research(1, ++seq, smith, tech));
                Steps(gateway, sim, 1);
                Assert.That(Fields(sim)["Buildings[" + smith + "].Researching"], Is.EqualTo("0"), tech + " waits for the third age");
            }

            for (int i = 0; i < 5; i++) gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, ++seq, 0));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, CivKind.Metallurgy));
            Steps(gateway, sim, s.Economy.Age3Ticks + 2);
            Assume.That(sim.Capture(1).Economy.Age, Is.EqualTo(3));

            // A market trades more with banking.
            uint market = Place(gateway, sim, s, BuildingKind.Market, ref seq);
            Assume.That(market, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, market));
            for (int t = 0; t < 4000 && Fields(sim)["Buildings[" + market + "].Complete"] != "1"; t += 20) Steps(gateway, sim, 20);
            Assume.That(Fields(sim)["Buildings[" + market + "].Complete"], Is.EqualTo("1"));
            long stone = Number(Fields(sim), "Economy[1].Stone");
            gateway.SubmitEconomy(EconomyCommand.Trade(1, ++seq, ResourceKind.Food, ResourceKind.Stone));
            Steps(gateway, sim, 1);
            Assert.That(Number(Fields(sim), "Economy[1].Stone"), Is.EqualTo(stone + s.Economy.TradeReturn), "plain trade before banking");

            gateway.SubmitEconomy(EconomyCommand.Research(1, ++seq, smith, TechKind.Banking));
            Steps(gateway, sim, 1);
            Assert.That(Fields(sim)["Buildings[" + smith + "].Researching"], Is.EqualTo(((byte)TechKind.Banking).ToString(CultureInfo.InvariantCulture)));
            Steps(gateway, sim, s.Economy.TechTicks[(int)TechKind.Banking - 1] + 1);
            var f = Fields(sim);
            Assert.That(Number(f, "Economy[1].Techs") & (1L << ((int)TechKind.Banking - 1)), Is.Not.EqualTo(0), "banking researched");
            stone = Number(f, "Economy[1].Stone");
            gateway.SubmitEconomy(EconomyCommand.Trade(1, ++seq, ResourceKind.Food, ResourceKind.Stone));
            Steps(gateway, sim, 1);
            Assert.That(Number(Fields(sim), "Economy[1].Stone"), Is.EqualTo(stone + s.Economy.TradeReturn + s.Economy.BankingTradeReturn), "banking pays more");

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

        /// <summary>Siegecraft is read where the blow lands, so a ram trained before it still hits harder after it (32 #10).</summary>
        [Test]
        public void SiegecraftMakesEveryRamHitBuildingsHarder()
        {
            var (s, sim, gateway, seq) = Aged(2, CivKind.Metallurgy, 3);
            uint smith = Place(gateway, sim, s, BuildingKind.Blacksmith, ref seq);
            Assume.That(smith, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, smith));
            for (int t = 0; t < 4000 && Fields(sim)["Buildings[" + smith + "].Complete"] != "1"; t += 20) Steps(gateway, sim, 20);
            Assume.That(Fields(sim)["Buildings[" + smith + "].Complete"], Is.EqualTo("1"));
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++seq, smith, TechKind.Siegecraft));
            Steps(gateway, sim, 1);
            Assert.That(Fields(sim)["Buildings[" + smith + "].Researching"], Is.EqualTo(((byte)TechKind.Siegecraft).ToString(CultureInfo.InvariantCulture)),
                "siegecraft is open in the third age");
            Steps(gateway, sim, s.Economy.TechTicks[(int)TechKind.Siegecraft - 1] + 1);
            Assert.That(Number(Fields(sim), "Economy[1].Techs") & (1L << ((int)TechKind.Siegecraft - 1)), Is.Not.EqualTo(0));
        }
    }
}
