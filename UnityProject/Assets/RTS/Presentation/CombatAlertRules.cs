using System;
using System.Collections.Generic;

namespace Rts.Presentation
{
    /// <summary>
    /// Pure rules for the combat alerts (display only, 11-19). Finds where visible soldiers lost health between two
    /// frames, merges nearby hits into one fight, keeps a fight alive for a few seconds after its last hit, and names
    /// its direction from the own core. Nothing here goes back into the simulation.
    /// </summary>
    public static class CombatAlertRules
    {
        public const float MergeDistance = 30f;
        public const float LifetimeSeconds = 5f;

        public readonly struct HealthSample
        {
            public readonly uint Id;
            public readonly float X, Z;
            public readonly int Hp;
            public readonly bool IsOwn, IsVisible, IsSoldier;
            public HealthSample(uint id, float x, float z, int hp, bool isOwn, bool isVisible = true, bool isSoldier = true)
            { Id = id; X = x; Z = z; Hp = hp; IsOwn = isOwn; IsVisible = isVisible; IsSoldier = isSoldier; }
        }

        public readonly struct Alert
        {
            public readonly float X, Z, LastDamageSeconds;
            public readonly int OwnCount, EnemyCount;
            public readonly bool OwnUnderAttack;
            /// <summary>When the fight was first seen; the list is shown newest fight first.</summary>
            public readonly float StartSeconds;
            public Alert(float x, float z, float lastDamageSeconds, int ownCount, int enemyCount, bool ownUnderAttack)
                : this(x, z, lastDamageSeconds, ownCount, enemyCount, ownUnderAttack, lastDamageSeconds) { }
            public Alert(float x, float z, float lastDamageSeconds, int ownCount, int enemyCount, bool ownUnderAttack, float startSeconds)
            { X = x; Z = z; LastDamageSeconds = lastDamageSeconds; OwnCount = ownCount; EnemyCount = enemyCount; OwnUnderAttack = ownUnderAttack; StartSeconds = startSeconds; }
        }

        private static readonly Dictionary<uint, int> previousHp = new Dictionary<uint, int>();

        /// <summary>The fights found between two frames. Own soldiers that lost health make a defending fight.</summary>
        public static List<Alert> Detect(IList<HealthSample> previous, IList<HealthSample> current, float nowSeconds)
        {
            var result = new List<Alert>();
            if (current == null) return result;
            previousHp.Clear();
            if (previous != null) foreach (var sample in previous) previousHp[sample.Id] = sample.Hp;
            AddDamaged(result, current, nowSeconds, true);
            // A visible enemy taking damage is an attacking fight. It is intentionally limited to visible units.
            AddDamaged(result, current, nowSeconds, false);
            return result;
        }

        private static void AddDamaged(List<Alert> result, IList<HealthSample> current, float nowSeconds, bool own)
        {
            for (int i = 0; i < current.Count; i++)
            {
                var sample = current[i];
                if (!sample.IsVisible || !sample.IsSoldier || sample.IsOwn != own) continue;
                if (!previousHp.TryGetValue(sample.Id, out int oldHp) || sample.Hp >= oldHp) continue;
                int cluster = FindCluster(result, sample.X, sample.Z);
                if (cluster < 0) result.Add(new Alert(sample.X, sample.Z, nowSeconds, own ? 1 : 0, own ? 0 : 1, own));
                else result[cluster] = Merge(result[cluster], sample.X, sample.Z, nowSeconds, own, !own);
            }
        }

        /// <summary>
        /// Adds this frame's fights to the running list: a fight near one already listed refreshes it (it does not
        /// make a second line), a new fight goes to the front. Fights older than the lifetime are dropped.
        /// </summary>
        public static void Accumulate(List<Alert> running, IList<Alert> detected, float nowSeconds)
        {
            if (detected != null)
                foreach (var found in detected)
                {
                    int index = FindCluster(running, found.X, found.Z);
                    if (index < 0) { running.Insert(0, found); continue; }
                    var old = running[index];
                    running[index] = new Alert((old.X + found.X) * .5f, (old.Z + found.Z) * .5f, Math.Max(old.LastDamageSeconds, found.LastDamageSeconds),
                        found.OwnCount, found.EnemyCount, old.OwnUnderAttack || found.OwnUnderAttack, old.StartSeconds);
                }
            running.RemoveAll(alert => nowSeconds - alert.LastDamageSeconds >= LifetimeSeconds);
        }

        public static List<Alert> Live(IList<Alert> alerts, float nowSeconds)
        {
            var result = new List<Alert>();
            if (alerts == null) return result;
            foreach (var alert in alerts) if (nowSeconds - alert.LastDamageSeconds < LifetimeSeconds) result.Add(alert);
            return result;
        }

        /// <summary>One of 8 directions from the own core (each a 45 degree slice; north is +Z).</summary>
        public static string Direction(float coreX, float coreZ, float x, float z, bool japanese)
        {
            float dx = x - coreX, dz = z - coreZ;
            if (Math.Abs(dx) < 0.001f && Math.Abs(dz) < 0.001f) return japanese ? "本拠地" : "Home";
            double degrees = Math.Atan2(dx, dz) * 180.0 / Math.PI; // 0 = north, 90 = east
            int sector = (int)Math.Floor((degrees + 360.0 + 22.5) / 45.0) % 8;
            string[] ja = { "北", "北東", "東", "南東", "南", "南西", "西", "北西" };
            string[] en = { "North", "Northeast", "East", "Southeast", "South", "Southwest", "West", "Northwest" };
            return japanese ? ja[sector] : en[sector];
        }

        /// <summary>The one line shown for a fight.</summary>
        public static string Line(string direction, int own, int enemy, bool japanese)
            => japanese ? "⚔ " + direction + "で戦闘中（自分 " + own + " vs 相手 " + enemy + "）"
                : "⚔ Fighting " + direction + " (you " + own + " vs enemy " + enemy + ")";

        private static int FindCluster(List<Alert> alerts, float x, float z)
        { for (int i = 0; i < alerts.Count; i++) { float dx = alerts[i].X - x, dz = alerts[i].Z - z; if (dx * dx + dz * dz <= MergeDistance * MergeDistance) return i; } return -1; }
        private static Alert Merge(Alert a, float x, float z, float now, bool own, bool enemy)
        { return new Alert((a.X + x) * .5f, (a.Z + z) * .5f, Math.Max(a.LastDamageSeconds, now), a.OwnCount + (own ? 1 : 0), a.EnemyCount + (enemy ? 1 : 0), a.OwnUnderAttack || own, a.StartSeconds); }
    }
}
