using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>霧を適用する前の、記録パック用の集計スナップショットです。</summary>
    public sealed class MatchPackSnapshot
    {
        public long Tick { get; internal set; }
        public MatchPackFactionSnapshot[] Factions { get; internal set; } = Array.Empty<MatchPackFactionSnapshot>();
        public MatchPackObjectiveSnapshot[] Cores { get; internal set; } = Array.Empty<MatchPackObjectiveSnapshot>();
        public MatchPackObjectiveSnapshot[] Outposts { get; internal set; } = Array.Empty<MatchPackObjectiveSnapshot>();
    }

    public sealed class MatchPackFactionSnapshot
    {
        public uint FactionId { get; internal set; }
        public int Villagers { get; internal set; }
        public int Food { get; internal set; }
        public int Wood { get; internal set; }
        public int Ore { get; internal set; }
        public int Metal { get; internal set; }
        public int Stone { get; internal set; }
        public int Gold { get; internal set; }
        public int Gems { get; internal set; }
        public string Civ { get; internal set; }
        public int Age { get; internal set; }
        public MatchPackCount[] Soldiers { get; internal set; } = Array.Empty<MatchPackCount>();
        public MatchPackCount[] Buildings { get; internal set; } = Array.Empty<MatchPackCount>();
    }

    public sealed class MatchPackObjectiveSnapshot
    {
        public uint Id { get; internal set; }
        public uint OwnerFactionId { get; internal set; }
    }

    public sealed class MatchPackCount
    {
        public string Kind { get; internal set; }
        public int Count { get; internal set; }
    }

    public sealed partial class Simulation
    {
        /// <summary>
        /// Returns a fog-free aggregate for post-match analysis. This is deliberately separate from Capture(), which
        /// remains faction-local and is the only view exposed to tactics.
        /// </summary>
        public MatchPackSnapshot CaptureMatchPackSnapshot()
        {
            var snapshot = new MatchPackSnapshot { Tick = world.Tick };
            snapshot.Factions = new MatchPackFactionSnapshot[world.Factions.Length];
            for (int f = 0; f < world.Factions.Length; f++)
            {
                var economy = world.Economies != null && f < world.Economies.Length ? world.Economies[f] : default(FactionEconomy);
                var faction = new MatchPackFactionSnapshot
                {
                    FactionId = world.Factions[f].Id,
                    Villagers = LivingVillagers(world.Factions[f].Id),
                    Food = economy.Food,
                    Wood = economy.Wood,
                    Ore = economy.Ore,
                    Metal = economy.Metal,
                    Stone = economy.Stone,
                    Gold = economy.Gold,
                    Gems = economy.Gems,
                    Civ = economy.Civ.ToString(),
                    Age = economy.Age,
                    Soldiers = CountSoldiers(world.Factions[f].Id),
                    Buildings = CountBuildings(world.Factions[f].Id)
                };
                snapshot.Factions[f] = faction;
            }
            snapshot.Cores = new MatchPackObjectiveSnapshot[world.Cores.Length];
            for (int i = 0; i < world.Cores.Length; i++)
                snapshot.Cores[i] = new MatchPackObjectiveSnapshot { Id = world.Cores[i].Definition.Id, OwnerFactionId = world.Cores[i].Definition.FactionId };
            snapshot.Outposts = new MatchPackObjectiveSnapshot[world.Outposts.Length];
            for (int i = 0; i < world.Outposts.Length; i++)
                snapshot.Outposts[i] = new MatchPackObjectiveSnapshot { Id = world.Outposts[i].Definition.Id, OwnerFactionId = world.Outposts[i].OwnerFactionId };
            return snapshot;
        }

        private MatchPackCount[] CountSoldiers(uint factionId)
        {
            var counts = new Dictionary<UnitKind, int>();
            foreach (UnitKind kind in Enum.GetValues(typeof(UnitKind))) counts[kind] = 0;
            for (int i = 0; i < world.SoldierCount; i++)
                if (world.Soldiers[i].Alive && world.Soldiers[i].Initial.FactionId == factionId) counts[world.Soldiers[i].Initial.Kind]++;
            var result = new MatchPackCount[counts.Count];
            int index = 0;
            foreach (var pair in counts)
                result[index++] = new MatchPackCount { Kind = pair.Key.ToString(), Count = pair.Value };
            return result;
        }

        private MatchPackCount[] CountBuildings(uint factionId)
        {
            var counts = new Dictionary<BuildingKind, int>();
            foreach (BuildingKind kind in Enum.GetValues(typeof(BuildingKind))) counts[kind] = 0;
            for (int i = 0; i < world.BuildingCount; i++)
                if (world.Buildings[i].Alive && world.Buildings[i].FactionId == factionId) counts[world.Buildings[i].Kind]++;
            var result = new MatchPackCount[counts.Count];
            int index = 0;
            foreach (var pair in counts)
                result[index++] = new MatchPackCount { Kind = pair.Key.ToString(), Count = pair.Value };
            return result;
        }
    }
}
