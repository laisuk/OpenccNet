using System;
using System.Collections.Generic;
using System.IO;
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
        private static readonly Lazy<UnicodeCompat> BuiltinTable =
            new Lazy<UnicodeCompat>(LoadBuiltinTable);

        private readonly CompatIdeographs _compat;
        private readonly Dictionary<int, int> _extended;

        private UnicodeCompat(
            CompatIdeographs compat,
            Dictionary<int, int> extended)
        {
            _compat = compat ?? throw new ArgumentNullException(nameof(compat));
            _extended = extended ?? throw new ArgumentNullException(nameof(extended));
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

        private bool TryGetMapping(
            int codePoint,
            bool includeCompat,
            out int replacement)
        {
            if (!includeCompat ||
                !_compat.TryNormalizeCodePoint(codePoint, out var compatReplacement))
                return _extended.TryGetValue(codePoint, out replacement);
            replacement = ReadSingleScalar(
                compatReplacement,
                lineNo: 0,
                field: "compatibility target");
            return true;
        }

        private static UnicodeCompat LoadBuiltinTable()
        {
            return new UnicodeCompat(
                CompatIdeographs.Builtin(),
                LoadExtendedMap());
        }

        /// <summary>
        /// Loads the sparse curated normalization table.
        /// </summary>
        /// <remarks>
        /// Each non-comment line must contain exactly two tab-separated columns:
        /// one non-ASCII source Unicode scalar and one target Unicode scalar.
        /// ASCII source mappings are rejected to prevent accidental rewriting of
        /// markup and structured-text syntax, including XML/OpenXML content.
        /// Invalid rows fail fast so bundled mapping mistakes cannot be silently ignored.
        /// </remarks>
        private static Dictionary<int, int> LoadExtendedMap()
        {
            var map = new Dictionary<int, int>(256);
            var path = GetBuiltinUnicodeCompatPath();

            if (!File.Exists(path))
                return map;

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

                map[source] = target;
            }

            return map;
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