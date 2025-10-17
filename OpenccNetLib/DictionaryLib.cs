using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
// using System.Text.Json.Serialization;
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
        /// The minimum length of any key in the dictionary.
        /// Used for optimizing longest-match lookups.
        /// </summary>
        public int MinLength { get; set; }

        /// <summary>
        /// Bitmask tracking which key lengths (1..64) exist in <see cref="Dict"/>.
        /// Helps skip impossible probes in hot lookup paths.
        /// </summary>
        public ulong LengthMask { get; set; }

        /// <summary>
        /// Tracks key lengths &gt; 64 UTF-16 units (rare) for completeness.
        /// Allocated lazily to avoid overhead when not needed.
        /// </summary>
        public HashSet<int> LongLengths { get; set; }

        /// <summary>
        /// Per-starter mask of key lengths (1 to =64) present for that starter.
        /// Key is UTF-16 starter:
        ///  - 1-char for BMP
        ///  - 2-char for surrogate-pair (high+low)
        /// </summary>
        public Dictionary<string, ulong> StarterLenMask { get; set; }

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
        /// Determines whether the dictionary contains any key with the specified length.
        /// </summary>
        /// <param name="length">Target key length (in UTF-16 code units).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool SupportsLength(int length)
        {
            if (length <= 0) return false;

            var minLen = MinLength;
            if (minLen == 0 || length < minLen || length > MaxLength)
                return false;

            if (length <= 64)
                return ((LengthMask >> (length - 1)) & 1UL) != 0UL;

            var longLengths = LongLengths;
            return longLengths != null && longLengths.Contains(length);
        }

        /// <summary>
        /// Sets the length metadata that was precomputed during dictionary load.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetLengthMetadata(ulong mask, HashSet<int> longLengths)
        {
            LengthMask = mask;
            LongLengths = longLengths;
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
    public sealed class DictionaryMaxlength
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
        /// <summary>Add a thread-safe, opt-in cache.</summary>
        private static readonly Lazy<DictionaryMaxlength> DefaultLib =
            new Lazy<DictionaryMaxlength>(() => FromZstd(), true);

        /// <summary>Singleton reuse instead of reloading repeatedly.</summary>
        public static DictionaryMaxlength Default => DefaultLib.Value;

        /// <summary>
        /// Loads the dictionary from a Zstd-compressed file.
        /// Always returns a new instance.
        /// </summary>
        public static DictionaryMaxlength New()
        {
            return DefaultLib.Value;
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
                    var instance = JsonSerializer.Deserialize<DictionaryMaxlength>(decompressionStream);
                    return instance;
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
                var instance = JsonSerializer.Deserialize<DictionaryMaxlength>(json);
                return instance;
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

            // RebuildAllLengthMetadata(instance);

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
            var maxLength = 0; // start at 0 so empty dict stays 0
            var minLength = int.MaxValue;
            var lengthMask = 0UL;
            HashSet<int> longLengths = null;

            if (!File.Exists(path)) throw new FileNotFoundException($"Dictionary file not found: {path}");

            foreach (var line in File.ReadLines(path))
            {
                var lineSpan = line.AsSpan().Trim();

                // Skip empty lines, whitespace-only lines, or comment lines
                if (lineSpan.IsEmpty || lineSpan.IsWhiteSpace() || (lineSpan.Length > 0 && lineSpan[0] == '#'))
                {
                    continue;
                }

                // Find the index of the first tab character
                var tabIndex = lineSpan.IndexOf('\t');

                if (tabIndex == -1) continue;
                var keySpan = lineSpan.Slice(0, tabIndex);
                var valueFullSpan = lineSpan.Slice(tabIndex + 1);

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

                var keyLength = key.Length;
                if (keyLength == 0) continue;

                if (keyLength > maxLength) maxLength = keyLength;
                if (keyLength < minLength) minLength = keyLength;

                if (keyLength <= 64)
                {
                    lengthMask |= 1UL << (keyLength - 1);
                }
                else
                {
                    if (longLengths == null)
                        longLengths = new HashSet<int>();

                    longLengths.Add(keyLength);
                }
                // Optional: Handle lines that do not contain a tab separator if needed
            }

            if (dict.Count == 0)
            {
                maxLength = 0;
                minLength = 0;
                lengthMask = 0UL;
                longLengths = null;
            }
            else if (minLength == int.MaxValue)
            {
                minLength = maxLength;
            }

            var d = new DictWithMaxLength
            {
                Dict = dict,
                MaxLength = maxLength,
                MinLength = minLength
            };

            d.SetLengthMetadata(lengthMask, longLengths);
            BuildStarterLenMask(d); // 👈 slot in right here

            return d;
        }

        private static void BuildStarterLenMask(DictWithMaxLength d)
        {
            if (d == null || d.Dict == null || d.Dict.Count == 0)
                return;

            var map = new Dictionary<string, ulong>(StringComparer.Ordinal);

            foreach (var key in d.Dict.Keys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                var len = key.Length;
                if (len <= 0) continue;

                string starter;
                if (len >= 2 && char.IsHighSurrogate(key[0]) && char.IsLowSurrogate(key[1]))
                    starter = key.Substring(0, 2);
                else
                    starter = key.Substring(0, 1);

                if (!map.TryGetValue(starter, out var mask))
                    mask = 0UL;

                if ((uint)len - 1u < 64u)
                    mask |= 1UL << (len - 1);

                map[starter] = mask;
            }

            d.StarterLenMask = map;
        }

        /// <summary>
        /// Loads <see cref="DictionaryMaxlength"/> from a CBOR file and rebuilds
        /// non-serialized length metadata (Min/Max/LengthMask) for all dictionaries.
        /// </summary>
        /// <param name="relativePath">
        /// Relative path under <see cref="AppContext.BaseDirectory"/> to the CBOR file.
        /// Default: <c>dicts/dictionary_maxlength.cbor</c>.
        /// </param>
        /// <returns>The hydrated <see cref="DictionaryMaxlength"/> instance.</returns>
        /// <exception cref="FileNotFoundException">If the CBOR file cannot be found.</exception>
        /// <exception cref="IOException">If the CBOR file cannot be read.</exception>
        public static DictionaryMaxlength FromCbor(string relativePath = "dicts/dictionary_maxlength.cbor")
        {
            if (string.IsNullOrEmpty(relativePath))
                throw new ArgumentException("Path must not be null or empty.", nameof(relativePath));

            var baseDir = AppContext.BaseDirectory;
            var fullPath = Path.Combine(baseDir, relativePath);

            if (!File.Exists(fullPath))
                throw new FileNotFoundException("CBOR dictionary file not found.", fullPath);

            var bytes = File.ReadAllBytes(fullPath);

            // Decode and materialize the object graph
            var cbor = CBORObject.DecodeFromBytes(bytes, CBOREncodeOptions.Default);
            var instance = cbor.ToObject<DictionaryMaxlength>();

            // IMPORTANT: rebuild derived fields not present in serialized form
            // RebuildAllLengthMetadata(instance);

            return instance;
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
        public static void SaveJsonCompressed(string path)
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
        public static DictionaryMaxlength LoadJsonCompressed(string path)
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