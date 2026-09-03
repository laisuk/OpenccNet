using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Formats.Cbor;
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
        public Dictionary<string, string> Dict { get; set; } = new(StringComparer.Ordinal);

        /// <summary>
        /// The maximum length, in UTF-16 code units, of any key in the dictionary.
        /// Used for optimizing longest-match lookups.
        /// </summary>
        [JsonInclude]
        public int MaxLength { get; set; }

        /// <summary>
        /// The minimum length, in UTF-16 code units, of any key in the dictionary.
        /// Used for optimizing longest-match lookups.
        /// </summary>
        [JsonInclude]
        public int MinLength { get; set; }

        /// <summary>
        /// Bitmask tracking which key lengths (1..64 UTF-16 code units) exist in <see cref="Dict"/>.
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

#if NET9_0_OR_GREATER
        /// <summary>
        /// Attempts to get a value without materializing the span as a string.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetValue(ReadOnlySpan<char> key, out string value)
        {
            if (Dict.TryGetAlternateLookup<ReadOnlySpan<char>>(out var lookup))
                return lookup.TryGetValue(key, out value);

            // Preserve support for callers that assign a custom comparer which
            // does not implement alternate span equality.
            return Dict.TryGetValue(key.ToString(), out value);
        }
#endif

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
        public DictWithMaxLength st_characters { get; set; } = new();

        /// <summary>
        /// Simplified-to-Traditional phrase mappings.
        /// </summary>
        public DictWithMaxLength st_phrases { get; set; } = new();

        /// <summary>
        /// Traditional-to-Simplified character mappings.
        /// </summary>
        public DictWithMaxLength ts_characters { get; set; } = new();

        /// <summary>
        /// Traditional-to-Simplified phrase mappings.
        /// </summary>
        public DictWithMaxLength ts_phrases { get; set; } = new();

        /// <summary>
        /// Traditional-to-Taiwan phrase mappings.
        /// </summary>
        public DictWithMaxLength tw_phrases { get; set; } = new();

        /// <summary>
        /// Taiwan-to-Traditional phrase mappings.
        /// </summary>
        public DictWithMaxLength tw_phrases_rev { get; set; } = new();

        /// <summary>
        /// Traditional-to-Taiwan character variant mappings.
        /// </summary>
        public DictWithMaxLength tw_variants { get; set; } = new();

        /// <summary>
        /// Traditional-to-Taiwan phrase variant mappings applied before character variants.
        /// </summary>
        public DictWithMaxLength tw_variants_phrases { get; set; } = new();

        /// <summary>
        /// Taiwan-to-Traditional character variant mappings.
        /// </summary>
        public DictWithMaxLength tw_variants_rev { get; set; } = new();

        /// <summary>
        /// Taiwan-to-Traditional phrase variant mappings.
        /// </summary>
        public DictWithMaxLength tw_variants_rev_phrases { get; set; } = new();

        /// <summary>
        /// Traditional-to-Hong Kong phrase mappings.
        /// </summary>
        public DictWithMaxLength hk_phrases { get; set; } = new();

        /// <summary>
        /// Hong Kong-to-Traditional phrase mappings.
        /// </summary>
        public DictWithMaxLength hk_phrases_rev { get; set; } = new();

        /// <summary>
        /// Traditional-to-Hong Kong character variant mappings.
        /// </summary>
        public DictWithMaxLength hk_variants { get; set; } = new();

        /// <summary>
        /// Traditional-to-Hong Kong phrase variant mappings applied before character variants.
        /// </summary>
        public DictWithMaxLength hk_variants_phrases { get; set; } = new();

        /// <summary>
        /// Hong Kong-to-Traditional character variant mappings.
        /// </summary>
        public DictWithMaxLength hk_variants_rev { get; set; } = new();

        /// <summary>
        /// Hong Kong-to-Traditional phrase variant mappings.
        /// </summary>
        public DictWithMaxLength hk_variants_rev_phrases { get; set; } = new();

        /// <summary>
        /// Japanese Shinjitai-to-Traditional Kyujitai character mappings.
        /// </summary>
        public DictWithMaxLength jps_characters { get; set; } = new();

        /// <summary>
        /// Traditional Kyujitai-to-Japanese Shinjitai character mappings.
        /// </summary>
        public DictWithMaxLength jps_characters_rev { get; set; } = new();

        /// <summary>
        /// Japanese Shinjitai-to-Traditional Kyujitai phrase mappings.
        /// </summary>
        public DictWithMaxLength jps_phrases { get; set; } = new();

        /// <summary>
        /// Simplified-to-Traditional punctuation mappings.
        /// </summary>
        public DictWithMaxLength st_punctuations { get; set; } = new();

        /// <summary>
        /// Traditional-to-Simplified punctuation mappings.
        /// </summary>
        public DictWithMaxLength ts_punctuations { get; set; } = new();
    }
    // ReSharper restore InconsistentNaming

    /// <summary>
    /// Provides centralized access to the built-in default dictionary plus
    /// dictionary loading, customization, and serialization helpers.
    /// </summary>
    /// <remarks>
    /// The public <see cref="Provider"/> always represents the built-in default
    /// dictionary singleton. Runtime active-provider ownership and conversion-plan
    /// cache state are internal implementation details owned by
    /// <see cref="ConversionPlanCache"/>.
    /// </remarks>
    public static class DictionaryLib
    {
        private const string BuiltInDictionaryResourceName =
            "OpenccNetLib.Resources.dictionary_maxlength.zstd";

        private static readonly DictionaryJsonContext IndentedJsonContext =
            new(
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        private static readonly DictionaryJsonContext IndentedUnescapedJsonContext =
            new(
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

        // --------------------------------------------------------------------------------
        // Lazy loader for the default dictionary
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Lazily initializes the default <see cref="DictionaryMaxlength"/> instance  
        /// used by all conversions that do not explicitly specify a custom dictionary set.  
        /// 
        /// This loads the embedded Zstandard-compressed dictionary resource on first
        /// access. The initialization is thread-safe and
        /// performed only once per process lifetime.
        /// </summary>
        private static readonly Lazy<DictionaryMaxlength> DefaultLib = new(LoadBuiltInDictionary, isThreadSafe: true);

        // --------------------------------------------------------------------------------
        // Public accessors and provider management
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Gets the built-in default singleton <see cref="DictionaryMaxlength"/> instance.
        /// This property always returns the same object reference.
        /// </summary>
        /// <remarks>
        /// The dictionary is lazily initialized from a Zstandard-compressed resource
        /// embedded in the OpenccNetLib assembly. It does not require an external dictionary
        /// file and is safe for concurrent read access from multiple threads.
        /// <para>
        /// To obtain a new, independent dictionary instance (e.g., when reloading from
        /// an external file), use <see cref="FromZstd(string)"/> or other loader methods directly.
        /// </para>
        /// </remarks>
        /// <returns>
        /// The built-in default <see cref="DictionaryMaxlength"/> instance.
        /// </returns>
        public static DictionaryMaxlength Provider => DefaultLib.Value;

        /// <summary>
        /// Returns the default singleton dictionary instance and resets the active
        /// planning provider to use the built-in dictionary.
        /// </summary>
        /// <remarks>
        /// This method is retained for backward compatibility and acts as a convenience
        /// wrapper around <see cref="Provider"/>.
        /// <para>
        /// In addition to returning the default singleton dictionary, this method
        /// reconfigures the global planning source to use the built-in dictionary provider
        /// and clears all cached conversion plans.
        /// </para>
        /// <para>
        /// No new dictionary instance is created or allocated.
        /// To explicitly create a separate dictionary instance, use
        /// <see cref="FromZstd(string)"/> or other loader methods.
        /// </para>
        /// </remarks>
        /// <returns>
        /// The default singleton <see cref="DictionaryMaxlength"/> instance shared across conversions.
        /// </returns>
        public static DictionaryMaxlength New()
        {
            ConversionPlanCache.ResetProvider();
            return Provider;
        }

        /// <summary>
        /// Loads the canonical built-in dictionary from this assembly's embedded resource.
        /// </summary>
        private static DictionaryMaxlength LoadBuiltInDictionary()
        {
            var stream = typeof(DictionaryLib).Assembly
                .GetManifestResourceStream(BuiltInDictionaryResourceName);

            if (stream == null)
            {
                throw new InvalidOperationException(
                    "Embedded dictionary resource was not found: " +
                    BuiltInDictionaryResourceName);
            }

            using (stream)
            {
                return DeserializeZstd(stream);
            }
        }

        /// <summary>
        /// Loads an independent dictionary from an external Zstandard-compressed JSON file.
        /// </summary>
        /// <param name="relativePath">
        /// Relative path under <see cref="AppContext.BaseDirectory"/> or an absolute
        /// path to the Zstandard-compressed JSON dictionary file.
        /// </param>
        /// <returns>
        /// A new, independent, deserialized and normalized
        /// <see cref="DictionaryMaxlength"/> instance.
        /// </returns>
        /// <remarks>
        /// This filesystem loader is separate from <see cref="Provider"/>, which loads the
        /// built-in dictionary from an embedded assembly resource. Callers must provide the
        /// path to an external dictionary file explicitly.
        /// <para>
        /// Unlike <see cref="New"/>, this method does not change the active
        /// global dictionary provider or clear cached conversion plans.
        /// Use <see cref="FromZstdBytes(byte[])"/> to load compressed data already in memory.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="relativePath"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="relativePath"/> is empty or whitespace.
        /// </exception>
        /// <exception cref="FileNotFoundException">
        /// Thrown when the Zstandard dictionary file does not exist.
        /// </exception>
        public static DictionaryMaxlength FromZstd(string relativePath)
        {
            if (relativePath == null)
                throw new ArgumentNullException(nameof(relativePath));

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

            using var inputStream = File.OpenRead(fullPath);
            return DeserializeZstd(inputStream);
        }

        /// <summary>
        /// Loads an independent dictionary from caller-provided Zstandard-compressed JSON data.
        /// </summary>
        /// <param name="data">
        /// Zstandard-compressed JSON dictionary data.
        /// </param>
        /// <returns>
        /// A new, independent, deserialized and normalized
        /// <see cref="DictionaryMaxlength"/> instance.
        /// </returns>
        /// <remarks>
        /// This in-memory loader is separate from <see cref="Provider"/>, which loads the
        /// built-in dictionary from an embedded assembly resource.
        /// <para>
        /// Unlike <see cref="New"/>, this method does not change the active
        /// global dictionary provider or clear cached conversion plans.
        /// Use <see cref="FromZstd(string)"/> to load from a file path.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="data"/> is <see langword="null"/>.
        /// </exception>
        public static DictionaryMaxlength FromZstdBytes(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            using var inputStream = new MemoryStream(data, writable: false);
            return DeserializeZstd(inputStream);
        }

        /// <summary>
        /// Decompresses, deserializes, and normalizes a Zstandard dictionary stream.
        /// </summary>
        private static DictionaryMaxlength DeserializeZstd(Stream compressedStream)
        {
            if (compressedStream == null)
                throw new ArgumentNullException(nameof(compressedStream));

            using var decompressionStream = new DecompressionStream(compressedStream);
            var instance =
                JsonSerializer.Deserialize(
                    decompressionStream,
                    DictionaryJsonContext.Default.DictionaryMaxlength);

            return EnsureDerivedMetadata(instance);
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
        /// Production applications should prefer the default embedded Zstd dictionary
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

            using var stream = File.OpenRead(fullPath);
            var instance =
                JsonSerializer.Deserialize(
                    stream,
                    DictionaryJsonContext.Default.DictionaryMaxlength);

            return EnsureDerivedMetadata(instance);
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
                    IndentedJsonContext.DictionaryMaxlength));
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
            new(@"\\u(?<hi>[dD][89ABab][0-9A-Fa-f]{2})\\u(?<lo>[dD][CDEFcdef][0-9A-Fa-f]{2})",
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
            var instance = dictionary ?? FromDicts();

            var json = JsonSerializer.Serialize(
                instance,
                IndentedUnescapedJsonContext.DictionaryMaxlength);

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

        /// <summary>
        /// Deserializes an in-memory JSON dictionary using source-generated metadata
        /// and restores all derived lookup metadata.
        /// </summary>
        internal static DictionaryMaxlength DeserializeJson(string json)
        {
            var instance = JsonSerializer.Deserialize(
                json,
                DictionaryJsonContext.Default.DictionaryMaxlength);

            return EnsureDerivedMetadata(instance);
        }

        #region FromDicts

        /// <summary>
        /// Maps internal OpenCC dictionary slot names to their default
        /// dictionary text file names.
        ///
        /// <para>
        /// These slot names form the stable internal dictionary contract used by
        /// <see cref="DictionaryMaxlength"/>, <c>DictRefs</c>, starter indexes,
        /// and the starter-union caches owned by <see cref="ConversionPlanCache"/>.
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
            new()
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
                [DictSlot.TWVariantsPhrases] = "TWVariantsPhrases.txt",
                [DictSlot.TWVariantsRev] = "TWVariantsRev.txt",
                [DictSlot.TWVariantsRevPhrases] = "TWVariantsRevPhrases.txt",

                [DictSlot.HKPhrases] = "HKPhrases.txt",
                [DictSlot.HKPhrasesRev] = "HKPhrasesRev.txt",
                [DictSlot.HKVariants] = "HKVariants.txt",
                [DictSlot.HKVariantsPhrases] = "HKVariantsPhrases.txt",
                [DictSlot.HKVariantsRev] = "HKVariantsRev.txt",
                [DictSlot.HKVariantsRevPhrases] = "HKVariantsRevPhrases.txt",

                [DictSlot.JPSCharacters] = "JPShinjitaiCharacters.txt",
                [DictSlot.JPSCharactersRev] = "JPShinjitaiCharactersRev.txt",
                [DictSlot.JPSPhrases] = "JPShinjitaiPhrases.txt"
            };

        /// <summary>
        /// Resolves a user-provided dictionary path into a normalized absolute path.
        ///
        /// <para>
        /// Relative paths are resolved against <see cref="AppContext.BaseDirectory"/>,
        /// matching the built-in dictionary loading behavior.
        /// </para>
        ///
        /// <para>
        /// Absolute paths are normalized with <see cref="Path.GetFullPath(string)"/>.
        /// </para>
        ///
        /// <para>
        /// This helper intentionally does not validate file existence. File loading
        /// and exception behavior are handled by the centralized dictionary loading
        /// pipeline.
        /// </para>
        /// </summary>
        /// <param name="path">
        /// User-provided dictionary file or directory path.
        /// </param>
        /// <returns>
        /// A normalized absolute dictionary path.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// The provided path is null, empty, or whitespace.
        /// </exception>
        private static string ResolveUserPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException(
                    "Path must not be null or empty.",
                    nameof(path));

            path = path.Trim();

            path = path.Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar);

            return Path.GetFullPath(
                Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(AppContext.BaseDirectory, path));
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
        /// <c>DictRefs</c>, starter indexes, and the starter-union caches owned by
        /// <see cref="ConversionPlanCache"/>.
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
                case DictSlot.TWVariantsPhrases: return d.tw_variants_phrases;
                case DictSlot.TWVariantsRev: return d.tw_variants_rev;
                case DictSlot.TWVariantsRevPhrases: return d.tw_variants_rev_phrases;

                case DictSlot.HKPhrases: return d.hk_phrases;
                case DictSlot.HKPhrasesRev: return d.hk_phrases_rev;
                case DictSlot.HKVariants: return d.hk_variants;
                case DictSlot.HKVariantsPhrases: return d.hk_variants_phrases;
                case DictSlot.HKVariantsRev: return d.hk_variants_rev;
                case DictSlot.HKVariantsRevPhrases: return d.hk_variants_rev_phrases;

                case DictSlot.JPSCharacters: return d.jps_characters;
                case DictSlot.JPSCharactersRev: return d.jps_characters_rev;
                case DictSlot.JPSPhrases: return d.jps_phrases;

                default:
                    throw new ArgumentException(
                        "Unknown dictionary slot: " + slot,
                        nameof(slot));
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
        /// <c>DictRefs</c>, starter indexes, and the starter-union caches owned by
        /// <see cref="ConversionPlanCache"/>.
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
                case DictSlot.TWVariantsPhrases: d.tw_variants_phrases = value; break;
                case DictSlot.TWVariantsRev: d.tw_variants_rev = value; break;
                case DictSlot.TWVariantsRevPhrases: d.tw_variants_rev_phrases = value; break;

                case DictSlot.HKPhrases: d.hk_phrases = value; break;
                case DictSlot.HKPhrasesRev: d.hk_phrases_rev = value; break;
                case DictSlot.HKVariants: d.hk_variants = value; break;
                case DictSlot.HKVariantsPhrases: d.hk_variants_phrases = value; break;
                case DictSlot.HKVariantsRev: d.hk_variants_rev = value; break;
                case DictSlot.HKVariantsRevPhrases: d.hk_variants_rev_phrases = value; break;

                case DictSlot.JPSCharacters: d.jps_characters = value; break;
                case DictSlot.JPSCharactersRev: d.jps_characters_rev = value; break;
                case DictSlot.JPSPhrases: d.jps_phrases = value; break;

                default:
                    throw new ArgumentException(
                        "Unknown dictionary slot: " + slot,
                        nameof(slot));
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
        /// remain consistent for <c>DictRefs</c>, starter indexes, and the
        /// starter-union caches owned by <see cref="ConversionPlanCache"/>.
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
        /// and the starter-union caches owned by <see cref="ConversionPlanCache"/>.
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
        /// contract used by <c>DictRefs</c>, starter indexes, and the starter-union
        /// caches owned by <see cref="ConversionPlanCache"/>.
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
            var baseDir = ResolveUserPath(relativeBaseDir);

            var instance = new DictionaryMaxlength();

            foreach (var slot in DictSlotExtensions.ActiveSlots)
            {
                var file = SlotFiles[slot];
                var path = Path.Combine(baseDir, file);

                SetSlot(instance, slot, LoadFile(path));
            }

            if (overrides != null)
            {
                foreach (var kv in overrides)
                {
                    if (!kv.Key.IsActive())
                        throw new ArgumentException("Unknown dictionary slot: " + kv.Key);

                    SetSlot(instance, kv.Key, LoadFile(ResolveUserPath(kv.Value)));
                }
            }

            if (appends == null)
                return EnsureDerivedMetadata(instance);

            foreach (var kv in appends)
            {
                if (!kv.Key.IsActive())
                    throw new ArgumentException("Unknown dictionary slot: " + kv.Key);

                AppendSlot(instance, kv.Key, ResolveUserPath(kv.Value));
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
                // Match Rust behavior: remove trailing whitespace only.
                // Leading whitespace is dictionary data and must be preserved.
                var lineSpan = line.AsSpan().TrimEnd();

                // Skip empty lines or comment lines
                if (lineSpan.IsEmpty || lineSpan[0] == '#')
                {
                    continue;
                }

                // Find the index of the first tab character
                var tabIndex = lineSpan.IndexOf('\t');

                if (tabIndex == -1) continue;

                // IMPORTANT: do not trim keySpan.
                var keySpan = lineSpan.Slice(0, tabIndex);
                var valueFullSpan = lineSpan.Slice(tabIndex + 1);

                // Match Rust split_whitespace().next():
                // but keep leading whitespace in the value as member of the first token.
                var valueEnd = 0;
                var seenNonWhitespace = false;

                while (valueEnd < valueFullSpan.Length)
                {
                    var ch = valueFullSpan[valueEnd];

                    if (char.IsWhiteSpace(ch))
                    {
                        if (seenNonWhitespace)
                            break;
                    }
                    else
                    {
                        seenNonWhitespace = true;
                    }

                    valueEnd++;
                }

                var valueSpan = valueFullSpan.Slice(0, valueEnd);

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
                    longLengths ??= new HashSet<int>();

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

        #region Post Load Custom Dictionary

        /// <summary>
        /// Applies post-load custom dictionary modifications to an existing
        /// <see cref="DictionaryMaxlength"/> instance.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method allows additional custom dictionary files and/or in-memory
        /// dictionary pairs to be appended to or override specific OpenCC dictionary
        /// slots after the base dictionary has already been loaded.
        /// </para>
        ///
        /// <para>
        /// Unlike <see cref="FromDicts"/>, which injects custom dictionaries during
        /// initial dictionary construction, this method operates on an already-loaded
        /// dictionary instance from any provider, including:
        /// </para>
        ///
        /// <list type="bullet">
        /// <item><description>default built-in dictionaries</description></item>
        /// <item><description>Zstd dictionaries</description></item>
        /// <item><description>CBOR dictionaries</description></item>
        /// <item><description>JSON dictionaries</description></item>
        /// <item><description>pure file-based dictionaries created through <see cref="FromDicts"/></description></item>
        /// </list>
        ///
        /// <para>
        /// Custom dictionary specifications are applied sequentially in enumeration
        /// order. Within a single <see cref="CustomDictSpec"/>:
        /// </para>
        ///
        /// <list type="number">
        /// <item><description>Dictionary files are applied in array order.</description></item>
        /// <item><description>In-memory pairs are applied after files.</description></item>
        /// <item><description>Later entries overwrite earlier duplicate keys.</description></item>
        /// </list>
        ///
        /// <para>
        /// In <see cref="CustomDictMode.Override"/> mode, the final merged result
        /// replaces the entire target slot.
        /// </para>
        ///
        /// <para>
        /// The supplied <paramref name="dict"/> instance is modified in place and is
        /// also returned for convenience.
        /// </para>
        /// 
        /// <para>
        /// Dictionary metadata such as maximum phrase lengths and starter lookup
        /// caches are automatically rebuilt after customization.
        /// </para>
        /// </remarks>
        /// <param name="dict">
        /// Target dictionary instance to customize.
        /// </param>
        /// <param name="specs">
        /// Custom dictionary specifications describing which slots to modify
        /// and how custom entries should be applied.
        /// </param>
        /// <returns>
        /// The same <see cref="DictionaryMaxlength"/> instance after customization.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="dict"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// A custom dictionary specification is invalid, contains no dictionary
        /// source, or references an unknown dictionary slot.
        /// </exception>
        public static DictionaryMaxlength WithCustomDicts(
            DictionaryMaxlength dict,
            IEnumerable<CustomDictSpec> specs)
        {
            if (dict == null)
                throw new ArgumentNullException(nameof(dict));

            if (specs == null)
                return EnsureDerivedMetadata(dict);

            foreach (var spec in specs)
            {
                if (spec == null)
                    continue;

                ApplyCustomDictSpec(dict, spec);
            }

            return EnsureDerivedMetadata(dict);
        }

        /// <summary>
        /// Applies a single <see cref="CustomDictSpec"/> to a target
        /// <see cref="DictionaryMaxlength"/> instance.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the internal core implementation behind
        /// <see cref="WithCustomDicts"/>.
        /// </para>
        ///
        /// <para>
        /// Depending on <see cref="CustomDictMode"/>, custom dictionary entries
        /// are either appended to the existing slot or used to replace the entire
        /// slot.
        /// </para>
        ///
        /// <para>
        /// When both dictionary files and in-memory pairs are provided:
        /// </para>
        ///
        /// <list type="number">
        /// <item><description>Files are loaded and applied in array order.</description></item>
        /// <item><description>Pairs are applied after files.</description></item>
        /// <item><description>Later entries overwrite earlier duplicate keys.</description></item>
        /// </list>
        ///
        /// <para>
        /// In <see cref="CustomDictMode.Override"/> mode, a temporary slot is built,
        /// fully populated, and then atomically replaces the original slot after
        /// metadata reconstruction completes.
        /// </para>
        ///
        /// <para>
        /// Slot metadata such as maximum phrase lengths and starter lookup caches
        /// are rebuilt automatically before the slot becomes visible to callers.
        /// </para>
        /// </remarks>
        /// <param name="dict">
        /// Target dictionary instance to modify.
        /// </param>
        /// <param name="spec">
        /// Custom dictionary specification describing the target slot,
        /// dictionary sources, and merge behavior.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The specification references an unknown slot, contains no dictionary
        /// source, or contains an invalid dictionary path.
        /// </exception>
        private static void ApplyCustomDictSpec(
            DictionaryMaxlength dict,
            CustomDictSpec spec)
        {
            if (!spec.Slot.IsActive())
                throw new ArgumentException("Unknown dictionary slot: " + spec.Slot, nameof(spec));

            var hasPaths = spec.Paths is { Length: > 0 };
            var hasPairs = spec.Pairs is { Count: > 0 };

            if (!hasPaths && !hasPairs)
                throw new ArgumentException(
                    "CustomDictSpec must provide at least one dictionary source: Paths or Pairs.",
                    nameof(spec));

            var target = spec.Mode == CustomDictMode.Override
                ? new DictWithMaxLength { Dict = new Dictionary<string, string>(StringComparer.Ordinal) }
                : GetSlot(dict, spec.Slot);

            if (hasPaths)
            {
                foreach (var path in spec.Paths)
                {
                    if (string.IsNullOrWhiteSpace(path))
                        throw new ArgumentException("Custom dictionary path must not be null or empty.", nameof(spec));

                    var extra = LoadFile(ResolveUserPath(path));

                    foreach (var kv in extra.Dict)
                        target.Dict[kv.Key] = kv.Value;
                }
            }

            if (hasPairs)
            {
                foreach (var kv in spec.Pairs)
                {
                    if (string.IsNullOrEmpty(kv.Key))
                        continue;

                    target.Dict[kv.Key] = kv.Value ?? string.Empty;
                }
            }

            RebuildDictionaryMetadata(target);

            if (spec.Mode == CustomDictMode.Override)
                SetSlot(dict, spec.Slot, target);
        }

        #endregion // Post Load Custom Dictionary

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

            instance.tw_variants_phrases ??= new DictWithMaxLength();

            instance.hk_variants_phrases ??= new DictWithMaxLength();

            instance.hk_phrases ??= new DictWithMaxLength();

            instance.hk_phrases_rev ??= new DictWithMaxLength();

            instance.jps_characters_rev ??= new DictWithMaxLength();

            EnsureDictionaryMetadata(instance.st_characters);
            EnsureDictionaryMetadata(instance.st_phrases);
            EnsureDictionaryMetadata(instance.st_punctuations);

            EnsureDictionaryMetadata(instance.ts_characters);
            EnsureDictionaryMetadata(instance.ts_phrases);
            EnsureDictionaryMetadata(instance.ts_punctuations);

            EnsureDictionaryMetadata(instance.tw_phrases);
            EnsureDictionaryMetadata(instance.tw_phrases_rev);
            EnsureDictionaryMetadata(instance.tw_variants);
            EnsureDictionaryMetadata(instance.tw_variants_phrases);
            EnsureDictionaryMetadata(instance.tw_variants_rev);
            EnsureDictionaryMetadata(instance.tw_variants_rev_phrases);

            EnsureDictionaryMetadata(instance.hk_phrases);
            EnsureDictionaryMetadata(instance.hk_phrases_rev);
            EnsureDictionaryMetadata(instance.hk_variants);
            EnsureDictionaryMetadata(instance.hk_variants_phrases);
            EnsureDictionaryMetadata(instance.hk_variants_rev);
            EnsureDictionaryMetadata(instance.hk_variants_rev_phrases);

            EnsureDictionaryMetadata(instance.jps_characters);
            EnsureDictionaryMetadata(instance.jps_characters_rev);
            EnsureDictionaryMetadata(instance.jps_phrases);

            EnsureRequiredDictionarySlots(instance);

            return instance;
        }

        /// <summary>
        /// Verifies schema-breaking required dictionary slots are present after
        /// hydration from bundled JSON/CBOR/Zstd dictionary packs.
        /// </summary>
        private static void EnsureRequiredDictionarySlots(DictionaryMaxlength instance)
        {
            if (instance.jps_characters_rev.Dict == null ||
                instance.jps_characters_rev.Dict.Count == 0)
                throw new InvalidOperationException(
                    "Required dictionary slot 'jps_characters_rev' is missing or empty. " +
                    "Regenerate dictionary_maxlength assets or include JPShinjitaiCharactersRev.txt.");
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
                        longLengths ??= new HashSet<int>();

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

        #region CBOR Serialization

        /// <summary>
        /// Number of dictionary slots written to the top-level CBOR map.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The CBOR schema intentionally mirrors the public snake_case properties on
        /// <see cref="DictionaryMaxlength"/>. Keeping this count explicit makes the
        /// writer emit a definite-length map, which is compact and avoids buffering or
        /// reflection-based object discovery.
        /// </para>
        /// <para>
        /// When a new persistent dictionary slot is added to <see cref="DictionaryMaxlength"/>,
        /// update this value together with <see cref="WriteDictionaryMaxlengthCbor"/> and
        /// <see cref="ReadDictionaryMaxlengthCbor"/>.
        /// </para>
        /// </remarks>
        private const int CborDictionarySlotCount = 21;

        /// <summary>
        /// Number of fields persisted for each <see cref="DictWithMaxLength"/> CBOR object.
        /// </summary>
        /// <remarks>
        /// The six fields deliberately include the precomputed lookup metadata rather
        /// than serializing only <see cref="DictWithMaxLength.Dict"/>. In particular,
        /// <see cref="DictWithMaxLength.StarterLenMask"/> can be expensive to rebuild for
        /// large phrase dictionaries because reconstruction requires scanning every key.
        /// Persisting the metadata moves that work to dictionary-build time and keeps
        /// normal application startup on the fast hydration path.
        /// </remarks>
        private const int CborSlotFieldCount = 6;

        /// <summary>
        /// Loads a <see cref="DictionaryMaxlength"/> instance from a CBOR file using
        /// an explicit, reflection-free CBOR reader.
        /// </summary>
        /// <param name="relativePath">
        /// Relative path under <see cref="AppContext.BaseDirectory"/> or an absolute
        /// path to the CBOR file. Defaults to
        /// <c>dicts/dictionary_maxlength.cbor</c>.
        /// </param>
        /// <returns>
        /// A hydrated <see cref="DictionaryMaxlength"/> whose persisted acceleration
        /// metadata is reused when present and rebuilt only when missing or incomplete.
        /// </returns>
        /// <remarks>
        /// <para>
        /// This implementation intentionally does not use a CLR-object mapper. The CBOR
        /// structure is decoded field-by-field with <see cref="CborReader"/>, which keeps
        /// the path deterministic and NativeAOT/trimming friendly.
        /// </para>
        /// <para>
        /// The reader accepts the legacy camelCase field names emitted by
        /// PeterO.Cbor (<c>dict</c>, <c>maxLength</c>, <c>minLength</c>,
        /// <c>lengthMask</c>, <c>longLengths</c>, and <c>starterLenMask</c>) as
        /// well as the corresponding PascalCase forms. Unknown fields are skipped
        /// so newer dictionary packs can remain forward-compatible with older readers.
        /// </para>
        /// <para>
        /// After decoding, <see cref="EnsureDerivedMetadata"/> remains the compatibility
        /// safety net for older or externally generated payloads that omit one or more
        /// derived fields. Normal generated CBOR dictionaries should already contain all
        /// metadata and therefore avoid rebuilding it at runtime.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="relativePath"/> is null, empty, or whitespace.
        /// </exception>
        /// <exception cref="FileNotFoundException">
        /// Thrown when the CBOR dictionary file cannot be found.
        /// </exception>
        /// <exception cref="CborContentException">
        /// Thrown when the file contains malformed CBOR or an unexpected value type for
        /// a known schema field.
        /// </exception>
        /// <exception cref="InvalidDataException">
        /// Thrown when the root CBOR value is not a map or trailing data remains after
        /// the dictionary payload.
        /// </exception>
        /// <exception cref="IOException">
        /// Thrown when the file cannot be read.
        /// </exception>
        public static DictionaryMaxlength FromCbor(
            string relativePath = "dicts/dictionary_maxlength.cbor")
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
                throw new FileNotFoundException("CBOR dictionary file not found.", fullPath);

            var bytes = File.ReadAllBytes(fullPath);
            return DeserializeCbor(bytes);
        }

        /// <summary>
        /// Serializes a <see cref="DictionaryMaxlength"/> instance to CBOR and saves
        /// the encoded bytes to a file.
        /// </summary>
        /// <param name="path">Destination CBOR file path.</param>
        /// <param name="dictionary">
        /// Optional preloaded dictionary instance. When <see langword="null"/>, the
        /// dictionary is built from the default OpenCC text dictionary sources via
        /// <see cref="FromDicts"/>.
        /// </param>
        /// <remarks>
        /// Serialization is performed explicitly with <see cref="CborWriter"/>. The
        /// persisted schema includes all derived lookup metadata so applications can
        /// hydrate the hot-path structures directly without rescanning dictionary keys.
        /// </remarks>
        public static void SaveCbor(
            string path,
            DictionaryMaxlength dictionary = null)
        {
            File.WriteAllBytes(path, ToCborBytes(dictionary));
        }

        /// <summary>
        /// Serializes a <see cref="DictionaryMaxlength"/> instance to CBOR and returns
        /// the encoded bytes.
        /// </summary>
        /// <param name="dictionary">
        /// Optional preloaded dictionary instance. When <see langword="null"/>, the
        /// dictionary is built from the default OpenCC text dictionary sources via
        /// <see cref="FromDicts"/>.
        /// </param>
        /// <returns>
        /// A CBOR payload containing all OpenCC dictionary slots and their precomputed
        /// length/starter metadata.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The writer uses definite-length maps and arrays whenever their sizes are
        /// known. No reflection, runtime property enumeration, or generic POCO mapping
        /// is involved.
        /// </para>
        /// <para>
        /// Field names intentionally match the historical CBOR object schema so that
        /// existing dictionary packs remain readable by the new implementation and the
        /// serialized contract stays aligned with the JSON representation.
        /// </para>
        /// </remarks>
        public static byte[] ToCborBytes(
            DictionaryMaxlength dictionary = null)
        {
            var instance = dictionary ?? FromDicts();
            var writer = new CborWriter(CborConformanceMode.Lax);

            WriteDictionaryMaxlengthCbor(writer, instance);
            return writer.Encode();
        }

        /// <summary>
        /// Deserializes a CBOR payload into <see cref="DictionaryMaxlength"/> and
        /// restores any missing derived metadata.
        /// </summary>
        /// <param name="data">Complete CBOR dictionary payload.</param>
        /// <returns>A hydrated and metadata-ready dictionary instance.</returns>
        /// <remarks>
        /// <para>
        /// Lax conformance mode is used deliberately for compatibility with existing
        /// PeterO.Cbor-generated dictionary files and externally generated CBOR that is
        /// structurally valid but not necessarily canonical. The explicit schema reader
        /// still validates the expected major types for known fields.
        /// </para>
        /// <para>
        /// Unknown top-level slots and unknown per-slot fields are skipped. This makes
        /// the decoder tolerant of additive schema evolution while preserving strict
        /// handling of known data.
        /// </para>
        /// </remarks>
        private static DictionaryMaxlength DeserializeCbor(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var reader = new CborReader(data, CborConformanceMode.Lax);

            if (reader.PeekState() != CborReaderState.StartMap)
            {
                throw new InvalidDataException(
                    "Invalid CBOR dictionary: root value must be a map.");
            }

            var instance = ReadDictionaryMaxlengthCbor(reader);

            if (reader.BytesRemaining != 0)
            {
                throw new InvalidDataException(
                    "Invalid CBOR dictionary: trailing data after root map.");
            }

            return EnsureDerivedMetadata(instance);
        }

        /// <summary>
        /// Writes the complete <see cref="DictionaryMaxlength"/> object as a CBOR map.
        /// </summary>
        /// <param name="writer">Target CBOR writer.</param>
        /// <param name="dictionary">Dictionary container to encode.</param>
        /// <remarks>
        /// The top-level names are a persistent wire contract and intentionally match
        /// the snake_case property names exposed by <see cref="DictionaryMaxlength"/>.
        /// They are written explicitly rather than derived from reflection or enum names
        /// so a future source-code rename cannot silently change the serialized format.
        /// </remarks>
        private static void WriteDictionaryMaxlengthCbor(
            CborWriter writer,
            DictionaryMaxlength dictionary)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            if (dictionary == null)
                throw new ArgumentNullException(nameof(dictionary));

            writer.WriteStartMap(CborDictionarySlotCount);

            WriteCborSlot(writer, "st_characters", dictionary.st_characters);
            WriteCborSlot(writer, "st_phrases", dictionary.st_phrases);
            WriteCborSlot(writer, "ts_characters", dictionary.ts_characters);
            WriteCborSlot(writer, "ts_phrases", dictionary.ts_phrases);

            WriteCborSlot(writer, "tw_phrases", dictionary.tw_phrases);
            WriteCborSlot(writer, "tw_phrases_rev", dictionary.tw_phrases_rev);
            WriteCborSlot(writer, "tw_variants", dictionary.tw_variants);
            WriteCborSlot(writer, "tw_variants_phrases", dictionary.tw_variants_phrases);
            WriteCborSlot(writer, "tw_variants_rev", dictionary.tw_variants_rev);
            WriteCborSlot(writer, "tw_variants_rev_phrases", dictionary.tw_variants_rev_phrases);

            WriteCborSlot(writer, "hk_phrases", dictionary.hk_phrases);
            WriteCborSlot(writer, "hk_phrases_rev", dictionary.hk_phrases_rev);
            WriteCborSlot(writer, "hk_variants", dictionary.hk_variants);
            WriteCborSlot(writer, "hk_variants_phrases", dictionary.hk_variants_phrases);
            WriteCborSlot(writer, "hk_variants_rev", dictionary.hk_variants_rev);
            WriteCborSlot(writer, "hk_variants_rev_phrases", dictionary.hk_variants_rev_phrases);

            WriteCborSlot(writer, "jps_characters", dictionary.jps_characters);
            WriteCborSlot(writer, "jps_characters_rev", dictionary.jps_characters_rev);
            WriteCborSlot(writer, "jps_phrases", dictionary.jps_phrases);

            WriteCborSlot(writer, "st_punctuations", dictionary.st_punctuations);
            WriteCborSlot(writer, "ts_punctuations", dictionary.ts_punctuations);

            writer.WriteEndMap();
        }

        /// <summary>
        /// Reads the top-level CBOR dictionary map into a new
        /// <see cref="DictionaryMaxlength"/> instance.
        /// </summary>
        /// <param name="reader">Reader positioned at the start of the root map.</param>
        /// <returns>The decoded dictionary container.</returns>
        /// <remarks>
        /// Unknown slot names are skipped rather than rejected. Known slot names are
        /// assigned explicitly, preserving the serialized schema independently of CLR
        /// metadata or property ordering.
        /// </remarks>
        private static DictionaryMaxlength ReadDictionaryMaxlengthCbor(CborReader reader)
        {
            var instance = new DictionaryMaxlength();

            reader.ReadStartMap();

            while (reader.PeekState() != CborReaderState.EndMap)
            {
                var slotName = reader.ReadTextString();

                switch (slotName)
                {
                    case "st_characters": instance.st_characters = ReadCborSlot(reader); break;
                    case "st_phrases": instance.st_phrases = ReadCborSlot(reader); break;
                    case "ts_characters": instance.ts_characters = ReadCborSlot(reader); break;
                    case "ts_phrases": instance.ts_phrases = ReadCborSlot(reader); break;

                    case "tw_phrases": instance.tw_phrases = ReadCborSlot(reader); break;
                    case "tw_phrases_rev": instance.tw_phrases_rev = ReadCborSlot(reader); break;
                    case "tw_variants": instance.tw_variants = ReadCborSlot(reader); break;
                    case "tw_variants_phrases": instance.tw_variants_phrases = ReadCborSlot(reader); break;
                    case "tw_variants_rev": instance.tw_variants_rev = ReadCborSlot(reader); break;
                    case "tw_variants_rev_phrases": instance.tw_variants_rev_phrases = ReadCborSlot(reader); break;

                    case "hk_phrases": instance.hk_phrases = ReadCborSlot(reader); break;
                    case "hk_phrases_rev": instance.hk_phrases_rev = ReadCborSlot(reader); break;
                    case "hk_variants": instance.hk_variants = ReadCborSlot(reader); break;
                    case "hk_variants_phrases": instance.hk_variants_phrases = ReadCborSlot(reader); break;
                    case "hk_variants_rev": instance.hk_variants_rev = ReadCborSlot(reader); break;
                    case "hk_variants_rev_phrases": instance.hk_variants_rev_phrases = ReadCborSlot(reader); break;

                    case "jps_characters": instance.jps_characters = ReadCborSlot(reader); break;
                    case "jps_characters_rev": instance.jps_characters_rev = ReadCborSlot(reader); break;
                    case "jps_phrases": instance.jps_phrases = ReadCborSlot(reader); break;

                    case "st_punctuations": instance.st_punctuations = ReadCborSlot(reader); break;
                    case "ts_punctuations": instance.ts_punctuations = ReadCborSlot(reader); break;

                    default:
                        reader.SkipValue();
                        break;
                }
            }

            reader.ReadEndMap();
            return instance;
        }

        /// <summary>
        /// Writes one <see cref="DictWithMaxLength"/> slot to the parent CBOR map.
        /// </summary>
        /// <param name="writer">Target CBOR writer.</param>
        /// <param name="slotName">Persistent snake_case slot name.</param>
        /// <param name="slot">Dictionary slot to encode.</param>
        /// <remarks>
        /// Each slot persists both the source mapping and all acceleration metadata
        /// using the historical PeterO.Cbor camelCase wire names:
        /// <c>dict</c>, <c>maxLength</c>, <c>minLength</c>, <c>lengthMask</c>,
        /// <c>longLengths</c>, and <c>starterLenMask</c>. This preserves legacy
        /// dictionary-pack compatibility while avoiding expensive key scans during
        /// normal hydration.
        /// </remarks>
        private static void WriteCborSlot(
            CborWriter writer,
            string slotName,
            DictWithMaxLength slot)
        {
            slot ??= new DictWithMaxLength();

            writer.WriteTextString(slotName);
            writer.WriteStartMap(CborSlotFieldCount);

            writer.WriteTextString("dict");
            WriteStringDictionary(writer, slot.Dict);

            writer.WriteTextString("maxLength");
            writer.WriteInt32(slot.MaxLength);

            writer.WriteTextString("minLength");
            writer.WriteInt32(slot.MinLength);

            writer.WriteTextString("lengthMask");
            writer.WriteUInt64(slot.LengthMask);

            writer.WriteTextString("longLengths");
            WriteLongLengths(writer, slot.LongLengths);

            writer.WriteTextString("starterLenMask");
            WriteStarterLenMask(writer, slot.StarterLenMask);

            writer.WriteEndMap();
        }

        /// <summary>
        /// Reads one <see cref="DictWithMaxLength"/> object from CBOR.
        /// </summary>
        /// <param name="reader">Reader positioned at the slot value.</param>
        /// <returns>A decoded dictionary slot.</returns>
        /// <remarks>
        /// Missing fields retain their normal CLR defaults and are repaired later by
        /// <see cref="EnsureDerivedMetadata"/>. Unknown additive fields are skipped,
        /// allowing newer dictionary builders to extend the slot schema without making
        /// older readers unusable.
        /// </remarks>
        private static DictWithMaxLength ReadCborSlot(CborReader reader)
        {
            if (reader.PeekState() == CborReaderState.Null)
            {
                reader.ReadNull();
                return new DictWithMaxLength();
            }

            var slot = new DictWithMaxLength();

            reader.ReadStartMap();

            while (reader.PeekState() != CborReaderState.EndMap)
            {
                var fieldName = reader.ReadTextString();

                switch (fieldName)
                {
                    case "dict":
                    case "Dict":
                        slot.Dict = ReadStringDictionary(reader);
                        break;

                    case "maxLength":
                    case "MaxLength":
                        slot.MaxLength = ReadNullableInt32(reader);
                        break;

                    case "minLength":
                    case "MinLength":
                        slot.MinLength = ReadNullableInt32(reader);
                        break;

                    case "lengthMask":
                    case "LengthMask":
                        slot.LengthMask = ReadNullableUInt64(reader);
                        break;

                    case "longLengths":
                    case "LongLengths":
                        slot.LongLengths = ReadLongLengths(reader);
                        break;

                    case "starterLenMask":
                    case "StarterLenMask":
                        slot.StarterLenMask = ReadStarterLenMask(reader);
                        break;

                    default:
                        reader.SkipValue();
                        break;
                }
            }

            reader.ReadEndMap();
            return slot;
        }

        /// <summary>
        /// Writes a string-to-string dictionary as a definite-length CBOR map.
        /// </summary>
        /// <param name="writer">Target CBOR writer.</param>
        /// <param name="dictionary">Dictionary to encode; null is encoded as an empty map.</param>
        private static void WriteStringDictionary(
            CborWriter writer,
            Dictionary<string, string> dictionary)
        {
            dictionary ??= new Dictionary<string, string>(StringComparer.Ordinal);

            writer.WriteStartMap(dictionary.Count);

            foreach (var pair in dictionary)
            {
                writer.WriteTextString(pair.Key);
                writer.WriteTextString(pair.Value ?? string.Empty);
            }

            writer.WriteEndMap();
        }

        /// <summary>
        /// Reads a CBOR map containing string keys and string values.
        /// </summary>
        /// <param name="reader">Reader positioned at the map or a CBOR null.</param>
        /// <returns>
        /// A new dictionary using <see cref="StringComparer.Ordinal"/>. Duplicate map
        /// keys, when accepted by lax CBOR conformance, use last-value-wins semantics.
        /// </returns>
        private static Dictionary<string, string> ReadStringDictionary(CborReader reader)
        {
            if (reader.PeekState() == CborReaderState.Null)
            {
                reader.ReadNull();
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var declaredCount = reader.ReadStartMap();
            var dictionary = declaredCount.HasValue
                ? new Dictionary<string, string>(declaredCount.Value, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);

            while (reader.PeekState() != CborReaderState.EndMap)
            {
                var key = reader.ReadTextString();
                var value = reader.ReadTextString();
                dictionary[key] = value;
            }

            reader.ReadEndMap();
            return dictionary;
        }

        /// <summary>
        /// Writes the set of key lengths greater than 64 UTF-16 code units.
        /// </summary>
        /// <param name="writer">Target CBOR writer.</param>
        /// <param name="longLengths">
        /// Optional long-length set. A null value is encoded as CBOR null to preserve
        /// the historical object schema and avoid allocating an empty collection.
        /// </param>
        private static void WriteLongLengths(CborWriter writer, HashSet<int> longLengths)
        {
            if (longLengths == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartArray(longLengths.Count);

            foreach (var length in longLengths)
                writer.WriteInt32(length);

            writer.WriteEndArray();
        }

        /// <summary>
        /// Reads the optional set of key lengths greater than 64 UTF-16 code units.
        /// </summary>
        /// <param name="reader">Reader positioned at a CBOR array or null.</param>
        /// <returns>The decoded set, or <see langword="null"/> when encoded as null.</returns>
        private static HashSet<int> ReadLongLengths(CborReader reader)
        {
            if (reader.PeekState() == CborReaderState.Null)
            {
                reader.ReadNull();
                return null;
            }

            _ = reader.ReadStartArray();
            var lengths = new HashSet<int>();

            while (reader.PeekState() != CborReaderState.EndArray)
                lengths.Add(reader.ReadInt32());

            reader.ReadEndArray();
            return lengths;
        }

        /// <summary>
        /// Writes the precomputed starter-to-length-mask index used by the conversion
        /// hot path.
        /// </summary>
        /// <param name="writer">Target CBOR writer.</param>
        /// <param name="starterLenMask">
        /// Optional starter index. A null value is encoded as CBOR null so empty slots
        /// remain allocation-free until metadata is actually required.
        /// </param>
        /// <remarks>
        /// Persisting this map is intentional even though it can be rebuilt from
        /// <see cref="DictWithMaxLength.Dict"/>. Rebuilding requires a complete scan of
        /// the slot's keys and starter extraction, which is undesirable on every normal
        /// application startup for large phrase dictionaries.
        /// </remarks>
        private static void WriteStarterLenMask(
            CborWriter writer,
            Dictionary<string, ulong> starterLenMask)
        {
            if (starterLenMask == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartMap(starterLenMask.Count);

            foreach (var pair in starterLenMask)
            {
                writer.WriteTextString(pair.Key);
                writer.WriteUInt64(pair.Value);
            }

            writer.WriteEndMap();
        }

        /// <summary>
        /// Reads the precomputed starter-to-length-mask index from CBOR.
        /// </summary>
        /// <param name="reader">Reader positioned at a CBOR map or null.</param>
        /// <returns>
        /// A dictionary using <see cref="StringComparer.Ordinal"/>, or
        /// <see langword="null"/> when the field was encoded as null.
        /// </returns>
        private static Dictionary<string, ulong> ReadStarterLenMask(CborReader reader)
        {
            if (reader.PeekState() == CborReaderState.Null)
            {
                reader.ReadNull();
                return null;
            }

            var declaredCount = reader.ReadStartMap();
            var map = declaredCount.HasValue
                ? new Dictionary<string, ulong>(declaredCount.Value, StringComparer.Ordinal)
                : new Dictionary<string, ulong>(StringComparer.Ordinal);

            while (reader.PeekState() != CborReaderState.EndMap)
            {
                var starter = reader.ReadTextString();
                map[starter] = reader.ReadUInt64();
            }

            reader.ReadEndMap();
            return map;
        }

        /// <summary>
        /// Reads an optional integer field used by historical or externally generated
        /// CBOR dictionary payloads.
        /// </summary>
        /// <param name="reader">Reader positioned at an integer or null.</param>
        /// <returns>The decoded value, or zero when the CBOR value is null.</returns>
        private static int ReadNullableInt32(CborReader reader)
        {
            if (reader.PeekState() != CborReaderState.Null)
                return reader.ReadInt32();

            reader.ReadNull();
            return 0;
        }

        /// <summary>
        /// Reads an optional unsigned 64-bit metadata field.
        /// </summary>
        /// <param name="reader">Reader positioned at an unsigned integer or null.</param>
        /// <returns>The decoded value, or zero when the CBOR value is null.</returns>
        private static ulong ReadNullableUInt64(CborReader reader)
        {
            if (reader.PeekState() != CborReaderState.Null)
                return reader.ReadUInt64();

            reader.ReadNull();
            return 0UL;
        }

        #endregion // CBOR Serialization

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

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(
                instance,
                DictionaryJsonContext.Default.DictionaryMaxlength);

            using var compressor = new Compressor(19);
            var compressed = compressor.Wrap(jsonBytes);
            File.WriteAllBytes(path, compressed.ToArray());
        }

        /// <summary>
        /// Loads and normalizes the dictionary from a Zstd-compressed JSON file.
        /// </summary>
        /// <param name="path">The path to the compressed file.</param>
        /// <returns>The deserialized <see cref="DictionaryMaxlength"/> instance.</returns>
        public static DictionaryMaxlength LoadJsonCompressed(string path)
        {
            var compressed = File.ReadAllBytes(path);

            using var decompressor = new Decompressor();
            var jsonBytes = decompressor.Unwrap(compressed);
            var instance = JsonSerializer.Deserialize(
                jsonBytes,
                DictionaryJsonContext.Default.DictionaryMaxlength);
            return EnsureDerivedMetadata(instance);
        }
    }
}