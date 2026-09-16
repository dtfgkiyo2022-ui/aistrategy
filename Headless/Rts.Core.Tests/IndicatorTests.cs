using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Headless.Cli;
using Rts.Replay;
using Rts.Simulation;

namespace Rts.Tests.Headless
{
    public sealed class IndicatorTests
    {
        private static Dictionary<string, string> Initial() =>
            DiagnosticComparison.Fields(new Rts.Simulation.Simulation(WeekOneScenario.Create()).CaptureDiagnostic())
                .ToDictionary(p => p.Key, p => p.Value);
        private static void Offense(Dictionary<string, string> d, int id, int phase, int goal = 1)
        {
            const string p = "Ai.Factions[1].Offense.";
            d[p + "Id"] = id.ToString(); d[p + "Phase"] = phase.ToString();
            d[p + "Goal.Kind"] = phase == 0 ? "0" : "2"; d[p + "Goal.Id"] = phase == 0 ? "0" : goal.ToString();
        }
        private static void Observe(IndicatorCounter counter, int tick, Dictionary<string, string> d) =>
            counter.Observe(tick, new Dictionary<string, string>(d));

        [Test]
        public void CountsReleaseReplacementWaitingRetryAndRepeatedAdvances()
        {
            var c = new IndicatorCounter(); var d = Initial();
            Offense(d, 1, 1); Observe(c, 0, d);
            Offense(d, 1, 2); Observe(c, 1, d);
            Offense(d, 1, 3); d["Ai.Factions[1].Offense.Advancing.Count"] = "1"; d["Ai.Factions[1].Offense.Advancing[0]"] = "1";
            int alive = d.Keys.Count(k => k.StartsWith("Soldiers[") && k.EndsWith("].ArmyId") && d[k] == "1" && d[k.Replace(".ArmyId", ".Alive")] == "1");
            Observe(c, 2, d);
            Offense(d, 1, 1); Observe(c, 3, d); // Re-gather, same offense.
            Offense(d, 1, 3); Observe(c, 4, d);
            Offense(d, 2, 1, 2); Observe(c, 5, d); // Replaces an advancing offense, not a gathering failure.
            Offense(d, 3, 1, 1); Observe(c, 6, d); // Releases gathering and retries target 1.
            Offense(d, 3, 2, 1); Observe(c, 7, d);
            Offense(d, 3, 0); Observe(c, 8, d);
            Offense(d, 4, 1, 2); Observe(c, 9, d); // Retry a non-immediately-previous released goal.
            var m = c.Report.Factions[1];
            Assert.That(m.GatheringFailures, Is.EqualTo(2));
            Assert.That(m.Retries, Is.EqualTo(2));
            Assert.That(m.Advances.Select(a => a.Tick), Is.EqualTo(new long[] { 2, 4 }));
            Assert.That(m.AdvancedTotal, Is.EqualTo(alive * 2));
            Assert.That(m.AdvancedAverage, Is.EqualTo(alive));
        }

        [Test]
        public void CountsDurationsOwnershipAndFirstHitWithoutCountingS0()
        {
            var c = new IndicatorCounter(); var d = Initial();
            d["Ai.Armies[1].Assignment"] = "2"; d["Outposts[1].OwnerFactionId"] = "0";
            Observe(c, 0, d); Observe(c, 1, d);
            d["Ai.Armies[1].Assignment"] = "1"; d["Outposts[1].OwnerFactionId"] = "1"; d["Cores[1].Hp"] = "2990";
            Observe(c, 2, d);
            d["Ai.Armies[1].Assignment"] = "3"; d["Outposts[1].OwnerFactionId"] = "2";
            d["Cores[1].Hp"] = "0"; d["Result.HasEnded"] = "1"; d["Result.WinnerFactionId"] = "2";
            Observe(c, 3, d);
            var a = c.Report.Armies[1];
            Assert.That(new[] { a.Guard, a.Reserve, a.CoreDefense, a.Total }, Is.EqualTo(new long[] { 1, 1, 1, 3 }));
            Assert.That(c.Report.Factions[1].DefenseTicks, Is.EqualTo(c.Report.Armies.Values.Where(v => v.Faction == 1).Sum(v => v.Total)));
            Assert.That(c.Report.Outposts[1].OwnershipTicks.Values, Is.EqualTo(new long[] { 1, 1, 1 }));
            Assert.That(c.Report.FirstCoreHitTick, Is.EqualTo(2));
            Assert.That(c.Report.EndTick, Is.EqualTo(3));
            Assert.That(c.Report.Result, Is.EqualTo("CoreDestroyed:1"));
            Assert.That(c.Report.Factions[1].CoreDefenseEntriesAfterFirstHit, Is.EqualTo(1));
            Assert.Throws<InvalidDataException>(() => c.Observe(5, d));
        }

        [Test]
        public void DistinguishesUndecidedDrawAndFault()
        {
            var c = new IndicatorCounter(); var d = Initial();
            Observe(c, 0, d);
            Assert.That(c.Report.Result, Is.EqualTo("Undecided"));
            Assert.That(c.Report.EndTick, Is.Null);
            Assert.That(c.Report.FirstCoreHitTick, Is.Null);
            d["Result.HasEnded"] = "1"; d["Result.IsDraw"] = "1";
            Observe(c, 1, d);
            Assert.That(c.Report.Result, Is.EqualTo("Draw"));
            d["Result.IsFault"] = "1"; Observe(c, 2, d);
            Assert.That(c.Report.Result, Is.EqualTo("Fault"));
        }

        [Test]
        public void AnalyzeMatchesRecordStateAndEventHashesEveryTick()
        {
            var build = new BuildIdentity { SourceHash = new string('a', 64) };
            var expected = new List<byte[]>();
            using (var stream = new MemoryStream())
            {
                ReplayRunner.Record(stream, WeekOneScenario.Create(), Array.Empty<ScheduledInput>(), 230, build,
                    (s, h, e) => expected.Add(h.Concat(e).ToArray()));
                stream.Position = 0;
                int count = 0;
                var report = AnalyzeCommand.Analyze(stream, build, false, (s, h, e) =>
                {
                    Assert.That(s.Tick, Is.EqualTo(count));
                    Assert.That(h.Concat(e), Is.EqualTo(expected[count++]));
                    Assert.That(ReplayBinary.Hash(s.CanonicalState), Is.EqualTo(h));
                });
                Assert.That(count, Is.EqualTo(231));
                Assert.That(report.LastTick, Is.EqualTo(230));
                Assert.That(report.FirstMismatchTick, Is.Null);
                Assert.That(report.IsFault, Is.False);
            }
        }
    }
}
