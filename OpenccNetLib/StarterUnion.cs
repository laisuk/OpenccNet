using System;
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

            for (int di = 0; di < dictionaries.Count; di++)
            {
                var d = dictionaries[di];

                foreach (var kv in d.Dict)
                {
                    var key = kv.Key;
                    if (string.IsNullOrEmpty(key)) continue;

                    var c0 = key[0]; // first UTF-16 code unit (OK for non-BMP: we key on the high surrogate)
                    var len = key.Length; // key length in UTF-16 code units

                    // cap: max length seen
                    if (len > u._cap[c0])
                        u._cap[c0] = (ushort)Math.Min(ushort.MaxValue, len);

                    // mask: only up to 64
                    if (len <= 64)
                        u._mask[c0] |= 1UL << (len - 1);

                    // minLen: true minimum (works for >64 as well)
                    var m = u._minLen[c0];
                    if (m == 0 || len < m)
                        u._minLen[c0] = (ushort)Math.Min(ushort.MaxValue, len);
                }
            }

            return u;
        }
    }
}