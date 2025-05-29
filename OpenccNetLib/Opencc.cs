using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpenccNetLib
{
    /// <summary>
    /// Helper class for managing multiple rounds of dictionary references for multi-stage text conversion.
    /// </summary>
    public class DictRefs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DictRefs"/> class with the first round of dictionaries.
        /// </summary>
        /// <param name="round1">The first round of dictionaries to use for conversion.</param>
        public DictRefs(List<DictWithMaxLength> round1)
        {
            Round1 = round1;
        }

        private List<DictWithMaxLength> Round1 { get; }
        private List<DictWithMaxLength> Round2 { get; set; }
        private List<DictWithMaxLength> Round3 { get; set; }

        /// <summary>
        /// Sets the second round of dictionaries for conversion.
        /// </summary>
        /// <param name="round2">The second round of dictionaries.</param>
        /// <returns>The current <see cref="DictRefs"/> instance for chaining.</returns>
        public DictRefs WithRound2(List<DictWithMaxLength> round2)
        {
            Round2 = round2;
            return this;
        }

        /// <summary>
        /// Sets the third round of dictionaries for conversion.
        /// </summary>
        /// <param name="round3">The third round of dictionaries.</param>
        /// <returns>The current <see cref="DictRefs"/> instance for chaining.</returns>
        public DictRefs WithRound3(List<DictWithMaxLength> round3)
        {
            Round3 = round3;
            return this;
        }

        /// <summary>
        /// Applies the segment replacement function for each round of dictionaries.
        /// </summary>
        /// <param name="inputText">The input text to convert.</param>
        /// <param name="segmentReplace">The segment replacement function.</param>
        /// <returns>The converted text after all rounds.</returns>
        public string ApplySegmentReplace(string inputText,
            Func<string, List<DictWithMaxLength>, string> segmentReplace)
        {
            var output = segmentReplace(inputText, Round1);
            if (Round2 != null) output = segmentReplace(output, Round2);
            if (Round3 != null) output = segmentReplace(output, Round3);
            return output;
        }
    }

    /// <summary>
    /// Main class for OpenCC text conversion. Provides methods for various conversion directions
    /// (Simplified-Traditional, Traditional-Simplified, etc.) and supports multi-stage, high-performance conversion.
    /// </summary>
    public class Opencc
    {
        // Delimiter characters used for segmenting input text.
        private static readonly char[] DelimiterArray =
            " \t\n\r!\"#$%&'()*+,-./:;<=>?@[\\]^_{}|~＝、。﹁﹂—－（）《》〈〉？！…／＼︒︑︔︓︿﹀︹︺︙︐［﹇］﹈︕︖︰︳︴︽︾︵︶｛︷｝︸﹃﹄【︻】︼　～．，；："
                .ToCharArray();

        private static readonly HashSet<char> Delimiters = new HashSet<char>(DelimiterArray);

        // Regex for stripping non-Chinese and non-symbol characters.
        private static readonly Regex StripRegex = new Regex(
            @"[!-/:-@\[-`{-~\t\n\v\f\r 0-9A-Za-z_]",
            RegexOptions.Compiled);

        // Supported configuration names for conversion directions.
        private static readonly HashSet<string> ConfigList = new HashSet<string>(StringComparer.Ordinal)
        {
            "s2t", "t2s", "s2tw", "tw2s", "s2twp", "tw2sp", "s2hk", "hk2s", "t2tw", "tw2t", "t2twp", "tw2tp",
            "t2hk", "hk2t", "t2jp", "jp2t"
        };

        private string _config;
        private string _lastError;

        // Cached lists of dictionaries for each conversion round.
        private static readonly List<DictWithMaxLength> RoundStPunct;
        private static readonly List<DictWithMaxLength> RoundSt;
        private static readonly List<DictWithMaxLength> RoundTsPunct;
        private static readonly List<DictWithMaxLength> RoundTs;

        // Thread-local StringBuilder for efficient string concatenation.
        private static readonly ThreadLocal<StringBuilder> StringBuilderCache =
            new ThreadLocal<StringBuilder>(() => new StringBuilder(1024));

        static Opencc()
        {
            Dictionary = DictionaryLib.New();

            RoundStPunct = new List<DictWithMaxLength>
            {
                Dictionary.st_phrases,
                Dictionary.st_characters,
                Dictionary.st_punctuations
            };
            RoundSt = new List<DictWithMaxLength>
            {
                Dictionary.st_phrases,
                Dictionary.st_characters
            };
            RoundTsPunct = new List<DictWithMaxLength>
            {
                Dictionary.ts_phrases,
                Dictionary.ts_characters,
                Dictionary.ts_punctuations
            };
            RoundTs = new List<DictWithMaxLength>
            {
                Dictionary.ts_phrases,
                Dictionary.ts_characters
            };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Opencc"/> class with the specified configuration.
        /// </summary>
        /// <param name="config">The conversion configuration (e.g., "s2t", "t2s").</param>
        public Opencc(string config = null)
        {
            Config = config; // Calls the setter with validation logic
        }

        /// <summary>
        /// Gets the loaded dictionary set for all conversion types.
        /// </summary>
        private static DictionaryMaxlength Dictionary { get; set; }

        /// <summary>
        /// Gets or sets the current conversion configuration.
        /// </summary>
        public string Config
        {
            get => _config;
            set
            {
                var lower = value?.ToLowerInvariant();
                if (ConfigList.Contains(lower))
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
        /// <returns>The converted text.</returns>
        private static string SegmentReplace(string text, List<DictWithMaxLength> dictionaries)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var maxWordLength = dictionaries.Count == 0 ? 1 : dictionaries.Max(d => d.MaxLength);

            // Use span-based splitting for better performance
            var splitRanges = GetSplitRangesSpan(text.AsSpan());

            if (splitRanges.Count == 0)
            {
                if (!string.IsNullOrEmpty(text))
                    splitRanges.Add((0, text.Length));
                else
                    return string.Empty;
            }

            // Optimize for small number of segments
            if (splitRanges.Count == 1)
            {
                return ConvertBy(text, dictionaries, maxWordLength);
            }

            var results = new string[splitRanges.Count];

            // Use parallel processing only for larger workloads
            if (splitRanges.Count > 4 && text.Length > 1000)
            {
                Parallel.For(0, splitRanges.Count, i =>
                {
                    var (start, end) = splitRanges[i];
                    var segment = text.Substring(start, end - start);
                    results[i] = ConvertBy(segment, dictionaries, maxWordLength);
                });
            }
            else
            {
                // Sequential processing for smaller workloads
                for (int i = 0; i < splitRanges.Count; i++)
                {
                    var (start, end) = splitRanges[i];
                    var segment = text.Substring(start, end - start);
                    results[i] = ConvertBy(segment, dictionaries, maxWordLength);
                }
            }

            return string.Concat(results);
        }

        /// <summary>
        /// Converts a string using the provided dictionaries, matching the longest possible key at each position.
        /// </summary>
        /// <param name="text">The input text segment.</param>
        /// <param name="dictionaries">The dictionaries to use for lookup.</param>
        /// <param name="maxWordLength">The maximum key length to consider.</param>
        /// <returns>The converted string segment.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string ConvertBy(string text, List<DictWithMaxLength> dictionaries, int maxWordLength)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Quick check for single delimiter
            if (text.Length == 1 && Delimiters.Contains(text[0]))
                return text;

            // Use thread-local StringBuilder
            var resultBuilder = StringBuilderCache.Value;
            resultBuilder.Clear();
            resultBuilder.EnsureCapacity(text.Length * 2);

            var span = text.AsSpan();
            var textLen = span.Length;
            var i = 0;

            // Use ArrayPool for better memory management
            var keyBuffer = ArrayPool<char>.Shared.Rent(maxWordLength);

            try
            {
                while (i < textLen)
                {
                    var remaining = span.Slice(i);
                    var tryMaxLen = Math.Min(maxWordLength, remaining.Length);

                    string bestMatch = null;
                    int bestMatchLength = 0;

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

                            if (dictObj.Data.TryGetValue(key, out var match))
                            {
                                bestMatch = match;
                                bestMatchLength = length;
                                goto FoundMatch; // Break out of both loops
                            }
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
                        resultBuilder.Append(span[i]);
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
            if (string.IsNullOrEmpty(inputText))
            {
                _lastError = "Input text is empty";
                return string.Empty;
            }

            var round1List = punctuation ? RoundStPunct : RoundSt;
            var refs = new DictRefs(round1List);
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
            if (string.IsNullOrEmpty(inputText))
            {
                _lastError = "Input text is empty";
                return string.Empty;
            }

            var round1List = punctuation ? RoundTsPunct : RoundTs;
            var refs = new DictRefs(round1List);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Simplified Chinese to Traditional Chinese (Taiwan standard).
        /// </summary>
        public string S2Tw(string inputText, bool punctuation = false)
        {
            var round1List = punctuation
                ? RoundStPunct
                : RoundSt;

            var refs = new DictRefs(round1List)
                .WithRound2(new List<DictWithMaxLength>
                {
                    Dictionary.tw_variants
                });
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

        /// <summary>
        /// Converts Traditional Chinese (Taiwan standard) to Simplified Chinese.
        /// </summary>
        public string Tw2S(string inputText, bool punctuation = false)
        {
            var round2List = punctuation
                ? RoundTsPunct
                : RoundTs;

            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.tw_variants_rev_phrases,
                Dictionary.tw_variants_rev
            }).WithRound2(round2List);
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

        /// <summary>
        /// Converts Simplified Chinese to Traditional Chinese (Taiwan standard, with phrase and variant rounds).
        /// </summary>
        public string S2Twp(string inputText, bool punctuation = false)
        {
            var round1List = punctuation
                ? RoundStPunct
                : RoundSt;

            var refs = new DictRefs(round1List)
                .WithRound2(new List<DictWithMaxLength>
                {
                    Dictionary.tw_phrases
                }).WithRound3(new List<DictWithMaxLength>
                {
                    Dictionary.tw_variants
                });
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

        /// <summary>
        /// Converts Traditional Chinese (Taiwan) to Simplified Chinese (with phrase and variant rounds).
        /// </summary>
        public string Tw2Sp(string inputText, bool punctuation = false)
        {
            var round2List = punctuation
                ? RoundTsPunct
                : RoundTs;

            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.tw_phrases_rev,
                Dictionary.tw_variants_rev_phrases,
                Dictionary.tw_variants_rev
            }).WithRound2(round2List);
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

        /// <summary>
        /// Converts Simplified Chinese to Hong Kong Traditional Chinese.
        /// </summary>
        public string S2Hk(string inputText, bool punctuation = false)
        {
            var round1List = punctuation
                ? RoundStPunct
                : RoundSt;

            var refs = new DictRefs(round1List).WithRound2(new List<DictWithMaxLength>
            {
                Dictionary.hk_variants
            });
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

        /// <summary>
        /// Converts Hong Kong Traditional Chinese to Simplified Chinese.
        /// </summary>
        public string Hk2S(string inputText, bool punctuation = false)
        {
            var round2List = punctuation
                ? RoundTsPunct
                : RoundTs;

            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.hk_variants_rev_phrases,
                Dictionary.hk_variants_rev
            }).WithRound2(round2List);
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
            return ConvertBy(inputText, dictRefs, 1);
        }

        /// <summary>
        /// Converts Traditional Chinese characters to Simplified Chinese (single character only).
        /// </summary>
        public static string Ts(string inputText)
        {
            var dictRefs = new List<DictWithMaxLength> { Dictionary.ts_characters };
            return ConvertBy(inputText, dictRefs, 1);
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
            var maxChars = FindMaxUtf8Length(stripped, 200);
            var stripText = maxChars < stripped.Length ? stripped.Substring(0, maxChars) : stripped;

            var tsConverted = Ts(stripText);
            if (stripText != tsConverted) return 1;

            var stConverted = St(stripText);
            return stripText != stConverted ? 2 : 0;
        }

        /// <summary>
        /// Finds the maximum substring length that fits within the specified UTF-8 byte count.
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
        /// <returns>A list of (start, end) index tuples for each segment.</returns>
        private static List<(int start, int end)> GetSplitRangesSpan(ReadOnlySpan<char> input)
        {
            if (input.IsEmpty)
                return new List<(int, int)>();

            var ranges = new List<(int, int)>();
            var currentStart = 0;
            var length = input.Length;

            for (var i = 0; i < length; i++)
            {
                if (!Delimiters.Contains(input[i])) continue;

                var chunkLength = i - currentStart + 1;
                if (chunkLength > 0)
                {
                    ranges.Add((currentStart, currentStart + chunkLength));
                }

                currentStart = i + 1;
            }

            if (currentStart < length)
            {
                ranges.Add((currentStart, length));
            }

            return ranges;
        }
    }
}