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

        /// <summary>
        /// Lookup table for UTF-16 high surrogates (U+D800–U+DBFF).
        /// </summary>
        /// <remarks>
        /// Indexed by the <c>char</c> value (0..65535). <c>true</c> means the code unit is a high surrogate.
        /// Using a table avoids per-iteration range compares in the hot loop.
        /// Memory cost (with <see cref="IsLs"/>) is ~128 KB total plus array headers.
        /// </remarks>
        private static readonly bool[] IsHs = new bool[char.MaxValue + 1];

        /// <summary>
        /// Lookup table for UTF-16 low surrogates (U+DC00–U+DFFF).
        /// </summary>
        /// <remarks>
        /// Indexed by the <c>char</c> value (0..65535). <c>true</c> means the code unit is a low surrogate.
        /// Used together with <see cref="IsHs"/> to detect valid surrogate pairs with a single indexed load.
        /// </remarks>
        private static readonly bool[] IsLs = new bool[char.MaxValue + 1];

        /// <summary>
        /// Type initializer: precomputes surrogate lookup tables once per AppDomain.
        /// </summary>
        /// <remarks>
        /// Fills <see cref="IsHs"/> for U+D800..U+DBFF and <see cref="IsLs"/> for U+DC00..U+DFFF.
        /// Complexity is O(2048) and runs only once; arrays are thereafter read-only.
        /// </remarks>
        static StarterUnion()
        {
            // High surrogates: D800–DBFF
            for (var c = 0xD800; c <= 0xDBFF; c++) IsHs[c] = true;
            // Low surrogates: DC00–DFFF
            for (var c = 0xDC00; c <= 0xDFFF; c++) IsLs[c] = true;
        }

        /// <summary>
        /// Retrieves precomputed starter information for the character(s) at the current position,
        /// including grapheme width, starter presence, maximum key length, allowed length mask,
        /// and minimum key length.
        /// </summary>
        /// <param name="c0">
        /// The current UTF-16 code unit at the conversion cursor.  
        /// If <paramref name="c0"/> is a high surrogate and a valid low surrogate follows,
        /// the method treats it as the high surrogate of a supplementary (non-BMP) character.
        /// </param>
        /// <param name="c1">
        /// The next UTF-16 code unit after <paramref name="c0"/>;  
        /// ignored unless <paramref name="hasSecond"/> is <c>true</c>.
        /// </param>
        /// <param name="hasSecond">
        /// Indicates whether there is at least one code unit available after <paramref name="c0"/>.  
        /// This avoids bounds checks when the caller is near the end of the buffer.
        /// </param>
        /// <param name="starterUnits">
        /// Receives the number of UTF-16 code units that form the starter:  
        /// <c>1</c> for BMP characters, <c>2</c> for valid surrogate pairs (astral characters).
        /// </param>
        /// <param name="hasStarter">
        /// <c>true</c> if at least one dictionary key begins with this starter character;  
        /// otherwise, <c>false</c>.
        /// </param>
        /// <param name="cap">
        /// The maximum key length (in UTF-16 code units) observed for this starter across all dictionaries.  
        /// Zero indicates that no keys start with this character.
        /// </param>
        /// <param name="mask">
        /// A 64-bit bitmask where bit <c>L-1</c> is set if a key of length <c>L</c> (in UTF-16 units)
        /// begins with this starter.  
        /// For astral characters, bit 0 (<c>len==1</c>) is always cleared to prevent invalid single-unit matches.
        /// </param>
        /// <param name="minLen">
        /// The minimum key length (in UTF-16 code units) observed for this starter across all dictionaries.  
        /// Zero indicates that no keys start with this character.
        /// </param>
        /// <remarks>
        /// <para>
        /// This method combines surrogate-pair detection and starter lookup in a single inline call.  
        /// It eliminates redundant per-iteration surrogate checks in the conversion loop
        /// and ensures that astral characters are never matched with <c>len==1</c>.
        /// </para>
        /// <para>
        /// The caller should use <paramref name="starterUnits"/> to advance the cursor
        /// and to compute the lower bound for key-length probing:
        /// <code language="csharp">
        /// var lower = Math.Max(minLen, starterUnits);
        /// for (int len = tryMax; len >= lower; --len) { ... }
        /// </code>
        /// </para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetAt(char c0, char c1, bool hasSecond,
            out int starterUnits, out bool hasStarter,
            out ushort cap, out ulong mask, out ushort minLen)
        {
            if (hasSecond && IsHs[c0] && IsLs[c1])
            {
                starterUnits = 2;
                cap = _cap[c0];
                mask = _mask[c0] & ~1UL; // never len==1
                minLen = _minLen[c0];
                hasStarter = minLen != 0;
                return;
            }

            starterUnits = 1;
            cap = _cap[c0];
            mask = _mask[c0];
            minLen = _minLen[c0];
            hasStarter = minLen != 0;
        }

        // ---- Legacy overloads: keep, discourage, and route to GetAt ----

        [Obsolete("Use GetAt(c0,c1,hasSecond, ...) to handle astral starters correctly.")]
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Get(char c0, out ushort cap, out ulong mask)
        {
            cap = _cap[c0];
            mask = _mask[c0];
        }

        [Obsolete("Use GetAt(c0,c1,hasSecond, ...) to handle astral starters correctly.")]
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
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