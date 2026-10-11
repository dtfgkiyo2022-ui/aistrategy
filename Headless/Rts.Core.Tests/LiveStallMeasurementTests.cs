using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;
using Battle = Rts.Simulation.Simulation;

namespace Rts.Core.Tests
{
    /// <summary>
    /// Measurement for the live terrain match. This is deliberately explicit: it is an investigation aid and does not
    /// change the simulation or the normal test suite.
    /// </summary>
    public sealed class LiveStallMeasurementTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const long MaxTicks = 30000;
        private static readonly long[] Checkpoints = { 14400, 21600, 28800 };

        [Test, Explicit("investigation"), Category("Measurement")]
        public void MeasureLiveStalls()
        {
            string seedText = Environment.GetEnvironmentVariable("STALL_SEEDS") ?? "1,4,5";
            string output = Environment.GetEnvironmentVariable("STALL_DIR") ?? Path.Combine(Directory.GetCurrentDirectory(), "stall");
            Directory.CreateDirectory(output);
            foreach (ulong seed in seedText.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => ulong.Parse(x.Trim(), CultureInfo.InvariantCulture)))
                WriteSeed(seed, output);
        }

        [Test, Explicit("investigation"), Category("Measurement")]
        public void MeasureLiveSummary()
        {
            string seedText = Environment.GetEnvironmentVariable("STALL_SUMMARY_SEEDS") ?? "1,2,3,4,5,6,7,8";
            string output = Environment.GetEnvironmentVariable("STALL_DIR") ?? Path.Combine(Directory.GetCurrentDirectory(), "stall");
            Directory.CreateDirectory(output);
            var summary = new StringBuilder("seed,ended,winner,endTick");
            for (long minute = 3; minute <= 24; minute += 3)
                summary.Append($",f1_{minute}m_age,f1_{minute}m_coreHp,f2_{minute}m_age,f2_{minute}m_coreHp");
            summary.AppendLine();
            foreach (ulong seed in seedText.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => ulong.Parse(x.Trim(), CultureInfo.InvariantCulture)))
                summary.AppendLine(SummaryRow(seed));
            File.WriteAllText(Path.Combine(output, "live-summary.csv"), summary.ToString(), new UTF8Encoding(false));
            TestContext.WriteLine(summary.ToString());
        }

        private static string SummaryRow(ulong seed)
        {
            var scenario = MeasuredScenario(seed);
            var sim = new Battle(scenario);
            object world = typeof(Battle).GetField("world", Flags).GetValue(sim);
            var values = new List<string>();
            long next = scenario.TickRateHz * 60 * 3;
            while (sim.Capture(1).Tick < MaxTicks && !sim.Capture(1).Result.HasEnded)
            {
                sim.Step(sim.Capture(1).Tick + 1, Array.Empty<ScheduledInput>());
                if (sim.Capture(1).Tick == next)
                {
                    AppendSummaryCheckpoint(values, sim, world, 1);
                    AppendSummaryCheckpoint(values, sim, world, 2);
                    next += scenario.TickRateHz * 60 * 3;
                }
            }
            var result = sim.Capture(1).Result;
            while (values.Count < 32) values.Add("");
            return string.Join(",", new[] { seed.ToString(CultureInfo.InvariantCulture), result.HasEnded ? "true" : "false",
                result.WinnerFactionId.ToString(CultureInfo.InvariantCulture), sim.Capture(1).Tick.ToString(CultureInfo.InvariantCulture) }.Concat(values));
        }

        private static void AppendSummaryCheckpoint(List<string> values, Battle sim, object world, uint faction)
        {
            values.Add(sim.Capture(faction).Economy?.Age.ToString(CultureInfo.InvariantCulture) ?? "");
            values.Add(GetInt(Core(world, faction), "Hp").ToString(CultureInfo.InvariantCulture));
        }

        private static void WriteSeed(ulong seed, string output)
        {
            var scenario = MeasuredScenario(seed);
            var sim = new Battle(scenario);
            object world = typeof(Battle).GetField("world", Flags).GetValue(sim);
            var report = new StringBuilder();
            report.AppendLine($"seed={seed} tickRate={scenario.TickRateHz} maxTicks={MaxTicks}");
            report.AppendLine($"rules CoreDefence={scenario.Economy.CoreDefence} range={scenario.Economy.CoreDefenceRange} damage={scenario.Economy.CoreDefenceDamage} interval={scenario.Economy.CoreDefenceIntervalTicks} maxTargets={scenario.Economy.CoreDefenceMaxTargets}");
            report.AppendLine($"rules LatePush={scenario.Economy.LatePush} after={scenario.Economy.LatePushAfterTicks} advantageAfter={scenario.Economy.LatePushAdvantageAfterTicks} loss=300 permille");
            report.AppendLine($"rules SiegeByAge={scenario.Economy.SiegeByAge} permilles={scenario.Economy.SiegeAge0Permille}/{scenario.Economy.SiegeAge1Permille}/{scenario.Economy.SiegeAge2Permille}/{scenario.Economy.SiegeAge3Permille}");

            var previousAlive = new Dictionary<uint, bool>();
            var deathCounts = new int[2, 2];
            foreach (var checkpoint in Checkpoints)
            {
                while (sim.Capture(1).Tick < checkpoint && !sim.Capture(1).Result.HasEnded)
                {
                    long tick = sim.Capture(1).Tick + 1;
                    sim.Step(tick, Array.Empty<ScheduledInput>());
                    ObserveDeaths(world, scenario, previousAlive, deathCounts);
                }
                if (sim.Capture(1).Tick == checkpoint)
                    AppendCheckpoint(report, sim, world, scenario, checkpoint, deathCounts);
            }
            while (sim.Capture(1).Tick < MaxTicks && !sim.Capture(1).Result.HasEnded)
            {
                long tick = sim.Capture(1).Tick + 1;
                sim.Step(tick, Array.Empty<ScheduledInput>());
                ObserveDeaths(world, scenario, previousAlive, deathCounts);
            }
            var result = sim.Capture(1).Result;
            report.AppendLine($"result ended={result.HasEnded} winner={result.WinnerFactionId} tick={sim.Capture(1).Tick}");
            report.AppendLine($"deaths coreDefence faction1={deathCounts[0, 0]} enemyOrOther={deathCounts[0, 1]} faction2={deathCounts[1, 0]} enemyOrOther={deathCounts[1, 1]}");
            File.WriteAllText(Path.Combine(output, $"live-seed-{seed}.txt"), report.ToString(), new UTF8Encoding(false));
            TestContext.WriteLine(report.ToString());
        }

        private static ScenarioDefinition MeasuredScenario(ulong seed)
        {
            var scenario = LiveGameRules.Create(seed, false, true, false, false);
            if (int.TryParse(Environment.GetEnvironmentVariable("STALL_CORE_DAMAGE"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var damage))
                scenario.Economy.CoreDefenceDamage = damage;
            if (int.TryParse(Environment.GetEnvironmentVariable("STALL_CORE_RANGE"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var range))
                scenario.Economy.CoreDefenceRange = range;
            return scenario;
        }

        private static void AppendCheckpoint(StringBuilder report, Battle sim, object world, ScenarioDefinition scenario, long tick, int[,] deathCounts)
        {
            report.AppendLine();
            report.AppendLine($"===== checkpoint tick={tick} minutes={(tick / (double)scenario.TickRateHz / 60.0).ToString("0.00", CultureInfo.InvariantCulture)} =====");
            for (uint faction = 1; faction <= 2; faction++)
            {
                var frame = sim.Capture(faction);
                var core = Core(world, faction);
                report.AppendLine($"faction {faction} age={frame.Economy?.Age} civ={frame.Economy?.Civ} coreHp={GetInt(core, "Hp")} resources food={frame.Economy?.Food} wood={frame.Economy?.Wood} metal={frame.Economy?.Metal} stone={frame.Economy?.Stone} population={frame.Economy?.Population}/{frame.Economy?.PopulationCap}");
                AppendArmies(report, frame, world, scenario, faction);
                AppendCommands(report, frame);
                AppendTraining(report, sim, world, frame, faction);
                report.AppendLine($"deathSoFar coreDefence={deathCounts[faction - 1, 0]} enemyOrOther={deathCounts[faction - 1, 1]}");
            }
        }

        private static void AppendArmies(StringBuilder report, FactionFrame frame, object world, ScenarioDefinition scenario, uint faction)
        {
            var armies = (Array)GetField(world, "Armies");
            var soldiers = (Array)GetField(world, "Soldiers");
            var enemyCore = Core(world, faction == 1 ? 2U : 1U);
            SimPoint enemyPosition = GetStruct<SimPoint>(GetField(enemyCore, "Definition"), "Position");
            foreach (object army in armies)
            {
                object definition = GetField(army, "Definition");
                if (GetUInt(definition, "FactionId") != faction) continue;
                var ids = (uint[])GetField(army, "SoldierIds") ?? Array.Empty<uint>();
                var live = ids.Where(id => GetBool(soldiers.GetValue((int)id - 1), "Alive")).ToArray();
                var statuses = new Dictionary<string, int>();
                var causes = new Dictionary<string, int>();
                foreach (uint id in live)
                {
                    object soldier = soldiers.GetValue((int)id - 1);
                    string status = GetBool(soldier, "IsRetreating") ? "撤退中(IsRetreating)" : GetBool(soldier, "IsAttacking") ? "攻撃中(IsAttacking)" : GetBool(soldier, "IsMoving") ? "移動中(IsMoving)" : "待機";
                    Add(statuses, status);
                    string cause = Cause(army, soldier, scenario, world);
                    Add(causes, cause);
                }
                object decision = GetField(army, "Decision");
                var pos = live.Length == 0 ? default : GetStruct<SimPoint>(soldiers.GetValue((int)live[0] - 1), "Position");
                string policy = GetField(army, "Policy").ToString();
                string goal = GoalText(GetField(army, "Goal"));
                string assignment = GetField(decision, "Assignment").ToString();
                bool returning = GetBool(decision, "Returning");
                report.AppendLine($"army id={GetUInt(definition, "Id")} kind={GetField(definition, "Role")} live={live.Length}/{ids.Length} pos={Point(pos)} enemyCoreDistance={Distance(pos, enemyPosition).ToString("0.0", CultureInfo.InvariantCulture)} policy={policy} goal={goal} decision={assignment} returning={returning} path={GetInt(army, "PathCursor")}/{((Array)GetField(army, "Path"))?.Length ?? 0} pathImpossible={GetBool(army, "PathImpossible")} status=[{Join(statuses)}] causes=[{Join(causes)}]");
                foreach (uint id in live)
                {
                    object soldier = soldiers.GetValue((int)id - 1);
                    var p = GetStruct<SimPoint>(soldier, "Position");
                    report.AppendLine($"  soldier id={id} kind={GetField(GetField(soldier, "Initial"), "Kind")} pos={Point(p)} enemyCoreDistance={Distance(p, enemyPosition).ToString("0.0", CultureInfo.InvariantCulture)} status={(GetBool(soldier, "IsRetreating") ? "撤退中" : GetBool(soldier, "IsAttacking") ? "攻撃中" : GetBool(soldier, "IsMoving") ? "移動中" : "待機")} moveGoal={Point(GetStruct<SimPoint>(soldier, "MoveGoal"))} joining={GetBool(soldier, "Joining")} tactical={GetBool(soldier, "TacticalRoute")} slotTimeout={GetBool(soldier, "SlotWaitTimedOut")} target={GetField(soldier, "TargetKind")}:{GetUInt(soldier, "TargetId")}");
                }
            }
        }

        private static void AppendCommands(StringBuilder report, FactionFrame frame)
        {
            foreach (var c in frame.Commands.OrderBy(c => c.CommandId))
                report.AppendLine($"command id={c.CommandId} target={c.Target.Kind}:{c.Target.Id} kind={c.Kind} goal={GoalText(c.Goal)} source={c.Source} status={c.Status} reason={c.Reason} accepted={c.AcceptedTick} apply={c.ApplyTick}");
        }

        private static void AppendTraining(StringBuilder report, Battle sim, object world, FactionFrame frame, uint faction)
        {
            var economy = frame.Economy;
            var buildings = economy?.Buildings.Where(b => b.FactionId == faction && (b.Kind == BuildingKind.Barracks || b.Kind == BuildingKind.ArcheryRange || b.Kind == BuildingKind.Stable || b.Kind == BuildingKind.SiegeWorkshop || b.Kind == BuildingKind.Monastery || b.Kind == BuildingKind.Castle)).ToArray() ?? Array.Empty<BuildingView>();
            var ready = buildings.Count(b => b.Complete && b.Queued < 5);
            var queued = buildings.Sum(b => b.Queued);
            string reason = economy == null ? "EconomyOff" : economy.Population >= economy.PopulationCap ? "PopulationCap" : buildings.Length == 0 ? "NoTrainingBuilding" : ready == 0 ? "QueueLimit" : queued == 0 ? "AutoDecisionOrCanPay" : "TrainingInProgress";
            var hasRoom = typeof(Battle).GetMethod("HasInfantryRoom", Flags).Invoke(sim, new object[] { faction });
            var canPay = typeof(Battle).GetMethod("CanPay", Flags, null, new[] { typeof(uint), typeof(UnitKind) }, null).Invoke(sim, new object[] { faction, UnitKind.Infantry });
            int foodCost = (int)typeof(Battle).GetMethod("InfantryFoodFor", Flags).Invoke(sim, new object[] { faction });
            int woodCost = (int)typeof(Battle).GetMethod("InfantryWoodFor", Flags).Invoke(sim, new object[] { faction });
            int metalCost = (int)typeof(Battle).GetMethod("InfantryMetalFor", Flags).Invoke(sim, new object[] { faction });
            report.AppendLine($"training reason={reason} canPayInfantry={canPay} cost={foodCost}/{woodCost}/{metalCost} population={economy?.Population}/{economy?.PopulationCap} villagerQueue={economy?.VillagerQueued} buildings={buildings.Length} ready={ready} queued={queued} hasInfantryRoom={hasRoom} buildings=[{string.Join(",", buildings.Select(b => $"{b.Kind}:{b.Queued}/{b.TrainRemaining}"))}]");
        }

        private static string Cause(object army, object soldier, ScenarioDefinition scenario, object world)
        {
            object decision = GetField(army, "Decision");
            if (GetBool(soldier, "IsRetreating") || GetBool(decision, "Returning") || GetField(army, "Policy").ToString() == "Retreat") return "Retreat/Returning";
            if (GetBool(army, "PathImpossible")) return "NoPath(PathImpossible)";
            if (GetField(decision, "Assignment").ToString() == "Reserve") return "Reserve(ReserveShortfall候補)";
            if (GetBool(soldier, "Joining") || GetBool(soldier, "SlotWaitTimedOut")) return "FormationSlotWait";
            if (GetField(army, "Policy").ToString() == "Focus" && GetField(army, "Goal").ToString().Contains("None")) return "NoGoal";
            if (!GetBool(soldier, "IsMoving") && !GetBool(soldier, "IsAttacking")) return "Idle/NoIntent";
            return "Active";
        }

        private static void ObserveDeaths(object world, ScenarioDefinition scenario, Dictionary<uint, bool> previousAlive, int[,] counts)
        {
            var soldiers = (Array)GetField(world, "Soldiers");
            for (int i = 0; i < soldiers.Length; i++)
            {
                object soldier = soldiers.GetValue(i);
                object initial = GetField(soldier, "Initial");
                uint id = GetUInt(initial, "Id");
                bool alive = GetBool(soldier, "Alive");
                if (previousAlive.TryGetValue(id, out var wasAlive) && wasAlive && !alive)
                {
                    uint faction = GetUInt(initial, "FactionId");
                    uint enemy = faction == 1 ? 2U : 1U;
                    var enemyCore = Core(world, enemy);
                    var position = GetStruct<SimPoint>(soldier, "Position");
                    bool coreRange = Distance(position, GetStruct<SimPoint>(GetField(enemyCore, "Definition"), "Position")) <= scenario.Economy.CoreDefenceRange;
                    counts[faction - 1, coreRange ? 0 : 1]++;
                }
                previousAlive[id] = alive;
            }
        }

        private static object Core(object world, uint faction)
        {
            object[] factions = ((Array)GetField(world, "Factions")).Cast<object>().ToArray();
            uint coreId = GetUInt(factions[faction - 1], "CoreId");
            return ((Array)GetField(world, "Cores")).GetValue((int)coreId - 1);
        }

        private static object GetField(object value, string name) => value.GetType().GetField(name, Flags).GetValue(value);
        private static T GetStruct<T>(object value, string name) => (T)GetField(value, name);
        private static int GetInt(object value, string name) => (int)GetField(value, name);
        private static uint GetUInt(object value, string name) => Convert.ToUInt32(GetField(value, name), CultureInfo.InvariantCulture);
        private static bool GetBool(object value, string name) => (bool)GetField(value, name);
        private static string GoalText(object goal)
        {
            if (goal == null) return "null";
            var type = goal.GetType();
            var kind = type.GetField("Kind", Flags)?.GetValue(goal) ?? type.GetProperty("Kind")?.GetValue(goal);
            var id = type.GetField("Id", Flags)?.GetValue(goal) ?? type.GetProperty("Id")?.GetValue(goal);
            return $"{kind}:{Convert.ToUInt32(id ?? 0, CultureInfo.InvariantCulture)}";
        }
        private static string Point(SimPoint p) => $"({(p.X.Raw / 65536.0).ToString("0.0", CultureInfo.InvariantCulture)},{(p.Z.Raw / 65536.0).ToString("0.0", CultureInfo.InvariantCulture)})";
        private static double Distance(SimPoint a, SimPoint b) { double x = (a.X.Raw - b.X.Raw) / 65536.0; double z = (a.Z.Raw - b.Z.Raw) / 65536.0; return Math.Sqrt(x * x + z * z); }
        private static void Add(Dictionary<string, int> counts, string key) { counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1; }
        private static string Join(Dictionary<string, int> values) => string.Join(",", values.OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value.ToString(CultureInfo.InvariantCulture)));
    }
}
