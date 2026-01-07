using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpenccNetLib
{
    /// <summary>
    /// Represents all supported OpenCC conversion configurations.
    /// </summary>
    /// <remarks>
    /// Each configuration defines a directional transformation between
    /// Chinese variants (Simplified, Traditional, Taiwan, Hong Kong) 
    /// or Japanese Shinjitai/Kyūjitai forms.
    /// </remarks>
    public enum OpenccConfig
    {
        /// <summary>
        /// Simplified Chinese → Traditional Chinese (General Standard).
        /// </summary>
        S2T,

        /// <summary>
        /// Traditional Chinese → Simplified Chinese (General Standard).
        /// </summary>
        T2S,

        /// <summary>
        /// Simplified Chinese → Traditional Chinese (Taiwan Standard).
        /// </summary>
        S2Tw,

        /// <summary>
        /// Traditional Chinese (Taiwan Standard) → Simplified Chinese.
        /// </summary>
        Tw2S,

        /// <summary>
        /// Simplified Chinese → Traditional Chinese (Taiwan Standard, with Taiwan idioms).
        /// </summary>
        S2Twp,

        /// <summary>
        /// Traditional Chinese (Taiwan Standard, with idioms) → Simplified Chinese.
        /// </summary>
        Tw2Sp,

        /// <summary>
        /// Simplified Chinese → Traditional Chinese (Hong Kong Standard).
        /// </summary>
        S2Hk,

        /// <summary>
        /// Traditional Chinese (Hong Kong Standard) → Simplified Chinese.
        /// </summary>
        Hk2S,

        /// <summary>
        /// Traditional Chinese (General Standard) → Traditional Chinese (Taiwan Standard).
        /// </summary>
        T2Tw,

        /// <summary>
        /// Traditional Chinese (General Standard) → Traditional Chinese (Taiwan, with idioms).
        /// </summary>
        T2Twp,

        /// <summary>
        /// Traditional Chinese (Taiwan Standard) → Traditional Chinese (General Standard).
        /// </summary>
        Tw2T,

        /// <summary>
        /// Traditional Chinese (Taiwan, with idioms) → Traditional Chinese (General Standard).
        /// </summary>
        Tw2Tp,

        /// <summary>
        /// Traditional Chinese (General Standard) → Traditional Chinese (Hong Kong Standard).
        /// </summary>
        T2Hk,

        /// <summary>
        /// Traditional Chinese (Hong Kong Standard) → Traditional Chinese (General Standard).
        /// </summary>
        Hk2T,

        /// <summary>
        /// Traditional Japanese Kyujitai → Japanese Shinjitai.
        /// </summary>
        T2Jp,

        /// <summary>
        /// Japanese Shinjitai → Traditional Japanese Kyujitai.
        /// </summary>
        Jp2T
    }

    // @Since v1.4.1
    public static class OpenccConfigExtensions
    {
        /// <summary>
        /// Converts an <see cref="OpenccConfig"/> value to its canonical Opencc configuration name
        /// (e.g. "s2t", "s2twp").
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToCanonicalName(this OpenccConfig config)
        {
            switch (config)
            {
                case OpenccConfig.S2T: return "s2t";
                case OpenccConfig.T2S: return "t2s";
                case OpenccConfig.S2Tw: return "s2tw";
                case OpenccConfig.Tw2S: return "tw2s";
                case OpenccConfig.S2Twp: return "s2twp";
                case OpenccConfig.Tw2Sp: return "tw2sp";
                case OpenccConfig.S2Hk: return "s2hk";
                case OpenccConfig.Hk2S: return "hk2s";
                case OpenccConfig.T2Tw: return "t2tw";
                case OpenccConfig.T2Twp: return "t2twp";
                case OpenccConfig.Tw2T: return "tw2t";
                case OpenccConfig.Tw2Tp: return "tw2tp";
                case OpenccConfig.T2Hk: return "t2hk";
                case OpenccConfig.Hk2T: return "hk2t";
                case OpenccConfig.T2Jp: return "t2jp";
                case OpenccConfig.Jp2T: return "jp2t";
                default:
                    throw new ArgumentOutOfRangeException(nameof(config), config, "Invalid OpenCC config");
            }
        }
    }

    /// <summary>
    /// Main class for OpenCC text conversion. Provides methods for various conversion directions
    /// (Simplified-Traditional, Traditional-Simplified, etc.) and supports multi-stage, high-performance conversion.
    /// </summary>
    public class Opencc
    {
        // Regex for stripping non-Chinese and non-symbol characters.
        private static readonly Regex StripRegex = new Regex(
            @"[!-/:-@\[-`{-~\t\n\v\f\r 0-9A-Za-z_著]",
            RegexOptions.Compiled);

        // Thread-local StringBuilder for efficient string concatenation.
        private static readonly ThreadLocal<StringBuilder> StringBuilderCache =
            new ThreadLocal<StringBuilder>(() => new StringBuilder(1024));

        #region Config Enum Helpers Region

        /// <summary>
        /// Represents a single OpenCC configuration mapping entry.
        /// </summary>
        /// <remarks>
        /// This struct defines the canonical relationship between an
        /// <see cref="OpenccConfig"/> identifier and its string representation.
        /// It is used as the single source of truth for configuration mapping.
        /// </remarks>
        private struct ConfigEntry
        {
            /// <summary>
            /// The OpenCC configuration identifier.
            /// </summary>
            public readonly OpenccConfig Id;

            /// <summary>
            /// The canonical string name of the configuration (lowercase).
            /// </summary>
            public readonly string Name;

            /// <summary>
            /// Initializes a new <see cref="ConfigEntry"/> instance.
            /// </summary>
            /// <param name="id">The configuration identifier.</param>
            /// <param name="name">The canonical configuration name.</param>
            public ConfigEntry(OpenccConfig id, string name)
            {
                Id = id;
                Name = name;
            }
        }

        private const OpenccConfig DefaultConfigId = OpenccConfig.S2T;
        private const string DefaultConfigName = "s2t";

        /// <summary>
        /// Canonical OpenCC configuration mapping table.
        /// </summary>
        /// <remarks>
        /// This array is the single authoritative source for all configuration mappings
        /// between <see cref="OpenccConfig"/> identifiers and their string representations.
        /// <para/>
        /// All validation, parsing, and projection logic must be derived from this table.
        /// To add or remove a supported configuration, update this table only.
        /// </remarks>
        private static readonly ConfigEntry[] ConfigMap =
        {
            new ConfigEntry(OpenccConfig.S2T, "s2t"),
            new ConfigEntry(OpenccConfig.T2S, "t2s"),
            new ConfigEntry(OpenccConfig.S2Tw, "s2tw"),
            new ConfigEntry(OpenccConfig.Tw2S, "tw2s"),
            new ConfigEntry(OpenccConfig.S2Twp, "s2twp"),
            new ConfigEntry(OpenccConfig.Tw2Sp, "tw2sp"),
            new ConfigEntry(OpenccConfig.S2Hk, "s2hk"),
            new ConfigEntry(OpenccConfig.Hk2S, "hk2s"),
            new ConfigEntry(OpenccConfig.T2Tw, "t2tw"),
            new ConfigEntry(OpenccConfig.Tw2T, "tw2t"),
            new ConfigEntry(OpenccConfig.T2Twp, "t2twp"),
            new ConfigEntry(OpenccConfig.Tw2Tp, "tw2tp"),
            new ConfigEntry(OpenccConfig.T2Hk, "t2hk"),
            new ConfigEntry(OpenccConfig.Hk2T, "hk2t"),
            new ConfigEntry(OpenccConfig.T2Jp, "t2jp"),
            new ConfigEntry(OpenccConfig.Jp2T, "jp2t"),
        };

        /// <summary>
        /// Lookup table for mapping configuration names to identifiers.
        /// </summary>
        /// <remarks>
        /// Built once from <see cref="ConfigMap"/> at type initialization.
        /// Uses case-insensitive string comparison.
        /// </remarks>
        private static readonly Dictionary<string, OpenccConfig> NameToId =
            BuildNameToId(ConfigMap);

        /// <summary>
        /// Lookup table for mapping configuration identifiers to canonical names.
        /// </summary>
        /// <remarks>
        /// Built once from <see cref="ConfigMap"/> at type initialization.
        /// </remarks>
        private static readonly Dictionary<OpenccConfig, string> IdToName =
            BuildIdToName(ConfigMap);

        /// <summary>
        /// Read-only collection of supported configuration names.
        /// </summary>
        /// <remarks>
        /// Derived from <see cref="ConfigMap"/> and created once at type initialization.
        /// The collection is stable and does not allocate on each access.
        /// </remarks>
        private static readonly IReadOnlyCollection<string> SupportedConfigNames =
            BuildSupportedNames(ConfigMap);

        /// <summary>
        /// Builds a lookup table that maps configuration names to identifiers.
        /// </summary>
        /// <param name="map">
        /// The canonical configuration mapping table.
        /// </param>
        /// <returns>
        /// A dictionary mapping configuration names to <see cref="OpenccConfig"/> identifiers.
        /// </returns>
        /// <remarks>
        /// This helper is invoked once during type initialization to construct
        /// a case-insensitive lookup table from <see cref="ConfigMap"/>.
        /// </remarks>
        private static Dictionary<string, OpenccConfig> BuildNameToId(ConfigEntry[] map)
        {
            var dict = new Dictionary<string, OpenccConfig>(map.Length, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < map.Length; i++)
                dict[map[i].Name] = map[i].Id;
            return dict;
        }

        /// <summary>
        /// Builds a lookup table that maps configuration identifiers to canonical names.
        /// </summary>
        /// <param name="map">
        /// The canonical configuration mapping table.
        /// </param>
        /// <returns>
        /// A dictionary mapping <see cref="OpenccConfig"/> identifiers to their
        /// canonical string representations.
        /// </returns>
        /// <remarks>
        /// This helper is invoked once during type initialization to construct
        /// the reverse mapping from <see cref="ConfigMap"/>.
        /// </remarks>
        private static Dictionary<OpenccConfig, string> BuildIdToName(ConfigEntry[] map)
        {
            var dict = new Dictionary<OpenccConfig, string>(map.Length);
            for (var i = 0; i < map.Length; i++)
                dict[map[i].Id] = map[i].Name;
            return dict;
        }

        /// <summary>
        /// Builds a read-only collection of supported configuration names.
        /// </summary>
        /// <param name="map">
        /// The canonical configuration mapping table.
        /// </param>
        /// <returns>
        /// A stable, read-only collection of configuration names.
        /// </returns>
        /// <remarks>
        /// The returned collection is created once at type initialization
        /// and reused for the lifetime of the application.
        /// </remarks>
        private static IReadOnlyCollection<string> BuildSupportedNames(ConfigEntry[] map)
        {
            // Stable ordering, allocation happens once at type initialization.
            var names = new string[map.Length];
            for (var i = 0; i < map.Length; i++)
                names[i] = map[i].Name;

            // .NET Standard 2.0: Array.AsReadOnly is available.
            return Array.AsReadOnly(names);
        }

        /// <summary>
        /// Converts a configuration identifier to its canonical string representation.
        /// </summary>
        /// <param name="configId">
        /// The configuration identifier to convert.
        /// </param>
        /// <returns>
        /// The canonical lowercase configuration name, or the default configuration
        /// name if the identifier is not recognized.
        /// </returns>
        /// <remarks>
        /// This helper is used internally to project the authoritative enum-based
        /// configuration state into the legacy string-based API.
        /// </remarks>
        private static string ConfigIdToString(OpenccConfig configId)
        {
            return IdToName.TryGetValue(configId, out var name) ? name : DefaultConfigName;
        }

        /// <summary>
        /// Attempts to parse a configuration name into an <see cref="OpenccConfig"/> identifier.
        /// </summary>
        /// <param name="config">
        /// The configuration name to parse (for example, <c>"s2t"</c>, <c>"tw2sp"</c>).
        /// The comparison is case-insensitive and ignores leading or trailing whitespace.
        /// </param>
        /// <param name="result">
        /// When this method returns, contains the corresponding <see cref="OpenccConfig"/> value
        /// if parsing succeeded; otherwise, the default value.
        /// </param>
        /// <returns>
        /// <c>true</c> if <paramref name="config"/> represents a supported OpenCC configuration;
        /// <c>false</c> if <paramref name="config"/> is <c>null</c>, empty, whitespace-only,
        /// or not a recognized configuration name.
        /// </returns>
        /// <remarks>
        /// This method does not throw exceptions.
        /// It is intended for validation, parsing user input, and binding scenarios.
        /// </remarks>
        public static bool TryParseConfig(string config, out OpenccConfig result)
        {
            if (string.IsNullOrWhiteSpace(config))
            {
                result = default;
                return false;
            }

            var s = config.Trim();
            return NameToId.TryGetValue(s, out result);
        }

        /// <summary>
        /// Determines whether the specified configuration name is a supported OpenCC configuration.
        /// </summary>
        /// <param name="config">
        /// The configuration name to validate (for example, <c>"s2t"</c>, <c>"t2hk"</c>).
        /// The comparison is case-insensitive and ignores leading or trailing whitespace.
        /// </param>
        /// <returns>
        /// <c>true</c> if the configuration name is recognized and supported;
        /// otherwise, <c>false</c>.
        /// </returns>
        /// <remarks>
        /// This method performs validation only and does not modify any instance state.
        /// </remarks>
        public static bool IsValidConfig(string config)
        {
            return TryParseConfig(config, out _);
        }

        /// <summary>
        /// Gets a read-only collection of all supported OpenCC configuration names.
        /// </summary>
        /// <returns>
        /// A stable, read-only collection of canonical configuration names
        /// (for example, <c>"s2t"</c>, <c>"tw2sp"</c>).
        /// </returns>
        /// <remarks>
        /// The returned collection contains lowercase, canonical identifiers and
        /// does not allocate on each call.  
        /// Modifying the returned collection is not supported.
        /// </remarks>
        public static IReadOnlyCollection<string> GetSupportedConfigs()
        {
            return SupportedConfigNames;
        }

        #endregion // Config Enum Helpers Region

        #region Instance Configs

        // ================= Instance state =================

        /// <summary>
        /// Holds the current OpenCC configuration identifier.
        /// </summary>
        /// <remarks>
        /// This field is the single source of truth for the active configuration.
        /// All public and internal configuration APIs ultimately read from or
        /// update this value.
        /// </remarks>
        private OpenccConfig _configId = DefaultConfigId;

        /// <summary>
        /// Stores the most recent configuration-related error message.
        /// </summary>
        /// <remarks>
        /// This field is updated when invalid configuration values are provided
        /// through public or internal setters. A <c>null</c> value indicates that
        /// no configuration error is currently recorded.
        /// </remarks>
        private string _lastError;

        /// <summary>
        /// Gets or sets the current OpenCC conversion configuration using the legacy string-based API.
        /// </summary>
        /// <value>
        /// A canonical configuration name such as <c>"s2t"</c> or <c>"tw2sp"</c>.
        /// The returned value is always lowercase and normalized.
        /// </value>
        /// <remarks>
        /// This property exists for backward compatibility and convenience.
        /// Internally, the configuration is stored as an <see cref="OpenccConfig"/> identifier.
        /// <para/>
        /// When an invalid value is assigned, the configuration falls back to <c>"s2t"</c>
        /// and an error message is recorded internally.
        /// </remarks>
        public string Config
        {
            get => ConfigIdToString(_configId);
            set => SetConfigInternal(value, true);
        }

        /// <summary>
        /// Sets the conversion configuration using a configuration name string.
        /// </summary>
        /// <param name="config">
        /// The configuration name to apply (for example, <c>"s2t"</c>, <c>"t2hk"</c>).
        /// The comparison is case-insensitive and ignores leading or trailing whitespace.
        /// </param>
        /// <remarks>
        /// This method is equivalent to assigning the <see cref="Config"/> property.
        /// Internally, the configuration is stored as an <see cref="OpenccConfig"/> identifier.
        /// <para/>
        /// If the provided value is invalid or unrecognized, the configuration
        /// falls back to <c>"s2t"</c> and an error message is recorded internally.
        /// </remarks>
        public void SetConfig(string config)
        {
            SetConfigInternal(config, true);
        }

        /// <summary>
        /// Sets the conversion configuration using an <see cref="OpenccConfig"/> enum value.
        /// </summary>
        /// <param name="configEnum">
        /// The OpenCC configuration identifier to apply.
        /// </param>
        /// <remarks>
        /// This overload is the preferred way to set the configuration when using strongly typed APIs.
        /// <para/>
        /// If an invalid enum value is provided (for example, a value created by casting an integer),
        /// the configuration falls back to <c>"s2t"</c> and an error message is recorded internally.
        /// </remarks>
        public void SetConfig(OpenccConfig configEnum)
        {
            SetConfigInternal(configEnum, true);
        }

        /// <summary>
        /// Gets the current conversion configuration as a canonical string.
        /// </summary>
        /// <returns>
        /// The normalized configuration name currently in use
        /// (for example, <c>"s2t"</c>, <c>"tw2sp"</c>).
        /// </returns>
        /// <remarks>
        /// This method is functionally equivalent to reading the <see cref="Config"/> property
        /// and is provided for compatibility with older APIs.
        /// </remarks>
        public string GetConfig()
        {
            return Config;
        }

        /// <summary>
        /// Gets the current conversion configuration as an <see cref="OpenccConfig"/> identifier.
        /// </summary>
        /// <returns>
        /// The <see cref="OpenccConfig"/> value currently in use.
        /// </returns>
        /// <remarks>
        /// This method exposes the authoritative internal configuration state.
        /// It is recommended for advanced usage, interop scenarios, and strongly typed integrations.
        /// </remarks>
        public OpenccConfig GetConfigId()
        {
            return _configId;
        }

        /// <summary>
        /// Sets the internal configuration from a string value.
        /// </summary>
        /// <param name="config">
        /// Configuration name to apply. Parsed in a case-insensitive manner.
        /// </param>
        /// <param name="setLastError">
        /// Whether to record an error message when the input is invalid.
        /// </param>
        /// <remarks>
        /// This method is the internal entry point for all string-based configuration updates.
        /// It enforces the single-owner rule by updating <c>_configId</c> only.
        /// <para/>
        /// Invalid values cause the configuration to fall back to the default
        /// and optionally update the internal error state.
        /// </remarks>
        private void SetConfigInternal(string config, bool setLastError)
        {
            if (TryParseConfig(config, out var id))
            {
                _configId = id;
                if (setLastError) _lastError = null;
                return;
            }

            _configId = DefaultConfigId;
            if (setLastError)
                _lastError = "Invalid config provided: \"" + config + "\". Using default '" + DefaultConfigName + "'.";
        }

        /// <summary>
        /// Sets the internal configuration from an <see cref="OpenccConfig"/> identifier.
        /// </summary>
        /// <param name="id">
        /// Configuration identifier to apply.
        /// </param>
        /// <param name="setLastError">
        /// Whether to record an error message when the input is invalid.
        /// </param>
        /// <remarks>
        /// This method is the internal entry point for enum-based configuration updates.
        /// It validates the identifier against the known configuration map
        /// and enforces the single-owner rule by updating <c>_configId</c> only.
        /// <para/>
        /// Invalid enum values (for example, values created by casting integers)
        /// cause the configuration to fall back to the default
        /// and optionally update the internal error state.
        /// </remarks>
        private void SetConfigInternal(OpenccConfig id, bool setLastError)
        {
            // Reject unknown enum values (e.g. cast from int)
            if (IdToName.TryGetValue(id, out _))
            {
                _configId = id;
                if (setLastError) _lastError = null;
                return;
            }

            _configId = DefaultConfigId;
            if (setLastError)
                _lastError = "Invalid config id provided: " + id + ". Using default '" + DefaultConfigName + "'.";
        }

        #endregion // Instance Configs

        #region Delimiters Region

        /// <summary>
        /// Full delimiter set (spaces, punctuation, brackets, symbols, etc.).
        /// Centralized here so it’s easy to audit/extend.
        /// </summary>
        private const string FullDelimiters =
            " \t\n\r!\"#$%&'()*+,-./:;<=>?@[\\]^_{}|~＝、。﹁﹂—－（）《》〈〉？！…／＼︒︑︔︓︿﹀︹︺︙︐［﹇］﹈︕︖︰︳︴︽︾︵︶｛︷｝︸﹃﹄【︻】︼　～．，；：";

        /// <summary>
        /// Provides fast O(1) delimiter membership checks using a precomputed bitset table.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Each of the 65,536 possible <see cref="char"/> values is mapped to one bit
        /// in a compact <c>ulong[]</c> table (8 KB total).
        /// </para>
        /// <para>
        /// This allows membership testing to be reduced to a single bitwise operation,
        /// which is far faster than <see cref="HashSet{T}.Contains"/> in hot per-character loops,
        /// especially for large text (e.g. multi-MB documents).
        /// </para>
        /// <para>
        /// The table is initialized once in the static constructor by setting the bit
        /// corresponding to each delimiter character in <c>FullDelimiters</c>.
        /// </para>
        /// </remarks>
        private static class DelimiterTable
        {
            private static readonly ulong[] T = new ulong[0x10000 / 64];

            static DelimiterTable()
            {
                foreach (var ch in FullDelimiters)
                {
                    T[ch >> 6] |= 1UL << (ch & 63);
                }
            }

            /// <summary>
            /// Tests whether the given character is a delimiter.
            /// </summary>
            /// <param name="c">The character to test.</param>
            /// <returns>
            /// <c>true</c> if <paramref name="c"/> is in the delimiter set;
            /// otherwise, <c>false</c>.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static bool Contains(char c) => (T[c >> 6] & (1UL << (c & 63))) != 0;
        }

        /// <summary>
        /// Inline helper method that wraps <see cref="DelimiterTable.Contains"/>.
        /// </summary>
        /// <remarks>
        /// This method exists mainly for code readability at call sites.  
        /// Because it is marked with <see cref="MethodImplOptions.AggressiveInlining"/>,
        /// the JIT will inline it, so it has effectively zero overhead at runtime.
        /// </remarks>
        /// <param name="c">The character to test.</param>
        /// <returns>
        /// <c>true</c> if <paramref name="c"/> is a delimiter; otherwise, <c>false</c>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDelimiter(char c) => DelimiterTable.Contains(c);

        #endregion // Delimiters Region

        #region Lazy Static Dictionary Region

        // --- START Lazy<T> Implementation ---

        // Use Lazy<T> for the dictionary and derived lists
        // Initialize these in the static constructor.
        private static Lazy<DictionaryMaxlength> _lazyDictionary;

        /// <summary>
        /// Gets the loaded dictionary set for all conversion types.
        /// This property will lazily load the default dictionary if no custom one has been set.
        /// </summary>
        private static DictionaryMaxlength Dictionary => _lazyDictionary.Value;

        // Static constructor to initialize the Lazy<T> instances.
        // This runs once, automatically and thread-safely, when the Opencc class is first accessed.
        static Opencc()
        {
            Warmup();
        }

        /// <summary>
        /// Preloads the default dictionary and initializes all derived lookup tables  
        /// used internally by the <see cref="Opencc"/> class.
        /// </summary>
        /// <remarks>
        /// This method is invoked once by the static constructor to ensure that the default  
        /// <see cref="DictionaryLib"/> instance and its associated conversion metadata  
        /// (such as starter masks and plan caches) are ready for immediate use.
        ///
        /// Optionally, a brief warm-up block can be enabled to trigger JIT compilation,  
        /// tiered PGO optimization, and initialization of global <see cref="DictionaryLib.PlanCache"/>  
        /// entries for the most common conversion paths (<c>S2T</c> and <c>T2S</c>).
        ///
        /// Uncomment the warm-up section below if you wish to minimize first-use latency in  
        /// long-running or GUI applications. For short-lived console tools, it is not necessary.  
        /// The operation is side effect free and does not modify dictionary contents.
        /// </remarks>
        private static void Warmup()
        {
            var dict = DictionaryLib.New(); // Load default configuration
            InitializeLazyLoaders(dict); // Initialize with the default dictionary

            // Optional warm-up for JIT + Tiered PGO + PlanCache.
            // --------------------------------------------------
            // Uncomment the section below to pre-cache hot paths
            // (recommended for GUI or service applications).
            /*
            const string text = "預熱文本 Sample 測試 Warmup";
            var dummy = new Opencc();
            _ = dummy.S2T(text);
            _ = dummy.S2T(text, true);
            _ = dummy.T2S(text);
            _ = dummy.T2S(text, true);
            */
        }

        /// <summary>
        /// Helper method to create the Lazy<T/> instances for the dictionary and its derived lists.
        /// This is used both for default loading and when a custom dictionary is provided.
        /// </summary>
        /// <param name="initialDictionary">The dictionary instance to use for initialization.</param>
        private static void InitializeLazyLoaders(DictionaryMaxlength initialDictionary)
        {
            // The factory method for _lazyDictionary.
            // This ensures that the dictionary is set up, and then the derived lists are configured based on it.
            _lazyDictionary =
                new Lazy<DictionaryMaxlength>(() => initialDictionary,
                    LazyThreadSafetyMode.ExecutionAndPublication); // Ensures thread safety for the Lazy<T> itself
        }

        // === Public Static Methods for Custom Dictionary Loading (Optional for Users) ===

        /// <summary>
        /// Overrides the default OpenCC planning source with a custom  
        /// <see cref="DictionaryMaxlength"/> instance by updating the global  
        /// <see cref="DictionaryLib.PlanCache"/> provider and clearing all cached plans.  
        /// </summary>
        /// <remarks>
        /// Call this method to apply a custom dictionary at runtime for specialized  
        /// conversion needs (for example, user-defined terminology or modified mappings).  
        /// <para>
        /// This operation does <b>not</b> replace or modify the default lazy-loaded  
        /// dictionary (<c>DefaultLib.Value</c>); only the global planning provider  
        /// is swapped. Subsequent conversions will use the supplied custom dictionary  
        /// for new plan builds, while the original default remains intact and accessible.  
        /// </para>
        /// <para>
        /// Thread-safe: the provider swap is performed atomically, and the global  
        /// <see cref="DictionaryLib.PlanCache"/> is cleared to ensure that all  
        /// new conversions use the custom provider.  
        /// Existing conversions already in progress will continue using their  
        /// previously built plans.  
        /// </para>
        /// </remarks>
        /// <param name="customDictionary">
        /// The custom <see cref="DictionaryMaxlength"/> instance to use for  
        /// future conversion plan construction.  
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="customDictionary"/> is <see langword="null"/>.
        /// </exception>
        public static void UseCustomDictionary(DictionaryMaxlength customDictionary)
        {
            if (customDictionary is null)
                throw new ArgumentNullException(nameof(customDictionary), "Custom dictionary cannot be null.");

            // Swap only the plan cache provider and clear caches.
            // The default dictionary (DefaultLib.Value) remains intact.
            DictionaryLib.SetDictionaryProvider(customDictionary);
        }

        /// <summary>
        /// Restores the global dictionary provider to the default built-in instance  
        /// and clears all cached conversion plans in <see cref="DictionaryLib.PlanCache"/>.  
        /// </summary>
        /// <remarks>
        /// Call this method to revert the active dictionary provider back to the default  
        /// <see cref="DictionaryMaxlength"/> loaded from embedded resources.  
        /// <para>
        /// This method only resets the planning provider; the original lazy-loaded  
        /// dictionary (<c>DefaultLib.Value</c>) remains intact and is reused.  
        /// No additional allocations occur, and existing <see cref="DictionaryMaxlength"/>  
        /// objects are not modified.  
        /// </para>
        /// <para>
        /// Thread-safe: the provider swap is performed atomically, and the global plan cache  
        /// is fully cleared to ensure that all subsequent conversions use the restored default.  
        /// </para>
        /// </remarks>
        public static void UseDefaultDictionary()
        {
            // Restore the default provider and clear the plan cache.
            DictionaryLib.ResetDictionaryProviderToDefault();
        }

        /// <summary>
        /// Overrides the planning source by loading a dictionary from a specified path
        /// and applying it to <see cref="DictionaryLib.PlanCache"/>.
        /// </summary>
        /// <remarks>
        /// Leaves the default lazy dictionary untouched. Subsequent conversions build plans
        /// from the loaded custom dictionary.
        /// </remarks>
        /// <param name="dictionaryRelativePath">The path to the dictionary file(s).</param>
        public static void UseDictionaryFromPath(string dictionaryRelativePath)
        {
            if (string.IsNullOrWhiteSpace(dictionaryRelativePath)) return;

            var custom = DictionaryLib.FromDicts(dictionaryRelativePath);
            UseCustomDictionary(custom);
        }

        /// <summary>
        /// Overrides the planning source by parsing a JSON representation of
        /// <see cref="DictionaryMaxlength"/> and applying it to <see cref="DictionaryLib.PlanCache"/>.
        /// </summary>
        /// <remarks>
        /// Leaves the default lazy dictionary untouched. Subsequent conversions build plans
        /// from the parsed custom dictionary.
        /// </remarks>
        /// <param name="jsonString">The JSON string representing the dictionary.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the JSON payload does not deserialize into a valid dictionary.
        /// </exception>
        public static void UseDictionaryFromJsonString(string jsonString)
        {
            if (string.IsNullOrWhiteSpace(jsonString)) return;

            var custom = JsonSerializer.Deserialize<DictionaryMaxlength>(jsonString);
            if (custom is null)
                throw new InvalidOperationException("Failed to deserialize DictionaryMaxlength from JSON.");

            UseCustomDictionary(custom);
        }

        // --- END Lazy<T> Implementation ---

        #endregion // Lazy Static Dictionary Region

        #region Opencc Constructor and Public Fields Region

        /// <summary>
        /// Initializes a new instance of the <see cref="Opencc"/> class with the specified configuration.
        /// This constructor ensures the global dictionary and its associated lists are initialized.
        /// </summary>
        /// <param name="config">The conversion configuration (e.g., "s2t", "t2s").</param>
        public Opencc(string config = null)
        {
            Config = config;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Opencc"/> class using an <see cref="OpenccConfig"/> enum value.
        /// This overload is the preferred way to create an Opencc instance.
        /// </summary>
        /// <param name="configEnum">The OpenCC conversion configuration enum value.</param>
        public Opencc(OpenccConfig configEnum)
        {
            // Single-owner rule: set via enum path
            SetConfigInternal(configEnum, setLastError: false);
        }

        /// <summary>
        /// Gets the last error message, if any, from the most recent operation.
        /// </summary>
        public string GetLastError()
        {
            return _lastError;
        }

        #endregion // Opencc Constructor and Public Fields Region

        #region Pre-Splitting and Pre-Chunking Region

        /// <summary>
        /// Represents a contiguous batch of text segments (split ranges) to be processed
        /// sequentially within a single parallel worker.
        /// </summary>
        /// <remarks>
        /// Each <see cref="Chunk"/> groups several adjacent <see cref="Range"/> entries
        /// from the same input text, forming a coarse partition that balances workload
        /// across threads while maintaining input order.
        /// </remarks>
        private readonly struct Chunk
        {
            /// <summary>
            /// The zero-based index of the first <see cref="Range"/> included in this chunk.
            /// </summary>
            public readonly int FirstRange;

            /// <summary>
            /// The total number of <see cref="Range"/> items grouped into this chunk.
            /// </summary>
            public readonly int Count;

            /// <summary>
            /// Estimated total number of UTF-16 characters across all ranges in this chunk.
            /// Used to pre-size <see cref="StringBuilder"/> capacity for minimal reallocations.
            /// </summary>
            public readonly int EstChars;

            /// <summary>
            /// Initializes a new instance of the <see cref="Chunk"/> struct.
            /// </summary>
            /// <param name="first">Index of the first range in this chunk.</param>
            /// <param name="count">Number of ranges contained in this chunk.</param>
            /// <param name="estChars">
            /// Estimated total character count of all ranges; used for capacity pre-allocation.
            /// </param>
            public Chunk(int first, int count, int estChars)
            {
                FirstRange = first;
                Count = count;
                EstChars = estChars;
            }
        }

        /// <summary>
        /// Builds coarse-grained partitions ("chunks") from a list of split text ranges,
        /// improving load-balancing and cache locality for
        /// <see cref="Parallel.For(int,int,Action{int})"/> operations.
        /// </summary>
        /// <param name="ranges">
        /// The list of delimiter-inclusive <see cref="Range"/> segments to partition.
        /// </param>
        /// <param name="batchSize">
        /// Approximate number of ranges per chunk. 128–512 is a good starting point,
        /// depending on typical segment size.
        /// </param>
        /// <returns>
        /// A list of <see cref="Chunk"/> instances describing balanced,
        /// contiguous partitions of the input ranges.
        /// </returns>
        /// <remarks>
        /// <para>
        /// This method groups adjacent <see cref="Range"/> entries until the specified
        /// <paramref name="batchSize"/> is reached, estimating total character counts
        /// to pre-allocate per-chunk <see cref="StringBuilder"/> instances efficiently.
        /// </para>
        /// <para>
        /// Pre-chunking substantially reduces task-scheduling overhead and improves
        /// the performance of <see cref="Parallel.For(int,int,Action{int})"/> by feeding it coarser,
        /// more predictable work units.
        /// </para>
        /// </remarks>
        private static List<Chunk> BuildChunks(IReadOnlyList<Range> ranges, int batchSize = 256)
        {
            var chunks = new List<Chunk>(Math.Max(1, ranges.Count / batchSize + 1));

            var i = 0;
            while (i < ranges.Count)
            {
                var take = Math.Min(batchSize, ranges.Count - i);

                // Estimate total character length in this chunk
                var estChars = 0;
                for (var k = 0; k < take; k++)
                    estChars += ranges[i + k].Length;

                chunks.Add(new Chunk(i, take, estChars));
                i += take;
            }

            return chunks;
        }

        /// <summary>
        /// Centralized thresholds and tuning parameters controlling how  
        /// OpenCC conversion routines choose between linear and parallel paths.  
        /// </summary>
        /// <remarks>
        /// These constants define when the converter switches from single-threaded  
        /// to parallel execution and how data chunks are distributed among workers.  
        /// 
        /// Values are selected based on typical performance profiles observed  
        /// on multicore CPUs (e.g., Intel i5–i9, AMD Ryzen).  
        /// Adjusting them can fine-tune performance for specific workloads or  
        /// hardware configurations, but the defaults already yield optimal results  
        /// for most CJK document conversions.
        /// </remarks>
        private static class ConvertTuning
        {
            /// <summary>
            /// Character-length threshold below which the converter runs  
            /// purely linear (single-threaded) for minimal scheduling overhead.  
            /// Defaults to <c>8 000</c> on ≤4-core CPUs, <c>10 000</c> on larger systems.  
            /// </summary>
            public static readonly int LinearCutoffChars =
                Environment.ProcessorCount <= 4 ? 8_000 : 10_000;

            /// <summary>
            /// Minimum text length required to trigger parallel processing.  
            /// Below this threshold, sequential stitching remains faster.  
            /// Defaults to <c>150 000</c> on ≤4-core CPUs, <c>100 000</c> otherwise.  
            /// </summary>
            public static readonly int ParallelTextGate =
                Environment.ProcessorCount <= 4 ? 150_000 : 100_000;

            /// <summary>
            /// Split-range count threshold: if the number of logical text  
            /// segments exceeds this value, the converter may engage  
            /// parallel processing even if the text length is smaller.  
            /// </summary>
            public const int ParallelRangeGate = 1_000;

            /// <summary>
            /// Default batch size (number of ranges per chunk) used when  
            /// constructing <c>BuildChunks()</c> for <see cref="Parallel.For(int,int,Action{int})"/>.  
            /// A value of 256 balances task granularity and scheduling overhead.  
            /// </summary>
            public const int BatchSize = 256;
        }

        /// <summary>
        /// Performs segmented dictionary-based conversion on the specified text.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The input text is split into delimiter-aware segments (via
        /// <see cref="GetSplitRangesSpan(ReadOnlySpan{char}, bool)"/>), and each
        /// segment is converted using <see cref="ConvertByUnion(ReadOnlySpan{char}, DictWithMaxLength[], StarterUnion)"/>.
        /// </para>
        /// <para>
        /// For large or highly fragmented inputs, segments are pre-partitioned into
        /// balanced <see cref="Chunk"/> groups and processed in parallel using
        /// <see cref="Parallel.For(int,int,Action{int})"/> to maximize throughput and CPU utilization.
        /// </para>
        /// <para>
        /// For small texts or limited segment counts, a single-threaded sequential
        /// path is chosen to avoid thread-pool overhead.
        /// </para>
        /// <para>
        /// The method reuses a global <see cref="StringBuilder"/> pre-sized to
        /// <c>textLength × 1.068</c> to minimize reallocation and GC pressure, which
        /// performs better than <see cref="string.Concat(string[])"/> on
        /// .NET Standard 2.0.
        /// </para>
        /// </remarks>
        /// <param name="text">The input text to convert.</param>
        /// <param name="dictionaries">
        /// The collection of <see cref="DictWithMaxLength"/> instances used for
        /// dictionary-based phrase and character replacement.
        /// </param>
        /// <param name="union">
        /// The <see cref="StarterUnion"/> providing starter-character metadata and
        /// length-mask gating for each dictionary.
        /// </param>
        /// <returns>
        /// A converted string with all applicable dictionary replacements applied.
        /// </returns>
        private static string SegmentReplace(
            string text,
            DictWithMaxLength[] dictionaries,
            StarterUnion union)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var textLength = text.Length;

            // Small texts → run single-threaded for lower scheduling overhead.
            if (textLength < ConvertTuning.LinearCutoffChars)
                return ConvertByUnion(text.AsSpan(), dictionaries, union);

            var splitRanges = GetSplitRangesSpan(text.AsSpan(), inclusive: true);

            // Global builder reused for both serial and parallel stitching.
            var sb = new StringBuilder(textLength + (textLength >> 4)); // +6.25% headroom (~6.8%)

            // Sequential path for small or moderately sized input.
            if (splitRanges.Count <= ConvertTuning.ParallelRangeGate && textLength <= ConvertTuning.ParallelTextGate)
            {
                for (var i = 0; i < splitRanges.Count; i++)
                {
                    var r = splitRanges[i];
                    sb.Append(ConvertByUnion(
                        text.AsSpan(r.Start, r.Length),
                        dictionaries, union));
                }

                return sb.ToString();
            }

            // Parallel path for large inputs -------------------------------------
            var chunks = BuildChunks(splitRanges, batchSize: ConvertTuning.BatchSize);
            var parts = new string[chunks.Count];
            var po = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

            Parallel.For(0, chunks.Count, po, cIdx =>
            {
                var chunk = chunks[cIdx];
                var sbPart = new StringBuilder(chunk.EstChars + (chunk.EstChars >> 6));

                var end = chunk.FirstRange + chunk.Count;
                for (var i = chunk.FirstRange; i < end; i++)
                {
                    var r = splitRanges[i];
                    sbPart.Append(ConvertByUnion(
                        text.AsSpan(r.Start, r.Length),
                        dictionaries, union));
                }

                parts[cIdx] = sbPart.ToString();
            });

            // Reuse the same global StringBuilder for final stitching (NS2.0-friendly).
            for (var i = 0; i < parts.Length; i++)
                sb.Append(parts[i]);

            return sb.ToString();
        }

        #endregion

        #region Core Convertion Region

        /// <summary>
        /// Converts text using one or more dictionaries, matching the longest possible key
        /// at each position. 
        /// </summary>
        /// <remarks>
        /// The conversion process is optimized by using a <see cref="StarterUnion"/>:
        /// <list type="bullet">
        ///   <item>
        ///     <description>
        ///     O(1) lookup of per-starter metadata (maximum length, minimum length, length bitmask).
        ///     </description>
        ///   </item>
        ///   <item>
        ///     <description>
        ///     Fast path: single-grapheme replacements are applied immediately when no longer match
        ///     is possible.
        ///     </description>
        ///   </item>
        ///   <item>
        ///     <description>
        ///     General path: longest-first search within the range [<c>unionMinLen</c> … <c>tryMax</c>],
        ///     skipping impossible lengths using the precomputed bitmask.
        ///     </description>
        ///   </item>
        /// </list>
        /// Graphemes are determined by UTF-16 code units: BMP characters are length 1; surrogate pairs
        /// are length 2.  
        /// Fallback: if no dictionary entry matches, the original grapheme is emitted unchanged.
        /// </remarks>
        /// <param name="textSpan">
        /// The input text segment as a <see cref="ReadOnlySpan{Char}"/>.
        /// </param>
        /// <param name="dictionaries">
        /// The list of dictionaries to use for lookup. Each dictionary specifies its own
        /// <c>MinLength</c> and <c>MaxLength</c> bounds.
        /// </param>
        /// <param name="union">
        /// The precomputed <see cref="StarterUnion"/> that provides per-starter caps,
        /// masks, and minimum lengths.
        /// </param>
        /// <returns>
        /// A converted string with dictionary replacements applied.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string ConvertByUnion(
            ReadOnlySpan<char> textSpan,
            DictWithMaxLength[] dictionaries,
            StarterUnion union)
        {
            var n = textSpan.Length;
            switch (n)
            {
                case 0: return string.Empty;
                case 1 when IsDelimiter(textSpan[0]): return textSpan.ToString();
            }

            var sb = StringBuilderCache.Value;
            sb.Clear();
            sb.EnsureCapacity(n * 2 + (n >> 4));

            var i = 0;
            var keyBuffer = ArrayPool<char>.Shared.Rent(union.GlobalCap);

            try
            {
                while (i < n)
                {
                    var c0 = textSpan[i];
                    var hasSecond = i + 1 < n;
                    var c1 = hasSecond ? textSpan[i + 1] : '\0';

                    // Grapheme step: BMP=1, surrogate pair=2
                    // var step = char.IsHighSurrogate(c0) && remaining.Length > 1 && char.IsLowSurrogate(remaining[1])
                    //     ? 2
                    //     : 1;
                    // var step = (uint)(c0 - 0xD800) <= 0x03FF && hasSecond && (uint)(c1 - 0xDC00) <= 0x03FF ? 2 : 1;

                    // union.Get(c0, out var cap, out var lenMaskAll, out var unionMinLen);
                    // CHANGED: ask StarterUnion for step (starterUnits), presence, and masks.
                    union.GetAt(c0, c1, hasSecond,
                        out var step, out var hasStarter,
                        out var cap, out var lenMaskAll, out var unionMinLen);

                    // Upper bound: dict cap, remaining input, and caller max
                    var remaining = n - i;
                    // var tryMax = Math.Min(Math.Min(maxWordLength, remaining), cap);
                    var tryMax = Math.Min(cap, remaining);

                    // No possible match? (no entries, or entries longer than we can take)
                    if (!hasStarter || cap == 0 || unionMinLen == 0 || unionMinLen > tryMax)
                    {
                        sb.Append(c0);
                        if (step == 2) sb.Append(c1);
                        i += step;
                        continue;
                    }

                    // Is there any candidate longer than the current grapheme size?
                    bool hasLonger;
                    if (tryMax < 64)
                    {
                        var maskUpToTry = lenMaskAll & ((1UL << tryMax) - 1);
                        hasLonger = step < tryMax && maskUpToTry >> step != 0UL;
                    }
                    else
                    {
                        // If tryMax >= 64 we can’t trim the mask cheaply → just check shift
                        hasLonger = lenMaskAll >> step != 0UL;
                    }

                    // Single-grapheme fast path when:
                    //  - there is a length==step candidate (bit set), AND
                    //  - there's no longer candidate to prefer, AND
                    //  - step >= minLen (NEW: respect lower bound)
                    if (!hasLonger &&
                        step >= unionMinLen &&
                        ((lenMaskAll >> (step - 1)) & 1UL) != 0UL)
                    {
                        keyBuffer[0] = c0;
                        if (step == 2) keyBuffer[1] = c1;

                        string keyStep = null;

                        for (var di = 0; di < dictionaries.Length; di++)
                        {
                            var d = dictionaries[di];
                            if (!d.SupportsLength(step)) continue;

                            if (keyStep == null)
                                keyStep = new string(keyBuffer, 0, step);
                            if (!d.TryGetValue(keyStep, out var repl)) continue;

                            sb.Append(repl);
                            i += step;
                            goto ContinueOuter;
                        }
                    }

                    // General longest-first search, narrowed to [minLen … tryMax]
                    string bestMatch = null;
                    var bestLen = 0;

                    // Copy once at max candidate length
                    // (We still need a copy because dict keys are strings)
                    textSpan.Slice(i, tryMax).CopyTo(keyBuffer);

                    // CHANGED: enforce lower bound = max(unionMinLen, step)
                    //  - ensures we never test len < step (e.g., len==1 for astral starters)
                    //  - len==1 for astrals is already masked out, but this makes it explicit and self-documenting
                    var lower = unionMinLen > step ? unionMinLen : step;

                    for (var len = tryMax; len >= lower; --len)
                    {
                        if (len <= 64 && ((lenMaskAll >> (len - 1)) & 1UL) == 0UL)
                            continue; // impossible length per mask

                        string key = null;

                        for (var di = 0; di < dictionaries.Length; di++)
                        {
                            var d = dictionaries[di];
                            if (!d.SupportsLength(len)) continue;

                            if (key == null)
                                key = new string(keyBuffer, 0, len);
                            if (!d.TryGetValue(key, out var repl)) continue;

                            bestMatch = repl;
                            bestLen = len;
                            goto Found;
                        }
                    }

                    Found:
                    if (bestMatch != null)
                    {
                        sb.Append(bestMatch);
                        i += bestLen;
                    }
                    else
                    {
                        // Reuse the precomputed step and c1 — no need to recompute
                        sb.Append(textSpan[i]);
                        if (step == 2) sb.Append(c1);
                        i += step;
                    }

                    ContinueOuter: ;
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(keyBuffer, clearArray: false);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Converts a span of characters using the provided dictionaries, matching the longest possible key at each position.
        /// </summary>
        /// <remarks>
        /// This method is the legacy, backward-compatible implementation of the conversion routine,
        /// updated to accept <see cref="ReadOnlySpan{Char}"/> for improved performance and reduced
        /// allocations. It preserves the same behavior as the original <c>ConvertBy(string,...)</c>
        /// overload, including delimiter handling and longest-match dictionary lookup.
        /// </remarks>
        /// <param name="text">
        /// The input text segment as a <see cref="ReadOnlySpan{Char}"/>.
        /// </param>
        /// <param name="dictionaries">
        /// The dictionaries to use for lookup. Each dictionary is paired with its maximum supported key length.
        /// </param>
        /// <param name="maxWordLength">
        /// The maximum key length to consider during matching. Longer candidates are truncated to this value.
        /// </param>
        /// <returns>
        /// The converted string segment, with all possible matches replaced by their dictionary values.
        /// If no match is found at a position, the original character is preserved.
        /// </returns>
        /// <seealso cref="ConvertByUnion(ReadOnlySpan{char}, DictWithMaxLength[], StarterUnion)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string ConvertBy(
            ReadOnlySpan<char> text,
            DictWithMaxLength[] dictionaries,
            int maxWordLength)
        {
            if (text.IsEmpty)
                return string.Empty;

            var textLen = text.Length;

            // Fast path: single delimiter
            if (textLen == 1 && IsDelimiter(text[0]))
                return text.ToString();

            var sb = StringBuilderCache.Value;
            sb.Clear();
            sb.EnsureCapacity(textLen * 2 + (textLen >> 4));

            var i = 0;

            // Ensure we rent at least length 1
            var bufferLen = Math.Max(1, maxWordLength);
            var keyBuffer = ArrayPool<char>.Shared.Rent(bufferLen);

            try
            {
                while (i < textLen)
                {
                    var remaining = text.Slice(i);
                    var tryMaxLen = Math.Min(maxWordLength, remaining.Length);

                    string bestMatch = null;
                    var bestMatchLength = 0;

                    // Descend from longest to shortest
                    for (var length = tryMaxLen; length > 0; --length)
                    {
                        string key = null;

                        // Probe dictionaries lazily:
                        // Only materialize the key when at least one dictionary supports this length.
                        for (var d = 0; d < dictionaries.Length; d++)
                        {
                            var dict = dictionaries[d];
                            if (!dict.SupportsLength(length))
                                continue;

                            if (key == null)
                            {
                                var wordSpan = remaining.Slice(0, length);
                                wordSpan.CopyTo(keyBuffer.AsSpan(0, length));
                                key = new string(keyBuffer, 0, length);
                            }

                            if (!dict.TryGetValue(key, out var match))
                                continue;

                            bestMatch = match;
                            bestMatchLength = length;
                            goto FoundMatch;
                        }
                    }

                    FoundMatch:
                    if (bestMatch != null)
                    {
                        sb.Append(bestMatch);
                        i += bestMatchLength;
                    }
                    else
                    {
                        sb.Append(text[i]);
                        i++;
                    }
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(keyBuffer, clearArray: false);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Represents a half-open character range [Start, End) within an input span.
        /// Used internally by OpenCC to track non-delimiter and delimiter segments.
        /// </summary>
        private readonly struct Range
        {
            /// <summary>
            /// Inclusive start index of the range (0-based).
            /// </summary>
            public int Start { get; }

            /// <summary>
            /// Exclusive end index of the range (0-based).
            /// </summary>
            private int End { get; }

            /// <summary>
            /// Initializes a new <see cref="Range"/> with the specified boundaries.
            /// </summary>
            /// <param name="start">Inclusive start index.</param>
            /// <param name="end">Exclusive end index.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Range(int start, int end)
            {
                Start = start;
                End = end;
            }

            /// <summary>
            /// Gets the length of the range (<c>End - Start</c>).
            /// </summary>
            public int Length => End - Start;

            /// <summary>
            /// Returns a string representation in the form <c>[Start, End)</c>.
            /// </summary>
            public override string ToString() => $"[{Start}, {End})";
        }

        /// <summary>
        /// Splits the input span into contiguous ranges of non-delimiter and delimiter segments.
        /// <para>
        /// This implementation uses a fast bit-level <see cref="IsDelimiter"/> check
        /// for compatibility and performance on .NET Standard 2.0.
        /// </para>
        /// </summary>
        /// <param name="input">The text span to segment.</param>
        /// <param name="inclusive">
        /// If <c>true</c>, each delimiter is included at the end of its preceding segment;
        /// otherwise a delimiter at the start becomes its own segment.
        /// </param>
        /// <returns>
        /// A list of <see cref="Range"/> objects representing half-open intervals [Start, End).
        /// </returns>
        /// Note: inclusive mode still emits delimiter-only ranges for leading/consecutive delimiters.
        private static List<Range> GetSplitRangesSpan(ReadOnlySpan<char> input, bool inclusive = false)
        {
            var length = input.Length;
            if (length == 0)
                return new List<Range>(0);

            // Heuristic: inclusive ≈ 25 % delimiters, exclusive ≈ 50 %.
            var estSegments = inclusive ? (length >> 2) + 1 : (length >> 1) + 1;
            if (estSegments < 8) estSegments = 8;
            if (estSegments > length) estSegments = length;

            var ranges = new List<Range>(estSegments);
            var currentStart = 0;

            for (var i = 0; i < length; i++)
            {
                if (!IsDelimiter(input[i])) continue;

                if (inclusive)
                {
                    // include delimiter in same segment
                    ranges.Add(new Range(currentStart, i + 1));
                }
                else
                {
                    if (i > currentStart)
                        ranges.Add(new Range(currentStart, i)); // text before delimiter

                    ranges.Add(new Range(i, i + 1)); // delimiter itself
                }

                currentStart = i + 1;
            }

            // Add trailing range if text doesn't end with a delimiter
            if (currentStart < length)
                ranges.Add(new Range(currentStart, length));

            return ranges;
        }

        #endregion

        #region Direct API and General Conversion Region

        /// <summary>
        /// Retrieves a cached <see cref="DictRefs"/> instance for the specified  
        /// OpenCC conversion configuration and punctuation mode.
        /// </summary>
        /// <remarks>
        /// This method obtains prebuilt dictionary references and lookup structures
        /// from the global <see cref="DictionaryLib.PlanCache"/> initialized with  
        /// <see cref="DictionaryLib.Provider"/>.  
        /// By serving results from the cache rather than rebuilding plans on demand,
        /// it minimizes redundant allocations, improves performance consistency,
        /// and reduces GC pressure during high-throughput text conversions.
        /// </remarks>
        /// <param name="configEnum">
        /// The OpenCC conversion configuration (e.g., <c>S2T</c>, <c>T2HK</c>, <c>TW2S</c>).
        /// </param>
        /// <param name="punctuation">
        /// When <see langword="true"/>, includes punctuation conversion;  
        /// when <see langword="false"/>, punctuation characters remain unchanged.
        /// </param>
        /// <returns>
        /// A <see cref="DictRefs"/> object containing the prepared dictionary references
        /// and lookup tables for the given configuration.
        /// </returns>
        private static DictRefs GetDictRefs(OpenccConfig configEnum, bool punctuation)
            => DictionaryLib.PlanCache.GetPlan(configEnum, punctuation);

        /// <summary>
        /// Converts Simplified Chinese to Traditional Chinese.
        /// </summary>
        /// <param name="inputText">The input text.</param>
        /// <param name="punctuation">Whether to convert punctuation as well.</param>
        /// <returns>The converted text.</returns>
        public string S2T(string inputText, bool punctuation = false)
        {
            var refs = GetDictRefs(OpenccConfig.S2T, punctuation);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Traditional Chinese to Simplified Chinese.
        /// </summary>
        /// <param name="inputText">The input text.</param>
        /// <param name="punctuation">Whether to convert punctuation as well.</param>
        /// <returns>The converted text.</returns>
        public string T2S(string inputText, bool punctuation = false)
        {
            var refs = GetDictRefs(OpenccConfig.T2S, punctuation);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Simplified Chinese to Traditional Chinese (Taiwan standard).
        /// </summary>
        public string S2Tw(string inputText, bool punctuation = false)
        {
            var refs = GetDictRefs(OpenccConfig.S2Tw, punctuation);
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

        /// <summary>
        /// Converts Traditional Chinese (Taiwan standard) to Simplified Chinese.
        /// </summary>
        public string Tw2S(string inputText, bool punctuation = false)
        {
            var refs = GetDictRefs(OpenccConfig.Tw2S, punctuation);
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

        /// <summary>
        /// Converts Simplified Chinese to Traditional Chinese (Taiwan standard, with phrase and variant rounds).
        /// </summary>
        public string S2Twp(string inputText, bool punctuation = false)
        {
            var refs = GetDictRefs(OpenccConfig.S2Twp, punctuation);
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

        /// <summary>
        /// Converts Traditional Chinese (Taiwan) to Simplified Chinese (with phrase and variant rounds).
        /// </summary>
        public string Tw2Sp(string inputText, bool punctuation = false)
        {
            var refs = GetDictRefs(OpenccConfig.Tw2Sp, punctuation);
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

        /// <summary>
        /// Converts Simplified Chinese to Hong Kong Traditional Chinese.
        /// </summary>
        public string S2Hk(string inputText, bool punctuation = false)
        {
            var refs = GetDictRefs(OpenccConfig.S2Hk, punctuation);
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

        /// <summary>
        /// Converts Hong Kong Traditional Chinese to Simplified Chinese.
        /// </summary>
        public string Hk2S(string inputText, bool punctuation = false)
        {
            var refs = GetDictRefs(OpenccConfig.Hk2S, punctuation);
            var output = refs.ApplySegmentReplace(inputText, SegmentReplace);
            return output;
        }

        /// <summary>
        /// Converts Traditional Chinese to Taiwan Traditional Chinese.
        /// </summary>
        public string T2Tw(string inputText)
        {
            var refs = GetDictRefs(OpenccConfig.T2Tw, false);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Traditional Chinese to Taiwan Traditional Chinese (with phrase and variant rounds).
        /// </summary>
        public string T2Twp(string inputText)
        {
            var refs = GetDictRefs(OpenccConfig.T2Twp, false);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Taiwan Traditional Chinese to Traditional Chinese.
        /// </summary>
        public string Tw2T(string inputText)
        {
            var refs = GetDictRefs(OpenccConfig.Tw2T, false);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Taiwan Traditional Chinese to Traditional Chinese (with phrase round).
        /// </summary>
        public string Tw2Tp(string inputText)
        {
            var refs = GetDictRefs(OpenccConfig.Tw2Tp, false);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Traditional Chinese to Hong Kong Traditional Chinese.
        /// </summary>
        public string T2Hk(string inputText)
        {
            var refs = GetDictRefs(OpenccConfig.T2Hk, false);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Hong Kong Traditional Chinese to Traditional Chinese.
        /// </summary>
        public string Hk2T(string inputText)
        {
            var refs = GetDictRefs(OpenccConfig.Hk2T, false);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Traditional Chinese to Japanese Kanji variants.
        /// </summary>
        public string T2Jp(string inputText)
        {
            var refs = GetDictRefs(OpenccConfig.T2Jp, false);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts Japanese Kanji variants to Traditional Chinese.
        /// </summary>
        public string Jp2T(string inputText)
        {
            var refs = GetDictRefs(OpenccConfig.Jp2T, false);
            return refs.ApplySegmentReplace(inputText, SegmentReplace);
        }

        /// <summary>
        /// Converts text according to the current <see cref="Config"/> setting.
        /// </summary>
        /// <param name="inputText">The input text.</param>
        /// <param name="punctuation">Whether to convert punctuation as well.</param>
        /// <returns>The converted text, or the original input if the config is invalid.</returns>
        public string Convert(string inputText, bool punctuation = false)
        {
            if (string.IsNullOrEmpty(inputText))
            {
                _lastError = "Input text is empty";
                return string.Empty;
            }

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

        #endregion

        #region zh-Hant / zh-Hans Detection Region

        /// <summary>
        /// Converts Simplified Chinese characters to Traditional Chinese (single character only).
        /// </summary>
        public static string St(string inputText)
        {
            var dictRefs = new[] { Dictionary.st_characters };
            // maxLength for surrogate pairs and non-BMP character is 2
            return ConvertBy(inputText.AsSpan(), dictRefs, 2);
        }

        /// <summary>
        /// Converts Traditional Chinese characters to Simplified Chinese (single character only).
        /// </summary>
        public static string Ts(string inputText)
        {
            var dictRefs = new[] { Dictionary.ts_characters };
            // maxLength for surrogate pairs and non-BMP character is 2
            return ConvertBy(inputText.AsSpan(), dictRefs, 2);
        }

        /// <summary>
        /// Checks if the input text is Simplified, Traditional, or neither.
        /// Returns 1 if Traditional, 2 if Simplified, 0 otherwise.
        /// </summary>
        /// <param name="inputText">The input text to check.</param>
        /// <returns>1 for Traditional, 2 for Simplified, 0 for neither.</returns>
        public static int ZhoCheck(string inputText)
        {
            if (string.IsNullOrEmpty(inputText)) return 0;

            var scanLength = inputText.Length > 500 ? 500 : inputText.Length;
            var stripped = StripRegex.Replace(inputText.Substring(0, scanLength), string.Empty);
            if (string.IsNullOrEmpty(stripped)) return 0;

            var stringInfo = new StringInfo(stripped);
            var lengthInElements = Math.Min(stringInfo.LengthInTextElements, 100);
            var safeText = stringInfo.SubstringByTextElements(0, lengthInElements);

            var tsConverted = Ts(safeText);
            if (safeText != tsConverted) return 1;

            var stConverted = St(safeText);
            return safeText != stConverted ? 2 : 0;
        }

        #endregion
    }
}