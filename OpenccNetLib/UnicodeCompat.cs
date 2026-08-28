using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace OpenccNetLib
{
    /// <summary>
    /// Provides curated Unicode normalization for Chinese text and PDF extraction artifacts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This internal normalizer loads the curated mappings from
    /// <c>dicts/Unicode_Compatibility.txt</c>. The table contains selected
    /// Unicode radicals, glyph variants, punctuation forms, and known text-extraction
    /// artifacts that are useful when normalizing CJK text.
    /// </para>
    /// <para>
    /// Every mapping is strictly one Unicode scalar value to one Unicode scalar value.
    /// Length-expanding or length-contracting mappings are rejected when the table is
    /// loaded. This keeps normalization position-stable at the scalar level and avoids
    /// introducing hidden offset, diff, selection, or indexing changes.
    /// </para>
    /// <para>
    /// <see cref="NormalizeAll(string)"/> additionally applies the built-in
    /// <see cref="CompatIdeographs"/> mappings. This class is intentionally not a
    /// general-purpose Unicode NFC, NFKC, or other standard normalization engine.
    /// </para>
    /// </remarks>
    internal sealed class UnicodeCompat
    {
        /// <summary>
        /// Lazily initialized singleton containing the built-in Unicode compatibility
        /// normalization tables.
        /// </summary>
        /// <remarks>
        /// Initialization loads the curated extended Unicode compatibility mappings
        /// together with the shared CJK Compatibility Ideograph normalizer at most once
        /// per process. Subsequent calls reuse the cached instance.
        /// </remarks>
        private static readonly Lazy<UnicodeCompat> BuiltinTable =
            new Lazy<UnicodeCompat>(LoadBuiltinTable);

        /// <summary>
        /// Built-in CJK Compatibility Ideograph normalizer used by combined
        /// compatibility normalization.
        /// </summary>
        /// <remarks>
        /// This table is applied after the curated extended Unicode compatibility
        /// mapping when extended normalization is requested.
        /// </remarks>
        private readonly CompatIdeographs _compat;

        /// <summary>
        /// Number of low-order bits used to address an entry within a mapping page.
        /// </summary>
        /// <remarks>
        /// Each page contains 256 Unicode code points. Using fixed-size pages keeps
        /// lookups array-based while avoiding allocation of one large table covering
        /// the entire Unicode range.
        /// </remarks>
        private const int PageShift = 8;

        /// <summary>
        /// Number of Unicode code points stored in each lazily allocated mapping page.
        /// </summary>
        private const int PageSize = 1 << PageShift;

        /// <summary>
        /// Bit mask used to obtain the offset of a code point within its mapping page.
        /// </summary>
        private const int PageMask = PageSize - 1;

        /// <summary>
        /// Number of pages required to address the full Unicode scalar range
        /// U+0000 through U+10FFFF.
        /// </summary>
        private const int PageCount = (0x10FFFF >> PageShift) + 1;

        /// <summary>
        /// Sparse paged lookup table for the curated Unicode compatibility mappings.
        /// </summary>
        /// <remarks>
        /// The outer array always exists, but individual 256-entry pages are allocated
        /// only when at least one mapping falls within that page.
        ///
        /// Each populated entry stores <c>replacement + 1</c>. A zero value therefore
        /// means "unmapped" while still allowing U+0000 to be represented as a valid
        /// replacement code point.
        /// </remarks>
        private readonly int[][] _extendedPages;

        /// <summary>
        /// Initializes a Unicode compatibility normalizer from the built-in CJK
        /// compatibility table and the curated extended mapping pages.
        /// </summary>
        /// <param name="compat">
        /// Built-in CJK Compatibility Ideograph normalizer.
        /// </param>
        /// <param name="extendedPages">
        /// Sparse paged lookup table containing the curated Unicode compatibility
        /// mappings.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="compat"/> or <paramref name="extendedPages"/> is
        /// <see langword="null"/>.
        /// </exception>
        private UnicodeCompat(
            CompatIdeographs compat,
            int[][] extendedPages)
        {
            _compat = compat ?? throw new ArgumentNullException(nameof(compat));
            _extendedPages = extendedPages ??
                             throw new ArgumentNullException(nameof(extendedPages));
        }

        /// <summary>
        /// Returns the cached built-in Unicode compatibility normalizer.
        /// </summary>
        /// <remarks>
        /// The bundled mapping table is loaded at most once per process and reused by
        /// subsequent callers.
        /// </remarks>
        internal static UnicodeCompat Builtin()
        {
            return BuiltinTable.Value;
        }

        /// <summary>
        /// Normalizes text using only the curated mappings from
        /// <c>Unicode_Compatibility.txt</c>.
        /// </summary>
        /// <param name="input">The text to normalize.</param>
        /// <returns>
        /// The normalized text, or the original string instance when no mapping applies.
        /// A <see langword="null"/> input returns <see cref="string.Empty"/>.
        /// </returns>
        internal string Normalize(string input)
        {
            return NormalizeCore(input, includeCompat: false);
        }

        /// <summary>
        /// Normalizes text using both the curated Unicode compatibility table and the
        /// built-in CJK Compatibility Ideograph mappings.
        /// </summary>
        /// <param name="input">The text to normalize.</param>
        /// <returns>
        /// The normalized text, or the original string instance when no mapping applies.
        /// A <see langword="null"/> input returns <see cref="string.Empty"/>.
        /// </returns>
        internal string NormalizeAll(string input)
        {
            return NormalizeCore(input, includeCompat: true);
        }

        private string NormalizeCore(
            string input,
            bool includeCompat)
        {
            if (string.IsNullOrEmpty(input))
                return input ?? string.Empty;

            var firstMapping = FindFirstMapping(input, includeCompat);
            if (firstMapping < 0)
                return input;

            var output = new StringBuilder(input.Length);
            output.Append(input, 0, firstMapping);

            for (var i = firstMapping; i < input.Length; i++)
            {
                var ch = input[i];
                var codePoint = (int)ch;
                var charCount = 1;

                if (char.IsHighSurrogate(ch) &&
                    i + 1 < input.Length &&
                    char.IsLowSurrogate(input[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(ch, input[i + 1]);
                    charCount = 2;
                }

                if (TryGetMapping(
                        codePoint,
                        includeCompat,
                        out var replacement))
                {
                    AppendCodePoint(output, replacement);

                    if (charCount == 2)
                        i++;

                    continue;
                }

                output.Append(ch);

                if (charCount == 2)
                    output.Append(input[++i]);
            }

            return output.ToString();
        }

        /// <summary>
        /// Normalizes a mutable text buffer using the curated mapping table.
        /// </summary>
        /// <param name="builder">The mutable buffer to normalize.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is <see langword="null"/>.
        /// </exception>
        internal void NormalizeInPlace(StringBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            if (builder.Length == 0)
                return;

            var normalized = Normalize(builder.ToString());
            builder.Length = 0;
            builder.Append(normalized);
        }

        private int FindFirstMapping(
            string input,
            bool includeCompat)
        {
            for (var i = 0; i < input.Length; i++)
            {
                var ch = input[i];
                var codePoint = (int)ch;

                if (char.IsHighSurrogate(ch) &&
                    i + 1 < input.Length &&
                    char.IsLowSurrogate(input[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(ch, input[i + 1]);

                    if (TryGetMapping(
                            codePoint,
                            includeCompat,
                            out _))
                    {
                        return i;
                    }

                    i++;
                    continue;
                }

                if (TryGetMapping(
                        codePoint,
                        includeCompat,
                        out _))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Attempts to normalize one Unicode code point using the configured
        /// compatibility tables.
        /// </summary>
        /// <param name="codePoint">Unicode code point to normalize.</param>
        /// <param name="includeCompat">
        /// <see langword="true"/> to additionally apply CJK Compatibility Ideograph
        /// normalization after the curated extended mapping.
        /// </param>
        /// <param name="replacement">
        /// Receives the normalized code point when a mapping changes the input.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the resulting code point differs from the
        /// input; otherwise <see langword="false"/>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetMapping(
            int codePoint,
            bool includeCompat,
            out int replacement)
        {
            var mapped = MapExtended(codePoint);

            if (includeCompat)
                mapped = _compat.MapCodePoint(mapped);

            replacement = mapped;
            return mapped != codePoint;
        }

        /// <summary>
        /// Maps one Unicode code point using the curated extended compatibility table.
        /// </summary>
        /// <param name="codePoint">Unicode code point to normalize.</param>
        /// <returns>
        /// The mapped code point when an extended compatibility mapping exists;
        /// otherwise, the original <paramref name="codePoint"/>.
        /// </returns>
        /// <remarks>
        /// This method performs only the curated
        /// <c>Unicode_Compatibility.txt</c> lookup. It does not apply the separate
        /// CJK Compatibility Ideograph table.
        ///
        /// Lookup is allocation-free and uses at most two array accesses.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int MapExtended(int codePoint)
        {
            var page = _extendedPages[codePoint >> PageShift];

            if (page == null)
                return codePoint;

            var mapped = page[codePoint & PageMask];

            return mapped == 0
                ? codePoint
                : mapped - 1;
        }

        private static UnicodeCompat LoadBuiltinTable()
        {
            return new UnicodeCompat(
                CompatIdeographs.Builtin(),
                LoadExtendedPages());
        }

        /// <summary>
        /// Loads the curated Unicode compatibility mapping table into a sparse
        /// paged lookup structure.
        /// </summary>
        /// <remarks>
        /// Each non-comment line must contain exactly two tab-separated columns:
        /// one non-ASCII source Unicode scalar and one target Unicode scalar.
        ///
        /// Pages are allocated lazily in blocks of 256 code points. This avoids
        /// dictionary lookup overhead during normalization while keeping memory usage
        /// proportional to the Unicode regions actually used by the mapping table.
        ///
        /// ASCII source mappings are rejected to prevent accidental rewriting of
        /// markup and structured-text syntax, including XML and OpenXML content.
        /// Invalid rows fail fast so bundled mapping mistakes cannot be silently
        /// ignored.
        /// </remarks>
        /// <returns>
        /// A sparse paged table containing the curated compatibility mappings.
        /// </returns>
        /// <exception cref="InvalidDataException">
        /// A mapping row is malformed, contains an invalid Unicode scalar, contains
        /// too many columns, or uses an ASCII source character.
        /// </exception>
        private static int[][] LoadExtendedPages()
        {
            var pages = new int[PageCount][];
            var path = GetBuiltinUnicodeCompatPath();

            if (!File.Exists(path))
                return pages;

            var lineNo = 0;

            foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
            {
                lineNo++;

                if (string.IsNullOrWhiteSpace(rawLine) ||
                    rawLine.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = rawLine.Split('\t');

                if (parts.Length < 2)
                {
                    throw new InvalidDataException(
                        $"line {lineNo}: missing target");
                }

                if (parts.Length > 2)
                {
                    throw new InvalidDataException(
                        $"line {lineNo}: too many columns");
                }

                var source = ReadSingleScalar(
                    parts[0].Trim(),
                    lineNo,
                    "source");

                if (source <= 0x7F)
                {
                    throw new InvalidDataException(
                        $"line {lineNo}: source must not be an ASCII character");
                }

                var target = ReadSingleScalar(
                    parts[1].Trim(),
                    lineNo,
                    "target");

                SetExtendedMapping(pages, source, target);
            }

            return pages;
        }

        /// <summary>
        /// Stores one source-to-target Unicode mapping in the paged lookup table.
        /// </summary>
        /// <param name="pages">Destination paged lookup table.</param>
        /// <param name="source">Source Unicode code point.</param>
        /// <param name="target">Replacement Unicode code point.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetExtendedMapping(
            int[][] pages,
            int source,
            int target)
        {
            var pageIndex = source >> PageShift;
            var page = pages[pageIndex];

            if (page == null)
            {
                page = new int[PageSize];
                pages[pageIndex] = page;
            }

            // Zero means unmapped, so store target + 1.
            page[source & PageMask] = target + 1;
        }

        /// <summary>
        /// Reads exactly one valid Unicode scalar value from a string.
        /// </summary>
        /// <param name="value">The UTF-16 text containing the scalar.</param>
        /// <param name="lineNo">
        /// Mapping-table line number, or zero when parsing an internal scalar.
        /// </param>
        /// <param name="field">Field name used in validation errors.</param>
        /// <returns>The Unicode scalar value as an integer code point.</returns>
        /// <exception cref="InvalidDataException">
        /// The value is empty, malformed UTF-16, or contains more than one scalar.
        /// </exception>
        private static int ReadSingleScalar(
            string value,
            int lineNo,
            string field)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new InvalidDataException(
                    LinePrefix(lineNo) + "empty " + field);
            }

            var first = value[0];

            if (char.IsHighSurrogate(first))
            {
                if (value.Length != 2 ||
                    !char.IsLowSurrogate(value[1]))
                {
                    throw new InvalidDataException(
                        LinePrefix(lineNo) +
                        field + " must be exactly one valid Unicode scalar value");
                }

                return char.ConvertToUtf32(first, value[1]);
            }

            if (char.IsLowSurrogate(first) || value.Length != 1)
            {
                throw new InvalidDataException(
                    LinePrefix(lineNo) +
                    field + " must be exactly one valid Unicode scalar value");
            }

            return first;
        }

        /// <summary>
        /// Appends one Unicode scalar value to a UTF-16 string builder.
        /// </summary>
        private static void AppendCodePoint(
            StringBuilder output,
            int codePoint)
        {
            if (codePoint <= char.MaxValue)
            {
                output.Append((char)codePoint);
                return;
            }

            output.Append(char.ConvertFromUtf32(codePoint));
        }

        private static string LinePrefix(int lineNo)
        {
            return lineNo > 0
                ? $"line {lineNo}: "
                : string.Empty;
        }

        private static string GetBuiltinUnicodeCompatPath()
        {
            return Path.Combine(
                AppContext.BaseDirectory,
                "dicts",
                "Unicode_Compatibility.txt");
        }
    }
}