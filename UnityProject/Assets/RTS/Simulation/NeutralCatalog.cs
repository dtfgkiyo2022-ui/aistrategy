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
            {
                var authored = new List<NeutralCampDefinition>(scenario.NeutralRules.Camps);
                bool hasGuardian = false;
                for (int i = 0; i < authored.Count; i++)
                    if (authored[i].Kind == NeutralKind.AugmentGuardian) { hasGuardian = true; break; }
                if (scenario.Economy.Augments && !hasGuardian)
                    authored.Add(new NeutralCampDefinition { Id = authored.Count == 0 ? 1U : authored[authored.Count - 1].Id + 1,
                        Kind = NeutralKind.AugmentGuardian, Position = new SimPoint(Fix64.FromInt(scenario.Map.WidthMeters / 2), Fix64.FromInt(scenario.Map.HeightMeters / 2)) });
                AppendGreatCamps(scenario, authored);
                return authored.ToArray();
            }
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
            if (scenario.Economy.Augments)
                defs.Add(new NeutralCampDefinition { Id = id++, Kind = NeutralKind.AugmentGuardian, Position = new SimPoint(Fix64.FromInt(mid), Fix64.FromInt(height / 2)) });
            if (scenario.Economy.GreatNeutrals)
            {
                defs.Add(new NeutralCampDefinition { Id = id++, Kind = NeutralKind.Dragon, Position = new SimPoint(Fix64.FromInt(mid), Fix64.FromInt(height / 2 - 40)) });
                AddPair(defs, ref id, NeutralKind.SiegeBeast, width, height, width * 2 / 5, height / 2 - 30);
                defs.Add(new NeutralCampDefinition { Id = id++, Kind = NeutralKind.GreatLord, Position = new SimPoint(Fix64.FromInt(mid), Fix64.FromInt(height / 2 + 40)) });
            }
            return defs.ToArray();
        }

        private static void AppendGreatCamps(ScenarioDefinition scenario, List<NeutralCampDefinition> camps)
        {
            if (!scenario.Economy.GreatNeutrals) return;
            bool dragon = false, siege = false, lord = false;
            for (int i = 0; i < camps.Count; i++)
            {
                dragon |= camps[i].Kind == NeutralKind.Dragon;
                siege |= camps[i].Kind == NeutralKind.SiegeBeast;
                lord |= camps[i].Kind == NeutralKind.GreatLord;
            }
            uint id = camps.Count == 0 ? 1U : camps[camps.Count - 1].Id + 1;
            int mid = scenario.Map.WidthMeters / 2, half = scenario.Map.HeightMeters / 2;
            if (!dragon) camps.Add(new NeutralCampDefinition { Id = id++, Kind = NeutralKind.Dragon, Position = new SimPoint(Fix64.FromInt(mid), Fix64.FromInt(half - 40)) });
            if (!siege) { camps.Add(new NeutralCampDefinition { Id = id++, Kind = NeutralKind.SiegeBeast, Position = new SimPoint(Fix64.FromInt(scenario.Map.WidthMeters * 2 / 5), Fix64.FromInt(half - 30)) });
                camps.Add(new NeutralCampDefinition { Id = id++, Kind = NeutralKind.SiegeBeast, Position = new SimPoint(Fix64.FromInt(scenario.Map.WidthMeters * 3 / 5), Fix64.FromInt(half - 30)) }); }
            if (!lord) camps.Add(new NeutralCampDefinition { Id = id, Kind = NeutralKind.GreatLord, Position = new SimPoint(Fix64.FromInt(mid), Fix64.FromInt(half + 40)) });
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
                case NeutralKind.AugmentGuardian: count = 1; hp = 2500; ad = 40; interval = 30; range = 3; armor = 40; mr = 40; break;
                case NeutralKind.Dragon: count = 1; hp = 2200; ad = 35; interval = 30; range = 3; armor = 35; mr = 35; break;
                case NeutralKind.SiegeBeast: count = 1; hp = 1200; ad = 20; interval = 30; range = 2; armor = 50; mr = 20; break;
                case NeutralKind.GreatLord: count = 1; hp = 6000; ad = 60; interval = 30; range = 4; armor = 60; mr = 60; break;
                default: count = 1; hp = 900; ad = 0; interval = 30; range = 2; armor = 40; mr = 40; break;
            }
        }

        internal static int InitialDelay(NeutralRules rules, NeutralKind kind)
            => kind == NeutralKind.GreatLord ? 9000
            : kind == NeutralKind.Dragon ? 3600
            : kind == NeutralKind.SiegeBeast ? 2400
            : kind == NeutralKind.AugmentGuardian ? 6000
            : kind == NeutralKind.WolfPack ? rules.WolfInitialDelayTicks
            : kind == NeutralKind.RockBeast ? rules.RockInitialDelayTicks
            : kind == NeutralKind.ShellBeast ? rules.ShellInitialDelayTicks : rules.BlessingInitialDelayTicks;

        internal static int RespawnDelay(NeutralRules rules, NeutralKind kind)
            => kind == NeutralKind.GreatLord ? 6000
            : kind == NeutralKind.Dragon ? 3600
            : kind == NeutralKind.SiegeBeast ? 4800
            : kind == NeutralKind.AugmentGuardian ? 6000
            : kind == NeutralKind.WolfPack ? rules.WolfRespawnTicks
            : kind == NeutralKind.RockBeast ? rules.RockRespawnTicks
            : kind == NeutralKind.ShellBeast ? rules.ShellRespawnTicks : rules.BlessingRespawnTicks;

        internal static int SkillInterval(NeutralKind kind)
            => kind == NeutralKind.GreatLord ? 240
            : kind == NeutralKind.Dragon ? 240
            : kind == NeutralKind.SiegeBeast ? 200
            : kind == NeutralKind.AugmentGuardian ? 300
            : kind == NeutralKind.RockBeast ? 200
            : kind == NeutralKind.FlameHornBeast || kind == NeutralKind.AzureSerpent ? 240 : int.MaxValue;

        internal static DragonElement DragonElementFor(ulong seed, int spawnCount)
        {
            if (spawnCount < 1) return DragonElement.None;
            DragonElement previous = DragonElement.None;
            for (int count = 1; count <= spawnCount; count++)
            {
                ulong x = seed + unchecked((ulong)count) * 0x9E3779B97F4A7C15UL;
                x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL; x ^= x >> 27; x *= 0x94D049BB133111EBUL; x ^= x >> 31;
                int value = (int)(x % 4UL) + 1;
                var element = (DragonElement)value;
                if (element == previous) element = (DragonElement)(value % 4 + 1);
                previous = element;
            }
            return previous;
        }
    }
}
