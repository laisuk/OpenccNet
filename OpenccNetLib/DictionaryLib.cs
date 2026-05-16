using System;
using System.Collections.Generic;
using System.IO;
// using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using PeterO.Cbor;
using ZstdSharp;

namespace OpenccNetLib
{
    /// <summary>
    /// Represents a dictionary with string keys and values plus derived key-length metadata.
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
        /// Per-starter mask of key lengths (1 to 64) present for that starter.
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
        /// <param name="length">Target key length in UTF-16 code units.</param>
        /// <returns>
        /// <see langword="true"/> if the dictionary contains at least one key with
        /// the specified length; otherwise, <see langword="false"/>.
        /// </returns>
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
        /// Sets the key-length metadata that was precomputed during dictionary load
        /// or rebuilt after dictionary mutation.
        /// </summary>
        /// <param name="mask">
        /// Bitmask for key lengths from 1 through 64 UTF-16 code units.
        /// </param>
        /// <param name="longLengths">
        /// Optional set of key lengths greater than 64 UTF-16 code units.
        /// </param>
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
    /// Holds all dictionary tables used by the OpenCC conversion engine.
    /// </summary>
    /// <remarks>
    /// This type is a mutable data-transfer container for built-in dictionaries,
    /// custom dictionary loading, and serialization scenarios. The snake_case
    /// property names are part of the public API and match the dictionary payload
    /// names used by OpenccNet and related packages.
    /// <para>
    /// Most consumers do not need to construct this type manually. Use
    /// <see cref="DictionaryLib.Provider"/> for the built-in dictionary,
    /// <see cref="DictionaryLib.FromDicts(string,IDictionary{DictSlot,string},IDictionary{DictSlot,string})"/> or
    /// <see cref="DictionaryLib.FromJson(string)"/> to load dictionary data, and
    /// <see cref="Opencc.UseCustomDictionary(DictionaryMaxlength)"/> to activate a
    /// custom dictionary set.
    /// </para>
    /// </remarks>
    // ReSharper disable InconsistentNaming
    public sealed class DictionaryMaxlength
    {
        /// <summary>
        /// Simplified-to-Traditional character mappings.
        /// </summary>
        public DictWithMaxLength st_characters { get; set; } = new DictWithMaxLength();

        /// <summary>
        /// Simplified-to-Traditional phrase mappings.
        /// </summary>
        public DictWithMaxLength st_phrases { get; set; } = new DictWithMaxLength();

        /// <summary>
        /// Traditional-to-Simplified character mappings.
        /// </summary>
        public DictWithMaxLength ts_characters { get; set; } = new DictWithMaxLength();

        /// <summary>
        /// Traditional-to-Simplified phrase mappings.
        /// </summary>
        public DictWithMaxLength ts_phrases { get; set; } = new DictWithMaxLength();

        /// <summary>
        /// Traditional-to-Taiwan phrase mappings.
        /// </summary>
        public DictWithMaxLength tw_phrases { get; set; } = new DictWithMaxLength();

        /// <summary>
        /// Taiwan-to-Traditional phrase mappings.
        /// </summary>
        public DictWithMaxLength tw_phrases_rev { get; set; } = new DictWithMaxLength();

        /// <summary>
        /// Traditional-to-Taiwan character variant mappings.
        /// </summary>
        public DictWithMaxLength tw_variants { get; set; } = new DictWithMaxLength();

        /// <summary>
        /// Taiwan-to-Traditional character variant mappings.
        /// </summary>
        public DictWithMaxLength tw_variants_rev { get; set; } = new DictWithMaxLength();

        /// <summary>
        /// Taiwan-to-Traditional phrase variant mappings.
        /// </summary>
        public DictWithMaxLength tw_variants_rev_phrases { get; set; } = new DictWithMaxLength();

        /// <summary>
        /// Traditional-to-Hong Kong character variant mappings.
        /// </summary>
        public DictWithMaxLength hk_variants { get; set; } = new DictWithMaxLength();

        /// <summary>
        /// Hong Kong-to-Traditional character variant mappings.
        /// </summary>
        public DictWithMaxLength hk_variants_rev { get; set; } = new DictWithMaxLength();

        /// <summary>
        /// Hong Kong-to-Traditional phrase variant mappings.
        /// </summary>
        public DictWithMaxLength hk_variants_rev_phrases { get; set; } = new DictWithMaxLength();

        /// <summary>
        /// Traditional Kyujitai-to-Japanese Shinjitai character mappings.
        /// </summary>
        public DictWithMaxLength jps_characters { get; set; } = new DictWithMaxLength();

        /// <summary>
        /// Traditional Kyujitai-to-Japanese Shinjitai phrase mappings.
        /// </summary>
        public DictWithMaxLength jps_phrases { get; set; } = new DictWithMaxLength();

        /// <summary>
        /// Traditional-to-Japanese character variant mappings.
        /// </summary>
        public DictWithMaxLength jp_variants { get; set; } = new DictWithMaxLength();

        /// <summary>
        /// Japanese variant-to-Traditional character mappings.
        /// </summary>
        public DictWithMaxLength jp_variants_rev { get; set; } = new DictWithMaxLength();

        /// <summary>
        /// Simplified-to-Traditional punctuation mappings.
        /// </summary>
        public DictWithMaxLength st_punctuations { get; set; } = new DictWithMaxLength();

        /// <summary>
        /// Traditional-to-Simplified punctuation mappings.
        /// </summary>
        public DictWithMaxLength ts_punctuations { get; set; } = new DictWithMaxLength();
    }
    // ReSharper restore InconsistentNaming

