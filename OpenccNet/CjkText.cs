using System.Runtime.CompilerServices;

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
                if (IsNeutralPunctuation(ch))
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNeutralPunctuation(char ch)
        {
            // ASCII neutral punctuation
            if (ch is ' ' or '-' or '/' or ':' or '.')
                return true;

            // Common CJK / typography punctuation (neutral)
            return ch switch
            {
                '—' => true, // em dash
                '–' => true, // en dash
                '…' => true, // ellipsis
                '·' => true, // middle dot
                '・' => true, // katakana middle dot
                '～' => true, // wave dash
                '―' => true, // horizontal bar
                '‐' => true, // hyphen
                '-' => true, // non-breaking hyphen

                // Full-width punctuation often seen in titles
                '、' or '：'
                    => true,

                _ => false
            };
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
        private static bool ContainsAnyCjk(ReadOnlySpan<char> s)
        {
            for (var i = 0; i < s.Length; i++)
                if (IsCjk(s[i]))
                    return true;
            return false;
        }

        // =========================
        //  Mostly-CJK heuristic
        // =========================

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
                if (ch > 0x7F || !char.IsLetter(ch)) continue;
                ascii++;
                // Early fail: clearly non-CJK
                if (ascii > cjk + 4)
                    return false;
            }

            return cjk > 0 && cjk >= ascii;
        }

        // =========================
        //  Ellipsis
        // =========================

        private static bool EndsWithEllipsis(ReadOnlySpan<char> s)
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

        // ------ Sentence Boundary start ------ //

        internal static bool EndsWithSentenceBoundary(ReadOnlySpan<char> s, int level = 2)
        {
            if (s.IsWhiteSpace())
                return false;

            // last non-whitespace
            if (!PunctSets.TryGetLastNonWhitespace(s, out var lastIdx, out var last))
                return false;

            // ---- STRICT rules (level >= 3) ----
            // 1) Strong sentence end
            switch (last)
            {
                case var _ when PunctSets.IsStrongSentenceEnd(last):
                case '.' when level >= 3 && IsOcrCjkAsciiPunctAtLineEnd(s, lastIdx):
                case ':' when level >= 3 && IsOcrCjkAsciiPunctAtLineEnd(s, lastIdx):
                    return true;
            }

            // prev non-whitespace (before last-Non-Ws)
            PunctSets.TryGetPrevNonWhitespace(s, lastIdx, out var prevIdx, out var prev);

            // 2) Quote closers + Allowed postfix closer after strong end
            if ((PunctSets.IsQuoteCloser(last) || PunctSets.IsAllowedPostfixCloser(last)) && prevIdx >= 0)
            {
                // Strong end immediately before quote closer
                if (PunctSets.IsStrongSentenceEnd(prev))
                    return true;

                // OCR artifact: “.” where '.' acts like '。' (CJK context)
                // '.' is not the lastNonWs (quote is), so use the "before closers" version.
                if (prev == '.' && IsOcrCjkAsciiPunctBeforeClosers(s, prevIdx))
                    return true;
            }

            if (level >= 3)
                return false;

            // ---- LENIENT rules (level == 2) ----

            // 4) NEW: long Mostly-CJK line ending with full-width colon "："
            // Treat as a weak boundary (common in novels: "他说：" then dialog starts next line)
            if (last == '：' && IsMostlyCjk(s))
                return true;

            // Level 2 (lenient): allow ellipsis as weak boundary
            if (EndsWithEllipsis(s))
                return true;

            if (level >= 2)
                return false;

            // ---- VERY LENIENT rules (level == 1) ----
            return last is '；' or '：' or ';' or ':';
        }

        // Strict: the ASCII punct itself is the last non-whitespace char (level 3 strict rules).
        private static bool IsOcrCjkAsciiPunctAtLineEnd(ReadOnlySpan<char> s, int lastNonWsIndex)
        {
            if (lastNonWsIndex <= 0)
                return false;

            return IsCjk(s[lastNonWsIndex - 1]) && IsMostlyCjk(s);
        }

        // Relaxed "end": after index, only whitespace and closers are allowed.
        // Needed for patterns like: CJK '.' then closing quote/bracket: “。”  .」  .）
        private static bool IsOcrCjkAsciiPunctBeforeClosers(ReadOnlySpan<char> s, int index)
        {
            if (!IsAtEndAllowingClosers(s, index))
                return false;

            // Must have a previous *non-whitespace* character
            if (!PunctSets.TryGetPrevNonWhitespace(s, index, out var prev))
                return false;

            // Previous meaningful char must be CJK, and the line mostly CJK
            return IsCjk(prev) && IsMostlyCjk(s);
        }

        private static bool IsAtEndAllowingClosers(ReadOnlySpan<char> s, int index)
        {
            for (var j = index + 1; j < s.Length; j++)
            {
                var ch = s[j];
                if (char.IsWhiteSpace(ch))
                    continue;

                if (PunctSets.IsQuoteCloser(ch) || PunctSets.IsBracketCloser(ch))
                    continue;

                return false;
            }

            return true;
        }

        // ------ Sentence Boundary end ------ //


        // ------ Bracket Boundary start ------

        internal static bool EndsWithCjkBracketBoundary(ReadOnlySpan<char> s)
        {
            // Equivalent to string.IsNullOrWhiteSpace
            if (s.IsWhiteSpace())
                return false;

            // Trim without allocation
            s = s.Trim();
            if (s.Length < 2)
                return false;

            var open = s[0];
            var close = s[^1];

            // 1) Must be one of our known pairs.
            if (!PunctSets.IsMatchingBracket(open, close))
                return false;

            // Inner content (exclude the outer bracket pair)
            var inner = s.Slice(1, s.Length - 2).Trim();
            if (inner.IsEmpty)
                return false;

            // 2) Must be mostly CJK (reject "(test)", "[1.2]", etc.)
            if (!IsMostlyCjk(inner))
                return false;

            // ASCII bracket pairs are suspicious → require at least one CJK inside
            if ((open is '(' or '[') && !ContainsAnyCjk(inner))
                return false;

            // 3) Ensure this bracket type is balanced inside the text
            //    (prevents malformed OCR / premature close)
            return PunctSets.IsBracketTypeBalanced(s, open);
        }

        // ------ Bracket Boundary end ------
    }
}