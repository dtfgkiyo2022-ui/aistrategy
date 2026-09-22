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
    /// technical-design-v3 32 (V3-5 PR1): training queues that hold unit kinds, and scouts from the barracks. Runs cross
    /// many training times and AI cycles.
    /// </summary>
    public sealed class TrainingTests
    {
        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> f, string name) => long.Parse(f[name], CultureInfo.InvariantCulture);

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        /// <summary>The terrain map with one west scout already lost, so the scout army has room.</summary>
        private static ScenarioDefinition OneScoutLost(ulong seed)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            var scout = Array.FindIndex(s.Soldiers, d => d.FactionId == 1 && d.Kind == UnitKind.Scout);
            s.Soldiers[scout].Alive = false;
            s.Soldiers[scout].Hp = 0;
            return s;
        }

        private static uint WestBarracks(Dictionary<string, string> f)
        {
            for (int b = 1; b <= Number(f, "Buildings.Count"); b++)
                if (f["Buildings[" + b + "].FactionId"] == "1" && f["Buildings[" + b + "].Kind"] == "1" && f["Buildings[" + b + "].Complete"] == "1") return (uint)b;
            return 0;
        }

        [Test]
        public void LeftAloneTheWestReplacesItsLostScout()
        {
            var s = OneScoutLost(1);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            Steps(gateway, sim, 6000);
            var f = Fields(sim);
            var trained = Enumerable.Range(s.Soldiers.Length + 1, (int)Number(f, "NextSoldierId") - s.Soldiers.Length - 1)
                .Where(id => f["Soldiers[" + id + "].FactionId"] == "1" && f["Soldiers[" + id + "].Kind"] == ((byte)UnitKind.Scout).ToString(CultureInfo.InvariantCulture)).ToArray();
            Assert.That(trained.Length, Is.EqualTo(1), "exactly the one missing scout");
            uint army = (uint)Number(f, "Soldiers[" + trained[0] + "].ArmyId");
            Assert.That(s.Armies[army - 1].Role, Is.EqualTo("scout"), "it joined the scout army");
            Assert.That(Number(f, "Soldiers[" + trained[0] + "].Parameters.Hp"), Is.EqualTo(40), "a scout, not an infantry");
        }

        [Test]
        public void AQueueKeepsItsKindsInOrderAndACancelReturnsTheLastOnesPrice()
        {
            var s = OneScoutLost(1);
            s.Economy.StartFood = 2000; s.Economy.StartWood = 2000;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            uint barracks = 0;
            for (int t = 0; t < 3000 && barracks == 0; t += 20) { Steps(gateway, sim, 20); barracks = WestBarracks(Fields(sim)); }
            Assume.That(barracks, Is.Not.EqualTo(0u));
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            Steps(gateway, sim, 1);
            // Whatever the automatic economy queued before, it is cancelled away first.
            for (int i = 0; i < 5; i++) gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, 2, barracks));
            Steps(gateway, sim, 1);
            long food = Number(Fields(sim), "Economy[1].Food");
            gateway.SubmitEconomy(EconomyCommand.Train(1, 3, barracks, UnitKind.Infantry));
            gateway.SubmitEconomy(EconomyCommand.Train(1, 4, barracks, UnitKind.Scout));
            gateway.SubmitEconomy(EconomyCommand.Train(1, 5, barracks, UnitKind.Scout)); // the scout army has room for one only
            gateway.SubmitEconomy(EconomyCommand.Train(1, 6, barracks, UnitKind.Infantry));
            Steps(gateway, sim, 1);
            var f = Fields(sim);
            string n = "Buildings[" + barracks + "].";
            Assert.That(Number(f, n + "Queued"), Is.EqualTo(3));
            Assert.That(new[] { f[n + "QueueKinds[0]"], f[n + "QueueKinds[1]"], f[n + "QueueKinds[2]"] }, Is.EqualTo(new[] { "1", "2", "1" }));
            Assert.That(Number(f, "Economy[1].Food"), Is.EqualTo(food - 2 * 50 - s.Economy.ScoutFoodCost), "two infantry and one scout paid");

            gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, 7, barracks)); // the last infantry
            gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, 8, barracks)); // the scout
            Steps(gateway, sim, 1);
            f = Fields(sim);
            Assert.That(Number(f, n + "Queued"), Is.EqualTo(1));
            Assert.That(Number(f, "Economy[1].Food"), Is.EqualTo(food - 50), "the scout's own price came back");

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

        /// <summary>32 #2: the cap starts at BasePopulation and grows by HousePopulation per finished house.</summary>
        [Test]
        public void HousesRaiseThePopulationCapAndTheAutomaticEconomyBuildsThem()
        {
            var s = MapGenerator.GenerateTerrain(1);
            var sim = new Battle(s);
            Assert.That(sim.Capture(1).Economy.PopulationCap, Is.EqualTo(s.Economy.BasePopulation));
            var gateway = new CommandGateway(sim);
            Steps(gateway, sim, 6000);
            var f = Fields(sim);
            int houses = 0;
            for (int b = 1; b <= Number(f, "Buildings.Count"); b++)
                if (f["Buildings[" + b + "].FactionId"] == "1" && f["Buildings[" + b + "].Kind"] == "5" && f["Buildings[" + b + "].Alive"] == "1" && f["Buildings[" + b + "].Complete"] == "1") houses++;
            TestContext.WriteLine("west houses by 6000: " + houses + ", cap " + sim.Capture(1).Economy.PopulationCap + ", population " + sim.Capture(1).Economy.Population);
            Assert.That(houses, Is.GreaterThan(0), "the automatic economy built houses");
            Assert.That(sim.Capture(1).Economy.PopulationCap, Is.EqualTo(Math.Min(s.Economy.PopulationCap, s.Economy.BasePopulation + s.Economy.HousePopulation * houses)));
            Assert.That(sim.Capture(1).Economy.Population, Is.LessThanOrEqualTo(sim.Capture(1).Economy.PopulationCap));
        }

        [Test]
        public void AMapWithoutAgesHasNoHousesAndTheOldCap()
        {
            var s = MapGenerator.Generate(1, true, true);
            s.Economy.StartWood = 1000;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            Assert.That(sim.Capture(1).Economy.PopulationCap, Is.EqualTo(s.Economy.PopulationCap));
            int core = (int)(s.Cores[0].Position.Z.Raw / 65536 / 2) * 128 + (int)(s.Cores[0].Position.X.Raw / 65536 / 2);
            for (int d = 5; d < 12; d++) gateway.SubmitEconomy(EconomyCommand.Place(1, (ulong)d, BuildingKind.House, core + d, Facing.North));
            Steps(gateway, sim, 1);
            var f = Fields(sim);
            for (int b = 1; b <= Number(f, "Buildings.Count"); b++) Assert.That(f["Buildings[" + b + "].Kind"], Is.Not.EqualTo("5"));
        }

        [Test]
        public void AMapWithoutAgesTrainsNoScouts()
        {
            var s = MapGenerator.Generate(1, true, true);
            s.Soldiers[Array.FindIndex(s.Soldiers, d => d.FactionId == 1 && d.Kind == UnitKind.Scout)].Alive = false;
            s.Soldiers[Array.FindIndex(s.Soldiers, d => d.FactionId == 1 && d.Kind == UnitKind.Scout && !d.Alive)].Hp = 0;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            Steps(gateway, sim, 3000);
            var f = Fields(sim);
            Assert.That(f.Keys.Any(k => k.Contains("QueueKinds")), Is.False);
            for (long id = s.Soldiers.Length + 1; id < Number(f, "NextSoldierId"); id++)
                Assert.That(f["Soldiers[" + id + "].Kind"], Is.EqualTo(((byte)UnitKind.Infantry).ToString(CultureInfo.InvariantCulture)));
        }
    }
}
