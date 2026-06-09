using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OpenccNetLib
{
    /// <summary>
    /// Specifies the CJK extension threshold used by DeTofu fallback conversion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DeTofu levels are threshold-based: the selected level is the earliest
    /// extension block whose mapped characters are eligible for replacement, and
    /// all supported later extension blocks are eligible too.
    /// </para>
    /// <para>
    /// For example, <see cref="DeTofuLevel.ExtB"/> enables mapped Extension B
    /// characters and all later supported extension mappings, while
    /// <see cref="DeTofuLevel.ExtI"/> enables only mapped Extension I characters.
    /// </para>
    /// </remarks>
    public enum DeTofuLevel
    {
        /// <summary>
        /// Enables mapped CJK Extension B characters and all supported later extension mappings.
        /// </summary>
        ExtB = 0,

        /// <summary>
        /// Enables mapped CJK Extension C characters and all supported later extension mappings.
        /// </summary>
        ExtC = 1,

        /// <summary>
        /// Enables mapped CJK Extension D characters and all supported later extension mappings.
        /// </summary>
        ExtD = 2,

        /// <summary>
        /// Enables mapped CJK Extension E characters and all supported later extension mappings.
        /// </summary>
        ExtE = 3,

        /// <summary>
        /// Enables mapped CJK Extension F characters and all supported later extension mappings.
        /// </summary>
        ExtF = 4,

        /// <summary>
        /// Enables mapped CJK Extension G characters and all supported later extension mappings.
        /// </summary>
        ExtG = 5,

        /// <summary>
        /// Enables mapped CJK Extension H characters and all supported later extension mappings.
        /// </summary>
        ExtH = 6,

        /// <summary>
        /// Enables mapped CJK Extension I characters only.
        /// </summary>
        ExtI = 7
    }

    /// <summary>
    /// Provides display-compatibility fallback utilities for rare non-BMP CJK extension characters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DeTofu is intended for environments with incomplete rare-character font coverage, where
    /// some CJK extension characters may render as tofu boxes or missing-glyph placeholders.
    /// </para>
    /// <para>
    /// This is not OpenCC linguistic conversion. It does not modify conversion dictionaries,
    /// phrase matching, regional variant selection, script detection, or punctuation conversion.
    /// Apply DeTofu after normal OpenCC conversion when both operations are needed.
    /// </para>
    /// <para>
    /// Unknown or unmapped characters are preserved unchanged. DeTofu never replaces unknown
    /// characters with <c>?</c>, <c>□</c>, <c>�</c>, or empty text.
    /// </para>
    /// </remarks>
    public static class DeTofu
    {
        private static readonly Lazy<List<DeTofuEntry>> BuiltinEntries =
            new Lazy<List<DeTofuEntry>>(LoadBuiltinEntries);

        /// <summary>
        /// Parses a textual DeTofu level into a <see cref="DeTofuLevel"/> value.
        /// </summary>
        /// <param name="value">
        /// Level name such as <c>all</c>, <c>ext-b</c>, <c>b</c>, <c>ext-c</c>, or <c>ext-i</c>.
        /// Matching is case-insensitive and ignores leading or trailing whitespace.
        /// </param>
        /// <returns>The parsed DeTofu extension threshold.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="value"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="value"/> is not a supported DeTofu level.
        /// </exception>
        public static DeTofuLevel ParseLevel(string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            switch (value.Trim().ToLowerInvariant())
            {
                case "all":
                case "ext-b":
                case "b":
                    return DeTofuLevel.ExtB;
                case "ext-c":
                case "c":
                    return DeTofuLevel.ExtC;
                case "ext-d":
                case "d":
                    return DeTofuLevel.ExtD;
                case "ext-e":
                case "e":
                    return DeTofuLevel.ExtE;
                case "ext-f":
                case "f":
                    return DeTofuLevel.ExtF;
                case "ext-g":
                case "g":
                    return DeTofuLevel.ExtG;
                case "ext-h":
                case "h":
                    return DeTofuLevel.ExtH;
                case "ext-i":
                case "i":
                    return DeTofuLevel.ExtI;
                default:
                    throw new ArgumentException(
                        "Supported deTofu levels: all, ext-b, ext-c, ext-d, ext-e, ext-f, ext-g, ext-h, ext-i.",
                        nameof(value));
            }
        }

        /// <summary>
        /// Converts mapped rare CJK extension characters to display-compatible fallback characters.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method uses the built-in fallback mappings loaded from
        /// <c>dicts/TSCharactersTofu.txt</c>. For repeated conversions or custom mappings,
        /// build a reusable <see cref="DeTofuMap"/> with <see cref="DeTofuMap.Builtin(DeTofuLevel)"/>.
        /// </para>
        /// <para>
        /// The method is non-destructive: characters without a fallback mapping are preserved
        /// unchanged, even when they belong to an enabled CJK extension block.
        /// </para>
        /// </remarks>
        /// <param name="input">The input text. A <see langword="null"/> value is treated as empty text.</param>
        /// <param name="level">The threshold-based DeTofu extension level.</param>
        /// <returns>Text with mapped tofu-risk characters replaced and unmapped characters preserved.</returns>
        public static string Convert(string input, DeTofuLevel level)
        {
            return DeTofuMap.Builtin(level).Convert(input);
        }

        internal static List<DeTofuEntry> ParseEntries(string text)
        {
            var entries = new List<DeTofuEntry>();

            if (string.IsNullOrEmpty(text))
                return entries;

            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();

                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    var parts = line.Split('\t');
                    if (parts.Length < 3)
                        continue;

                    var tofu = ReadFirstScalar(parts[0].Trim());
                    var fallback = ReadFirstScalar(parts[1].Trim());

                    if (tofu == null || fallback == null || !TryParseExtension(parts[2], out var ext))
                        continue;

                    entries.Add(new DeTofuEntry(tofu, fallback, ext));
                }
            }

            return entries;
        }

        internal static IReadOnlyList<DeTofuEntry> GetBuiltinEntries()
        {
            return BuiltinEntries.Value;
        }

        private static string GetBuiltinTofuPath()
        {
            var baseDir = AppContext.BaseDirectory;
            return Path.Combine(baseDir, "dicts", "TSCharactersTofu.txt");
        }

        private static List<DeTofuEntry> LoadBuiltinEntries()
        {
            var path = GetBuiltinTofuPath();

            if (!File.Exists(path))
            {
                return new List<DeTofuEntry>();
            }

            return ParseEntries(File.ReadAllText(path, Encoding.UTF8));
        }

        private static bool TryParseExtension(string value, out DeTofuLevel level)
        {
            level = DeTofuLevel.ExtB;

            if (value == null)
                return false;

            switch (value.Trim())
            {
                case "ExtB":
                case "B":
                case "b":
                    level = DeTofuLevel.ExtB;
                    return true;
                case "ExtC":
                case "C":
                case "c":
                    level = DeTofuLevel.ExtC;
                    return true;
                case "ExtD":
                case "D":
                case "d":
                    level = DeTofuLevel.ExtD;
                    return true;
                case "ExtE":
                case "E":
                case "e":
                    level = DeTofuLevel.ExtE;
                    return true;
                case "ExtF":
                case "F":
                case "f":
                    level = DeTofuLevel.ExtF;
                    return true;
                case "ExtG":
                case "G":
                case "g":
                    level = DeTofuLevel.ExtG;
                    return true;
                case "ExtH":
                case "H":
                case "h":
                    level = DeTofuLevel.ExtH;
                    return true;
                case "ExtI":
                case "I":
                case "i":
                    level = DeTofuLevel.ExtI;
                    return true;
                default:
                    return false;
            }
        }

        internal static string ReadFirstScalar(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            var first = value[0];
            if (char.IsHighSurrogate(first) && value.Length >= 2 && char.IsLowSurrogate(value[1]))
                return value.Substring(0, 2);

            return value.Substring(0, 1);
        }

        internal static IEnumerable<string> EnumerateScalars(string input)
        {
            if (string.IsNullOrEmpty(input))
                yield break;

            for (var i = 0; i < input.Length; i++)
            {
                var ch = input[i];
                if (char.IsHighSurrogate(ch) && i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
                {
                    yield return input.Substring(i, 2);
                    i++;
                }
                else
                {
                    yield return input.Substring(i, 1);
                }
            }
        }
    }

    internal sealed class DeTofuEntry
    {
        public DeTofuEntry(string tofu, string fallback, DeTofuLevel extension)
        {
            Tofu = tofu;
            Fallback = fallback;
            Extension = extension;
        }

        public string Tofu { get; }

        public string Fallback { get; }

        public DeTofuLevel Extension { get; }
    }

    /// <summary>
    /// Represents a reusable DeTofu display-compatibility fallback map.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="DeTofuMap"/> is useful when callers want to build a fallback table once
    /// and reuse it across many strings, or layer application-specific fallback data on top
    /// of the built-in mappings from <c>dicts/TSCharactersTofu.txt</c>.
    /// </para>
    /// <para>
    /// Custom files and custom pairs are applied after the built-in mappings. Later mappings
    /// override earlier mappings when the same tofu-risk character is provided.
    /// </para>
    /// <para>
    /// Characters without a built-in or custom fallback mapping are preserved unchanged,
    /// even when they belong to an enabled CJK extension block. The map never replaces
    /// unknown characters with <c>?</c>, <c>□</c>, <c>�</c>, or empty text.
    /// </para>
    /// </remarks>
    public sealed class DeTofuMap
    {
        private readonly DeTofuLevel _level;
        private readonly Dictionary<string, string> _map;

        private DeTofuMap(DeTofuLevel level, Dictionary<string, string> map)
        {
            _level = level;
            _map = map;
        }

        /// <summary>
        /// Builds a DeTofu map from the library's built-in compatibility data.
        /// </summary>
        /// <remarks>
        /// Built-in mappings are loaded from <c>dicts/TSCharactersTofu.txt</c>. Only entries
        /// at or above the specified threshold are included. For example,
        /// <see cref="DeTofuLevel.ExtB"/> includes mapped Extension B and later entries, while
        /// <see cref="DeTofuLevel.ExtI"/> includes mapped Extension I entries only.
        /// </remarks>
        /// <param name="level">The threshold-based DeTofu extension level.</param>
        /// <returns>A reusable fallback map initialized with built-in mappings.</returns>
        public static DeTofuMap Builtin(DeTofuLevel level)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var entry in DeTofu.GetBuiltinEntries())
            {
                if (entry.Extension >= level)
                    map[entry.Tofu] = entry.Fallback;
            }

            return new DeTofuMap(level, map);
        }

        /// <summary>
        /// Adds or overrides compatibility fallback entries from a UTF-8 DeTofu mapping file.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The file format is <c>tofu_char&lt;TAB&gt;fallback_char&lt;TAB&gt;extension</c>,
        /// with one mapping per line. Blank lines and lines beginning with <c>#</c> are ignored.
        /// </para>
        /// <para>
        /// The extension field accepts compact values such as <c>B</c> through <c>I</c>,
        /// or legacy values such as <c>ExtB</c> through <c>ExtI</c>.
        /// </para>
        /// <para>
        /// Custom file mappings are applied after the mappings already present in the map.
        /// If the same tofu-risk character is provided more than once, the later mapping wins.
        /// Entries below this map's threshold level are ignored.
        /// </para>
        /// </remarks>
        /// <param name="path">Path to a UTF-8 DeTofu mapping file.</param>
        /// <returns>The current map instance, updated with eligible custom entries.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="path"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="IOException">
        /// The file cannot be read.
        /// </exception>
        /// <exception cref="UnauthorizedAccessException">
        /// The caller does not have permission to read the file.
        /// </exception>
        public DeTofuMap WithCustomFile(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            var text = File.ReadAllText(path, Encoding.UTF8);
            return WithCustomEntries(DeTofu.ParseEntries(text));
        }

        /// <summary>
        /// Adds or overrides compatibility fallback pairs directly on this map.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Pairs are applied after the mappings already present in the map. If the same
        /// tofu-risk character is provided more than once, the later mapping wins.
        /// </para>
        /// <para>
        /// Only the first Unicode scalar value from each key and value is used. Empty or
        /// <see langword="null"/> keys and values are ignored. Unlike file entries, direct
        /// pairs do not carry an extension column, so they are always added to the map.
        /// </para>
        /// </remarks>
        /// <param name="pairs">Fallback pairs where the key is the tofu-risk character and the value is its fallback.</param>
        /// <returns>The current map instance, updated with the supplied pairs.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="pairs"/> is <see langword="null"/>.
        /// </exception>
        public DeTofuMap WithCustomPairs(IEnumerable<KeyValuePair<string, string>> pairs)
        {
            if (pairs == null)
                throw new ArgumentNullException(nameof(pairs));

            foreach (var pair in pairs)
            {
                var tofu = DeTofu.ReadFirstScalar(pair.Key);
                var fallback = DeTofu.ReadFirstScalar(pair.Value);

                if (tofu != null && fallback != null)
                    _map[tofu] = fallback;
            }

            return this;
        }

        /// <summary>
        /// Replaces mapped characters in the input text with their DeTofu fallback characters.
        /// </summary>
        /// <remarks>
        /// Unmapped characters are preserved unchanged. A <see langword="null"/> input value
        /// returns <see cref="String.Empty"/>.
        /// </remarks>
        /// <param name="input">The input text to process.</param>
        /// <returns>Processed text with mapped characters replaced and all unmapped characters preserved.</returns>
        public string Convert(string input)
        {
            if (string.IsNullOrEmpty(input) || _map.Count == 0)
                return input ?? string.Empty;

            var output = new StringBuilder(input.Length);

            foreach (var scalar in DeTofu.EnumerateScalars(input))
            {
                output.Append(_map.TryGetValue(scalar, out var fallback) ? fallback : scalar);
            }

            return output.ToString();
        }

        private DeTofuMap WithCustomEntries(IEnumerable<DeTofuEntry> entries)
        {
            foreach (var entry in entries)
            {
                if (entry.Extension >= _level)
                    _map[entry.Tofu] = entry.Fallback;
            }

            return this;
        }
    }
}