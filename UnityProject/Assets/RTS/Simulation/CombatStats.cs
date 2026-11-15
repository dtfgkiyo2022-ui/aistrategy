using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>C12-1's provisional stat table. It is only consulted while CombatStats is enabled.</summary>
    internal static class CombatStatsDefaults
    {
        internal static void Apply(ref UnitParameters parameters, UnitKind kind, EconomyRules rules)
        {
            if (!rules.CombatStats || HasAuthoredCombatValues(parameters)) return;
            parameters.Armor = Armor(kind, rules);
            parameters.MagicResist = MagicResist(kind, rules);
        }

        internal static void ApplyDynamic(ref UnitParameters parameters, UnitKind kind, EconomyRules rules)
        {
            if (!rules.CombatStats) return;
            parameters.Armor = Armor(kind, rules);
            parameters.MagicResist = MagicResist(kind, rules);
        }

        internal static int Armor(UnitKind kind, EconomyRules rules)
            => kind == UnitKind.Infantry ? rules.CombatStatsInfantryArmor
                : kind == UnitKind.Archer ? rules.CombatStatsArcherArmor
                : kind == UnitKind.Cavalry || kind == UnitKind.LightCavalry ? rules.CombatStatsCavalryArmor
                : kind == UnitKind.Scout ? rules.CombatStatsScoutArmor
                : kind == UnitKind.Monk ? rules.CombatStatsMonkArmor
                : kind == UnitKind.HeavyInfantry ? rules.CombatStatsHeavyInfantryArmor
                : kind == UnitKind.HeavyCavalry ? rules.CombatStatsHeavyCavalryArmor
                : kind == UnitKind.Mercenary ? rules.CombatStatsMercenaryArmor
                : kind == UnitKind.Ram ? rules.CombatStatsRamArmor
                : kind == UnitKind.Cannon ? rules.CombatStatsCannonArmor
                : rules.CombatStatsVillagerArmor;

        internal static int MagicResist(UnitKind kind, EconomyRules rules)
            => kind == UnitKind.Infantry ? rules.CombatStatsInfantryMagicResist
                : kind == UnitKind.Archer ? rules.CombatStatsArcherMagicResist
                : kind == UnitKind.Cavalry || kind == UnitKind.LightCavalry ? rules.CombatStatsCavalryMagicResist
                : kind == UnitKind.Scout ? rules.CombatStatsScoutMagicResist
                : kind == UnitKind.Monk ? rules.CombatStatsMonkMagicResist
                : kind == UnitKind.HeavyInfantry ? rules.CombatStatsHeavyInfantryMagicResist
                : kind == UnitKind.HeavyCavalry ? rules.CombatStatsHeavyCavalryMagicResist
                : kind == UnitKind.Mercenary ? rules.CombatStatsMercenaryMagicResist
                : kind == UnitKind.Ram ? rules.CombatStatsRamMagicResist
                : kind == UnitKind.Cannon ? rules.CombatStatsCannonMagicResist
                : rules.CombatStatsVillagerMagicResist;

        internal static int BuildingArmor(BuildingKind kind, EconomyRules rules)
            => kind == BuildingKind.Wall || kind == BuildingKind.Gate ? rules.CombatStatsWallArmor
                : kind == BuildingKind.Tower ? rules.CombatStatsTowerArmor
                : rules.CombatStatsBuildingArmor;

        private static bool HasAuthoredCombatValues(UnitParameters p)
            => p.AbilityPower != 0 || p.Armor != 0 || p.MagicResist != 0
                || p.PhysicalPenetrationPermille != 0 || p.PhysicalFlatPenetration != 0
                || p.MagicPenetrationPermille != 0 || p.MagicFlatPenetration != 0
                || p.TenacityPermille != 0 || p.LifestealPermille != 0;
    }
}
