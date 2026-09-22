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
    /// technical-design-v3 20 (V3-3 PR2): the same map with only the west policy changed. The count is taken long after
    /// the start - many training times and AI cycles - so each policy has had time to show.
    /// </summary>
    public sealed class EconomyPolicyTests
    {
        private static Dictionary<string, string> Fields(Battle sim)
            => DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);

        private static long Number(Dictionary<string, string> f, string name) => long.Parse(f[name], CultureInfo.InvariantCulture);

        private sealed class Count { public int Villagers, Trained; public long Tick; public List<ScheduledInput> Inputs; public ScenarioDefinition S; }

        private static Count Run(ulong seed, EconomyPolicy? policy, int ticks)
        {
            var s = MapGenerator.Generate(seed, true, true);
            var sim = new Battle(s);
            var gateway = new CommandGateway(sim);
            if (policy.HasValue) gateway.SubmitEconomy(EconomyCommand.SetPolicy(1, 1, policy.Value));
            for (int i = 0; i < ticks && !sim.Capture(1).Result.HasEnded; i++) gateway.Step();
            var f = Fields(sim);
            var c = new Count { Tick = Number(f, "Tick"), Inputs = gateway.Inputs.ToList(), S = s };
            for (int i = 1; i <= Number(f, "Villagers.Count"); i++)
                if (f["Villagers[" + i + "].FactionId"] == "1" && f["Villagers[" + i + "].Alive"] == "1") c.Villagers++;
            for (long id = s.Soldiers.Length + 1; id < Number(f, "NextSoldierId"); id++)
                if (f["Soldiers[" + id + "].FactionId"] == "1") c.Trained++;
            return c;
        }

        [TestCase(1UL)]
        [TestCase(2UL)]
        public void ThePolicyChangesHowManyVillagersAndSoldiersTheWestHas(ulong seed)
        {
            const int Ticks = 9000;
            var balanced = Run(seed, null, Ticks);
            var military = Run(seed, EconomyPolicy.Military, Ticks);
            var growth = Run(seed, EconomyPolicy.Growth, Ticks);
            TestContext.WriteLine("seed " + seed + " at tick " + balanced.Tick + "/" + military.Tick + "/" + growth.Tick
                + ": villagers balanced " + balanced.Villagers + ", military " + military.Villagers + ", growth " + growth.Villagers
                + "; infantry trained balanced " + balanced.Trained + ", military " + military.Trained + ", growth " + growth.Trained);
            Assert.That(growth.Villagers, Is.GreaterThan(balanced.Villagers), "growth trains more villagers");
            Assert.That(military.Villagers, Is.LessThan(balanced.Villagers), "military trains fewer villagers");
            Assert.That(military.Trained, Is.GreaterThanOrEqualTo(growth.Trained), "military trains at least as many infantry as growth");
        }

        [Test]
        public void APolicyChangeIsLoggedAndReplays()
        {
            var run = Run(3, EconomyPolicy.Growth, 3000);
            var input = run.Inputs.Single(i => i.Kind == InputKind.Economy);
            var copy = InputBinary.Decode(InputBinary.Encode(input));
            Assert.That(copy.Economy.Policy, Is.EqualTo(EconomyPolicy.Growth));
            using (var stream = new MemoryStream())
            {
                var build = new BuildIdentity();
                ReplayRunner.Record(stream, run.S, run.Inputs, run.Tick, build);
                stream.Position = 0;
                var outcome = ReplayRunner.Replay(stream, build);
                Assert.That(outcome.FirstMismatchTick, Is.Null);
                Assert.That(outcome.IsFault, Is.False);
            }
        }

        [Test]
        public void AVerThreeOneMapIgnoresThePolicy()
        {
            var plain = new Battle(MapGenerator.Generate(2, true));
            var withPolicy = new Battle(MapGenerator.Generate(2, true));
            var gateway = new CommandGateway(withPolicy);
            gateway.SubmitEconomy(EconomyCommand.SetPolicy(1, 1, EconomyPolicy.Growth));
            for (long t = 1; t <= 3000; t++) { plain.Step(t, Array.Empty<ScheduledInput>()); gateway.Step(); }
            var a = Fields(plain);
            var b = Fields(withPolicy);
            Assert.That(b.Keys.Any(k => k.StartsWith("Economy[", StringComparison.Ordinal) && k.EndsWith("].Policy", StringComparison.Ordinal)), Is.False);
            foreach (var key in a.Keys.Where(k => k.StartsWith("Villagers[", StringComparison.Ordinal) || k.StartsWith("Economy[", StringComparison.Ordinal)))
                Assert.That(b[key], Is.EqualTo(a[key]), key);
        }
    }
}
