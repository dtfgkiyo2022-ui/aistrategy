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
    /// Issue #110 follow-up probe: soldiers that want to move but have not moved 1 m in 300 ticks, sorted by why. An
    /// army keeps the path it found when its goal was set, and walks it only when every participant reached its slot,
    /// so a building placed later, or one soldier that cannot reach its slot, can hold the whole army. Investigation only.
    /// </summary>
    public sealed class MovementStallProbeTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [Test, Explicit("investigation"), Category("Measurement")]
        public void TraceSelectedRetreatArmyEveryTick()
        {
            ulong seed = ulong.Parse(Environment.GetEnvironmentVariable("PROBE_SEED") ?? "5", CultureInfo.InvariantCulture);
            uint wantedArmy = uint.Parse(Environment.GetEnvironmentVariable("PROBE_ARMY") ?? "2", CultureInfo.InvariantCulture);
            long traceFrom = long.Parse(Environment.GetEnvironmentVariable("PROBE_FROM") ?? "-1", CultureInfo.InvariantCulture);
            long traceTo = long.Parse(Environment.GetEnvironmentVariable("PROBE_TO") ?? "-1", CultureInfo.InvariantCulture);
            var scenario = MapGenerator.GenerateTerrain(seed);
            var phaseTrace = new StringBuilder();
            Battle sim = null;
            object observedWorld = null;
            Action<string, bool> measure = (phase, begin) =>
            {
                if (begin || observedWorld == null) return;
                long currentTick = (long)observedWorld.GetType().GetField("Tick", Flags).GetValue(observedWorld);
                if (traceFrom > 0 && currentTick >= traceFrom && (traceTo <= 0 || currentTick <= traceTo))
                {
                    var phaseSoldiers = (Array)observedWorld.GetType().GetField("Soldiers", Flags).GetValue(observedWorld);
                    var phaseSoldier = phaseSoldiers.GetValue(38);
                    var phaseState = phaseSoldier.GetType();
                    var phasePosition = (SimPoint)phaseState.GetField("Position", Flags).GetValue(phaseSoldier);
                    var phaseGoal = (SimPoint)phaseState.GetField("MoveGoal", Flags).GetValue(phaseSoldier);
                    phaseTrace.AppendLine($"phase tick {currentTick} {phase} soldier39 ({M(phasePosition.X.Raw):0.000},{M(phasePosition.Z.Raw):0.000}) goal ({M(phaseGoal.X.Raw):0.000},{M(phaseGoal.Z.Raw):0.000})");
                }
            };
            sim = new Battle(scenario, measure);
            var world = typeof(Battle).GetField("world", Flags).GetValue(sim);
            observedWorld = world;
            var map = world.GetType().GetField("Map", Flags).GetValue(world);
            var clear = typeof(Battle).GetMethod("Clear", Flags);
            var center = map.GetType().GetMethod("Center", new[] { typeof(int) });
            var cellOf = map.GetType().GetMethod("Cell", new[] { typeof(SimPoint) });
            var passable = map.GetType().GetMethod("IsPassable", new[] { typeof(int) });
            var moveTowards = typeof(FixMath).GetMethod("MoveTowards", BindingFlags.Static | BindingFlags.Public);
            var armyGoal = typeof(Battle).GetMethod("ArmyGoal", Flags);
            var previousReturning = false;
            long firstReturn = -1;
            var report = new StringBuilder();
            long limit = traceTo > 0 ? traceTo : 24000;
            for (long tick = 1; tick <= limit && !sim.Capture(1).Result.HasEnded; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                var armies = (Array)world.GetType().GetField("Armies", Flags).GetValue(world);
                var soldiers = (Array)world.GetType().GetField("Soldiers", Flags).GetValue(world);
                var f = DiagnosticComparison.Fields(sim.CaptureDiagnostic()).ToDictionary(p => p.Key, p => p.Value);
                string n = "Armies[" + wantedArmy.ToString(CultureInfo.InvariantCulture) + "].";
                bool returning = f.TryGetValue("Ai.Armies[" + wantedArmy.ToString(CultureInfo.InvariantCulture) + "].Returning", out var r) && r == "1";
                if (returning && !previousReturning) firstReturn = tick;
                previousReturning = returning;
                if (!returning || firstReturn < 0 || traceFrom > 0 && tick < traceFrom || traceTo > 0 && tick > traceTo
                    || traceFrom <= 0 && tick < firstReturn || traceFrom <= 0 && tick > firstReturn + 220) continue;
                var army = armies.GetValue((int)wantedArmy - 1);
                var armyType = army.GetType();
                var policy = armyType.GetField("Policy", Flags).GetValue(army);
                var armyPolicyGoal = armyType.GetField("Goal", Flags).GetValue(army);
                var decision = armyType.GetField("Decision", Flags).GetValue(army);
                var decisionReturning = decision.GetType().GetField("Returning", Flags).GetValue(decision);
                int cursor = (int)army.GetType().GetField("PathCursor", Flags).GetValue(army);
                int[] path = (int[])army.GetType().GetField("Path", Flags).GetValue(army) ?? Array.Empty<int>();
                var goal = (SimPoint)armyGoal.Invoke(sim, new[] { army });
                string pathCell = "none";
                if (cursor >= 0 && cursor < path.Length)
                {
                    var pc = (SimPoint)center.Invoke(map, new object[] { path[cursor] });
                    pathCell = $"cell {path[cursor]} center ({M(pc.X.Raw):0.000},{M(pc.Z.Raw):0.000}) passable {passable.Invoke(map, new object[] { path[cursor] })}";
                }
                report.AppendLine($"tick {tick} returning {returning} policy {policy} policyGoal {armyPolicyGoal} decisionReturning {decisionReturning} goal ({M(goal.X.Raw):0.000},{M(goal.Z.Raw):0.000}) path {cursor}/{path.Length} {pathCell}");
                var ids = (uint[])army.GetType().GetField("SoldierIds", Flags).GetValue(army) ?? Array.Empty<uint>();
                for (int i = 0; i < ids.Length; i++)
                {
                    uint id = ids[i];
                    var soldier = soldiers.GetValue((int)id - 1);
                    var st = soldier.GetType();
                    if (!(bool)st.GetField("Alive", Flags).GetValue(soldier)) continue;
                    var position = (SimPoint)st.GetField("Position", Flags).GetValue(soldier);
                    var moveGoal = (SimPoint)st.GetField("MoveGoal", Flags).GetValue(soldier);
                    var localGoal = (SimPoint)st.GetField("LocalGoal", Flags).GetValue(soldier);
                    var local = (int[])st.GetField("LocalPath", Flags).GetValue(soldier);
                    string slot = "none";
                    if (cursor >= 0 && cursor < path.Length)
                    {
                        var c = (SimPoint)center.Invoke(map, new object[] { path[cursor] });
                        var target = new SimPoint(c.X + Fix64.FromInt(i % 4), c.Z + Fix64.FromInt(i / 4));
                        if (!(bool)clear.Invoke(sim, new object[] { c, target }) || !(bool)clear.Invoke(sim, new object[] { position, target })
                            || cursor + 1 < path.Length && !(bool)clear.Invoke(sim, new object[] { target, center.Invoke(map, new object[] { path[cursor + 1] }) })) target = c;
                        if (cursor == path.Length - 1) target = (bool)clear.Invoke(sim, new object[] { position, goal }) ? goal : c;
                        slot = $"({M(target.X.Raw):0.000},{M(target.Z.Raw):0.000})";
                    }
                    var clipped = world.GetType().GetField("Map", Flags).GetValue(world).GetType().GetMethod("ClipMove", Flags).Invoke(map, new object[] { position, moveGoal });
                    var clippedPoint = (SimPoint)clipped;
                    var stepDistance = (Fix64)st.GetField("StepDistance", Flags).GetValue(soldier);
                    var intendedStep = (SimPoint)moveTowards.Invoke(null, new object[] { position, moveGoal, stepDistance });
                    var actualStep = (SimPoint)world.GetType().GetField("Map", Flags).GetValue(world).GetType().GetMethod("ClipMove", Flags).Invoke(map, new object[] { position, intendedStep });
                    report.AppendLine($"  soldier {id} pos ({M(position.X.Raw):0.000},{M(position.Z.Raw):0.000}) raw ({position.X.Raw},{position.Z.Raw}) cell {(int)cellOf.Invoke(map, new object[] { position })} move ({M(moveGoal.X.Raw):0.000},{M(moveGoal.Z.Raw):0.000}) step {M(stepDistance.Raw):0.000} intended ({M(intendedStep.X.Raw):0.000},{M(intendedStep.Z.Raw):0.000}) actual ({M(actualStep.X.Raw):0.000},{M(actualStep.Z.Raw):0.000}) clipped ({M(clippedPoint.X.Raw):0.000},{M(clippedPoint.Z.Raw):0.000}) slot {slot} moving {st.GetField("IsMoving", Flags).GetValue(soldier)} joining {st.GetField("Joining", Flags).GetValue(soldier)} tactical {st.GetField("TacticalRoute", Flags).GetValue(soldier)} local {st.GetField("LocalCursor", Flags).GetValue(soldier)}/{local?.Length ?? 0} localGoal ({M(localGoal.X.Raw):0.000},{M(localGoal.Z.Raw):0.000})");
                }
            }
            TestContext.WriteLine(report.ToString());
            TestContext.WriteLine(phaseTrace.ToString());
            string fallback = Path.Combine(Path.GetTempPath(), "rts-verify-110");
            Directory.CreateDirectory(fallback);
            File.WriteAllText(Path.Combine(fallback, $"trace_seed{seed}_army{wantedArmy}.txt"), report.ToString() + phaseTrace, new UTF8Encoding(false));
        }

        [Test, Explicit("investigation"), Category("Measurement")]
        public void CountStalledSoldiersByCause()
        {
            ulong seed = ulong.Parse(Environment.GetEnvironmentVariable("PROBE_SEED") ?? "4", CultureInfo.InvariantCulture);
            var s = MapGenerator.GenerateTerrain(seed);
            var sim = new Battle(s);
            var world = typeof(Battle).GetField("world", Flags).GetValue(sim);
            var map = world.GetType().GetField("Map", Flags).GetValue(world);
            var isPassable = map.GetType().GetMethod("IsPassable", new[] { typeof(int) });
            var cellOf = map.GetType().GetMethod("Cell", new[] { typeof(SimPoint) });
            bool Passable(int cell) => (bool)isPassable.Invoke(map, new object[] { cell });
            int Cell(SimPoint p) => (int)cellOf.Invoke(map, new object[] { p });

            var history = new Dictionary<string, Queue<SimPoint>>();
            var counts = new Dictionary<string, long> { { "army path blocked ahead", 0 }, { "own route blocked ahead", 0 }, { "waiting at slot for others", 0 }, { "other", 0 } };
            var examples = new StringBuilder();
            int shown = 0;
            for (long tick = 1; tick <= 24000 && !sim.Capture(1).Result.HasEnded; tick++)
            {
                sim.Step(tick, Array.Empty<ScheduledInput>());
                if (tick % 100 != 0) continue;
                var soldiers = (Array)world.GetType().GetField("Soldiers", Flags).GetValue(world);
                var armies = (Array)world.GetType().GetField("Armies", Flags).GetValue(world);
                foreach (var armyObject in armies)
                {
                    var at = armyObject.GetType();
                    var path = (int[])at.GetField("Path", Flags).GetValue(armyObject) ?? Array.Empty<int>();
                    int cursor = (int)at.GetField("PathCursor", Flags).GetValue(armyObject);
                    var ids = (uint[])at.GetField("SoldierIds", Flags).GetValue(armyObject) ?? Array.Empty<uint>();
                    bool armyBlocked = false;
                    for (int c = cursor; c < path.Length; c++) if (!Passable(path[c])) { armyBlocked = true; break; }
                    foreach (uint id in ids)
                    {
                        var soldier = soldiers.GetValue((int)id - 1);
                        var st = soldier.GetType();
                        string key = id.ToString(CultureInfo.InvariantCulture);
                        if (!(bool)st.GetField("Alive", Flags).GetValue(soldier)) { history.Remove(key); continue; }
                        var position = (SimPoint)st.GetField("Position", Flags).GetValue(soldier);
                        if (!history.TryGetValue(key, out var q)) history[key] = q = new Queue<SimPoint>();
                        q.Enqueue(position);
                        if (q.Count > 4) q.Dequeue();
                        if (q.Count < 4 || (bool)st.GetField("IsAttacking", Flags).GetValue(soldier)) continue;
                        var old = q.Peek();
                        if (Metres(old, position) >= 1) continue;
                        var goal = (SimPoint)st.GetField("MoveGoal", Flags).GetValue(soldier);
                        bool armyUnfinished = cursor + 1 < path.Length;
                        if (Metres(goal, position) < 0.5 && !armyUnfinished) continue; // really resting
                        var local = (int[])st.GetField("LocalPath", Flags).GetValue(soldier);
                        int localCursor = (int)st.GetField("LocalCursor", Flags).GetValue(soldier);
                        bool localBlocked = false;
                        if (local != null) for (int c = localCursor; c < local.Length; c++) if (!Passable(local[c])) { localBlocked = true; break; }
                        string cause = armyBlocked ? "army path blocked ahead" : localBlocked ? "own route blocked ahead"
                            : Metres(goal, position) < 0.5 && armyUnfinished ? "waiting at slot for others" : "other";
                        counts[cause]++;
                        if (shown < 25 && tick % 1000 == 0)
                        {
                            shown++;
                            examples.AppendLine($"tick {tick} soldier {id} cause '{cause}' at ({M(position.X.Raw):0.0},{M(position.Z.Raw):0.0}) cell {Cell(position)} goal ({M(goal.X.Raw):0.0},{M(goal.Z.Raw):0.0}) goal cell passable {Passable(Cell(goal))} army cursor {cursor}/{path.Length} retreating {st.GetField("IsRetreating", Flags).GetValue(soldier)} joining {st.GetField("Joining", Flags).GetValue(soldier)} tactical {st.GetField("TacticalRoute", Flags).GetValue(soldier)}");
                        }
                    }
                }
            }
            var report = new StringBuilder();
            report.AppendLine($"seed {seed}: soldier-samples (every 100 ticks) that wanted to move but moved under 1 m in 300 ticks");
            foreach (var pair in counts) report.AppendLine($"  {pair.Key}: {pair.Value}");
            report.AppendLine(examples.ToString());
            try
            {
                Directory.CreateDirectory(@"D:\rts-verify\110");
                File.WriteAllText($@"D:\rts-verify\110\stall_seed{seed}.txt", report.ToString(), new UTF8Encoding(false));
            }
            catch (UnauthorizedAccessException)
            {
                string fallback = Path.Combine(Path.GetTempPath(), "rts-verify-110");
                Directory.CreateDirectory(fallback);
                File.WriteAllText(Path.Combine(fallback, $"stall_seed{seed}.txt"), report.ToString(), new UTF8Encoding(false));
            }
            TestContext.WriteLine(report.ToString());
        }

        private static double M(long raw) => raw / 65536.0;
        private static double Metres(SimPoint a, SimPoint b) => Math.Sqrt(Math.Pow(M(a.X.Raw - b.X.Raw), 2) + Math.Pow(M(a.Z.Raw - b.Z.Raw), 2));
    }
}
