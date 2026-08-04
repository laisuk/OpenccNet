using System;
using System.Runtime.CompilerServices;

namespace OpenccNetLib
{
    /// <summary>
    /// Detects complete Unicode Ideographic Description Sequences (IDS).
    /// </summary>
    /// <remarks>Malformed or partial IDS expressions are not treated as complete IDS.</remarks>
    internal static class IdsHelper
    {
        private const int MaxIdsDepth = 16;

        private static readonly byte[] IdsArity =
        {
            2, 2, 3, 3,
            2, 2, 2, 2,
            2, 2, 2, 2,
            2, 2, 1, 1
        };

        /// <summary>
        /// Gets the range of a complete IDS expression starting at the specified position.
        /// </summary>
        /// <param name="chars">The text to inspect.</param>
        /// <param name="start">The zero-based starting position.</param>
        /// <param name="end">The exclusive end position when a complete IDS is found.</param>
        /// <returns><c>true</c> if a complete IDS starts at <paramref name="start"/>; otherwise, <c>false</c>.</returns>
        internal static bool IdsRangeAt(
            ReadOnlySpan<char> chars,
            int start,
            out int end)
        {
            end = start;

            if ((uint)start >= (uint)chars.Length)
                return false;

            if (!TryGetIdsOperatorArity(chars[start], out _))
                return false;

            var pos = start;

            if (!ConsumeIds(chars, ref pos, 0))
                return false;

            end = pos;
            return true;
        }

        /// <summary>
        /// Determines whether the entire span is one complete IDS expression.
        /// </summary>
        /// <param name="chars">The text span to inspect.</param>
        /// <returns><c>true</c> if the whole span is one complete IDS; otherwise, <c>false</c>.</returns>
        /// <remarks>Malformed or partial IDS expressions return <c>false</c>.</remarks>
        internal static bool IsCompleteIds(ReadOnlySpan<char> chars)
        {
            if (chars.IsEmpty)
                return false;

            if (!TryGetIdsOperatorArity(chars[0], out _))
                return false;

            var pos = 0;
            return ConsumeIds(chars, ref pos, 0) && pos == chars.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryGetIdsOperatorArity(char ch, out int arity)
        {
            var offset = ch - 0x2FF0;

            if ((uint)offset <= 0x0F)
            {
                arity = IdsArity[offset];
                return true;
            }

            arity = 0;
            return false;
        }

        private static bool ConsumeIds(
            ReadOnlySpan<char> chars,
            ref int pos,
            int depth)
        {
            if (pos >= chars.Length || depth > MaxIdsDepth)
                return false;

            var ch = chars[pos++];

            if (!TryGetIdsOperatorArity(ch, out var arity))
                return true;

            for (var i = 0; i < arity; i++)
            {
                if (!ConsumeIds(chars, ref pos, depth + 1))
                    return false;
            }

            return true;
        }

#if NET9_0_OR_GREATER
        /// <summary>
        /// Determines whether the specified text contains any Unicode
        /// Ideographic Description Sequence (IDS) operator in U+2FF0–U+2FFF.
        /// </summary>
        /// <remarks>
        /// A complete IDS expression cannot exist without at least one operator
        /// from the contiguous Unicode range U+2FF0–U+2FFF.
        /// <para>
        /// On .NET 9 and later, this method intentionally uses
        /// <see cref="System.MemoryExtensions.IndexOfAnyInRange{T}(System.ReadOnlySpan{T}, T, T)"/>
        /// instead of a manual character scan. This allows the runtime to use its
        /// optimized contiguous-range search.
        /// </para>
        /// <para>
        /// The pre-check lets the conversion pipeline bypass IDS-aware splitting when
        /// the input contains no IDS operator, preserving the faster non-IDS conversion
        /// path for ordinary text.
        /// </para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool ContainsIdsOperator(ReadOnlySpan<char> chars)
        {
            return chars.IndexOfAnyInRange('\u2FF0', '\u2FFF') >= 0;
        }
#endif
    }
}