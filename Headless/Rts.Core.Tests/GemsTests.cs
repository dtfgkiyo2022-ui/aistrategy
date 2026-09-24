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
    /// technical-design-v3 32 #19 (V3-5): Gems, obtained only by trading at a market, and the Gem armour tech they pay
    /// for. No resource node ever gives Gems, so the automatic villager gathering AI is never touched by this feature.
    /// </summary>
    public sealed class GemsTests
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

        /// <summary>Takes the west to the third age of <paramref name="civ"/>, with stock for what follows.</summary>
        private static (ScenarioDefinition s, Battle sim, CommandGateway gateway, ulong seq) ThirdAge(CivKind civ)
        {
            var s = MapGenerator.GenerateTerrain(2);
            s.Armies[2].Capacity = 26;
            s.Economy.StartFood = 8000; s.Economy.StartWood = 8000; s.Economy.StartStone = 800;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong seq = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++seq, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, civ));
            Steps(gateway, sim, s.Economy.AdvanceTicks + 2);
            for (int step = 2; step <= 3; step++)
            {
                for (int i = 0; i < 5; i++) gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, ++seq, 0));
                gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, civ));
                Steps(gateway, sim, (step == 2 ? s.Economy.Age2Ticks : s.Economy.Age3Ticks) + 2);
            }
            Assume.That(sim.Capture(1).Economy.Age, Is.EqualTo(3));
            return (s, sim, gateway, seq);
        }

        [Test]
        public void TradingForGemsIsRefusedWithoutAFinishedMarket()
        {
            var s = MapGenerator.GenerateTerrain(2);
            s.Economy.StartFood = 8000; s.Economy.StartWood = 8000;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong seq = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++seq, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, CivKind.Agrarian));
            Steps(gateway, sim, s.Economy.AdvanceTicks + 2);
            gateway.SubmitEconomy(EconomyCommand.Trade(1, ++seq, ResourceKind.Wood, ResourceKind.Gems));
            Steps(gateway, sim, 1);
            Assert.That(Number(Fields(sim), "Economy[1].Gems"), Is.EqualTo(0));
        }

        [Test]
        public void AFinishedMarketTradesWoodForGemsOneWayOnlyAndItReplays()
        {
            var (scenario, sim, gateway, seq0) = ThirdAge(CivKind.Agrarian);
            ulong seq = seq0;
            uint market = Place(gateway, sim, scenario, BuildingKind.Market, ref seq);
            Assume.That(market, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, market));
            for (int t = 0; t < 4000 && Fields(sim)["Buildings[" + market + "].Complete"] != "1"; t += 20) Steps(gateway, sim, 20);
            Assume.That(Fields(sim)["Buildings[" + market + "].Complete"], Is.EqualTo("1"));

            var before = Fields(sim);
            long wood = Number(before, "Economy[1].Wood"), gems = Number(before, "Economy[1].Gems");
            gateway.SubmitEconomy(EconomyCommand.Trade(1, ++seq, ResourceKind.Wood, ResourceKind.Gems));
            Steps(gateway, sim, 1);
            var after = Fields(sim);
            Assert.That(Number(after, "Economy[1].Wood"), Is.EqualTo(wood - scenario.Economy.TradeLot), "the lot was paid");
            Assert.That(Number(after, "Economy[1].Gems"), Is.EqualTo(gems + scenario.Economy.GemsTradeReturn), "exactly GemsTradeReturn was given");

            // Gems cannot be given back for another resource: the market never returns them.
            gateway.SubmitEconomy(EconomyCommand.Trade(1, ++seq, ResourceKind.Gems, ResourceKind.Wood));
            Steps(gateway, sim, 1);
            Assert.That(Number(Fields(sim), "Economy[1].Gems"), Is.EqualTo(Number(after, "Economy[1].Gems")), "Gems given back are refused");

            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, gateway.Inputs, sim.Capture(1).Tick, build);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, build);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        [Test]
        public void GemArmorNeedsTheThirdAgeAndEnoughGemsAndThenArmsEverySoldier()
        {
            var (scenario, sim, gateway, seq0) = ThirdAge(CivKind.Metallurgy);
            ulong seq = seq0;
            uint smith = Place(gateway, sim, scenario, BuildingKind.Blacksmith, ref seq);
            Assume.That(smith, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, smith));
            for (int t = 0; t < 4000 && Fields(sim)["Buildings[" + smith + "].Complete"] != "1"; t += 20) Steps(gateway, sim, 20);
            Assume.That(Fields(sim)["Buildings[" + smith + "].Complete"], Is.EqualTo("1"));

            // No Gems in store: the research is refused even though the age is right.
            Assume.That(Number(Fields(sim), "Economy[1].Gems"), Is.EqualTo(0));
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++seq, smith, TechKind.GemArmor));
            Steps(gateway, sim, 1);
            Assert.That(Fields(sim)["Buildings[" + smith + "].Researching"], Is.EqualTo("0"), "GemArmor is paid in Gems");

            // Nothing else researched first is required (32 #19: Gems are a separate route).
            var before = Fields(sim);
            var alive = Enumerable.Range(1, (int)Number(before, "NextSoldierId") - 1)
                .Where(id => before["Soldiers[" + id + "].FactionId"] == "1" && before["Soldiers[" + id + "].Alive"] == "1").ToArray();
            Assume.That(alive.Length, Is.GreaterThan(0));

            uint market = Place(gateway, sim, scenario, BuildingKind.Market, ref seq);
            Assume.That(market, Is.Not.EqualTo(0u));
            // Only 3 villagers exist per side (ids 1-3 for the west); reuse them for the second building.
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, market));
            for (int t = 0; t < 4000 && Fields(sim)["Buildings[" + market + "].Complete"] != "1"; t += 20) Steps(gateway, sim, 20);
            Assume.That(Fields(sim)["Buildings[" + market + "].Complete"], Is.EqualTo("1"));
            int need = scenario.Economy.TechGems[(int)TechKind.GemArmor - 1];
            while (Number(Fields(sim), "Economy[1].Gems") < need)
            {
                gateway.SubmitEconomy(EconomyCommand.Trade(1, ++seq, ResourceKind.Wood, ResourceKind.Gems));
                Steps(gateway, sim, 1);
            }
            long gemsBeforeResearch = Number(Fields(sim), "Economy[1].Gems");

            gateway.SubmitEconomy(EconomyCommand.Research(1, ++seq, smith, TechKind.GemArmor));
            Steps(gateway, sim, 1);
            var f = Fields(sim);
            Assert.That(f["Buildings[" + smith + "].Researching"], Is.EqualTo(((byte)TechKind.GemArmor).ToString(CultureInfo.InvariantCulture)));
            Assert.That(Number(f, "Economy[1].Gems"), Is.EqualTo(gemsBeforeResearch - need), "the Gems were paid");
            Steps(gateway, sim, scenario.Economy.TechTicks[(int)TechKind.GemArmor - 1] + 1);
            f = Fields(sim);
            Assert.That(Number(f, "Economy[1].Techs") & (1L << ((int)TechKind.GemArmor - 1)), Is.Not.EqualTo(0), "GemArmor researched");
            foreach (int id in alive.Where(id => f["Soldiers[" + id + "].Alive"] == "1"))
                Assert.That(Number(f, "Soldiers[" + id + "].Parameters.Hp"),
                    Is.EqualTo(Number(before, "Soldiers[" + id + "].Parameters.Hp") + scenario.Economy.GemArmorHp), "soldier " + id);
        }

        [Test]
        public void GemArmorDoesNotOpenBeforeTheThirdAge()
        {
            var s = MapGenerator.GenerateTerrain(2);
            s.Economy.StartFood = 8000; s.Economy.StartWood = 8000; s.Economy.StartStone = 800;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong seq = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++seq, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, CivKind.Agrarian));
            Steps(gateway, sim, s.Economy.AdvanceTicks + 2);
            Assume.That(sim.Capture(1).Economy.Age, Is.EqualTo(1));
            uint smith = Place(gateway, sim, s, BuildingKind.Blacksmith, ref seq);
            Assume.That(smith, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, smith));
            for (int t = 0; t < 4000 && Fields(sim)["Buildings[" + smith + "].Complete"] != "1"; t += 20) Steps(gateway, sim, 20);
            Assume.That(Fields(sim)["Buildings[" + smith + "].Complete"], Is.EqualTo("1"));
            gateway.SubmitEconomy(EconomyCommand.Research(1, ++seq, smith, TechKind.GemArmor));
            Steps(gateway, sim, 1);
            Assert.That(Fields(sim)["Buildings[" + smith + "].Researching"], Is.EqualTo("0"), "GemArmor waits for the third age");
        }

        [Test]
        public void AMapWithoutAgesNeverChangesGems()
        {
            var s = MapGenerator.Generate(1, true, true);
            var sim = new Battle(s);
            for (long t = 1; t <= 600; t++) sim.Step(t, Array.Empty<ScheduledInput>());
            var f = DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);
            Assert.That(f.Keys.Any(k => k.EndsWith("].Gems", StringComparison.Ordinal)), Is.False, "an ageless map never writes a Gems field");
        }
    }
}
