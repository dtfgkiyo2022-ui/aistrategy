using Rts.Contracts;

namespace Rts.Decision
{
    /// <summary>
    /// V3-5 counters (technical-design-v3 32 #13). One triangle among the line units: archers beat infantry, cavalry beats
    /// archers, infantry beats cavalry. Rams and scouts stand outside it, and a soldier of no class counts as infantry,
    /// so a map without ages (where no soldier has a class) keeps every number it had.
    /// </summary>
    public static class CombatMath
    {
        /// <summary>True when <paramref name="attacker"/> has the edge over <paramref name="target"/> in the triangle.</summary>
        public static bool Counters(UnitKind attacker, UnitKind target)
        {
            var a = Line(attacker);
            var t = Line(target);
            if (a == 0 || t == 0) return false;
            return (a == UnitKind.Archer && t == UnitKind.Infantry)
|| ((a == UnitKind.Cavalry || a == UnitKind.HeavyCavalry) && t == UnitKind.Archer)
|| (a == UnitKind.Infantry && (t == UnitKind.Cavalry || t == UnitKind.HeavyCavalry));
        }

        /// <summary>
        /// The damage one blow deals: <paramref name="damage"/>, raised by <paramref name="bonusPermille"/> thousandths
        /// when the attacker counters the target. Integer arithmetic only, so every machine gets the same number.
        /// </summary>
        public static int DamageAgainst(int damage, UnitKind attacker, UnitKind target, int bonusPermille)
        {
            if (bonusPermille <= 0 || !Counters(attacker, target)) return damage;
            return (int)((long)damage * (1000 + bonusPermille) / 1000);
        }

        /// <summary>Effective positive defence after percentage penetration has been applied first.</summary>
        public static int EffectiveDefense(int defense, int penetrationPermille, int flatPenetration)
        {
            int remainingPermille = 1000 - penetrationPermille;
            long scaled = (long)defense * remainingPermille / 1000;
            long effective = scaled - flatPenetration;
            return effective <= 0 ? 0 : effective > int.MaxValue ? int.MaxValue : (int)effective;
        }

        /// <summary>Physical or magical damage after positive defence. All arithmetic is integer and deterministic.</summary>
        public static int DamageAfterDefense(int damage, int defense, int penetrationPermille, int flatPenetration)
        {
            if (damage <= 0) return 0;
            int effective = EffectiveDefense(defense, penetrationPermille, flatPenetration);
            long denominator = 100L + effective;
            long numerator = (long)damage * 100;
            long reduced = (numerator + denominator - 1) / denominator;
            return reduced < 1 ? 1 : reduced > int.MaxValue ? int.MaxValue : (int)reduced;
        }

        /// <summary>True damage intentionally bypasses defence while retaining the zero-damage result.</summary>
        public static int TrueDamage(int damage) => damage <= 0 ? 0 : damage;

        /// <summary>Counter multiplier is resolved before the target's physical defence.</summary>
        public static int PhysicalDamageAgainst(int damage, UnitKind attacker, UnitKind target, int counterBonusPermille,
            int defense, int penetrationPermille, int flatPenetration)
            => DamageAfterDefense(DamageAgainst(damage, attacker, target, counterBonusPermille), defense,
                penetrationPermille, flatPenetration);

        /// <summary>Infantry, archer or cavalry - the three in the triangle. 0 for a scout, ram, mercenary, monk or villager.</summary>
        private static UnitKind Line(UnitKind kind)
        => kind == 0 || kind == UnitKind.Infantry || kind == UnitKind.HeavyInfantry || kind == UnitKind.SkirmishArcher ? UnitKind.Infantry
            : kind == UnitKind.Archer || kind == UnitKind.Cavalry || kind == UnitKind.HeavyCavalry ? (kind == UnitKind.HeavyCavalry ? UnitKind.Cavalry : kind)
                : kind == UnitKind.LightCavalry ? UnitKind.Cavalry
                : kind == UnitKind.Scout || kind == UnitKind.Ram || kind == UnitKind.Mercenary || kind == UnitKind.Monk ? (UnitKind)0 : (UnitKind)0;
    }
}
