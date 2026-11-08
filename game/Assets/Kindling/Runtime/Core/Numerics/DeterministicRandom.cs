using System;

namespace Kindling.Core.Numerics
{
    /// <summary>
    /// xorshift128+ PRNG with identical output on every runtime (System.Random differs between
    /// .NET Framework, .NET and Unity). Use it for anything that must match on client, server and bots,
    /// e.g. procedural map layout from a seed.
    /// </summary>
    public sealed class DeterministicRandom
    {
        ulong m_S0;
        ulong m_S1;

        public DeterministicRandom(ulong seed)
        {
            // SplitMix64 to spread the seed over both state words (and avoid an all-zero state).
            m_S0 = SplitMix(ref seed);
            m_S1 = SplitMix(ref seed);
        }

        static ulong SplitMix(ref ulong x)
        {
            ulong z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            ulong s1 = m_S0;
            ulong s0 = m_S1;
            m_S0 = s0;
            s1 ^= s1 << 23;
            m_S1 = s1 ^ s0 ^ (s1 >> 17) ^ (s0 >> 26);
            return m_S1 + s0;
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat() => (NextULong() >> 40) * (1f / (1UL << 24));

        /// <summary>Uniform float in [min, max).</summary>
        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>Uniform int in [min, maxExclusive).</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            }

            return min + (int)(NextULong() % (ulong)(maxExclusive - min));
        }
    }
}
