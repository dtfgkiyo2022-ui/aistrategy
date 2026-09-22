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
    /// <summary>technical-design-v3 32 #9 (V3-5): the market's trades and the siege workshop's rams.</summary>
    public sealed class MarketTests
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

        private static void BuildUp(CommandGateway gateway, Battle sim, uint building, ref ulong seq)
        {
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, building));
            for (int t = 0; t < 4000 && Fields(sim)["Buildings[" + building + "].Complete"] != "1"; t += 20) Steps(gateway, sim, 20);
            Assert.That(Fields(sim)["Buildings[" + building + "].Complete"], Is.EqualTo("1"));
        }

        private static void AssertReplays(CommandGateway gateway, Battle sim, ScenarioDefinition s)
        {
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

        [Test]
        public void AFinishedMarketTradesOneLotForTheReturnAndItReplays()
        {
            var s = MapGenerator.GenerateTerrain(2);
            s.Economy.StartFood = 3000; s.Economy.StartWood = 3000;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong seq = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++seq, false));
            Steps(gateway, sim, 1);
            Assert.That(Place(gateway, sim, s, BuildingKind.Market, ref seq), Is.EqualTo(0u), "no market in the primitive age");
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, CivKind.Agrarian));
            Steps(gateway, sim, s.Economy.AdvanceTicks + 2);
            uint market = Place(gateway, sim, s, BuildingKind.Market, ref seq);
            Assume.That(market, Is.Not.EqualTo(0u));

            // Before it stands, nothing is traded.
            long stone = Number(Fields(sim), "Economy[1].Stone");
            gateway.SubmitEconomy(EconomyCommand.Trade(1, ++seq, ResourceKind.Food, ResourceKind.Stone));
            Steps(gateway, sim, 1);
            Assert.That(Number(Fields(sim), "Economy[1].Stone"), Is.EqualTo(stone));

            BuildUp(gateway, sim, market, ref seq);
            var before = Fields(sim);
            gateway.SubmitEconomy(EconomyCommand.Trade(1, ++seq, ResourceKind.Food, ResourceKind.Food));   // same: refused
            gateway.SubmitEconomy(EconomyCommand.Trade(1, ++seq, ResourceKind.Food, ResourceKind.Metal));  // not traded: refused
            gateway.SubmitEconomy(EconomyCommand.Trade(1, ++seq, ResourceKind.Food, ResourceKind.Stone));
            Steps(gateway, sim, 1);
            var f = Fields(sim);
            Assert.That(Number(f, "Economy[1].Stone"), Is.EqualTo(Number(before, "Economy[1].Stone") + s.Economy.TradeReturn));
            Assert.That(Number(f, "Economy[1].Metal"), Is.EqualTo(Number(before, "Economy[1].Metal")));
            Assert.That(Number(f, "Economy[1].Food"), Is.LessThanOrEqualTo(Number(before, "Economy[1].Food") - s.Economy.TradeLot + 50), "the lot was paid");
            Assert.That(sim.Capture(1).Economy.TradeLot, Is.EqualTo(s.Economy.TradeLot));
            AssertReplays(gateway, sim, s);
        }

        [Test]
        public void TheSiegeWorkshopTrainsRamsOnlyInTheSecondAge()
        {
            var s = MapGenerator.GenerateTerrain(7);
            s.Armies[2].Capacity = 26;
            s.Economy.StartFood = 3000; s.Economy.StartWood = 3000;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong seq = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++seq, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, CivKind.Metallurgy));
            Steps(gateway, sim, s.Economy.AdvanceTicks + 2);
            Assert.That(Place(gateway, sim, s, BuildingKind.SiegeWorkshop, ref seq), Is.EqualTo(0u), "no workshop before the second age");
            for (int i = 0; i < 5; i++) gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, ++seq, 0));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, CivKind.Metallurgy));
            Steps(gateway, sim, s.Economy.Age2Ticks + 2);
            Assume.That(sim.Capture(1).Economy.Age, Is.EqualTo(2));
            uint workshop = Place(gateway, sim, s, BuildingKind.SiegeWorkshop, ref seq);
            Assume.That(workshop, Is.Not.EqualTo(0u));
            BuildUp(gateway, sim, workshop, ref seq);

            gateway.SubmitEconomy(EconomyCommand.Train(1, ++seq, workshop, UnitKind.Infantry)); // a workshop makes rams only
            gateway.SubmitEconomy(EconomyCommand.Train(1, ++seq, workshop, UnitKind.Ram));
            Steps(gateway, sim, 1);
            var f = Fields(sim);
            string n = "Buildings[" + workshop + "].";
            Assert.That(Number(f, n + "Queued"), Is.EqualTo(1));
            Assert.That(f[n + "QueueKinds[0]"], Is.EqualTo(((byte)UnitKind.Ram).ToString(CultureInfo.InvariantCulture)));
            long next = Number(f, "NextSoldierId");
            Steps(gateway, sim, s.Economy.RamTicks + 20);
            f = Fields(sim);
            long ram = Enumerable.Range((int)next, (int)(Number(f, "NextSoldierId") - next))
                .FirstOrDefault(i => f["Soldiers[" + i + "].FactionId"] == "1" && f["Soldiers[" + i + "].Class"] == ((byte)UnitKind.Ram).ToString(CultureInfo.InvariantCulture));
            Assert.That(ram, Is.GreaterThan(0), "a ram came out");
            Assert.That(Number(f, "Soldiers[" + ram + "].Parameters.Hp"), Is.EqualTo(s.Economy.RamHp));
            Assert.That(Number(f, "Soldiers[" + ram + "].Parameters.Speed.Raw"), Is.EqualTo(s.Economy.RamSpeed.Raw));
            AssertReplays(gateway, sim, s);
        }
    }
}
