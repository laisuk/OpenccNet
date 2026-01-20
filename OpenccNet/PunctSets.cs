using System.Buffers;
using System.Runtime.CompilerServices;

namespace OpenccNet;

internal static class PunctSets
{
    // -------------------------
    // Dialog quotes
    // -------------------------

    // Dialog brackets (Simplified / Traditional / JP-style)
    private const string DialogOpeners = "“‘「『﹁﹃";
    private const string DialogClosers = "”’」』﹂﹄";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsDialogOpener(char ch) => DialogOpeners.Contains(ch);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDialogCloser(char ch) => DialogClosers.Contains(ch);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsQuoteCloser(char ch) => IsDialogCloser(ch);

    /// <summary>
    /// Returns <c>true</c> if the line starts with a dialog opener
    /// after skipping leading whitespace and indentation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool BeginWithDialogStarter(ReadOnlySpan<char> s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (char.IsWhiteSpace(ch))
                continue;

            return IsDialogOpener(ch);
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool EndsWithDialogCloser(ReadOnlySpan<char> s)
    {
        return TryGetLastNonWhitespace(s, out _, out var last) && IsDialogCloser(last);
    }

    // -------------------------
    // Soft continuation punctuation
    // -------------------------
    private const string CommaLikeChars = "，,、";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsCommaLike(char ch) => CommaLikeChars.Contains(ch);

