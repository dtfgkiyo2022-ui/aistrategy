using System;

namespace Rts.Application
{
    /// <summary>
    /// How often the gateway asks the policy provider for an autonomous decision.
    /// <para>
    /// Asking once per allocation cycle is what Ver.1 does and stays the default, because every recorded baseline was
    /// produced with it. With an external model behind the provider, the cadence is also the cost: at one call per
    /// second a ten minute match is hundreds of calls per faction. <see cref="OnChange"/> asks only when the faction's
    /// observation shows something that could change the policy, plus a slow heartbeat so a quiet match still gets
    /// looked at.
    /// </para>
    /// <para>
    /// Either way the decision comes from the observation and the tick, never from a clock, so a replay of the same
    /// inputs produces the same state.
    /// </para>
    /// </summary>
    public sealed class AutonomousPollSchedule
    {
        /// <summary>Ver.1 behaviour: ask on every allocation cycle (every 20 ticks) when nothing is outstanding.</summary>
        public static AutonomousPollSchedule EveryCycle { get; } = new AutonomousPollSchedule(0);

        /// <summary>
        /// Ask when the observation changes in a way that could change the policy - the first enemy contact, more
        /// contacts than before, an outpost changing hands, or own core HP dropping - and otherwise at most once every
        /// <paramref name="maxIntervalTicks"/>. 600 ticks is 30 seconds at 20 Hz.
        /// </summary>
        public static AutonomousPollSchedule OnChange(long maxIntervalTicks = 600)
        {
            if (maxIntervalTicks < 20) throw new ArgumentOutOfRangeException(nameof(maxIntervalTicks), "The heartbeat cannot be shorter than one allocation cycle.");
            return new AutonomousPollSchedule(maxIntervalTicks);
        }

        private AutonomousPollSchedule(long maxIntervalTicks) { MaxIntervalTicks = maxIntervalTicks; }

        /// <summary>0 when every cycle is asked; otherwise the longest gap between two asks.</summary>
        public long MaxIntervalTicks { get; }

        public bool AsksEveryCycle => MaxIntervalTicks == 0;

        public override string ToString() => AsksEveryCycle ? "every-cycle" : "on-change/" + MaxIntervalTicks;
    }
}
