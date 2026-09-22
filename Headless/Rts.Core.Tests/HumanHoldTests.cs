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
    /// technical-design-v3 19 (V3-3 PR1): what the player touches, the automatic economy leaves alone until the player
    /// hands it back. Each run is thousands of ticks long, many AI cycles (20 ticks) and training times past the touch.
    /// </summary>
    public sealed class HumanHoldTests
    {
        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> f, string name) => long.Parse(f[name], CultureInfo.InvariantCulture);

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        private static int WestVillagers(Dictionary<string, string> f)
        {
            int count = 0;
            for (int i = 1; i <= Number(f, "Villagers.Count"); i++)
                if (f["Villagers[" + i + "].FactionId"] == "1" && f["Villagers[" + i + "].Alive"] == "1") count++;
            return count;
        }

        [Test]
        public void TrainingAVillagerByHandStopsTheAutomaticTrainingUntilReturned()
        {
            var free = new Battle(MapGenerator.Generate(1, true, true));
            var freeGateway = new CommandGateway(free);
            Steps(freeGateway, free, 3000);
            int left = WestVillagers(Fields(free));
            Assert.That(left, Is.GreaterThan(4), "left alone, the core trains villagers");

            var sim = new Battle(MapGenerator.Generate(1, true, true));
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Train(1, 1, 0, UnitKind.Villager));
            Steps(gateway, sim, 3000);
            var f = Fields(sim);
            Assert.That(f["Economy[1].CoreHeld"], Is.EqualTo("1"));
            Assert.That(WestVillagers(f), Is.EqualTo(4), "three at the start and the one the player trained, no more");
            Assert.That(WestVillagers(f), Is.LessThan(left));

            gateway.SubmitEconomy(EconomyCommand.ReturnToAuto(1, 2));
            Steps(gateway, sim, 2000);
            f = Fields(sim);
            Assert.That(f["Economy[1].CoreHeld"], Is.EqualTo("0"));
            Assert.That(WestVillagers(f), Is.GreaterThan(4), "handed back, the core trains again");
        }

        [Test]
        public void VillagersThePlayerAssignedAreNeverTakenForBuildingOrCarrying()
        {
            var s = MapGenerator.Generate(1, true, true);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            var core = s.Cores[0].Position;
            var wood = s.ResourceNodes.Where(n => n.Kind == ResourceKind.Wood)
                .OrderBy(n => Math.Abs((n.Position.X.Raw - core.X.Raw) / 65536) + Math.Abs((n.Position.Z.Raw - core.Z.Raw) / 65536)).ThenBy(n => n.Id).First();
            gateway.SubmitEconomy(EconomyCommand.Assign(1, 1, new uint[] { 1, 2 }, EconomyTargetKind.ResourceNode, wood.Id));
            for (int t = 0; t < 4000; t += 20)
            {
                Steps(gateway, sim, 20);
                var f = Fields(sim);
                for (int v = 1; v <= 2; v++)
                {
                    string n = "Villagers[" + v + "].";
                    Assert.That(f[n + "Held"], Is.EqualTo("1"));
                    Assert.That(f[n + "Task"], Is.Not.EqualTo("4").And.Not.EqualTo("5").And.Not.EqualTo("6").And.Not.EqualTo("7"), "villager " + v + " at " + f["Tick"]);
                    Assert.That(f[n + "HaulFrom"], Is.EqualTo("0"));
                }
            }
            // The third, not held, was free for the automatic economy all along.
            Assert.That(Fields(sim)["Villagers[3].Held"], Is.EqualTo("0"));
        }

        [Test]
        public void TheAutomaticLineGoesAroundABeltThePlayerLaidAndTheMatchReplays()
        {
            // Where the west line runs when nobody interferes.
            var probe = new Battle(MapGenerator.Generate(1, true, true));
            var probeGateway = new CommandGateway(probe);
            Steps(probeGateway, probe, 6000);
            var p = Fields(probe);
            var route = p.Keys.Where(k => k.StartsWith("Belts[", StringComparison.Ordinal) && k.EndsWith("].FactionId", StringComparison.Ordinal) && p[k] == "1")
                .Select(k => int.Parse(k.Substring(6, k.IndexOf(']') - 6), CultureInfo.InvariantCulture)).OrderBy(c => c).ToArray();
            Assume.That(route.Length, Is.GreaterThan(4), "the west line exists");
            int blocked = route[route.Length / 2];
            var aiFacing = (Facing)byte.Parse(p["Belts[" + blocked + "].Facing"], CultureInfo.InvariantCulture);
            var playerFacing = (Facing)(((int)aiFacing + 2) % 4); // against the line

            var s = MapGenerator.Generate(1, true, true);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.PlaceBelt(1, 1, new[] { blocked }, new[] { playerFacing }));
            Steps(gateway, sim, 7000);
            var f = Fields(sim);
            Assert.That(f["Belts[" + blocked + "].Held"], Is.EqualTo("1"));
            Assert.That(f["Belts[" + blocked + "].Facing"], Is.EqualTo(((byte)playerFacing).ToString(CultureInfo.InvariantCulture)), "the automatic line did not turn it");
            bool metalMoved = f.Any(k => k.Key.StartsWith("Belts[", StringComparison.Ordinal) && k.Key.EndsWith("].Item", StringComparison.Ordinal)
                && k.Value == ((byte)ResourceKind.Metal).ToString(CultureInfo.InvariantCulture) && f[k.Key.Replace("].Item", "].FactionId")] == "1");
            // Infantry costs metal here, so a west soldier past the starting ones was paid for by the west line.
            bool westTrained = Enumerable.Range(s.Soldiers.Length + 1, (int)Number(f, "NextSoldierId") - s.Soldiers.Length - 1)
                .Any(id => f["Soldiers[" + id + "].FactionId"] == "1");
            Assert.That(metalMoved || Number(f, "Economy[1].Metal") > 0 || westTrained, "the west line still works around it");

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
        public void TheEnemyLineIsNotTouchedAndAVerThreeOneMapHasNoHolds()
        {
            var sim = new Battle(MapGenerator.Generate(2, true));
            var gateway = new CommandGateway(sim);
            gateway.SubmitEconomy(EconomyCommand.Assign(1, 1, new uint[] { 1 }, EconomyTargetKind.ResourceNode, 1));
            gateway.SubmitEconomy(EconomyCommand.Train(1, 2, 0, UnitKind.Villager));
            gateway.SubmitEconomy(EconomyCommand.ReturnToAuto(1, 3));
            Steps(gateway, sim, 50);
            Assert.That(Fields(sim).Keys.Any(k => k.EndsWith("Held", StringComparison.Ordinal)), Is.False, "V3-1 maps keep their canonical state");
        }
    }
}
