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
    private static bool IsDialogCloser(char ch) => DialogClosers.Contains(ch);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsQuoteCloser(char ch) => IsDialogCloser(ch);

    /// <summary>
    /// Returns <c>true</c> if the line starts with a dialog opener
    /// after skipping leading whitespace and indentation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsDialogStarter(string s)
    {
        return TryGetFirstNonWhitespace(s, out var ch) &&
               IsDialogOpener(ch);
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
    internal static bool ContainsAnyCommaLike(string s)
    {
        foreach (var ch in s)
        {
            if (IsCommaLike(ch))
                return true;
        }

        return false;
    }

    // Optional broader soft clause end (ONLY use if a rule explicitly wants it)
    private const string SoftClauseEndChars = "，,、;；:：";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsSoftClauseEnd(char ch) => SoftClauseEndChars.Contains(ch);

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
    internal static bool ContainsStrongSentenceEnd(string s)
    {
        foreach (var ch in s)
        {
            if (IsStrongSentenceEnd(ch))
                return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool EndsWithStrongSentenceEnd(string s)
        => TryGetLastNonWhitespace(s, out var ch) && IsStrongSentenceEnd(ch);

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
    internal static bool IsMatchingBracket(char open, char close)
        => BracketPairs.TryGetValue(open, out var expected) && expected == close;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsWrappedByMatchingBracket(string s, char lastNonWs, int minLen = 3)
    {
        // minLen=3 means at least: open + 1 char + close
        return s.Length >= minLen && IsMatchingBracket(s[0], lastNonWs);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsBracketTypeBalanced(string s, char open)
    {
        if (!BracketPairs.TryGetValue(open, out var close))
            return true; // unknown opener → ignore

        var depth = 0;

        foreach (var ch in s)
        {
            if (ch == open)
            {
                depth++;
            }
            else if (ch == close)
            {
                depth--;
                if (depth < 0)
                    return false;
            }
        }

        return depth == 0;
    }

    /// <summary>
    /// Returns <c>true</c> if the text contains any unclosed or mismatched brackets
    /// according to <see cref="BracketPairs"/>.
    /// Treats stray closers / mismatches as unsafe.
    /// </summary>
    // Cross-page / soft-wrap safety:
    // If the previous buffer is inside an unclosed bracket like
    // "（......" ... "...。）", never flush on blank lines / sentence ends.
    // Otherwise, we may split a single parenthesized paragraph across pages.
    internal static bool HasUnclosedBracket(string s)
    {
        if (string.IsNullOrEmpty(s))
            return false;

        char[]? rented = null;
        var top = 0;
        var seenBracket = false;

        try
        {
            foreach (var ch in s)
            {
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
    internal static bool IsVisualDividerLine(string s, int minVisualChars = 3)
    {
        if (string.IsNullOrEmpty(s))
            return false;

        var visualCount = 0;

        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
                continue;

            if (!IsBoxDrawingChar(ch) && !IsAsciiDividerChar(ch) && !IsStarDividerChar(ch)) return false; // real text
            visualCount++;
        }

        return visualCount >= minVisualChars;
    }

    // -------------------------
    // Common helper (optional)
    // -------------------------
    internal static bool TryGetLastNonWhitespace(string s, out char ch)
    {
        for (var i = s.Length - 1; i >= 0; i--)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) continue;
            ch = c;
            return true;
        }

        ch = '\0';
        return false;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetLastNonWhitespace(string s, out int index, out char ch)
    {
        for (var i = s.Length - 1; i >= 0; i--)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
                continue;

            index = i;
            ch = c;
            return true;
        }

        index = -1;
        ch = '\0';
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetFirstNonWhitespace(string s, out char ch)
    {
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
                continue;

            ch = c;
            return true;
        }

        ch = '\0';
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetPrevNonWhitespace(string s, int beforeIndex, out char ch)
    {
        for (var i = beforeIndex - 1; i >= 0; i--)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
                continue;

            ch = c;
            return true;
        }

        ch = '\0';
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetPrevNonWhitespace(string s, int beforeIndex, out int index, out char ch)
    {
        for (var i = beforeIndex - 1; i >= 0; i--)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
                continue;

            index = i;
            ch = c;
            return true;
        }

        index = -1;
        ch = '\0';
        return false;
    }
}