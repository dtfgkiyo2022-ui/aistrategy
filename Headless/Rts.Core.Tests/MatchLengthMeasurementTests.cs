using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    [TestFixture]
    public sealed class MatchLengthMeasurementTests
    {
        private const int MaxTicks = 60000;
        private const int SnapshotInterval = 1000;
        private const int RetreatThreshold = 600;
        private static readonly VillagerActivity[] Activities =
        {
            VillagerActivity.Idle, VillagerActivity.ToResource, VillagerActivity.Gathering,
            VillagerActivity.Returning, VillagerActivity.ToBuild, VillagerActivity.Building,
            VillagerActivity.Hauling, VillagerActivity.Trading
        };

        [Test, Explicit("measurement"), Category("Measurement")]
        public void MeasureMatchLengthAndTimeBreakdown()
        {
            var matches = new List<MatchMeasurement>();
            var report = new StringBuilder();
            report.AppendLine("# Match length measurement");
            report.AppendLine();
            report.AppendLine("Tick rate is read from `ScenarioDefinition.TickRateHz` for each generated scenario.");
            report.AppendLine();
            var seeds = ReadSeeds();
            bool goldEnabled = IsEnabled("MATCHLEN_GOLD");
            bool fishingEnabled = IsEnabled("MATCHLEN_FISHING");
            report.AppendLine($"条件: 漁={(fishingEnabled ? "オン" : "オフ")}、金={(goldEnabled ? "オン" : "オフ")}、種={SeedRangeText(seeds)}（{seeds.Count}試合）。");
            report.AppendLine();
            report.AppendLine("## 表1: 試合ごとの節目");
            report.AppendLine();
            report.AppendLine("| seed | 決着tick（分） | 勝者 | 時代到達勝利 | 陣営1文明 | 陣営1 文明入り(分) | 陣営1 2つ目の時代(分) | 陣営1 3つ目の時代(分) | 陣営2文明 | 陣営2 文明入り(分) | 陣営2 2つ目の時代(分) | 陣営2 3つ目の時代(分) | 最初の戦闘(分) | 陣営1コア初被弾(分) | 陣営2コア初被弾(分) |");
            report.AppendLine("|---:|---:|---:|:---:|:---|---:|---:|---:|:---|---:|---:|---:|---:|---:|---:|");

            foreach (ulong seed in seeds)
            {
                var match = Measure(seed);
                matches.Add(match);
                TestContext.WriteLine(match.SummaryLine());
            }

            var decisiveMinutes = matches.Where(m => m.HasEnded).Select(m => m.DecisionTick / (double)m.TickRate / 60.0).ToList();
            decisiveMinutes.Sort();
            report.Clear();
            report.AppendLine("# Match length measurement");
            report.AppendLine();
            report.AppendLine("Tick rate is read from `ScenarioDefinition.TickRateHz` for each generated scenario.");
            report.AppendLine();
            report.AppendLine($"条件: 漁={(fishingEnabled ? "オン" : "オフ")}、金={(goldEnabled ? "オン" : "オフ")}、種={SeedRangeText(seeds)}（{seeds.Count}試合）。");
            report.AppendLine();
            report.AppendLine("## 表1: 試合ごとの節目");
            report.AppendLine();
            report.AppendLine("| seed | 決着tick（分） | 勝者 | 勝者の文明 | 金の点数 | 漁場の点数 | 時代到達勝利 | 陣営1文明 | 陣営1 文明入り(分) | 陣営1 2つ目の時代(分) | 陣営1 3つ目の時代(分) | 陣営2文明 | 陣営2 文明入り(分) | 陣営2 2つ目の時代(分) | 陣営2 3つ目の時代(分) | 最初の戦闘(分) | 陣営1コア初被弾(分) | 陣営2コア初被弾(分) |");
            report.AppendLine("|---:|---:|---:|:---|---:|---:|:---:|:---|---:|---:|---:|:---|---:|---:|---:|---:|---:|---:|");
            foreach (var match in matches) report.AppendLine(match.SummaryRow());
            report.AppendLine("## 要約");
            report.AppendLine();
            report.AppendLine("| 決着時間（分）の一覧 | 中央値（分） | 15〜25分 | 未決着 | 6分未満 | 2つ目の時代：農耕 | 2つ目の時代：冶金 | 3つ目の時代：農耕 | 3つ目の時代：冶金 | 勝者文明:農耕 | 勝者文明:冶金 | 未決着 | 漁食料平均:農耕 | 漁食料平均:冶金 |");
            report.AppendLine("|:---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            report.AppendLine($"| {string.Join(", ", decisiveMinutes.Select(v => v.ToString("0.00", CultureInfo.InvariantCulture)))} | {Median(decisiveMinutes)} | {matches.Count(m => m.HasEnded && Minutes(m) >= 15.0 && Minutes(m) <= 25.0)} | {matches.Count(m => !m.HasEnded)} | {matches.Count(m => m.HasEnded && Minutes(m) < 6.0)} | {CountAge(matches, 2, CivKind.Agrarian)} | {CountAge(matches, 2, CivKind.Metallurgy)} | {CountAge(matches, 3, CivKind.Agrarian)} | {CountAge(matches, 3, CivKind.Metallurgy)} | {WinnerCivCount(matches, CivKind.Agrarian)} | {WinnerCivCount(matches, CivKind.Metallurgy)} | {matches.Count(m => !m.HasEnded)} | {AverageFishing(matches, CivKind.Agrarian)} | {AverageFishing(matches, CivKind.Metallurgy)} |");
            report.AppendLine();

            report.AppendLine();
            report.AppendLine("## 漁と金");
            report.AppendLine();
            report.AppendLine("| seed | 陣営 | 漁で得た食料 | 漁場で働いた村人tick | 集めた金 | コアの金最大 | 3つ目の時代に金を払ったか |");
            report.AppendLine("|---:|---:|---:|---:|---:|---:|:---|");
            foreach (var match in matches) for (int faction = 1; faction <= 2; faction++) report.AppendLine(match.Factions[faction - 1].EconomyRow(match.Seed, faction));
            if (goldEnabled)
            {
                report.AppendLine(); report.AppendLine("## 金あり地図の比較"); report.AppendLine();
                report.AppendLine("| seed | 金以外の資源点・コア・前哨 |"); report.AppendLine("|---:|:---|");
                foreach (var match in matches) report.AppendLine($"| {match.Seed} | {match.GoldMapComparison} |");
            }

            report.AppendLine();
            report.AppendLine("## 表2: 兵士の時間内訳");
            report.AppendLine();
            report.AppendLine("| seed | 陣営 | 撤退 | 攻撃 | 移動 | 待機 | 撤退600tick以上の兵士数 | 最長連続撤退tick |");
            report.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var match in matches)
                for (int faction = 1; faction <= 2; faction++) report.AppendLine(match.Soldiers[faction - 1].MarkdownRow(match.Seed, faction));

            report.AppendLine();
            report.AppendLine("## 表3: 村人の時間内訳");
            report.AppendLine();
            report.AppendLine("| seed | 陣営 | Idle | ToResource | Gathering | Returning | ToBuild | Building | Hauling | Trading |");
            report.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var match in matches)
                for (int faction = 1; faction <= 2; faction++) report.AppendLine(match.Villagers[faction - 1].MarkdownRow(match.Seed, faction));

            report.AppendLine();
            report.AppendLine("## 表4: 1000tickごとの時系列");
            report.AppendLine();
            report.AppendLine("| seed | tick | 分 | 陣営 | 生存兵士 | 村人 | 人口/上限 | 食料 | 木材 | 金属 | 石 | 時代 | 撤退人tick | 攻撃人tick | 移動人tick | 待機人tick |");
            report.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var match in matches)
                foreach (var snapshot in match.Snapshots) report.AppendLine(snapshot.MarkdownRow(match.Seed, match.TickRate));

            long totalTicks = 0;
            int decisive = 0;
            foreach (var match in matches) { totalTicks += match.DecisionTick; if (match.HasEnded) decisive++; }
            report.AppendLine();
            report.AppendLine("## 合計");
            report.AppendLine();
            report.AppendLine($"{matches.Count}試合、決着 {decisive}/{matches.Count}、決着tick合計 {totalTicks.ToString(CultureInfo.InvariantCulture)}、決着tick平均 {(totalTicks / (double)matches.Count).ToString("0.0", CultureInfo.InvariantCulture)}。");

            var reportPath = Environment.GetEnvironmentVariable("MATCHLEN_REPORT") ?? @"D:\rts-verify\matchlen\report.md";
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
            TestContext.WriteLine($"{matches.Count}試合完了: decisive {decisive}/{matches.Count}, decision ticks total {totalTicks.ToString(CultureInfo.InvariantCulture)}, report {reportPath}");
            Assert.That(matches.Count, Is.EqualTo(seeds.Count));
        }

        private static MatchMeasurement Measure(ulong seed)
        {
            bool gold = IsEnabled("MATCHLEN_GOLD");
            var scenario = MapGenerator.GenerateTerrain(seed, gold: gold);
            ApplyExperimentalOverrides(scenario);
            int tickRate = scenario.TickRateHz;
            var sim = new Battle(scenario);
            var match = new MatchMeasurement(seed, tickRate, scenario, gold);
            var previous = new FactionFrame[2];
            previous[0] = sim.Capture(1); previous[1] = sim.Capture(2);
            match.Initialize(previous);
            match.ObserveDiagnostic(sim.CaptureDiagnostic());

            for (long tick = 1; tick <= MaxTicks && !previous[0].Result.HasEnded; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                var current = new[] { sim.Capture(1), sim.Capture(2) };
                match.Observe(tick, current);
                match.ObserveDiagnostic(sim.CaptureDiagnostic());
                previous = current;
            }

            match.Finish(previous[0].Result);
            _ = sim.CaptureDiagnostic();
            return match;
        }

        private static void ApplyExperimentalOverrides(ScenarioDefinition scenario)
        {
            if (IsEnabled("MATCHLEN_FISHING")) scenario.Economy.FishingEnabled = true;
            var villagerTarget = Environment.GetEnvironmentVariable("MATCHLEN_VILLAGER_TARGET");
            if (!string.IsNullOrEmpty(villagerTarget)) scenario.Economy.AutoVillagerTarget = int.Parse(villagerTarget, CultureInfo.InvariantCulture);

            var agePrice = Environment.GetEnvironmentVariable("MATCHLEN_AGE_PRICE_PERMILLE");
            if (!string.IsNullOrEmpty(agePrice))
            {
                int permille = int.Parse(agePrice, CultureInfo.InvariantCulture);
                scenario.Economy.AdvanceFoodCost = ScalePrice(scenario.Economy.AdvanceFoodCost, permille);
                scenario.Economy.AdvanceWoodCost = ScalePrice(scenario.Economy.AdvanceWoodCost, permille);
                scenario.Economy.Age2FoodCost = ScalePrice(scenario.Economy.Age2FoodCost, permille);
                scenario.Economy.Age2WoodCost = ScalePrice(scenario.Economy.Age2WoodCost, permille);
                scenario.Economy.Age3FoodCost = ScalePrice(scenario.Economy.Age3FoodCost, permille);
                scenario.Economy.Age3WoodCost = ScalePrice(scenario.Economy.Age3WoodCost, permille);
            }

            var saveArmyFloor = Environment.GetEnvironmentVariable("MATCHLEN_SAVE_ARMY_FLOOR");
            if (!string.IsNullOrEmpty(saveArmyFloor)) scenario.Economy.Age2SaveArmyFloor = int.Parse(saveArmyFloor, CultureInfo.InvariantCulture);

            var coreHp = Environment.GetEnvironmentVariable("MATCHLEN_CORE_HP");
            if (!string.IsNullOrEmpty(coreHp))
            {
                int hp = int.Parse(coreHp, CultureInfo.InvariantCulture);
                for (int i = 0; i < scenario.Cores.Length; i++) scenario.Cores[i].Hp = hp;
            }
        }

        private static bool IsEnabled(string name) => Environment.GetEnvironmentVariable(name) == "1";

        private static List<ulong> ReadSeeds()
        {
            string value = Environment.GetEnvironmentVariable("MATCHLEN_SEEDS");
            if (string.IsNullOrWhiteSpace(value)) return Enumerable.Range(1, 8).Select(i => (ulong)i).ToList();
            var parts = value.Split(new[] { '-' }, 2);
            ulong first = ulong.Parse(parts[0], CultureInfo.InvariantCulture);
            ulong last = parts.Length == 1 ? first : ulong.Parse(parts[1], CultureInfo.InvariantCulture);
            if (first < 1 || last < first) throw new ArgumentException("MATCHLEN_SEEDS must be a range such as 1-8.");
            var result = new List<ulong>();
            for (ulong seed = first; seed <= last; seed++) { result.Add(seed); if (seed == ulong.MaxValue) break; }
            return result;
        }

        private static string SeedRangeText(List<ulong> seeds) => seeds.Count == 0 ? "なし" : seeds.Count == 1 ? seeds[0].ToString(CultureInfo.InvariantCulture) : seeds[0].ToString(CultureInfo.InvariantCulture) + "-" + seeds[seeds.Count - 1].ToString(CultureInfo.InvariantCulture);

        private static int ScalePrice(int price, int permille) => checked((int)((long)price * permille / 1000L));

        private static int WinnerCivCount(List<MatchMeasurement> matches, CivKind civ) => matches.Count(m => m.HasEnded && ((m.Winner == 1 && m.Factions[0].Civ == civ) || (m.Winner == 2 && m.Factions[1].Civ == civ)));
        private static string AverageFishing(List<MatchMeasurement> matches, CivKind civ)
        {
            var values = matches.SelectMany(m => m.Factions).Where(f => f.Civ == civ).Select(f => (double)f.FishingFood).ToList();
            return values.Count == 0 ? "—" : values.Average().ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string CompareGoldMap(ulong seed)
        {
            var withGold = MapGenerator.GenerateTerrain(seed, gold: true);
            var withoutGold = MapGenerator.GenerateTerrain(seed);
            bool resources = withGold.ResourceNodes.Where(n => n.Kind != ResourceKind.Gold).SequenceEqual(withoutGold.ResourceNodes);
            bool cores = withGold.Cores.SequenceEqual(withoutGold.Cores);
            bool outposts = withGold.Outposts.SequenceEqual(withoutGold.Outposts);
            return resources && cores && outposts ? "同じ（金を追加しただけ）" : "違う（資源点=" + (resources ? "同じ" : "違う") + "、コア=" + (cores ? "同じ" : "違う") + "、前哨=" + (outposts ? "同じ" : "違う") + "）";
        }

        private static double Minutes(MatchMeasurement match) => match.DecisionTick / (double)match.TickRate / 60.0;

        private static string Median(List<double> sorted)
        {
            if (sorted.Count == 0) return "-";
            double value = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0;
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static int CountAge(List<MatchMeasurement> matches, int age, CivKind civ)
        {
            int count = 0;
            foreach (var match in matches)
                foreach (var faction in match.Factions)
                    if (faction.Civ == civ && (age == 2 ? faction.Age2Tick >= 0 : faction.Age3Tick >= 0)) count++;
            return count;
        }

        private sealed class MatchMeasurement
        {
            public readonly ulong Seed;
            public readonly int TickRate;
            public readonly FactionMeasurement[] Factions = { new FactionMeasurement(), new FactionMeasurement() };
            public readonly SoldierMeasurement[] Soldiers = { new SoldierMeasurement(), new SoldierMeasurement() };
            public readonly VillagerMeasurement[] Villagers = { new VillagerMeasurement(), new VillagerMeasurement() };
            public readonly List<Snapshot> Snapshots = new List<Snapshot>();
            private readonly int[][] IntervalSoldierTicks = { new int[4], new int[4] };
            public long DecisionTick;
            public bool HasEnded;
            public uint Winner;
            public bool IsAgeVictory;
            public bool IsDraw;
            public readonly string GoldMapComparison;
            private readonly ScenarioDefinition scenario;
            private Dictionary<string, string> previousDiagnostic;
            public MatchMeasurement(ulong seed, int tickRate, ScenarioDefinition scenario, bool gold)
            {
                Seed = seed; TickRate = tickRate; this.scenario = scenario;
                GoldMapComparison = gold ? CompareGoldMap(seed) : "対象外（金なし）";
            }

            public void Initialize(FactionFrame[] frames)
            {
                for (int i = 0; i < 2; i++)
                {
                    Factions[i].LastAge = frames[i].Economy.Age;
                    Factions[i].CoreHp = CoreHp(frames[i], (uint)(i + 1));
                }
            }

            public void Observe(long tick, FactionFrame[] frames)
            {
                for (int i = 0; i < 2; i++)
                {
                    var faction = Factions[i];
                    var frame = frames[i];
                    var economy = frame.Economy;
                    var soldiers = Soldiers[i];
                    var seen = new HashSet<uint>();
                    foreach (var unit in frame.Units)
                    {
                        if (!unit.IsOwn) continue;
                        seen.Add(unit.Id);
                        int category = unit.IsRetreating ? 0 : unit.IsAttacking ? 1 : unit.IsMoving ? 2 : 3;
                        soldiers.Ticks[category]++;
                        IntervalSoldierTicks[i][category]++;
                        if (unit.IsRetreating)
                        {
                            int streak = soldiers.Streaks.ContainsKey(unit.Id) ? soldiers.Streaks[unit.Id] + 1 : 1;
                            soldiers.Streaks[unit.Id] = streak;
                            if (streak > soldiers.LongestRetreat) soldiers.LongestRetreat = streak;
                            if (streak == RetreatThreshold && soldiers.LongRetreatIds.Add(unit.Id)) soldiers.LongRetreatingSoldiers++;
                        }
                        else soldiers.Streaks[unit.Id] = 0;
                    }
                    var oldIds = new List<uint>(soldiers.Streaks.Keys);
                    foreach (var id in oldIds) if (!seen.Contains(id)) soldiers.Streaks[id] = 0;
                    if (frame.Units.Count > 0 && faction.FirstBattleTick < 0)
                        foreach (var unit in frame.Units) if (unit.IsOwn && unit.IsAttacking) { faction.FirstBattleTick = tick; break; }

                    foreach (var villager in economy.Villagers)
                        if (villager.IsOwn) Villagers[i].Ticks[(int)villager.Activity]++;
                    if (economy.Age != faction.LastAge)
                    {
                        // Age 0 is the primitive age; 1 is the civilisation just taken, then its second and third ages.
                        if (economy.Age == 1 && faction.Age1Tick < 0) { faction.Age1Tick = tick; faction.Civ = economy.Civ; }
                        if (economy.Age == 2 && faction.Age2Tick < 0) faction.Age2Tick = tick;
                        if (economy.Age == 3 && faction.Age3Tick < 0) faction.Age3Tick = tick;
                        faction.LastAge = economy.Age;
                    }
                    int hp = CoreHp(frame, (uint)(i + 1));
                    if (faction.FirstCoreHitTick < 0 && faction.CoreHp >= 0 && hp >= 0 && hp < faction.CoreHp) faction.FirstCoreHitTick = tick;
                    if (hp >= 0) faction.CoreHp = hp;
                    if (tick % SnapshotInterval == 0)
                    {
                        Snapshots.Add(new Snapshot(tick, i + 1, frame, IntervalSoldierTicks[i]));
                        Array.Clear(IntervalSoldierTicks[i], 0, IntervalSoldierTicks[i].Length);
                    }
                }
                if (frames[0].Result.HasEnded && DecisionTick == 0)
                {
                    DecisionTick = tick; HasEnded = true; Winner = frames[0].Result.WinnerFactionId;
                    IsAgeVictory = frames[0].Result.IsAgeVictory; IsDraw = frames[0].Result.IsDraw;
                }
            }

            public void Finish(MatchResult result)
            {
                if (DecisionTick == 0) DecisionTick = MaxTicks;
                if (!HasEnded) { HasEnded = result.HasEnded; Winner = result.WinnerFactionId; IsAgeVictory = result.IsAgeVictory; IsDraw = result.IsDraw; }
            }

            public void ObserveDiagnostic(DiagnosticState state)
            {
                var fields = DiagnosticComparison.Fields(state).ToDictionary(p => p.Key, p => p.Value);
                if (fieldsFishingNodeIds.Count == 0)
                    foreach (var node in scenario.ResourceNodes)
                    {
                        string fishingKey = "ResourceNodes[" + node.Id.ToString(CultureInfo.InvariantCulture) + "].Fishing";
                        if (fields.ContainsKey(fishingKey) && fields[fishingKey] == "1") fieldsFishingNodeIds.Add(node.Id);
                    }
                if (previousDiagnostic != null)
                {
                    foreach (var node in scenario.ResourceNodes)
                    {
                        string key = "ResourceNodes[" + node.Id.ToString(CultureInfo.InvariantCulture) + "].Remaining";
                        if (!fields.ContainsKey(key) || !previousDiagnostic.ContainsKey(key)) continue;
                        int delta = int.Parse(previousDiagnostic[key], CultureInfo.InvariantCulture) - int.Parse(fields[key], CultureInfo.InvariantCulture);
                        if (delta <= 0) continue;
                        for (int faction = 1; faction <= 2; faction++)
                        {
                            bool working = false;
                            for (int v = 1; ; v++)
                            {
                                string prefix = "Villagers[" + v.ToString(CultureInfo.InvariantCulture) + "].";
                                string factionKey = prefix + "FactionId";
                                if (!fields.ContainsKey(factionKey)) break;
                                if (fields[factionKey] == faction.ToString(CultureInfo.InvariantCulture) && fields[prefix + "NodeId"] == node.Id.ToString(CultureInfo.InvariantCulture) && fields[prefix + "Task"] == "2") { working = true; break; }
                            }
                            if (working)
                            {
                                if (node.Kind == ResourceKind.Gold) Factions[faction - 1].GoldGathered += delta;
                                if (node.Kind == ResourceKind.Food && FishingNode(node)) Factions[faction - 1].FishingFood += delta;
                                break;
                            }
                        }
                    }
                }
                for (int faction = 1; faction <= 2; faction++)
                {
                    string prefix = "Economy[" + faction.ToString(CultureInfo.InvariantCulture) + "].";
                    string goldKey = prefix + "Gold";
                    if (fields.ContainsKey(goldKey)) Factions[faction - 1].MaxGold = Math.Max(Factions[faction - 1].MaxGold, int.Parse(fields[goldKey], CultureInfo.InvariantCulture));
                    for (int v = 1; ; v++)
                    {
                        string vp = "Villagers[" + v.ToString(CultureInfo.InvariantCulture) + "].";
                        if (!fields.ContainsKey(vp + "FactionId")) break;
                        if (fields[vp + "FactionId"] == faction.ToString(CultureInfo.InvariantCulture) && fields[vp + "Task"] == "2" && fields.ContainsKey(vp + "NodeId"))
                        {
                            uint id = uint.Parse(fields[vp + "NodeId"], CultureInfo.InvariantCulture);
                            var node = scenario.ResourceNodes.FirstOrDefault(n => n.Id == id);
                            if (node.Id != 0 && FishingNode(node)) Factions[faction - 1].FishingWorkerTicks++;
                        }
                    }
                }
                previousDiagnostic = fields;
            }

            private bool FishingNode(ResourceNodeDefinition node) => scenario.Economy.FishingEnabled && node.Kind == ResourceKind.Food && fieldsFishingNodeIds.Contains(node.Id);
            private readonly HashSet<uint> fieldsFishingNodeIds = new HashSet<uint>();

            public string SummaryLine() => $"seed {Seed}: {(HasEnded ? DecisionTick.ToString(CultureInfo.InvariantCulture) : "未決着")} tick ({Minutes(DecisionTick)}分), winner {Winner}, age victory {IsAgeVictory}, civ {Factions[0].Civ}/{Factions[1].Civ}";
            public string SummaryRow() => $"| {Seed} | {(HasEnded ? DecisionTick.ToString(CultureInfo.InvariantCulture) + " (" + Minutes(DecisionTick) + ")" : "未決着")} | {(IsDraw ? "引き分け" : Winner == 0 ? "なし" : Winner.ToString(CultureInfo.InvariantCulture))} | {WinnerCiv()} | {scenario.ResourceNodes.Count(n => n.Kind == ResourceKind.Gold)} | {(scenario.Economy.FishingEnabled ? fieldsFishingNodeIds.Count.ToString(CultureInfo.InvariantCulture) : "-")} | {(IsAgeVictory ? "はい" : "いいえ")} | {Factions[0].Civ} | {MinutesOrDash(Factions[0].Age1Tick)} | {MinutesOrDash(Factions[0].Age2Tick)} | {MinutesOrDash(Factions[0].Age3Tick)} | {Factions[1].Civ} | {MinutesOrDash(Factions[1].Age1Tick)} | {MinutesOrDash(Factions[1].Age2Tick)} | {MinutesOrDash(Factions[1].Age3Tick)} | {MinutesOrDash(Math.Min(FirstBattle(0), FirstBattle(1)))} | {MinutesOrDash(Factions[0].FirstCoreHitTick)} | {MinutesOrDash(Factions[1].FirstCoreHitTick)} |";
            private long FirstBattle(int i) => Factions[i].FirstBattleTick < 0 ? long.MaxValue : Factions[i].FirstBattleTick;
            private string WinnerCiv() => Winner == 1 ? Factions[0].Civ.ToString() : Winner == 2 ? Factions[1].Civ.ToString() : "—";
            private string MinutesOrDash(long tick) => tick < 0 || tick == long.MaxValue ? "—" : Minutes(tick);
            private string Minutes(long tick) => (tick / (double)TickRate / 60.0).ToString("0.00", CultureInfo.InvariantCulture);
        }

        private sealed class FactionMeasurement
        {
            public int LastAge;
            public CivKind Civ;
            public int CoreHp = -1;
            public long Age1Tick = -1, Age2Tick = -1, Age3Tick = -1, FirstBattleTick = -1, FirstCoreHitTick = -1;
            public long FishingFood, FishingWorkerTicks, GoldGathered;
            public int MaxGold;
            public string EconomyRow(ulong seed, int faction) => $"| {seed} | {faction} | {FishingFood} | {FishingWorkerTicks} | {GoldGathered} | {MaxGold} | {(Civ == CivKind.Metallurgy && Age3Tick >= 0 ? "はい" : "いいえ")} |";
        }

        private sealed class SoldierMeasurement
        {
            public readonly long[] Ticks = new long[4];
            public readonly Dictionary<uint, int> Streaks = new Dictionary<uint, int>();
            public readonly HashSet<uint> LongRetreatIds = new HashSet<uint>();
            public long LongRetreatingSoldiers;
            public int LongestRetreat;
            public string MarkdownRow(ulong seed, int faction)
            {
                long total = Ticks[0] + Ticks[1] + Ticks[2] + Ticks[3];
                string P(int i) => total == 0 ? "0.00%" : (Ticks[i] * 100.0 / total).ToString("0.00", CultureInfo.InvariantCulture) + "%";
                return $"| {seed} | {faction} | {P(0)} | {P(1)} | {P(2)} | {P(3)} | {LongRetreatingSoldiers} | {LongestRetreat} |";
            }
        }

        private sealed class VillagerMeasurement
        {
            public readonly long[] Ticks = new long[8];
            public string MarkdownRow(ulong seed, int faction)
            {
                long total = 0; foreach (long value in Ticks) total += value;
                string P(int i) => total == 0 ? "0.00%" : (Ticks[i] * 100.0 / total).ToString("0.00", CultureInfo.InvariantCulture) + "%";
                return $"| {seed} | {faction} | {P(0)} | {P(1)} | {P(2)} | {P(3)} | {P(4)} | {P(5)} | {P(6)} | {P(7)} |";
            }
        }

        private sealed class Snapshot
        {
            private readonly long Tick;
            private readonly int Faction, AliveSoldiers, Villagers, Population, PopulationCap, Food, Wood, Metal, Stone, Age;
            private readonly int[] SoldierTicks;
            public Snapshot(long tick, int faction, FactionFrame frame, int[] soldierTicks)
            {
                Tick = tick; Faction = faction; AliveSoldiers = 0;
                foreach (var unit in frame.Units) if (unit.IsOwn) AliveSoldiers++;
                Villagers = 0; foreach (var villager in frame.Economy.Villagers) if (villager.IsOwn) Villagers++;
                Population = frame.Economy.Population; PopulationCap = frame.Economy.PopulationCap;
                Food = frame.Economy.Food; Wood = frame.Economy.Wood; Metal = frame.Economy.Metal; Stone = frame.Economy.Stone; Age = frame.Economy.Age;
                SoldierTicks = (int[])soldierTicks.Clone();
            }
            public string MarkdownRow(ulong seed, int tickRate) => $"| {seed} | {Tick} | {(Tick / (double)tickRate / 60.0).ToString("0.00", CultureInfo.InvariantCulture)} | {Faction} | {AliveSoldiers} | {Villagers} | {Population}/{PopulationCap} | {Food} | {Wood} | {Metal} | {Stone} | {Age} | {SoldierTicks[0]} | {SoldierTicks[1]} | {SoldierTicks[2]} | {SoldierTicks[3]} |";
        }

        private static int CoreHp(FactionFrame frame, uint faction)
        {
            foreach (var objective in frame.Observation.Objectives)
                if (objective.Kind == GoalKind.Core && objective.IsOwnerKnown && objective.OwnerFactionId == faction && objective.IsHpKnown) return objective.Hp;
            return -1;
        }
    }
}
