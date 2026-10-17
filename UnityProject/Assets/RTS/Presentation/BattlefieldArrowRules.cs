using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>Pure ordering for the one visible movement arrow allowed per army.</summary>
    public static class BattlefieldArrowRules
    {
        private static readonly Fix64 MovementArrivalRadius = Fix64.FromInt(4);

        public static bool IsNewer(CommandView candidate, CommandView current)
        {
            if (candidate.AcceptedTick != current.AcceptedTick) return candidate.AcceptedTick > current.AcceptedTick;
            if (candidate.ApplyTick != current.ApplyTick) return candidate.ApplyTick > current.ApplyTick;
            return candidate.CommandId > current.CommandId;
        }

        /// <summary>Human point moves stop drawing once the army reference position reaches the clicked point.</summary>
        public static bool ShouldShowMovementArrow(CommandView command, SimPoint armyPosition)
        {
            // Core and outpost Focus orders are attacks.  Their arrows remain while combat continues.
            if (command.Kind != PolicyKind.Focus || command.Goal.Kind != GoalKind.Point) return true;
            long dx = armyPosition.X.Raw - command.Goal.Point.X.Raw;
            long dz = armyPosition.Z.Raw - command.Goal.Point.Z.Raw;
            return FixMath.CompareDistanceSquared(dx, dz, MovementArrivalRadius.Raw) > 0;
        }
    }
}
