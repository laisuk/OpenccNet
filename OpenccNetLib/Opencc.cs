using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpenccNetLib
{
    /// <summary>
    /// Main class for OpenCC text conversion. Provides methods for various conversion directions
    /// (Simplified-Traditional, Traditional-Simplified, etc.) and supports multi-stage, high-performance conversion.
    /// </summary>
    public class Opencc
    {
        // Regex for stripping non-Chinese and non-symbol characters.
        private static readonly Regex StripRegex = new Regex(
            @"[!-/:-@\[-`{-~\t\n\v\f\r 0-9A-Za-z_著]",
            RegexOptions.Compiled);

        // Supported configuration names for conversion directions.
        private static readonly HashSet<string> ConfigList = new HashSet<string>(StringComparer.Ordinal)
        {
            "s2t", "t2s", "s2tw", "tw2s", "s2twp", "tw2sp", "s2hk", "hk2s", "t2tw", "tw2t", "t2twp", "tw2tp",
            "t2hk", "hk2t", "t2jp", "jp2t"
        };

        // Thread-local StringBuilder for efficient string concatenation.
        private static readonly ThreadLocal<StringBuilder> StringBuilderCache =
            new ThreadLocal<StringBuilder>(() => new StringBuilder(1024));

        #region Config Enum Region

        /// <summary>
        /// Enum representing supported OpenCC configuration keys.
        /// </summary>
        public enum OpenccConfig
        {
            S2T,
            T2S,
            S2Tw,
            Tw2S,
            S2Twp,
            Tw2Sp,
            S2Hk,
            Hk2S,
            T2Tw,
            T2Twp,
            Tw2T,
            Tw2Tp,
            T2Hk,
            Hk2T,
            T2Jp,
            Jp2T
        }

        /// <summary>
        /// Converts the specified <see cref="OpenccConfig"/> enum value to its corresponding string representation.
        /// </summary>
        /// <param name="configEnum">The enum value representing the desired OpenCC configuration.</param>
        /// <returns>
        /// A lowercase string representing the configuration (e.g., "s2t", "t2s").
        /// If the input is not recognized, defaults to "s2t".
        /// </returns>
        private static string ConfigEnumToString(OpenccConfig configEnum)
        {
            switch (configEnum)
            {
                case OpenccConfig.S2T: return "s2t";
                case OpenccConfig.T2S: return "t2s";
                case OpenccConfig.S2Tw: return "s2tw";
                case OpenccConfig.Tw2S: return "tw2s";
                case OpenccConfig.S2Twp: return "s2twp";
                case OpenccConfig.Tw2Sp: return "tw2sp";
                case OpenccConfig.S2Hk: return "s2hk";
                case OpenccConfig.Hk2S: return "hk2s";
                case OpenccConfig.T2Tw: return "t2tw";
                case OpenccConfig.T2Twp: return "t2twp";
                case OpenccConfig.Tw2T: return "tw2t";
                case OpenccConfig.Tw2Tp: return "tw2tp";
                case OpenccConfig.T2Hk: return "t2hk";
                case OpenccConfig.Hk2T: return "hk2t";
                case OpenccConfig.T2Jp: return "t2jp";
                case OpenccConfig.Jp2T: return "jp2t";
                default: return "s2t";
            }
        }

        /// <summary>
        /// Attempts to parse a configuration string into an <see cref="OpenccConfig"/> enum value.
        /// </summary>
        /// <param name="config">The configuration string to parse (e.g., "s2t", "tw2sp").</param>
        /// <param name="result">
        /// When this method returns, contains the <see cref="OpenccConfig"/> value equivalent to the input string,
        /// if the conversion succeeded, or the default value if the conversion failed.
        /// </param>
        /// <returns>
        /// <c>true</c> if the input string was successfully parsed into an <see cref="OpenccConfig"/>; otherwise, <c>false</c>.
        /// </returns>
        public static bool TryParseConfig(string config, out OpenccConfig result)
        {
            if (config == null)
            {
                result = default;
                return false;
            }

            switch (config.ToLowerInvariant())
            {
                case "s2t":
                    result = OpenccConfig.S2T;
                    return true;
                case "t2s":
                    result = OpenccConfig.T2S;
                    return true;
                case "s2tw":
                    result = OpenccConfig.S2Tw;
                    return true;
                case "tw2s":
                    result = OpenccConfig.Tw2S;
                    return true;
                case "s2twp":
                    result = OpenccConfig.S2Twp;
                    return true;
                case "tw2sp":
                    result = OpenccConfig.Tw2Sp;
                    return true;
                case "s2hk":
                    result = OpenccConfig.S2Hk;
                    return true;
                case "hk2s":
                    result = OpenccConfig.Hk2S;
                    return true;
                case "t2tw":
                    result = OpenccConfig.T2Tw;
                    return true;
                case "t2twp":
                    result = OpenccConfig.T2Twp;
                    return true;
                case "tw2t":
                    result = OpenccConfig.Tw2T;
                    return true;
                case "tw2tp":
                    result = OpenccConfig.Tw2Tp;
                    return true;
                case "t2hk":
                    result = OpenccConfig.T2Hk;
                    return true;
                case "hk2t":
                    result = OpenccConfig.Hk2T;
                    return true;
                case "t2jp":
                    result = OpenccConfig.T2Jp;
                    return true;
                case "jp2t":
                    result = OpenccConfig.Jp2T;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }

        #endregion

        #region Delimiters Region

        /// <summary>
        /// Full delimiter set (spaces, punctuation, brackets, symbols, etc.).
        /// Centralized here so it’s easy to audit/extend.
        /// </summary>
        private const string FullDelimiters =
            " \t\n\r!\"#$%&'()*+,-./:;<=>?@[\\]^_{}|~＝、。﹁﹂—－（）《》〈〉？！…／＼︒︑︔︓︿﹀︹︺︙︐［﹇］﹈︕︖︰︳︴︽︾︵︶｛︷｝︸﹃﹄【︻】︼　～．，；：";

        /// <summary>
        /// Provides fast O(1) delimiter membership checks using a precomputed bitset table.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Each of the 65,536 possible <see cref="char"/> values is mapped to one bit
        /// in a compact <c>ulong[]</c> table (8 KB total).
        /// </para>
        /// <para>
        /// This allows membership testing to be reduced to a single bitwise operation,
        /// which is far faster than <see cref="HashSet{T}.Contains"/> in hot per-character loops,
        /// especially for large text (e.g. multi-MB documents).
        /// </para>
        /// <para>
        /// The table is initialized once in the static constructor by setting the bit
        /// corresponding to each delimiter character in <c>FullDelimiters</c>.
        /// </para>
        /// </remarks>
        private static class DelimiterTable
        {
            private static readonly ulong[] T = new ulong[0x10000 / 64];

            static DelimiterTable()
            {
                foreach (var ch in FullDelimiters)
                {
                    T[ch >> 6] |= 1UL << (ch & 63);
                }
            }

            /// <summary>
            /// Tests whether the given character is a delimiter.
            /// </summary>
            /// <param name="c">The character to test.</param>
            /// <returns>
            /// <c>true</c> if <paramref name="c"/> is in the delimiter set;
            /// otherwise, <c>false</c>.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static bool Contains(char c) => (T[c >> 6] & (1UL << (c & 63))) != 0;
        }

        /// <summary>
        /// Inline helper method that wraps <see cref="DelimiterTable.Contains"/>.
        /// </summary>
        /// <remarks>
        /// This method exists mainly for code readability at call sites.  
        /// Because it is marked with <see cref="MethodImplOptions.AggressiveInlining"/>,
        /// the JIT will inline it, so it has effectively zero overhead at runtime.
        /// </remarks>
        /// <param name="c">The character to test.</param>
        /// <returns>
        /// <c>true</c> if <paramref name="c"/> is a delimiter; otherwise, <c>false</c>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDelimiter(char c) => DelimiterTable.Contains(c);

        #endregion

        #region Lazy Static Dictionary Region

        // --- START Lazy<T> Implementation ---

        // Use Lazy<T> for the dictionary and derived lists
        // Initialize these in the static constructor.
        private static Lazy<DictionaryMaxlength> _lazyDictionary;

        // Static constructor to initialize the Lazy<T> instances.
        // This runs once, automatically and thread-safely, when the Opencc class is first accessed.
        static Opencc()
        {
            Warmup();
        }

        /// <summary>
        /// Initializes the static Lazy&lt;T&gt; fields for the Opencc class and preloads the default dictionary and round lists.
        /// This method is called once by the static constructor to ensure that the default dictionary and its associated
        /// conversion lists are loaded and ready for use. It also preloads round list values to minimize lazy initialization
        /// overhead during the first conversion operation.
        /// </summary>
        private static void Warmup()
        {
            var dict = DictionaryLib.New(); // Load default config
            InitializeLazyLoaders(dict); // Initialize with the default dictionary

            // Warm up JIT + Tiered PGO for segment replacement logic,
            // and potentially pre-spin the ThreadPool if parallel thresholds are met.
            // Uncomment to enable conversion warmup.
            // _ = new Opencc("s2t").Convert("预热文本");
        }

        /// <summary>
        /// Gets the loaded dictionary set for all conversion types.
        /// This property will lazily load the default dictionary if no custom one has been set.
        /// </summary>
        private static DictionaryMaxlength Dictionary => _lazyDictionary.Value;

        /// <summary>
        /// Helper method to create the Lazy<T/> instances for the dictionary and its derived lists.
        /// This is used both for default loading and when a custom dictionary is provided.
        /// </summary>
        /// <param name="initialDictionary">The dictionary instance to use for initialization.</param>
        private static void InitializeLazyLoaders(DictionaryMaxlength initialDictionary)
        {
            // The factory method for _lazyDictionary.
            // This ensures that the dictionary is set up, and then the derived lists are configured based on it.
            _lazyDictionary =
                new Lazy<DictionaryMaxlength>(() => initialDictionary,
                    LazyThreadSafetyMode.ExecutionAndPublication); // Ensures thread safety for the Lazy<T> itself
        }

        // === Public Static Methods for Custom Dictionary Loading (Optional for Users) ===

        /// <summary>
        /// Overrides the default Opencc dictionary with a custom DictionaryMaxlength instance.
        /// This method should be called once at application startup, if a custom dictionary is desired.
        /// </summary>
        /// <param name="customDictionary">The custom dictionary instance.</param>
        public static void UseCustomDictionary(DictionaryMaxlength customDictionary)
        {
            if (customDictionary == null)
            {
                throw new ArgumentNullException(nameof(customDictionary), "Custom dictionary cannot be null.");
            }

            // Re-initialize the Lazy<T> instances to point to the new custom dictionary.
            // This effectively "resets" the lazy loading for the custom dictionary.
            InitializeLazyLoaders(customDictionary);
        }

        /// <summary>
        /// Overrides the default Opencc dictionary by loading it from a specified path.
        /// This method should be called once at application startup, if custom dictionary is desired.
        /// </summary>
        /// <param name="dictionaryRelativePath">The path to the dictionary file(s).</param>
        public static void UseDictionaryFromPath(string dictionaryRelativePath)
        {
            UseCustomDictionary(DictionaryLib.FromDicts(dictionaryRelativePath));
        }

        /// <summary>
        /// Overrides the default Opencc dictionary by loading it from a JSON string.
        /// This method should be called once at application startup, if custom dictionary is desired.
        /// </summary>
        /// <param name="jsonString">The JSON string representing the dictionary.</param>
        public static void UseDictionaryFromJsonString(string jsonString)
        {
            UseCustomDictionary(JsonSerializer.Deserialize<DictionaryMaxlength>(jsonString));
        }

        // --- END Lazy<T> Implementation ---

        #endregion

        #region Opencc Contructor and Public Fields Region

        private string _config;

        private string _lastError;

        /// <summary>
        /// Initializes a new instance of the <see cref="Opencc"/> class with the specified configuration.
        /// This constructor ensures the global dictionary and its associated lists are initialized.
        /// </summary>
        /// <param name="config">The conversion configuration (e.g., "s2t", "t2s").</param>
        public Opencc(string config = null)
        {
            Config = config;
        }

        /// <summary>
        /// Gets or sets the current conversion configuration.
        /// If an invalid configuration is assigned, it falls back to "s2t" and records the error.
        /// </summary>
        public string Config
        {
            get => _config;
            set
            {
                var lower = value?.ToLowerInvariant();
                if (IsValidConfig(lower))
                {
                    _config = lower;
                    _lastError = null;
                }
                else
                {
                    _config = "s2t";
                    _lastError = $"Invalid config provided: \"{value}\". Using default 's2t'.";
                }
            }
        }

        /// <summary>
        /// Sets the configuration value for the current OpenCC instance.
        /// If the provided config is invalid, it reverts to the default "s2t".
        /// </summary>
        /// <param name="config">The configuration name to set (e.g., "s2t", "t2s").</param>
        public void SetConfig(string config)
        {
            Config = IsValidConfig(config) ? config : "s2t";
        }

        /// <summary>
        /// Sets the configuration using the <see cref="OpenccConfig"/> enum.
        /// </summary>
        /// <param name="configEnum">The OpenCC configuration enum value.</param>
        public void SetConfig(OpenccConfig configEnum)
        {
            Config = ConfigEnumToString(configEnum);
        }

        /// <summary>
        /// Gets the current configuration value of this OpenCC instance.
        /// </summary>
        /// <returns>The configuration name currently in use.</returns>
        public string GetConfig()
        {
            return Config;
        }

        /// <summary>
        /// Gets a read-only collection of all supported configuration names.
        /// </summary>
        /// <returns>A collection of valid configuration identifiers.</returns>
        public static IReadOnlyCollection<string> GetSupportedConfigs()
        {
            return ConfigList;
        }

        /// <summary>
        /// Checks whether the provided configuration name is valid.
        /// </summary>
        /// <param name="config">The configuration name to validate.</param>
        /// <returns><c>true</c> if the configuration is supported; otherwise, <c>false</c>.</returns>
        public static bool IsValidConfig(string config)
        {
            return ConfigList.Contains(config);
        }

        /// <summary>
        /// Gets the last error message, if any, from the most recent operation.
        /// </summary>
        public string GetLastError()
        {
            return _lastError;
        }

        #endregion

        #region Core Convertion Region

        /// <summary>
        /// Performs segment replacement using the provided dictionaries.
        /// Splits the input text by delimiters and applies dictionary-based conversion to each segment.
        /// </summary>
        /// <param name="text">The input text to convert.</param>
        /// <param name="dictionaries">The list of dictionaries to use for conversion.</param>
        /// <param name="union"></param>
        /// <param name="maxWordLength">Maximum word length for dictionaries</param>
        /// <returns>The converted text.</returns>
        private static string SegmentReplace(string text, DictWithMaxLength[] dictionaries, StarterUnion union,
            int maxWordLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var textLength = text.Length;

            if (textLength < 10_000)
            {
                return ConvertByUnion(text.AsSpan(), dictionaries, union, maxWordLength);
            }

            var splitRanges = GetSplitRangesSpan(text.AsSpan(), true);
            var sb = new StringBuilder(textLength + (textLength >> 4)); // +6.8%

            // Shortcut for lower count segment
            if (splitRanges.Count > 1_000 || textLength > 100_000)
            {
                var results = new string[splitRanges.Count];
                Parallel.For(0, splitRanges.Count, i =>
                {
                    var segment = text.AsSpan(splitRanges[i].Start, splitRanges[i].Length);
                    results[i] = ConvertByUnion(segment, dictionaries, union, maxWordLength);
                });

                for (var i = 0; i < results.Length; i++)
                    sb.Append(results[i]);
            }
            else
            {
                for (var i = 0; i < splitRanges.Count; i++)
                {
                    var segment = text.AsSpan(splitRanges[i].Start, splitRanges[i].Length);
                    sb.Append(ConvertByUnion(segment, dictionaries, union, maxWordLength));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Converts text using one or more dictionaries, matching the longest possible key
        /// at each position. 
        /// </summary>
        /// <remarks>
        /// The conversion process is optimized by using a <see cref="StarterUnion"/>:
        /// <list type="bullet">
        ///   <item>
        ///     <description>
        ///     O(1) lookup of per-starter metadata (maximum length, minimum length, length bitmask).
        ///     </description>
        ///   </item>
        ///   <item>
        ///     <description>
        ///     Fast path: single-grapheme replacements are applied immediately when no longer match
        ///     is possible.
        ///     </description>
        ///   </item>
        ///   <item>
        ///     <description>
        ///     General path: longest-first search within the range [<c>unionMinLen</c> … <c>tryMax</c>],
        ///     skipping impossible lengths using the precomputed bitmask.
        ///     </description>
        ///   </item>
        /// </list>
        /// Graphemes are determined by UTF-16 code units: BMP characters are length 1; surrogate pairs
        /// are length 2.  
        /// Fallback: if no dictionary entry matches, the original grapheme is emitted unchanged.
        /// </remarks>
        /// <param name="textSpan">
        /// The input text segment as a <see cref="ReadOnlySpan{Char}"/>.
        /// </param>
        /// <param name="dictionaries">
        /// The list of dictionaries to use for lookup. Each dictionary specifies its own
        /// <c>MinLength</c> and <c>MaxLength</c> bounds.
        /// </param>
        /// <param name="union">
        /// The precomputed <see cref="StarterUnion"/> that provides per-starter caps,
        /// masks, and minimum lengths.
        /// </param>
        /// <param name="maxWordLength">
        /// The maximum key length to consider (global clamp).
        /// </param>
        /// <returns>
        /// A converted string with dictionary replacements applied.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string ConvertByUnion(
            ReadOnlySpan<char> textSpan,
            DictWithMaxLength[] dictionaries,
            StarterUnion union,
            int maxWordLength)
        {
            var n = textSpan.Length;
            switch (n)
            {
                case 0: return string.Empty;
                case 1 when IsDelimiter(textSpan[0]): return textSpan.ToString();
            }

            var sb = StringBuilderCache.Value;
            sb.Clear();
            sb.EnsureCapacity(n * 2 + (n >> 4));

            var i = 0;
            var keyBuffer = ArrayPool<char>.Shared.Rent(maxWordLength);

            try
            {
                while (i < n)
                {
                    var c0 = textSpan[i];
                    var hasSecond = i + 1 < n;
                    var c1 = hasSecond ? textSpan[i + 1] : '\0';

                    // Grapheme step: BMP=1, surrogate pair=2
                    // var step = char.IsHighSurrogate(c0) && remaining.Length > 1 && char.IsLowSurrogate(remaining[1])
                    //     ? 2
                    //     : 1;
                    // var step = (uint)(c0 - 0xD800) <= 0x03FF && hasSecond && (uint)(c1 - 0xDC00) <= 0x03FF ? 2 : 1;

                    // union.Get(c0, out var cap, out var lenMaskAll, out var unionMinLen);
                    // CHANGED: ask StarterUnion for step (starterUnits), presence, and masks.
                    union.GetAt(c0, c1, hasSecond,
                        out var step, out var hasStarter,
                        out var cap, out var lenMaskAll, out var unionMinLen);

                    // Upper bound: dict cap, remaining input, and caller max
                    var remaining = n - i;
                    var tryMax = Math.Min(Math.Min(maxWordLength, remaining), cap);

                    // No possible match? (no entries, or entries longer than we can take)
                    if (!hasStarter || cap == 0 || unionMinLen == 0 || unionMinLen > tryMax)
                    {
                        sb.Append(c0);
                        if (step == 2) sb.Append(c1);
                        i += step;
                        continue;
                    }

                    // Is there any candidate longer than the current grapheme size?
                    bool hasLonger;
                    if (tryMax < 64)
                    {
                        var maskUpToTry = lenMaskAll & ((1UL << tryMax) - 1);
                        hasLonger = step < tryMax && maskUpToTry >> step != 0UL;
                    }
                    else
                    {
                        // If tryMax >= 64 we can’t trim the mask cheaply → just check shift
                        hasLonger = lenMaskAll >> step != 0UL;
                    }

                    // Single-grapheme fast path when:
                    //  - there is a length==step candidate (bit set), AND
                    //  - there's no longer candidate to prefer, AND
                    //  - step >= minLen (NEW: respect lower bound)
                    if (!hasLonger &&
                        step >= unionMinLen &&
                        ((lenMaskAll >> (step - 1)) & 1UL) != 0UL)
                    {
                        keyBuffer[0] = c0;
                        if (step == 2) keyBuffer[1] = c1;

                        string keyStep = null;

                        for (var di = 0; di < dictionaries.Length; di++)
                        {
                            var d = dictionaries[di];
                            if (!d.SupportsLength(step)) continue;

                            if (keyStep == null)
                                keyStep = new string(keyBuffer, 0, step);
                            if (!d.TryGetValue(keyStep, out var repl)) continue;

                            sb.Append(repl);
                            i += step;
                            goto ContinueOuter;
                        }
                    }

                    // General longest-first search, narrowed to [minLen … tryMax]
                    string bestMatch = null;
                    var bestLen = 0;

                    // Copy once at max candidate length
                    // (We still need a copy because dict keys are strings)
                    textSpan.Slice(i, tryMax).CopyTo(keyBuffer);

                    // CHANGED: enforce lower bound = max(unionMinLen, step)
                    //  - ensures we never test len < step (e.g., len==1 for astral starters)
                    //  - len==1 for astrals is already masked out, but this makes it explicit and self-documenting
                    var lower = unionMinLen > step ? unionMinLen : step;

                    for (var len = tryMax; len >= lower; --len)
                    {
                        if (len <= 64 && ((lenMaskAll >> (len - 1)) & 1UL) == 0UL)
                            continue; // impossible length per mask

                        string key = null;

                        for (var di = 0; di < dictionaries.Length; di++)
                        {
                            var d = dictionaries[di];
                            if (!d.SupportsLength(len)) continue;

                            if (key == null)
                                key = new string(keyBuffer, 0, len);
                            if (!d.TryGetValue(key, out var repl)) continue;

                            bestMatch = repl;
                            bestLen = len;
                            goto Found;
                        }
                    }

                    Found:
                    if (bestMatch != null)
                    {
                        sb.Append(bestMatch);
                        i += bestLen;
                    }
                    else
                    {
                        // Reuse the precomputed step and c1 — no need to recompute
                        sb.Append(textSpan[i]);
                        if (step == 2) sb.Append(c1);
                        i += step;
                    }

                    ContinueOuter: ;
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(keyBuffer, clearArray: false);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Converts a span of characters using the provided dictionaries, matching the longest possible key at each position.
        /// </summary>
        /// <remarks>
        /// This method is the legacy, backward-compatible implementation of the conversion routine,
        /// updated to accept <see cref="ReadOnlySpan{Char}"/> for improved performance and reduced
        /// allocations. It preserves the same behavior as the original <c>ConvertBy(string,...)</c>
        /// overload, including delimiter handling and longest-match dictionary lookup.
        /// </remarks>
        /// <param name="text">
        /// The input text segment as a <see cref="ReadOnlySpan{Char}"/>.
        /// </param>
        /// <param name="dictionaries">
        /// The dictionaries to use for lookup. Each dictionary is paired with its maximum supported key length.
        /// </param>
        /// <param name="maxWordLength">
        /// The maximum key length to consider during matching. Longer candidates are truncated to this value.
        /// </param>
        /// <returns>
        /// The converted string segment, with all possible matches replaced by their dictionary values.
        /// If no match is found at a position, the original character is preserved.
        /// </returns>
        /// <seealso cref="ConvertByUnion(ReadOnlySpan{char}, DictWithMaxLength[], StarterUnion, int)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string ConvertBy(
            ReadOnlySpan<char> text,
            DictWithMaxLength[] dictionaries,
            int maxWordLength)
        {
            if (text.IsEmpty)
                return string.Empty;

            var textLen = text.Length;

            // Fast path: single delimiter
            if (textLen == 1 && IsDelimiter(text[0]))
                return text.ToString();

            var sb = StringBuilderCache.Value;
            sb.Clear();
            sb.EnsureCapacity(textLen * 2 + (textLen >> 4));

            var i = 0;

            // Ensure we rent at least length 1
            var bufferLen = Math.Max(1, maxWordLength);
            var keyBuffer = ArrayPool<char>.Shared.Rent(bufferLen);

            try
            {
                while (i < textLen)
                {
                    var remaining = text.Slice(i);
                    var tryMaxLen = Math.Min(maxWordLength, remaining.Length);

                    string bestMatch = null;
                    var bestMatchLength = 0;

                    // Descend from longest to shortest
                    for (var length = tryMaxLen; length > 0; --length)
                    {
                        string key = null;

                        // Probe dictionaries lazily:
                        // Only materialize the key when at least one dictionary supports this length.
                        for (var d = 0; d < dictionaries.Length; d++)
                        {
                            var dict = dictionaries[d];
                            if (!dict.SupportsLength(length))
                                continue;

                            if (key == null)
                            {
                                var wordSpan = remaining.Slice(0, length);
                                wordSpan.CopyTo(keyBuffer.AsSpan(0, length));
                                key = new string(keyBuffer, 0, length);
                            }

                            if (!dict.TryGetValue(key, out var match))
                                continue;

                            bestMatch = match;
                            bestMatchLength = length;
                            goto FoundMatch;
                        }
                    }

                    FoundMatch:
                    if (bestMatch != null)
                    {
                        sb.Append(bestMatch);
                        i += bestMatchLength;
                    }
                    else
                    {
                        sb.Append(text[i]);
                        i++;
                    }
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(keyBuffer, clearArray: false);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Represents a half-open character range [Start, End) within an input span.
        /// Used internally by OpenCC to track non-delimiter and delimiter segments.
        /// </summary>
        private readonly struct Range
        {
            /// <summary>
            /// Inclusive start index of the range (0-based).
            /// </summary>
            public int Start { get; }

            /// <summary>
            /// Exclusive end index of the range (0-based).
            /// </summary>
            private int End { get; }

            /// <summary>
            /// Initializes a new <see cref="Range"/> with the specified boundaries.
            /// </summary>
            /// <param name="start">Inclusive start index.</param>
            /// <param name="end">Exclusive end index.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Range(int start, int end)
            {
                Start = start;
                End = end;
            }

            /// <summary>
            /// Gets the length of the range (<c>End - Start</c>).
            /// </summary>
            public int Length => End - Start;

            /// <summary>
            /// Returns a string representation in the form <c>[Start, End)</c>.
            /// </summary>
            public override string ToString() => $"[{Start}, {End})";
        }

        /// <summary>
        /// Splits the input span into contiguous ranges of non-delimiter and delimiter segments.
        /// <para>
        /// This implementation uses a fast bit-level <see cref="IsDelimiter"/> check
        /// for compatibility and performance on .NET Standard 2.0.
        /// </para>
        /// </summary>
        /// <param name="input">The text span to segment.</param>
        /// <param name="inclusive">
        /// If <c>true</c>, each delimiter is included at the end of its preceding segment;
        /// otherwise delimiters are emitted as separate single-character ranges.
        /// </param>
        /// <returns>
        /// A list of <see cref="Range"/> objects representing half-open intervals [Start, End).
        /// </returns>
        private static List<Range> GetSplitRangesSpan(ReadOnlySpan<char> input, bool inclusive = false)
        {
            var length = input.Length;
            if (length == 0)
                return new List<Range>();

            // Heuristic: inclusive ≈ 25 % delimiters, exclusive ≈ 50 %.
            var estSegments = inclusive ? (length >> 2) + 1 : (length >> 1) + 1;
            if (estSegments < 8) estSegments = 8;
            if (estSegments > length) estSegments = length;

            var ranges = new List<Range>(estSegments);
            var currentStart = 0;

            for (var i = 0; i < length; i++)
            {
                if (!IsDelimiter(input[i])) continue;

                if (inclusive)
                {
                    // include delimiter in same segment
                    ranges.Add(new Range(currentStart, i + 1));
                }
                else
                {
                    if (i > currentStart)
                        ranges.Add(new Range(currentStart, i)); // text before delimiter

                    ranges.Add(new Range(i, i + 1)); // delimiter itself
                }

                currentStart = i + 1;
            }

            // Add trailing range if text doesn't end with a delimiter
            if (currentStart < length)
                ranges.Add(new Range(currentStart, length));

            return ranges;
        }

        #endregion

        #region Direct API and General Conversion Region

        /// <summary>
        /// Centralized cache for precomputed conversion plans.  
        /// Each plan bundles dictionary references, union masks, and other  
        /// runtime-optimized lookup data, keyed by configuration and punctuation mode.  
        /// Initialized with a delegate that returns the current <see cref="DictionaryMaxlength"/> instance.
        /// </summary>
        private readonly ConversionPlanCache _planCache =
            new ConversionPlanCache(() => Dictionary);

        /// <summary>
        /// Retrieves the dictionary references and lookup structures for the specified  
        /// OpenCC configuration and punctuation handling mode.  
        /// Results are served from <see cref="_planCache"/> to avoid rebuilding plans and  
        /// reduce GC pressure in high-throughput conversions.
        /// </summary>
        /// <param name="config">The OpenCC conversion configuration (e.g., S2T, T2HK).</param>
        /// <param name="punctuation">
        /// True to include punctuation conversion; false to leave punctuation unchanged.
        /// </param>
        /// <returns>A <see cref="DictRefs"/> object containing the prepared dictionary references.</returns>
        private DictRefs GetDictRefs(OpenccConfig config, bool punctuation)
            => _planCache.GetPlan(config, punctuation);

        /// <summary>
        /// Converts Simplified Chinese to Traditional Chinese.
        /// </summary>
        /// <param name="inputText">The input text.</param>
        /// <param name="punctuation">Whether to convert punctuation as well.</param>
        /// <returns>The converted text.</returns>
        public string S2T(string inputText, bool punctuation = false)
        {
            var refs = GetDictRefs(OpenccConfig.S2T, punctuation);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Traditional Chinese to Simplified Chinese.
        /// </summary>
        /// <param name="inputText">The input text.</param>
        /// <param name="punctuation">Whether to convert punctuation as well.</param>
        /// <returns>The converted text.</returns>
        public string T2S(string inputText, bool punctuation = false)
        {
            var refs = GetDictRefs(OpenccConfig.T2S, punctuation);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Simplified Chinese to Traditional Chinese (Taiwan standard).
        /// </summary>
        public string S2Tw(string inputText, bool punctuation = false)
        {
            var refs = GetDictRefs(OpenccConfig.S2Tw, punctuation);
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

        /// <summary>
        /// Converts Traditional Chinese (Taiwan standard) to Simplified Chinese.
        /// </summary>
        public string Tw2S(string inputText, bool punctuation = false)
        {
            var refs = GetDictRefs(OpenccConfig.Tw2S, punctuation);
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

        /// <summary>
        /// Converts Simplified Chinese to Traditional Chinese (Taiwan standard, with phrase and variant rounds).
        /// </summary>
        public string S2Twp(string inputText, bool punctuation = false)
        {
            var refs = GetDictRefs(OpenccConfig.S2Twp, punctuation);
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

        /// <summary>
        /// Converts Traditional Chinese (Taiwan) to Simplified Chinese (with phrase and variant rounds).
        /// </summary>
        public string Tw2Sp(string inputText, bool punctuation = false)
        {
            var refs = GetDictRefs(OpenccConfig.Tw2Sp, punctuation);
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

        /// <summary>
        /// Converts Simplified Chinese to Hong Kong Traditional Chinese.
        /// </summary>
        public string S2Hk(string inputText, bool punctuation = false)
        {
            var refs = GetDictRefs(OpenccConfig.S2Hk, punctuation);
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

        /// <summary>
        /// Converts Hong Kong Traditional Chinese to Simplified Chinese.
        /// </summary>
        public string Hk2S(string inputText, bool punctuation = false)
        {
            var refs = GetDictRefs(OpenccConfig.Hk2S, punctuation);
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

        /// <summary>
        /// Converts Traditional Chinese to Taiwan Traditional Chinese.
        /// </summary>
        public string T2Tw(string inputText)
        {
            var refs = GetDictRefs(OpenccConfig.T2Tw, false);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Traditional Chinese to Taiwan Traditional Chinese (with phrase and variant rounds).
        /// </summary>
        public string T2Twp(string inputText)
        {
            var refs = GetDictRefs(OpenccConfig.T2Twp, false);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Taiwan Traditional Chinese to Traditional Chinese.
        /// </summary>
        public string Tw2T(string inputText)
        {
            var refs = GetDictRefs(OpenccConfig.Tw2T, false);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Taiwan Traditional Chinese to Traditional Chinese (with phrase round).
        /// </summary>
        public string Tw2Tp(string inputText)
        {
            var refs = GetDictRefs(OpenccConfig.Tw2Tp, false);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Traditional Chinese to Hong Kong Traditional Chinese.
        /// </summary>
        public string T2Hk(string inputText)
        {
            var refs = GetDictRefs(OpenccConfig.T2Hk, false);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Hong Kong Traditional Chinese to Traditional Chinese.
        /// </summary>
        public string Hk2T(string inputText)
        {
            var refs = GetDictRefs(OpenccConfig.Hk2T, false);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Traditional Chinese to Japanese Kanji variants.
        /// </summary>
        public string T2Jp(string inputText)
        {
            var refs = GetDictRefs(OpenccConfig.T2Jp, false);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Japanese Kanji variants to Traditional Chinese.
        /// </summary>
        public string Jp2T(string inputText)
        {
            var refs = GetDictRefs(OpenccConfig.Jp2T, false);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts text according to the current <see cref="Config"/> setting.
        /// </summary>
        /// <param name="inputText">The input text.</param>
        /// <param name="punctuation">Whether to convert punctuation as well.</param>
        /// <returns>The converted text, or the original input if the config is invalid.</returns>
        public string Convert(string inputText, bool punctuation = false)
        {
            if (string.IsNullOrEmpty(inputText))
            {
                _lastError = "Input text is empty";
                return string.Empty;
            }

            try
            {
                switch (Config)
                {
                    case "s2t":
                        return S2T(inputText, punctuation);
                    case "s2tw":
                        return S2Tw(inputText, punctuation);
                    case "s2twp":
                        return S2Twp(inputText, punctuation);
                    case "s2hk":
                        return S2Hk(inputText, punctuation);
                    case "t2s":
                        return T2S(inputText, punctuation);
                    case "t2tw":
                        return T2Tw(inputText);
                    case "t2twp":
                        return T2Twp(inputText);
                    case "t2hk":
                        return T2Hk(inputText);
                    case "tw2s":
                        return Tw2S(inputText, punctuation);
                    case "tw2sp":
                        return Tw2Sp(inputText, punctuation);
                    case "tw2t":
                        return Tw2T(inputText);
                    case "tw2tp":
                        return Tw2Tp(inputText);
                    case "hk2s":
                        return Hk2S(inputText, punctuation);
                    case "hk2t":
                        return Hk2T(inputText);
                    case "jp2t":
                        return Jp2T(inputText);
                    case "t2jp":
                        return T2Jp(inputText);
                    default:
                        return inputText; // Return the original input
                }
            }
            catch (Exception e)
            {
                _lastError = $"Conversion failed: {e.Message}";
                return _lastError;
            }
        }

        #endregion

        #region zh-Hant / zh-Hans Detection Region

        /// <summary>
        /// Converts Simplified Chinese characters to Traditional Chinese (single character only).
        /// </summary>
        public static string St(string inputText)
        {
            var dictRefs = new[] { Dictionary.st_characters };
            // maxLength for surrogate pairs and non-BMP character is 2
            return ConvertBy(inputText.AsSpan(), dictRefs, 2);
        }

        /// <summary>
        /// Converts Traditional Chinese characters to Simplified Chinese (single character only).
        /// </summary>
        public static string Ts(string inputText)
        {
            var dictRefs = new[] { Dictionary.ts_characters };
            // maxLength for surrogate pairs and non-BMP character is 2
            return ConvertBy(inputText.AsSpan(), dictRefs, 2);
        }

        /// <summary>
        /// Checks if the input text is Simplified, Traditional, or neither.
        /// Returns 1 if Traditional, 2 if Simplified, 0 otherwise.
        /// </summary>
        /// <param name="inputText">The input text to check.</param>
        /// <returns>1 for Traditional, 2 for Simplified, 0 for neither.</returns>
        public static int ZhoCheck(string inputText)
        {
            if (string.IsNullOrEmpty(inputText)) return 0;

            var scanLength = inputText.Length > 500 ? 500 : inputText.Length;
            var stripped = StripRegex.Replace(inputText.Substring(0, scanLength), string.Empty);
            if (string.IsNullOrEmpty(stripped)) return 0;

            var stringInfo = new StringInfo(stripped);
            var lengthInElements = Math.Min(stringInfo.LengthInTextElements, 100);
            var safeText = stringInfo.SubstringByTextElements(0, lengthInElements);

            var tsConverted = Ts(safeText);
            if (safeText != tsConverted) return 1;

            var stConverted = St(safeText);
            return safeText != stConverted ? 2 : 0;
        }

        #endregion
    }
}