    /// <summary>
    /// Provides centralized access to the built-in default dictionary and the global
    /// conversion plan cache used by all OpenCC conversions.
    /// </summary>
    /// <remarks>
    /// This class exposes a lazily loaded, process-wide default
    /// <see cref="DictionaryMaxlength"/> instance (<see cref="Provider"/>) and a global
    /// <see cref="ConversionPlanCache"/> (<see cref="PlanCache"/>) that caches precomputed
    /// <see cref="DictRefs"/> plans.
    /// <para>
    /// The global <see cref="PlanCache"/> is the <b>active planning source of truth</b>
    /// for conversions: all conversion paths obtain <see cref="DictRefs"/> from this cache.
    /// To apply a custom dictionary source, the active provider delegate is updated and a
    /// fresh <see cref="ConversionPlanCache"/> instance is published, discarding any
    /// previously cached plans and starter-union state to avoid mixed or inconsistent data.
    /// </para>
    /// <para>
    /// The built-in default dictionary singleton remains intact and reusable regardless
    /// of provider swaps. Custom dictionary instances are not cloned or modified; they are
    /// used directly as the source for subsequent plan construction.
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
        /// Global cache for precomputed <see cref="DictRefs"/> instances used by all
        /// OpenCC conversions.
        /// </summary>
        /// <remarks>
        /// Each cached plan bundles optimized lookup structures, prefiltered dictionary  
        /// references, and starter masks for a specific <see cref="OpenccConfig"/> and  
        /// punctuation mode.
        /// <para>
        /// This cache is the <b>active planning source of truth</b> for all conversions.  
        /// Switching the dictionary provider replaces this cache instance entirely,  
        /// ensuring that all newly built plans are derived from the updated dictionary  
        /// source.
        /// </para>
        /// <para>
        /// By default, the cache is initialized with a provider delegate that returns  
        /// the lazily loaded built-in dictionary (<see cref="Provider"/>).  
        /// Custom dictionaries can be applied through
        /// <see cref="SetDictionaryProvider(DictionaryMaxlength)"/> or
        /// <see cref="Opencc.UseCustomDictionary(DictionaryMaxlength)"/>.
        /// </para>
        /// <para>
        /// This design avoids partial or inconsistent state during dictionary swaps by  
        /// treating the cache and its provider as a single atomic unit.
        /// </para>
        /// </remarks>
        private static ConversionPlanCache _planCache = new ConversionPlanCache(() => Provider);

        /// <summary>
        /// Gets the active global cache for precomputed conversion plans.
        /// </summary>
        /// <remarks>
        /// The cache instance is replaced only by the dictionary-provider APIs on this
        /// type. This keeps provider swaps atomic and prevents external code from
        /// publishing a null or inconsistent cache.
        /// </remarks>
        public static ConversionPlanCache PlanCache => Volatile.Read(ref _planCache);

        // --------------------------------------------------------------------------------
        // Public accessors and provider management
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Delegate that supplies the <see cref="DictionaryMaxlength"/> instance used
        /// for constructing new conversion plans.
        /// </summary>
        /// <remarks>
        /// Defaults to the built-in lazy-loaded dictionary (<see cref="DefaultLib"/>),
        /// but may be replaced to support custom dictionary sources.
        /// </remarks>
        private static Func<DictionaryMaxlength> _activeProvider = () => DefaultLib.Value;

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
        /// Returns the dictionary instance supplied by the currently active provider delegate
        /// for constructing new conversion plans.
        /// </summary>
        /// <remarks>
        /// This method reflects the <b>logical provider</b> used when building new
        /// <see cref="DictRefs"/> and populating the global <see cref="PlanCache"/>.
        /// <para>
        /// Note that the active provider delegate is lightweight and does not own any
        /// cached state. All derived lookup structures and starter-union caches are owned
        /// by <see cref="PlanCache"/>. To fully switch dictionary sources for future
        /// conversions, call <see cref="SetDictionaryProvider(DictionaryMaxlength)"/> or
        /// <see cref="Opencc.UseCustomDictionary(DictionaryMaxlength)"/>.
        /// </para>
        /// <para>
        /// This method does not imply atomicity or synchronization guarantees beyond those
        /// provided by the underlying provider delegate implementation.
        /// </para>
        /// </remarks>
        /// <returns>
        /// The <see cref="DictionaryMaxlength"/> instance currently returned by the active
        /// provider delegate.
        /// </returns>
        public static DictionaryMaxlength GetActiveProvider() => _activeProvider();

        /// <summary>
        /// Returns the default singleton dictionary instance and resets the active
        /// planning provider to use the built-in dictionary.
        /// </summary>
        /// <remarks>
        /// This method is retained for backward compatibility and acts as a convenience
        /// wrapper around <see cref="Provider"/>.
        /// <para>
        /// In addition to returning the default singleton dictionary, this method
        /// reconfigures the global planning source (<see cref="PlanCache"/>) to use the
        /// built-in dictionary provider and clears all cached conversion plans.
        /// </para>
        /// <para>
        /// No new dictionary instance is created or allocated.
        /// To explicitly create a separate dictionary instance, use
        /// <see cref="FromZstd"/> or other loader methods.
        /// </para>
        /// </remarks>
        /// <returns>
        /// The default singleton <see cref="DictionaryMaxlength"/> instance shared across conversions.
        /// </returns>
        public static DictionaryMaxlength New()
        {
            SetDictionaryProvider(() => Provider);
            return Provider;
        }

