using System;
using System.IO;
using System.Text;

namespace OpenccNetLib
{
    /// <summary>
    /// Provides CJK Compatibility Ideograph normalization utilities.
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
    /// and conversion when callers want Unicode compatibility behavior; DeToFu is
    /// an optional post-processing display fallback for rare characters after
    /// conversion.
    /// </para>
    /// <para>
    /// Characters outside the CJK Compatibility Ideograph ranges, and compatibility
    /// ideographs without a decomposition mapping, are preserved unchanged.
    /// </para>
    /// </remarks>
    public sealed class CompatIdeographs
    {
        private const int BmpStart = 0xF900;
        private const int BmpEnd = 0xFAFF;
        private const int BmpLen = BmpEnd - BmpStart + 1;

        private const int SuppStart = 0x2F800;
        private const int SuppEnd = 0x2FA1F;
        private const int SuppLen = SuppEnd - SuppStart + 1;

        private static readonly Lazy<CompatIdeographs> BuiltinTable =
            new Lazy<CompatIdeographs>(LoadBuiltinTable);

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
        /// The bundled mapping data is loaded from
        /// <c>dicts/CJK_Compatibility_Ideographs.txt</c> and parsed at most once
        /// per process. Subsequent calls reuse the same dense lookup tables.
        /// </remarks>
        /// <returns>The reusable built-in compatibility normalizer.</returns>
        public static CompatIdeographs Builtin()
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
        public static CompatIdeographs FromText(string text)
        {
            var table = new CompatIdeographs();

            if (string.IsNullOrEmpty(text))
                return table;

            using (var reader = new StringReader(text))
            {
                string rawLine;
                var lineNo = 0;

                while ((rawLine = reader.ReadLine()) != null)
                {
                    lineNo++;
                    var line = rawLine.Trim();

                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    var parts = line.Split('\t');
                    if (parts.Length < 2)
                        throw new ArgumentException("line " + lineNo + ": missing target", nameof(text));

                    if (parts.Length > 2)
                        throw new ArgumentException("line " + lineNo + ": too many columns", nameof(text));

                    var src = ReadSingleScalar(parts[0].Trim(), lineNo, "source");
                    var dst = ReadSingleScalar(parts[1].Trim(), lineNo, "target");

                    table.Set(src.CodePoint, dst.Scalar, lineNo);
                }
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
        public string NormalizeScalar(string scalar)
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
        public string NormalizeChar(char ch)
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
        public string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input ?? string.Empty;

            var output = new StringBuilder(input.Length);

            for (var i = 0; i < input.Length; i++)
            {
                var codePoint = ReadCodePointAt(input, i, out var charCount);
                output.Append(NormalizeCodePoint(codePoint));

                if (charCount == 2)
                    i++;
            }

            return output.ToString();
        }

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
        public void NormalizeInPlace(StringBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            if (builder.Length == 0)
                return;

            var normalized = Normalize(builder.ToString());
            builder.Length = 0;
            builder.Append(normalized);
        }

        /// <summary>
        /// Normalizes mapped CJK Compatibility Ideographs using the built-in table.
        /// </summary>
        /// <remarks>
        /// This is a convenience wrapper around <see cref="Builtin"/> and
        /// <see cref="Normalize(string)"/>. It performs Unicode compatibility
        /// normalization as an optional pre-pass before OpenCC conversion.
        /// </remarks>
        /// <param name="input">The input text to normalize.</param>
        /// <returns>Text with mapped compatibility ideographs normalized.</returns>
        public static string NormalizeCompatIdeographs(string input)
        {
            return Builtin().Normalize(input);
        }

        private string NormalizeCodePoint(int codePoint)
        {
            if (codePoint >= BmpStart && codePoint <= BmpEnd)
                return _bmp[codePoint - BmpStart];

            if (codePoint >= SuppStart && codePoint <= SuppEnd)
                return _supp[codePoint - SuppStart];

            return CharFromCodePoint(codePoint);
        }

        private void Set(int sourceCodePoint, string targetScalar, int lineNo)
        {
            if (sourceCodePoint >= BmpStart && sourceCodePoint <= BmpEnd)
            {
                _bmp[sourceCodePoint - BmpStart] = targetScalar;
                return;
            }

            if (sourceCodePoint >= SuppStart && sourceCodePoint <= SuppEnd)
            {
                _supp[sourceCodePoint - SuppStart] = targetScalar;
                return;
            }

            throw new ArgumentException(
                "line " + lineNo + ": source U+" + sourceCodePoint.ToString("X4") +
                " is outside CJK Compatibility Ideograph ranges");
        }

        private static string GetBuiltinCompatPath()
        {
            var baseDir = AppContext.BaseDirectory;
            return Path.Combine(baseDir, "dicts", "CJK_Compatibility_Ideographs.txt");
        }

        private static CompatIdeographs LoadBuiltinTable()
        {
            var path = GetBuiltinCompatPath();

            if (!File.Exists(path))
                return new CompatIdeographs();

            return FromText(File.ReadAllText(path, Encoding.UTF8));
        }

        private static ScalarValue ReadSingleScalar(string value, int lineNo, string field)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException(LinePrefix(lineNo) + "empty " + field);

            var codePoint = ReadCodePointAt(value, 0, out var charCount);

            if (charCount == 1 && char.IsSurrogate(value[0]))
                throw new ArgumentException(LinePrefix(lineNo) + field + " must be a valid Unicode scalar value");

            if (charCount != value.Length)
                throw new ArgumentException(LinePrefix(lineNo) + field + " must be exactly one character");

            return new ScalarValue(codePoint, value);
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
            if (codePoint >= 0xD800 && codePoint <= 0xDFFF)
                return new string((char)codePoint, 1);

            return char.ConvertFromUtf32(codePoint);
        }

        private static string LinePrefix(int lineNo)
        {
            return lineNo > 0 ? "line " + lineNo + ": " : string.Empty;
        }

        private struct ScalarValue
        {
            public ScalarValue(int codePoint, string scalar)
            {
                CodePoint = codePoint;
                Scalar = scalar;
            }

            public int CodePoint { get; }

            public string Scalar { get; }
        }
    }
}
