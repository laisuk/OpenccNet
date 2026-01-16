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
    /// new line or text fragment into <see cref="Update(string?)"/>,
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
        /// If <c>null</c> or empty, the method performs no action.
        /// </param>
        public void Update(string? s)
        {
            if (string.IsNullOrEmpty(s))
                return;

            foreach (var ch in s)
            {
                switch (ch)
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

                    // ===== NEW: vertical brackets (﹁ ﹂) =====
                    case '﹁': _cornerTop++; break;
                    case '﹂':
                        if (_cornerTop > 0) _cornerTop--;
                        break;

                    // ===== NEW: vertical bold brackets (﹃ ﹄) =====
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

            // 3) Logical form for heading detection: no indent at all
            var headingProbe = stripped.TrimStart(' ', '\u3000');

            var isTitleHeading = TitleHeadingRegex.IsMatch(headingProbe);
            var isShortHeading = IsHeadingLike(stripped);
            var isMetadata = IsMetadataLine(stripped);

            // Collapse style-layer repeated titles
            if (isTitleHeading)
                stripped = CollapseRepeatedSegments(stripped);

            // We already have some text in buffer
            var bufferText = buffer.Length > 0 ? buffer.ToString() : string.Empty;
            var hasUnclosedBracket = buffer.Length > 0 && PunctSets.HasUnclosedBracket(bufferText);

            // 1) Empty line
            if (stripped.Length == 0)
            {
                if (!addPdfPageHeader && buffer.Length > 0)
                {
                    // NEW: If dialog is unclosed, always treat blank line as soft (cross-page artifact).
                    // Never flush mid-dialog just because we saw a blank line.
                    if (dialogState.IsUnclosed || hasUnclosedBracket)
                        continue;

                    // Light rule: only flush on blank line if buffer ends with STRONG sentence end.
                    // Otherwise, treat as a soft cross-page blank line and keep accumulating.
                    if (PunctSets.TryGetLastNonWhitespace(bufferText, out var last) &&
                        !PunctSets.IsStrongSentenceEnd(last))
                    {
                        continue;
                    }
                }

                // End of paragraph → flush buffer (do NOT emit "")
                if (buffer.Length > 0)
                {
                    segments.Add(bufferText);
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
                    segments.Add(bufferText);
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
                    segments.Add(bufferText);
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
                    segments.Add(bufferText);
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
                var isAllCjk = IsAllCjkIgnoringWhitespace(stripped);

                bool splitAsHeading;
                if (buffer.Length == 0)
                {
                    // Start of document / just flushed
                    splitAsHeading = true;
                }
                else
                {
                    if (hasUnclosedBracket)
                    {
                        // Unsafe previous paragraph → must be continuation
                        splitAsHeading = false;
                    }
                    else
                    {
                        if (!PunctSets.TryGetLastNonWhitespace(bufferText, out var last))
                        {
                            // Buffer is whitespace-only → treat like empty
                            splitAsHeading = true;
                        }
                        else
                        {
                            var prevEndsWithCommaLike = PunctSets.IsCommaLike(last);
                            var prevEndsWithSentencePunct = PunctSets.IsClauseOrEndPunct(last);

                            // Comma-ending → continuation
                            if (prevEndsWithCommaLike)
                                splitAsHeading = false;
                            // All-CJK short heading-like + previous not ended → continuation
                            else if (isAllCjk && !prevEndsWithSentencePunct)
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
                        segments.Add(bufferText);
                        buffer.Clear();
                        dialogState.Reset();
                    }

                    // Current line becomes a standalone heading
                    segments.Add(stripped);
                    continue;
                }

                // else: fall through → normal merge logic below
            }

            // ===== Finalizer: strong sentence end → flush immediately. Do not remove. ===== //
            // If the current line completes a strong sentence, append it and flush immediately.
            if (buffer.Length > 0
                && !dialogState.IsUnclosed
                && !hasUnclosedBracket
                && PunctSets.EndsWithStrongSentenceEnd(stripped))
            {
                buffer.Append(stripped); // buffer now has new value
                segments.Add(buffer.ToString()); // This is not old bufferText (it had been updated)
                buffer.Clear();
                dialogState.Reset();
                dialogState.Update(stripped);
                continue;
            }

            // *** DIALOG: treat any line that *starts* with a dialog opener as a new paragraph
            var currentIsDialogStart = PunctSets.IsDialogStarter(stripped);

            if (buffer.Length == 0)
            {
                // 4) First line inside buffer → start of a new paragraph
                buffer.Append(stripped);
                dialogState.Reset();
                dialogState.Update(stripped);
                continue;
            }

            // We already have some text in buffer
            // var bufferText = buffer.ToString();

            // 🔸 NEW RULE: If previous line ends with comma, 
            //     do NOT flush even if this line starts dialog.
            //     (comma-ending means the sentence is not finished)
            if (currentIsDialogStart)
            {
                var shouldFlushPrev = false;

                if (bufferText.Length > 0 &&
                    PunctSets.TryGetLastNonWhitespace(bufferText, out var last))
                {
                    var isContinuation =
                        PunctSets.IsCommaLike(last) ||
                        IsCjk(last) ||
                        dialogState.IsUnclosed ||
                        hasUnclosedBracket;

                    shouldFlushPrev = !isContinuation;
                }

                if (shouldFlushPrev)
                {
                    segments.Add(bufferText);
                    buffer.Clear();
                }

                // Start (or continue) the dialog paragraph
                buffer.Append(stripped);
                dialogState.Reset();
                dialogState.Update(stripped);
                continue;
            }

            // NEW RULE: colon + dialog continuation
            // e.g. "她寫了一行字：" + "「如果連自己都不相信……」"
            if (bufferText.EndsWith('：') || bufferText.EndsWith(':'))
            {
                if (stripped.Length > 0 && PunctSets.IsDialogOpener(stripped[0]))
                {
                    buffer.Append(stripped);
                    dialogState.Update(stripped);
                    continue;
                }
            }

            // NOTE: we *do* block splits when dialogState.IsUnclosed,
            // so multi-line dialog stays together. Once all quotes are
            // closed, CJK punctuation may end the paragraph as usual.

            switch (dialogState.IsUnclosed)
            {
                // 5) Strong sentence boundary → new paragraph
                // Triggered by full-width CJK sentence-ending punctuation (。！？ etc.)
                // NOTE: Dialog safety gate has the highest priority.
                // If dialog quotes/brackets are not closed, never split the paragraph.
                case false when EndsWithSentenceBoundary(bufferText, level: 2) && !hasUnclosedBracket:

                // 6) Closing CJK bracket boundary → new paragraph
                // Handles cases where a paragraph ends with a full-width closing bracket/quote
                // (e.g. ）】》」) and should not be merged with the next line.
                case false when EndsWithCjkBracketBoundary(bufferText):

                // 7) Indentation → new paragraph
                // Pre-append rule:
                // Indentation indicates a new paragraph starts on this line.
                // Flush the previous buffer and immediately seed the next paragraph.
                case false when buffer.Length > 0 && IndentRegex.IsMatch(rawLine):
                    segments.Add(bufferText);
                    buffer.Clear();
                    buffer.Append(stripped);
                    dialogState.Reset();
                    dialogState.Update(stripped);
                    continue;
            }

            // 8) Chapter-like endings: 章 / 节 / 部 / 卷 (with trailing brackets)
            if (bufferText.Length <= 12 &&
                Regex.IsMatch(bufferText, @"(章|节|部|卷|節|回)[】》〗〕〉」』）]*$"))
            {
                segments.Add(bufferText);
                buffer.Clear();
                buffer.Append(stripped);
                dialogState.Reset();
                dialogState.Update(stripped);
                continue;
            }

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

        static bool IsHeadingLike(string? s)
        {
            if (s is null)
                return false;

            s = s.Trim();
            if (string.IsNullOrEmpty(s))
                return false;

            // keep page markers intact
            if (s.StartsWith("=== ") && s.EndsWith("==="))
                return false;

            // Reject headings with unclosed brackets (「『“”( 等未配對)
            if (PunctSets.HasUnclosedBracket(s))
                return false;

            // If *ends* with CJK punctuation → not heading
            var last = s[^1];
            var len = s.Length;

            if (len > 2 && PunctSets.IsMatchingBracket(s[0], last) && IsMostlyCjk(s)) return true;

            var maxLen = IsAllAscii(s) || IsMixedCjkAscii(s)
                ? 16
                : 8;

            // Short circuit for item title-like: "物品准备："
            if ((last is ':' or '：') && s.Length <= maxLen && IsAllCjkNoWhiteSpace(s[..^1]))
                return true;

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

        static bool IsMetadataLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            if (line.Length > 30)
                return false;

            var firstNonWs = 0;
            while (firstNonWs < line.Length && char.IsWhiteSpace(line[firstNonWs]))
                firstNonWs++;

            var idx = -1;
            var j = -1;

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

            // structural early reject (ignore leading whitespace)
            var rawKeyLen = idx - firstNonWs;
            if (rawKeyLen <= 0 || rawKeyLen > MaxMetadataKeyLength)
                return false;

            if (j < 0 || j >= line.Length)
                return false;

            // semantic owner
            if (!IsMetadataKey(line.AsSpan(firstNonWs, rawKeyLen)))
                return false;

            return !PunctSets.IsDialogOpener(line[j]);
        }

        static bool EndsWithSentenceBoundary(string s, int level = 2)
        {
            if (string.IsNullOrWhiteSpace(s))
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

            // 3) Bracket closers with most CJK (reserved)
            // if (PunctSets.IsBracketCloser(last) && lastIdx > 0 && IsMostlyCjk(s))
            //     return true;

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

        static bool EndsWithCjkBracketBoundary(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return false;

            s = s.Trim();
            if (s.Length < 2)
                return false;

            var open = s[0];
            var close = s[^1];

            // 1) Must be one of our known pairs
            if (!PunctSets.IsMatchingBracket(open, close))
                return false;

            // 2) Must be mostly CJK to avoid "(test)" "[1.2]" etc.
            return IsMostlyCjk(s) &&
                   // 3) Ensure this bracket type is balanced inside the line
                   //    (prevents premature close / malformed OCR)
                   IsBracketTypeBalanced(s, open, close);
        }

        static bool IsBracketTypeBalanced(string s, char open, char close)
        {
            var depth = 0;

            foreach (var ch in s)
            {
                if (ch == open) depth++;
                else if (ch == close)
                {
                    depth--;
                    if (depth < 0) return false; // closing before opening
                }
            }

            return depth == 0;
        }

        static bool EndsWithEllipsis(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
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

        // Strict: the ASCII punct itself is the last non-whitespace char (level 3 strict rules).
        static bool IsOcrCjkAsciiPunctAtLineEnd(string s, int lastNonWsIndex)
        {
            if (lastNonWsIndex <= 0)
                return false;

            return IsCjk(s[lastNonWsIndex - 1]) && IsMostlyCjk(s);
        }

        // Relaxed "end": after index, only whitespace and closers are allowed.
        // Needed for patterns like: CJK '.' then closing quote/bracket: “。”  .」  .）
        static bool IsOcrCjkAsciiPunctBeforeClosers(string s, int index)
        {
            if (!IsAtEndAllowingClosers(s, index))
                return false;

            if (index <= 0)
                return false;

            return IsCjk(s[index - 1]) && IsMostlyCjk(s);
        }

        static bool IsAtEndAllowingClosers(string s, int index)
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

        // static int FindLastNonWhitespaceIndex(string s)
        // {
        //     for (var i = s.Length - 1; i >= 0; i--)
        //         if (!char.IsWhiteSpace(s[i]))
        //             return i;
        //     return -1;
        // }

        // static int FindPrevNonWhitespaceIndex(string s, int endExclusive)
        // {
        //     for (var j = endExclusive - 1; j >= 0; j--)
        //         if (!char.IsWhiteSpace(s[j]))
        //             return j;
        //     return -1;
        // }

        // static bool IsStrongSentenceEnd(char ch) =>
        //     ch is '。' or '！' or '？' or '!' or '?';

        static bool IsAllAscii(string s)
        {
            for (var i = 0; i < s.Length; i++)
                if (s[i] > 0x7F)
                    return false;
            return true;
        }

        // static bool IsAllAsciiDigits(string s)
        // {
        //     var hasDigit = false;
        //
        //     for (var i = 0; i < s.Length; i++)
        //     {
        //         var ch = s[i];
        //
        //         switch (ch)
        //         {
        //             // ASCII space is neutral
        //             case ' ':
        //                 continue;
        //             // ASCII digits
        //             case >= '0' and <= '9':
        //             // FULLWIDTH digits
        //             case >= '０' and <= '９':
        //                 hasDigit = true;
        //                 continue;
        //             default:
        //                 // anything else -> reject
        //                 return false;
        //         }
        //     }
        //
        //     return hasDigit;
        // }

        // Minimal CJK checker (BMP focused). You can swap with your existing one.
        static bool IsCjk(char ch)
        {
            var c = (int)ch;

            // CJK Unified Ideographs + Extension A
            if ((uint)(c - 0x3400) <= (0x4DBF - 0x3400)) return true;
            if ((uint)(c - 0x4E00) <= (0x9FFF - 0x4E00)) return true;

            // Compatibility Ideographs
            return (uint)(c - 0xF900) <= (0xFAFF - 0xF900);
        }

        // Returns true if the string consists entirely of CJK characters.
        // Whitespace handling is controlled by allowWhitespace.
        // Returns false for null, empty, or whitespace-only strings.
        static bool IsAllCjk(string s, bool allowWhitespace = false)
        {
            var seen = false;

            foreach (var ch in s)
            {
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

        static bool IsAllCjkIgnoringWhitespace(string s)
            => IsAllCjk(s, allowWhitespace: true);

        static bool IsAllCjkNoWhiteSpace(string s)
            => IsAllCjk(s, allowWhitespace: false);

        static bool IsMixedCjkAscii(string s)
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
                    {
                        hasAscii = true;
                    }
                    else
                    {
                        return false;
                    }
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

        static bool IsMostlyCjk(string s)
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

        static bool IsDigitAsciiOrFullWidth(char ch)
        {
            // ASCII digits
            if ((uint)(ch - '0') <= 9) return true;
            // FULLWIDTH digits
            return (uint)(ch - '０') <= 9;
        }
    }

    /// <summary>
    /// Detects visual separator / divider lines such as:
    /// ──────
    /// ======
    /// ------
    /// or mixed variants (e.g. ───===───).
    /// 
    /// This method is intended to run on a *probe* string
    /// (indentation already removed). Whitespace is ignored.
    /// 
    /// These lines represent layout boundaries and must always
    /// force paragraph breaks during reflow.
    /// </summary>
    // private static bool IsBoxDrawingLine(string s)
    // {
    //     if (string.IsNullOrWhiteSpace(s))
    //         return false;
    //
    //     var total = 0;
    //
    //     foreach (var ch in s)
    //     {
    //         // Ignore whitespace completely (probe may still contain gaps)
    //         if (char.IsWhiteSpace(ch))
    //             continue;
    //
    //         total++;
    //
    //         // Unicode box drawing block (U+2500–U+257F)
    //         if (ch is >= '\u2500' and <= '\u257F')
    //             continue;
    //
    //         // ASCII visual separators (common in TXT / OCR)
    //         if (ch is '-' or '=' or '_' or '~' or '～')
    //             continue;
    //
    //         // Star / asterisk-based visual dividers
    //         if (ch is '*' // ASTERISK (U+002A)
    //             or '＊' // FULLWIDTH ASTERISK (U+FF0A)
    //             or '★' // BLACK STAR (U+2605)
    //             or '☆' // WHITE STAR (U+2606)
    //            )
    //             continue;
    //
    //         // Any real text → not a pure visual divider
    //         return false;
    //     }
    //
    //     // Require minimal visual length to avoid accidental triggers
    //     return total >= 3;
    // }
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
        // Very short tokens or huge ones are unlikely to be styled repeats
        if (token.Length is < 4 or > 200)
            return token;

        // Try unit sizes between 2 and 20 chars
        // Enough for things like "第十九章", "信的故事", etc.
        for (var unitLen = 2; unitLen <= 20 && unitLen <= token.Length / 2; unitLen++)
        {
            if (token.Length % unitLen != 0)
                continue;

            var unit = token[..unitLen];
            var allMatch = true;

            for (var pos = 0; pos < token.Length; pos += unitLen)
            {
                if (token.AsSpan(pos, unitLen).SequenceEqual(unit)) continue;
                allMatch = false;
                break;
            }

            if (allMatch)
            {
                // token is just unit repeated N times, collapse to a single unit
                return unit;
            }
        }

        return token;
    }
}