using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DictionaryLib;

namespace OpenccNet
{
    public class DictRefs
    {
        private List<DictWithMaxLength> Round1 { get; }
        private List<DictWithMaxLength> Round2 { get; set; }
        private List<DictWithMaxLength> Round3 { get; set; }

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
                _lastError = $"Error initializing dictionary: {e.Message}";
            }
        }

        public string GetLastError()
        {
            return _lastError;
        }

        private static string SegmentReplace(string text, List<DictWithMaxLength> dictionaries)
        {
            int maxWordLength = dictionaries.Count == 0
                ? 1
                : dictionaries.Max(d => d.MaxLength);

            List<string> splitChunks = SplitStringInclusivePar(text); // Pass delimiters

            var results = new string[splitChunks.Count];
            Parallel.ForEach(Enumerable.Range(0, splitChunks.Count),
                i => { results[i] = ConvertBy(splitChunks[i], dictionaries, maxWordLength); });

            return string.Concat(results);
        }

        private static string ConvertBy(string text, List<DictWithMaxLength> dictionaries, int maxWordLength)
        {
            int textLen = text.Length;
            if (textLen == 0)
                return "";

            if (textLen == 1 && Delimiters.Contains(text[0]))
                return text;

            var resultBuilder = new StringBuilder(textLen * 2);
            int i = 0;

            char[] buffer = new char[maxWordLength]; // reuse buffer

            while (i < textLen)
            {
                string bestMatch = null;
                int bestLength = 0;

                int tryMaxLen = Math.Min(maxWordLength, textLen - i);
                for (int length = tryMaxLen; length > 0; --length)
                {
                    if (i + length <= textLen) // Ensure we don't go out of bounds
                    {
                        text.CopyTo(i, buffer, 0, length);
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
                    }

                    if (bestLength > 0)
                        break;
                }

                if (bestLength == 0)
                {
                    bestMatch = text[i].ToString();
                    bestLength = 1;
                }

                resultBuilder.Append(bestMatch);
                i += bestLength;
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
            // var chars = inputText.ToList();
            // return ConvertBy(chars, dictRefs, 1);
            return ConvertBy(inputText, dictRefs, 1);
        }

        public string Ts(string inputText)
        {
            var dictRefs = new List<DictWithMaxLength> { Dictionary.ts_characters };
            // var chars = inputText.ToList();
            // return ConvertBy(chars, dictRefs, 1);
            return ConvertBy(inputText, dictRefs, 1);
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
            byte[] encoded = Encoding.UTF8.GetBytes(s);
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
            string partialString = Encoding.UTF8.GetString(encoded, 0, byteCount);
            return partialString.Length;
        }

        private static List<string> SplitStringInclusivePar(string input, int minChunkSize = 4096)
        {
            if (string.IsNullOrEmpty(input))
                return new List<string>();

            int length = input.Length;
            var partitionResults = new ConcurrentBag<List<KeyValuePair<int, string>>>();

            Parallel.ForEach(Partitioner.Create(0, length, minChunkSize), range =>
            {
                int start = range.Item1;
                int end = range.Item2;
                var localResults = new List<KeyValuePair<int, string>>();
                int currentStart = start;

                for (int i = start; i < end; i++)
                {
                    if (Delimiters.Contains(input[i]))
                    {
                        int chunkLength = i - currentStart + 1;
                        if (chunkLength > 0)
                        {
                            localResults.Add(new KeyValuePair<int, string>(
                                currentStart, input.Substring(currentStart, chunkLength)));
                        }

                        currentStart = i + 1;
                    }
                }

                if (currentStart < end)
                {
                    localResults.Add(new KeyValuePair<int, string>(
                        currentStart, input.Substring(currentStart, end - currentStart)));
                }

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