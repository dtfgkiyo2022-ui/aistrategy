using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    public static class WeekTwoScenario
    {
        /// <summary>Chapter 5.1 reference layout. Replay stores the generated definitions.</summary>
        public static ScenarioDefinition Create()
        {
            var s = WeekOneScenario.Create();
            s.ScenarioId = "week2-2routes";
            var blocked = new List<int>();
            for (int z = 0; z < 64; z++)
                for (int x = 0; x < 128; x++)
                {
                    int mx = x * 2 + 1, mz = z * 2 + 1;
                    if (!((mz >= 88 && mz < 104) || (mz >= 24 && mz < 40)
                        || (mx >= 16 && mx < 32) || (mx >= 224 && mx < 240))) blocked.Add(z * 128 + x);
                }
            s.Map.BlockedCellIds = blocked.ToArray();
            s.Soldiers = new SoldierDefinition[44];
            int[] counts = { 8, 8, 2, 2 }, zs = { 96, 32, 64, 64 };
            uint id = 1;
            for (uint f = 1; f <= 2; f++)
                for (int a = 0; a < 4; a++)
                {
                    uint army = (f - 1) * 4 + (uint)a + 1;
                    if (a < 2) s.Armies[army - 1].HomeObjective = new PolicyGoal(GoalKind.Outpost, (uint)a + 1, default);
                    for (int i = 0; i < counts[a]; i++)
                    {
                        int x = (a == 3 ? 28 : 24) + i % 4;
                        s.Soldiers[id - 1] = new SoldierDefinition { Id = id, FactionId = f, ArmyId = army,
                            Kind = a == 3 ? UnitKind.Scout : UnitKind.Infantry, Alive = true, Hp = a == 3 ? 40 : 100,
                            Position = new SimPoint(Fix64.FromInt(f == 1 ? x : 256 - x), Fix64.FromInt(zs[a] + i / 4)) };
                        id++;
                    }
                }
            // Sentries are appended (41..44) so that no existing soldier ID shifts.
            for (uint f = 1; f <= 2; f++)
                for (int i = 0; i < 2; i++)
                {
                    s.Soldiers[id - 1] = new SoldierDefinition { Id = id, FactionId = f, ArmyId = 8 + f,
                        Kind = UnitKind.Sentry, Alive = true, Hp = 800, Position = WeekOneScenario.SentryPosition(f, i) };
                    id++;
                }
            return s;
        }
    }
}
