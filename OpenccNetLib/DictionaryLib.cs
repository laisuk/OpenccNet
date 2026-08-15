using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PeterO.Cbor;
using ZstdSharp;

namespace OpenccNetLib
{
    /// <summary>
    /// Represents a dictionary with string keys and values plus derived key-length metadata.
    /// </summary>
    public class DictWithMaxLength
    {
        [JsonInclude]
        public Dictionary<string, string> Dict { get; set; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        [JsonInclude]
        public int MaxLength { get; set; }

        [JsonInclude]
        public int MinLength { get; set; }

        [JsonInclude]
        public ulong LengthMask { get; set; }

        [JsonInclude]
        public HashSet<int> LongLengths { get; set; }

        [JsonInclude]
        public Dictionary<string, ulong> StarterLenMask { get; set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(string key, out string value)
            => Dict.TryGetValue(key, out value);

#if NET9_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetValue(ReadOnlySpan<char> key, out string value)
        {
            if (Dict.TryGetAlternateLookup<ReadOnlySpan<char>>(out var lookup))
                return lookup.TryGetValue(key, out value);

            return Dict.TryGetValue(key.ToString(), out value);
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool SupportsLength(int length)
        {
            if (length <= 0)
                return false;

            var minLen = MinLength;
            if (minLen == 0 || length < minLen || length > MaxLength)
                return false;

            if (length <= 64)
                return ((LengthMask >> (length - 1)) & 1UL) != 0UL;

            var longLengths = LongLengths;
            return longLengths != null && longLengths.Contains(length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetLengthMetadata(ulong mask, HashSet<int> longLengths)
        {
            LengthMask = mask;
            LongLengths = longLengths;
        }

        public int Count => Dict.Count;
    }

    // ReSharper disable InconsistentNaming
    /// <summary>
    /// Holds all dictionary tables used by the OpenCC conversion engine.
    /// </summary>
    public sealed class DictionaryMaxlength
    {
        public DictWithMaxLength st_characters { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength st_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength ts_characters { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength ts_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength tw_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength tw_phrases_rev { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength tw_variants { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength tw_variants_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength tw_variants_rev { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength tw_variants_rev_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength hk_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength hk_phrases_rev { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength hk_variants { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength hk_variants_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength hk_variants_rev { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength hk_variants_rev_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength jps_characters { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength jps_characters_rev { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength jps_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength st_punctuations { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength ts_punctuations { get; set; } = new DictWithMaxLength();
    }
    // ReSharper restore InconsistentNaming

    /// <summary>
    /// Dictionary loading, customization, and serialization helpers.
    /// <see cref="Provider"/> is always the built-in default dictionary.
    /// Runtime active-provider ownership belongs to the internal
    /// <see cref="ConversionPlanCache"/>.
    /// </summary>
    public static class DictionaryLib
    {
        private static readonly Lazy<DictionaryMaxlength> DefaultLib =
            new Lazy<DictionaryMaxlength>(() => FromZstd(), isThreadSafe: true);

        /// <summary>
        /// Gets the built-in default singleton dictionary.
        /// This property is independent of the currently active conversion provider.
        /// </summary>
        public static DictionaryMaxlength Provider => DefaultLib.Value;

        // Internal compatibility surface for tests and existing in-assembly call sites.
        // The active provider and all cache state are owned by ConversionPlanCache.
        internal static ConversionPlanCache PlanCache => ConversionPlanCache.Current;
        internal static DictionaryMaxlength GetActiveProvider() => ConversionPlanCache.Provider;

        /// <summary>
        /// Returns the built-in default dictionary and resets conversion planning
        /// to use that default provider.
        /// </summary>
        public static DictionaryMaxlength New()
        {
            ConversionPlanCache.ResetProvider();
            return Provider;
        }

        internal static void SetDictionaryProvider(DictionaryMaxlength dictionary)
            => ConversionPlanCache.UseProvider(dictionary);

        internal static void ResetDictionaryProviderToDefault()
            => ConversionPlanCache.ResetProvider();

        private static DictionaryMaxlength FromZstd(
            string relativePath = "dicts/dictionary_maxlength.zstd")
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("Path must not be null or empty.", nameof(relativePath));

            var fullPath = Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(AppContext.BaseDirectory, relativePath);

            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Zstd dictionary file not found.", fullPath);

            using (var inputStream = File.OpenRead(fullPath))
            using (var decompressionStream = new DecompressionStream(inputStream))
            {
                var instance = JsonSerializer.Deserialize<DictionaryMaxlength>(decompressionStream);
                return EnsureDerivedMetadata(instance);
            }
        }

        public static DictionaryMaxlength FromJson(
            string relativePath = "dicts/dictionary_maxlength.json")
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("Path must not be null or empty.", nameof(relativePath));

            var fullPath = Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(AppContext.BaseDirectory, relativePath);

            if (!File.Exists(fullPath))
                throw new FileNotFoundException("JSON dictionary file not found.", fullPath);

            using (var stream = File.OpenRead(fullPath))
            {
                var instance = JsonSerializer.Deserialize<DictionaryMaxlength>(stream);
                return EnsureDerivedMetadata(instance);
            }
        }

        public static void SerializeToJson(
            string path,
            DictionaryMaxlength dictionary = null)
        {
            var instance = dictionary ?? FromDicts();

            File.WriteAllText(
                path,
                JsonSerializer.Serialize(
                    instance,
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        private static readonly Regex SurrogatePairRegex =
            new Regex(
                @"\\u(?<hi>[dD][89ABab][0-9A-Fa-f]{2})\\u(?<lo>[dD][CDEFcdef][0-9A-Fa-f]{2})",
                RegexOptions.Compiled);

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
            var json = DecodeJsonSurrogatePairs(JsonSerializer.Serialize(instance, options));

            File.WriteAllText(
                path,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public static DictionaryMaxlength DeserializedFromJson(string path)
            => FromJson(path);

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

        private static string ResolveUserPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path must not be null or empty.", nameof(path));

            path = path.Trim().Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar);

            return Path.GetFullPath(
                Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(AppContext.BaseDirectory, path));
        }

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
                    throw new ArgumentException("Unknown dictionary slot: " + slot, nameof(slot));
            }
        }

        private static void SetSlot(
            DictionaryMaxlength d,
            DictSlot slot,
            DictWithMaxLength value)
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
                    throw new ArgumentException("Unknown dictionary slot: " + slot, nameof(slot));
            }
        }

        private static void AppendSlot(DictionaryMaxlength d, DictSlot slot, string path)
        {
            var target = GetSlot(d, slot);
            var extra = LoadFile(path);

            foreach (var kv in extra.Dict)
                target.Dict[kv.Key] = kv.Value;

            RebuildDictionaryMetadata(target);
        }

        private static void RebuildDictionaryMetadata(DictWithMaxLength d)
        {
            d.MaxLength = 0;
            d.MinLength = 0;
            d.SetLengthMetadata(0UL, null);
            d.StarterLenMask = null;
            EnsureDictionaryMetadata(d);
        }

        public static DictionaryMaxlength FromDicts(
            string relativeBaseDir = "dicts",
            IDictionary<DictSlot, string> overrides = null,
            IDictionary<DictSlot, string> appends = null)
        {
            var baseDir = ResolveUserPath(relativeBaseDir);
            var instance = new DictionaryMaxlength();

            foreach (var slot in DictSlotExtensions.ActiveSlots)
            {
                var path = Path.Combine(baseDir, SlotFiles[slot]);
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

            if (appends != null)
            {
                foreach (var kv in appends)
                {
                    if (!kv.Key.IsActive())
                        throw new ArgumentException("Unknown dictionary slot: " + kv.Key);

                    AppendSlot(instance, kv.Key, ResolveUserPath(kv.Value));
                }
            }

            return EnsureDerivedMetadata(instance);
        }

        private static DictWithMaxLength LoadFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Dictionary file not found: " + path, path);

            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            var maxLength = 0;
            var minLength = int.MaxValue;
            var lengthMask = 0UL;
            HashSet<int> longLengths = null;

            foreach (var line in File.ReadLines(path))
            {
                var lineSpan = line.AsSpan().Trim();
                if (lineSpan.IsEmpty || lineSpan[0] == '#')
                    continue;

                var tabIndex = lineSpan.IndexOf('\t');
                if (tabIndex == -1)
                    continue;

                var keySpan = lineSpan.Slice(0, tabIndex).Trim();
                var valueFullSpan = lineSpan.Slice(tabIndex + 1);
                var firstSpaceIndex = valueFullSpan.IndexOf(' ');
                var valueSpan = (firstSpaceIndex != -1
                    ? valueFullSpan.Slice(0, firstSpaceIndex)
                    : valueFullSpan).Trim();

                if (keySpan.IsEmpty || valueSpan.IsEmpty)
                    continue;

                var key = keySpan.ToString();
                dict[key] = valueSpan.ToString();

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
            BuildStarterLenMask(d);
            return d;
        }

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
                if (spec != null)
                    ApplyCustomDictSpec(dict, spec);
            }

            return EnsureDerivedMetadata(dict);
        }

        private static void ApplyCustomDictSpec(
            DictionaryMaxlength dict,
            CustomDictSpec spec)
        {
            if (!spec.Slot.IsActive())
                throw new ArgumentException("Unknown dictionary slot: " + spec.Slot, nameof(spec));

            var hasPaths = spec.Paths != null && spec.Paths.Length > 0;
            var hasPairs = spec.Pairs != null && spec.Pairs.Count > 0;

            if (!hasPaths && !hasPairs)
            {
                throw new ArgumentException(
                    "CustomDictSpec must provide at least one dictionary source: Paths or Pairs.",
                    nameof(spec));
            }

            var target = spec.Mode == CustomDictMode.Override
                ? new DictWithMaxLength
                {
                    Dict = new Dictionary<string, string>(StringComparer.Ordinal)
                }
                : GetSlot(dict, spec.Slot);

            if (hasPaths)
            {
                foreach (var path in spec.Paths)
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        throw new ArgumentException(
                            "Custom dictionary path must not be null or empty.",
                            nameof(spec));
                    }

                    var extra = LoadFile(ResolveUserPath(path));
                    foreach (var kv in extra.Dict)
                        target.Dict[kv.Key] = kv.Value;
                }
            }

            if (hasPairs)
            {
                foreach (var kv in spec.Pairs)
                {
                    if (!string.IsNullOrEmpty(kv.Key))
                        target.Dict[kv.Key] = kv.Value ?? string.Empty;
                }
            }

            RebuildDictionaryMetadata(target);

            if (spec.Mode == CustomDictMode.Override)
                SetSlot(dict, spec.Slot, target);
        }

        private static void BuildStarterLenMask(DictWithMaxLength d)
        {
            if (d?.Dict == null || d.Dict.Count == 0)
                return;

            var map = new Dictionary<string, ulong>(
                Math.Min(d.Dict.Count, 1024),
                StringComparer.Ordinal);

            foreach (var key in d.Dict.Keys)
            {
                if (string.IsNullOrEmpty(key))
                    continue;

                var len = key.Length;
                var starter = len >= 2 &&
                              char.IsHighSurrogate(key[0]) &&
                              char.IsLowSurrogate(key[1])
                    ? key.Substring(0, 2)
                    : key.Substring(0, 1);

                map.TryGetValue(starter, out var mask);

                if ((uint)len - 1u < 64u)
                    mask |= 1UL << (len - 1);

                map[starter] = mask;
            }

            d.StarterLenMask = map;
        }

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

        private static void EnsureRequiredDictionarySlots(DictionaryMaxlength instance)
        {
            if (instance.jps_characters_rev.Dict == null ||
                instance.jps_characters_rev.Dict.Count == 0)
            {
                throw new InvalidOperationException(
                    "Required dictionary slot 'jps_characters_rev' is missing or empty. " +
                    "Regenerate dictionary_maxlength assets or include JPShinjitaiCharactersRev.txt.");
            }
        }

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

            var needsLengthMetadata =
                d.MaxLength <= 0 ||
                d.MinLength <= 0 ||
                (d.LengthMask == 0UL && d.MaxLength > 0);

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

        public static DictionaryMaxlength FromCbor(
            string relativePath = "dicts/dictionary_maxlength.cbor")
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("Path must not be null or empty.", nameof(relativePath));

            var fullPath = Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(AppContext.BaseDirectory, relativePath);

            if (!File.Exists(fullPath))
                throw new FileNotFoundException("CBOR dictionary file not found.", fullPath);

            var cbor = CBORObject.DecodeFromBytes(
                File.ReadAllBytes(fullPath),
                CBOREncodeOptions.Default);

            return EnsureDerivedMetadata(cbor.ToObject<DictionaryMaxlength>());
        }

        public static void SaveCbor(
            string path,
            DictionaryMaxlength dictionary = null)
        {
            var instance = dictionary ?? FromDicts();
            var cbor = CBORObject.FromObject(instance);
            File.WriteAllBytes(path, cbor.EncodeToBytes());
        }

        public static byte[] ToCborBytes(
            DictionaryMaxlength dictionary = null)
        {
            return CBORObject
                .FromObject(dictionary ?? FromDicts())
                .EncodeToBytes();
        }

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
