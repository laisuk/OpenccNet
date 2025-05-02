using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DictionaryLib;

namespace OpenccNet
{
    public class DictRefs
    {
        public List<DictWithMaxLength> Round1 { get; }
        public List<DictWithMaxLength> Round2 { get; private set; }
        public List<DictWithMaxLength> Round3 { get; private set; }

        public DictRefs(List<DictWithMaxLength> round1)
        {
            Round1 = round1;
        }

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
            string output = segmentReplace(inputText, Round1);
            if (Round2 != null)
            {
                output = segmentReplace(output, Round2);
            }

            if (Round3 != null)
            {
                output = segmentReplace(output, Round3);
            }

            return output;
        }
    }

    public class Opencc
    {
        private static readonly HashSet<char> Delimiters = new HashSet<char>(
            " \t\n\r!\"#$%&'()*+,-./:;<=>?@[\\]^_{}|~＝、。“”‘’『』「」﹁﹂—－（）《》〈〉？！…／＼︒︑︔︓︿﹀︹︺︙︐［﹇］﹈︕︖︰︳︴︽︾︵︶｛︷｝︸﹃﹄【︻】︼　～．，；："
                .ToCharArray());

        private static readonly Regex StripRegex = new Regex("[!-/:-@\\[-`{-~\\t\\n\\v\\f\\r 0-9A-Za-z_]");

        private readonly List<string> _configList = new List<string>
        {
            "s2t", "t2s", "s2tw", "tw2s", "s2twp", "tw2sp", "s2hk", "hk2s", "t2tw", "tw2t", "t2twp", "tw2tp",
            "t2hk", "hk2t", "t2jp", "jp2t"
        };

        private string _config;
        private string _lastError;

        public string Config
        {
            get => _config;
            set
            {
                string lower = value?.ToLowerInvariant();
                if (_configList.Contains(lower))
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

        private DictionaryMaxlength Dictionary { get; }

        public Opencc(string config = null)
        {
            Config = config; // Calls the setter with validation logic

            try
            {
                Dictionary = DictionaryMaxlength.New();
                // Dictionary = DictionaryMaxlength.FromJson();
                // Dictionary = DictionaryMaxlength.FromCbor();
                // Dictionary = DictionaryMaxlength.FromZstd();
                // Dictionary = DictionaryMaxlength.FromDicts();
            }
            catch (Exception e)
            {
                _lastError = _lastError == null
                    ? $"Error initializing dictionary: {e.Message}"
                    : $"{_lastError} Error initializing dictionary: {e.Message}";

            }
        }

        public string GetLastError()
        {
            return _lastError;
        }

        private string SegmentReplace(string text, List<DictWithMaxLength> dictionaries)
        {
            int maxWordLength = dictionaries.Count == 0
                ? 1
                : dictionaries.Max(d => d.MaxLength);

            List<List<char>> splitChunks = SplitStringInclusivePar(text);

            // var results = splitChunks
            //     .AsParallel()
            //     .AsOrdered() // Ensures chunks are reassembled in original order
            //     .Select(chunk => ConvertBy(chunk, dictionaries, maxWordLength));
            var results = new string[splitChunks.Count];
            Parallel.ForEach(Enumerable.Range(0, splitChunks.Count),
                i => { results[i] = ConvertBy(splitChunks[i], dictionaries, maxWordLength); });

            return string.Concat(results);
        }

        private string ConvertBy(List<char> textChars, List<DictWithMaxLength> dictionaries, int maxWordLength)
        {
            if (textChars == null || textChars.Count == 0)
                return "";

            if (textChars.Count == 1 && Delimiters.Contains(textChars[0]))
                return textChars[0].ToString();

            var result = new List<string>();
            int i = 0;
            int textCharsLen = textChars.Count;
            char[] buffer = new char[maxWordLength]; // reuse buffer

            while (i < textCharsLen)
            {
                string bestMatch = null;
                int bestLength = 0;

                int tryMaxLen = Math.Min(maxWordLength, textCharsLen - i);
                for (int length = tryMaxLen; length > 0; --length)
                {
                    // Copy directly to buffer
                    textChars.CopyTo(i, buffer, 0, length);
                    string word = new string(buffer, 0, length);

                    foreach (var dictObj in dictionaries)
                    {
                        if (dictObj.Data.TryGetValue(word, out string match))
                        {
                            bestMatch = match;
                            bestLength = length;
                            break;
                        }
                    }

                    if (bestLength > 0)
                        break;
                }

                if (bestLength == 0)
                {
                    bestMatch = textChars[i].ToString();
                    bestLength = 1;
                }

                result.Add(bestMatch);
                i += bestLength;
            }

            return string.Concat(result);
        }

        public string S2T(string inputText, bool punctuation = false)
        {
            if (string.IsNullOrEmpty(inputText))
            {
                _lastError = "Input text is empty";
                return "";
            }

            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.st_phrases,
                Dictionary.st_characters
            });
            string output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return punctuation ? ConvertPunctuation(output, "s") : output;
        }

        public string T2S(string inputText, bool punctuation = false)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.ts_phrases,
                Dictionary.ts_characters
            });
            string output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return punctuation ? ConvertPunctuation(output, "t") : output;
        }

        public string S2Tw(string inputText, bool punctuation = false)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.st_phrases,
                Dictionary.st_characters
            }).WithRound2(new List<DictWithMaxLength>
            {
                Dictionary.tw_variants
            });
            string output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return punctuation ? ConvertPunctuation(output, "s") : output;
        }

        public string Tw2S(string inputText, bool punctuation = false)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.tw_variants_rev_phrases,
                Dictionary.tw_variants_rev
            }).WithRound2(new List<DictWithMaxLength>
            {
                Dictionary.ts_phrases,
                Dictionary.ts_characters
            });
            string output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return punctuation ? ConvertPunctuation(output, "t") : output;
        }

        public string S2Twp(string inputText, bool punctuation = false)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.st_phrases,
                Dictionary.st_characters
            }).WithRound2(new List<DictWithMaxLength>
            {
                Dictionary.tw_phrases
            }).WithRound3(new List<DictWithMaxLength>
            {
                Dictionary.tw_variants
            });
            string output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return punctuation ? ConvertPunctuation(output, "s") : output;
        }

        public string Tw2Sp(string inputText, bool punctuation = false)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.tw_phrases_rev,
                Dictionary.tw_variants_rev_phrases,
                Dictionary.tw_variants_rev
            }).WithRound2(new List<DictWithMaxLength>
            {
                Dictionary.ts_phrases,
                Dictionary.ts_characters
            });
            string output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return punctuation ? ConvertPunctuation(output, "t") : output;
        }

        public string S2Hk(string inputText, bool punctuation = false)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.st_phrases,
                Dictionary.st_characters
            }).WithRound2(new List<DictWithMaxLength>
            {
                Dictionary.hk_variants
            });
            string output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return punctuation ? ConvertPunctuation(output, "s") : output;
        }

        public string Hk2S(string inputText, bool punctuation = false)
        {
            var refs = new DictRefs(new List<DictWithMaxLength>
            {
                Dictionary.hk_variants_rev_phrases,
                Dictionary.hk_variants_rev
            }).WithRound2(new List<DictWithMaxLength>
            {
                Dictionary.ts_phrases,
                Dictionary.ts_characters
            });
            string output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return punctuation ? ConvertPunctuation(output, "t") : output;
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
                        _lastError = $"Invalid conversion config requested: {Config}";
                        return inputText; // Return the original input
                }
            }
            catch (Exception e)
            {
                _lastError = $"Conversion failed: {e.Message}";
                return _lastError;
            }
        }

        public string St(string inputText)
        {
            var dictRefs = new List<DictWithMaxLength> { Dictionary.st_characters };
            var chars = inputText.ToList();
            return ConvertBy(chars, dictRefs, 1);
        }

        public string Ts(string inputText)
        {
            var dictRefs = new List<DictWithMaxLength> { Dictionary.ts_characters };
            var chars = inputText.ToList();
            return ConvertBy(chars, dictRefs, 1);
        }

        public int ZhoCheck(string inputText)
        {
            if (string.IsNullOrEmpty(inputText))
            {
                return 0;
            }

            string stripped = StripRegex.Replace(inputText, "");
            int maxChars = FindMaxUtf8Length(stripped, 200);
            string stripText = stripped.Substring(0, maxChars);

            if (stripText != Ts(stripText))
            {
                return 1;
            }
            else if (stripText != St(stripText))
            {
                return 2;
            }
            else
            {
                return 0;
            }
        }

        private static string ConvertPunctuation(string inputText, string config)
        {
            Dictionary<char, char> s2T = new Dictionary<char, char>
            {
                { '“', '「' },
                { '”', '」' },
                { '‘', '『' },
                { '’', '』' }
            };

            if (config.StartsWith("s"))
            {
                string pattern = "[" + string.Concat(s2T.Keys.Select(c => Regex.Escape(c.ToString()))) + "]";
                return Regex.Replace(inputText, pattern, m => s2T[m.Value[0]].ToString());
            }
            else
            {
                Dictionary<char, char> t2S = s2T.ToDictionary(k => k.Value, v => v.Key);
                string pattern = "[" + string.Concat(t2S.Keys.Select(c => Regex.Escape(c.ToString()))) + "]";
                return Regex.Replace(inputText, pattern, m => t2S[m.Value[0]].ToString());
            }
        }

        private static int FindMaxUtf8Length(string s, int maxByteCount)
        {
            byte[] encoded = System.Text.Encoding.UTF8.GetBytes(s);
            if (encoded.Length <= maxByteCount)
            {
                return s.Length;
            }

            int byteCount = maxByteCount;
            while (byteCount > 0 && (encoded[byteCount - 1] & 0b11000000) == 0b10000000)
            {
                byteCount--;
            }

            // Adjust for potential partial character at the end
            string partialString = System.Text.Encoding.UTF8.GetString(encoded, 0, byteCount);
            return partialString.Length;
        }

        public static List<List<char>> SplitStringInclusivePar(string input, int chunkCount = 4)
        {
            if (string.IsNullOrEmpty(input))
                return new List<List<char>>();

            int length = input.Length;

            // Step 1: Find aligned split points (after delimiters)
            var splitPoints = new List<int> { 0 };

            for (int i = 0; i < length; i++)
            {
                if (Delimiters.Contains(input[i]))
                    splitPoints.Add(i + 1); // +1 to include delimiter
            }

            if (splitPoints[splitPoints.Count - 1] < length)
                splitPoints.Add(length); // Final chunk end

            // Step 2: Create ranges (start, end) from splitPoints
            var ranges = new List<(int Start, int End)>();
            for (int i = 0; i < splitPoints.Count - 1; i++)
            {
                int start = splitPoints[i];
                int end = splitPoints[i + 1];
                ranges.Add((start, end));
            }

            // Step 3: Group ranges into balanced chunks
            var chunkedRanges = new List<List<(int, int)>>();
            int groupSize = (int)Math.Ceiling((double)ranges.Count / chunkCount);
            for (int i = 0; i < ranges.Count; i += groupSize)
            {
                chunkedRanges.Add(ranges.Skip(i).Take(groupSize).ToList());
            }

            // Step 4: Parallel processing of grouped ranges
            var chunkResults = chunkedRanges
                .AsParallel()
                .Select(rangeGroup =>
                {
                    var result = new List<List<char>>();
                    foreach (var (start, end) in rangeGroup)
                    {
                        if (end > start)
                        {
                            result.Add(input.Substring(start, end - start).ToList());
                        }
                    }

                    return result;
                }).ToList();

            // Step 5: Merge all results
            return chunkResults.SelectMany(r => r).ToList();
        }
    }
}