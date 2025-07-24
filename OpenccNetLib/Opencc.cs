using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        /// <summary>
        /// Enum representing supported OpenCC configuration keys.
        /// </summary>
        private enum OpenccConfig
        {
            S2T,
            T2S,
            S2Tw,
            Tw2S,
            S2Twp,
            Tw2Sp,
            S2Hk,
            Hk2S
        }

        /// <summary>
        /// Delimiter modes for segmenting text.
        /// Internal use only – <see cref="Full"/> is always used in public APIs.
        /// </summary>
        private enum DelimiterMode
        {
            /// <summary>
            /// Minimal mode: uses only newline characters ('\n') as delimiters.
            /// Fastest, suitable for large text blocks (e.g. EPUB files) where fine segmentation isn't needed.
            /// </summary>
            Minimal,

            /// <summary>
            /// Normal mode: uses a small set of common punctuation (e.g. ， 。 ！ ？ and '\n') for balanced segmentation.
            /// Suitable for general-purpose usage.
            /// </summary>
            Normal,

            /// <summary>
            /// Full mode: uses an extensive set of delimiters including punctuation, whitespace, brackets, and symbols.
            /// Most accurate for text like articles, emails, and web content.
            /// </summary>
            Full
        }

        /// <summary>
        /// Default delimiters used for segmenting input text based on <see cref="DelimiterMode.Full"/>.
        /// This set includes spaces, punctuation marks, brackets, and other common symbols for accurate word segmentation.
        /// For <see cref="DelimiterMode.Minimal"/>, only newline characters are used.
        /// </summary>
        private static readonly HashSet<char> Delimiters = GetDelimiters(DelimiterMode.Full);


        /// <summary>
        /// Returns the set of delimiters for internal segmentation logic.
        /// Only <see cref="DelimiterMode.Full"/> is used in public-facing behavior.
        /// </summary>
        private static HashSet<char> GetDelimiters(DelimiterMode mode)
        {
            switch (mode)
            {
                case DelimiterMode.Minimal:
                    return new HashSet<char>("\n".ToCharArray());

                case DelimiterMode.Normal:
                    return new HashSet<char>("，。！？\n".ToCharArray());

                case DelimiterMode.Full:
                    return new HashSet<char>(
                        " \t\n\r!\"#$%&'()*+,-./:;<=>?@[\\]^_{}|~＝、。﹁﹂—－（）《》〈〉？！…／＼︒︑︔︓︿﹀︹︺︙︐［﹇］﹈︕︖︰︳︴︽︾︵︶｛︷｝︸﹃﹄【︻】︼　～．，；："
                            .ToCharArray());

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown delimiter mode");
            }
        }

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

        private string _config;
        private string _lastError;

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

        // Thread-local StringBuilder for efficient string concatenation.
        private static readonly ThreadLocal<StringBuilder> StringBuilderCache =
            new ThreadLocal<StringBuilder>(() => new StringBuilder(1024));

        /// <summary>
        /// Caches compiled <see cref="DictRefs"/> instances keyed by <see cref="OpenccConfig"/> and punctuation flag.
        /// </summary>
        /// <remarks>
        /// The cache key is constructed from the <see cref="OpenccConfig"/> enum name (e.g., <c>S2T</c>, <c>T2S</c>).
        /// If the <c>punctuation</c> flag is <c>true</c>, the suffix <c>_punct</c> is appended to the key.
        /// This allows the cache to distinguish between punctuation-aware and non-punctuation versions of each configuration.
        /// </remarks>
        private readonly ConcurrentDictionary<string, DictRefs> _configCache =
            new ConcurrentDictionary<string, DictRefs>();


        /// <summary>
        /// Retrieves or creates a <see cref="DictRefs"/> instance based on the specified OpenCC configuration
        /// and punctuation setting.
        /// </summary>
        /// <param name="config">The OpenCC configuration enum value.</param>
        /// <param name="punctuation">
        /// If <c>true</c>, includes punctuation dictionary in round 1 or 2 as appropriate.
        /// </param>
        /// <returns>A cached or newly constructed <see cref="DictRefs"/> instance.</returns>
        private DictRefs GetDictRefs(OpenccConfig config, bool punctuation)
        {
            var cacheKey = config + (punctuation ? "_punct" : "");

            return _configCache.GetOrAdd(cacheKey, _ =>
            {
                var d = _lazyDictionary.Value;
                List<DictWithMaxLength> baseRound1;
                List<DictWithMaxLength> round2;
                DictRefs refs;

                switch (config)
                {
                    case OpenccConfig.S2T:
                        baseRound1 = new List<DictWithMaxLength> { d.st_phrases, d.st_characters };
                        if (punctuation) baseRound1.Add(d.st_punctuations);
                        refs = new DictRefs(baseRound1);
                        break;

                    case OpenccConfig.T2S:
                        baseRound1 = new List<DictWithMaxLength> { d.ts_phrases, d.ts_characters };
                        if (punctuation) baseRound1.Add(d.ts_punctuations);
                        refs = new DictRefs(baseRound1);
                        break;

                    case OpenccConfig.S2Tw:
                        baseRound1 = new List<DictWithMaxLength> { d.st_phrases, d.st_characters };
                        if (punctuation) baseRound1.Add(d.st_punctuations);
                        refs = new DictRefs(baseRound1)
                            .WithRound2(new List<DictWithMaxLength> { d.tw_variants });
                        break;

                    case OpenccConfig.Tw2S:
                        round2 = new List<DictWithMaxLength> { d.ts_phrases, d.ts_characters };
                        if (punctuation) round2.Add(d.ts_punctuations);
                        refs = new DictRefs(new List<DictWithMaxLength>
                                { d.tw_variants_rev_phrases, d.tw_variants_rev })
                            .WithRound2(round2);
                        break;

                    case OpenccConfig.S2Twp:
                        baseRound1 = new List<DictWithMaxLength> { d.st_phrases, d.st_characters };
                        if (punctuation) baseRound1.Add(d.st_punctuations);
                        refs = new DictRefs(baseRound1)
                            .WithRound2(new List<DictWithMaxLength> { d.tw_phrases })
                            .WithRound3(new List<DictWithMaxLength> { d.tw_variants });
                        break;

                    case OpenccConfig.Tw2Sp:
                        round2 = new List<DictWithMaxLength> { d.ts_phrases, d.ts_characters };
                        if (punctuation) round2.Add(d.ts_punctuations);
                        refs = new DictRefs(new List<DictWithMaxLength>
                                { d.tw_phrases_rev, d.tw_variants_rev_phrases, d.tw_variants_rev })
                            .WithRound2(round2);
                        break;

                    case OpenccConfig.S2Hk:
                        baseRound1 = new List<DictWithMaxLength> { d.st_phrases, d.st_characters };
                        if (punctuation) baseRound1.Add(d.st_punctuations);
                        refs = new DictRefs(baseRound1)
                            .WithRound2(new List<DictWithMaxLength> { d.hk_variants });
                        break;

                    case OpenccConfig.Hk2S:
                        round2 = new List<DictWithMaxLength> { d.ts_phrases, d.ts_characters };
                        if (punctuation) round2.Add(d.ts_punctuations);
                        refs = new DictRefs(new List<DictWithMaxLength>
                                { d.hk_variants_rev_phrases, d.hk_variants_rev })
                            .WithRound2(round2);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(config), config, null);
                }

                return refs;
            });
        }

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
                    _lastError = $"Invalid config provided: {value}. Using default 's2t'.";
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

        /// <summary>
        /// Performs segment replacement using the provided dictionaries.
        /// Splits the input text by delimiters and applies dictionary-based conversion to each segment.
        /// </summary>
        /// <param name="text">The input text to convert.</param>
        /// <param name="dictionaries">The list of dictionaries to use for conversion.</param>
        /// <param name="maxWordLength">Maximum word length for dictionaries</param>
        /// <returns>The converted text.</returns>
        private static string SegmentReplace(string text, List<DictWithMaxLength> dictionaries, int maxWordLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var splitRanges = GetSplitRangesSpan(text.AsSpan(), inclusive: true);

            if (splitRanges.Count == 0)
            {
                if (!string.IsNullOrEmpty(text))
                    splitRanges.Add((0, text.Length));
                else
                    return string.Empty;
            }

            // Shortcut for single segment
            if (splitRanges.Count == 1)
            {
                return ConvertBy(text.AsSpan(), dictionaries, maxWordLength);
            }

            var results = new string[splitRanges.Count];

            if (splitRanges.Count > 16 && text.Length > 2000)
            {
                Parallel.For(0, splitRanges.Count, i =>
                {
                    var (start, end) = splitRanges[i];
                    var segment = text.AsSpan(start, end - start);
                    results[i] = ConvertBy(segment, dictionaries, maxWordLength);
                });
            }
            else
            {
                for (var i = 0; i < splitRanges.Count; i++)
                {
                    var (start, end) = splitRanges[i];
                    var segment = text.AsSpan(start, end - start);
                    results[i] = ConvertBy(segment, dictionaries, maxWordLength);
                }
            }

            // Always use StringBuilder with pre-capacity
            var sb = new StringBuilder((int)(text.Length * 1.1));
            foreach (var s in results)
                sb.Append(s);

            return sb.ToString();
        }

        /// <summary>
        /// Converts a string using the provided dictionaries, matching the longest possible key at each position.
        /// </summary>
        /// <param name="textSpan">The input text segment.</param>
        /// <param name="dictionaries">The dictionaries to use for lookup.</param>
        /// <param name="maxWordLength">The maximum key length to consider.</param>
        /// <returns>The converted string segment.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string ConvertBy(ReadOnlySpan<char> textSpan, List<DictWithMaxLength> dictionaries,
            int maxWordLength)
        {
            switch (textSpan.Length)
            {
                case 0:
                    return string.Empty;
                // Quick check for single delimiter
                case 1 when Delimiters.Contains(textSpan[0]):
                    return textSpan.ToString();
            }

            // Use thread-local StringBuilder
            var resultBuilder = StringBuilderCache.Value;
            resultBuilder.Clear();
            resultBuilder.EnsureCapacity(textSpan.Length * 2);

            var textLen = textSpan.Length;
            var i = 0;

            // Use ArrayPool for better memory management
            var keyBuffer = ArrayPool<char>.Shared.Rent(maxWordLength);

            try
            {
                while (i < textLen)
                {
                    var remaining = textSpan.Slice(i);
                    var tryMaxLen = Math.Min(maxWordLength, remaining.Length);

                    string bestMatch = null;
                    var bestMatchLength = 0;

                    // Optimize dictionary lookup order
                    for (var length = tryMaxLen; length > 0; --length)
                    {
                        var wordSpan = remaining.Slice(0, length);

                        // Check each dictionary
                        foreach (var dictObj in dictionaries)
                        {
                            if (dictObj.MaxLength < length) continue;

                            // Copy to buffer and create string key
                            wordSpan.CopyTo(keyBuffer.AsSpan());
                            var key = new string(keyBuffer, 0, length);

                            if (!dictObj.Data.TryGetValue(key, out var match)) continue;
                            bestMatch = match;
                            bestMatchLength = length;
                            goto FoundMatch; // Break out of both loops
                        }
                    }

                    FoundMatch:
                    if (bestMatch != null)
                    {
                        resultBuilder.Append(bestMatch);
                        i += bestMatchLength;
                    }
                    else
                    {
                        resultBuilder.Append(textSpan[i]);
                        i++;
                    }
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(keyBuffer);
            }

            return resultBuilder.ToString();
        }

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
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.tw_variants
            });
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Traditional Chinese to Taiwan Traditional Chinese (with phrase and variant rounds).
        /// </summary>
        public string T2Twp(string inputText)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.tw_phrases
            }).WithRound2(new List<DictWithMaxLength>
            {
                Dictionary.tw_variants
            });
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Taiwan Traditional Chinese to Traditional Chinese.
        /// </summary>
        public string Tw2T(string inputText)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.tw_variants_rev_phrases,
                Dictionary.tw_variants_rev
            });
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Taiwan Traditional Chinese to Traditional Chinese (with phrase round).
        /// </summary>
        public string Tw2Tp(string inputText)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.tw_variants_rev_phrases,
                Dictionary.tw_variants_rev
            }).WithRound2(new List<DictWithMaxLength>
            {
                Dictionary.tw_phrases_rev
            });
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Traditional Chinese to Hong Kong Traditional Chinese.
        /// </summary>
        public string T2Hk(string inputText)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.hk_variants
            });
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Hong Kong Traditional Chinese to Traditional Chinese.
        /// </summary>
        public string Hk2T(string inputText)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.hk_variants_rev_phrases,
                Dictionary.hk_variants_rev
            });
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Traditional Chinese to Japanese Kanji variants.
        /// </summary>
        public string T2Jp(string inputText)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.jp_variants
            });
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Japanese Kanji variants to Traditional Chinese.
        /// </summary>
        public string Jp2T(string inputText)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.jps_phrases,
                Dictionary.jps_characters,
                Dictionary.jp_variants_rev
            });
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

        /// <summary>
        /// Converts Simplified Chinese characters to Traditional Chinese (single character only).
        /// </summary>
        public static string St(string inputText)
        {
            var dictRefs = new List<DictWithMaxLength> { Dictionary.st_characters };
            return ConvertBy(inputText.AsSpan(), dictRefs, 1);
        }

        /// <summary>
        /// Converts Traditional Chinese characters to Simplified Chinese (single character only).
        /// </summary>
        public static string Ts(string inputText)
        {
            var dictRefs = new List<DictWithMaxLength> { Dictionary.ts_characters };
            return ConvertBy(inputText.AsSpan(), dictRefs, 1);
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

            var stripped = StripRegex.Replace(inputText, "");
            stripped = stripped.Substring(0, Math.Min(stripped.Length, 100));
            var maxChars = FindMaxUtf8Length(stripped, 200);
            var stripText = stripped.Substring(0, maxChars);

            var tsConverted = Ts(stripText);
            if (stripText != tsConverted) return 1;

            var stConverted = St(stripText);
            return stripText != stConverted ? 2 : 0;
        }

        /// <summary>
        /// Finds the maximum substring length that fits within the specified UTF-8 byte count.
        /// Ensures truncation does not split multibyte characters.
        /// Returns the number of characters that can be safely included
        /// within a UTF-8 byte slice of `maxByteCount`.
        /// </summary>
        /// <param name="s">The input string.</param>
        /// <param name="maxByteCount">The maximum allowed byte count.</param>
        /// <returns>The maximum substring length that fits.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindMaxUtf8Length(string s, int maxByteCount)
        {
            if (string.IsNullOrEmpty(s)) return 0;

            var encoded = Encoding.UTF8.GetBytes(s);
            if (encoded.Length <= maxByteCount) return s.Length;

            var byteCount = maxByteCount;
            while (byteCount > 0 && (encoded[byteCount - 1] & 0b11000000) == 0b10000000)
                byteCount--;

            var partialString = Encoding.UTF8.GetString(encoded, 0, byteCount);
            return partialString.Length;
        }

        /// <summary>
        /// Splits the input span into ranges based on delimiter characters.
        /// </summary>
        /// <param name="input">The input character span.</param>
        /// <param name="inclusive">
        /// If true, each segment includes the delimiter (e.g. "你好，").
        /// If false (default), each segment excludes the delimiter and delimiters are returned as their own segment.
        /// </param>
        /// <returns>A list of (start, end) index tuples for each segment.</returns>
        private static List<(int start, int end)> GetSplitRangesSpan(ReadOnlySpan<char> input, bool inclusive = false)
        {
            var length = input.Length;

            if (input.IsEmpty)
                return new List<(int, int)>();

            // Pre-size based on estimate (assume ~10% delimiters for better initial capacity)
            var estimatedCapacity = Math.Max(8, length / 10);
            var ranges = new List<(int, int)>(estimatedCapacity);

            var currentStart = 0;

            // Use spans for delimiter lookup if Delimiters is a collection
            for (var i = 0; i < length; i++)
            {
                if (!Delimiters.Contains(input[i]))
                    continue;

                if (inclusive)
                {
                    // Include delimiter in current segment
                    if (i >= currentStart) // Simplified condition
                    {
                        ranges.Add((currentStart, i + 1));
                    }
                }
                else
                {
                    // Add segment before delimiter (if any)
                    if (i > currentStart)
                    {
                        ranges.Add((currentStart, i));
                    }

                    // Add delimiter as separate segment
                    ranges.Add((i, i + 1));
                }

                currentStart = i + 1;
            }

            // Add remaining segment if any
            if (currentStart < length)
            {
                ranges.Add((currentStart, length));
            }

            return ranges;
        }
    }
}