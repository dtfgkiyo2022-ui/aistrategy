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
    /// <summary>technical-design-v3 6 (V3-1 PR4): direct economy operations go through the gateway, are logged, and replay.</summary>
    public sealed class EconomyCommandTests
    {
        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> f, string name) => long.Parse(f[name], CultureInfo.InvariantCulture);

        private static void Steps(CommandGateway gateway, Battle sim, int count)
        {
            for (int i = 0; i < count && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
        }

        /// <summary>The west site the automatic economy would choose: a known-legal footprint to place by hand.</summary>
        private static int WestSite(ulong seed)
        {
            var probe = new Battle(MapGenerator.Generate(seed, true));
            for (long t = 1; t <= 25; t++) probe.Step(t, Array.Empty<ScheduledInput>());
            var f = Fields(probe);
            for (int b = 1; b <= Number(f, "Buildings.Count"); b++)
                if (f["Buildings[" + b + "].FactionId"] == "1") return (int)Number(f, "Buildings[" + b + "].OriginCell");
            throw new InvalidOperationException("no west site");
        }

        [Test]
        public void APlayerRunsTheWestEconomyByHandAndTheMatchReplays()
        {
            int site = WestSite(1);
            var scenario = MapGenerator.Generate(1, true);
            scenario.Economy.StartWood = 300; // the barracks and an infantry, with nobody gathering
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            ulong seq = 0;
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++seq, false));
            Steps(gateway, sim, 1);
            var f = Fields(sim);
            Assert.That(f["Economy[1].AutoOff"], Is.EqualTo("1"));

            // A villager by hand; with the automatic economy off nothing else is trained.
            gateway.SubmitEconomy(EconomyCommand.Train(1, ++seq, 0, UnitKind.Villager));
            gateway.SubmitEconomy(EconomyCommand.Place(1, ++seq, BuildingKind.Barracks, site));
            Steps(gateway, sim, 1);
            f = Fields(sim);
            Assert.That(Number(f, "Economy[1].Queued"), Is.EqualTo(1));
            Assert.That(Number(f, "Economy[1].Food"), Is.EqualTo(scenario.Economy.StartFood - scenario.Economy.VillagerFoodCost));
            Assert.That(Number(f, "Economy[1].Wood"), Is.EqualTo(scenario.Economy.StartWood - scenario.Economy.BarracksWoodCost));
            uint barracks = 0;
            for (int b = 1; b <= Number(f, "Buildings.Count"); b++)
                if (f["Buildings[" + b + "].FactionId"] == "1") { barracks = (uint)b; Assert.That(Number(f, "Buildings[" + b + "].OriginCell"), Is.EqualTo(site)); }
            Assert.That(barracks, Is.Not.EqualTo(0u), "the barracks was placed where the player asked");

            // Idle villagers stay idle without the automatic economy; the player sends the three to build.
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1, 2, 3 }, EconomyTargetKind.Building, barracks));
            // 400 work over three builders is 134 ticks once they arrive; the walk from the core comes on top.
            int walked = 0;
            while (walked < 1000 && Fields(sim)["Buildings[" + barracks + "].Complete"] != "1") { Steps(gateway, sim, 10); walked += 10; }
            f = Fields(sim);
            Assert.That(f["Buildings[" + barracks + "].Complete"], Is.EqualTo("1"), "three builders finish within 1000 ticks");
            Assert.That(walked, Is.GreaterThanOrEqualTo(130), "not faster than the work allows");

            // An infantry, then cancelled: its cost comes back.
            gateway.SubmitEconomy(EconomyCommand.Train(1, ++seq, barracks, UnitKind.Infantry));
            Steps(gateway, sim, 1);
            Assert.That(Number(Fields(sim), "Buildings[" + barracks + "].Queued"), Is.EqualTo(1));
            long foodBefore = Number(Fields(sim), "Economy[1].Food");
            gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, ++seq, barracks));
            Steps(gateway, sim, 1);
            f = Fields(sim);
            Assert.That(Number(f, "Buildings[" + barracks + "].Queued"), Is.EqualTo(0));
            Assert.That(Number(f, "Economy[1].Food"), Is.EqualTo(foodBefore + scenario.Economy.InfantryFoodCost));

            // Everything the player did is in the log, and the log alone rebuilds every tick.
            Steps(gateway, sim, 600);
            var inputs = gateway.Inputs;
            Assert.That(inputs.Count(i => i.Kind == InputKind.Economy), Is.EqualTo(6));
            foreach (var input in inputs.Where(i => i.Kind == InputKind.Economy))
            {
                var copy = InputBinary.Decode(InputBinary.Encode(input));
                Assert.That(InputBinary.Encode(copy), Is.EqualTo(InputBinary.Encode(input)));
                Assert.That(copy.Economy.Kind, Is.EqualTo(input.Economy.Kind));
            }
            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                ReplayRunner.Record(stream, scenario, inputs, sim.Capture(1).Tick, build);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, build);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        [Test]
        public void IllegalOperationsChangeNothing()
        {
            var scenario = MapGenerator.Generate(2, true);
            var sim = new Battle(scenario);
            var gateway = new CommandGateway(sim);
            Steps(gateway, sim, 1);
            var before = Fields(sim);
            ulong seq = 0;
            int blocked = scenario.Map.BlockedCellIds[0];
            gateway.SubmitEconomy(EconomyCommand.Place(1, ++seq, BuildingKind.Barracks, blocked));        // on an obstacle
            gateway.SubmitEconomy(EconomyCommand.Place(1, ++seq, BuildingKind.Barracks, -5));             // off the map
            gateway.SubmitEconomy(EconomyCommand.Train(1, ++seq, 0, UnitKind.Infantry));                  // the core trains villagers only
            gateway.SubmitEconomy(EconomyCommand.Train(1, ++seq, 99, UnitKind.Infantry));                 // no such building
            gateway.SubmitEconomy(EconomyCommand.CancelTrain(1, ++seq, 0));                               // nothing queued
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 4, 5, 6 }, EconomyTargetKind.ResourceNode, 1)); // east villagers
            gateway.SubmitEconomy(EconomyCommand.Assign(1, ++seq, new uint[] { 1 }, EconomyTargetKind.ResourceNode, 999));      // no such node
            // Stop both automatic economies on the same tick so they do not act either.
            gateway.SubmitEconomy(EconomyCommand.Auto(1, ++seq, false));
            gateway.SubmitEconomy(EconomyCommand.Auto(2, ++seq, false));
            Steps(gateway, sim, 1);
            var after = Fields(sim);
            foreach (string key in new[] { "Economy[1].Food", "Economy[1].Wood", "Economy[1].Queued", "Buildings.Count" })
                Assert.That(after[key], Is.EqualTo(before[key]), key);
            for (int v = 4; v <= 6; v++) Assert.That(after["Villagers[" + v + "].NodeId"], Is.EqualTo(before["Villagers[" + v + "].NodeId"]), "east villager " + v);
        }

        [Test]
        public void EveryVerOneInputKeepsItsBytesAndAnEconomyInputRoundTrips()
        {
            var policy = new ScheduledInput(3, InputKind.Proposal, 1, 2, 7, 9, Array.Empty<PolicyOrder>());
            Assert.That(policy.Economy, Is.Null);
            var bytes = InputBinary.Encode(policy);
            Assert.That(InputBinary.Encode(InputBinary.Decode(bytes)), Is.EqualTo(bytes));
            var economy = new ScheduledInput(4, 1, 2, EconomyCommand.Assign(2, 5, new uint[] { 7, 8 }, EconomyTargetKind.ResourceNode, 3));
            var copy = InputBinary.Decode(InputBinary.Encode(economy));
            Assert.That(copy.Kind, Is.EqualTo(InputKind.Economy));
            Assert.That(copy.Economy.FactionId, Is.EqualTo(2u));
            Assert.That(copy.Economy.VillagerIds, Is.EqualTo(new uint[] { 7, 8 }));
            Assert.That(copy.Economy.TargetId, Is.EqualTo(3u));
            // An economy input must carry its operation.
            Assert.Throws<ArgumentException>(() => new ScheduledInput(5, InputKind.Economy, 1, 2, 0, 0, Array.Empty<PolicyOrder>(), long.MaxValue, ReasonCode.None));
        }
    }
}
