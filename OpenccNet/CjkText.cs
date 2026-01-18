using System.Runtime.CompilerServices;
using System.Text;

namespace OpenccNet
{
    internal static class CjkText
    {
        // =========================
        //  Character classification
        // =========================

        /// <summary>
        /// Minimal CJK checker (BMP focused).
        /// Designed for reflow heuristics, not full Unicode linguistics.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsCjk(char ch)
        {
            int c = ch;

            // CJK Unified Ideographs + Extension A
            if ((uint)(c - 0x3400) <= (0x4DBF - 0x3400)) return true;
            if ((uint)(c - 0x4E00) <= (0x9FFF - 0x4E00)) return true;

            // Compatibility Ideographs
            return (uint)(c - 0xF900) <= (0xFAFF - 0xF900);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDigitAsciiOrFullWidth(char ch)
        {
            // ASCII digits
            if ((uint)(ch - '0') <= 9) return true;
            // FULLWIDTH digits
            return (uint)(ch - '０') <= 9;
        }

        // =========================
        //  ASCII helpers
        // =========================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsAllAscii(ReadOnlySpan<char> s)
        {
            for (var i = 0; i < s.Length; i++)
                if (s[i] > 0x7F)
                    return false;
            return s.Length > 0;
        }

        internal static bool IsAllAsciiDigits(ReadOnlySpan<char> s)
        {
            var hasDigit = false;

            for (var i = 0; i < s.Length; i++)
            {
                var ch = s[i];

                switch (ch)
                {
                    // ASCII space is neutral
                    case ' ':
                        continue;

                    // ASCII digits
                    case >= '0' and <= '9':
                    // FULLWIDTH digits
                    case >= '０' and <= '９':
                        hasDigit = true;
                        continue;

                    default:
                        // anything else -> reject
                        return false;
                }
            }

            return hasDigit;
        }

        // =========================
        //  CJK / mixed helpers
        // =========================

        internal static bool IsMixedCjkAscii(ReadOnlySpan<char> s)
        {
            var hasCjk = false;
            var hasAscii = false;

            for (var i = 0; i < s.Length; i++)
            {
                var ch = s[i];

                // Neutral ASCII (allowed, but doesn't count as ASCII content)
                if (ch is ' ' or '-' or '/' or ':' or '.')
                    continue;

                if (ch <= 0x7F)
                {
                    if (char.IsLetterOrDigit(ch))
                        hasAscii = true;
                    else
                        return false;
                }
                else if (ch is >= '０' and <= '９')
                {
                    hasAscii = true;
                }
                else if (IsCjk(ch))
                {
                    hasCjk = true;
                }
                else
                {
                    return false;
                }

                if (hasCjk && hasAscii)
                    return true;
            }

            return false;
        }

        // Returns true if the span consists entirely of CJK characters.
        // Whitespace handling is controlled by allowWhitespace.
        // Returns false for empty or whitespace-only spans.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsAllCjk(ReadOnlySpan<char> s, bool allowWhitespace = false)
        {
            var seen = false;

            for (var i = 0; i < s.Length; i++)
            {
                var ch = s[i];

                if (char.IsWhiteSpace(ch))
                {
                    if (!allowWhitespace)
                        return false;
                    continue;
                }

                seen = true;

                if (!IsCjk(ch))
                    return false;
            }

            return seen;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsAllCjkIgnoringWhitespace(ReadOnlySpan<char> s)
            => IsAllCjk(s, allowWhitespace: true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsAllCjkNoWhiteSpace(ReadOnlySpan<char> s)
            => IsAllCjk(s, allowWhitespace: false);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool ContainsAnyCjk(ReadOnlySpan<char> s)
        {
            for (var i = 0; i < s.Length; i++)
                if (IsCjk(s[i]))
                    return true;
            return false;
        }

        // =========================
        //  Mostly-CJK heuristic
        // =========================

        // internal static bool IsMostlyCjk(string s)
        //     => !string.IsNullOrEmpty(s) && IsMostlyCjk(s.AsSpan());

        internal static bool IsMostlyCjk(ReadOnlySpan<char> s)
        {
            var cjk = 0;
            var ascii = 0;

            for (var i = 0; i < s.Length; i++)
            {
                var ch = s[i];

                // Neutral whitespace
                if (char.IsWhiteSpace(ch))
                    continue;

                // Neutral digits (ASCII + FULLWIDTH)
                if (IsDigitAsciiOrFullWidth(ch))
                    continue;

                if (IsCjk(ch))
                {
                    cjk++;
                    continue;
                }

                // Count ASCII letters only; ASCII punctuation is neutral
                if (ch <= 0x7F && char.IsLetter(ch))
                    ascii++;
            }

            return cjk > 0 && cjk >= ascii;
        }

        internal static bool IsMostlyCjk(StringBuilder? sb)
        {
            if (sb == null || sb.Length == 0)
                return false;

            var cjk = 0;
            var ascii = 0;

            foreach (var mem in sb.GetChunks())
            {
                var s = mem.Span;
                for (var i = 0; i < s.Length; i++)
                {
                    var ch = s[i];

                    if (char.IsWhiteSpace(ch))
                        continue;

                    if (IsDigitAsciiOrFullWidth(ch))
                        continue;

                    if (IsCjk(ch))
                    {
                        cjk++;
                        continue;
                    }

                    if (ch <= 0x7F && char.IsLetter(ch))
                        ascii++;
                }
            }

            return cjk > 0 && cjk >= ascii;
        }

        // =========================
        //  Ellipsis
        // =========================

        internal static bool EndsWithEllipsis(ReadOnlySpan<char> s)
        {
            if (s.IsEmpty)
                return false;

            // Strong CJK gate: ellipsis only meaningful in CJK context
            if (!IsMostlyCjk(s))
                return false;

            var i = s.Length - 1;
            while (i >= 0 && char.IsWhiteSpace(s[i])) i--;
            if (i < 0)
                return false;

            // Single Unicode ellipsis
            if (s[i] == '…')
                return true;

            // OCR case: ASCII "..."
            return i >= 2 && s[i] == '.' && s[i - 1] == '.' && s[i - 2] == '.';
        }

        // ------ Sentence Boundary ------ //

        internal static bool EndsWithSentenceBoundary(StringBuilder? sb, int level = 2)
        {
            if (sb == null || sb.Length == 0)
                return false;

            // Rolling last 3 non-whitespace chars (+ indices)
            int lastIdx = -1, prevIdx = -1, prevPrevIdx = -1;
            char last = '\0', prev = '\0', prevPrev = '\0';

            // "Mostly CJK" counts (EXACT same semantics as IsMostlyCjk())
            var cjk = 0;
            var ascii = 0;

            // Track the most recent ASCII punct candidate '.' or ':' and whether only closers/ws follow it.
            // Needed for: IsOcrCjkAsciiPunctBeforeClosers(...)
            var asciiPunctIdx = -1;
            var asciiPunctTailOk = false;

            var pos = 0;
            foreach (var mem in sb.GetChunks())
            {
                var span = mem.Span;

                for (var i = 0; i < span.Length; i++, pos++)
                {
                    var ch = span[i];

                    if (char.IsWhiteSpace(ch))
                        continue;

                    // ---- Mostly-CJK counting (neutral ws + digits; count CJK; count ASCII letters only)
                    if (!IsDigitAsciiOrFullWidth(ch))
                    {
                        if (IsCjk(ch))
                        {
                            cjk++;
                        }
                        else if (ch <= 0x7F && char.IsLetter(ch))
                        {
                            ascii++;
                        }
                    }

                    // ---- Tail validation for ASCII punct candidate (after candidate: only whitespace/closers allowed)
                    if (asciiPunctIdx >= 0)
                    {
                        // We only see non-whitespace here (whitespace skipped).
                        if (!(PunctSets.IsQuoteCloser(ch) || PunctSets.IsBracketCloser(ch)))
                        {
                            asciiPunctTailOk = false;
                        }
                    }

                    // ---- Update rolling last/prev/prevPrev (non-whitespace only)
                    prevPrevIdx = prevIdx;
                    prevPrev = prev;
                    prevIdx = lastIdx;
                    prev = last;
                    lastIdx = pos;
                    last = ch;

                    // ---- Update ASCII punct candidate
                    if (ch is not ('.' or ':')) continue;
                    asciiPunctIdx = pos;
                    asciiPunctTailOk = true;
                }
            }

            if (lastIdx < 0)
                return false;

            var isMostlyCjk = cjk > 0 && cjk >= ascii;

            // ---- STRICT rules (level >= 3) ----
            // 1) Strong sentence end
            if (PunctSets.IsStrongSentenceEnd(last))
                return true;

            // Strict OCR: ASCII punct itself is last non-ws char
            if (last is '.' or ':' && prevIdx >= 0 && IsCjk(prev) && isMostlyCjk)
                return true;

            // 2) Quote closers + Allowed postfix closer after strong end
            if ((PunctSets.IsQuoteCloser(last) || PunctSets.IsAllowedPostfixCloser(last)) && prevIdx >= 0)
            {
                // Strong end immediately before quote closer
                if (PunctSets.IsStrongSentenceEnd(prev))
                    return true;

                // OCR artifact: '.' before closers ('.' is prev non-ws, last is closer)
                // Original requires:
                // - after '.' only whitespace and closers
                // - previous non-ws before '.' must be CJK
                // - line mostly CJK
                if (prev == '.' &&
                    asciiPunctIdx == prevIdx &&
                    asciiPunctTailOk &&
                    prevPrevIdx >= 0 &&
                    IsCjk(prevPrev) &&
                    isMostlyCjk)
                {
                    return true;
                }
            }
            
            if (level >= 3)
                return false;

            // ---- LENIENT rules (level == 2) ----
            
            // 3) Bracket closers with most CJK (reserved)
            // if (PunctSets.IsBracketCloser(last) && lastIdx > 0 && IsMostlyCjk(s))
            //     return true;
            
            switch (isMostlyCjk)
            {
                // 4) Mostly-CJK line ending with full-width colon "："
                case true when last == '：':
                // Ellipsis (same semantics as your string version: trim-right-ws then check tail)
                // Single Unicode ellipsis
                case true when last == '…':
                // OCR case: ASCII "..." at the very end (consecutive indices after trimming whitespace)
                case true when
                    last == '.' && prev == '.' && prevPrev == '.' &&
                    prevPrevIdx >= 0 &&
                    lastIdx == prevIdx + 1 &&
                    prevIdx == prevPrevIdx + 1:
                    return true;
            }

            if (level >= 2)
                return false;

            // ---- VERY LENIENT rules (level == 1) ----
            return last is '；' or '：' or ';' or ':';
        }

        // ------ Bracket Boundary ------ //

        internal static bool EndsWithCjkBracketBoundary(StringBuilder? sb)
        {
            if (sb == null || sb.Length == 0)
                return false;

            // Equivalent to: s = s.Trim(); if (s.Length < 2) return false;
            if (!PunctSets.TryGetLastNonWhitespace(sb, out var lastIdx, out var close))
                return false;

            var firstIdx = -1;
            var open = '\0';

            var pos = 0;
            foreach (var mem in sb.GetChunks())
            {
                var span = mem.Span;
                for (var i = 0; i < span.Length; i++, pos++)
                {
                    var ch = span[i];
                    if (char.IsWhiteSpace(ch))
                        continue;

                    firstIdx = pos;
                    open = ch;
                    goto FOUND_FIRST;
                }
            }

            FOUND_FIRST:
            if (firstIdx < 0)
                return false;

            if (lastIdx - firstIdx + 1 < 2)
                return false;

            // 1) Must be one of our known pairs.
            if (!PunctSets.IsMatchingBracket(open, close))
                return false;

            // Inner range (exclude outer bracket pair), then inner.Trim()
            var innerStart = firstIdx + 1;
            var innerEnd = lastIdx - 1;
            if (innerStart > innerEnd)
                return false;

            var innerFirst = -1;
            var innerLast = -1;

            // 2) Must be mostly CJK (based on inner.Trim()).
            // Additionally: if open is '(' or '[', require at least one CJK char in inner.Trim().
            var cjk = 0;
            var ascii = 0;
            var innerHasCjk = false;

            pos = 0;
            foreach (var mem in sb.GetChunks())
            {
                var span = mem.Span;
                for (var i = 0; i < span.Length; i++, pos++)
                {
                    if (pos < innerStart)
                        continue;
                    if (pos > innerEnd)
                        goto DONE_INNER_SCAN;

                    var ch = span[i];

                    // inner.Trim() bounds
                    if (!char.IsWhiteSpace(ch))
                    {
                        if (innerFirst < 0) innerFirst = pos;
                        innerLast = pos;
                    }

                    // Mostly-CJK counts (same semantics as IsMostlyCjk): ignore whitespace and digits.
                    if (char.IsWhiteSpace(ch))
                        continue;

                    if (IsDigitAsciiOrFullWidth(ch))
                        continue;

                    if (IsCjk(ch))
                    {
                        cjk++;
                        innerHasCjk = true;
                        continue;
                    }

                    // Count ASCII letters only; ASCII punctuation is neutral
                    if (ch <= 0x7F && char.IsLetter(ch))
                        ascii++;
                }
            }

            DONE_INNER_SCAN:
            // inner.Trim().Length == 0
            if (innerFirst < 0 || innerLast < innerFirst)
                return false;

            // Must be mostly CJK: cjk > 0 && cjk >= ascii
            if (!(cjk > 0 && cjk >= ascii))
                return false;

            // ASCII bracket pairs suspicious -> require at least one CJK in inner
            if (open is '(' or '[' && !innerHasCjk)
                return false;

            // 3) Ensure this bracket type is balanced inside the trimmed text
            return PunctSets.IsBracketTypeBalanced(sb, firstIdx, lastIdx, open);
        }
    }
}