        /// <summary>
        /// Replaces the active dictionary source used for constructing new conversion plans
        /// by updating the provider delegate and publishing a fresh global
        /// <see cref="ConversionPlanCache"/> instance.
        /// </summary>
        /// <remarks>
        /// This method is the primary mechanism for switching dictionary sources at runtime
        /// (for example, when applying a custom dictionary set or performing hot-reloads).
        /// <para>
        /// The provider delegate is first updated to supply the new
        /// <see cref="DictionaryMaxlength"/> instance. A new
        /// <see cref="ConversionPlanCache"/> is then created and published, ensuring that
        /// all subsequently built plans are derived exclusively from the new dictionary
        /// source.
        /// </para>
        /// <para>
        /// Cached plans and starter-union data from the previous dictionary source are
        /// intentionally discarded by replacing the cache instance, preventing mixed or
        /// inconsistent state.
        /// </para>
        /// <para>
        /// This method does not modify or clone any existing dictionary instances; it
        /// only affects how future conversion plans are constructed.
        /// </para>
        /// </remarks>
        /// <param name="provider">
        /// A delegate that returns the <see cref="DictionaryMaxlength"/> instance to be
        /// used as the source for all subsequent plan construction.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="provider"/> is <see langword="null"/>.
        /// </exception>
        private static void SetDictionaryProvider(Func<DictionaryMaxlength> provider)
        {
            if (provider is null)
                throw new ArgumentNullException(nameof(provider));

            // publish provider first (so any new cache uses it)
            Volatile.Write(ref _activeProvider, provider);

            // publish a fresh cache (empty, consistent)
            var newCache = new ConversionPlanCache(provider);

            // Replace the global cache with a new instance using the new provider.
            Interlocked.Exchange(ref _planCache, newCache);
        }

        /// <summary>
        /// Restores the active dictionary source used for plan construction to the
        /// built-in default dictionary.
        /// </summary>
        /// <remarks>
        /// This method resets the provider delegate to return the lazily loaded
        /// built-in dictionary (<see cref="Provider"/>) and publishes a fresh
        /// <see cref="ConversionPlanCache"/> instance.
        /// <para>
        /// All previously cached conversion plans and starter-union data are
        /// intentionally discarded, ensuring that all subsequent conversions
        /// are derived exclusively from the default dictionary source.
        /// </para>
        /// <para>
        /// No dictionary instances are modified or reloaded; the original
        /// built-in singleton remains intact and is reused.
        /// </para>
        /// </remarks>
        public static void ResetDictionaryProviderToDefault()
        {
            SetDictionaryProvider(() => DefaultLib.Value);
        }

