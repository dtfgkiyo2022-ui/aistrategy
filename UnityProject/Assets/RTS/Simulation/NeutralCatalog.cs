using System;
using System.Collections.Generic;
using Rts.Contracts;

namespace Rts.Simulation
{
    internal static class NeutralCatalog
    {
        internal static NeutralCampDefinition[] Build(ScenarioDefinition scenario)
        {
            if (scenario.NeutralRules.Camps != null && scenario.NeutralRules.Camps.Length > 0)
                return (NeutralCampDefinition[])scenario.NeutralRules.Camps.Clone();
            int width = scenario.Map.WidthMeters, height = scenario.Map.HeightMeters;
            int n = (int)(scenario.Seed % 5UL);
            int mid = width / 2;
            var defs = new List<NeutralCampDefinition>(18);
            uint id = 1;
            AddPair(defs, ref id, NeutralKind.WolfPack, width, height, 30 + n, height / 4);
            AddPair(defs, ref id, NeutralKind.WolfPack, width, height, 42 + n, height * 3 / 4);
            AddPair(defs, ref id, NeutralKind.WolfPack, width, height, 66 + n, height / 2 - 18);
            AddPair(defs, ref id, NeutralKind.WolfPack, width, height, 66 + n, height / 2 + 18);
            AddPair(defs, ref id, NeutralKind.RockBeast, width, height, 86 + n, height / 3);
            AddPair(defs, ref id, NeutralKind.RockBeast, width, height, 86 + n, height * 2 / 3);
            AddPair(defs, ref id, NeutralKind.FlameHornBeast, width, height, width * 3 / 10, height / 2 - 16);
            AddPair(defs, ref id, NeutralKind.AzureSerpent, width, height, width * 3 / 10, height / 2 + 16);
            defs.Add(new NeutralCampDefinition { Id = id++, Kind = NeutralKind.ShellBeast, Position = new SimPoint(Fix64.FromInt(mid - 12), Fix64.FromInt(height / 2)) });
            defs.Add(new NeutralCampDefinition { Id = id++, Kind = NeutralKind.ShellBeast, Position = new SimPoint(Fix64.FromInt(mid + 12), Fix64.FromInt(height / 2)) });
            return defs.ToArray();
        }

        private static void AddPair(List<NeutralCampDefinition> result, ref uint id, NeutralKind kind,
            int width, int height, int westX, int z)
        {
            int eastX = width - westX;
            result.Add(new NeutralCampDefinition { Id = id++, Kind = kind, Position = new SimPoint(Fix64.FromInt(westX), Fix64.FromInt(Math.Max(4, Math.Min(height - 4, z)))) });
            result.Add(new NeutralCampDefinition { Id = id++, Kind = kind, Position = new SimPoint(Fix64.FromInt(eastX), Fix64.FromInt(Math.Max(4, Math.Min(height - 4, z)))) });
        }

        internal static void Stats(NeutralKind kind, out int count, out int hp, out int ad, out int interval,
            out int range, out int armor, out int mr)
        {
            switch (kind)
            {
                case NeutralKind.WolfPack: count = 3; hp = 180; ad = 8; interval = 20; range = 2; armor = 10; mr = 10; break;
                case NeutralKind.RockBeast: count = 1; hp = 600; ad = 14; interval = 30; range = 2; armor = 30; mr = 10; break;
                case NeutralKind.FlameHornBeast:
                case NeutralKind.AzureSerpent: count = 1; hp = 1400; ad = 30; interval = 30; range = 2; armor = 30; mr = 20; break;
                default: count = 1; hp = 900; ad = 0; interval = 30; range = 2; armor = 40; mr = 40; break;
            }
        }

        internal static int InitialDelay(NeutralRules rules, NeutralKind kind)
            => kind == NeutralKind.WolfPack ? rules.WolfInitialDelayTicks
            : kind == NeutralKind.RockBeast ? rules.RockInitialDelayTicks
            : kind == NeutralKind.ShellBeast ? rules.ShellInitialDelayTicks : rules.BlessingInitialDelayTicks;

        internal static int RespawnDelay(NeutralRules rules, NeutralKind kind)
            => kind == NeutralKind.WolfPack ? rules.WolfRespawnTicks
            : kind == NeutralKind.RockBeast ? rules.RockRespawnTicks
            : kind == NeutralKind.ShellBeast ? rules.ShellRespawnTicks : rules.BlessingRespawnTicks;

        internal static int SkillInterval(NeutralKind kind)
            => kind == NeutralKind.RockBeast ? 200
            : kind == NeutralKind.FlameHornBeast || kind == NeutralKind.AzureSerpent ? 240 : int.MaxValue;
    }
}
