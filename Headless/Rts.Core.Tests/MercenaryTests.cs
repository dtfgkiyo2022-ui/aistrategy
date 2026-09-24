using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Decision;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>
    /// technical-design-v3 32 #20 (V3-5): the mercenary. Trained only at a castle, paid in Gems alone, fights as
    /// infantry with its own numbers, and stands outside the counter triangle (like a ram or a scout).
    /// </summary>
    public sealed class MercenaryTests
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
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r || cx + dx < 0 || cz + dz < 0 || cx + dx > Width - 5 || cz + dz > 58) continue;
                        long before = Number(Fields(sim), "Buildings.Count");
                        gateway.SubmitEconomy(EconomyCommand.Place(1, ++seq, kind, (cz + dz) * Width + cx + dx, Facing.North));
                        Steps(gateway, sim, 1);
                        var f = Fields(sim);
                        if (Number(f, "Buildings.Count") > before && f["Buildings[" + (before + 1) + "].Kind"] == ((byte)kind).ToString(CultureInfo.InvariantCulture)) return (uint)(before + 1);
                    }
            return 0;
        }

        /// <summary>Takes the west to the third age of <paramref name="civ"/>, with a finished castle and Gems in store.</summary>
        private static (ScenarioDefinition s, Battle sim, CommandGateway gateway, ulong seq, uint castle) CastleWithGems(CivKind civ)
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

            uint castle = Place(gateway, sim, s, BuildingKind.Castle, ref seq);
            Assume.That(castle, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, castle));
            for (int t = 0; t < 6000 && Fields(sim)["Buildings[" + castle + "].Complete"] != "1"; t += 20) Steps(gateway, sim, 20);
            Assume.That(Fields(sim)["Buildings[" + castle + "].Complete"], Is.EqualTo("1"));

            uint market = Place(gateway, sim, s, BuildingKind.Market, ref seq);
            Assume.That(market, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, market));
            for (int t = 0; t < 6000 && Fields(sim)["Buildings[" + market + "].Complete"] != "1"; t += 20) Steps(gateway, sim, 20);
            Assume.That(Fields(sim)["Buildings[" + market + "].Complete"], Is.EqualTo("1"));
            int need = s.Economy.MercenaryGems;
            while (Number(Fields(sim), "Economy[1].Gems") < need)
            {
                gateway.SubmitEconomy(EconomyCommand.Trade(1, ++seq, ResourceKind.Wood, ResourceKind.Gems));
                Steps(gateway, sim, 1);
            }
            return (s, sim, gateway, seq, castle);
        }

        [Test]
        public void MercenariesCannotBeTrainedAtABarracksOrBeforeACastleStands()
        {
            var s = MapGenerator.GenerateTerrain(2);
            s.Economy.StartFood = 8000; s.Economy.StartWood = 8000; s.Economy.StartStone = 800;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong seq = 0;
            // Auto economy stays on here, so it places the barracks itself; no manual building is needed for this check.
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, CivKind.Agrarian));
            uint barracks = 0;
            for (int t = 0; t < 4000 && barracks == 0; t += 20)
            {
                Steps(gateway, sim, 20);
                var b = sim.Capture(1).Economy.Buildings.FirstOrDefault(v => v.Kind == BuildingKind.Barracks && v.FactionId == 1 && v.Complete);
                barracks = b.Id;
            }
            Assume.That(barracks, Is.Not.EqualTo(0u), "the west placed a barracks");
            // Auto economy keeps queuing infantry on its own here, so the check is that no Mercenary ever joins the
            // queue - not that the queue stays empty.
            gateway.SubmitEconomy(EconomyCommand.Train(1, ++seq, barracks, UnitKind.Mercenary));
            Steps(gateway, sim, 1);
            var f = Fields(sim);
            long queued = Number(f, "Buildings[" + barracks + "].Queued");
            for (int i = 0; i < queued; i++)
                Assert.That(f["Buildings[" + barracks + "].QueueKinds[" + i + "]"], Is.Not.EqualTo(((byte)UnitKind.Mercenary).ToString(CultureInfo.InvariantCulture)),
                    "a barracks never trains a mercenary");
        }

        [Test]
        public void ACastleTrainsAMercenaryForGemsAloneWhateverTheCivilisationAndItReplays()
        {
            var (scenario, sim, gateway, seq0, castle) = CastleWithGems(CivKind.Agrarian);
            ulong seq = seq0;
            var before = Fields(sim);
            long food = Number(before, "Economy[1].Food"), wood = Number(before, "Economy[1].Wood"), gems = Number(before, "Economy[1].Gems");
            gateway.SubmitEconomy(EconomyCommand.Train(1, ++seq, castle, UnitKind.Mercenary));
            Steps(gateway, sim, 1);
            var f = Fields(sim);
            Assert.That(Number(f, "Buildings[" + castle + "].Queued"), Is.EqualTo(1));
            Assert.That(f["Buildings[" + castle + "].QueueKinds[0]"], Is.EqualTo(((byte)UnitKind.Mercenary).ToString(CultureInfo.InvariantCulture)));
            Assert.That(Number(f, "Economy[1].Gems"), Is.EqualTo(gems - scenario.Economy.MercenaryGems), "only Gems were paid");
            Assert.That(Number(f, "Economy[1].Food"), Is.EqualTo(food), "no food");
            Assert.That(Number(f, "Economy[1].Wood"), Is.EqualTo(wood), "no wood");

            long next = Number(f, "NextSoldierId");
            Steps(gateway, sim, scenario.Economy.MercenaryTicks + 20);
            f = Fields(sim);
            long mine = Enumerable.Range((int)next, (int)(Number(f, "NextSoldierId") - next))
                .FirstOrDefault(i => f["Soldiers[" + i + "].FactionId"] == "1" && f["Soldiers[" + i + "].Class"] == ((byte)UnitKind.Mercenary).ToString(CultureInfo.InvariantCulture));
            Assert.That(mine, Is.GreaterThan(0), "the mercenary came out");
            string id = "Soldiers[" + mine + "].";
            Assert.That(f[id + "Kind"], Is.EqualTo(((byte)UnitKind.Infantry).ToString(CultureInfo.InvariantCulture)), "it fights as infantry");
            Assert.That(Number(f, id + "Parameters.Hp"), Is.EqualTo(scenario.Economy.MercenaryHp));
            Assert.That(Number(f, id + "Parameters.Damage"), Is.EqualTo(scenario.Economy.MercenaryDamage));

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
        public void TrainingIsRefusedWithoutEnoughGemsAndCancellingReturnsWhatWasPaid()
        {
            var (scenario, sim, gateway, seq0, castle) = CastleWithGems(CivKind.Metallurgy);
            ulong seq = seq0;
            // Spend the Gems down to nothing, then try to train.
            var f = Fields(sim);
            long gems = Number(f, "Economy[1].Gems");
            Assume.That(gems, Is.GreaterThanOrEqualTo(scenario.Economy.MercenaryGems));
            // Queue one (affordable), then try a second with whatever remains.
            gateway.SubmitEconomy(EconomyCommand.Train(1, ++seq, castle, UnitKind.Mercenary));
            Steps(gateway, sim, 1);
            long afterFirst = Number(Fields(sim), "Economy[1].Gems");
            if (afterFirst < scenario.Economy.MercenaryGems)
            {
                gateway.SubmitEconomy(EconomyCommand.Train(1, ++seq, castle, UnitKind.Mercenary));
                Steps(gateway, sim, 1);
                Assert.That(Number(Fields(sim), "Buildings[" + castle + "].Queued"), Is.EqualTo(1), "the second one was refused for lack of Gems");
            }

            gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, ++seq, castle));
            Steps(gateway, sim, 1);
            Assert.That(Number(Fields(sim), "Economy[1].Gems"), Is.EqualTo(gems), "cancelling gave every paid Gem back");
            Assert.That(Number(Fields(sim), "Buildings[" + castle + "].Queued"), Is.EqualTo(0));
        }

        [Test]
        public void MercenaryStandsOutsideTheCounterTriangle()
        {
            Assert.That(CombatMath.Counters(UnitKind.Mercenary, UnitKind.Infantry), Is.False);
            Assert.That(CombatMath.Counters(UnitKind.Mercenary, UnitKind.Archer), Is.False);
            Assert.That(CombatMath.Counters(UnitKind.Mercenary, UnitKind.Cavalry), Is.False);
            Assert.That(CombatMath.Counters(UnitKind.Archer, UnitKind.Mercenary), Is.False);
            Assert.That(CombatMath.Counters(UnitKind.Cavalry, UnitKind.Mercenary), Is.False);
            Assert.That(CombatMath.Counters(UnitKind.Infantry, UnitKind.Mercenary), Is.False);
            Assert.That(CombatMath.DamageAgainst(10, UnitKind.Archer, UnitKind.Mercenary, 500), Is.EqualTo(10), "no bonus either way");
        }
    }
}
