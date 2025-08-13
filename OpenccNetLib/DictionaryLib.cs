using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using PeterO.Cbor;
using ZstdSharp;

namespace OpenccNetLib
{
    /// <summary>
    /// Represents a dictionary with string keys and values, and tracks the maximum key length.
    /// Used for efficient word/phrase lookup in OpenCC conversion.
    /// </summary>
    public class DictWithMaxLength
    {
        /// <summary>
        /// The mapping of keys to values for conversion.
        /// </summary>
        public Dictionary<string, string> Dict { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// The maximum length of any key in the dictionary.
        /// Used for optimizing longest-match lookups.
        /// </summary>
        public int MaxLength { get; set; }

        /// <summary>
        /// Persisted starter-cap table: first Unicode text element (grapheme) → maximum key length in UTF-16 code units.
        /// </summary>
        /// <remarks>
        /// - Sparse and compact for JSON/CBOR/Zstd. Key is the first text element (e.g., "𠮷", "Á", "👨‍👩‍👧‍👦").
        /// - Value is capped to <see cref="byte"/> (1–255).
        /// - On <c>BuildStartCharIndexes()</c>, this map is used to hydrate runtime arrays. If empty but
        ///   <see cref="Dict"/> is populated, it is derived once from <see cref="Dict"/> using text-element boundaries.
        /// - Runtime matching remains in UTF-16 units; this map exists to serialize a stable, Unicode-friendly cap.
        /// </remarks>
        private Dictionary<string, byte> StarterCapTextElem { get; } = new Dictionary<string, byte>(512);

        /// <summary>
        /// Runtime-only per-starter cap (UTF-16 units). Index by the first UTF-16 code unit; 0 means “no entries”.
        /// </summary>
        /// <remarks>
        /// - Hydrated by <c>BuildStartCharIndexes()</c> from <see cref="StarterCapTextElem"/> (or derived from <see cref="Dict"/>).
        /// - Used in <c>ConvertBy()</c> to bound the longest-match probe: <c>tryMaxLen = min(maxWordLength, remaining.Length, cap)</c>.
        /// - Not serialized. Zero-initialized and built once; safe for O(1) lookups in hot paths.
        /// </remarks>
        [JsonIgnore]
        private ushort[] FirstCharMaxLenUtf16Arr { get; } = new ushort[char.MaxValue + 1];

        /// <summary>
        /// Runtime-only per-starter length bitmap (1..64). Bit <c>(n−1)</c> set ⇢ a key of length <c>n</c> exists for this starter.
        /// </summary>
        /// <remarks>
        /// - Hydrated by <c>BuildStartCharIndexes()</c> directly from <see cref="Dict"/> keys.
        /// - Speeds up longest-first search by skipping impossible lengths:
        ///   <c>if (n ≤ 64 && ((mask >> (n−1)) & 1) == 0) continue;</c>
        /// - Lengths &gt; 64 are not represented—do not skip those based on this mask (correctness preserved).
        /// - Not serialized. O(1) read per position; complements <see cref="FirstCharMaxLenUtf16Arr"/>.
        /// </remarks>
        [JsonIgnore]
        private ulong[] FirstCharLenMask64 { get; } = new ulong[char.MaxValue + 1];

        /// <summary>
        /// Indicates whether the runtime starter indexes have been built.
        /// Used as a guard to avoid rebuilding <c>FirstCharMaxLenUtf16Arr</c> and <c>FirstCharLenMask64</c>.
        /// </summary>
        private bool _built;

        /// <summary>
        /// Builds the runtime starter indexes for fast lookup.
        /// </summary>
        /// <remarks>
        /// Idempotent: if already built, returns immediately.
        /// If <c>StarterCapTextElem</c> is empty and <c>Dict</c> is populated, this method first derives
        /// the sparse caps (first text element → max UTF-16 length) and then hydrates:
        /// <list type="bullet">
        /// <item><description><c>FirstCharMaxLenUtf16Arr</c>: per-starter cap (UTF-16 units, O(1)).</description></item>
        /// <item><description><c>FirstCharLenMask64</c>: per-starter length bitmap for lengths 1..64 (O(1)).</description></item>
        /// </list>
        /// Designed to be called once during initialization; not intended for concurrent mutation.
        /// </remarks>
        public void BuildStartCharIndexes()
        {
            if (_built) return;

            // If no persisted caps, derive sparse caps by first text element (StringInfo)
            if (StarterCapTextElem.Count == 0 && Dict.Count != 0)
            {
                foreach (var k in Dict.Keys)
                {
                    if (string.IsNullOrEmpty(k)) continue;

                    var te0 = GetFirstTextElement(k); // e.g. "𠮷", "Á", "👨‍👩‍👧‍👦"
                    var len = k.Length; // UTF-16 units

                    if (!StarterCapTextElem.TryGetValue(te0, out var cur) || len > cur)
                        StarterCapTextElem[te0] = (byte)Math.Min(len, byte.MaxValue);
                }
            }

            // Hydrate per-starter CAP array from text-element caps
            foreach (var kv in StarterCapTextElem)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                var c0 = kv.Key[0]; // first UTF-16 unit of the grapheme
                if (kv.Value > FirstCharMaxLenUtf16Arr[c0])
                    FirstCharMaxLenUtf16Arr[c0] = kv.Value;
            }

            // Build per-starter LENGTH MASK (≤64) directly from Dict keys
            foreach (var k in Dict.Keys)
            {
                if (string.IsNullOrEmpty(k)) continue;
                var c0 = k[0];
                var len = k.Length;
                if ((uint)len <= 64u)
                    FirstCharLenMask64[c0] |= 1UL << (len - 1);
            }

            _built = true;
        }

