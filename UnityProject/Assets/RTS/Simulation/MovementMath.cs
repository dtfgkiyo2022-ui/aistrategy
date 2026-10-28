using Rts.Contracts;

namespace Rts.Simulation
{
    /// <summary>Deterministic conversion from a configured speed to one simulation tick of movement.</summary>
    internal static class MovementMath
    {
        internal static Fix64 StepDistance(Fix64 speed, EconomyRules rules, bool villager = false)
        {
            if (!rules.FastMovement) return Fix64.FromRaw(speed.Raw / 20);
            int permille = villager ? rules.FastMovementVillagerSpeedPermille : rules.FastMovementUnitSpeedPermille;
            return Fix64.FromRaw(checked(speed.Raw * permille / 1000) / 20);
        }
    }
}
