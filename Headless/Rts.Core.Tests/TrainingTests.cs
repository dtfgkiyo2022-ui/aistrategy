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

        /// <summary>Wood the three west villagers bring in over <paramref name="ticks"/>, from a far wood point, with or without a drop-off by it.</summary>
        private static long FarWood(bool dropSite, int ticks)
        {
            var s = MapGenerator.GenerateTerrain(2);
            s.Economy.StartWood = 1000;
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Auto(1, 1, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, 2, false));
            Steps(gateway, sim, 1);
            var core = s.Cores[0].Position;
            long Dist2(SimPoint p) { long dx = (p.X.Raw - core.X.Raw) / 65536, dz = (p.Z.Raw - core.Z.Raw) / 65536; return dx * dx + dz * dz; }
            var wood = s.ResourceNodes.Where(n => n.Kind == ResourceKind.Wood && Dist2(n.Position) >= 40 * 40 && Dist2(n.Position) <= 60 * 60)
                .OrderBy(n => Dist2(n.Position)).ThenBy(n => n.Id).First();
            ulong seq = 10;
            if (dropSite)
            {
                int cell = (int)(wood.Position.Z.Raw / 65536 / 2) * 128 + (int)(wood.Position.X.Raw / 65536 / 2);
                uint id = 0;
                foreach (var (dx, dz) in new[] { (2, 0), (-3, 0), (0, 2), (0, -3), (2, 2), (-3, -3), (2, -3), (-3, 2) })
                {
                    gateway.SubmitEconomy(EconomyCommand.Place(1, ++seq, BuildingKind.DropSite, cell + dz * 128 + dx, Facing.North));
                    Steps(gateway, sim, 1);
                    var f0 = Fields(sim);
                    if (Number(f0, "Buildings.Count") > 0) { id = 1; break; }
                }
                Assume.That(id, Is.EqualTo(1u), "a drop-off site by the far wood");
                gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, id));
                for (int t = 0; t < 3000 && Fields(sim)["Buildings[1].Complete"] != "1"; t += 20) Steps(gateway, sim, 20);
            }
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.ResourceNode, wood.Id));
            Steps(gateway, sim, 1);
            long before = Number(Fields(sim), "Economy[1].Wood");
            Steps(gateway, sim, ticks);
            return Number(Fields(sim), "Economy[1].Wood") - before;
        }

        /// <summary>32 #3: a drop-off by a far resource point shortens the walk, so the same villagers bring in more.</summary>
        [Test]
        public void ADropOffByAFarPointBringsInMoreWood()
        {
            long without = FarWood(false, 3000), with = FarWood(true, 3000);
            TestContext.WriteLine("far wood in 3000 ticks: without a drop-off " + without + ", with " + with);
            Assert.That(with, Is.GreaterThan(without));
        }

        /// <summary>West belts laid all around the east soldiers' starting places (outside the east core).</summary>
        private static ScenarioDefinition WestBeltsAmongEastSoldiers(ScenarioDefinition s)
        {
            var cells = new SortedSet<int>();
            var core = s.Cores[1].Position;
            var blocked = new HashSet<int>(s.Map.BlockedCellIds);
            var nodes = new HashSet<int>(s.ResourceNodes.Select(n => (int)(n.Position.Z.Raw / 65536 / 2) * 128 + (int)(n.Position.X.Raw / 65536 / 2)));
            foreach (var d in s.Soldiers.Where(d => d.FactionId == 2))
            {
                int c = (int)(d.Position.Z.Raw / 65536 / 2) * 128 + (int)(d.Position.X.Raw / 65536 / 2);
                foreach (int n in new[] { c + 1, c - 1, c + 128, c - 128 })
                {
                    long x = (n % 128) * 2 + 1 - core.X.Raw / 65536, z = (n / 128) * 2 + 1 - core.Z.Raw / 65536;
                    if (n >= 0 && n < 128 * 64 && !blocked.Contains(n) && !nodes.Contains(n) && x * x + z * z > 36) cells.Add(n);
                }
            }
            s.Belts = cells.Take(s.Economy.BeltLimit).Select(c => new BeltDefinition { Cell = c, FactionId = 1, Facing = Facing.North }).ToArray();
            return s;
        }

        /// <summary>32 #4: soldiers with nothing else to fight break the enemy belts in reach - cutting the lines.</summary>
        [Test]
        public void SoldiersBreakEnemyBeltsInReach()
        {
            var s = WestBeltsAmongEastSoldiers(MapGenerator.GenerateTerrain(1));
            int laid = s.Belts.Length;
            var sim = new Battle(s);
            for (long t = 1; t <= 600; t++) sim.Step(t, Array.Empty<ScheduledInput>());
            long left = Number(Fields(sim), "Belts.Count");
            TestContext.WriteLine("west belts among the east soldiers: " + laid + ", left after 600 ticks: " + left);
            Assert.That(left, Is.LessThan(laid), "some were broken");

            // Without ages (mapgen-2) the same belts stay: belts are not raided there.
            var old = WestBeltsAmongEastSoldiers(MapGenerator.Generate(1, true, true));
            var oldSim = new Battle(old);
            for (long t = 1; t <= 600; t++) oldSim.Step(t, Array.Empty<ScheduledInput>());
            Assert.That(Number(Fields(oldSim), "Belts.Count"), Is.EqualTo(old.Belts.Length));
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
