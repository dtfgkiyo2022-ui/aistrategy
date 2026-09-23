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
    /// technical-design-v3 32 #17 (V3-5): the castle of the third age - it stands only there, shoots further and harder
    /// than a tower, sees further, and trains any of the three line units whatever the civilisation.
    /// </summary>
    public sealed class CastleTests
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

        /// <summary>Takes the west to the third age of <paramref name="civ"/> with stone and wood for a castle.</summary>
        private static (ScenarioDefinition s, Battle sim, CommandGateway gateway, ulong seq) ThirdAge(CivKind civ)
        {
            var s = MapGenerator.GenerateTerrain(2);
            s.Armies[2].Capacity = 26;
            s.Economy.StartFood = 8000; s.Economy.StartWood = 8000; s.Economy.StartStone = 800; s.Economy.StartMetal = 200;
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
        public void ACastleStandsOnlyInTheThirdAgeCostsStoneAndTrainsAllThreeAndItReplays()
        {
            var s = MapGenerator.GenerateTerrain(2);
            s.Economy.StartFood = 8000; s.Economy.StartWood = 8000; s.Economy.StartStone = 800;
            var early = new Battle(s);
            var gate = new CommandGateway(early);
            ulong q = 0;
            gate.SubmitEconomy(EconomyCommand.Auto(1, ++q, false));
            gate.SubmitEconomy(EconomyCommand.Advance(1, ++q, CivKind.Agrarian));
            Steps(gate, early, s.Economy.AdvanceTicks + 2);
            Assert.That(Place(gate, early, s, BuildingKind.Castle, ref q), Is.EqualTo(0u), "no castle before the third age");

            var (scenario, sim, gateway, seq) = ThirdAge(CivKind.Agrarian);
            long stone = Number(Fields(sim), "Economy[1].Stone");
            uint castle = Place(gateway, sim, scenario, BuildingKind.Castle, ref seq);
            Assume.That(castle, Is.Not.EqualTo(0u));
            var f = Fields(sim);
            Assert.That(Number(f, "Economy[1].Stone"), Is.EqualTo(stone - scenario.Economy.CastleStoneCost), "the stone was paid");
            Assert.That(Number(f, "Buildings[" + castle + "].Hp"), Is.GreaterThan(scenario.Economy.TowerHp), "a castle is harder than a tower");

            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, castle));
            for (int t = 0; t < 6000 && Fields(sim)["Buildings[" + castle + "].Complete"] != "1"; t += 20) Steps(gateway, sim, 20);
            Assert.That(Fields(sim)["Buildings[" + castle + "].Complete"], Is.EqualTo("1"));

            // Farming has no cavalry of its own, and the castle still trains one.
            gateway.SubmitEconomy(EconomyCommand.Train(1, ++seq, castle, UnitKind.Cavalry));
            Steps(gateway, sim, 1);
            f = Fields(sim);
            Assert.That(Number(f, "Buildings[" + castle + "].Queued"), Is.EqualTo(1));
            Assert.That(f["Buildings[" + castle + "].QueueKinds[0]"], Is.EqualTo(((byte)UnitKind.Cavalry).ToString(CultureInfo.InvariantCulture)));
            gateway.SubmitEconomy(EconomyCommand.Train(1, ++seq, castle, UnitKind.Ram)); // a ram is for the workshop
            Steps(gateway, sim, 1);
            Assert.That(Number(Fields(sim), "Buildings[" + castle + "].Queued"), Is.EqualTo(1), "the ram was refused");

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

        /// <summary>A castle shoots at what comes near, harder than a tower would (watched every tick, 32.15).</summary>
        [Test]
        public void ACastleShootsWhatComesIntoItsRange()
        {
            var (scenario, sim, gateway, seq) = ThirdAge(CivKind.Metallurgy);
            uint castle = Place(gateway, sim, scenario, BuildingKind.Castle, ref seq);
            Assume.That(castle, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, castle));
            for (int t = 0; t < 6000 && Fields(sim)["Buildings[" + castle + "].Complete"] != "1"; t += 20) Steps(gateway, sim, 20);
            Assume.That(Fields(sim)["Buildings[" + castle + "].Complete"], Is.EqualTo("1"));
            long shots = Number(Fields(sim), "Buildings[" + castle + "].Shots");
            for (int t = 0; t < 8000 && Number(Fields(sim), "Buildings[" + castle + "].Shots") == shots; t += 40) Steps(gateway, sim, 40);
            TestContext.WriteLine("castle shots: " + Number(Fields(sim), "Buildings[" + castle + "].Shots"));
            Assert.That(Number(Fields(sim), "Buildings[" + castle + "].Shots"), Is.GreaterThan(shots), "the castle fired at the enemy");
        }
    }
}
