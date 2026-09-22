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
    /// technical-design-v3 32 #12 (V3-5): the archery range and the stable. Either civilisation may put one up from its
    /// second age, and train there the unit its own civilisation never trains.
    /// </summary>
    public sealed class RangeStableTests
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

        /// <summary>Farming builds a stable for cavalry, metallurgy an archery range for archers - the unit it lacks.</summary>
        [TestCase(CivKind.Agrarian, BuildingKind.Stable, UnitKind.Cavalry, BuildingKind.ArcheryRange, UnitKind.Archer)]
        [TestCase(CivKind.Metallurgy, BuildingKind.ArcheryRange, UnitKind.Archer, BuildingKind.Stable, UnitKind.Cavalry)]
        public void EitherCivilisationTrainsTheUnitItLacksAtItsOwnBuildingAndItReplays(CivKind civ, BuildingKind kind, UnitKind unit, BuildingKind otherKind, UnitKind otherUnit)
        {
            var s = MapGenerator.GenerateTerrain(2);
            s.Armies[2].Capacity = 26;
            s.Economy.StartFood = 6000; s.Economy.StartWood = 6000; s.Economy.StartMetal = 200;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            ulong seq = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++seq, false));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, civ));
            Steps(gateway, sim, s.Economy.AdvanceTicks + 2);
            Assert.That(Place(gateway, sim, s, kind, ref seq), Is.EqualTo(0u), "not before the second age");

            for (int i = 0; i < 5; i++) gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, ++seq, 0));
            gateway.SubmitEconomy(EconomyCommand.Advance(1, ++seq, civ));
            Steps(gateway, sim, s.Economy.Age2Ticks + 2);
            Assume.That(sim.Capture(1).Economy.Age, Is.EqualTo(2));

            uint building = Place(gateway, sim, s, kind, ref seq);
            Assume.That(building, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, building));
            for (int t = 0; t < 4000 && Fields(sim)["Buildings[" + building + "].Complete"] != "1"; t += 20) Steps(gateway, sim, 20);
            Assert.That(Fields(sim)["Buildings[" + building + "].Complete"], Is.EqualTo("1"));

            gateway.SubmitEconomy(EconomyCommand.Train(1, ++seq, building, UnitKind.Infantry)); // one unit only
            gateway.SubmitEconomy(EconomyCommand.Train(1, ++seq, building, otherUnit));         // the other building's unit
            gateway.SubmitEconomy(EconomyCommand.Train(1, ++seq, building, unit));
            Steps(gateway, sim, 1);
            var f = Fields(sim);
            string n = "Buildings[" + building + "].";
            Assert.That(Number(f, n + "Queued"), Is.EqualTo(1));
            Assert.That(f[n + "QueueKinds[0]"], Is.EqualTo(((byte)unit).ToString(CultureInfo.InvariantCulture)));
            long next = Number(f, "NextSoldierId");
            Steps(gateway, sim, 400);
            f = Fields(sim);
            long mine = Enumerable.Range((int)next, (int)(Number(f, "NextSoldierId") - next))
                .FirstOrDefault(i => f["Soldiers[" + i + "].FactionId"] == "1" && f["Soldiers[" + i + "].Class"] == ((byte)unit).ToString(CultureInfo.InvariantCulture));
            Assert.That(mine, Is.GreaterThan(0), "it came out");
            Assert.That(f["Soldiers[" + mine + "].Kind"], Is.EqualTo(((byte)UnitKind.Infantry).ToString(CultureInfo.InvariantCulture)), "it fights as infantry");
            if (unit == UnitKind.Archer) Assert.That(Number(f, "Soldiers[" + mine + "].Parameters.Range.Raw"), Is.EqualTo(s.Economy.ArcherRange.Raw));
            else Assert.That(Number(f, "Soldiers[" + mine + "].Parameters.Speed.Raw"), Is.EqualTo(s.Economy.CavalrySpeed.Raw));

            // The other of the two stands just as well: both are open to both civilisations.
            Assert.That(Place(gateway, sim, s, otherKind, ref seq), Is.Not.EqualTo(0u), otherKind + " is open too");

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
