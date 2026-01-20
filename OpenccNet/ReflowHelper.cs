using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenccNet;

public static class ReflowHelper
{
    // Chapter / heading patterns (短行 + 第N章/卷/节/部, 前言/序章/终章/尾声/番外)
    private static readonly Regex TitleHeadingRegex =
        new(
            @"^(?!.*[,，])(?=.{0,50}$)
                  (前言|序章|楔子|终章|尾声|后记|尾聲|後記|番外.{0,15}
                  |.{0,10}?第.{0,5}?([章节部卷節回][^分合的])|(?:卷|章)[一二三四五六七八九十](?:$|.{0,20}?)
                  )",
            RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace);

    //Paragraph indentation
    private static readonly Regex IndentRegex =
        new(@"^[\s\u3000]{2,}", RegexOptions.Compiled);

    // Metadata heading title names
    private static readonly HashSet<string> MetadataKeys = new(StringComparer.Ordinal)
    {
        // ===== 1. Title / Author / Publishing =====
        "書名", "书名",
        "作者",
        "原著",
        "譯者", "译者",
        "校訂", "校订",
        "出版社",
        "出版時間", "出版时间",
        "出版日期",

        // ===== 2. Copyright / License =====
        "版權", "版权",
        "版權頁", "版权页",
        "版權信息", "版权信息",

        // ===== 3. Editor / Pricing =====
        "責任編輯", "责任编辑",
        "編輯", "编辑", // 有些出版社簡化成「编辑」
        "責編", "责编", // 等同责任编辑，但常見
        "定價", "定价",

        // ===== 4. Descriptions / Forewords =====
        // "內容簡介", "内容简介",
        // "作者簡介", "作者简介",
        "簡介", "简介",
        "前言",
        "序章",
        "終章", "终章",
        "尾聲", "尾声",
        "後記", "后记",

        // ===== 5. Digital Publishing (ebook platforms) =====
        "品牌方",
        "出品方",
        "授權方", "授权方",
        "電子版權", "数字版权",
        "掃描", "扫描",
        "發行", "发行",
        "OCR",

        // ===== 6. CIP / Cataloging =====
        "CIP",
        "在版編目", "在版编目",
        "分類號", "分类号",
        "主題詞", "主题词",
        "類型", "类型",
        "標簽", "标签",
        "系列",

        // ===== 7. Publishing Cycle =====
        "發行日", "发行日",
        "初版",

        // ===== 8. Common keys without variants =====
        "ISBN"
    };

