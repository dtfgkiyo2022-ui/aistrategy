using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
            report.AppendLine("## 表1: 試合ごとの節目");
            report.AppendLine();
            report.AppendLine("| seed | 決着tick（分） | 勝者 | 時代到達勝利 | 陣営1文明 | 陣営1 2つ目の時代(分) | 陣営1 3つ目の時代(分) | 陣営2文明 | 陣営2 2つ目の時代(分) | 陣営2 3つ目の時代(分) | 最初の戦闘(分) | 陣営1コア初被弾(分) | 陣営2コア初被弾(分) |");
            report.AppendLine("|---:|---:|---:|:---:|:---|---:|---:|:---|---:|---:|---:|---:|---:|");

            for (ulong seed = 1; seed <= 8; seed++)
            {
                var match = Measure(seed);
                matches.Add(match);
                report.AppendLine(match.SummaryRow());
                TestContext.WriteLine(match.SummaryLine());
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
            report.AppendLine($"8試合、決着 {decisive}/8、決着tick合計 {totalTicks.ToString(CultureInfo.InvariantCulture)}、決着tick平均 {(totalTicks / 8.0).ToString("0.0", CultureInfo.InvariantCulture)}。");

            var reportPath = @"D:\rts-verify\matchlen\report.md";
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
            TestContext.WriteLine($"8試合完了: decisive {decisive}/8, decision ticks total {totalTicks.ToString(CultureInfo.InvariantCulture)}, report {reportPath}");
            Assert.That(matches.Count, Is.EqualTo(8));
        }

        private static MatchMeasurement Measure(ulong seed)
        {
            var scenario = MapGenerator.GenerateTerrain(seed);
            int tickRate = scenario.TickRateHz;
            var sim = new Battle(scenario);
            var match = new MatchMeasurement(seed, tickRate);
            var previous = new FactionFrame[2];
            previous[0] = sim.Capture(1); previous[1] = sim.Capture(2);
            match.Initialize(previous);

            for (long tick = 1; tick <= MaxTicks && !previous[0].Result.HasEnded; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                var current = new[] { sim.Capture(1), sim.Capture(2) };
                match.Observe(tick, current);
                previous = current;
            }

            match.Finish(previous[0].Result);
            _ = sim.CaptureDiagnostic();
            return match;
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
            public MatchMeasurement(ulong seed, int tickRate) { Seed = seed; TickRate = tickRate; }

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
                        if (economy.Age == 2 && faction.Age2Tick < 0) { faction.Age2Tick = tick; faction.Civ = economy.Civ; }
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

            public string SummaryLine() => $"seed {Seed}: {(HasEnded ? DecisionTick.ToString(CultureInfo.InvariantCulture) : "未決着")} tick ({Minutes(DecisionTick)}分), winner {Winner}, age victory {IsAgeVictory}, civ {Factions[0].Civ}/{Factions[1].Civ}";
            public string SummaryRow() => $"| {Seed} | {(HasEnded ? DecisionTick.ToString(CultureInfo.InvariantCulture) + " (" + Minutes(DecisionTick) + ")" : "未決着")} | {(IsDraw ? "引き分け" : Winner == 0 ? "なし" : Winner.ToString(CultureInfo.InvariantCulture))} | {(IsAgeVictory ? "はい" : "いいえ")} | {Factions[0].Civ} | {MinutesOrDash(Factions[0].Age2Tick)} | {MinutesOrDash(Factions[0].Age3Tick)} | {Factions[1].Civ} | {MinutesOrDash(Factions[1].Age2Tick)} | {MinutesOrDash(Factions[1].Age3Tick)} | {MinutesOrDash(Math.Min(FirstBattle(0), FirstBattle(1)))} | {MinutesOrDash(Factions[0].FirstCoreHitTick)} | {MinutesOrDash(Factions[1].FirstCoreHitTick)} |";
            private long FirstBattle(int i) => Factions[i].FirstBattleTick < 0 ? long.MaxValue : Factions[i].FirstBattleTick;
            private string MinutesOrDash(long tick) => tick < 0 || tick == long.MaxValue ? "—" : Minutes(tick);
            private string Minutes(long tick) => (tick / (double)TickRate / 60.0).ToString("0.00", CultureInfo.InvariantCulture);
        }

        private sealed class FactionMeasurement
        {
            public int LastAge;
            public CivKind Civ;
            public int CoreHp = -1;
            public long Age2Tick = -1, Age3Tick = -1, FirstBattleTick = -1, FirstCoreHitTick = -1;
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
