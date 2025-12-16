using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace OpenccNet
{
    /// <summary>
    /// Specifies which PDF text extraction engine to use.
    /// </summary>
    public enum PdfEngine
    {
        /// <summary>
        /// Uses the PdfPig backend for text extraction.
        /// Suitable for general-purpose parsing and stable for
        /// most text-embedded PDFs.  
        /// Pure managed code, no native dependencies.
        /// </summary>
        PdfPig,

        /// <summary>
        /// Uses the PDFium backend for text extraction.
        /// Faster and more robust against complex page structures,
        /// vector overlays, rotated text, or unusual PDF layouts.  
        /// Requires native PDFium runtime libraries.
        /// </summary>
        Pdfium
    }

    internal static class PdfHelper
    {
        // CJK-aware punctuation set (used for paragraph detection)
        private static readonly char[] CjkPunctEndChars =
        {
            // Standard CJK sentence-ending punctuation
            '。', '！', '？', '；', '：', '…', '—', '”', '」', '’', '』', '.',

            // Chinese closing brackets / quotes
            '）', '】', '》', '〗', '〕', '〉', '」', '』', '］', '｝', ')', ':', '!'
        };

        // Chapter / heading patterns (短行 + 第N章/卷/节/部, 前言/序章/终章/尾声/番外)
        private static readonly Regex TitleHeadingRegex =
            new(
                @"^(?=.{0,50}$)
                  (前言|序章|终章|尾声|后记|尾聲|後記|番外.{0,15}
                  |.{0,10}?第.{0,5}?([章节部卷節回][^分合]).{0,20}?
                  )",
                RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace);

        //Paragraph indentation
        private static readonly Regex IndentRegex =
            new(@"^[\s\u3000]{2,}", RegexOptions.Compiled);

        // Dialog brackets (Simplified / Traditional / JP-style)
        private const string DialogOpeners = "“‘「『﹁﹃";

        private static bool IsDialogOpener(char ch)
            => DialogOpeners.Contains(ch);

        // Bracket punctuations (open-close)
        private const string OpenBrackets = "（([【《｛〈";
        private const string CloseBrackets = "）)]】》｝〉";

        // Metadata key-value separators
        private static readonly char[] MetadataSeparators =
        {
            '：', // full-width colon
            ':', // ASCII colon
            '　', // full-width ideographic space (U+3000)
            '・' // full-width ideographic dot (U+3000)
        };

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
        /// Asynchronously loads a PDF file and extracts plain text from all pages,
        /// with optional page headers and real-time progress reporting.
        ///
        /// This method runs the extraction work on a background thread using
        /// <see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)"/> and is suitable
        /// for UI applications that must remain responsive while processing
        /// large PDFs.
        ///
        /// Extraction uses PdfPig’s <see cref="ContentOrderTextExtractor"/> to obtain
        /// text in a visually ordered manner. No layout information (fonts,
        /// positions, spacing) is preserved—only text content.
        /// </summary>
        /// <param name="filename">
        /// Full path to the PDF file to load.
        /// </param>
        /// <param name="addPdfPageHeader">
        /// If <c>true</c>, each extracted page is prefixed with a marker in the
        /// form <c>=== [Page X/Y] ===</c>, which is useful for debugging or for
        /// downstream paragraph-reflow heuristics that rely on page boundaries.
        /// </param>
        /// <param name="statusCallback">
        /// Optional callback invoked periodically with human-readable progress
        /// messages (e.g. <c>"Loading PDF [#####-----] 45%"</c>).  
        ///  
        /// The callback is triggered:
        /// <list type="bullet">
        ///   <item><description>at page 1,</description></item>
        ///   <item><description>at the last page,</description></item>
        ///   <item><description>and every adaptive interval determined by the
        ///     total page count.</description></item>
        /// </list>
        /// This is typically used to update a UI status bar.
        /// </param>
        /// <param name="cancellationToken">
        /// Token used to cancel the operation.  
        /// If cancellation is requested, a <see cref="OperationCanceledException"/>
        /// is thrown immediately.
        /// </param>
        /// <returns>
        /// A task representing the asynchronous operation, returning the fully
        /// concatenated plain-text content of the PDF file.
        ///
        /// Line breaks are normalized, trailing whitespace is trimmed per page,
        /// and an empty string is returned for PDFs with zero pages.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The adaptive progress interval scales based on page count:
        /// <list type="bullet">
        ///   <item><description>≤ 20 pages → update every page</description></item>
        ///   <item><description>≤ 100 pages → every 3 pages</description></item>
        ///   <item><description>≤ 300 pages → every 5 pages</description></item>
        ///   <item><description>&gt; 300 pages → ~5% of total pages</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// Because the underlying extraction is CPU-bound and potentially slow,
        /// this method should always be awaited to prevent blocking the caller's
        /// thread.
        /// </para>
        /// </remarks>
        internal static Task<string> LoadPdfTextAsync(
            string filename,
            bool addPdfPageHeader,
            Action<string>? statusCallback = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filename))
                throw new ArgumentException("PDF path is required.", nameof(filename));

            return Task.Run(() =>
            {
                using var document = PdfDocument.Open(filename);

                var sb = new StringBuilder();
                var total = document.NumberOfPages;

                if (total <= 0)
                {
                    statusCallback?.Invoke("PDF has no pages.");
                    return string.Empty;
                }

                // Adaptive progress update interval
                static int GetProgressBlock(int totalPages)
                {
                    return totalPages switch
                    {
                        <= 20 => 1,
                        <= 100 => 3,
                        <= 300 => 5,
                        _ => Math.Max(1, totalPages / 20)
                    };

                    // large PDFs: ~5% intervals
                }

                var block = GetProgressBlock(total);

                for (var i = 1; i <= total; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Progress callback
                    if (i % block == 0 || i == 1 || i == total)
                    {
                        var percent = (int)((double)i / total * 100);
                        statusCallback?.Invoke(
                            $"Loading PDF {BuildProgressBar(percent)}  {percent}%");
                    }

                    var page = document.GetPage(i);
                    var text = ContentOrderTextExtractor.GetText(page);

                    text = text.Trim('\r', '\n', ' ');

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        if (addPdfPageHeader)
                            sb.AppendLine($"=== [Page {i}/{total}] ===");

                        sb.AppendLine(); // visible blank page separator
                        continue;
                    }

                    if (addPdfPageHeader)
                        sb.AppendLine($"=== [Page {i}/{total}] ===");

                    sb.AppendLine(text);
                    sb.AppendLine();
                }

                return sb.ToString();
            }, cancellationToken);
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
                if (IsBoxDrawingLine(probe))
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

                // 1) Empty line
                if (stripped.Length == 0)
                {
                    if (!addPdfPageHeader && buffer.Length > 0)
                    {
                        var lastChar = buffer[^1];

                        // Page-break-like blank line, skip it
                        if (Array.IndexOf(CjkPunctEndChars, lastChar) < 0)
                            continue;
                    }

                    // End of paragraph → flush buffer, do not add ""
                    if (buffer.Length > 0)
                    {
                        segments.Add(buffer.ToString());
                        buffer.Clear();
                        dialogState.Reset();
                    }

                    continue;
                }

                // 2) Page markers
                if (stripped.StartsWith("=== ") && stripped.EndsWith("==="))
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

                // 3) Titles
                if (isTitleHeading)
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

                // 3b) Metadata 行（短 key:val，如「書名：xxx」「作者：yyy」）
                if (isMetadata)
                {
                    if (buffer.Length > 0)
                    {
                        segments.Add(buffer.ToString());
                        buffer.Clear();
                        dialogState.Reset();
                    }

                    // Metadata 每行獨立存放（之後你可以決定係 skip、折疊、顯示）
                    segments.Add(stripped);
                    continue;
                }

                // 3c) 弱 heading-like：只在上一段尾不是逗號時才生效
                if (isShortHeading)
                {
                    if (buffer.Length > 0)
                    {
                        var bt = buffer.ToString().TrimEnd();
                        if (bt.Length > 0)
                        {
                            var last = bt[^1];
                            if (last is '，' or ',' or '、')
                            {
                                // 上一行逗號結尾 → 視作續句，不當 heading
                                // fall through → 後面 default merge 邏輯處理
                            }
                            else
                            {
                                // 真 heading-like → flush
                                segments.Add(buffer.ToString());
                                buffer.Clear();
                                dialogState.Reset();
                                segments.Add(stripped);
                                continue;
                            }
                        }
                        else
                        {
                            // buffer 有長度但全空白，其實等同無 → 直接當 heading
                            segments.Add(stripped);
                            continue;
                        }
                    }
                    else
                    {
                        // buffer 空 → 直接當 heading
                        segments.Add(stripped);
                        continue;
                    }
                }

                // *** DIALOG: treat any line that *starts* with a dialog opener as a new paragraph
                var currentIsDialogStart = IsDialogStarter(stripped);

                if (buffer.Length == 0)
                {
                    // 4) First line inside buffer → start of a new paragraph
                    buffer.Append(stripped);
                    dialogState.Reset();
                    dialogState.Update(stripped);
                    continue;
                }

                // We already have some text in buffer
                var bufferText = buffer.ToString();

                // 🔸 NEW RULE: If previous line ends with comma, 
                //     do NOT flush even if this line starts dialog.
                //     (comma-ending means the sentence is not finished)
                if (bufferText.Length > 0)
                {
                    var trimmed = bufferText.TrimEnd();
                    var last = trimmed.Length > 0 ? trimmed[^1] : '\0';
                    if (last is '，' or ',')
                    {
                        // fall through → treat as continuation
                        // do NOT flush here
                    }
                    else if (currentIsDialogStart)
                    {
                        // *** DIALOG: if this line starts a dialog, 
                        //     flush previous paragraph (only if safe)
                        segments.Add(bufferText);
                        buffer.Clear();
                        buffer.Append(stripped);
                        dialogState.Reset();
                        dialogState.Update(stripped);
                        continue;
                    }
                }
                else
                {
                    // buffer empty, just add new dialog line
                    if (currentIsDialogStart)
                    {
                        buffer.Append(stripped);
                        dialogState.Reset();
                        dialogState.Update(stripped);
                        continue;
                    }
                }

                // NEW RULE: colon + dialog continuation
                // e.g. "她寫了一行字：" + "「如果連自己都不相信……」"
                if (bufferText.EndsWith('：') || bufferText.EndsWith(':'))
                {
                    if (stripped.Length > 0 && DialogOpeners.Contains(stripped[0]))
                    {
                        buffer.Append(stripped);
                        dialogState.Update(stripped);
                        continue;
                    }
                }

                // NOTE: we *do* block splits when dialogState.IsUnclosed,
                // so multi-line dialog stays together. Once all quotes are
                // closed, CJK punctuation may end the paragraph as usual.

                // 5) Ends with CJK punctuation → new paragraph
                if (Array.IndexOf(CjkPunctEndChars, bufferText[^1]) >= 0 &&
                    !dialogState.IsUnclosed)
                {
                    segments.Add(bufferText);
                    buffer.Clear();
                    buffer.Append(stripped);
                    dialogState.Reset();
                    dialogState.Update(stripped);
                    continue;
                }

                // 7) Indentation → new paragraph
                if (IndentRegex.IsMatch(rawLine))
                {
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

            // Helper: does this line start with a dialog opener? (full-width quotes)
            static bool IsDialogStarter(string s)
            {
                s = s.TrimStart(' ', '\u3000'); // ignore indent
                return s.Length > 0 && DialogOpeners.Contains(s[0]);
            }

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
                if (HasUnclosedBracket(s))
                    return false;

                // If *ends* with CJK punctuation → not heading
                var last = s[^1];

                var len = s.Length;
                var maxLen = IsAllAscii(s) || IsMixedCjkAscii(s) ? 16 : 8;

                // Short circuit for item title-like: "物品准备："
                if ((last is ':' or '：') && s.Length <= maxLen && IsAllCjk(s[..^1]))
                    return true;

                if (Array.IndexOf(CjkPunctEndChars, last) >= 0)
                    return false;

                // Reject any short line containing comma-like separators
                if (s.Contains('，') || s.Contains(',') || s.Contains('、'))
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
                if (hasNonAscii && last != '，' && last != ',')
                    return true;

                // Rule B: pure ASCII short line with at least one letter (PROLOGUE / END)
                return allAscii && hasLetter;
            }

            static bool IsMetadataLine(string line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    return false;

                // A) length limit
                if (line.Length > 30)
                    return false;

                // B) find first separator
                var idx = line.IndexOfAny(MetadataSeparators);
                if (idx is <= 0 or > 10)
                    return false;

                // C) extract key
                var key = line[..idx].Trim();
                if (!MetadataKeys.Contains(key))
                    return false;

                // D) get next non-space character
                var j = idx + 1;
                while (j < line.Length && char.IsWhiteSpace(line[j]))
                    j++;

                if (j >= line.Length)
                    return false;

                // E) must NOT be dialog opener
                return !IsDialogOpener(line[j]);
            }

            // Check if any unclosed brackets in text string
            static bool HasUnclosedBracket(string s)
            {
                if (string.IsNullOrEmpty(s))
                    return false;

                var hasOpen = false;
                var hasClose = false;

                foreach (var ch in s)
                {
                    if (!hasOpen && OpenBrackets.Contains(ch)) hasOpen = true;
                    if (!hasClose && CloseBrackets.Contains(ch)) hasClose = true;

                    if (hasOpen && hasClose)
                        break;
                }

                return hasOpen && !hasClose;
            }

            static bool IsAllAscii(string s)
            {
                for (var i = 0; i < s.Length; i++)
                    if (s[i] > 0x7F)
                        return false;
                return true;
            }

            // static bool IsAllAsciiDigits(string s)
            // {
            //     for (var i = 0; i < s.Length; i++)
            //     {
            //         var ch = s[i];
            //         if (ch > 0x7F || ch < '0' || ch > '9')
            //             return false;
            //     }
            //
            //     return s.Length > 0;
            // }

            static bool IsAllCjk(string s)
            {
                for (var i = 0; i < s.Length; i++)
                {
                    var ch = s[i];

                    // treat common full-width space as not CJK heading content
                    if (char.IsWhiteSpace(ch))
                        return false;

                    if (!IsCjk(ch))
                        return false;
                }

                return s.Length > 0;
            }

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

            static bool IsMixedCjkAscii(string s)
            {
                var hasCjk = false;
                var hasAscii = false;

                for (var i = 0; i < s.Length; i++)
                {
                    var ch = s[i];

                    if (ch <= 0x7F)
                    {
                        // ASCII letter/digit only (you can decide if punctuation counts)
                        if (char.IsLetterOrDigit(ch))
                            hasAscii = true;
                        else
                            return false; // reject ASCII punctuation in headings
                    }
                    else
                    {
                        if (IsCjk(ch))
                            hasCjk = true;
                        else
                            return false; // reject other scripts/symbols
                    }

                    if (hasCjk && hasAscii)
                        return true;
                }

                return false;
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
        private static bool IsBoxDrawingLine(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return false;

            var total = 0;

            foreach (var ch in s)
            {
                // Ignore whitespace completely (probe may still contain gaps)
                if (char.IsWhiteSpace(ch))
                    continue;

                total++;

                // Unicode box drawing block (U+2500–U+257F)
                if (ch is >= '\u2500' and <= '\u257F')
                    continue;

                // ASCII visual separators (common in TXT / OCR)
                if (ch is '-' or '=' or '_' or '~')
                    continue;

                // Any real text → not a pure visual divider
                return false;
            }

            // Require minimal visual length to avoid accidental triggers
            return total >= 3;
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

        // change BuildProgressBar to use percent, not current/total
        private static string BuildProgressBar(int percent, int width = 10)
        {
            percent = Math.Clamp(percent, 0, 100);
            var filled = (int)((long)percent * width / 100);

            var sb = new StringBuilder(width * 4 + 2);
            sb.Append('[');

            for (var i = 0; i < filled; i++) sb.Append("🟩");
            for (var i = filled; i < width; i++) sb.Append("🟨");

            sb.Append(']');
            return sb.ToString();
        }
    }
}