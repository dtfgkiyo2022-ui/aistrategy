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

        private long tick;

        public void Advance() { tick++; }

        public FactionFrame Latest(uint factionId)
        {
            var units = new List<RenderUnit>();
            for (uint i = 0; i < UnitsPerSide; i++)
            {
                units.Add(Unit(1 + i, true, 30000 + Bounce(tick * MillimetersPerTick), 40000 + i * 5000));
                units.Add(Unit(101 + i, false, 226000 - Bounce(tick * MillimetersPerTick), 40000 + i * 5000));
            }
            units.Add(new RenderUnit(200, true, UnitKind.Scout, Point(60000 + Bounce(tick * MillimetersPerTick * 2), 64000), true, false, false, true, 40));

            int hp = CoreMaxHp - (int)((tick / 2) % CoreMaxHp);
            var objectives = new List<KnownObjective>
            {
                new KnownObjective(GoalKind.Core, 1, Point(24000, 64000), true, 1, true, hp, tick),
                new KnownObjective(GoalKind.Core, 2, Point(232000, 64000), true, 2, true, CoreMaxHp, tick),
            };
            var armies = new List<OwnArmyView>
            {
                new OwnArmyView(1, 1, UnitKind.Infantry, Point(30000 + Bounce(tick * MillimetersPerTick), 47500), 5, default(PolicyGoal)),
                new OwnArmyView(2, 1, UnitKind.Infantry, Point(30000 + Bounce(tick * MillimetersPerTick), 62500), 5, default(PolicyGoal)),
            };
            var observation = new FactionObservation(factionId, tick,
                armies, Array.Empty<VisibleEnemy>(), Array.Empty<EnemyContact>(), objectives);
            return new FactionFrame(tick, factionId, units, observation,
                Array.Empty<CommandView>(), Array.Empty<GameEvent>(),
                new FogView(Array.Empty<bool>(), Array.Empty<bool>()), default(MatchResult));
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