    // NOTE:
    // MaxMetadataKeyLength is derived from MetadataKeys (single policy owner).
    // Do NOT hardcode or duplicate this limit elsewhere.
    private static readonly int MaxMetadataKeyLength = MetadataKeys.Max(k => k.Length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsMetadataKey(ReadOnlySpan<char> keySpan)
    {
        keySpan = TrimWhitespace(keySpan);
        if (keySpan.Length == 0 || keySpan.Length > MaxMetadataKeyLength)
            return false;

        return MetadataKeys.Contains(keySpan.ToString());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<char> TrimWhitespace(ReadOnlySpan<char> s)
    {
        var start = 0;
        while (start < s.Length && char.IsWhiteSpace(s[start])) start++;

        var end = s.Length - 1;
        while (end >= start && char.IsWhiteSpace(s[end])) end--;

        return s.Slice(start, end - start + 1);
    }

    // =========================================================
    //  Dialog state tracking
    // =========================================================

    /// <summary>
    /// Tracks the state of open or unmatched dialog quotation marks within
    /// the current paragraph buffer during PDF text reflow.
    ///
    /// This class is designed for incremental updates: callers feed each
    /// new line or text fragment into <see cref="Update(System.ReadOnlySpan{char})"/>,
    /// allowing the state to evolve without rescanning previously processed
    /// text. This is essential for maintaining dialog continuity across
    /// broken PDF lines.
    /// </summary>
    private sealed class DialogState
    {
        /// <summary>
        /// Counter for unmatched CJK double quotes: “ ”.
        /// Increments on encountering “ and decrements on ”.
        /// </summary>
        private int _doubleQuote;

        /// <summary>
        /// Counter for unmatched CJK single quotes: ‘ ’.
        /// Increments on encountering ‘ and decrements on ’.
        /// </summary>
        private int _singleQuote;

        /// <summary>
        /// Counter for unmatched CJK corner quotes: 「 」.
        /// Increments on encountering 「 and decrements on 」.
        /// </summary>
        private int _corner;

        /// <summary>
        /// Counter for unmatched CJK bold corner quotes: 『 』.
        /// Increments on encountering 『 and decrements on 』.
        /// </summary>
        private int _cornerBold;

        /// <summary>
        /// Counter for unmatched upper corner brackets: ﹁ ﹂.
        /// </summary>
        private int _cornerTop;

        /// <summary>
        /// Counter for unmatched wide corner brackets: ﹃ ﹄.
        /// </summary>
        private int _cornerWide;

        /// <summary>
        /// Resets all quote counters to zero.
        /// Call this at the start of a new paragraph buffer.
        /// </summary>
        public void Reset()
        {
            _doubleQuote = 0;
            _singleQuote = 0;
            _corner = 0;
            _cornerBold = 0;
            _cornerTop = 0;
            _cornerWide = 0;
        }

        /// <summary>
        /// Updates the dialog state by scanning the provided text fragment.
        ///
        /// Only characters representing CJK dialog punctuation are examined.
        /// Counters are increased for opening quotes and decreased for
        /// closing quotes (never below zero). This incremental approach
        /// avoids rescanning previously processed text and is safe even
        /// when PDF line breaks occur mid-dialog.
        /// </summary>
        /// <param name="s">
        /// A text fragment (typically one line or buffer chunk).
        /// If empty or whitespace-only, the method performs no action.
        /// </param>
        public void Update(ReadOnlySpan<char> s)
        {
            if (s.IsEmpty)
                return;

            for (var i = 0; i < s.Length; i++)
            {
                switch (s[i])
                {
                    // ===== Double quotes =====
                    case '“': _doubleQuote++; break;
                    case '”':
                        if (_doubleQuote > 0) _doubleQuote--;
                        break;

                    // ===== Single quotes =====
                    case '‘': _singleQuote++; break;
                    case '’':
                        if (_singleQuote > 0) _singleQuote--;
                        break;

                    // ===== Corner brackets =====
                    case '「': _corner++; break;
                    case '」':
                        if (_corner > 0) _corner--;
                        break;

                    // ===== Bold corner brackets =====
                    case '『': _cornerBold++; break;
                    case '』':
                        if (_cornerBold > 0) _cornerBold--;
                        break;

                    // ===== vertical brackets (﹁ ﹂) =====
                    case '﹁': _cornerTop++; break;
                    case '﹂':
                        if (_cornerTop > 0) _cornerTop--;
                        break;

                    // ===== vertical bold brackets (﹃ ﹄) =====
                    case '﹃': _cornerWide++; break;
                    case '﹄':
                        if (_cornerWide > 0) _cornerWide--;
                        break;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether any dialog quote type is
        /// currently left unclosed. When <c>true</c>, the current paragraph
        /// buffer is considered to be inside an ongoing dialog segment, and
        /// reflow logic should avoid forcing paragraph breaks until closure.
        /// </summary>
        public bool IsUnclosed =>
            _doubleQuote > 0 || _singleQuote > 0 || _corner > 0 || _cornerBold > 0 || _cornerTop > 0 ||
            _cornerWide > 0;
    }

    /// <summary>
    /// Reflows CJK text extracted from PDF into cleaner paragraphs.
    /// Produces compact or novel-style output depending on <paramref name="compact"/>.
    /// </summary>
    /// <param name="text">Raw extracted text.</param>
    /// <param name="addPdfPageHeader">Whether to keep PDF page headers.</param>
    /// <param name="compact">
    /// If true → compact mode (one line per paragraph, no blank lines).  
    /// If false → novel mode (blank line between paragraphs).
    /// </param>
    internal static string ReflowCjkParagraphs(string text, bool addPdfPageHeader, bool compact = false)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Normalize \r\n and \r into \n for cross-platform stability
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        var lines = text.Split('\n');
        var segments = new List<string>();
        var buffer = new StringBuilder();
        var dialogState = new DialogState();

        foreach (var rawLine in lines)
        {
            // 1) Visual form: keep full-width indent, drop half-width indent on the left, trim only right side
            var stripped = rawLine.TrimEnd();
            stripped = StripHalfWidthIndentKeepFullWidth(stripped);

            // 2) Probe form (for structural / heading detection): remove all indentation
            var probe = stripped.TrimStart(' ', '\u3000');

            // 🧱 ABSOLUTE STRUCTURAL RULE — must be first (run on probe, output stripped)
            if (PunctSets.IsVisualDividerLine(probe))
            {
                if (buffer.Length > 0)
                {
                    segments.Add(buffer.ToString());
                    buffer.Clear();
                    dialogState.Reset();
                }

                segments.Add(stripped);
                continue;
            }

            // 🔹 NEW: collapse style-layer repeated segments *before* heading detection
            stripped = CollapseRepeatedSegments(stripped);

            // 3) Logical form for heading detection: no indent at all
            probe = stripped.TrimStart(' ', '\u3000');

            var isTitleHeading = TitleHeadingRegex.IsMatch(probe);
            var isShortHeading = IsHeadingLike(probe);
            var isMetadata = IsMetadataLine(probe);

            var hasBuffer = buffer.Length > 0;

            // Lazy snapshot for this iteration only
            string? bufferTextLazy = null;

            string BufferText()
            {
                if (!hasBuffer)
                    return string.Empty;

                return bufferTextLazy ??= buffer.ToString();
            }

            bool? hasUnclosedBracketLazy = null;

            bool HasUnclosedBracket()
            {
                if (!hasBuffer)
                    return false;

                return hasUnclosedBracketLazy ??=
                    PunctSets.HasUnclosedBracket(BufferText());
            }

            // 1) Empty line
            if (stripped.Length == 0)
            {
                if (!addPdfPageHeader && buffer.Length > 0)
                {
                    // NEW: If dialog is unclosed, always treat blank line as soft (cross-page artifact).
                    // Never flush mid-dialog just because we saw a blank line.
                    if (dialogState.IsUnclosed || HasUnclosedBracket())
                        continue;

                    // Light rule: only flush on blank line if buffer ends with STRONG sentence end.
                    // Otherwise, treat as a soft cross-page blank line and keep accumulating.
                    if (PunctSets.TryGetLastNonWhitespace(BufferText(), out _, out var last) &&
                        !PunctSets.IsStrongSentenceEnd(last))
                    {
                        continue;
                    }
                }

                // End of paragraph → flush buffer (do NOT emit "")
                if (buffer.Length > 0)
                {
                    segments.Add(BufferText());
                    buffer.Clear();
                    dialogState.Reset();
                }

                // IMPORTANT: Emitting empty segments would introduce
                // hard paragraph boundaries and break cross-line reflow
                continue;
            }

            // 2) Page markers
            if (stripped.StartsWith("=== ") && stripped.EndsWith("==="))
            {
                if (buffer.Length > 0)
                {
                    segments.Add(BufferText());
                    buffer.Clear();
                    dialogState.Reset();
                }

                segments.Add(stripped);
                continue;
            }

            // 3) Titles
            if (isTitleHeading)
            {
                if (buffer.Length > 0)
                {
                    segments.Add(BufferText());
                    buffer.Clear();
                    dialogState.Reset();
                }

                segments.Add(stripped);
                continue;
            }

            // 3b) Metadata 行（短 key:val，如「書名：xxx」「作者：yyy」）
            if (isMetadata)
            {
                if (buffer.Length > 0)
                {
                    segments.Add(BufferText());
                    buffer.Clear();
                    dialogState.Reset();
                }

                // Metadata 每行獨立存放（之後你可以決定係 skip、折疊、顯示）
                segments.Add(stripped);
                continue;
            }

            // 3c) Weak heading-like:
            //     Only takes effect when the “previous paragraph is safe”
            //     AND “the previous paragraph’s ending looks like a sentence boundary”.
            if (isShortHeading)
            {
                var isAllCjk = CjkText.IsAllCjkIgnoringWhitespace(stripped);

                bool splitAsHeading;
                if (buffer.Length == 0)
                {
                    // Start of document / just flushed
                    splitAsHeading = true;
                }
                else
                {
                    if (HasUnclosedBracket())
                    {
                        // Unsafe previous paragraph → must be continuation
                        splitAsHeading = false;
                    }
                    else
                    {
                        if (!PunctSets.TryGetLastNonWhitespace(BufferText(), out _, out var last))
                        {
                            // Buffer is whitespace-only → treat like empty
                            splitAsHeading = true;
                        }
                        else
                        {
                            var prevEndsWithCommaLike = PunctSets.IsCommaLike(last);
                            var prevEndsWithSentencePunct = PunctSets.IsClauseOrEndPunct(last);

                            var currentLooksLikeContinuationMarker =
                                isAllCjk
                                || PunctSets.EndsWithColonLike(stripped)
                                || PunctSets.EndsWithAllowedPostfixCloser(stripped);

                            // Comma-ending → continuation
                            if (prevEndsWithCommaLike)
                                splitAsHeading = false;
                            // All-CJK short heading-like + previous not ended → continuation
                            else if (currentLooksLikeContinuationMarker && !prevEndsWithSentencePunct)
                                splitAsHeading = false;
                            else
                                splitAsHeading = true;
                        }
                    }
                }

                if (splitAsHeading)
                {
                    // If we have a real previous paragraph, flush it first
                    if (buffer.Length > 0)
                    {
                        segments.Add(BufferText());
                        buffer.Clear();
                        dialogState.Reset();
                    }

                    // Current line becomes a standalone heading
                    segments.Add(stripped);
                    continue;
                }

                // else: fall through → normal merge logic below
            }

            // ----- Current line finalizer ------
            switch (buffer.Length)
            {
                // ===== Finalizer: strong sentence end → flush immediately. Do not remove. ===== //
                // If the current line completes a strong sentence, append it and flush immediately.
                case > 0
                    when !dialogState.IsUnclosed
                         && !HasUnclosedBracket()
                         && PunctSets.EndsWithStrongSentenceEnd(stripped):
                    buffer.Append(stripped); // buffer now has new value
                    segments.Add(buffer.ToString()); // This is not old bufferText (it had been updated)
                    buffer.Clear();
                    dialogState.Reset();
                    dialogState.Update(stripped);
                    continue;
                case 0:
                    // 4) First line inside buffer → start of a new paragraph
                    buffer.Append(stripped);
                    dialogState.Reset();
                    dialogState.Update(stripped);
                    continue;
            }

            // *** DIALOG: treat any line that *starts* with a dialog opener as a new paragraph
            var currentIsDialogStart = PunctSets.BeginsWithDialogOpener(stripped);

            // 🔸 NEW RULE: If previous line ends with comma, 
            //     do NOT flush even if this line starts dialog.
            //     (comma-ending means the sentence is not finished)
            if (currentIsDialogStart)
            {
                var shouldFlushPrev = false;

                if (buffer.Length > 0 &&
                    PunctSets.TryGetLastNonWhitespace(BufferText(), out _, out var last))
                {
                    var isContinuation =
                        PunctSets.IsCommaLike(last) ||
                        CjkText.IsCjk(last) ||
                        dialogState.IsUnclosed ||
                        HasUnclosedBracket();

                    shouldFlushPrev = !isContinuation;
                }

                if (shouldFlushPrev)
                {
                    segments.Add(BufferText());
                    buffer.Clear();
                }

                // Start (or continue) the dialog paragraph
                buffer.Append(stripped);
                dialogState.Reset();
                dialogState.Update(stripped);
                continue;
            }

            // 🔸 9b) Dialog end line: ends with dialog closer.
            // Flush when the char before closer is strong end,
            // and bracket safety is satisfied (with a narrow typo override).
            if (PunctSets.TryGetLastNonWhitespace(stripped, out var lastIdx, out var lastCh) &&
                PunctSets.IsDialogCloser(lastCh))
            {
                // Check punctuation right before the closer (e.g., “？” / “。”)
                var punctBeforeCloserIsStrong =
                    PunctSets.TryGetPrevNonWhitespace(stripped, lastIdx, out var prevCh) &&
                    PunctSets.IsClauseOrEndPunct(prevCh);

                // Snapshot bracket safety BEFORE appending current line
                var bufferHasBracketIssue = HasUnclosedBracket();
                var lineHasBracketIssue = PunctSets.HasUnclosedBracket(stripped);

                buffer.Append(stripped);
                dialogState.Update(stripped);

                // Allow flush if:
                // - dialog is closed after this line
                // - punctuation before closer is a strong end
                // - and either:
                //     (a) buffer has no bracket issue, OR
                //     (b) buffer has bracket issue but this line itself is the culprit (OCR/typo),
                //        so allow a dialog-end flush anyway.
                if (!dialogState.IsUnclosed &&
                    punctBeforeCloserIsStrong &&
                    (!bufferHasBracketIssue || lineHasBracketIssue))
                {
                    segments.Add(buffer.ToString()); // new buffer value, not old bufferText
                    buffer.Clear();
                    dialogState.Reset();
                }

                continue;
            }

            // NEW RULE: colon + dialog continuation
            // e.g. "她寫了一行字：" + "「如果連自己都不相信……」"
            // if (PunctSets.EndsWithColonLike(buffer))
            // {
            //     if (stripped.Length > 0 && PunctSets.IsDialogOpener(stripped[0]))
            //     {
            //         buffer.Append(stripped);
            //         dialogState.Update(stripped);
            //         continue;
            //     }
            // }

            // NOTE: we *do* block splits when dialogState.IsUnclosed,
            // so multi-line dialog stays together. Once all quotes are
            // closed, CJK punctuation may end the paragraph as usual.

            switch (dialogState.IsUnclosed)
            {
                // 5) Strong sentence boundary → new paragraph
                // Triggered by full-width CJK sentence-ending punctuation (。！？ etc.)
                // NOTE: Dialog safety gate has the highest priority.
                // If dialog quotes/brackets are not closed, never split the paragraph.
                case false when CjkText.EndsWithSentenceBoundary(BufferText(), level: 2) && !HasUnclosedBracket():

                // 6) Closing CJK bracket boundary → new paragraph
                // Handles cases where a paragraph ends with a full-width closing bracket/quote
                // (e.g. ）】》」) and should not be merged with the next line.
                case false when CjkText.EndsWithCjkBracketBoundary(BufferText()):

                // 7) Indentation → new paragraph
                // Pre-append rule:
                // Indentation indicates a new paragraph starts on this line.
                // Flush the previous buffer and immediately seed the next paragraph.
                case false when buffer.Length > 0 && IndentRegex.IsMatch(rawLine):
                    segments.Add(BufferText());
                    buffer.Clear();
                    buffer.Append(stripped);
                    dialogState.Reset();
                    dialogState.Update(stripped);
                    continue;
            }

            // 8) Chapter-like endings: 章 / 节 / 部 / 卷 (with trailing brackets)
            // if (buffer.Length <= 12 &&
            //     Regex.IsMatch(buffer.ToString(), @"(章|节|部|卷|節|回)[】》〗〕〉」』）]*$"))
            // {
            //     segments.Add(buffer.ToString());
            //     buffer.Clear();
            //     buffer.Append(stripped);
            //     dialogState.Reset();
            //     dialogState.Update(stripped);
            //     continue;
            // }

            // 9) Default merge (soft line break)
            buffer.Append(stripped);
            dialogState.Update(stripped);
        }

        // flush the final buffer
        if (buffer.Length > 0)
            segments.Add(buffer.ToString());

        // Formatting:
        // compact → "p1\np2\np3"
        // novel   → "p1\n\np2\n\np3"
        return compact
            ? string.Join("\n", segments)
            : string.Join("\n\n", segments);


        // ====== Inline helpers ======

        // ------ heading-Like ------
        static bool IsHeadingLike(ReadOnlySpan<char> s)
        {
            s = s.Trim();
            if (s.IsEmpty)
                return false;

            // keep page markers intact
            if (s.StartsWith("=== ", StringComparison.Ordinal) && s.EndsWith("===", StringComparison.Ordinal))
                return false;

            // Reject headings with unclosed brackets (「『“”( 等未配對)
            if (PunctSets.HasUnclosedBracket(s))
                return false;

            // last non-whitespace (more robust than s[^1])
            if (!PunctSets.TryGetLastNonWhitespace(s, out var lastIdx, out var last))
                return false; // whitespace-only

            // If wrapped by matching bracket, accept if mostly CJK
            if (PunctSets.IsWrappedByMatchingBracket(s, last) && CjkText.IsMostlyCjk(s))
                return true;

            var maxLen = CjkText.IsAllAscii(s) || CjkText.IsMixedCjkAscii(s)
                ? 16
                : 8;

            var len = s.Length;

            // Short circuit for item title-like: "物品准备："
            if ((last is ':' or '：') &&
                len <= maxLen &&
                lastIdx > 0 && // need at least one char before ':'
                CjkText.IsAllCjkNoWhiteSpace(s[..lastIdx]))
            {
                return true;
            }

            // Allow postfix closer with condition
            if (PunctSets.IsAllowedPostfixCloser(last) && !PunctSets.ContainsAnyCommaLike(s))
            {
                return true;
            }

            // if (Array.IndexOf(CjkPunctEndChars, last) >= 0)
            if (PunctSets.IsClauseOrEndPunct(last))
                return false;

            // Reject any short line containing comma-like separators
            if (PunctSets.ContainsAnyCommaLike(s))
                return false;

            // Short line heuristics (<= maxLen chars)
            if (len > maxLen) return false;
            var hasNonAscii = false;
            var allAscii = true;
            var hasLetter = false;
            var allAsciiDigits = true;

            for (var i = 0; i < len; i++)
            {
                var ch = s[i];

                if (ch > 0x7F)
                {
                    hasNonAscii = true;
                    allAscii = false;
                    allAsciiDigits = false;
                    continue;
                }

                if (!char.IsDigit(ch))
                    allAsciiDigits = false;

                if (char.IsLetter(ch))
                    hasLetter = true;
            }

            // Rule C: pure ASCII digits (1, 007, 23, 128 ...) → heading
            if (allAsciiDigits)
                return true;

            // Rule A: CJK/mixed short line, not ending with comma
            if (hasNonAscii && !PunctSets.IsCommaLike(last))
                return true;

            // Rule B: pure ASCII short line with at least one letter (PROLOGUE / END)
            return allAscii && hasLetter;
        }

        // ------ Metadata ------
        static bool IsMetadataLine(ReadOnlySpan<char> line)
        {
            if (line.IsEmpty)
                return false;

            // Equivalent to string.IsNullOrWhiteSpace
            var firstNonWs = 0;
            while (firstNonWs < line.Length && char.IsWhiteSpace(line[firstNonWs]))
                firstNonWs++;

            if (firstNonWs >= line.Length)
                return false;

            // original: if (line.Length > 30) return false;
            // (keep semantics: uses raw line length, not trimmed length)
            if (line.Length > 30)
                return false;

            var idx = -1; // separator index
            var j = -1; // first non-ws after separator

            for (var i = firstNonWs; i < line.Length; i++)
            {
                if (!PunctSets.IsMetadataSeparator(line[i]))
                    continue;

                idx = i;

                j = i + 1;
                while (j < line.Length && char.IsWhiteSpace(line[j]))
                    j++;

                break;
            }

            // must have a separator and content after it
            if (idx < 0 || j < 0 || j >= line.Length)
                return false;

            // structural early reject (ignore leading whitespace)
            var rawKeyLen = idx - firstNonWs;
            if (rawKeyLen <= 0 || rawKeyLen > MaxMetadataKeyLength)
                return false;

            // semantic owner
            if (!IsMetadataKey(line.Slice(firstNonWs, rawKeyLen)))
                return false;

            // Must not start value with dialog opener
            return !PunctSets.IsDialogOpener(line[j]);
        }
    }

    private static string StripHalfWidthIndentKeepFullWidth(string s)
    {
        var i = 0;

        // Strip only halfwidth spaces at left
        while (i < s.Length && s[i] == ' ')
            i++;

        return s[i..];
    }

    private static string CollapseRepeatedSegments(string line)
    {
        if (string.IsNullOrEmpty(line))
            return line;

        // Split on whitespace into chunks (titles often have 1–3 parts)
        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return line;

        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = CollapseRepeatedToken(parts[i]);
        }

        // Re-join with a single space between tokens
        return string.Join(" ", parts);
    }

    private static string CollapseRepeatedToken(string token)
    {
        // Very short tokens or huge ones are unlikely to be styled repeats.
        if (token.Length is < 4 or > 100)
            return token;

        var span = token.AsSpan();

        // Try unit sizes between 4 and 10 chars, and require at least
        // 3 repeats (N >= 3). This corresponds roughly to a pattern like:
        //
        //   (.{4,10}?)\1{2,}
        //
        // but constrained to exactly fill the entire token.
        for (var unitLen = 4; unitLen <= 10 && unitLen <= span.Length / 3; unitLen++)
        {
            if (span.Length % unitLen != 0)
                continue;

            var unit = span[..unitLen];
            var allMatch = true;

            for (var pos = unitLen; pos < span.Length; pos += unitLen)
            {
                if (span.Slice(pos, unitLen).SequenceEqual(unit))
                    continue;

                allMatch = false;
                break;
            }

            if (allMatch)
            {
                // Token is just [unit] repeated N times (N >= 3):
                // collapse it to a single unit.
                return unit.ToString();
            }
        }

        return token;
    }
}