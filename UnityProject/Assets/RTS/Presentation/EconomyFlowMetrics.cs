using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Presentation
{
    public struct FlowMinuteSample
    {
        public long Minute;
        public int Produced;
        public int Consumed;

        public FlowMinuteSample(long minute, int produced, int consumed)
        {
            Minute = minute; Produced = produced; Consumed = consumed;
        }
    }

    /// <summary>
    /// Display-side rolling counters for processing lines.  It deliberately has no Unity dependency so the same
    /// frame-difference rules can be checked by the headless test project.
    /// </summary>
    public sealed class EconomyFlowMetrics
    {
        public const long SimulationTicksPerSecond = 20;
        public const long WindowTicks = 60 * SimulationTicksPerSecond;

        private sealed class BuildingState
        {
            public long LastTick = long.MinValue;
            public int LastProgress;
            public int LastInput;
            public int LastInputSecondary;
            public int LastOutput;
            public CraftRecipe Recipe;
            public bool HasRecipe;
            public readonly Queue<Sample> Samples = new Queue<Sample>();
        }

        private sealed class MinuteBucket
        {
            public long Minute;
            public readonly Dictionary<ResourceKind, FlowMinuteSample> Values = new Dictionary<ResourceKind, FlowMinuteSample>();
        }

        private struct Sample
        {
            public long Tick;
            public bool Active;

            public Sample(long tick, bool active) { Tick = tick; Active = active; }
        }

        private sealed class BeltState
        {
            public bool HadItem;
            public long LastTick = long.MinValue;
            public readonly Queue<long> Deliveries = new Queue<long>();
        }

        private readonly Dictionary<uint, BuildingState> buildings = new Dictionary<uint, BuildingState>();
        private readonly Dictionary<int, BeltState> beltEnds = new Dictionary<int, BeltState>();
        private readonly HashSet<ResourceKind> availableKinds = new HashSet<ResourceKind>();
        private readonly Queue<MinuteBucket> completedMinutes = new Queue<MinuteBucket>();
        private MinuteBucket currentMinute;

        /// <summary>Records one visible frame. Tick is the simulation tick, not Time.deltaTime.</summary>
        public void ObserveBuildings(long tick, IReadOnlyList<BuildingView> views, uint ownFaction)
        {
            if (views == null) return;
            EnsureMinute(tick);
            for (int i = 0; i < views.Count; i++)
            {
                var view = views[i];
                if (view.FactionId != ownFaction) continue;
                BuildingState state;
                if (!buildings.TryGetValue(view.Id, out state))
                {
                    state = new BuildingState { LastProgress = view.Progress, LastInput = view.Input,
                        LastInputSecondary = view.InputSecondary, LastOutput = view.Output };
                    state.HasRecipe = CraftVisualRules.TryGetRecipe(view.Kind, out state.Recipe);
                    if (state.HasRecipe) availableKinds.Add(state.Recipe.Output);
                    buildings.Add(view.Id, state);
                }

                bool active = state.LastTick != long.MinValue && view.Progress > state.LastProgress;
                // A newly observed completed building has no previous frame to compare with; do not invent work.
                if (state.LastTick == long.MinValue) active = false;
                state.Samples.Enqueue(new Sample(tick, active));
                state.LastProgress = view.Progress;
                if (state.HasRecipe && state.LastTick != long.MinValue)
                {
                    EnsureMinute(tick);
                    int produced = PositiveDelta(view.Output, state.LastOutput);
                    int consumed = PositiveDelta(state.LastInput, view.Input);
                    int secondaryConsumed = PositiveDelta(state.LastInputSecondary, view.InputSecondary);
                    AddFlow(state.Recipe.Output, produced, 0);
                    AddFlow(state.Recipe.Input, 0, consumed);
                    AddFlow(state.Recipe.InputSecondary, 0, secondaryConsumed);
                }
                state.LastInput = view.Input;
                state.LastInputSecondary = view.InputSecondary;
                state.LastOutput = view.Output;
                state.LastTick = tick;
                Trim(state.Samples, tick);
            }
        }

        public void GetAvailableKinds(List<ResourceKind> result)
        {
            if (result == null) return;
            result.Clear();
            foreach (var kind in availableKinds) result.Add(kind);
            result.Sort((a, b) => ((int)a).CompareTo((int)b));
        }

        public FlowMinuteSample GetCurrentMinute(ResourceKind kind)
        {
            if (currentMinute == null) return new FlowMinuteSample(0, 0, 0);
            FlowMinuteSample value;
            return currentMinute.Values.TryGetValue(kind, out value) ? value : new FlowMinuteSample(currentMinute.Minute, 0, 0);
        }

        public void GetHistory(ResourceKind kind, List<FlowMinuteSample> result)
        {
            if (result == null) return;
            result.Clear();
            foreach (var bucket in completedMinutes)
            {
                FlowMinuteSample value;
                result.Add(bucket.Values.TryGetValue(kind, out value)
                    ? value : new FlowMinuteSample(bucket.Minute, 0, 0));
            }
            if (currentMinute != null)
            {
                FlowMinuteSample value;
                result.Add(currentMinute.Values.TryGetValue(kind, out value)
                    ? value : new FlowMinuteSample(currentMinute.Minute, 0, 0));
            }
            while (result.Count < 10) result.Insert(0, new FlowMinuteSample(long.MinValue, 0, 0));
            if (result.Count > 10) result.RemoveRange(0, result.Count - 10);
        }

        public float BuildingUtilization(uint buildingId)
        {
            BuildingState state;
            if (!buildings.TryGetValue(buildingId, out state) || state.Samples.Count == 0) return 0f;
            int active = 0;
            foreach (var sample in state.Samples) if (sample.Active) active++;
            return (float)active / state.Samples.Count;
        }

        /// <summary>
        /// Counts a delivery when an item disappears from a belt cell that the presentation identified as an end.
        /// The simulation remains the sole owner of the item; this is only an observation.
        /// </summary>
        public void ObserveBeltEnds(long tick, IReadOnlyList<BeltView> belts, IReadOnlyCollection<int> endCells, uint ownFaction)
        {
            if (endCells == null) return;
            var current = new HashSet<int>();
            if (belts != null)
            {
                for (int i = 0; i < belts.Count; i++)
                {
                    var belt = belts[i];
                    if (belt.FactionId == ownFaction && HasCell(endCells, belt.Cell))
                        current.Add(belt.Cell);
                }
            }
            foreach (int cell in endCells)
            {
                BeltState state;
                if (!beltEnds.TryGetValue(cell, out state))
                {
                    state = new BeltState();
                    beltEnds.Add(cell, state);
                }
                bool hasItem = current.Contains(cell);
                if (state.LastTick != long.MinValue && state.HadItem && !hasItem)
                    state.Deliveries.Enqueue(tick);
                state.HadItem = hasItem;
                state.LastTick = tick;
                while (state.Deliveries.Count > 0 && state.Deliveries.Peek() < tick - WindowTicks)
                    state.Deliveries.Dequeue();
            }
        }

        public int BeltDeliveriesPerMinute(int cell)
        {
            BeltState state;
            return beltEnds.TryGetValue(cell, out state) ? state.Deliveries.Count : 0;
        }

        public void Clear()
        {
            buildings.Clear();
            beltEnds.Clear();
            availableKinds.Clear();
            completedMinutes.Clear();
            currentMinute = null;
        }

        private void EnsureMinute(long tick)
        {
            long minute = tick / WindowTicks;
            if (currentMinute == null) { currentMinute = new MinuteBucket { Minute = minute }; return; }
            if (minute < currentMinute.Minute) return;
            while (currentMinute.Minute < minute)
            {
                completedMinutes.Enqueue(currentMinute);
                while (completedMinutes.Count > 9) completedMinutes.Dequeue();
                currentMinute = new MinuteBucket { Minute = currentMinute.Minute + 1 };
            }
        }

        private void AddFlow(ResourceKind kind, int produced, int consumed)
        {
            if (kind == 0 || currentMinute == null || (produced == 0 && consumed == 0)) return;
            FlowMinuteSample value;
            if (!currentMinute.Values.TryGetValue(kind, out value)) value = new FlowMinuteSample(currentMinute.Minute, 0, 0);
            value.Produced += produced;
            value.Consumed += consumed;
            currentMinute.Values[kind] = value;
        }

        private static int PositiveDelta(int before, int after)
        {
            return before > after ? before - after : 0;
        }

        private static void Trim(Queue<Sample> samples, long tick)
        {
            while (samples.Count > 0 && samples.Peek().Tick < tick - WindowTicks) samples.Dequeue();
        }

        private static bool HasCell(IReadOnlyCollection<int> cells, int target)
        {
            foreach (int cell in cells) if (cell == target) return true;
            return false;
        }
    }
}
