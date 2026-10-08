using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Presentation
{
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
            public readonly Queue<Sample> Samples = new Queue<Sample>();
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

        /// <summary>Records one visible frame. Tick is the simulation tick, not Time.deltaTime.</summary>
        public void ObserveBuildings(long tick, IReadOnlyList<BuildingView> views, uint ownFaction)
        {
            if (views == null) return;
            for (int i = 0; i < views.Count; i++)
            {
                var view = views[i];
                if (view.FactionId != ownFaction) continue;
                BuildingState state;
                if (!buildings.TryGetValue(view.Id, out state))
                {
                    state = new BuildingState { LastProgress = view.Progress };
                    buildings.Add(view.Id, state);
                }

                bool active = state.LastTick != long.MinValue && view.Progress > state.LastProgress;
                // A newly observed completed building has no previous frame to compare with; do not invent work.
                if (state.LastTick == long.MinValue) active = false;
                state.Samples.Enqueue(new Sample(tick, active));
                state.LastProgress = view.Progress;
                state.LastTick = tick;
                Trim(state.Samples, tick);
            }
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
