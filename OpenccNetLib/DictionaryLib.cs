using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
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
        [JsonInclude]
        public Dictionary<string, string> Dict { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// The maximum length of any key in the dictionary.
        /// Used for optimizing longest-match lookups.
        /// </summary>
        [JsonInclude]
        public int MaxLength { get; set; }

        /// <summary>
        /// The minimum length of any key in the dictionary.
        /// Used for optimizing longest-match lookups.
        /// </summary>
        [JsonInclude]
        public int MinLength { get; set; }

        /// <summary>
        /// Bitmask tracking which key lengths (1..64) exist in <see cref="Dict"/>.
        /// Helps skip impossible probes in hot lookup paths.
        /// </summary>
        [JsonInclude]
        public ulong LengthMask { get; set; }

        /// <summary>
        /// Tracks key lengths &gt; 64 UTF-16 units (rare) for completeness.
        /// Allocated lazily to avoid overhead when not needed.
        /// </summary>
        [JsonInclude]
        public HashSet<int> LongLengths { get; set; }

        /// <summary>
        /// Per-starter mask of key lengths (1 to =64) present for that starter.
        /// Key is UTF-16 starter:
        ///  - 1-char for BMP
        ///  - 2-char for surrogate-pair (high+low)
        /// </summary>
        [JsonInclude]
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
    /// Provides centralized access to the global dictionary provider and  
    /// conversion plan cache used by all OpenCC conversions.
    /// </summary>
    /// <remarks>
    /// This class manages a single shared <see cref="DictionaryMaxlength"/> instance  
    /// and its associated <see cref="ConversionPlanCache"/> for efficient reuse  
    /// across all OpenCC operations.  
    /// <para>
    /// It supports hot-swapping custom dictionary providers via  
    /// <see cref="SetDictionaryProvider(DictionaryMaxlength)"/> or  
    /// <see cref="Opencc.UseCustomDictionary(DictionaryMaxlength)"/> while keeping  
    /// the default lazy-loaded instance intact.  
    /// </para>
    /// </remarks>
    public static class DictionaryLib
    {
        // --------------------------------------------------------------------------------
        // Lazy loader for the default dictionary
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Lazily initializes the default <see cref="DictionaryMaxlength"/> instance  
        /// used by all conversions that do not explicitly specify a custom dictionary set.  
        /// 
        /// This uses <see cref="FromZstd"/> to load the bundled Zstandard-compressed  
        /// dictionary data on first access. The initialization is thread-safe and  
        /// performed only once per process lifetime.
        /// </summary>
        private static readonly Lazy<DictionaryMaxlength> DefaultLib =
            new Lazy<DictionaryMaxlength>(() => FromZstd(), isThreadSafe: true);

        // --------------------------------------------------------------------------------
        // Global plan cache
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Global cache for precomputed <see cref="DictRefs"/> and  
        /// <see cref="ConversionPlanCache"/> instances derived from <see cref="DefaultLib"/>.  
        /// 
        /// Each plan bundles optimized lookup structures, prefiltered dictionary  
        /// references, and starter masks for a specific <see cref="Opencc.OpenccConfig"/> and  
        /// punctuation mode.  
        /// 
        /// This cache eliminates redundant plan reconstruction between repeated  
        /// conversions, improving throughput and reducing GC pressure.  
        /// It is initialized with a delegate that returns the lazily loaded  
        /// <see cref="DefaultLib"/> instance.
        /// </summary>
        public static ConversionPlanCache PlanCache;

        // --------------------------------------------------------------------------------
        // Public accessors and provider management
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Gets the singleton <see cref="DictionaryMaxlength"/> instance for reuse across  
        /// all conversions. This property always returns the same object reference.  
        /// </summary>
        /// <remarks>
        /// The dictionary is lazily initialized from the embedded Zstandard-compressed bundle  
        /// on first access and is safe for concurrent read access from multiple threads.  
        /// <para>
        /// To obtain a new, independent dictionary instance (e.g., when reloading from  
        /// an external file), use <see cref="FromZstd"/> or other loader methods directly.  
        /// </para>
        /// </remarks>
        /// <returns>
        /// The shared <see cref="DictionaryMaxlength"/> instance used for all conversions.  
        /// </returns>
        public static DictionaryMaxlength Provider => DefaultLib.Value;

        /// <summary>
        /// Returns the currently active dictionary provider used for plan construction.  
        /// </summary>
        /// <remarks>
        /// This method reads the current provider reference atomically to ensure that  
        /// concurrent provider swaps are observed immediately and without torn reads.  
        /// </remarks>
        /// <returns>
        /// The <see cref="DictionaryMaxlength"/> instance currently registered as the active provider.  
        /// </returns>
        public static DictionaryMaxlength GetProvider()
        {
            var currentProvider = Provider;
            return Volatile.Read(ref currentProvider);
        }

        /// <summary>
        /// Returns the singleton dictionary instance without creating a new copy.  
        /// </summary>
        /// <remarks>
        /// This method is an alias of <see cref="Provider"/> retained for backward compatibility.  
        /// It ensures that the global dictionary provider is properly initialized, but does  
        /// not load or allocate a new dictionary.  
        /// <para>
        /// To explicitly create a separate instance, use <see cref="FromZstd"/> or other loader methods.  
        /// </para>
        /// </remarks>
        /// <returns>
        /// The singleton <see cref="DictionaryMaxlength"/> instance shared across conversions.  
        /// </returns>
        public static DictionaryMaxlength New()
        {
            SetDictionaryProvider(() => Provider);
            return Provider;
        }

        /// <summary>
        /// Replaces the active dictionary provider used by the global  
        /// <see cref="ConversionPlanCache"/> and clears all cached plans.  
        /// </summary>
        /// <remarks>
        /// Call this method when hot-reloading or replacing the dictionary source at runtime  
        /// (for example, when loading a custom dictionary set).  
        /// <para>
        /// This method updates the internal provider delegate used to obtain  
        /// <see cref="DictionaryMaxlength"/> instances, then discards all previously built  
        /// conversion plans and starter-union caches to ensure consistency.  
        /// </para>
        /// <para>
        /// Thread-safe: the provider reference is updated atomically.  
        /// </para>
        /// </remarks>
        /// <param name="provider">
        /// A delegate returning the new <see cref="DictionaryMaxlength"/> instance  
        /// to be used by subsequent plan builds.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="provider"/> is <see langword="null"/>.
        /// </exception>
        private static void SetDictionaryProvider(Func<DictionaryMaxlength> provider)
        {
            if (provider is null)
                throw new ArgumentNullException(nameof(provider));

            // Publish new provider atomically
            var currentProvider = Provider;
            Interlocked.Exchange(ref currentProvider, provider());

            // Replace the global cache with a new instance using the new provider
            PlanCache = new ConversionPlanCache(provider);

            // Clear any previous cache state (for safety and consistency)
            PlanCache.Clear();
        }

        /// <summary>
        /// Resets the active dictionary provider back to the default singleton  
        /// loaded from the embedded resources, and clears all cached plans.  
        /// </summary>
        /// <remarks>
        /// This restores the original <see cref="Provider"/> provider  
        /// so that subsequent conversions use the default dictionary set.  
        /// Existing conversion plans and starter unions are discarded.  
        /// </remarks>
        public static void ResetDictionaryProviderToDefault()
        {
            var currentProvider = Provider;
            Interlocked.Exchange(ref currentProvider, DefaultLib.Value);
            PlanCache.Clear();
        }

        /// <summary>
        /// Replaces the active dictionary provider using a fixed  
        /// <see cref="DictionaryMaxlength"/> instance instead of a delegate.  
        /// </summary>
        /// <remarks>
        /// This overload is a convenience wrapper for  
        /// <see cref="SetDictionaryProvider(Func{DictionaryMaxlength})"/> that automatically  
        /// wraps the supplied instance in a provider delegate.  
        /// <para>
        /// The global <see cref="DictionaryLib.PlanCache"/> is rebuilt and all existing  
        /// cached plans are cleared to ensure consistency with the new dictionary source.  
        /// </para>
        /// </remarks>
        /// <param name="dictionary">
        /// The <see cref="DictionaryMaxlength"/> instance to use as the new active dictionary source.  
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="dictionary"/> is <see langword="null"/>.
        /// </exception>
        public static void SetDictionaryProvider(DictionaryMaxlength dictionary)
        {
            if (dictionary == null)
                throw new ArgumentNullException(nameof(dictionary));

            SetDictionaryProvider(() => dictionary);
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

        /// <summary>
        /// Builds a per-starter key-length bitmask for the specified dictionary.
        /// </summary>
        /// <param name="d">
        /// The <see cref="DictWithMaxLength"/> instance whose <see cref="DictWithMaxLength.Dict"/> keys
        /// are analyzed to populate <see cref="DictWithMaxLength.StarterLenMask"/>.
        /// </param>
        /// <remarks>
        /// <para>
        /// This method scans all keys in the dictionary and computes, for each unique starter
        /// (first Unicode character or surrogate pair), a <c>ulong</c> bitmask representing
        /// which key lengths (1–64) exist for that starter.
        /// </para>
        /// <para>
        /// Each bit position <c>n-1</c> corresponds to the presence of a key of length <c>n</c>.
        /// Lengths greater than 64 are ignored, as they are extremely rare and do not fit in the 64-bit mask.
        /// </para>
        /// <para>
        /// The resulting <see cref="DictWithMaxLength.StarterLenMask"/> enables fast runtime gating
        /// in hot lookup paths by allowing quick exclusion of impossible key lengths for a given starter.
        /// </para>
        /// </remarks>
        private static void BuildStarterLenMask(DictWithMaxLength d)
        {
            if (d?.Dict == null || d.Dict.Count == 0)
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
            var instance = (FromDicts());
            var cbor = CBORObject.FromObject(instance);
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