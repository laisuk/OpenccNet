using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
        public Dictionary<string, string> Data { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// The maximum length of any key in the dictionary.
        /// Used for optimizing longest-match lookups.
        /// </summary>
        public int MaxLength { get; set; }

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
            return Data.TryGetValue(key, out value);
        }

        /// <summary>
        /// Gets the number of entries in the dictionary.
        /// </summary>
        public int Count => Data.Count;
    }

    /// <summary>
    /// Holds all conversion dictionaries for different OpenCC conversion types.
    /// Each property represents a specific conversion mapping.
    /// </summary>
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

    /// <summary>
    /// Provides methods to load, save, and cache OpenCC conversion dictionaries
    /// in various formats (JSON, CBOR, Zstd-compressed).
    /// </summary>
    public static class DictionaryLib
    {
        // Lock object for thread-safe caching
        private static readonly object LockObject = new object();

        // Cached instance of the loaded dictionary to avoid repeated disk I/O
        private static DictionaryMaxlength _cachedDictionary;

        /// <summary>
        /// Loads and caches the dictionary from a Zstd-compressed file.
        /// Returns the cached instance on subsequent calls.
        /// </summary>
        public static DictionaryMaxlength New()
        {
            if (_cachedDictionary == null)
            {
                lock (LockObject)
                {
                    if (_cachedDictionary == null)
                    {
                        _cachedDictionary = FromZstd();
                    }
                }
            }

            return _cachedDictionary;
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
                int tabIndex = lineSpan.IndexOf('\t');

                if (tabIndex != -1)
                {
                    ReadOnlySpan<char> keySpan = lineSpan.Slice(0, tabIndex);
                    ReadOnlySpan<char> valueFullSpan = lineSpan.Slice(tabIndex + 1);

                    // Find the index of the first space in the value part
                    int firstSpaceIndex = valueFullSpan.IndexOf(' ');

                    ReadOnlySpan<char> valueSpan;
                    if (firstSpaceIndex != -1)
                    {
                        // If a space is found, take only the part before the first space
                        valueSpan = valueFullSpan.Slice(0, firstSpaceIndex);
                    }
                    else
                    {
                        // If no space, the entire valueFullSpan is the desired value
                        valueSpan = valueFullSpan;
                    }

                    // Trim any leading/trailing whitespace from the key and the extracted value part
                    keySpan = keySpan.Trim();
                    valueSpan = valueSpan.Trim();

                    // Convert ReadOnlySpan<char> to string ONLY when storing in the dictionary
                    var key = keySpan.ToString();
                    var value = valueSpan.ToString();

                    // Only add if both key and value are non-empty after trimming
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                    {
                        dict[key] = value;
                        maxLength = Math.Max(maxLength, key.Length);
                    }
                }
                // Optional: Handle lines that do not contain a tab separator if needed
            }

            return new DictWithMaxLength
            {
                Data = dict,
                MaxLength = maxLength
            };
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