    /// <summary>
    /// Returns <c>true</c> if the string contains any comma-like
    /// separator characters (e.g. ， , 、).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool ContainsAnyCommaLike(ReadOnlySpan<char> s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (IsCommaLike(s[i]))
                return true;
        }

        return false;
    }

    // Optional broader soft clause end (ONLY use if a rule explicitly wants it)
    private const string SoftClauseEndChars = "，,、;；:：";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsSoftClauseEnd(char ch) => SoftClauseEndChars.Contains(ch);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsColonLike(char ch) => ch is '：' or ':';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool EndsWithColonLike(ReadOnlySpan<char> s)
    {
        return TryGetLastNonWhitespace(s, out _, out var last) && IsColonLike(last);
    }

    // -------------------------
    // Sentence endings (two tiers)
    // -------------------------

    // Tier 1: hard sentence enders (safe for "flush now")
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsStrongSentenceEnd(char ch) => ch switch
    {
        '。' or '！' or '？' or '!' or '?' => true,
        _ => false
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool ContainsStrongSentenceEnd(ReadOnlySpan<char> s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (IsStrongSentenceEnd(s[i]))
                return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool EndsWithStrongSentenceEnd(ReadOnlySpan<char> s)
        => TryGetLastNonWhitespace(s, out _, out var ch) && IsStrongSentenceEnd(ch);

    // Tier 2: clause-or-end-ish (looser heuristics, not always a true sentence end)
    private static readonly HashSet<char> ClauseOrEndPunct = new()
    {
        // Standard CJK sentence-ending punctuation
        '。', '！', '？', '；', '：', '…', '—',
        // Chinese closing dialog / quotes
        '”', '」', '’', '』',
        // Chinese closing brackets
        '）', '】', '》', '〗', '〕', '］', '｝', '＞', '〉', '>',
        // Allowed ASCII-like ending and bracket
        '.', ')', ':', '!', '?'
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsClauseOrEndPunct(char ch) => ClauseOrEndPunct.Contains(ch);

    // -------------------------
    // Brackets
    // -------------------------
    // Bracket punctuations (open-close)
    private static readonly IReadOnlyDictionary<char, char> BracketPairs =
        new Dictionary<char, char>
        {
            // Parentheses
            ['（'] = '）', ['('] = ')',
            // Square brackets
            ['['] = ']', ['［'] = '］',
            // Curly braces (ASCII + FULLWIDTH)
            ['{'] = '}', ['｛'] = '｝',
            // Angle brackets
            ['<'] = '>', ['＜'] = '＞', ['〈'] = '〉',
            // CJK brackets
            ['【'] = '】',
            ['《'] = '》',
            ['〔'] = '〕',
            ['〖'] = '〗',
        };

    private static readonly HashSet<char> OpenBrackets = new(BracketPairs.Keys);
    private static readonly HashSet<char> CloseBrackets = new(BracketPairs.Values);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsBracketOpener(char ch) => OpenBrackets.Contains(ch);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsBracketCloser(char ch) => CloseBrackets.Contains(ch);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsAllowedPostfixCloser(char ch) => ch is '）' or ')';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool EndsWithAllowedPostfixCloser(ReadOnlySpan<char> s)
    {
        return TryGetLastNonWhitespace(s, out _, out var last)
               && IsAllowedPostfixCloser(last);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsMatchingBracket(char open, char close)
        => BracketPairs.TryGetValue(open, out var expected) && expected == close;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsWrappedByMatchingBracket(ReadOnlySpan<char> s, char lastNonWs, int minLen = 3)
    {
        // minLen=3 means at least: open + 1 char + close
        return s.Length >= minLen && IsMatchingBracket(s[0], lastNonWs);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsBracketTypeBalanced(ReadOnlySpan<char> s, char open)
    {
        if (!TryGetMatchingCloser(open, out var close))
            return true;

        var depth = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch == open) depth++;
            else if (ch == close)
            {
                depth--;
                if (depth < 0) return false;
            }
        }

        return depth == 0;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetMatchingCloser(char open, out char close)
        => BracketPairs.TryGetValue(open, out close);

    /// <summary>
    /// Returns <c>true</c> if the text contains any unclosed or mismatched brackets
    /// according to <see cref="BracketPairs"/>.
    /// Treats stray closers / mismatches as unsafe.
    /// </summary>
    /// <remarks>
    /// Cross-page / soft-wrap safety:
    /// If the previous buffer is inside an unclosed bracket like
    /// "（......" ... "...。）", never flush on blank lines / sentence ends.
    /// Otherwise, we may split a single parenthesized paragraph across pages.
    /// </remarks>
    internal static bool HasUnclosedBracket(ReadOnlySpan<char> s)
    {
        if (s.IsEmpty)
            return false;

        char[]? rented = null;
        var top = 0;
        var seenBracket = false;

        try
        {
            for (var i = 0; i < s.Length; i++)
            {
                var ch = s[i];

                if (IsBracketOpener(ch))
                {
                    seenBracket = true;

                    rented ??= ArrayPool<char>.Shared.Rent(16);

                    if (top == rented.Length)
                    {
                        var bigger = ArrayPool<char>.Shared.Rent(rented.Length * 2);
                        Array.Copy(rented, bigger, rented.Length);
                        ArrayPool<char>.Shared.Return(rented);
                        rented = bigger;
                    }

                    rented[top++] = ch;
                    continue;
                }

                if (!IsBracketCloser(ch))
                    continue;

                seenBracket = true;

                // stray closer
                if (top == 0)
                    return true;

                // here: top > 0 implies rented != null (we must have seen an opener)
                var open = rented![--top];

                if (!IsMatchingBracket(open, ch))
                    return true;
            }

            return seenBracket && top != 0;
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }

    // -------------------------
    // Metadata separators
    // -------------------------
    // Raw data (never used directly outside helpers)
    private static readonly HashSet<char> MetadataSeparators = new()
    {
        ':', // ASCII colon
        '：', // Full-width colon
        '　', // Ideographic space (U+3000)
        '·', // Middle dot (Latin)
        '・', // Katakana middle dot
        // '．', // Full-width dot
    };

    // Semantic predicate (ONLY entry point)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsMetadataSeparator(char ch) => MetadataSeparators.Contains(ch);

    // -------------------------
    // Layout / visual dividers
    // -------------------------
    private const string AsciiDividerChars = "-=_~～";
    private const string StarDividerChars = "*＊★☆";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsBoxDrawingChar(char ch) => ch is >= '\u2500' and <= '\u257F';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiDividerChar(char ch) => AsciiDividerChars.Contains(ch);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsStarDividerChar(char ch) => StarDividerChars.Contains(ch);

    /// <summary>
    /// Returns <c>true</c> if the line consists exclusively of
    /// box-drawing or visual divider characters, ignoring whitespace.
    ///
    /// Typical examples include:
    /// <code>
    /// ──────
    /// ======
    /// ------
    /// ───===───
    /// </code>
    ///
    /// Such lines are treated as layout separators rather than text content.
    /// They commonly originate from page decoration, OCR artifacts,
    /// or chapter / section dividers.
    /// </summary>
    /// <remarks>
    /// This method is intended to operate on a <em>probe</em> string
    /// (with indentation and leading formatting already removed).
    /// Whitespace is ignored during detection.
    ///
    /// When detected, these lines should be handled as hard layout boundaries
    /// and normally force a paragraph break during reflow.
    /// </remarks>
    internal static bool IsVisualDividerLine(ReadOnlySpan<char> s, int minVisualChars = 3)
    {
        if (s.IsEmpty)
            return false;

        var visualCount = 0;

        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];

            if (char.IsWhiteSpace(ch))
                continue;

            // Any non-divider visible char => real text
            if (!IsBoxDrawingChar(ch) && !IsAsciiDividerChar(ch) && !IsStarDividerChar(ch))
                return false;

            visualCount++;
        }

        return visualCount >= minVisualChars;
    }

    /// <summary>
    /// Try to get the last non-whitespace character from a span.
    /// Returns index (0.\.Length-1) and the character.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetLastNonWhitespace(
        ReadOnlySpan<char> s,
        out int lastIdx,
        out char last)
    {
        lastIdx = -1;
        last = '\0';

        for (var i = s.Length - 1; i >= 0; i--)
        {
            var ch = s[i];
            if (char.IsWhiteSpace(ch))
                continue;

            lastIdx = i;
            last = ch;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Try to get last and previous non-whitespace characters in one pass.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetLastTwoNonWhitespace(
        ReadOnlySpan<char> s,
        out int lastIdx, out char last,
        out int prevIdx, out char prev)
    {
        lastIdx = prevIdx = -1;
        last = prev = '\0';

        if (!TryGetLastNonWhitespace(s, out lastIdx, out last))
            return false;

        // prev non-ws before lastIdx
        for (var i = lastIdx - 1; i >= 0; i--)
        {
            var ch = s[i];
            if (char.IsWhiteSpace(ch))
                continue;

            prevIdx = i;
            prev = ch;
            return true;
        }

        // found last, but no prev
        prevIdx = -1;
        prev = '\0';
        return true;
    }

    /// <summary>
    /// Try to get the previous non-whitespace char before beforeIndex in a span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetPrevNonWhitespace(
        ReadOnlySpan<char> s,
        int beforeIndex,
        out int prevIndex,
        out char prevChar)
    {
        prevIndex = -1;
        prevChar = '\0';

        var i = Math.Min(beforeIndex - 1, s.Length - 1);
        for (; i >= 0; i--)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
                continue;

            prevIndex = i;
            prevChar = c;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Try to get the previous non-whitespace char before beforeIndex in a span (char-only).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetPrevNonWhitespace(
        ReadOnlySpan<char> s,
        int beforeIndex,
        out char prevChar)
    {
        return TryGetPrevNonWhitespace(s, beforeIndex, out _, out prevChar);
    }
    
}