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
        /// Maximum key length (in UTF-16 code units) across all starters in this union.
        /// </summary>
        public int GlobalCap { get; private set; }

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
        /// Fills <see cref="IsHs"/> for U+D800 to U+DBFF and <see cref="IsLs"/> for U+DC00 to U+DFFF.
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
                mask = _mask[c0] & ~1UL; // never len==1 for surrogate starters
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
        /// FAST PATH: Build from precomputed per-starter masks (no key scans).
        /// Falls back to legacy key-scan if StarterLenMask is absent.
        /// </summary>
        public static StarterUnion Build(IReadOnlyList<DictWithMaxLength> dictionaries)
        {
            // If at least one dict has StarterLenMask populated, use the fast union path.
            for (var i = 0; i < dictionaries.Count; i++)
                if (dictionaries[i]?.StarterLenMask != null && dictionaries[i].StarterLenMask.Count > 0)
                    return BuildFromStarterMasks(dictionaries);

            // Fallback to legacy builder (scans keys)
            return BuildByScanningKeys(dictionaries);
        }

        /// <summary>
        /// Builds a <see cref="StarterUnion"/> directly from pre-computed per-starter
        /// length masks instead of scanning all dictionary keys.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Each <see cref="DictWithMaxLength"/> provides a <c>StarterLenMask</c> mapping
        /// a UTF-16 starter string to a 64-bit mask where bit <c>n-1</c> indicates that
        /// at least one key of length <c>n</c> exists for that starter.
        /// </para>
        /// <para>
        /// This builder unions all such masks into dense per-code-unit tables:
        /// <list type="bullet">
        /// <item>
        /// <description>
        /// The first UTF-16 unit (<c>c0</c>) of each starter is used as the
        /// array index, even for surrogate-pair starters.  This design matches the
        /// existing dense-table layout and ensures fast O(1) access without hashing.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// For each bucket, <c>_mask[c0]</c> accumulates the union of all length bits,
        /// and the corresponding <c>_minLen[c0]</c> / <c>_cap[c0]</c> are derived from
        /// the lowest and highest set bits in the combined mask (covering lengths
        /// 1–64).  Dictionaries containing longer keys can extend this logic
        /// later if required.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// Astral (non-BMP) starters share their high-surrogate bucket (<c>c0</c>),
        /// which is sufficient because current OpenCC data only includes
        /// single-scalar astral characters (length 2) in
        /// <c>st_characters</c> and <c>ts_characters</c>.  If future datasets include
        /// astral phrases, the existing layout remains safe—it will simply probe a few
        /// extra candidate lengths.
        /// </description>
        /// </item>
        /// </list>
        /// </para>
        /// <para>
        /// Using pre-deserialized masks avoids rescanning every dictionary key and
        /// typically saves 5–15 ms of startup time for large OpenCC dictionaries.
        /// </para>
        /// </remarks>
        /// <param name="dictionaries">
        /// The collection of dictionaries whose <c>StarterLenMask</c> values will be
        /// union into a single <see cref="StarterUnion"/>.
        /// </param>
        /// <returns>
        /// A fully built <see cref="StarterUnion"/> containing merged starter masks,
        /// minimum lengths, caps and global cap for all provided dictionaries.
        /// </returns>
        private static StarterUnion BuildFromStarterMasks(IReadOnlyList<DictWithMaxLength> dictionaries)
        {
            var u = new StarterUnion();
            var cap = u._cap;
            var mask = u._mask;
            var minLn = u._minLen;
            var globalCap = 0; // NEW

            for (int di = 0, dn = dictionaries.Count; di < dn; di++)
            {
                var d = dictionaries[di];
                if (d?.StarterLenMask == null || d.StarterLenMask.Count == 0)
                    continue;

                if (d.MaxLength > globalCap) globalCap = d.MaxLength;

                foreach (var kv in d.StarterLenMask)
                {
                    var starter = kv.Key;
                    if (string.IsNullOrEmpty(starter)) continue;

                    // Use first UTF-16 unit (c0) as dense table index, even for surrogate starters.
                    int c0 = starter[0];

                    var m = kv.Value;
                    var combined = mask[c0] | m;
                    if (combined == mask[c0]) continue; // nothing new

                    mask[c0] = combined;

                    // Derive minLen/cap from combined mask (≤64 lengths).
                    var min = LowestLen(combined);
                    var max = HighestLen(combined);

                    if (min != 0 && (minLn[c0] == 0 || min < minLn[c0]))
                        minLn[c0] = (ushort)min;

                    if (max > cap[c0])
                        cap[c0] = (ushort)max;
                }
            }

            u.GlobalCap = globalCap;
            return u;
        }

        /// <summary>
        /// Legacy builder retained for compatibility / no StarterLenMask path.
        /// </summary>
        private static StarterUnion BuildByScanningKeys(IReadOnlyList<DictWithMaxLength> dictionaries)
        {
            var u = new StarterUnion();
            var cap = u._cap;
            var mask = u._mask;
            var minLn = u._minLen;
            var globalCap = 0;

            for (int di = 0, dn = dictionaries.Count; di < dn; di++)
            {
                var dict = dictionaries[di];
                if (dict?.Dict == null) continue;

                if (dict.MaxLength > globalCap) globalCap = dict.MaxLength;

                foreach (var key in dict.Dict.Keys)
                {
                    var len = key.Length;
                    if (len == 0) continue;

                    int c0 = key[0];

                    // mask (≤64)
                    if ((uint)len - 1u < 64u)
                    {
                        var combined = mask[c0] | (1UL << (len - 1));
                        mask[c0] = combined;

                        // Update min/max from mask bits
                        var min = LowestLen(combined);
                        var max = HighestLen(combined);
                        if (min != 0 && (minLn[c0] == 0 || min < minLn[c0]))
                            minLn[c0] = (ushort)min;
                        if (max > cap[c0])
                            cap[c0] = (ushort)max;
                    }
                    else
                    {
                        // >64 (rare; not present in your data). If you add support later,
                        // adjust cap/minLn to reflect true values here.
                        if (cap[c0] < len) cap[c0] = (ushort)Math.Min(len, ushort.MaxValue);
                        if (minLn[c0] == 0 || len < minLn[c0]) minLn[c0] = (ushort)Math.Min(len, ushort.MaxValue);
                    }
                }
            }

            u.GlobalCap = globalCap;
            return u;
        }

        // --- Bit helpers (no BitOperations dependency required) ---

        /// <summary>
        /// Returns the smallest key length represented in a 64-bit length mask.
        /// </summary>
        /// <remarks>
        /// Each bit <c>n-1</c> corresponds to the presence of keys of length <c>n</c>.
        /// The method scans from least-significant bit (bit 0) upward until the first
        /// set bit is found.  For example, a mask of <c>0b00000110</c> yields 2
        /// because the first set bit represents length 2.
        /// </remarks>
        /// <param name="m">
        /// The 64-bit mask to inspect.  Each set bit indicates a valid key length.
        /// </param>
        /// <returns>
        /// The smallest key length encoded in the mask, or 0 if the mask is 0.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int LowestLen(ulong m)
        {
            if (m == 0UL) return 0;
            var i = 0;
            // Find first set binary bit from LSB upward
            while (((m >> i) & 1UL) == 0UL) i++;
            return i + 1; // len = bitIndex + 1
        }

        /// <summary>
        /// Returns the largest key length represented in a 64-bit length mask.
        /// </summary>
        /// <remarks>
        /// Scans from the most-significant bit (bit 63) downward until the first set
        /// bit is encountered.  For example, a mask of <c>0b00111110</c> yields 6
        /// because the highest set bit corresponds to length 6.
        /// </remarks>
        /// <param name="m">
        /// The 64-bit mask to inspect.  Each set bit indicates a valid key length.
        /// </param>
        /// <returns>
        /// The largest key length encoded in the mask, or 0 if the mask is 0.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HighestLen(ulong m)
        {
            if (m == 0UL) return 0;
            var i = 63;
            // Find first set binary bit from MSB downward
            while (((m >> i) & 1UL) == 0UL) i--;
            return i + 1; // len = bitIndex + 1
        }
    }
}