        /// <summary>
        /// Gets the per-starter maximum key length (in UTF-16 code units) for the given starter.
        /// </summary>
        /// <param name="starter">
        /// The first UTF-16 code unit (char) of the current position in the input.
        /// </param>
        /// <returns>
        /// The maximum key length (UTF-16 units) observed for entries that start with <paramref name="starter"/>; 
        /// returns 0 if no entries start with this char.
        /// </returns>
        /// <remarks>
        /// O(1). Lazily builds the starter index on first use. The value is an upper bound used to cap
        /// the longest-match probe in <c>ConvertBy()</c>. Non-BMP single characters naturally have length 2.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort GetStarterCap(char starter)
        {
            if (!_built) BuildStartCharIndexes();
            return FirstCharMaxLenUtf16Arr[starter];
        }

        /// <summary>
        /// Gets the 64-bit length bitmap for keys that start with the given starter.
        /// </summary>
        /// <param name="starter">
        /// The first UTF-16 code unit (char) of the current position in the input.
        /// </param>
        /// <returns>
        /// A 64-bit mask where bit <c>(n-1)</c> is set if there exists a key of length <c>n</c> (1 ≤ n ≤ 64)
        /// that starts with <paramref name="starter"/>. Returns 0 if none exist.
        /// </returns>
        /// <remarks>
        /// O(1). Use this to skip impossible probe lengths during longest-first search.
        /// Lengths &gt; 64 are not represented in the mask and should not be skipped based on it.
        /// Lazily builds on first use.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong GetStarterLenMask(char starter)
        {
            if (!_built) BuildStartCharIndexes();
            return FirstCharLenMask64[starter];
        }

        /// <summary>
        /// Returns the first Unicode text element (grapheme cluster) of <paramref name="s"/>.
        /// </summary>
        /// <param name="s">The source string.</param>
        /// <returns>
        /// The first text element (which may consist of one or more UTF-16 code units, e.g., surrogate pairs or
        /// base+combining sequences). Returns <see cref="string.Empty"/> if <paramref name="s"/> is empty.
        /// </returns>
        /// <remarks>
        /// Uses <see cref="System.Globalization.StringInfo.GetTextElementEnumerator(string)"/> for culture-invariant
        /// text element boundaries. Intended for building persisted starter caps; runtime matching still operates in UTF-16 units.
        /// </remarks>
        private static string GetFirstTextElement(string s)
        {
            var e = StringInfo.GetTextElementEnumerator(s);
            return e.MoveNext() ? e.GetTextElement() : string.Empty;
        }

        /// <summary>
        /// Attempts to get the value associated with the specified key.
        /// Aggressively inlined for performance.
        /// </summary>
        /// <param name="key">The key to locate.</param>
        /// <param name="value">The value associated with the key, if found.</param>
        /// <returns>True if the key was found; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(string key, out string value)
        {
            return Dict.TryGetValue(key, out value);
        }

        /// <summary>
        /// Gets the number of entries in the dictionary.
        /// </summary>
        public int Count => Dict.Count;
    }

