using System.Collections.Generic;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>
    /// Display-only record of recent hits and deaths (11-01: "hard to tell whether damage lands"). It is fed by the
    /// HP drops between two frames and never reaches the simulation. Times are match seconds, so a pause freezes it.
    /// </summary>
    public sealed class CombatFeedback
    {
        public const int MaxNumbers = 48;
        public const float NumberSeconds = 0.9f;
        /// <summary>Hits on one unit closer together than this add to the number already shown.</summary>
        public const float NumberMergeSeconds = 0.3f;
        public const float StructureNumberMergeSeconds = 0.5f;
        public const float BattleCellMeters = 48f;
        /// <summary>A battle with nothing new for this long is no longer marked.</summary>
        public const float BattleQuietSeconds = 12f;
        /// <summary>Fewer events than this is a skirmish, not marked (a lone scout trading blows).</summary>
        public const int BattleMinimumEvents = 6;
        public const int MaxBattles = 6;

        public sealed class Number
        {
            public ulong Key;
            public Vector3 Position;
            public int Amount;
            public float Age;
            public bool OwnVictim;
        }

        public sealed class Battle
        {
            public long Cell;
            public Vector3 Sum;
            public int Samples;
            public int OwnLost;
            public int EnemyLost;
            /// <summary>11-02: buildings and belt cells destroyed in this fight.</summary>
            public int OwnStructuresLost;
            public int EnemyStructuresLost;
            public int Events;
            public float Quiet;
            public Vector3 Center { get { return Samples == 0 ? Sum : Sum / Samples; } }
        }

        private readonly List<Number> numbers = new List<Number>();
        private readonly Dictionary<long, Battle> battles = new Dictionary<long, Battle>();
        private readonly List<Battle> ranked = new List<Battle>();
        private readonly List<long> expired = new List<long>();

        public IReadOnlyList<Number> Numbers { get { return numbers; } }

        public void RegisterHit(ulong key, Vector3 position, int damage, bool ownVictim)
        {
            RegisterHit(key, position, damage, ownVictim, NumberMergeSeconds);
        }

        public void RegisterStructureHit(ulong key, Vector3 position, int damage, bool ownVictim)
        {
            RegisterHit(key, position, damage, ownVictim, StructureNumberMergeSeconds);
        }

        private void RegisterHit(ulong key, Vector3 position, int damage, bool ownVictim, float mergeSeconds)
        {
            if (damage <= 0) return;
            AddToBattle(position, 0, 0);
            for (int i = numbers.Count - 1; i >= 0; i--)
            {
                var existing = numbers[i];
                if (existing.Key != key || existing.Age > mergeSeconds) continue;
                existing.Amount += damage;
                existing.Position = position;
                return;
            }
            if (numbers.Count >= MaxNumbers) numbers.RemoveAt(0);
            numbers.Add(new Number { Key = key, Position = position, Amount = damage, OwnVictim = ownVictim });
        }

        public void RegisterDeath(Vector3 position, bool ownVictim)
        {
            AddToBattle(position, ownVictim ? 1 : 0, ownVictim ? 0 : 1);
        }

        /// <summary>11-02: a building or belt cell that was being hit is gone.</summary>
        public void RegisterStructureLost(Vector3 position, bool ownVictim)
        {
            var battle = AddToBattle(position, 0, 0);
            if (ownVictim) battle.OwnStructuresLost++; else battle.EnemyStructuresLost++;
        }

        public void Advance(float matchSeconds)
        {
            if (matchSeconds <= 0f) return;
            for (int i = numbers.Count - 1; i >= 0; i--)
            {
                numbers[i].Age += matchSeconds;
                if (numbers[i].Age >= NumberSeconds) numbers.RemoveAt(i);
            }
            expired.Clear();
            foreach (var pair in battles)
            {
                pair.Value.Quiet += matchSeconds;
                if (pair.Value.Quiet >= BattleQuietSeconds) expired.Add(pair.Key);
            }
            foreach (var cell in expired) battles.Remove(cell);
        }

        /// <summary>The marked battles, the most eventful first (ties by cell, so the order does not flicker).</summary>
        public IReadOnlyList<Battle> MarkedBattles()
        {
            ranked.Clear();
            foreach (var battle in battles.Values)
                if (battle.Events >= BattleMinimumEvents) ranked.Add(battle);
            ranked.Sort((a, b) => a.Events != b.Events ? b.Events.CompareTo(a.Events) : a.Cell.CompareTo(b.Cell));
            if (ranked.Count > MaxBattles) ranked.RemoveRange(MaxBattles, ranked.Count - MaxBattles);
            return ranked;
        }

        public void Clear()
        {
            numbers.Clear();
            battles.Clear();
            ranked.Clear();
        }

        private Battle AddToBattle(Vector3 position, int ownLost, int enemyLost)
        {
            long cell = CellOf(position);
            if (!battles.TryGetValue(cell, out var battle))
            {
                battle = new Battle { Cell = cell };
                battles.Add(cell, battle);
            }
            battle.Sum += position;
            battle.Samples++;
            battle.OwnLost += ownLost;
            battle.EnemyLost += enemyLost;
            battle.Events++;
            battle.Quiet = 0f;
            return battle;
        }

        public static long CellOf(Vector3 position)
        {
            long x = Mathf.FloorToInt(position.x / BattleCellMeters);
            long z = Mathf.FloorToInt(position.z / BattleCellMeters);
            return (x << 32) ^ (z & 0xffffffffL);
        }
    }
}
