using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.UnityHost
{
    /// <summary>Placeholder frames for display work before the simulation is wired in. Integer-only, tick-driven.</summary>
    public sealed class MockFrameSource : IFrameSource
    {
        private const int UnitsPerSide = 10;
        private const long MillimetersPerTick = 100;
        private const long TravelMillimeters = 90000;
        private const int CoreMaxHp = 3000;
        private const int WidthCells = 128;
        private const int HeightCells = 64;
        private const long CellMillimeters = 2000;
        private const long UnitVisionMillimeters = 20000;
        private const long ObjectiveVisionMillimeters = 24000;

        private long tick;
        private readonly bool[] explored = new bool[WidthCells * HeightCells];
        private readonly Dictionary<uint, long[]> lastSeen = new Dictionary<uint, long[]>();

        public Func<IReadOnlyList<CommandView>> CommandProvider { get; set; }

        public long Tick { get { return tick; } }

        public void Advance() { tick++; }

        public FactionFrame Latest(uint factionId)
        {
            long travel = Bounce(tick * MillimetersPerTick);
            var own = new List<RenderUnit>();
            var enemyPositions = new List<KeyValuePair<uint, long[]>>();
            for (uint i = 0; i < UnitsPerSide; i++)
            {
                own.Add(Unit(1 + i, true, 30000 + travel, 40000 + i * 5000));
                enemyPositions.Add(new KeyValuePair<uint, long[]>(101 + i, new[] { 226000 - travel, 40000 + i * 5000L }));
            }
            long scoutX = 60000 + Bounce(tick * MillimetersPerTick * 2);
            own.Add(new RenderUnit(200, true, UnitKind.Scout, Point(scoutX, 64000), true, false, false, true, 40));

            // Vision: own units, the own core, and owned outposts. Enemies are only visible inside these cells.
            var sources = new List<long[]>();
            foreach (var u in own) sources.Add(new[] { u.Position.X.Raw * 1000 / 65536, u.Position.Z.Raw * 1000 / 65536, UnitVisionMillimeters });
            sources.Add(new[] { 24000L, 64000L, ObjectiveVisionMillimeters });
            sources.Add(new[] { 128000L, 96000L, ObjectiveVisionMillimeters });
            var visible = new bool[WidthCells * HeightCells];
            foreach (var s in sources)
            {
                long r2 = s[2] * s[2];
                for (int cz = 0; cz < HeightCells; cz++)
                    for (int cx = 0; cx < WidthCells; cx++)
                    {
                        long dx = (cx * CellMillimeters + CellMillimeters / 2) - s[0];
                        long dz = (cz * CellMillimeters + CellMillimeters / 2) - s[1];
                        if (dx * dx + dz * dz <= r2) visible[cz * WidthCells + cx] = true;
                    }
            }
            for (int i = 0; i < visible.Length; i++) if (visible[i]) explored[i] = true;

            var units = new List<RenderUnit>(own);
            int visibleEnemies = 0;
            foreach (var pair in enemyPositions)
            {
                long xMm = pair.Value[0], zMm = pair.Value[1];
                if (!visible[(int)(zMm / CellMillimeters) * WidthCells + (int)(xMm / CellMillimeters)]) continue;
                units.Add(Unit(pair.Key, false, xMm, zMm));
                lastSeen[pair.Key] = new[] { xMm, zMm, tick };
                visibleEnemies++;
            }

            var contacts = new List<EnemyContact>();
            foreach (var pair in lastSeen)
            {
                long age = tick - pair.Value[2];
                bool visibleNow = age == 0;
                bool unknown = age >= 600;
                contacts.Add(new EnemyContact(pair.Key - 100, Point(pair.Value[0], pair.Value[1]), pair.Value[2],
                    unknown ? -1 : 1, unknown ? -1 : 1, visibleNow, null, age >= 200, unknown));
            }
            if (visibleEnemies > 0)
            {
                int k = (visibleEnemies + 4) / 5;
                contacts.Add(new EnemyContact(1, Point(190000, 64000), tick, 5 * (k - 1) + 1, 5 * k, true, null, false, false, 10, false, true));
            }

            int hp = CoreMaxHp - (int)((tick / 2) % CoreMaxHp);
            var objectives = new List<KnownObjective>
            {
                new KnownObjective(GoalKind.Core, 1, Point(24000, 64000), true, 1, true, hp, tick),
                new KnownObjective(GoalKind.Core, 2, Point(232000, 64000), true, 2, true, CoreMaxHp, tick),
            };
            objectives.Add(new KnownObjective(GoalKind.Outpost, 1, Point(128000, 96000), true, 1, false, 0, tick, 0, 0, 0));
            long phase = tick % 400;
            objectives.Add(new KnownObjective(GoalKind.Outpost, 2, Point(128000, 32000), true, 0, false, 0, tick, 2, (int)(phase / 2), 200));
            var armies = new List<OwnArmyView>
            {
                new OwnArmyView(1, 1, UnitKind.Infantry, Point(30000 + travel, 47500), 5, default(PolicyGoal)),
                new OwnArmyView(2, 1, UnitKind.Infantry, Point(30000 + travel, 62500), 5, default(PolicyGoal)),
                new OwnArmyView(3, 1, UnitKind.Infantry, Point(28000, 64000), 2, default(PolicyGoal)),
                new OwnArmyView(4, 1, UnitKind.Scout, Point(scoutX, 64000), 1, default(PolicyGoal)),
            };
            var observation = new FactionObservation(factionId, tick,
                armies, Array.Empty<VisibleEnemy>(), contacts, objectives);
            return new FactionFrame(tick, factionId, units, observation,
                CommandProvider != null ? CommandProvider() : Array.Empty<CommandView>(), Array.Empty<GameEvent>(),
                new FogView(visible, (bool[])explored.Clone()), default(MatchResult));
        }

        private static long Bounce(long distance)
        {
            long p = distance % (2 * TravelMillimeters);
            return p <= TravelMillimeters ? p : 2 * TravelMillimeters - p;
        }

        private static RenderUnit Unit(uint id, bool own, long xMm, long zMm)
        {
            return new RenderUnit(id, own, UnitKind.Infantry, Point(xMm, zMm), true, false, false, own, 100);
        }

        private static SimPoint Point(long xMm, long zMm)
        {
            return new SimPoint(Fix64.FromRatio(xMm, 1000), Fix64.FromRatio(zMm, 1000));
        }
    }
}
