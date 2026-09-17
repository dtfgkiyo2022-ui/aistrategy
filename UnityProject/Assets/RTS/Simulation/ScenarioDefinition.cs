using System;
using Rts.Contracts;

namespace Rts.Simulation
{
    // Authoring DTOs only. Simulation validates and copies them; no JSON dependency.
    public sealed class ScenarioDefinition
    {
        public int SchemaVersion = 1;
        public string ScenarioId = "week1-10v10";
        public ulong Seed;
        public int TickRateHz = 20;
        public long VerificationTickLimit = 36000;
        public MapDefinition Map = new MapDefinition();
        public RuleDefinition Rules = new RuleDefinition();
        public UnitParameters[] UnitParameters = Array.Empty<UnitParameters>();
        public FactionDefinition[] Factions = Array.Empty<FactionDefinition>();
        public CoreDefinition[] Cores = Array.Empty<CoreDefinition>();
        public OutpostDefinition[] Outposts = Array.Empty<OutpostDefinition>();
        public ArmyDefinition[] Armies = Array.Empty<ArmyDefinition>();
        public SoldierDefinition[] Soldiers = Array.Empty<SoldierDefinition>();
    }

    public sealed class MapDefinition
    {
        public int WidthMeters = 256, HeightMeters = 128, CellSizeMeters = 2;
        public int WidthCells = 128, HeightCells = 64;
        public bool DefaultPassable = true;
        public int[] BlockedCellIds = Array.Empty<int>();
    }

    public sealed class RuleDefinition
    {
        public int FactionCap = 40;
        public Fix64 CoreRadius = Fix64.FromInt(4);
        public Fix64 OwnedObjectiveVision = Fix64.FromInt(24);
        public Fix64 CaptureRadius = Fix64.FromInt(8);
        public int CaptureDurationTicks = 200;
        public int CoreReinforcementIntervalTicks = 100, OutpostReinforcementIntervalTicks = 200;
        public int OccupationThreatMemoryTicks = 200;
        public ushort DefaultReservePermille = 100;
    }

    public struct UnitParameters
    {
        public UnitKind Kind;
        public int Hp, Damage, AttackIntervalTicks;
        public Fix64 Speed, Vision, Range;
    }

    public struct FactionDefinition
    {
        public uint Id, CoreId;
        public uint[] ArmyIds;
    }

    public struct CoreDefinition
    {
        public uint Id, FactionId;
        public SimPoint Position;
        public int Hp;
    }

    public struct OutpostDefinition
    {
        public uint Id, OwnerFactionId;
        public SimPoint Position;
    }

    public struct ArmyDefinition
    {
        public uint Id, FactionId;
        public string Role;
        public int Capacity;
        public PolicyGoal HomeObjective;
    }

    public struct SoldierDefinition
    {
        public uint Id, FactionId, ArmyId;
        public UnitKind Kind;
        public bool Alive;
        public SimPoint Position;
        public int Hp;
    }

    public static class WeekOneScenario
    {
        /// <summary>Same explicit values as TestData/week1-10v10.json. No JSON loading.</summary>
        public static ScenarioDefinition Create()
        {
            var s = new ScenarioDefinition
            {
                UnitParameters = new[]
                {
                    new UnitParameters { Kind = UnitKind.Infantry, Hp = 100, Speed = Fix64.FromInt(2),
                        Vision = Fix64.FromInt(20), Range = Fix64.FromInt(2), Damage = 10, AttackIntervalTicks = 20 },
                    new UnitParameters { Kind = UnitKind.Scout, Hp = 40, Speed = Fix64.FromInt(4),
                        Vision = Fix64.FromInt(32), Range = Fix64.FromInt(2), Damage = 2, AttackIntervalTicks = 20 },
                    // Immobile core guard (5.1). Speed 0 makes StepDistance 0; movement itself is unchanged.
                    new UnitParameters { Kind = UnitKind.Sentry, Hp = 800, Speed = Fix64.FromInt(0),
                        Vision = Fix64.FromInt(24), Range = Fix64.FromInt(8), Damage = 40, AttackIntervalTicks = 20 }
                },
                Factions = new FactionDefinition[2], Cores = new CoreDefinition[2],
                Armies = new ArmyDefinition[10], Soldiers = new SoldierDefinition[24],
                Outposts = new[]
                {
                    new OutpostDefinition { Id = 1, Position = Point(128, 96) },
                    new OutpostDefinition { Id = 2, Position = Point(128, 32) }
                }
            };
            string[] roles = { "north", "south", "reserve", "scout" };
            int[] capacities = { 16, 16, 6, 2 };
            for (uint f = 1; f <= 2; f++)
            {
                uint firstArmy = (f - 1) * 4 + 1;
                // Sentry armies are appended (9 and 10) so that no existing army ID shifts.
                uint sentryArmy = 8 + f;
                s.Factions[f - 1] = new FactionDefinition { Id = f, CoreId = f,
                    ArmyIds = new[] { firstArmy, firstArmy + 1, firstArmy + 2, firstArmy + 3, sentryArmy } };
                s.Cores[f - 1] = new CoreDefinition { Id = f, FactionId = f,
                    Position = Point(f == 1 ? 24 : 232, 64), Hp = 3000 };
                for (int a = 0; a < 4; a++)
                    s.Armies[firstArmy - 1 + a] = new ArmyDefinition { Id = firstArmy + (uint)a,
                        FactionId = f, Role = roles[a], Capacity = capacities[a],
                        HomeObjective = new PolicyGoal(GoalKind.Core, f, default) };
                s.Armies[sentryArmy - 1] = new ArmyDefinition { Id = sentryArmy, FactionId = f,
                    Role = "sentry", Capacity = 2, HomeObjective = new PolicyGoal(GoalKind.Core, f, default) };
                for (int i = 0; i < 10; i++)
                {
                    int a = i < 8 ? i / 4 : i - 6;
                    int x = i < 8 ? 24 + i % 4 : i == 8 ? 24 : 28;
                    uint id = (f - 1) * 10 + (uint)i + 1;
                    s.Soldiers[id - 1] = new SoldierDefinition { Id = id, FactionId = f,
                        ArmyId = firstArmy + (uint)a, Kind = i == 9 ? UnitKind.Scout : UnitKind.Infantry,
                        Alive = true, Position = Point(f == 1 ? x : 256 - x, i < 4 ? 96 : i < 8 ? 32 : 64),
                        Hp = i == 9 ? 40 : 100 };
                }
                for (int i = 0; i < 2; i++)
                {
                    uint id = 20 + (f - 1) * 2 + (uint)i + 1; // Appended: 21/22 west, 23/24 east.
                    s.Soldiers[id - 1] = new SoldierDefinition { Id = id, FactionId = f, ArmyId = sentryArmy,
                        Kind = UnitKind.Sentry, Alive = true, Position = SentryPosition(f, i), Hp = 800 };
                }
            }
            return s;
        }

        /// <summary>
        /// The two sentries stand in front of their core, on the enemy side. x is 30/226 rather
        /// than the 8 m point 32/224 because cell (32,z) is off-road on the 2-route map of 5.1;
        /// 30/226 is the nearest passable column and stays within 8 m of the core centre.
        /// </summary>
        internal static SimPoint SentryPosition(uint faction, int index)
            => Point(faction == 1 ? 30 : 226, index == 0 ? 60 : 68);

        private static SimPoint Point(int x, int z) => new SimPoint(Fix64.FromInt(x), Fix64.FromInt(z));
    }
}
