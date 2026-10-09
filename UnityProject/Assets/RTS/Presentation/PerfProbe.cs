using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Rts.Presentation
{
    /// <summary>
    /// Display-side timing for finding what makes a long play slow (10-09: frames got choppy as the match went on).
    /// Each measured Update adds its milliseconds; the host writes one summary line every few seconds and resets.
    /// Never read by the simulation.
    /// </summary>
    public static class PerfProbe
    {
        private static readonly Dictionary<string, double> totals = new Dictionary<string, double>(StringComparer.Ordinal);
        private static readonly Dictionary<string, double> worst = new Dictionary<string, double>(StringComparer.Ordinal);
        private static readonly List<string> order = new List<string>();
        private static readonly Dictionary<string, Func<int>> counters = new Dictionary<string, Func<int>>(StringComparer.Ordinal);
        private static readonly List<string> counterOrder = new List<string>();

        public static long Start() => Stopwatch.GetTimestamp();

        public static void Stop(string name, long started)
        {
            double ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            if (!totals.ContainsKey(name)) { totals[name] = 0; worst[name] = 0; order.Add(name); }
            totals[name] += ms;
            if (ms > worst[name]) worst[name] = ms;
        }

        /// <summary>A count read when a summary is written (objects, UI elements, entries).</summary>
        public static void Counter(string name, Func<int> read)
        {
            if (!counters.ContainsKey(name)) counterOrder.Add(name);
            counters[name] = read;
        }

        /// <summary>Average and worst milliseconds per frame of each measured part since the last summary, then resets.</summary>
        public static string Summary(int frames)
        {
            var text = new StringBuilder();
            int n = Math.Max(1, frames);
            for (int i = 0; i < order.Count; i++)
            {
                string name = order[i];
                text.Append(' ').Append(name).Append('=').Append((totals[name] / n).ToString("0.00"))
                    .Append('/').Append(worst[name].ToString("0.0"));
                totals[name] = 0;
                worst[name] = 0;
            }
            for (int i = 0; i < counterOrder.Count; i++)
            {
                int value;
                try { value = counters[counterOrder[i]](); }
                catch (Exception) { value = -1; }
                text.Append(' ').Append(counterOrder[i]).Append('#').Append(value);
            }
            return text.ToString();
        }
    }
}
