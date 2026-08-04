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

        private static readonly Lazy<Dictionary<int, string>[]> BuiltinMaps =
            new Lazy<Dictionary<int, string>[]>(CreateBuiltinMaps);

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

            if (TryParseLevel(value, out var level))
                return level;

            throw new ArgumentException(
                "Supported deTofu levels: all, ext-b, ext-c, ext-d, ext-e, ext-f, ext-g, ext-h, ext-i.",
                nameof(value));
        }

        /// <summary>
        /// Attempts to parse a textual DeTofu threshold.
        /// </summary>
        /// <param name="value">The textual level name to parse.</param>
        /// <param name="level">The parsed level when this method returns <see langword="true"/>.</param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="value"/> is recognized;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        private static bool TryParseLevel(
            string value,
            out DeTofuLevel level)
        {
            level = DeTofuLevel.ExtB;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            switch (value.Trim().ToLowerInvariant())
            {
                case "all":
                case "ext-b":
                case "extb":
                case "b":
                    level = DeTofuLevel.ExtB;
                    return true;

                case "ext-c":
                case "extc":
                case "c":
                    level = DeTofuLevel.ExtC;
                    return true;

                case "ext-d":
                case "extd":
                case "d":
                    level = DeTofuLevel.ExtD;
                    return true;

                case "ext-e":
                case "exte":
                case "e":
                    level = DeTofuLevel.ExtE;
                    return true;

                case "ext-f":
                case "extf":
                case "f":
                    level = DeTofuLevel.ExtF;
                    return true;

                case "ext-g":
                case "extg":
                case "g":
                    level = DeTofuLevel.ExtG;
                    return true;

                case "ext-h":
                case "exth":
                case "h":
                    level = DeTofuLevel.ExtH;
                    return true;

                case "ext-i":
                case "exti":
                case "i":
                    level = DeTofuLevel.ExtI;
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Converts mapped rare CJK extension characters to display-compatible fallback characters.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method uses cached built-in fallback mappings loaded from
        /// <c>dicts/TSCharactersTofu.txt</c>. To add or override mappings, build a reusable
        /// <see cref="DeTofuMap"/> with <see cref="DeTofuMap.Builtin(DeTofuLevel)"/>.
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
            return ConvertCore(input, GetBuiltinMap(level));
        }

        /// <summary>
        /// Parses DeTofu mapping entries from their text representation.
        /// </summary>
        /// <param name="text">The mapping-file contents to parse.</param>
        /// <returns>A list containing every valid mapping entry found in <paramref name="text"/>.</returns>
        /// <remarks>
        /// Invalid, incomplete, blank, and comment lines are ignored. Only the first Unicode
        /// scalar value in each source and fallback field is retained.
        /// </remarks>
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

                    var tofu = ReadFirstScalarValue(parts[0].Trim());
                    var fallback = ReadFirstScalar(parts[1].Trim());

                    if (!tofu.HasValue || fallback == null || !TryParseLevel(parts[2], out var ext))
                        continue;

                    entries.Add(new DeTofuEntry(tofu.Value, fallback, ext));
                }
            }

            return entries;
        }

        /// <summary>
        /// Gets the lazily loaded built-in DeTofu entries.
        /// </summary>
        /// <returns>The shared, read-only view of the built-in mapping entries.</returns>
        private static IReadOnlyList<DeTofuEntry> GetBuiltinEntries()
        {
            return BuiltinEntries.Value;
        }

        /// <summary>
        /// Resolves the deployed built-in DeTofu mapping-file path.
        /// </summary>
        /// <returns>The absolute or application-relative path rooted at <see cref="AppContext.BaseDirectory"/>.</returns>
        private static string GetBuiltinTofuPath()
        {
            var baseDir = AppContext.BaseDirectory;
            return Path.Combine(baseDir, "dicts", "TSCharactersTofu.txt");
        }

        /// <summary>
        /// Loads and parses the deployed built-in DeTofu mappings.
        /// </summary>
        /// <returns>The parsed entries, or an empty list when the optional mapping file is absent.</returns>
        private static List<DeTofuEntry> LoadBuiltinEntries()
        {
            var path = GetBuiltinTofuPath();

            if (!File.Exists(path))
            {
                return new List<DeTofuEntry>();
            }

            return ParseEntries(File.ReadAllText(path, Encoding.UTF8));
        }

        /// <summary>
        /// Reads the first Unicode scalar value from a string.
        /// </summary>
        /// <param name="value">The string to inspect.</param>
        /// <returns>
        /// A string containing the first Unicode scalar value, or <see langword="null"/> when
        /// <paramref name="value"/> is <see langword="null"/> or empty.
        /// </returns>
        /// <remarks>An unpaired surrogate is preserved as a single UTF-16 code unit.</remarks>
        internal static string ReadFirstScalar(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            var first = value[0];
            if (char.IsHighSurrogate(first) && value.Length >= 2 && char.IsLowSurrogate(value[1]))
                return value.Substring(0, 2);

            return value.Substring(0, 1);
        }

        /// <summary>
        /// Converts text with a prepared scalar-key fallback map.
        /// </summary>
        /// <param name="input">The input text. A <see langword="null"/> value is treated as empty.</param>
        /// <param name="map">The scalar-key fallback map to apply.</param>
        /// <returns>
        /// The converted text, or the original string instance when no mapped scalar is found.
        /// </returns>
        internal static string ConvertCore(string input, Dictionary<int, string> map)
        {
            if (string.IsNullOrEmpty(input) || map.Count == 0)
                return input ?? string.Empty;

            StringBuilder output = null;

            for (var i = 0; i < input.Length;)
            {
                var scalarStart = i;
                var scalar = ReadScalar(input, i, out var scalarLength);
                i += scalarLength;

                if (!map.TryGetValue(scalar, out var fallback))
                {
                    if (output != null)
                        output.Append(input, scalarStart, scalarLength);

                    continue;
                }

                if (output == null)
                {
                    output = new StringBuilder(input.Length);
                    output.Append(input, 0, scalarStart);
                }

                output.Append(fallback);
            }

            return output == null ? input : output.ToString();
        }

        /// <summary>
        /// Creates one cached built-in lookup map for each DeTofu threshold.
        /// </summary>
        /// <returns>The lookup maps indexed by <see cref="DeTofuLevel"/>.</returns>
        private static Dictionary<int, string>[] CreateBuiltinMaps()
        {
            var maps = new Dictionary<int, string>[8];

            for (var level = DeTofuLevel.ExtB; level <= DeTofuLevel.ExtI; level++)
            {
                var map = new Dictionary<int, string>();

                foreach (var entry in GetBuiltinEntries())
                {
                    if (entry.Extension >= level)
                        map[entry.Tofu] = entry.Fallback;
                }

                maps[(int)level] = map;
            }

            return maps;
        }

        /// <summary>
        /// Gets the shared built-in lookup map for a threshold level.
        /// </summary>
        /// <param name="level">The threshold level whose map is required.</param>
        /// <returns>The cached built-in lookup map.</returns>
        internal static Dictionary<int, string> GetBuiltinMap(DeTofuLevel level)
        {
            var index = (int)level;
            if ((uint)index >= (uint)BuiltinMaps.Value.Length)
                return new Dictionary<int, string>();

            return BuiltinMaps.Value[index];
        }

        /// <summary>
        /// Reads the first Unicode scalar value from a string.
        /// </summary>
        /// <param name="value">The string to inspect.</param>
        /// <returns>
        /// The first scalar value, or <see langword="null"/> when <paramref name="value"/>
        /// is <see langword="null"/> or empty.
        /// </returns>
        internal static int? ReadFirstScalarValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            return ReadScalar(value, 0, out _);
        }

        /// <summary>
        /// Reads one Unicode scalar or unpaired UTF-16 code unit from a string.
        /// </summary>
        /// <param name="value">The source string.</param>
        /// <param name="index">The zero-based UTF-16 position to read.</param>
        /// <param name="utf16Length">The number of UTF-16 code units consumed.</param>
        /// <returns>The scalar value, or the value of an unpaired surrogate code unit.</returns>
        private static int ReadScalar(string value, int index, out int utf16Length)
        {
            var first = value[index];

            if (char.IsHighSurrogate(first) &&
                index + 1 < value.Length &&
                char.IsLowSurrogate(value[index + 1]))
            {
                utf16Length = 2;
                return char.ConvertToUtf32(first, value[index + 1]);
            }

            utf16Length = 1;
            return first;
        }
    }

    internal sealed class DeTofuEntry
    {
        /// <summary>
        /// Initializes a parsed DeTofu mapping entry.
        /// </summary>
        /// <param name="tofu">The Unicode scalar value requiring a fallback.</param>
        /// <param name="fallback">The display-compatible fallback scalar.</param>
        /// <param name="extension">The CJK extension block associated with the entry.</param>
        public DeTofuEntry(int tofu, string fallback, DeTofuLevel extension)
        {
            Tofu = tofu;
            Fallback = fallback;
            Extension = extension;
        }

        /// <summary>Gets the Unicode scalar value requiring a fallback.</summary>
        public int Tofu { get; }

        /// <summary>Gets the display-compatible fallback scalar.</summary>
        public string Fallback { get; }

        /// <summary>Gets the CJK extension block associated with the mapping.</summary>
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
        private readonly Dictionary<int, string> _map;

        private DeTofuMap(DeTofuLevel level, Dictionary<int, string> map)
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
            return new DeTofuMap(
                level,
                new Dictionary<int, string>(DeTofu.GetBuiltinMap(level)));
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
                var tofu = DeTofu.ReadFirstScalarValue(pair.Key);
                var fallback = DeTofu.ReadFirstScalar(pair.Value);

                if (tofu.HasValue && fallback != null)
                    _map[tofu.Value] = fallback;
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
            return DeTofu.ConvertCore(input, _map);
        }

        /// <summary>
        /// Adds parsed file entries that satisfy this map's extension threshold.
        /// </summary>
        /// <param name="entries">The parsed mapping entries to apply in enumeration order.</param>
        /// <returns>The current map instance after eligible entries have been applied.</returns>
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