        /// <summary>
        /// Replaces the active dictionary source for plan construction using a fixed
        /// <see cref="DictionaryMaxlength"/> instance.
        /// </summary>
        /// <remarks>
        /// This overload is a convenience wrapper around
        /// <see cref="SetDictionaryProvider(Func{DictionaryMaxlength})"/> that wraps the
        /// supplied instance in a provider delegate.
        /// <para>
        /// A fresh global <see cref="ConversionPlanCache"/> instance is published, and all
        /// previously cached conversion plans and starter-union data are discarded. This
        /// ensures that all subsequently built plans are derived exclusively from the new
        /// dictionary source.
        /// </para>
        /// <para>
        /// The supplied dictionary instance is not cloned or modified. It is used
        /// directly as the source for all future plan construction.
        /// </para>
        /// </remarks>
        /// <param name="dictionary">
        /// The <see cref="DictionaryMaxlength"/> instance to use as the new active dictionary
        /// source for subsequent plan construction.
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
        /// Loads the bundled dictionary from a Zstd-compressed JSON file.
        /// </summary>
        /// <param name="relativePath">
        /// Relative path under <see cref="AppContext.BaseDirectory"/> or an absolute
        /// path to the Zstandard-compressed JSON dictionary file.
        /// </param>
        /// <returns>
        /// The deserialized and normalized <see cref="DictionaryMaxlength"/> instance.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="relativePath"/> is null, empty, or whitespace.
        /// </exception>
        /// <exception cref="FileNotFoundException">
        /// Thrown when the Zstandard dictionary file does not exist.
        /// </exception>
        private static DictionaryMaxlength FromZstd(
            string relativePath = "dicts/dictionary_maxlength.zstd")
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new ArgumentException(
                    "Path must not be null or empty.",
                    nameof(relativePath));
            }

            var fullPath = Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(AppContext.BaseDirectory, relativePath);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    "Zstd dictionary file not found.",
                    fullPath);
            }

            using (var inputStream = File.OpenRead(fullPath))
            using (var decompressionStream =
                   new DecompressionStream(inputStream))
            {
                var instance =
                    JsonSerializer.Deserialize<DictionaryMaxlength>(
                        decompressionStream);

                return EnsureDerivedMetadata(instance);
            }
        }

        /// <summary>
        /// Loads a <see cref="DictionaryMaxlength"/> instance from a JSON file.
        ///
        /// The JSON payload is deserialized and normalized through
        /// <see cref="EnsureDerivedMetadata(DictionaryMaxlength)"/> to restore any
        /// derived lookup metadata required by the hot conversion paths.
        ///
        /// This method is intended primarily for debugging, development,
        /// interoperability, or external dictionary generation workflows.
        /// Production applications should prefer the default embedded Zstd dictionaries
        /// for best reliability and deployment simplicity.
        /// </summary>
        /// <param name="relativePath">
        /// Relative path under <see cref="AppContext.BaseDirectory"/> or an absolute
        /// path to the JSON dictionary file.
        /// Defaults to <c>dicts/dictionary_maxlength.json</c>.
        /// </param>
        /// <returns>
        /// The deserialized and normalized
        /// <see cref="DictionaryMaxlength"/> instance.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="relativePath"/> is null or empty.
        /// </exception>
        /// <exception cref="FileNotFoundException">
        /// Thrown when the JSON dictionary file does not exist.
        /// </exception>
        /// <exception cref="JsonException">
        /// Thrown when the JSON payload is invalid or cannot be deserialized.
        /// </exception>
        /// <exception cref="IOException">
        /// Thrown when the file cannot be opened or read.
        /// </exception>
        public static DictionaryMaxlength FromJson(
            string relativePath = "dicts/dictionary_maxlength.json")
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new ArgumentException(
                    "Path must not be null or empty.",
                    nameof(relativePath));
            }

            var fullPath = Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(AppContext.BaseDirectory, relativePath);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    "JSON dictionary file not found.",
                    fullPath);
            }

            using (var stream = File.OpenRead(fullPath))
            {
                var instance =
                    JsonSerializer.Deserialize<DictionaryMaxlength>(stream);

                return EnsureDerivedMetadata(instance);
            }
        }

        /// <summary>
        /// Serializes a <see cref="DictionaryMaxlength"/> instance to a JSON file.
        ///
        /// <para>
        /// If no dictionary instance is provided, the dictionary is loaded from the
        /// default OpenCC text dictionary sources via <see cref="FromDicts"/>.
        /// </para>
        /// </summary>
        /// <param name="path">
        /// Output JSON file path.
        /// </param>
        /// <param name="dictionary">
        /// Optional preloaded dictionary instance to serialize.
        /// </param>
        public static void SerializeToJson(
            string path,
            DictionaryMaxlength dictionary = null)
        {
            var instance = dictionary ?? FromDicts();

            File.WriteAllText(
                path,
                JsonSerializer.Serialize(
                    instance,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
        }

        /// <summary>
        /// Regular expression used to detect escaped UTF-16 surrogate pairs
        /// (e.g. <c>\uD841\uDDE3</c>) that represent non-BMP Unicode code points
        /// such as CJK Extension B–H characters.
        /// </summary>
        /// <remarks>
        /// These surrogate pairs are emitted by <see cref="System.Text.Json.JsonSerializer"/>
        /// when serializing supplementary-plane characters under .NET Standard 2.0.
        /// The expression captures the high (<c>\uD8xx</c> / <c>\uDBxx</c>) and low
        /// (<c>\uDCxx</c> / <c>\uDDxx</c>) surrogate components for later reconstruction
        /// into a full Unicode scalar via <see cref="char.ConvertFromUtf32(int)"/>.
        /// </remarks>
        private static readonly Regex SurrogatePairRegex =
            new Regex(@"\\u(?<hi>[dD][89ABab][0-9A-Fa-f]{2})\\u(?<lo>[dD][CDEFcdef][0-9A-Fa-f]{2})",
                RegexOptions.Compiled);

        /// <summary>
        /// Reconstructs actual Unicode characters from escaped UTF-16 surrogate pairs
        /// in a serialized JSON string.
        /// </summary>
        /// <param name="json">
        /// The JSON text that may contain surrogate-pair sequences such as
        /// <c>\uD841\uDDE3</c>.
        /// </param>
        /// <returns>
        /// A new string where all surrogate-pair escapes have been replaced with
        /// their corresponding UTF-8 code points (e.g. <c>𠗣</c>).
        /// </returns>
        /// <remarks>
        /// This method is primarily used by <see cref="SerializeToJsonUnescaped"/> to
        /// restore supplementary-plane characters (U+10000–U+10FFFF) that
        /// <see cref="System.Text.Json"/> would otherwise output as two escaped
        /// 16-bit surrogate values.
        /// </remarks>
        private static string DecodeJsonSurrogatePairs(string json)
        {
            return SurrogatePairRegex.Replace(json, m =>
            {
                var hi = Convert.ToInt32(m.Groups["hi"].Value, 16);
                var lo = Convert.ToInt32(m.Groups["lo"].Value, 16);
                var codepoint = 0x10000 + ((hi - 0xD800) << 10) + (lo - 0xDC00);
                return char.ConvertFromUtf32(codepoint);
            });
        }

        /// <summary>
        /// Serializes a dictionary to a JSON file
        /// without escaping non-ASCII characters.
        /// </summary>
        /// <param name="path">The output file path.</param>
        /// <param name="dictionary">
        /// Optional preloaded dictionary instance to serialize.
        /// </param>
        /// <remarks>
        /// <para>
        /// This method writes human-readable JSON where Chinese, Japanese, Korean, or other
        /// non-ASCII characters appear directly instead of escaped <c>\uXXXX</c> sequences.
        /// </para>
        /// <para>
        /// Because <see cref="System.Text.Json"/> still escapes supplementary-plane characters
        /// (e.g. CJK Extensions B–H) on .NET Standard 2.0, this method additionally invokes
        /// <see cref="DecodeJsonSurrogatePairs"/> to replace surrogate-pair escapes with their
        /// correct Unicode scalars (e.g. <c>\uD841\uDDE3 → 𠗣</c>).
        /// </para>
        /// <para>
        /// The resulting file is written in UTF-8 encoding without a BOM marker.
        /// </para>
        /// </remarks>
        public static void SerializeToJsonUnescaped(
            string path,
            DictionaryMaxlength dictionary = null)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var instance = dictionary ?? FromDicts();

            var json = JsonSerializer.Serialize(instance, options);

            // Convert remaining UTF-16 surrogate escape pairs into readable Unicode
            json = DecodeJsonSurrogatePairs(json);

            File.WriteAllText(
                path,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        /// <summary>
        /// Loads and normalizes a dictionary from a JSON file at the specified path.
        /// </summary>
        /// <param name="path">
        /// Relative path under <see cref="AppContext.BaseDirectory"/> or an absolute
        /// path to the JSON dictionary file.
        /// </param>
        /// <returns>
        /// The deserialized and normalized <see cref="DictionaryMaxlength"/> instance.
        /// </returns>
        public static DictionaryMaxlength DeserializedFromJson(string path)
        {
            return FromJson(path);
        }

        #region FromDicts

        /// <summary>
        /// Maps internal OpenCC dictionary slot names to their default
        /// dictionary text file names.
        ///
        /// <para>
        /// These slot names form the stable internal dictionary contract used by
        /// <see cref="DictionaryMaxlength"/>, <c>DictRefs</c>, starter indexes,
        /// and future acceleration structures such as <c>StarterUnion</c>
        /// and <c>UnionCache</c>.
        /// </para>
        ///
        /// <para>
        /// Custom dictionaries must attach to one of these existing slots through
        /// append or override operations. Arbitrary dynamic slots are intentionally
        /// not supported in order to preserve OpenCC-compatible dictionary topology
        /// and deterministic conversion behavior.
        /// </para>
        /// </summary>
        private static readonly Dictionary<DictSlot, string> SlotFiles =
            new Dictionary<DictSlot, string>
            {
                [DictSlot.STCharacters] = "STCharacters.txt",
                [DictSlot.STPhrases] = "STPhrases.txt",
                [DictSlot.STPunctuations] = "STPunctuations.txt",
                [DictSlot.TSCharacters] = "TSCharacters.txt",
                [DictSlot.TSPhrases] = "TSPhrases.txt",
                [DictSlot.TSPunctuations] = "TSPunctuations.txt",
                [DictSlot.TWPhrases] = "TWPhrases.txt",
                [DictSlot.TWPhrasesRev] = "TWPhrasesRev.txt",
                [DictSlot.TWVariants] = "TWVariants.txt",
                [DictSlot.TWVariantsRev] = "TWVariantsRev.txt",
                [DictSlot.TWVariantsRevPhrases] = "TWVariantsRevPhrases.txt",
                [DictSlot.HKVariants] = "HKVariants.txt",
                [DictSlot.HKVariantsRev] = "HKVariantsRev.txt",
                [DictSlot.HKVariantsRevPhrases] = "HKVariantsRevPhrases.txt",
                [DictSlot.JPSCharacters] = "JPShinjitaiCharacters.txt",
                [DictSlot.JPSPhrases] = "JPShinjitaiPhrases.txt",
                [DictSlot.JPVariants] = "JPVariants.txt",
                [DictSlot.JPVariantsRev] = "JPVariantsRev.txt"
            };

        /// <summary>
        /// Resolves a user-provided dictionary file path into a normalized absolute path.
        ///
        /// <para>
        /// Relative paths are resolved against the current working directory,
        /// allowing command-line tools and applications to load custom dictionaries
        /// from user-controlled locations.
        /// </para>
        ///
        /// <para>
        /// Absolute paths are returned unchanged.
        /// </para>
        ///
        /// <para>
        /// This helper intentionally does not validate file existence. File loading
        /// and exception behavior are handled by the centralized dictionary loading
        /// pipeline.
        /// </para>
        /// </summary>
        /// <param name="path">
        /// User-provided dictionary file path.
        /// </param>
        /// <returns>
        /// A normalized absolute dictionary file path.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// The provided path is null, empty, or whitespace.
        /// </exception>
        private static string ResolveUserPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path must not be null or empty.", nameof(path));

            return Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(path);
        }

        /// <summary>
        /// Retrieves a dictionary slot from a <see cref="DictionaryMaxlength"/>
        /// instance using its stable OpenCC slot name.
        ///
        /// <para>
        /// This helper centralizes slot resolution for append, override,
        /// normalization, and future acceleration workflows.
        /// </para>
        ///
        /// <para>
        /// Slot names form part of the internal OpenCC dictionary contract used by
        /// <c>DictRefs</c>, starter indexes, and future acceleration structures such
        /// as <c>StarterUnion</c> and <c>UnionCache</c>.
        /// </para>
        ///
        /// <para>
        /// Only predefined OpenCC-compatible slots are supported. Arbitrary dynamic
        /// slots are intentionally rejected in order to preserve deterministic
        /// conversion behavior and stable dictionary topology.
        /// </para>
        /// </summary>
        /// <param name="d">
        /// Target <see cref="DictionaryMaxlength"/> instance.
        /// </param>
        /// <param name="slot">
        /// OpenCC dictionary slot name.
        /// </param>
        /// <returns>
        /// The resolved <see cref="DictWithMaxLength"/> dictionary slot.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// The specified slot name is not a supported OpenCC dictionary slot.
        /// </exception>
        private static DictWithMaxLength GetSlot(DictionaryMaxlength d, DictSlot slot)
        {
            switch (slot)
            {
                case DictSlot.STCharacters: return d.st_characters;
                case DictSlot.STPhrases: return d.st_phrases;
                case DictSlot.STPunctuations: return d.st_punctuations;
                case DictSlot.TSCharacters: return d.ts_characters;
                case DictSlot.TSPhrases: return d.ts_phrases;
                case DictSlot.TSPunctuations: return d.ts_punctuations;
                case DictSlot.TWPhrases: return d.tw_phrases;
                case DictSlot.TWPhrasesRev: return d.tw_phrases_rev;
                case DictSlot.TWVariants: return d.tw_variants;
                case DictSlot.TWVariantsRev: return d.tw_variants_rev;
                case DictSlot.TWVariantsRevPhrases: return d.tw_variants_rev_phrases;
                case DictSlot.HKVariants: return d.hk_variants;
                case DictSlot.HKVariantsRev: return d.hk_variants_rev;
                case DictSlot.HKVariantsRevPhrases: return d.hk_variants_rev_phrases;
                case DictSlot.JPSCharacters: return d.jps_characters;
                case DictSlot.JPSPhrases: return d.jps_phrases;
                case DictSlot.JPVariants: return d.jp_variants;
                case DictSlot.JPVariantsRev: return d.jp_variants_rev;
                default:
                    throw new ArgumentException("Unknown dictionary slot: " + slot, nameof(slot));
            }
        }

        /// <summary>
        /// Replaces a dictionary slot inside a <see cref="DictionaryMaxlength"/>
        /// instance using a stable OpenCC slot name.
        ///
        /// <para>
        /// This helper centralizes slot assignment for base dictionary loading,
        /// override operations, normalization workflows, and future acceleration
        /// pipelines.
        /// </para>
        ///
        /// <para>
        /// Slot names form part of the internal OpenCC dictionary contract used by
        /// <c>DictRefs</c>, starter indexes, and future acceleration structures such
        /// as <c>StarterUnion</c> and <c>UnionCache</c>.
        /// </para>
        ///
        /// <para>
        /// Only predefined OpenCC-compatible slots are supported. Arbitrary dynamic
        /// slots are intentionally rejected in order to preserve deterministic
        /// conversion behavior, stable dictionary topology, and consistent metadata
        /// generation.
        /// </para>
        /// </summary>
        /// <param name="d">
        /// Target <see cref="DictionaryMaxlength"/> instance.
        /// </param>
        /// <param name="slot">
        /// OpenCC dictionary slot name.
        /// </param>
        /// <param name="value">
        /// Replacement dictionary value.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The specified slot name is not a supported OpenCC dictionary slot.
        /// </exception>
        private static void SetSlot(DictionaryMaxlength d, DictSlot slot, DictWithMaxLength value)
        {
            switch (slot)
            {
                case DictSlot.STCharacters: d.st_characters = value; break;
                case DictSlot.STPhrases: d.st_phrases = value; break;
                case DictSlot.STPunctuations: d.st_punctuations = value; break;
                case DictSlot.TSCharacters: d.ts_characters = value; break;
                case DictSlot.TSPhrases: d.ts_phrases = value; break;
                case DictSlot.TSPunctuations: d.ts_punctuations = value; break;
                case DictSlot.TWPhrases: d.tw_phrases = value; break;
                case DictSlot.TWPhrasesRev: d.tw_phrases_rev = value; break;
                case DictSlot.TWVariants: d.tw_variants = value; break;
                case DictSlot.TWVariantsRev: d.tw_variants_rev = value; break;
                case DictSlot.TWVariantsRevPhrases: d.tw_variants_rev_phrases = value; break;
                case DictSlot.HKVariants: d.hk_variants = value; break;
                case DictSlot.HKVariantsRev: d.hk_variants_rev = value; break;
                case DictSlot.HKVariantsRevPhrases: d.hk_variants_rev_phrases = value; break;
                case DictSlot.JPSCharacters: d.jps_characters = value; break;
                case DictSlot.JPSPhrases: d.jps_phrases = value; break;
                case DictSlot.JPVariants: d.jp_variants = value; break;
                case DictSlot.JPVariantsRev: d.jp_variants_rev = value; break;
                default:
                    throw new ArgumentException("Unknown dictionary slot: " + slot, nameof(slot));
            }
        }

        /// <summary>
        /// Appends custom dictionary entries into an existing OpenCC dictionary slot.
        ///
        /// <para>
        /// Custom entries are loaded through the centralized dictionary loader and
        /// merged into the target slot using "late-comer wins" behavior, meaning
        /// appended entries override earlier mappings with the same key.
        /// </para>
        ///
        /// <para>
        /// This helper is intended for user terminology, organization-specific
        /// vocabulary, temporary conversion fixes, and domain-specific extensions
        /// while preserving the existing OpenCC dictionary slot topology.
        /// </para>
        ///
        /// <para>
        /// After merging, dictionary metadata is fully rebuilt to ensure that
        /// maximum phrase lengths, starter masks, and derived acceleration metadata
        /// remain consistent for <c>DictRefs</c>, starter indexes, and future
        /// acceleration structures such as <c>StarterUnion</c>
        /// and <c>UnionCache</c>.
        /// </para>
        /// </summary>
        /// <param name="d">
        /// Target <see cref="DictionaryMaxlength"/> instance.
        /// </param>
        /// <param name="slot">
        /// OpenCC dictionary slot name.
        /// </param>
        /// <param name="path">
        /// Path to the custom dictionary text file.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The specified slot name is not a supported OpenCC dictionary slot.
        /// </exception>
        /// <exception cref="FileNotFoundException">
        /// The specified custom dictionary file could not be found.
        /// </exception>
        private static void AppendSlot(DictionaryMaxlength d, DictSlot slot, string path)
        {
            var target = GetSlot(d, slot);
            var extra = LoadFile(path);

            foreach (var kv in extra.Dict)
                target.Dict[kv.Key] = kv.Value; // late-comer wins

            RebuildDictionaryMetadata(target);
        }

        /// <summary>
        /// Fully clears and rebuilds derived metadata for a dictionary slot.
        ///
        /// <para>
        /// This helper is used after mutating an existing dictionary, such as after
        /// appending custom dictionary entries into a loaded OpenCC slot.
        /// </para>
        ///
        /// <para>
        /// Unlike <c>EnsureDictionaryMetadata</c>, this method intentionally resets
        /// existing metadata first. This guarantees that maximum phrase length,
        /// minimum phrase length, length masks, and starter length masks are
        /// recalculated from the final merged dictionary content.
        /// </para>
        ///
        /// <para>
        /// This is important for custom dictionary append mode because newly appended
        /// entries may introduce longer phrases, new starter characters, or new length
        /// buckets. Rebuilding keeps the slot safe for <c>DictRefs</c>, starter indexes,
        /// and future acceleration structures such as <c>StarterUnion</c> and
        /// <c>UnionCache</c>.
        /// </para>
        /// </summary>
        /// <param name="d">
        /// Dictionary slot whose derived metadata should be rebuilt.
        /// </param>
        private static void RebuildDictionaryMetadata(DictWithMaxLength d)
        {
            d.MaxLength = 0;
            d.MinLength = 0;
            d.SetLengthMetadata(0UL, null);
            d.StarterLenMask = null;

            EnsureDictionaryMetadata(d);
        }

        /// <summary>
        /// Loads OpenCC dictionary text files and constructs a fully normalized
        /// <see cref="DictionaryMaxlength"/> instance.
        /// </summary>
        /// <param name="relativeBaseDir">
        /// Directory containing the base OpenCC dictionary text files, resolved under
        /// <see cref="AppContext.BaseDirectory"/>. Defaults to <c>dicts</c>.
        /// </param>
        /// <param name="overrides">
        /// Optional dictionary slot -> file path mapping used to fully replace
        /// specific OpenCC dictionary slots.
        ///
        /// Override files completely replace the corresponding built-in slot.
        /// This mode is intended for advanced users maintaining proprietary or
        /// fully customized OpenCC dictionary copies.
        /// </param>
        /// <param name="appends">
        /// Optional dictionary slot -> file path mapping used to append custom
        /// dictionary entries on top of the built-in dictionaries.
        ///
        /// Appended entries are loaded after the built-in dictionaries and use
        /// "late-comer wins" behavior, meaning duplicate keys override earlier
        /// mappings.
        ///
        /// This mode is recommended for user terms, company terminology,
        /// domain-specific vocabulary, or temporary conversion adjustments.
        /// </param>
        /// <returns>
        /// A fully normalized and metadata-ready
        /// <see cref="DictionaryMaxlength"/> instance.
        /// </returns>
        /// <remarks>
        /// <para>
        /// This method follows the OpenCC dictionary slot structure and does not
        /// support arbitrary dynamic dictionary slots such as <c>user_dict</c>.
        /// Custom dictionaries must attach to existing OpenCC slots such as
        /// <c>st_phrases</c> or <c>ts_phrases</c>.
        /// </para>
        ///
        /// <para>
        /// All dictionaries are parsed through the centralized dictionary loader,
        /// ensuring consistent normalization, maximum phrase length calculation,
        /// and metadata rebuilding across TXT, JSON, CBOR, appended, and overridden
        /// dictionary sources.
        /// </para>
        ///
        /// <para>
        /// All required base dictionary files under the specified directory must
        /// exist. If any required file is missing, this method throws a
        /// <see cref="FileNotFoundException"/> and does not return a partially
        /// initialized <see cref="DictionaryMaxlength"/> instance.
        /// </para>
        ///
        /// <para>
        /// Unknown custom dictionary slots throw an
        /// <see cref="ArgumentException"/> to preserve the internal OpenCC slot
        /// contract used by <c>DictRefs</c>, starter indexes, and future
        /// acceleration structures such as <c>StarterUnion</c> and
        /// <c>UnionCache</c>.
        /// </para>
        /// </remarks>
        /// <exception cref="FileNotFoundException">
        /// One or more required dictionary files could not be found.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// An unknown custom dictionary slot was provided.
        /// </exception>
        public static DictionaryMaxlength FromDicts(
            string relativeBaseDir = "dicts",
            IDictionary<DictSlot, string> overrides = null,
            IDictionary<DictSlot, string> appends = null)
        {
            var baseDir = Path.Combine(AppContext.BaseDirectory, relativeBaseDir);

            var instance = new DictionaryMaxlength();

            foreach (var kv in SlotFiles)
            {
                var slot = kv.Key;
                var file = kv.Value;
                var path = Path.Combine(baseDir, file);

                SetSlot(instance, slot, LoadFile(path));
            }

            if (overrides != null)
            {
                foreach (var kv in overrides)
                {
                    if (!SlotFiles.ContainsKey(kv.Key))
                        throw new ArgumentException("Unknown dictionary slot: " + kv.Key);

                    SetSlot(instance, kv.Key, LoadFile(ResolveUserPath(kv.Value)));
                }
            }

            if (appends == null) return EnsureDerivedMetadata(instance);
            {
                foreach (var kv in appends)
                {
                    if (!SlotFiles.ContainsKey(kv.Key))
                        throw new ArgumentException("Unknown dictionary slot: " + kv.Key);

                    AppendSlot(instance, kv.Key, ResolveUserPath(kv.Value));
                }
            }

            return EnsureDerivedMetadata(instance);
        }

        /// <summary>
        /// Loads a dictionary from a UTF-8-compatible OpenCC text dictionary file.
        ///
        /// Each data line is tab-separated as <c>key[TAB]value</c>. Blank lines and
        /// lines beginning with <c>#</c> are ignored. If the value contains aliases or
        /// comments separated by spaces, only the first value token is used. Duplicate
        /// keys use late-comer wins behavior.
        /// </summary>
        /// <param name="path">The path to the dictionary text file.</param>
        /// <returns>
        /// A <see cref="DictWithMaxLength"/> instance with loaded data and rebuilt
        /// length/starter metadata.
        /// </returns>
        /// <exception cref="FileNotFoundException">
        /// Thrown when the dictionary text file does not exist.
        /// </exception>
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

                // Skip empty lines or comment lines
                if (lineSpan.IsEmpty || lineSpan[0] == '#')
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

                // Only add if both key and value are non-empty after trimming
                if (keySpan.IsEmpty || valueSpan.IsEmpty) continue;

                // Convert ReadOnlySpan<char> to string ONLY when storing in the dictionary
                var key = keySpan.ToString();
                var value = valueSpan.ToString();
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
            BuildStarterLenMask(d); // 👈 slot in

            return d;
        }

        #endregion // FromDicts

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

            var map = new Dictionary<string, ulong>(Math.Min(d.Dict.Count, 1024), StringComparer.Ordinal);

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
        /// Ensures all derived per-dictionary metadata needed by hot lookup paths
        /// exists after deserialization from JSON/CBOR/Zstd.
        /// </summary>
        private static DictionaryMaxlength EnsureDerivedMetadata(DictionaryMaxlength instance)
        {
            if (instance == null)
                throw new InvalidOperationException("Deserialized dictionary instance was null.");

            EnsureDictionaryMetadata(instance.st_characters);
            EnsureDictionaryMetadata(instance.st_phrases);
            EnsureDictionaryMetadata(instance.ts_characters);
            EnsureDictionaryMetadata(instance.ts_phrases);
            EnsureDictionaryMetadata(instance.tw_phrases);
            EnsureDictionaryMetadata(instance.tw_phrases_rev);
            EnsureDictionaryMetadata(instance.tw_variants);
            EnsureDictionaryMetadata(instance.tw_variants_rev);
            EnsureDictionaryMetadata(instance.tw_variants_rev_phrases);
            EnsureDictionaryMetadata(instance.hk_variants);
            EnsureDictionaryMetadata(instance.hk_variants_rev);
            EnsureDictionaryMetadata(instance.hk_variants_rev_phrases);
            EnsureDictionaryMetadata(instance.jps_characters);
            EnsureDictionaryMetadata(instance.jps_phrases);
            EnsureDictionaryMetadata(instance.jp_variants);
            EnsureDictionaryMetadata(instance.jp_variants_rev);
            EnsureDictionaryMetadata(instance.st_punctuations);
            EnsureDictionaryMetadata(instance.ts_punctuations);

            return instance;
        }

        /// <summary>
        /// Ensures derived metadata for a single dictionary exists when it is missing
        /// or incomplete.
        /// </summary>
        private static void EnsureDictionaryMetadata(DictWithMaxLength d)
        {
            if (d == null)
                return;

            var dict = d.Dict;
            if (dict == null || dict.Count == 0)
            {
                d.Dict = dict ?? new Dictionary<string, string>(StringComparer.Ordinal);
                d.MaxLength = 0;
                d.MinLength = 0;
                d.SetLengthMetadata(0UL, null);
                d.StarterLenMask = null;
                return;
            }

            var needsLengthMetadata = d.MaxLength <= 0 || d.MinLength <= 0 || (d.LengthMask == 0UL && d.MaxLength > 0);
            if (needsLengthMetadata)
            {
                var maxLength = 0;
                var minLength = int.MaxValue;
                var lengthMask = 0UL;
                HashSet<int> longLengths = null;

                foreach (var key in dict.Keys)
                {
                    if (string.IsNullOrEmpty(key))
                        continue;

                    var keyLength = key.Length;
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
                }

                d.MaxLength = maxLength;
                d.MinLength = minLength == int.MaxValue ? 0 : minLength;
                d.SetLengthMetadata(lengthMask, longLengths);
            }

            if (d.StarterLenMask == null || d.StarterLenMask.Count == 0)
                BuildStarterLenMask(d);
        }

        /// <summary>
        /// Loads <see cref="DictionaryMaxlength"/> from a CBOR file and ensures
        /// all derived dictionary metadata is present before returning the instance.
        /// </summary>
        /// <param name="relativePath">
        /// Relative path under <see cref="AppContext.BaseDirectory"/> or an absolute
        /// path to the CBOR file.
        /// Default: <c>dicts/dictionary_maxlength.cbor</c>.
        /// </param>
        /// <returns>The hydrated <see cref="DictionaryMaxlength"/> instance.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="relativePath"/> is null or empty.
        /// </exception>
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

            // Normalize older or externally-generated payloads that may not carry
            // all derived lookup metadata required by the hot conversion paths.
            return EnsureDerivedMetadata(instance);
        }

        /// <summary>
        /// Serializes the dictionary to CBOR format and saves it to a file.
        /// </summary>
        /// <param name="path">The output file path.</param>
        /// <param name="dictionary">
        /// Optional preloaded dictionary instance to serialize.
        /// </param>
        public static void SaveCbor(
            string path,
            DictionaryMaxlength dictionary = null)
        {
            var instance = dictionary ?? FromDicts();

            var cbor = CBORObject.FromObject(instance);
            File.WriteAllBytes(path, cbor.EncodeToBytes());
        }

        /// <summary>
        /// Serializes the dictionary to CBOR format and returns the bytes.
        /// </summary>
        /// <param name="dictionary">
        /// Optional preloaded dictionary instance to serialize.
        /// </param>
        /// <returns>CBOR-encoded byte array.</returns>
        public static byte[] ToCborBytes(
            DictionaryMaxlength dictionary = null)
        {
            return CBORObject
                .FromObject(dictionary ?? FromDicts())
                .EncodeToBytes();
        }

        /// <summary>
        /// Serializes the dictionary to JSON, compresses it with Zstd, and saves to a file.
        /// </summary>
        /// <param name="path">The output file path.</param>
        /// <param name="dictionary">
        /// Optional preloaded dictionary instance to serialize.
        /// </param>
        public static void SaveJsonCompressed(
            string path,
            DictionaryMaxlength dictionary = null)
        {
            var instance = dictionary ?? FromDicts();

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(instance);

            using (var compressor = new Compressor(19))
            {
                var compressed = compressor.Wrap(jsonBytes);
                File.WriteAllBytes(path, compressed.ToArray());
            }
        }

        /// <summary>
        /// Loads and normalizes the dictionary from a Zstd-compressed JSON file.
        /// </summary>
        /// <param name="path">The path to the compressed file.</param>
        /// <returns>The deserialized <see cref="DictionaryMaxlength"/> instance.</returns>
        public static DictionaryMaxlength LoadJsonCompressed(string path)
        {
            var compressed = File.ReadAllBytes(path);

            using (var decompressor = new Decompressor())
            {
                var jsonBytes = decompressor.Unwrap(compressed);
                var instance = JsonSerializer.Deserialize<DictionaryMaxlength>(jsonBytes);
                return EnsureDerivedMetadata(instance);
            }
        }
    }
}