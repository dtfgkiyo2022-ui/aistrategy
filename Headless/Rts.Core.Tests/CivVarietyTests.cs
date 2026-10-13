using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Rts.Application;
using Rts.Contracts;
using Rts.Decision;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    public sealed class CivVarietyTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo ChooseCiv = typeof(Battle).GetMethod("ChooseCiv", Private);

        [Test]
        public void GroundThatFitsNothingKeepsTheHistoricalAnswer()
        {
            var candidates = new[]
            {
                new EconomyDecision.CivScore(CivKind.Agrarian, 0, 0, 6, true),
                new EconomyDecision.CivScore(CivKind.Metallurgy, 0, 1, 8, true),
                new EconomyDecision.CivScore(CivKind.Cavalry, 0, 5, 5, false)
            };
            Assert.That(EconomyDecision.ChooseCivVariety(candidates, 7, 2, 200),
                Is.EqualTo(EconomyDecision.ChooseCiv(candidates)));
            Assert.That(EconomyDecision.ChooseCivVariety(candidates, 7, 2, 200), Is.EqualTo(CivKind.Agrarian));
        }

        [Test]
        public void AReservedCivilisationBeatsTheVarietyChoice()
        {
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var scenario = ReserveScenario(seed);
                CivKind ground = (CivKind)ChooseCiv.Invoke(new Battle(scenario), new object[] { (uint)1 });
                if (ground == CivKind.Sanctuary) continue;
                var sim = new Battle(scenario);
                var gateway = new CommandGateway(sim);
                gateway.SubmitEconomy(EconomyCommand.Advance(1, 1, CivKind.Sanctuary));
                for (int i = 0; i < 20000 && sim.Capture(1).Economy.Civ == CivKind.Primitive; i++) gateway.Step();
                Assert.That(sim.Capture(1).Economy.Civ, Is.EqualTo(CivKind.Sanctuary),
                    "seed " + seed + ": the reserved civilisation, not the variety choice " + ground);
                return;
            }
            Assert.Fail("seed 1..30 all choose the sanctuary");
        }

        private static ScenarioDefinition ReserveScenario(ulong seed)
        {
            var s = MapGenerator.GenerateTerrain(seed);
            s.Economy.Sanctuary = true;
            s.Economy.CivVariety = true;
            s.Economy.AutoVillagerTarget = 5;
            s.Economy.AutoInfantryQueue = 0;
            s.Cores[0].Hp = s.Cores[1].Hp = 1000000;
            for (int i = 0; i < s.UnitParameters.Length; i++) s.UnitParameters[i].Damage = 0;
            return s;
        }

        [Test]
        public void NormalisedCandidatesAreDeterministicAndSeeded()
        {
            var candidates = new[]
            {
                new EconomyDecision.CivScore(CivKind.Agrarian, 8, 0, 8, true),
                new EconomyDecision.CivScore(CivKind.Metallurgy, 5, 1, 5, true),
                new EconomyDecision.CivScore(CivKind.Forestry, 3, 2, 5, true)
            };

            CivKind first = EconomyDecision.ChooseCivVariety(candidates, 1, 1, 250);
            Assert.That(first, Is.EqualTo(EconomyDecision.ChooseCivVariety(candidates, 1, 1, 250)));
            Assert.That(first, Is.Not.EqualTo(CivKind.Forestry), "a score at 600‰ is outside a 250‰ window from 1000‰");

            ulong otherSeed = 0;
            for (ulong seed = 2; seed < 100; seed++)
                if (EconomyDecision.ChooseCivVariety(candidates, seed, 1, 250) != first)
                {
                    otherSeed = seed;
                    break;
                }
            Assert.That(otherSeed, Is.Not.EqualTo(0), "the seeded candidate index did not vary in the probe range");
        }

        [Test]
        public void ZeroDisabledAndOutsideWindowCandidatesAreNotSelected()
        {
            var candidates = new[]
            {
                new EconomyDecision.CivScore(CivKind.Agrarian, 8, 0, 8, true),
                new EconomyDecision.CivScore(CivKind.Metallurgy, 0, 1, 5, true),
                new EconomyDecision.CivScore(CivKind.Forestry, 8, 2, 8, false),
                new EconomyDecision.CivScore(CivKind.Masonry, 3, 3, 5, true)
            };
            Assert.That(EconomyDecision.ChooseCivVariety(candidates, 10, 1, 250), Is.EqualTo(CivKind.Agrarian));
        }

        [Test]
        public void OneEligibleCandidateIsSelectedWithoutASeedChoice()
        {
            var candidates = new[]
            {
                new EconomyDecision.CivScore(CivKind.Agrarian, 0, 0, 8, true),
                new EconomyDecision.CivScore(CivKind.Masonry, 1, 3, 5, true),
                new EconomyDecision.CivScore(CivKind.Cavalry, 0, 5, 5, false)
            };
            Assert.That(EconomyDecision.ChooseCivVariety(candidates, 1, 1, 250), Is.EqualTo(CivKind.Masonry));
        }

        [Test]
        public void SameSeedAndFactionChooseTheSameCivilisationInTheSimulation()
        {
            for (ulong seed = 1; seed <= 8; seed++)
            {
                var leftScenario = LiveGameRules.Create(seed, false, true, false, false);
                var rightScenario = LiveGameRules.Create(seed, false, true, false, false);
                Assert.That(leftScenario.Economy.CivVariety, Is.True);
                var left = new Battle(leftScenario);
                var right = new Battle(rightScenario);
                for (long tick = 1; tick <= 4800; tick++)
                {
                    left.Step(tick, Array.Empty<ScheduledInput>());
                    right.Step(tick, Array.Empty<ScheduledInput>());
                }
                Assert.That(left.Capture(1).Economy.Civ, Is.EqualTo(right.Capture(1).Economy.Civ), "seed " + seed + " west");
                Assert.That(left.Capture(2).Economy.Civ, Is.EqualTo(right.Capture(2).Economy.Civ), "seed " + seed + " east");
            }
        }

        [Test]
        public void CivVarietyMatchHasTheSameHashAtEveryTickOnReplayTwin()
        {
            var left = new Battle(LiveGameRules.Create(3, false, true, false, false));
            var right = new Battle(LiveGameRules.Create(3, false, true, false, false));
            for (long tick = 1; tick <= 6000; tick++)
            {
                left.Step(tick, Array.Empty<ScheduledInput>());
                right.Step(tick, Array.Empty<ScheduledInput>());
                Assert.That(ReplayBinary.Hash(left.CaptureDiagnostic().CanonicalState),
                    Is.EqualTo(ReplayBinary.Hash(right.CaptureDiagnostic().CanonicalState)), "tick " + tick);
            }
        }
    }

    /// <summary>Measurements for technical-design-v3 32.35. They write tables to civ_variety/ and never run by default.</summary>
    [Explicit("measurement"), Category("Measurement")]
    public sealed class CivVarietyMeasurementTests
    {
        private static readonly FieldInfo Registrations = typeof(Battle).GetField("CivRegistrations", BindingFlags.Static | BindingFlags.NonPublic);

        [Test]
        public void MeasureCivVarietyAcrossSeedsOneToForty()
        {
            var rows = new List<MeasurementRow>();
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var offScenario = LiveGameRules.Create(seed, false, true, false, false);
                offScenario.Economy.CivVariety = false;
                    var off = new Battle(offScenario);
                    RunToChoice(off);
                    for (uint faction = 1; faction <= 2; faction++)
                    {
                        var raw = ReadScores(off, offScenario, faction);
                    StepChoice(off, faction);
                    CivKind oldChoice = off.Capture(faction).Economy.Civ;
                    var onScenario = LiveGameRules.Create(seed, false, true, false, false);
                    var on = new Battle(onScenario);
                    RunToChoice(on);
                    StepChoice(on, faction);
                    CivKind newChoice = on.Capture(faction).Economy.Civ;
                    rows.Add(new MeasurementRow(seed, faction, oldChoice, newChoice, raw));
                }
            }

            string directory = MeasurementDirectory();
            Directory.CreateDirectory(directory);
            WriteRawCsv(directory, rows);
            WriteSummary(directory, rows);
            TestContext.WriteLine("CivVariety measurement: " + directory);
        }

        [Test]
        public void MeasureSettlementImpactAcrossSeedsOneToEight()
        {
            string directory = MeasurementDirectory();
            Directory.CreateDirectory(directory);
            var lines = new List<string> { "seed,flag,tick,ended,winner,westCiv,eastCiv" };
            for (ulong seed = 1; seed <= 8; seed++)
            {
                lines.Add(SettlementLine(seed, false));
                lines.Add(SettlementLine(seed, true));
            }
            File.WriteAllLines(Path.Combine(directory, "settlements.csv"), lines, new UTF8Encoding(false));
            TestContext.WriteLine("CivVariety settlement measurement: " + directory);
        }

        [Test]
        public void CompareCivVarietyWindowsFromRecordedScores()
        {
            string path = Path.Combine(MeasurementDirectory(), "choices.csv");
            Assert.That(File.Exists(path), Is.True, "choices.csv must be measured first");
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            var civs = new[] { CivKind.Agrarian, CivKind.Metallurgy, CivKind.Forestry, CivKind.Masonry, CivKind.Caravan,
                CivKind.Cavalry, CivKind.Bridge, CivKind.Academy, CivKind.Cult, CivKind.Fishing, CivKind.Mountain,
                CivKind.Tollgate, CivKind.Metropolis, CivKind.Sanctuary };
            var caps = new[] { 6, 8, 7, 6, 2, 5, 3, 3, 3, 3, 4, 3, 3, 3 };
            var output = new List<string> { "windowPermille," + string.Join(",", civs) };
            foreach (int window in new[] { 150, 200, 250, 300 })
            {
                var counts = new int[civs.Length];
                for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
                {
                    string[] fields = lines[lineIndex].Split(',');
                    var scores = new EconomyDecision.CivScore[civs.Length];
                    for (int i = 0; i < civs.Length; i++)
                        scores[i] = new EconomyDecision.CivScore(civs[i], int.Parse(fields[4 + i]), i, caps[i], bool.Parse(fields[18 + i]));
                    CivKind selected = EconomyDecision.ChooseCivVariety(scores, ulong.Parse(fields[0]), uint.Parse(fields[1]), window);
                    int index = Array.IndexOf(civs, selected);
                    if (index >= 0) counts[index]++;
                }
                output.Add(window + "," + string.Join(",", counts));
            }
            File.WriteAllLines(Path.Combine(MeasurementDirectory(), "window-comparison.csv"), output, new UTF8Encoding(false));
            TestContext.WriteLine(string.Join(Environment.NewLine, output));
        }

        private static string SettlementLine(ulong seed, bool variety)
        {
            var scenario = LiveGameRules.Create(seed, false, true, false, false);
            scenario.Economy.CivVariety = variety;
            var sim = new Battle(scenario);
            long tick = 0;
            for (tick = 1; tick <= 30000 && !sim.Capture(1).Result.HasEnded; tick++)
                sim.Step(tick, Array.Empty<ScheduledInput>());
            tick = sim.Capture(1).Tick;
            var result = sim.Capture(1).Result;
            return string.Join(",", seed, variety ? "on" : "off", tick, result.HasEnded, result.WinnerFactionId,
                sim.Capture(1).Economy.Civ, sim.Capture(2).Economy.Civ);
        }

        private static void RunToChoice(Battle sim)
        {
            for (long tick = 1; tick < 4800; tick++)
                sim.Step(tick, Array.Empty<ScheduledInput>());
        }

        private static void StepChoice(Battle sim, uint faction)
        {
            long tick = sim.Capture(faction).Tick + 1;
            sim.Step(tick, Array.Empty<ScheduledInput>());
        }

        private static RawScore[] ReadScores(Battle sim, ScenarioDefinition scenario, uint faction)
        {
            SimPoint core = scenario.Cores[faction - 1].Position;
            int ore = 0, food = 0;
            foreach (var node in scenario.ResourceNodes)
            {
                if (node.Kind == ResourceKind.Ore && InRange(node.Position, core, 44)) ore++;
                else if (node.Kind == ResourceKind.Food && InRange(node.Position, core, 30)) food++;
            }
            var rows = (Array)Registrations.GetValue(null);
            var values = new RawScore[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                object row = rows.GetValue(i);
                Type type = row.GetType();
                CivKind civ = (CivKind)type.GetProperty("Civ").GetValue(row);
                int cap = (int)type.GetProperty("ScoreCap").GetValue(row);
                var enabled = (Delegate)type.GetProperty("Enabled").GetValue(row);
                var score = (Delegate)type.GetProperty("Score").GetValue(row);
                bool isEnabled = (bool)enabled.DynamicInvoke(sim, faction);
                int value = isEnabled ? (int)score.DynamicInvoke(sim, faction, core, ore, food) : 0;
                values[i] = new RawScore(civ, value, cap, isEnabled);
            }
            return values;
        }

        private static bool InRange(SimPoint a, SimPoint b, int radius)
        {
            long dx = a.X.Raw - b.X.Raw, dz = a.Z.Raw - b.Z.Raw, r = Fix64.FromInt(radius).Raw;
            return dx * dx + dz * dz <= r * r;
        }

        private static string MeasurementDirectory()
        {
            string path = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(path))
            {
                if (Directory.Exists(Path.Combine(path, "UnityProject"))) return Path.Combine(path, "civ_variety");
                string parent = Directory.GetParent(path)?.FullName;
                if (parent == path) break;
                path = parent;
            }
            return Path.Combine(Environment.CurrentDirectory, "civ_variety");
        }

        private static void WriteRawCsv(string directory, List<MeasurementRow> rows)
        {
            var header = new List<string> { "seed", "faction", "selectedOff", "selectedOn" };
            foreach (var score in rows[0].Scores) header.Add(score.Civ + "_raw");
            foreach (var score in rows[0].Scores) header.Add(score.Civ + "_enabled");
            var lines = new List<string> { string.Join(",", header) };
            foreach (var row in rows)
            {
                var values = new List<string> { row.Seed.ToString(), row.Faction.ToString(), row.Off.ToString(), row.On.ToString() };
                values.AddRange(row.Scores.Select(x => x.Value.ToString()));
                values.AddRange(row.Scores.Select(x => x.Enabled ? "true" : "false"));
                lines.Add(string.Join(",", values));
            }
            File.WriteAllLines(Path.Combine(directory, "choices.csv"), lines, new UTF8Encoding(false));
        }

        private static void WriteSummary(string directory, List<MeasurementRow> rows)
        {
            var civs = rows[0].Scores.Select(x => x.Civ).ToArray();
            var text = new StringBuilder();
            text.AppendLine("# CivVariety 測定");
            text.AppendLine();
            text.AppendLine("80 回（種 1〜40、陣営 1〜2）。旗オフと旗オンで同じ地図を使い、tick 4800 の文明選択直前の生スコアを記録した。旗オンの窓は "
                + LiveGameRules.Create(1, false, true, false, false).Economy.CivVarietyWindowPermille + "‰。満点は登録表の ScoreCap で、整数の千分率にして 1000‰ で頭打ちにした。");
            text.AppendLine();
            text.AppendLine("## 生スコア");
            text.AppendLine();
            text.AppendLine("|文明|満点|最小|最大|中央値|旗オフ選択|旗オン選択|");
            text.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (var civ in civs)
            {
                var values = rows.SelectMany(x => x.Scores.Where(s => s.Civ == civ).Select(s => s.Value)).OrderBy(x => x).ToArray();
                int cap = rows.SelectMany(x => x.Scores).First(x => x.Civ == civ).Cap;
                text.AppendLine("|" + civ + "|" + cap + "|" + values.First() + "|" + values.Last() + "|" + Median(values) + "|"
                    + rows.Count(x => x.Off == civ) + "|" + rows.Count(x => x.On == civ) + "|");
            }
            text.AppendLine();
            text.AppendLine("## 選ばれなかった理由");
            text.AppendLine();
            text.AppendLine("|文明|理由|");
            text.AppendLine("|---|---|");
            foreach (var civ in civs) text.AppendLine("|" + civ + "|" + Reason(civ) + "|");
            File.WriteAllText(Path.Combine(directory, "summary.md"), text.ToString(), new UTF8Encoding(false));
        }

        private static decimal Median(int[] values)
            => values.Length % 2 == 1 ? values[values.Length / 2] : (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2m;

        private static string Reason(CivKind civ)
        {
            switch (civ)
            {
                case CivKind.Agrarian: return "有効。保証食料 3 点を除いた近距離の食料余剰で判定する。旗オフでは生の個数が他文明の上限を上回りやすい。";
                case CivKind.Metallurgy: return "有効。近距離の鉱石個数で判定する。旗オフでは個数の物差しが高い文明に負ける。";
                case CivKind.Forestry: return "Forestry 旗が必要で、合法な伐採所を置ける木だけが点になる。0 の地図では候補外。";
                case CivKind.Masonry: return "Masonry 旗が必要で、合法な採石場を置ける石だけが点になる。0 の地図では候補外。";
                case CivKind.Caravan: return "Caravan 旗が必要で、維持可能な前哨地と合法な隊商宿の組だけが点になる。";
                case CivKind.Cavalry: return "Cavalry 旗が必要。コアから目的地へ通れる地形の機動性が 0〜5 の段階点になる。";
                case CivKind.Bridge: return "Bridge 旗が必要。合法な橋で短縮できる川越えがない地図では 0。";
                case CivKind.Academy: return "Academy 旗と GoldEnabled が必要。探索済みで安全に運べる金がない地図では 0。";
                case CivKind.Cult: return "Cult 旗と MonksEnabled が必要。短い敵到達経路または有効な観測がないと 0。";
                case CivKind.Fishing: return "FishingCiv 旗と FishingEnabled が必要。探索済みで合法な港を置ける魚がないと 0。";
                case CivKind.Mountain: return "Mountain 旗が必要。合法な坑道の山肌がない地図では 0。";
                case CivKind.Tollgate: return "Tollgate 旗が必要。通路を実際に延ばす合法な狭所がないと 0。";
                case CivKind.Metropolis: return "Metropolis 旗が必要。コア周囲に探索済みの建築可能な平地が少ないと 0。";
                case CivKind.Sanctuary: return "Sanctuary 旗が必要。コアから近い前哨地がないと 0。";
                default: return "文明登録の対象外。";
            }
        }

        private sealed class MeasurementRow
        {
            internal readonly ulong Seed;
            internal readonly uint Faction;
            internal readonly CivKind Off, On;
            internal readonly RawScore[] Scores;
            internal MeasurementRow(ulong seed, uint faction, CivKind off, CivKind on, RawScore[] scores)
            { Seed = seed; Faction = faction; Off = off; On = on; Scores = scores; }
        }

        private readonly struct RawScore
        {
            internal readonly CivKind Civ;
            internal readonly int Value, Cap;
            internal readonly bool Enabled;
            internal RawScore(CivKind civ, int value, int cap, bool enabled)
            { Civ = civ; Value = value; Cap = cap; Enabled = enabled; }
        }
    }
}
