using System;

namespace Rts.Contracts
{
    /// <summary>A mutable deterministic stream. Save both State and CallCount for replay.</summary>
    public sealed class SplitMix64
    {
        public ulong State { get; private set; }
        /// <summary>Number of raw draws, including rejected range samples. Overflow throws.</summary>
        public ulong CallCount { get; private set; }

        public SplitMix64(ulong seed) { State = seed; }

        public void Restore(ulong state, ulong callCount)
        {
            State = state;
            CallCount = callCount;
        }

        public ulong NextUInt64()
        {
            ulong count = checked(CallCount + 1);
            unchecked
            {
                State += 0x9E3779B97F4A7C15UL;
                ulong z = State;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                CallCount = count;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Uniform sample in [0, exclusiveMax). Zero is invalid.</summary>
        public ulong NextUInt64(ulong exclusiveMax)
        {
            if (exclusiveMax == 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            // 2^64 mod bound: removing this prefix leaves a multiple of bound outcomes.
            ulong threshold = unchecked(0UL - exclusiveMax) % exclusiveMax;
            ulong sample;
            do { sample = NextUInt64(); } while (sample < threshold);
            return sample % exclusiveMax;
        }
    }
}
