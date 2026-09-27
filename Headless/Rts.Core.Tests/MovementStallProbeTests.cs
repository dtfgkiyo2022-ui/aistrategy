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
            Directory.CreateDirectory(@"D:\rts-verify\110");
            File.WriteAllText($@"D:\rts-verify\110\stall_seed{seed}.txt", report.ToString(), new UTF8Encoding(false));
            TestContext.WriteLine(report.ToString());
        }

        private static double M(long raw) => raw / 65536.0;
        private static double Metres(SimPoint a, SimPoint b) => Math.Sqrt(Math.Pow(M(a.X.Raw - b.X.Raw), 2) + Math.Pow(M(a.Z.Raw - b.Z.Raw), 2));
    }
}
