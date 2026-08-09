using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OpenccNetLib
{
    /// <summary>
    /// Provides extended Chinese Unicode compatibility normalization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This internal normalizer is a superset of <see cref="CompatIdeographs"/>.
    /// It preserves the built-in CJK Compatibility Ideograph mappings and adds
    /// curated Chinese Unicode compatibility, radical, glyph-variant, punctuation,
    /// and extraction-artifact mappings from <c>dicts/Unicode_Compatibility.txt</c>.
    /// </para>
    /// <para>
    /// It is intentionally not a general-purpose Unicode NFKC normalizer.
    /// </para>
    /// </remarks>
    internal sealed class UnicodeCompat
    {
        private static readonly Lazy<UnicodeCompat> BuiltinTable =
            new Lazy<UnicodeCompat>(LoadBuiltinTable);

        private readonly CompatIdeographs _compat;
        private readonly Dictionary<int, string> _extended;

        private UnicodeCompat(
            CompatIdeographs compat,
            Dictionary<int, string> extended)
        {
            _compat = compat;
            _extended = extended;
        }

        internal static UnicodeCompat Builtin()
        {
            return BuiltinTable.Value;
        }

        internal string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input ?? string.Empty;

            var firstMapping = FindFirstMapping(input);
            if (firstMapping < 0)
                return input;

            var output = new StringBuilder(input.Length);
            output.Append(input, 0, firstMapping);

            for (var i = firstMapping; i < input.Length; i++)
            {
                var ch = input[i];
                int codePoint = ch;
                var charCount = 1;

                if (char.IsHighSurrogate(ch) &&
                    i + 1 < input.Length &&
                    char.IsLowSurrogate(input[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(ch, input[i + 1]);
                    charCount = 2;
                }

                if (TryGetMapping(codePoint, out var replacement))
                {
                    output.Append(replacement);

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

        private int FindFirstMapping(string input)
        {
            for (var i = 0; i < input.Length; i++)
            {
                var ch = input[i];
                int codePoint = ch;

                if (char.IsHighSurrogate(ch) &&
                    i + 1 < input.Length &&
                    char.IsLowSurrogate(input[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(ch, input[i + 1]);

                    if (TryGetMapping(codePoint, out _))
                        return i;

                    i++;
                    continue;
                }

                if (TryGetMapping(codePoint, out _))
                    return i;
            }

            return -1;
        }

        private bool TryGetMapping(int codePoint, out string replacement)
        {
            return _compat.TryNormalizeCodePoint(codePoint, out replacement)
                   || _extended.TryGetValue(codePoint, out replacement);
        }

        private static UnicodeCompat LoadBuiltinTable()
        {
            return new UnicodeCompat(
                CompatIdeographs.Builtin(),
                LoadExtendedMap());
        }

        private static Dictionary<int, string> LoadExtendedMap()
        {
            var map = new Dictionary<int, string>(256);
            var path = GetBuiltinUnicodeCompatPath();

            if (!File.Exists(path))
                return map;

            foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
            {
                var line = rawLine.Trim();

                if (line.Length == 0 ||
                    line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                var parts = line.Split('\t');

                if (parts.Length != 2)
                    continue;

                var source = parts[0].Trim();
                var target = parts[1].Trim();

                if (!TryReadSingleScalar(source, out var sourceCodePoint) ||
                    !TryReadSingleScalar(target, out _))
                    continue;

                map[sourceCodePoint] = target;
            }

            return map;
        }

        private static bool TryReadSingleScalar(
            string value,
            out int codePoint)
        {
            codePoint = 0;

            if (string.IsNullOrEmpty(value))
                return false;

            var first = value[0];

            if (char.IsHighSurrogate(first))
            {
                if (value.Length != 2 ||
                    !char.IsLowSurrogate(value[1]))
                    return false;

                codePoint = char.ConvertToUtf32(first, value[1]);
                return true;
            }

            if (char.IsLowSurrogate(first) || value.Length != 1)
                return false;

            codePoint = first;
            return true;
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