    /// <summary>
    /// Holds all conversion dictionaries for different OpenCC conversion types.
    /// Each property represents a specific conversion mapping.
    /// </summary>
    // ReSharper disable InconsistentNaming
    public class DictionaryMaxlength
    {
        public DictWithMaxLength st_characters { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength st_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength ts_characters { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength ts_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength tw_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength tw_phrases_rev { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength tw_variants { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength tw_variants_rev { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength tw_variants_rev_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength hk_variants { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength hk_variants_rev { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength hk_variants_rev_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength jps_characters { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength jps_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength jp_variants { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength jp_variants_rev { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength st_punctuations { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength ts_punctuations { get; set; } = new DictWithMaxLength();
    }
    // ReSharper restore InconsistentNaming

    /// <summary>
    /// Provides methods to load, save, and cache OpenCC conversion dictionaries
    /// in various formats (JSON, CBOR, Zstd-compressed).
    /// </summary>
    public static class DictionaryLib
    {
        /// <summary>
        /// Loads the dictionary from a Zstd-compressed file.
        /// Always returns a new instance.
        /// </summary>
        public static DictionaryMaxlength New()
        {
            return FromZstd();
            // return FromDicts();
        }

        /// <summary>
        /// Loads the dictionary from a Zstd-compressed JSON file.
        /// </summary>
        /// <param name="relativePath">Relative path to the Zstd file.</param>
        /// <returns>The deserialized <see cref="DictionaryMaxlength"/> instance.</returns>
        private static DictionaryMaxlength FromZstd(string relativePath = "dicts/dictionary_maxlength.zstd")
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var fullPath = Path.Combine(baseDir, relativePath);

                using (var inputStream = File.OpenRead(fullPath))
                using (var decompressionStream = new DecompressionStream(inputStream))
                {
                    return JsonSerializer.Deserialize<DictionaryMaxlength>(decompressionStream);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to load dictionary from Zstd.", ex);
            }
        }

        /// <summary>
        /// Loads the dictionary from a JSON file.
        /// </summary>
        /// <param name="relativePath">Relative path to the JSON file.</param>
        /// <returns>The deserialized <see cref="DictionaryMaxlength"/> instance.</returns>
        public static DictionaryMaxlength FromJson(string relativePath = "dicts/dictionary_maxlength.json")
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var fullPath = Path.Combine(baseDir, relativePath);

                if (!File.Exists(fullPath))
                    throw new FileNotFoundException($"JSON dictionary file not found: {fullPath}");

                var json = File.ReadAllText(fullPath);
                return JsonSerializer.Deserialize<DictionaryMaxlength>(json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to load dictionary from JSON.", ex);
            }
        }

        /// <summary>
        /// Serializes the current dictionary (from text files) to a JSON file.
        /// </summary>
        /// <param name="path">The output file path.</param>
        public static void SerializeToJson(string path)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(FromDicts(),
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        }

        /// <summary>
        /// Loads the dictionary from a JSON file at the specified path.
        /// </summary>
        /// <param name="path">The path to the JSON file.</param>
        /// <returns>The deserialized <see cref="DictionaryMaxlength"/> instance.</returns>
        public static DictionaryMaxlength DeserializedFromJson(string path)
        {
            return FromJson(path);
        }

        /// <summary>
        /// Loads all dictionary files from the specified directory and constructs a <see cref="DictionaryMaxlength"/> instance.
        /// </summary>
        /// <param name="relativeBaseDir">Relative directory containing dictionary text files.</param>
        /// <returns>A fully populated <see cref="DictionaryMaxlength"/> instance.</returns>
        public static DictionaryMaxlength FromDicts(string relativeBaseDir = "dicts")
        {
            var baseDir = Path.Combine(AppContext.BaseDirectory, relativeBaseDir);
            var instance = new DictionaryMaxlength
            {
                st_characters = LoadFile(Path.Combine(baseDir, "STCharacters.txt")),
                st_phrases = LoadFile(Path.Combine(baseDir, "STPhrases.txt")),
                ts_characters = LoadFile(Path.Combine(baseDir, "TSCharacters.txt")),
                ts_phrases = LoadFile(Path.Combine(baseDir, "TSPhrases.txt")),
                tw_phrases = LoadFile(Path.Combine(baseDir, "TWPhrases.txt")),
                tw_phrases_rev = LoadFile(Path.Combine(baseDir, "TWPhrasesRev.txt")),
                tw_variants = LoadFile(Path.Combine(baseDir, "TWVariants.txt")),
                tw_variants_rev = LoadFile(Path.Combine(baseDir, "TWVariantsRev.txt")),
                tw_variants_rev_phrases = LoadFile(Path.Combine(baseDir, "TWVariantsRevPhrases.txt")),
                hk_variants = LoadFile(Path.Combine(baseDir, "HKVariants.txt")),
                hk_variants_rev = LoadFile(Path.Combine(baseDir, "HKVariantsRev.txt")),
                hk_variants_rev_phrases = LoadFile(Path.Combine(baseDir, "HKVariantsRevPhrases.txt")),
                jps_characters = LoadFile(Path.Combine(baseDir, "JPShinjitaiCharacters.txt")),
                jps_phrases = LoadFile(Path.Combine(baseDir, "JPShinjitaiPhrases.txt")),
                jp_variants = LoadFile(Path.Combine(baseDir, "JPVariants.txt")),
                jp_variants_rev = LoadFile(Path.Combine(baseDir, "JPVariantsRev.txt")),
                st_punctuations = LoadFile(Path.Combine(baseDir, "STPunctuations.txt")),
                ts_punctuations = LoadFile(Path.Combine(baseDir, "TSPunctuations.txt"))
            };

            return instance;
        }

        /// <summary>
        /// Loads a dictionary from a text file, extracting key-value pairs and tracking the maximum key length.
        /// Each line should be tab-separated: key[TAB]value.
        /// </summary>
        /// <param name="path">The path to the dictionary text file.</param>
        /// <returns>A <see cref="DictWithMaxLength"/> instance with loaded data.</returns>
        private static DictWithMaxLength LoadFile(string path)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            var maxLength = 1;

            if (!File.Exists(path)) throw new FileNotFoundException($"Dictionary file not found: {path}");

            foreach (var line in File.ReadLines(path))
            {
                ReadOnlySpan<char> lineSpan = line.AsSpan().Trim();

                // Skip empty lines, whitespace-only lines, or comment lines
                if (lineSpan.IsEmpty || lineSpan.IsWhiteSpace() || (lineSpan.Length > 0 && lineSpan[0] == '#'))
                {
                    continue;
                }

                // Find the index of the first tab character
                var tabIndex = lineSpan.IndexOf('\t');

                if (tabIndex == -1) continue;
                ReadOnlySpan<char> keySpan = lineSpan.Slice(0, tabIndex);
                ReadOnlySpan<char> valueFullSpan = lineSpan.Slice(tabIndex + 1);

                // Find the index of the first space in the value part
                var firstSpaceIndex = valueFullSpan.IndexOf(' ');

                var valueSpan =
                    // If a space is found, take only the part before the first space
                    firstSpaceIndex != -1
                        ? valueFullSpan.Slice(0, firstSpaceIndex)
                        :
                        // If no space, the entire valueFullSpan is the desired value
                        valueFullSpan;

                // Trim any leading/trailing whitespace from the key and the extracted value part
                keySpan = keySpan.Trim();
                valueSpan = valueSpan.Trim();

                // Convert ReadOnlySpan<char> to string ONLY when storing in the dictionary
                var key = keySpan.ToString();
                var value = valueSpan.ToString();

                // Only add if both key and value are non-empty after trimming
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) continue;
                dict[key] = value;
                maxLength = Math.Max(maxLength, key.Length);
                // Optional: Handle lines that do not contain a tab separator if needed
            }

            var d = new DictWithMaxLength
            {
                Dict = dict,
                MaxLength = maxLength
            };

            d.BuildStartCharIndexes();

            return d;
        }

        /// <summary>
        /// Loads the dictionary from a CBOR file.
        /// </summary>
        /// <param name="relativePath">Relative path to the CBOR file.</param>
        /// <returns>The deserialized <see cref="DictionaryMaxlength"/> instance.</returns>
        public static DictionaryMaxlength FromCbor(string relativePath = "dicts/dictionary_maxlength.cbor")
        {
            var baseDir = AppContext.BaseDirectory;
            var fullPath = Path.Combine(baseDir, relativePath);
            var bytes = File.ReadAllBytes(fullPath);
            var cbor = CBORObject.DecodeFromBytes(bytes, CBOREncodeOptions.Default);
            return cbor.ToObject<DictionaryMaxlength>();
        }

        /// <summary>
        /// Serializes the dictionary to CBOR format and saves it to a file.
        /// </summary>
        /// <param name="path">The output file path.</param>
        public static void SaveCbor(string path)
        {
            var cbor = CBORObject.FromObject(FromDicts());
            File.WriteAllBytes(path, cbor.EncodeToBytes());
        }

        /// <summary>
        /// Serializes the dictionary to CBOR format and returns the bytes.
        /// </summary>
        /// <returns>CBOR-encoded byte array.</returns>
        public static byte[] ToCborBytes()
        {
            return CBORObject.FromObject(FromDicts()).EncodeToBytes();
        }

        /// <summary>
        /// Serializes the dictionary to JSON, compresses it with Zstd, and saves to a file.
        /// </summary>
        /// <param name="path">The output file path.</param>
        public static void SaveCompressed(string path)
        {
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(FromDicts());

            using (var compressor = new Compressor(19))
            {
                var compressed = compressor.Wrap(jsonBytes);
                File.WriteAllBytes(path, compressed.ToArray());
            }
        }

        /// <summary>
        /// Loads the dictionary from a Zstd-compressed JSON file.
        /// </summary>
        /// <param name="path">The path to the compressed file.</param>
        /// <returns>The deserialized <see cref="DictionaryMaxlength"/> instance.</returns>
        public static DictionaryMaxlength LoadCompressed(string path)
        {
            var compressed = File.ReadAllBytes(path);

            using (var decompressor = new Decompressor())
            {
                var jsonBytes = decompressor.Unwrap(compressed);
                return JsonSerializer.Deserialize<DictionaryMaxlength>(jsonBytes);
            }
        }
    }
}