using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OpenccNet
{
    public class DictRefs
    {
        public DictRefs(List<DictWithMaxLength> round1)
        {
            Round1 = round1;
        }

        private List<DictWithMaxLength> Round1 { get; }
        private List<DictWithMaxLength> Round2 { get; set; }
        private List<DictWithMaxLength> Round3 { get; set; }

        public DictRefs WithRound2(List<DictWithMaxLength> round2)
        {
            Round2 = round2;
            return this;
        }

        public DictRefs WithRound3(List<DictWithMaxLength> round3)
        {
            Round3 = round3;
            return this;
        }

        public string ApplySegmentReplace(string inputText,
            Func<string, List<DictWithMaxLength>, string> segmentReplace)
        {
            var output = segmentReplace(inputText, Round1);
            if (Round2 != null) output = segmentReplace(output, Round2);

            if (Round3 != null) output = segmentReplace(output, Round3);

            return output;
        }
    }

    public class Opencc
    {
        private static readonly HashSet<char> Delimiters = new HashSet<char>(
            // " \t\n\r!\"#$%&'()*+,-./:;<=>?@[\\]^_{}|~＝、。“”‘’『』「」﹁﹂—－（）《》〈〉？！…／＼︒︑︔︓︿﹀︹︺︙︐［﹇］﹈︕︖︰︳︴︽︾︵︶｛︷｝︸﹃﹄【︻】︼　～．，；："
            " \t\n\r!\"#$%&'()*+,-./:;<=>?@[\\]^_{}|~＝、。﹁﹂—－（）《》〈〉？！…／＼︒︑︔︓︿﹀︹︺︙︐［﹇］﹈︕︖︰︳︴︽︾︵︶｛︷｝︸﹃﹄【︻】︼　～．，；："
                .ToCharArray());

        private static readonly Regex StripRegex = new Regex(@"[!-/:-@\[-`{-~\t\n\v\f\r 0-9A-Za-z_]");

        private static readonly HashSet<string> ConfigList = new HashSet<string>(StringComparer.Ordinal)
        {
            "s2t", "t2s", "s2tw", "tw2s", "s2twp", "tw2sp", "s2hk", "hk2s", "t2tw", "tw2t", "t2twp", "tw2tp",
            "t2hk", "hk2t", "t2jp", "jp2t"
        };

        private string _config;
        private string _lastError; // Change _lastError to static

        private static readonly List<DictWithMaxLength> RoundStPunct;
        private static readonly List<DictWithMaxLength> RoundSt;
        private static readonly List<DictWithMaxLength> RoundTsPunct;
        private static readonly List<DictWithMaxLength> RoundTs;

        static Opencc()
        {            
            Dictionary = DictionaryLib.New();
            // Dictionary = DictionaryLib.FromDicts();
            
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

        public Opencc(string config = null)
        {
            Config = config; // Calls the setter with validation logic           
        }
        
        private static DictionaryMaxlength Dictionary { get; set; }

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

        public string GetLastError()
        {
            return _lastError;
        }

        private static string SegmentReplace(string text, List<DictWithMaxLength> dictionaries)
        {
            var maxWordLength = dictionaries.Count == 0
                ? 1
                : dictionaries.Max(d => d.MaxLength);

            var splitChunks = SplitStringInclusivePar(text); // Pass delimiters

            var results = new string[splitChunks.Count];
            Parallel.ForEach(Enumerable.Range(0, splitChunks.Count),
                i => { results[i] = ConvertBy(splitChunks[i], dictionaries, maxWordLength); });

            return string.Concat(results);
        }

        private static string ConvertBy(string text, List<DictWithMaxLength> dictionaries, int maxWordLength)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            if (text.Length == 1 && Delimiters.Contains(text[0]))
                return text[0].ToString();

            var resultBuilder = new StringBuilder(text.Length * 2);
            var span = text.AsSpan();
            var textLen = span.Length;
            var i = 0;

            var buffer = maxWordLength <= 128
                ? stackalloc char[maxWordLength]
                : new char[maxWordLength]; // fallback if too big for stackalloc

            // Use ArrayPool for reusable buffer
            var pool = ArrayPool<char>.Shared;
            var keyBuffer = pool.Rent(maxWordLength);

            try
            {
                while (i < textLen)
                {
                    var remaining = span.Slice(i);
                    var tryMaxLen = Math.Min(maxWordLength, remaining.Length);

                    ReadOnlySpan<char> bestMatchSpan = default;
                    string bestMatch = null;

                    for (var length = tryMaxLen; length > 0; --length)
                    {
                        var wordSpan = remaining.Slice(0, length);
                        wordSpan.CopyTo(buffer);

                        foreach (var dictObj in dictionaries)
                        {
                            if (dictObj.MaxLength < length) continue;

                            buffer.Slice(0, length).CopyTo(keyBuffer);
                            var key = new string(keyBuffer, 0, length);

                            if (dictObj.Data.TryGetValue(key, out var match))
                            {
                                bestMatch = match;
                                bestMatchSpan = wordSpan;
                                break;
                            }
                        }

                        if (bestMatch != null)
                            break;
                    }

                    if (bestMatch != null)
                    {
                        resultBuilder.Append(bestMatch);
                        i += bestMatchSpan.Length;
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
                // Always return rented array
                pool.Return(keyBuffer);
            }

            return resultBuilder.ToString();
        }

        public string S2T(string inputText, bool punctuation = false)
        {
            if (string.IsNullOrEmpty(inputText))
            {
                _lastError = "Input text is empty";
                return "";
            }

            var round1List = punctuation
                ? RoundStPunct
                : RoundSt;

            var refs = new DictRefs(round1List);
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

        public string T2S(string inputText, bool punctuation = false)
        {
            if (string.IsNullOrEmpty(inputText))
            {
                _lastError = "Input text is empty";
                return "";
            }
            
            var round1List = punctuation
                ? RoundTsPunct
                : RoundTs;

            var refs = new DictRefs(round1List);
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

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

        public string T2Tw(string inputText)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.tw_variants
            });
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

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

        public string Tw2T(string inputText)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.tw_variants_rev_phrases,
                Dictionary.tw_variants_rev
            });
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

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

        public string T2Hk(string inputText)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.hk_variants
            });
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        public string Hk2T(string inputText)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.hk_variants_rev_phrases,
                Dictionary.hk_variants_rev
            });
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        public string T2Jp(string inputText)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.jp_variants
            });
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

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

        public static string St(string inputText)
        {
            var dictRefs = new List<DictWithMaxLength> { Dictionary.st_characters };
            return ConvertBy(inputText, dictRefs, 1);
        }

        public static string Ts(string inputText)
        {
            var dictRefs = new List<DictWithMaxLength> { Dictionary.ts_characters };
            return ConvertBy(inputText, dictRefs, 1);
        }

        public static int ZhoCheck(string inputText)
        {
            if (string.IsNullOrEmpty(inputText)) return 0;

            var stripped = StripRegex.Replace(inputText, "");
            var maxChars = FindMaxUtf8Length(stripped, 200);
            var stripText = stripped.Substring(0, maxChars);

            if (stripText != Ts(stripText)) return 1;

            return stripText != St(stripText) ? 2 : 0;
        }

        // private static string ConvertPunctuation(string inputText, string config)
        // {
        //     if (string.IsNullOrEmpty(inputText))
        //         return inputText;
        //
        //     // Static mappings
        //     var s2 = "“”‘’".AsSpan(); // Source: Simplified punctuation
        //     var t2 = "「」『』".AsSpan(); // Target: Traditional punctuation
        //
        //     ReadOnlySpan<char> from, to;
        //     if (config.StartsWith("s"))
        //     {
        //         from = s2;
        //         to = t2;
        //     }
        //     else
        //     {
        //         from = t2;
        //         to = s2;
        //     }
        //
        //     var result = new StringBuilder(inputText.Length);
        //     foreach (var c in inputText)
        //     {
        //         var idx = from.IndexOf(c);
        //         if (idx >= 0)
        //             result.Append(to[idx]);
        //         else
        //             result.Append(c);
        //     }
        //
        //     return result.ToString();
        // }

        private static int FindMaxUtf8Length(string s, int maxByteCount)
        {
            var encoded = Encoding.UTF8.GetBytes(s);
            if (encoded.Length <= maxByteCount) return s.Length;

            var byteCount = maxByteCount;
            while (byteCount > 0 && (encoded[byteCount - 1] & 0b11000000) == 0b10000000) byteCount--;

            // Adjust for potential partial character at the end
            var partialString = Encoding.UTF8.GetString(encoded, 0, byteCount);
            return partialString.Length;
        }

        private static List<string> SplitStringInclusivePar(string input, int minChunkSize = 4096)
        {
            if (string.IsNullOrEmpty(input))
                return new List<string>();

            var length = input.Length;
            var partitionResults = new ConcurrentBag<List<KeyValuePair<int, string>>>();

            Parallel.ForEach(Partitioner.Create(0, length, minChunkSize), range =>
            {
                var start = range.Item1;
                var end = range.Item2;
                var localResults = new List<KeyValuePair<int, string>>();
                var currentStart = start;

                for (var i = start; i < end; i++)
                    if (Delimiters.Contains(input[i]))
                    {
                        var chunkLength = i - currentStart + 1;
                        if (chunkLength > 0)
                            localResults.Add(new KeyValuePair<int, string>(
                                currentStart, input.Substring(currentStart, chunkLength)));

                        currentStart = i + 1;
                    }

                if (currentStart < end)
                    localResults.Add(new KeyValuePair<int, string>(
                        currentStart, input.Substring(currentStart, end - currentStart)));

                partitionResults.Add(localResults);
            });

            // Sort and flatten in one go
            return partitionResults
                .SelectMany(x => x)
                .OrderBy(pair => pair.Key)
                .Select(pair => pair.Value)
                .ToList();
        }
    }
}