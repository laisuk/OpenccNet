using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace OpenccNetLib
{
    /// <summary>
    /// Precomputed lookup structure for maximum word length (cap) and  
    /// allowed lengths (bitmask) for dictionary entries grouped by their  
    /// first character.  
    /// Designed to speed up segmentation and conversion by avoiding  
    /// repeated per-dictionary scans.
    /// </summary>
    public sealed class StarterUnion
    {
        // max length per starter character (UTF-16 code unit count)
        private readonly ushort[] _cap = new ushort[char.MaxValue + 1];

        // length bitmap per starter character (bit N = length N+1 exists)
        private readonly ulong[] _mask = new ulong[char.MaxValue + 1];

        /// <summary>
        /// Retrieves the precomputed maximum length and length mask for a given  
        /// starting character.
        /// </summary>
        /// <param name="c0">The starting character to query.</param>
        /// <param name="cap">
        /// Output: The maximum key length (UTF-16 units) for dictionary entries  
        /// beginning with <paramref name="c0"/>.
        /// </param>
        /// <param name="mask">
        /// Output: A bitmask where bit <c>n</c> is set if at least one  
        /// dictionary entry of length <c>n+1</c> begins with <paramref name="c0"/>.  
        /// Lengths above 64 are not tracked in the mask.
        /// </param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Get(char c0, out ushort cap, out ulong mask)
        {
            cap = _cap[c0];
            mask = _mask[c0];
        }

        /// <summary>
        /// Builds a <see cref="StarterUnion"/> instance from one or more dictionaries,  
        /// scanning all keys to compute per-character maximum lengths and bitmasks.
        /// </summary>
        /// <param name="dictionaries">
        /// The set of dictionaries to analyze.  
        /// Each <see cref="DictWithMaxLength"/> provides a mapping of keys to values  
        /// and its own maximum key length.
        /// </param>
        /// <returns>
        /// A fully initialized <see cref="StarterUnion"/> ready for use in  
        /// conversion routines.
        /// </returns>
        public static StarterUnion Build(IReadOnlyList<DictWithMaxLength> dictionaries)
        {
            var u = new StarterUnion();

            for (var di = 0; di < dictionaries.Count; di++)
            {
                var d = dictionaries[di];

                // Iterate the dict once; netstandard2.0-friendly
                foreach (var kv in d.Dict)
                {
                    var key = kv.Key;
                    if (string.IsNullOrEmpty(key)) continue;

                    var c0 = key[0];
                    var len = key.Length;

                    // Update cap
                    if (len > u._cap[c0])
                        u._cap[c0] = (ushort)Math.Min(ushort.MaxValue, len);

                    // Update mask (track only up to length 64)
                    if (len >= 1 && len <= 64)
                        u._mask[c0] |= 1UL << (len - 1);
                }
            }

            return u;
        }
    }
}