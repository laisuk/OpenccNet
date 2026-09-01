using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace OpenccNetLib
{
    internal static class EmbeddedData
    {
        internal const string CompatIdeographsResourceName =
            "OpenccNetLib.Resources.CJK_Compatibility_Ideographs.txt";

        internal const string UnicodeCompatResourceName =
            "OpenccNetLib.Resources.Unicode_Compatibility.txt";

        internal const string CharactersTofuResourceName =
            "OpenccNetLib.Resources.CharactersTofu.txt";

        /// <summary>
        /// Loads the specified built-in text data from this assembly's embedded resources.
        /// </summary>
        internal static string ReadText(string resourceName)
        {
            var assembly = typeof(EmbeddedData).Assembly;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new InvalidOperationException(
                    $"Embedded resource not found: {resourceName}");
            }

            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        internal static TextReader OpenText(string resourceName)
        {
            var assembly = typeof(EmbeddedData).Assembly;
            var stream = assembly.GetManifestResourceStream(resourceName);

            if (stream == null)
            {
                throw new InvalidOperationException(
                    $"Embedded resource not found: {resourceName}");
            }

            return new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
        }
    }

    /// <summary>
    /// Implements internal CJK Compatibility Ideograph normalization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This normalizer maps Unicode CJK Compatibility Ideographs to their
    /// UnicodeData decomposition targets. It is an optional Unicode compatibility
    /// normalization pre-pass, not an OpenCC dictionary conversion.
    /// </para>
    /// <para>
    /// Compatibility ideograph normalization is intentionally separate from
    /// <see cref="DeTofu"/>. Compatibility normalization runs before segmentation
    /// and conversion when callers want Unicode compatibility behavior; DeTofu is
    /// an optional post-processing display fallback for rare characters after
    /// conversion.
    /// </para>
    /// <para>
    /// Characters outside the CJK Compatibility Ideograph ranges, and compatibility
    /// ideographs without a decomposition mapping, are preserved unchanged.
    /// </para>
    /// </remarks>
    internal sealed class CompatIdeographs
    {
        private const int BmpStart = 0xF900;
        private const int BmpEnd = 0xFAFF;
        private const int BmpLen = BmpEnd - BmpStart + 1;

        private const int SuppStart = 0x2F800;
        private const int SuppEnd = 0x2FA1F;
        private const int SuppLen = SuppEnd - SuppStart + 1;

        private static readonly Lazy<CompatIdeographs> BuiltinTable = new(LoadBuiltinTable);

        private readonly string[] _bmp;
        private readonly string[] _supp;

        private CompatIdeographs()
        {
            _bmp = new string[BmpLen];
            _supp = new string[SuppLen];

            for (var i = 0; i < _bmp.Length; i++)
                _bmp[i] = CharFromCodePoint(BmpStart + i);

            for (var i = 0; i < _supp.Length; i++)
                _supp[i] = CharFromCodePoint(SuppStart + i);
        }

        /// <summary>
        /// Returns the cached built-in compatibility ideograph normalizer.
        /// </summary>
        /// <remarks>
        /// The bundled mapping data is loaded from the assembly's embedded
        /// <c>CJK_Compatibility_Ideographs.txt</c> resource and parsed at most once
        /// per process. Subsequent calls reuse the same dense lookup tables.
        /// </remarks>
        /// <returns>The reusable built-in compatibility normalizer.</returns>
        internal static CompatIdeographs Builtin()
        {
            return BuiltinTable.Value;
        }

        /// <summary>
        /// Builds a compatibility ideograph normalizer from mapping text.
        /// </summary>
        /// <remarks>
        /// The expected format is one tab-separated
        /// <c>source&lt;TAB&gt;target</c> pair per line. Blank lines and lines
        /// beginning with <c>#</c> are ignored.
        /// </remarks>
        /// <param name="text">UTF-8 mapping text that has already been decoded.</param>
        /// <returns>A reusable compatibility ideograph normalizer.</returns>
        /// <exception cref="ArgumentException">
        /// A non-comment mapping line is malformed, contains more than one scalar
        /// in either column, or uses a source outside the CJK Compatibility
        /// Ideograph ranges.
        /// </exception>
        internal static CompatIdeographs FromText(string text)
        {
            var table = new CompatIdeographs();

            if (string.IsNullOrEmpty(text))
                return table;

            using var reader = new StringReader(text);
            var lineNo = 0;

            while (reader.ReadLine() is { } rawLine)
            {
                lineNo++;

                if (string.IsNullOrWhiteSpace(rawLine) ||
                    rawLine.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = rawLine.Split('\t');
                switch (parts.Length)
                {
                    case < 2:
                        throw new ArgumentException("line " + lineNo + ": missing target", nameof(text));
                    case > 2:
                        throw new ArgumentException("line " + lineNo + ": too many columns", nameof(text));
                }

                var src = ReadSingleScalar(parts[0].Trim(), lineNo, "source");
                var dst = ReadSingleScalar(parts[1].Trim(), lineNo, "target");

                table.Set(src.CodePoint, dst.Scalar, lineNo);
            }

            return table;
        }

        /// <summary>
        /// Normalizes one Unicode scalar value if it has a compatibility mapping.
        /// </summary>
        /// <remarks>
        /// The input must contain exactly one Unicode scalar value. For ordinary
        /// BMP characters, pass a one-character string. For supplementary
        /// characters, pass the surrogate-pair string.
        /// </remarks>
        /// <param name="scalar">A string containing exactly one Unicode scalar value.</param>
        /// <returns>The mapped scalar, or the original scalar when no mapping exists.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="scalar"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="scalar"/> is empty or contains more than one Unicode scalar value.
        /// </exception>
        internal string NormalizeScalar(string scalar)
        {
            if (scalar == null)
                throw new ArgumentNullException(nameof(scalar));

            var parsed = ReadSingleScalar(scalar, 0, nameof(scalar));
            return NormalizeCodePoint(parsed.CodePoint);
        }

        /// <summary>
        /// Normalizes one UTF-16 BMP character if it has a compatibility mapping.
        /// </summary>
        /// <remarks>
        /// This overload is convenient for BMP compatibility ideographs such as
        /// <c>金</c>. Use <see cref="NormalizeScalar(string)"/> or
        /// <see cref="Normalize(string)"/> for supplementary-plane characters.
        /// </remarks>
        /// <param name="ch">The BMP character to normalize.</param>
        /// <returns>The mapped scalar, or the original character when no mapping exists.</returns>
        internal string NormalizeChar(char ch)
        {
            return NormalizeCodePoint(ch);
        }

        /// <summary>
        /// Normalizes all mapped CJK Compatibility Ideographs in <paramref name="input"/>.
        /// </summary>
        /// <remarks>
        /// A <see langword="null"/> input value returns <see cref="String.Empty"/>.
        /// Ordinary Chinese text, unmapped compatibility ideographs, and non-CJK
        /// text are preserved unchanged.
        /// </remarks>
        /// <param name="input">The input text to normalize.</param>
        /// <returns>Text with mapped compatibility ideographs normalized.</returns>
        internal string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input ?? string.Empty;

#if NET9_0_OR_GREATER
            var firstMapping = FindFirstMapping(input.AsSpan());
#else
            var firstMapping = FindFirstMapping(input);
#endif
            if (firstMapping < 0)
                return input;

            var output = new StringBuilder(input.Length);
            output.Append(input, 0, firstMapping);

            for (var i = firstMapping; i < input.Length; i++)
            {
                var ch = input[i];

                if (ch >= BmpStart && ch <= BmpEnd)
                {
                    output.Append(_bmp[ch - BmpStart]);
                    continue;
                }

                if (ch == '\uD87E' && i + 1 < input.Length)
                {
                    var low = input[i + 1];
                    if (low is >= '\uDC00' and <= '\uDE1F')
                    {
                        var codePoint = char.ConvertToUtf32(ch, low);
                        output.Append(_supp[codePoint - SuppStart]);
                        i++;
                        continue;
                    }
                }

                output.Append(ch);
            }

            return output.ToString();
        }

#if NET9_0_OR_GREATER
        private int FindFirstMapping(ReadOnlySpan<char> input)
        {
            var offset = 0;

            while (offset < input.Length)
            {
                var remaining = input[offset..];
                var bmpIndex = remaining.IndexOfAnyInRange((char)BmpStart, (char)BmpEnd);
                var suppIndex = remaining.IndexOf('\uD87E');

                if (bmpIndex < 0) bmpIndex = int.MaxValue;
                if (suppIndex < 0) suppIndex = int.MaxValue;

                var candidateIndex = Math.Min(bmpIndex, suppIndex);
                if (candidateIndex == int.MaxValue)
                    return -1;

                var index = offset + candidateIndex;
                if (HasMappingAt(input, index))
                    return index;

                offset = index + 1;
            }

            return -1;
        }

        private bool HasMappingAt(ReadOnlySpan<char> input, int index)
        {
            var ch = input[index];
            if (ch >= BmpStart && ch <= BmpEnd)
            {
                var mapping = _bmp[ch - BmpStart];
                return mapping.Length != 1 || mapping[0] != ch;
            }

            if (index + 1 >= input.Length)
                return false;

            var low = input[index + 1];
            if (low is < '\uDC00' or > '\uDE1F')
                return false;

            var supplementaryMapping = _supp[char.ConvertToUtf32(ch, low) - SuppStart];
            return supplementaryMapping.Length != 2 || supplementaryMapping[0] != ch ||
                   supplementaryMapping[1] != low;
        }
#else
        private int FindFirstMapping(string input)
        {
            for (var i = 0; i < input.Length; i++)
            {
                var ch = input[i];
                if (ch >= BmpStart && ch <= BmpEnd)
                {
                    var mapping = _bmp[ch - BmpStart];
                    if (mapping.Length != 1 || mapping[0] != ch)
                        return i;
                }
                else if (ch == '\uD87E' && i + 1 < input.Length)
                {
                    var low = input[i + 1];
                    if (low is < '\uDC00' or > '\uDE1F') continue;
                    var mapping = _supp[char.ConvertToUtf32(ch, low) - SuppStart];
                    if (mapping.Length != 2 || mapping[0] != ch || mapping[1] != low)
                        return i;

                    i++;
                }
            }

            return -1;
        }
#endif

        /// <summary>
        /// Normalizes a mutable string buffer in place.
        /// </summary>
        /// <remarks>
        /// This is useful when text has already been collected into a reusable
        /// <see cref="StringBuilder"/> before segmentation. Because C# strings are
        /// UTF-16, a supplementary scalar may occupy two code units; this method
        /// therefore rebuilds the buffer content after normalization.
        /// </remarks>
        /// <param name="builder">The mutable text buffer to normalize.</param>
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

        private bool TryNormalizeCodePoint(int codePoint, out string replacement)
        {
            switch (codePoint)
            {
                case >= BmpStart and <= BmpEnd:
                    replacement = _bmp[codePoint - BmpStart];
                    return replacement.Length != 1 || replacement[0] != (char)codePoint;
                case >= SuppStart and <= SuppEnd:
                {
                    replacement = _supp[codePoint - SuppStart];

                    var original = CharFromCodePoint(codePoint);
                    return !string.Equals(replacement, original, StringComparison.Ordinal);
                }
                default:
                    replacement = null;
                    return false;
            }
        }

        private string NormalizeCodePoint(int codePoint)
        {
            return TryNormalizeCodePoint(codePoint, out var replacement) ? replacement : CharFromCodePoint(codePoint);
        }

        private void Set(int sourceCodePoint, string targetScalar, int lineNo)
        {
            switch (sourceCodePoint)
            {
                case >= BmpStart and <= BmpEnd:
                    _bmp[sourceCodePoint - BmpStart] = targetScalar;
                    return;
                case >= SuppStart and <= SuppEnd:
                    _supp[sourceCodePoint - SuppStart] = targetScalar;
                    return;
                default:
                    throw new ArgumentException(
                        "line " + lineNo + ": source U+" + sourceCodePoint.ToString("X4") +
                        " is outside CJK Compatibility Ideograph ranges");
            }
        }

        private static CompatIdeographs LoadBuiltinTable()
        {
            return FromText(
                EmbeddedData.ReadText(EmbeddedData.CompatIdeographsResourceName));
        }

        private static ScalarValue ReadSingleScalar(string value, int lineNo, string field)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException(LinePrefix(lineNo) + "empty " + field);

            var codePoint = ReadCodePointAt(value, 0, out var charCount);

            if (charCount == 1 && char.IsSurrogate(value[0]))
                throw new ArgumentException(LinePrefix(lineNo) + field + " must be a valid Unicode scalar value");

            return charCount != value.Length
                ? throw new ArgumentException(LinePrefix(lineNo) + field + " must be exactly one character")
                : new ScalarValue(codePoint, value);
        }

        private static int ReadCodePointAt(string value, int index, out int charCount)
        {
            var ch = value[index];

            if (char.IsHighSurrogate(ch) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
            {
                charCount = 2;
                return char.ConvertToUtf32(ch, value[index + 1]);
            }

            charCount = 1;
            return ch;
        }

        private static string CharFromCodePoint(int codePoint)
        {
            return codePoint is >= 0xD800 and <= 0xDFFF
                ? new string((char)codePoint, 1)
                : char.ConvertFromUtf32(codePoint);
        }

        private static string LinePrefix(int lineNo)
        {
            return lineNo > 0 ? "line " + lineNo + ": " : string.Empty;
        }

        private struct ScalarValue
        {
            internal ScalarValue(int codePoint, string scalar)
            {
                CodePoint = codePoint;
                Scalar = scalar;
            }

            internal int CodePoint { get; }

            internal string Scalar { get; }
        }

        /// <summary>
        /// Maps one Unicode code point using the built-in CJK Compatibility
        /// Ideograph table.
        /// </summary>
        /// <param name="codePoint">Unicode code point to normalize.</param>
        /// <returns>
        /// The mapped Unicode code point when a compatibility mapping exists;
        /// otherwise, the original <paramref name="codePoint"/>.
        /// </returns>
        /// <remarks>
        /// The bundled compatibility table is defined as one Unicode scalar to one
        /// Unicode scalar for the mappings used by the normalization pipeline.
        ///
        /// This primitive mapping API is used internally by combined normalization to
        /// avoid temporary strings and repeated scalar parsing.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int MapCodePoint(int codePoint)
        {
            switch (codePoint)
            {
                case >= BmpStart and <= BmpEnd:
                {
                    var replacement = _bmp[codePoint - BmpStart];

                    if (replacement.Length == 1)
                        return replacement[0];

                    return char.ConvertToUtf32(
                        replacement[0],
                        replacement[1]);
                }
                case < SuppStart or > SuppEnd:
                    return codePoint;
                default:
                {
                    var replacement = _supp[codePoint - SuppStart];
                    var original = CharFromCodePoint(codePoint);

                    if (string.Equals(
                            replacement,
                            original,
                            StringComparison.Ordinal))
                    {
                        return codePoint;
                    }

                    return replacement.Length == 1
                        ? replacement[0]
                        : char.ConvertToUtf32(
                            replacement[0],
                            replacement[1]);
                }
            }
        }
    }
}