using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OpenccNetLib
{
    /// <summary>
    /// Controls which CJK extension ranges are replaced by detofu.
    /// </summary>
    /// <remarks>
    /// Detofu levels are threshold-based: the selected level is the earliest
    /// extension block to replace, and all supported later extension blocks are
    /// replaced too. <see cref="DeTofuLevel.ExtB"/> is the broadest level and
    /// is equivalent to the CLI concept of <c>all</c>.
    /// </remarks>
    public enum DeTofuLevel
    {
        /// <summary>Replace CJK Extension B and all supported later extension mappings.</summary>
        ExtB = 0,

        /// <summary>Replace CJK Extension C and all supported later extension mappings.</summary>
        ExtC = 1,

        /// <summary>Replace CJK Extension D and all supported later extension mappings.</summary>
        ExtD = 2,

        /// <summary>Replace CJK Extension E and all supported later extension mappings.</summary>
        ExtE = 3,

        /// <summary>Replace CJK Extension F and all supported later extension mappings.</summary>
        ExtF = 4,

        /// <summary>Replace CJK Extension G and all supported later extension mappings.</summary>
        ExtG = 5,

        /// <summary>Replace CJK Extension H and all supported later extension mappings.</summary>
        ExtH = 6,

        /// <summary>Replace CJK Extension I mappings.</summary>
        ExtI = 7
    }

    /// <summary>
    /// Display compatibility fallback utilities for rare non-BMP CJK extension characters.
    /// </summary>
    public static class DeTofu
    {
        private static readonly Lazy<List<DeTofuEntry>> BuiltinEntries =
            new Lazy<List<DeTofuEntry>>(LoadBuiltinEntries);

        /// <summary>
        /// Parses a detofu level string.
        /// </summary>
        /// <param name="value">Level name such as <c>all</c>, <c>ext-b</c>, <c>b</c>, or <c>ext-i</c>.</param>
        /// <returns>The parsed detofu level.</returns>
        /// <exception cref="ArgumentException">Thrown when the level is unsupported.</exception>
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
                        "Supported detofu levels: all, ext-b, ext-c, ext-d, ext-e, ext-f, ext-g, ext-h, ext-i.",
                        nameof(value));
            }
        }

        /// <summary>
        /// Converts non-BMP CJK extension characters to compatibility fallbacks.
        /// </summary>
        /// <param name="input">Input text.</param>
        /// <param name="level">Threshold-based detofu level.</param>
        /// <returns>Text with mapped tofu-risk characters replaced. Unmapped characters are preserved unchanged.</returns>
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

        public string Tofu { get; private set; }

        public string Fallback { get; private set; }

        public DeTofuLevel Extension { get; private set; }
    }

    /// <summary>
    /// Reusable detofu display-compatibility fallback map.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="DeTofuMap"/> is useful when callers want to build a fallback table once
    /// and reuse it across many strings, or layer application-specific fallbacks on top
    /// of the built-in map.
    /// </para>
    /// <para>
    /// Characters without a built-in or custom fallback mapping are preserved unchanged,
    /// even when they belong to an enabled CJK extension block.
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
        /// Builds a detofu map from the library's built-in compatibility data.
        /// </summary>
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
        /// Adds or overrides compatibility fallback entries from a UTF-8 tofu mapping file.
        /// </summary>
        /// <remarks>
        /// The file format is <c>tofu_char&lt;TAB&gt;fallback_char&lt;TAB&gt;extension</c>.
        /// The extension field accepts compact values such as <c>B</c> through <c>I</c>,
        /// or legacy values such as <c>ExtB</c> through <c>ExtI</c>.
        /// Custom mappings override built-in mappings when the same tofu-risk character is provided.
        /// Entries below this map's threshold level are ignored.
        /// </remarks>
        public DeTofuMap WithCustomFile(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            var text = File.ReadAllText(path, Encoding.UTF8);
            return WithCustomEntries(DeTofu.ParseEntries(text));
        }

        /// <summary>
        /// Adds or overrides compatibility fallback pairs after loading the map.
        /// </summary>
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
        /// Replaces mapped non-BMP CJK extension characters with compatibility fallbacks.
        /// </summary>
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