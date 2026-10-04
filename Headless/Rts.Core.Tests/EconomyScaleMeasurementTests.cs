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
    /// <summary>
    /// S-4b measurement only. This deliberately lives in the test assembly: the simulation has no counters or
    /// measurement branches. It classifies the public state immediately before each 20-tick economy decision.
    /// </summary>
    public sealed class EconomyScaleMeasurementTests
    {
        private const int TicksPerMinute = 1200;
        private const int DecisionInterval = 20;
        private const int MaxTicks = 24000;

        [Test, Explicit("measurement"), Category("Measurement")]
        public void MeasureEconomyScaleReasonsToCsv()
        {
            string output = Environment.GetEnvironmentVariable("ECON_SCALE_MEASURE_OUT")
                ?? Path.Combine(FindRepositoryRoot(), "artifacts", "econscale2", "cause.csv");
            string directory = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var rows = new List<MeasurementRow>();
            for (ulong seed = 1; seed <= 10; seed++) rows.AddRange(Measure(seed));

            using (var writer = new StreamWriter(output, false, new UTF8Encoding(false)))
            {
                writer.WriteLine(MeasurementRow.Header);
                foreach (var row in rows) writer.WriteLine(row.Csv());
            }
            TestContext.WriteLine("economy scale cause measurement: " + output + ", rows=" + rows.Count.ToString(CultureInfo.InvariantCulture));
            Assert.That(rows.Count, Is.GreaterThan(0));
        }

        private static List<MeasurementRow> Measure(ulong seed)
        {
            var scenario = MapGenerator.GenerateTerrain(seed);
            scenario.Economy.ArmyGrowth = true;
            scenario.Economy.EconomyScale = true;
            var simulation = new Battle(scenario);
            var result = new List<MeasurementRow>();
            var previous = Fields(simulation);
            var nodes = scenario.ResourceNodes.ToDictionary(value => value.Id, value => value.Kind);
            var capacity = scenario.Armies.ToDictionary(value => value.Id, value => value.Capacity);
            var minuteRows = new Dictionary<Tuple<int, int>, MeasurementRow>();

            for (long tick = 1; tick <= MaxTicks && !simulation.Capture(1).Result.HasEnded; tick++)
            {
                if (tick % DecisionInterval == 0)
                {
                    var current = Fields(simulation);
                    int minute = (int)((tick - 1) / TicksPerMinute) + 1;
                    for (int faction = 1; faction <= 2; faction++)
                    {
                        var frame = simulation.Capture((uint)faction);
                        var row = GetRow(minuteRows, seed, faction, minute);
                        ClassifyVillagerTraining(row, frame, scenario.Economy);
                        ClassifyInfantryTraining(row, frame, scenario, capacity);
                        CountWorkers(row, faction, current, nodes);
                    }
                    CountIncome(minuteRows, seed, previous, current, nodes);
                    previous = current;
                }
                simulation.Step(tick, Array.Empty<ScheduledInput>());
            }
            result.AddRange(minuteRows.Values.OrderBy(value => value.Seed).ThenBy(value => value.Faction).ThenBy(value => value.Minute));
            return result;
        }

        private static MeasurementRow GetRow(Dictionary<Tuple<int, int>, MeasurementRow> rows, ulong seed, int faction, int minute)
        {
            var key = Tuple.Create(faction, minute);
            if (!rows.TryGetValue(key, out var row))
            {
                row = new MeasurementRow { Seed = seed, Faction = faction, Minute = minute };
                rows.Add(key, row);
            }
            return row;
        }

        private static Dictionary<string, string> Fields(Battle simulation)
            => DiagnosticComparison.Fields(simulation.CaptureDiagnostic()).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        private static void ClassifyVillagerTraining(MeasurementRow row, FactionFrame frame, EconomyRules rules)
        {
            var economy = frame.Economy;
            if (economy == null || !economy.AutoEconomy) return;
            int villagers = economy.Villagers.Count(value => value.IsOwn);
            int queued = QueuedPopulation(economy);
            int target = Rts.Decision.EconomyDecision.VillagerTarget(true, economy.Age, rules.AutoVillagerTarget, false);
            if (economy.CorePlayerHeld) row.VillagerCoreHeld++;
            else if (economy.AdvanceRemaining > 0) row.VillagerAdvancing++;
            else if (economy.VillagerQueued > 0) row.VillagerCoreQueue++;
            else if (villagers >= target) row.VillagerTarget++;
            else if (economy.Food < economy.VillagerFoodCost) row.VillagerFood++;
            else if (economy.Population + queued >= economy.PopulationCap) row.VillagerPopulation++;
            else row.VillagerTrainable++;
        }

        private static void ClassifyInfantryTraining(MeasurementRow row, FactionFrame frame, ScenarioDefinition scenario,
            IReadOnlyDictionary<uint, int> capacity)
        {
            var economy = frame.Economy;
            if (economy == null || !economy.AutoEconomy) return;
            var barracks = economy.Buildings.Where(value => value.FactionId == frame.FactionId && value.Kind == BuildingKind.Barracks).ToArray();
            int villagers = economy.Villagers.Count(value => value.IsOwn);
            int desired = Math.Min(4, Math.Max(1, villagers / 15));
            if (barracks.Length < desired) row.BarracksShortage++;
            var ready = barracks.Where(value => value.Complete && !value.PlayerHeld).ToArray();
            if (ready.Length == 0) return;

            int queuedPopulation = QueuedPopulation(economy);
            int population = economy.Population + queuedPopulation;
            int queueTarget = Math.Min(2, scenario.Economy.QueueLimit);
            bool armyRoom = HasInfantryRoom(frame, economy, capacity);
            bool saving = economy.Ages && economy.Civ == CivKind.Primitive && economy.AdvanceRemaining == 0
                && ready.Length > 0 && villagers >= 8;
            foreach (var building in ready)
            {
                if (building.Queued >= queueTarget) row.BarracksQueueFull++;
                else if (economy.Food < economy.InfantryFoodCost) row.InfantryFood++;
                else if (economy.Wood < economy.InfantryWoodCost) row.InfantryWood++;
                else if (population >= economy.PopulationCap) row.InfantryPopulation++;
                else if (!armyRoom) row.InfantryArmyRoom++;
                else if (economy.Metal < economy.InfantryMetalCost) row.InfantryMetal++;
                else if (saving) row.InfantrySaving++;
                else row.InfantryTrainable++;
            }
        }

        private static bool HasInfantryRoom(FactionFrame frame, EconomyView economy, IReadOnlyDictionary<uint, int> capacities)
        {
            int free = 0;
            foreach (var army in frame.Observation.OwnArmies)
            {
                if (army.Kind == UnitKind.Scout) continue;
                int cap = capacities.TryGetValue(army.Id, out var authored) ? authored : 12;
                free += cap - army.AliveCount;
            }
            int queued = economy.Buildings.Where(value => value.FactionId == frame.FactionId && value.Kind != BuildingKind.Town).Sum(value => value.Queued);
            return free > queued || frame.Observation.OwnArmies.Count(value => value.Kind != UnitKind.Scout) < 12;
        }

        private static int QueuedPopulation(EconomyView economy)
            => economy.VillagerQueued + economy.Buildings.Where(value => value.FactionId != 0).Sum(value => value.Queued);

        private static void CountWorkers(MeasurementRow row, int faction, IReadOnlyDictionary<string, string> fields,
            IReadOnlyDictionary<uint, ResourceKind> nodes)
        {
            foreach (var pair in fields.Where(value => value.Key.StartsWith("Villagers[", StringComparison.Ordinal) && value.Key.EndsWith("].FactionId", StringComparison.Ordinal)))
            {
                if (!int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var owner) || owner != faction) continue;
                uint id = ParseId(pair.Key);
                if (!fields.TryGetValue("Villagers[" + id.ToString(CultureInfo.InvariantCulture) + "].NodeId", out var nodeText)
                    || !uint.TryParse(nodeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nodeId)
                    || !nodes.TryGetValue(nodeId, out var kind)) continue;
                if (!fields.TryGetValue("Villagers[" + id.ToString(CultureInfo.InvariantCulture) + "].Task", out var taskText)
                    || !int.TryParse(taskText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var task)
                    || task < 1 || task > 3) continue;
                row.AddWorker(kind);
            }
        }

        private static void CountIncome(Dictionary<Tuple<int, int>, MeasurementRow> rows, ulong seed,
            IReadOnlyDictionary<string, string> previous, IReadOnlyDictionary<string, string> current,
            IReadOnlyDictionary<uint, ResourceKind> nodes)
        {
            foreach (var node in nodes)
            {
                string key = "ResourceNodes[" + node.Key.ToString(CultureInfo.InvariantCulture) + "].Remaining";
                if (!previous.TryGetValue(key, out var oldText) || !current.TryGetValue(key, out var newText)
                    || !int.TryParse(oldText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var oldValue)
                    || !int.TryParse(newText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var newValue)) continue;
                int amount = oldValue - newValue;
                if (amount <= 0) continue;
                int minute = (int)((previous.TryGetValue("Tick", out var tickText) && long.TryParse(tickText, out var tick) ? tick : 0) / TicksPerMinute) + 1;
                // Resource collection is faction-local in the terrain map. Use the worker visible at the interval end;
                // an empty assignment means the point was exhausted during the interval and is intentionally unassigned.
                int faction = AssignedFaction(current, node.Key);
                if (faction == 0) faction = AssignedFaction(previous, node.Key);
                if (faction == 0) continue;
                var row = GetRow(rows, seed, faction, minute);
                row.AddIncome(node.Value, amount);
            }
        }

        private static int AssignedFaction(IReadOnlyDictionary<string, string> fields, uint nodeId)
        {
            foreach (var pair in fields.Where(value => value.Key.StartsWith("Villagers[", StringComparison.Ordinal) && value.Key.EndsWith("].NodeId", StringComparison.Ordinal)))
            {
                if (!uint.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var assigned) || assigned != nodeId) continue;
                uint id = ParseId(pair.Key);
                string taskKey = "Villagers[" + id.ToString(CultureInfo.InvariantCulture) + "].Task";
                if (!fields.TryGetValue(taskKey, out var task) || task != "2") continue;
                string factionKey = "Villagers[" + id.ToString(CultureInfo.InvariantCulture) + "].FactionId";
                if (fields.TryGetValue(factionKey, out var factionText) && int.TryParse(factionText, out var faction)) return faction;
            }
            return 0;
        }

        private static uint ParseId(string field)
        {
            int start = field.IndexOf('[', StringComparison.Ordinal) + 1;
            int end = field.IndexOf(']', start);
            return uint.Parse(field.Substring(start, end - start), CultureInfo.InvariantCulture);
        }

        private static string FindRepositoryRoot()
        {
            for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Headless", "Rts.Headless.slnx"))) return directory.FullName;
            return Directory.GetCurrentDirectory();
        }

        private sealed class MeasurementRow
        {
            internal const string Header = "seed,faction,minute,infantry_food,infantry_wood,infantry_metal,barracks_queue_full,barracks_shortage,infantry_population,infantry_army_room,infantry_saving,infantry_trainable,villager_food,villager_core_queue,villager_core_held,villager_advancing,villager_target,villager_population,villager_trainable,workers_food,workers_wood,workers_stone,workers_ore,workers_gold,workers_metal,income_food,income_wood,income_stone,income_ore,income_gold,income_metal";
            internal ulong Seed;
            internal int Faction;
            internal int Minute;
            internal int InfantryFood, InfantryWood, InfantryMetal, BarracksQueueFull, BarracksShortage, InfantryPopulation, InfantryArmyRoom, InfantrySaving, InfantryTrainable;
            internal int VillagerFood, VillagerCoreQueue, VillagerCoreHeld, VillagerAdvancing, VillagerTarget, VillagerPopulation, VillagerTrainable;
            private readonly int[] workers = new int[6];
            private readonly int[] income = new int[6];

            internal void AddWorker(ResourceKind kind) => workers[ResourceColumn(kind)]++;
            internal void AddIncome(ResourceKind kind, int amount) => income[ResourceColumn(kind)] += amount;

            internal string Csv() => string.Join(",", Seed.ToString(CultureInfo.InvariantCulture), Faction.ToString(CultureInfo.InvariantCulture), Minute.ToString(CultureInfo.InvariantCulture),
                InfantryFood, InfantryWood, InfantryMetal, BarracksQueueFull, BarracksShortage, InfantryPopulation, InfantryArmyRoom, InfantrySaving, InfantryTrainable,
                VillagerFood, VillagerCoreQueue, VillagerCoreHeld, VillagerAdvancing, VillagerTarget, VillagerPopulation, VillagerTrainable,
                workers[0], workers[1], workers[2], workers[3], workers[4], workers[5], income[0], income[1], income[2], income[3], income[4], income[5]);

            private static int ResourceColumn(ResourceKind kind) => kind == ResourceKind.Food ? 0 : kind == ResourceKind.Wood ? 1 : kind == ResourceKind.Stone ? 2 : kind == ResourceKind.Ore ? 3 : kind == ResourceKind.Gold ? 4 : kind == ResourceKind.Metal ? 5 : 0;
        }
    }
}
