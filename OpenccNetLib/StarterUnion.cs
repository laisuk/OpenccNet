using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace OpenccNetLib
{
    /// <summary>
    /// Precomputed lookup for per-starter maximum length (cap), allowed lengths bitmask,
    /// and minimum length. The mask tracks lengths 1..64; cap/minLen can exceed 64.
    /// </summary>
    public sealed class StarterUnion
    {
        // max length per starter character (UTF-16 code units)
        private readonly ushort[] _cap = new ushort[char.MaxValue + 1];

        // length bitmap per starter character (bit N = length N+1 exists), only up to 64
        private readonly ulong[] _mask = new ulong[char.MaxValue + 1];

        // minimum length per starter character (UTF-16 units), 0 means "no entries"
        private readonly ushort[] _minLen = new ushort[char.MaxValue + 1];

        /// <summary>Legacy getter (cap, mask).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Get(char c0, out ushort cap, out ulong mask)
        {
            cap = _cap[c0];
            mask = _mask[c0];
        }

        /// <summary>
        /// Preferred getter (cap, mask, minLen). minLen is the smallest key length
        /// observed for this starter (may be &gt; 64).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Get(char c0, out ushort cap, out ulong mask, out ushort minLen)
        {
            cap = _cap[c0];
            mask = _mask[c0];
            minLen = _minLen[c0];
        }

        /// <summary>
        /// Builds a StarterUnion from one or more dictionaries by scanning all keys once.
        /// </summary>
        public static StarterUnion Build(IReadOnlyList<DictWithMaxLength> dictionaries)
        {
            var u = new StarterUnion();

            // Hoist arrays to locals (cheaper in tight loops)
            var cap = u._cap; // ushort[65536]
            var mask = u._mask; // ulong[65536]
            var minLn = u._minLen; // ushort[65536]

            for (int di = 0, dn = dictionaries.Count; di < dn; di++)
            {
                var dict = dictionaries[di];

                // Iterate keys only; avoids reading kv.Value
                var keys = dict.Dict.Keys;
                foreach (var key in keys)
                {
                    // Dictionary keys should never be null, but guard length == 0 fast
                    var len = key.Length;
                    if (len == 0) continue;

                    // Starter bucket by first UTF-16 code unit (high surrogate for non-BMP)
                    int c0 = key[0];

                    // cap: track max observed length (clamped to ushort)
                    var oldCap = cap[c0];
                    if (len > oldCap)
                        cap[c0] = (ushort)(len > ushort.MaxValue ? ushort.MaxValue : len);

                    // mask: set binary bit for lengths 1..64
                    if (len <= 64)
                        mask[c0] |= 1UL << (len - 1);

                    // minLen: true minimum (also handles >64)
                    var m = minLn[c0];
                    if (m == 0 || len < m)
                        minLn[c0] = (ushort)(len > ushort.MaxValue ? ushort.MaxValue : len);
                }
            }

            return u;
        }
    }
}