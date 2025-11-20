using System;
using System.Collections.Concurrent;

namespace OpenccNetLib
{
    // ---- Public-facing cache facade -------------------------------------------------------------

    /// <summary>
    /// Centralized cache for fully-built conversion plans and their  
    /// associated <see cref="StarterUnion"/> instances.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This cache has two layers:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <b>Primary cache:</b> Maps a <see cref="PlanKey"/> (combination of  
    /// <see cref="OpenccConfig"/> and punctuation setting) to a  
    /// <see cref="DictRefs"/> instance, which contains the dictionary sequence  
    /// ("rounds") and any per-round <see cref="StarterUnion"/> for fast lookups.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>Secondary cache:</b> Maps a <see cref="UnionKey"/> (semantic slot key)
    /// to a shared <see cref="StarterUnion"/> instance.
    /// Each <see cref="UnionKey"/> corresponds to a fixed, well-defined
    /// dictionary grouping (e.g., <c>S2T</c>, <c>T2S</c>, <c>TwRevPair</c>).
    /// This allows all conversion plans that reference the same logical
    /// dictionary slot to reuse the same <see cref="StarterUnion"/>,
    /// minimizing build time and memory usage.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    public sealed class ConversionPlanCache
    {
        /// <summary>
        /// Defines the semantic slot identifiers used to group dictionaries
        /// for building and caching <see cref="StarterUnion"/> instances.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Each <see cref="UnionKey"/> represents a fixed and well-defined
        /// dictionary combination (a “conversion slot”) corresponding to one
        /// logical stage of the OpenCC conversion pipeline.
        /// </para>
        /// <para>
        /// These keys are shared across all conversion plans so that
        /// identical slots (e.g., <c>S2T</c> or <c>TwRevPair</c>) reuse the
        /// same cached <see cref="StarterUnion"/>, improving both memory
        /// efficiency and startup performance.
        /// </para>
        /// </remarks>
        private enum UnionKey
        {
            // --- Simplified ↔ Traditional ---
            /// <summary>
            /// Simplified → Traditional main dictionaries (phrases + characters).
            /// </summary>
            S2T,

            /// <summary>
            /// Simplified → Traditional with punctuation conversion.
            /// </summary>
            S2TPunct,

            /// <summary>
            /// Traditional → Simplified main dictionaries (phrases + characters).
            /// </summary>
            T2S,

            /// <summary>
            /// Traditional → Simplified with punctuation conversion.
            /// </summary>
            T2SPunct,

            // --- Taiwan-specific ---
            /// <summary>
            /// Taiwan phrases only (excludes character-level variants).
            /// </summary>
            TwPhrasesOnly,

            /// <summary>
            /// Taiwan variants only (character-level).
            /// </summary>
            TwVariantsOnly,

            /// <summary>
            /// Taiwan reverse phrases only.
            /// </summary>
            TwPhrasesRevOnly,

            /// <summary>
            /// Taiwan reverse pair: variant reverse phrases + variant reverse characters.
            /// </summary>
            TwRevPair,

            /// <summary>
            /// Taiwan → Simplified round-1 triple: phrases_rev + variants_rev_phrases + variants_rev.
            /// </summary>
            Tw2SpR1TwRevTriple,

            // --- Hong Kong-specific ---
            /// <summary>
            /// Hong Kong variants only (character-level).
            /// </summary>
            HkVariantsOnly,

            /// <summary>
            /// Hong Kong reverse pair: variant reverse phrases + variant reverse characters.
            /// </summary>
            HkRevPair,

            // --- Japan-specific ---
            /// <summary>
            /// Japanese variants only (character-level).
            /// </summary>
            JpVariantsOnly,

            /// <summary>
            /// Japanese reverse triple: JPS phrases + JPS characters + JP variants_rev.
            /// </summary>
            JpRevTriple
        }

        /// <summary>
        /// Provides access to the current <see cref="DictionaryMaxlength"/> instance
        /// used when building new conversion plans and <see cref="StarterUnion"/> caches.
        /// </summary>
        /// <remarks>
        /// This delegate is typically supplied by the owning <c>Opencc</c> instance and
        /// allows the cache to always reference the latest loaded dictionary set,
        /// supporting scenarios such as hot-reloading or external dictionary replacement.
        /// </remarks>
        private readonly Func<DictionaryMaxlength> _dictionaryProvider;

        // Primary cache: (config, punct) -> DictRefs (rounds include unions)
        private readonly ConcurrentDictionary<PlanKey, DictRefs> _planCache =
            new ConcurrentDictionary<PlanKey, DictRefs>();

        // Secondary cache: round layout (list of dict IDs) -> StarterUnion
        private readonly ConcurrentDictionary<UnionKey, StarterUnion> _unionCacheByKey =
            new ConcurrentDictionary<UnionKey, StarterUnion>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ConversionPlanCache"/> class.
        /// </summary>
        /// <param name="dictionaryProvider">
        /// A delegate that returns the current <see cref="DictionaryMaxlength"/> instance
        /// to be used when constructing new conversion plans.
        /// <para>
        /// This provider is invoked lazily whenever a plan for a specific
        /// <see cref="OpenccConfig"/> and punctuation mode is requested,
        /// ensuring that the latest dictionary data is always used without
        /// requiring explicit cache updates.
        /// </para>
        /// Typically, this delegate references the main <c>Dictionary</c>
        /// instance owned by <c>Opencc</c> or <see cref="DictionaryLib.Provider"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="dictionaryProvider"/> is <see langword="null"/>.
        /// </exception>
        public ConversionPlanCache(Func<DictionaryMaxlength> dictionaryProvider)
        {
            _dictionaryProvider = dictionaryProvider ?? throw new ArgumentNullException(nameof(dictionaryProvider));
        }

        /// <summary>
        /// Retrieves a cached plan for the specified <paramref name="config"/>  
        /// and punctuation setting, or builds and caches a new plan if not found.
        /// </summary>
        /// <param name="config">The OpenCC conversion configuration.</param>
        /// <param name="punctuation">
        /// Whether the plan should include punctuation conversion dictionaries.
        /// </param>
        /// <returns>
        /// A <see cref="DictRefs"/> containing the ordered dictionaries and  
        /// per-round <see cref="StarterUnion"/> instances.
        /// </returns>
        public DictRefs GetPlan(OpenccConfig config, bool punctuation = false)
            => _planCache.GetOrAdd(new PlanKey(config, punctuation), _ => BuildPlan(config, punctuation));

        /// <summary>Clear all plan and union caches (e.g., after hot-reloading dictionaries).</summary>
        public void Clear()
        {
            _planCache.Clear();
            _unionCacheByKey.Clear();
        }

        // ---- Plan building ----------------------------------------------------------------------

        /// <summary>
        /// Constructs a fully resolved <see cref="DictRefs"/> conversion plan
        /// for the specified <paramref name="config"/> and punctuation setting.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method is the central factory responsible for assembling a complete
        /// conversion plan for a given <see cref="OpenccConfig"/> value.
        /// It determines which dictionary groups (“rounds”) are required,
        /// based on the target conversion configuration, and attaches a
        /// corresponding <see cref="StarterUnion"/> to each round.
        /// </para>
        /// <para>
        /// Each round’s <see cref="StarterUnion"/> is obtained through
        /// <see cref="GetOrAddUnionFor(DictionaryMaxlength, UnionKey, out DictWithMaxLength[])"/>,
        /// which uses a <see cref="UnionKey"/> to identify a predefined
        /// dictionary group (slot). This ensures that identical rounds
        /// across different conversion plans share the same cached
        /// <see cref="StarterUnion"/> instance, improving memory reuse and
        /// reducing redundant build time.
        /// </para>
        /// <para>
        /// Some configurations (for example, <c>S2Tw</c>, <c>Tw2S</c>, <c>Hk2S</c>)
        /// consist of two sequential rounds of dictionary application, while
        /// others (such as <c>S2T</c> and <c>T2S</c>) require only one round.
        /// Complex conversions like <c>S2Twp</c> and <c>Tw2Sp</c> may involve
        /// three rounds.  Each round is represented by its corresponding
        /// <see cref="UnionKey"/> entry.
        /// </para>
        /// </remarks>
        /// <param name="config">
        /// The <see cref="OpenccConfig"/> defining the type of conversion
        /// (e.g., Simplified→Traditional, Traditional→Simplified, Taiwan, Hong Kong, or Japan variants).
        /// </param>
        /// <param name="punctuation">
        /// Whether punctuation conversion dictionaries should be included in the plan.
        /// </param>
        /// <returns>
        /// A fully initialized <see cref="DictRefs"/> instance containing all dictionary
        /// rounds and their associated <see cref="StarterUnion"/> accelerators.
        /// </returns>
        private DictRefs BuildPlan(OpenccConfig config, bool punctuation)
        {
            var d = _dictionaryProvider();

            switch (config)
            {
                case OpenccConfig.S2T:
                {
                    var u1 = GetOrAddUnionFor(d, punctuation ? UnionKey.S2TPunct : UnionKey.S2T, out var r1);
                    return new DictRefs(r1, u1);
                }

                case OpenccConfig.T2S:
                {
                    var u1 = GetOrAddUnionFor(d, punctuation ? UnionKey.T2SPunct : UnionKey.T2S, out var r1);
                    return new DictRefs(r1, u1);
                }

                case OpenccConfig.S2Tw:
                {
                    var u1 = GetOrAddUnionFor(d, punctuation ? UnionKey.S2TPunct : UnionKey.S2T, out var r1);
                    var u2 = GetOrAddUnionFor(d, UnionKey.TwVariantsOnly, out var r2);
                    return new DictRefs(r1, u1).WithRound2(r2, u2);
                }

                case OpenccConfig.Tw2S:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.TwRevPair, out var r1);
                    var u2 = GetOrAddUnionFor(d, punctuation ? UnionKey.T2SPunct : UnionKey.T2S, out var r2);
                    return new DictRefs(r1, u1).WithRound2(r2, u2);
                }

                case OpenccConfig.S2Twp:
                {
                    var u1 = GetOrAddUnionFor(d, punctuation ? UnionKey.S2TPunct : UnionKey.S2T, out var r1);
                    var u2 = GetOrAddUnionFor(d, UnionKey.TwPhrasesOnly, out var r2);
                    var u3 = GetOrAddUnionFor(d, UnionKey.TwVariantsOnly, out var r3);
                    return new DictRefs(r1, u1).WithRound2(r2, u2).WithRound3(r3, u3);
                }

                case OpenccConfig.Tw2Sp:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.Tw2SpR1TwRevTriple, out var r1);
                    var u2 = GetOrAddUnionFor(d, punctuation ? UnionKey.T2SPunct : UnionKey.T2S, out var r2);
                    return new DictRefs(r1, u1).WithRound2(r2, u2);
                }

                case OpenccConfig.S2Hk:
                {
                    var u1 = GetOrAddUnionFor(d, punctuation ? UnionKey.S2TPunct : UnionKey.S2T, out var r1);
                    var u2 = GetOrAddUnionFor(d, UnionKey.HkVariantsOnly, out var r2);
                    return new DictRefs(r1, u1).WithRound2(r2, u2);
                }

                case OpenccConfig.Hk2S:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.HkRevPair, out var r1);
                    var u2 = GetOrAddUnionFor(d, punctuation ? UnionKey.T2SPunct : UnionKey.T2S, out var r2);
                    return new DictRefs(r1, u1).WithRound2(r2, u2);
                }

                case OpenccConfig.T2Tw:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.TwVariantsOnly, out var r1);
                    return new DictRefs(r1, u1);
                }

                case OpenccConfig.T2Twp:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.TwPhrasesOnly, out var r1);
                    var u2 = GetOrAddUnionFor(d, UnionKey.TwVariantsOnly, out var r2);
                    return new DictRefs(r1, u1).WithRound2(r2, u2);
                }

                case OpenccConfig.Tw2T:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.TwRevPair, out var r1);
                    return new DictRefs(r1, u1);
                }

                case OpenccConfig.Tw2Tp:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.TwRevPair, out var r1);
                    var u2 = GetOrAddUnionFor(d, UnionKey.TwPhrasesRevOnly, out var r2);
                    return new DictRefs(r1, u1).WithRound2(r2, u2);
                }

                case OpenccConfig.T2Hk:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.HkVariantsOnly, out var r1);
                    return new DictRefs(r1, u1);
                }

                case OpenccConfig.Hk2T:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.HkRevPair, out var r1);
                    return new DictRefs(r1, u1);
                }

                case OpenccConfig.T2Jp:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.JpVariantsOnly, out var r1);
                    return new DictRefs(r1, u1);
                }

                case OpenccConfig.Jp2T:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.JpRevTriple, out var r1);
                    return new DictRefs(r1, u1);
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(config), config, null);
            }
        }

        // ---- Secondary union cache helpers ------------------------------------------------------

        /// <summary>
        /// Retrieves a cached <see cref="StarterUnion"/> for the specified <see cref="UnionKey"/>,
        /// or builds and caches a new one if it does not yet exist.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Each <see cref="UnionKey"/> represents a logical dictionary group (conversion slot),
        /// such as <c>S2T</c>, <c>T2S</c>, <c>TwRevPair</c>, etc.  
        /// This method ensures that all conversion plans referencing the same slot
        /// reuse a single shared <see cref="StarterUnion"/> instance.
        /// </para>
        /// <para>
        /// The corresponding list of dictionaries is produced by
        /// <see cref="BuildDicts(DictionaryMaxlength, UnionKey)"/>, which determines
        /// the exact sequence of dictionaries used for that slot.  
        /// The resulting <paramref name="dicts"/> list is returned alongside the
        /// cached or newly built <see cref="StarterUnion"/>.
        /// </para>
        /// <para>
        /// This implementation avoids lambda captures of <c>out</c> parameters to remain
        /// fully compatible with .NET Standard 2.0, using a direct
        /// <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,TValue)"/> call instead
        /// of the value-factory overload.
        /// </para>
        /// </remarks>
        /// <param name="d">
        /// The <see cref="DictionaryMaxlength"/> instance containing all available
        /// OpenCC dictionaries for the current configuration.
        /// </param>
        /// <param name="key">
        /// The <see cref="UnionKey"/> identifying the dictionary group (conversion slot)
        /// whose <see cref="StarterUnion"/> should be retrieved or built.
        /// </param>
        /// <param name="dicts">
        /// When this method returns, contains the array of dictionaries corresponding
        /// to the specified <paramref name="key"/>.  
        /// The same array is used to build the <see cref="StarterUnion"/> if it was not already cached.
        /// </param>
        /// <returns>
        /// The existing or newly constructed <see cref="StarterUnion"/> instance associated
        /// with the specified <paramref name="key"/>.
        /// </returns>
        /// <threadsafety>
        /// Thread-safe. Concurrent calls for the same <see cref="UnionKey"/> may result in
        /// one redundant <see cref="StarterUnion.Build"/> invocation, but only the first
        /// successful result is stored in the cache.
        /// </threadsafety>
        private StarterUnion GetOrAddUnionFor(DictionaryMaxlength d, UnionKey key, out DictWithMaxLength[] dicts)
        {
            dicts = BuildDicts(d, key);

            if (_unionCacheByKey.TryGetValue(key, out var existing))
                return existing;

            var built = StarterUnion.Build(dicts);
            // Uses the TValue overload; avoids a valueFactory lambda entirely.
            return _unionCacheByKey.GetOrAdd(key, built);
        }

        /// <summary>
        /// Builds the array of dictionaries corresponding to the specified <see cref="UnionKey"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Each <see cref="UnionKey"/> represents a predefined logical group of dictionaries
        /// (a “conversion slot”) used when constructing a <see cref="StarterUnion"/>.
        /// </para>
        /// <para>
        /// This method maps the given <paramref name="key"/> to the concrete dictionary instances
        /// stored within the provided <see cref="DictionaryMaxlength"/> container.  
        /// For example, <see cref="UnionKey.S2T"/> selects <c>st_phrases</c> and <c>st_characters</c>,
        /// while <see cref="UnionKey.TwRevPair"/> selects  
        /// <c>tw_variants_rev_phrases</c> and <c>tw_variants_rev</c>.
        /// </para>
        /// <para>
        /// The resulting array defines the exact dictionary sequence for that conversion slot
        /// and is used to build or retrieve a cached <see cref="StarterUnion"/>.
        /// </para>
        /// </remarks>
        /// <param name="d">
        /// The <see cref="DictionaryMaxlength"/> instance containing all available
        /// OpenCC dictionaries for the current configuration.
        /// </param>
        /// <param name="key">
        /// The <see cref="UnionKey"/> specifying which dictionary group to construct.
        /// </param>
        /// <returns>
        /// A newly created <see cref="Array"/> of <see cref="DictWithMaxLength"/>
        /// objects representing the dictionaries for the specified slot.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown if the provided <paramref name="key"/> does not correspond to a known slot.
        /// </exception>
        private static DictWithMaxLength[] BuildDicts(DictionaryMaxlength d, UnionKey key)
        {
            switch (key)
            {
                // --- S2T / T2S ---
                case UnionKey.S2T:
                    return new[]
                    {
                        d.st_phrases,
                        d.st_characters
                    };

                case UnionKey.S2TPunct:
                    return new[]
                    {
                        d.st_phrases,
                        d.st_characters,
                        d.st_punctuations
                    };

                case UnionKey.T2S:
                    return new[]
                    {
                        d.ts_phrases,
                        d.ts_characters
                    };

                case UnionKey.T2SPunct:
                    return new[]
                    {
                        d.ts_phrases,
                        d.ts_characters,
                        d.ts_punctuations
                    };

                // --- TW ---
                case UnionKey.TwPhrasesOnly:
                    return new[] { d.tw_phrases };

                case UnionKey.TwVariantsOnly:
                    return new[] { d.tw_variants };

                case UnionKey.TwPhrasesRevOnly:
                    return new[] { d.tw_phrases_rev };

                case UnionKey.TwRevPair:
                    return new[]
                    {
                        d.tw_variants_rev_phrases,
                        d.tw_variants_rev
                    };

                case UnionKey.Tw2SpR1TwRevTriple:
                    return new[]
                    {
                        d.tw_phrases_rev,
                        d.tw_variants_rev_phrases,
                        d.tw_variants_rev
                    };

                // --- HK ---
                case UnionKey.HkVariantsOnly:
                    return new[] { d.hk_variants };

                case UnionKey.HkRevPair:
                    return new[]
                    {
                        d.hk_variants_rev_phrases,
                        d.hk_variants_rev
                    };

                // --- JP ---
                case UnionKey.JpVariantsOnly:
                    return new[] { d.jp_variants };

                case UnionKey.JpRevTriple:
                    return new[]
                    {
                        d.jps_phrases,
                        d.jps_characters,
                        d.jp_variants_rev
                    };

                default:
                    throw new ArgumentOutOfRangeException(nameof(key), key, null);
            }
        }

        // ---- Keys / IDs ---------------------------------------------------------------------------

        /// <summary>
        /// Immutable key type for identifying cached conversion plans
        /// in <see cref="ConversionPlanCache"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A <see cref="PlanKey"/> uniquely identifies a conversion plan by the  
        /// <see cref="OpenccConfig"/> value and whether punctuation handling  
        /// is enabled. This ensures that the plan cache can differentiate between  
        /// otherwise identical dictionary sequences that differ only in punctuation inclusion.
        /// </para>
        /// <para>
        /// The struct implements <see cref="IEquatable{PlanKey}"/> for fast equality checks  
        /// and overrides <see cref="GetHashCode"/> to produce a stable hash suitable for  
        /// use as a key in <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>.
        /// </para>
        /// <para>
        /// The hash code is computed by combining the integer representation of  
        /// <see cref="OpenccConfig"/> with the punctuation flag using a prime  
        /// multiplier (397) to minimize collisions.
        /// </para>
        /// </remarks>
        /// <example>
        /// Example usage in the primary plan cache:
        /// <code>
        /// var plan = _planCache.GetOrAdd(
        ///     new PlanKey(OpenccConfig.S2T, true),
        ///     _ => BuildPlan(OpenccConfig.S2T, true)
        /// );
        /// </code>
        /// </example>
        private readonly struct PlanKey : IEquatable<PlanKey>
        {
            private readonly OpenccConfig _config;
            private readonly bool _punctuation;

            public PlanKey(OpenccConfig config, bool punctuation)
            {
                _config = config;
                _punctuation = punctuation;
            }

            public bool Equals(PlanKey other) => _config == other._config && _punctuation == other._punctuation;
            public override bool Equals(object obj) => obj is PlanKey pk && Equals(pk);
            public override int GetHashCode() => ((int)_config * 397) ^ (_punctuation ? 1 : 0);
            public override string ToString() => _config + (_punctuation ? "_punct" : "");
        }

        // ---- Notes --------------------------------------------------------------------------------
        // - This file assumes existing types in your project:
        //   - OpenccConfig (enum), DictRefs (rounds with optional StarterUnion args), DictWithMaxLength,
        //     StarterUnion (with static Build(IReadOnlyList<DictWithMaxLength>)), and DictionaryMaxlength.
        // - Inject your lazy dictionary via the constructor: () => _lazyDictionary.Value
        // - Thread-safe: both caches use ConcurrentDictionary, and StarterUnion is immutable after Build().
        // - To hot-reload dictionaries: rebuild DictionaryMaxlength, then call cache.Clear().
        //
        // - Secondary cache now keyed by UnionKey instead of RoundKey:
        //     Each UnionKey represents a predefined logical slot (e.g., S2T, TwRevPair, HkRevPair),
        //     allowing all conversion plans to share the same StarterUnion instance per slot.
        // - BuildDicts(DictionaryMaxlength, UnionKey) defines which dictionaries belong to each slot.
        // - GetOrAddUnionFor() ensures that identical slots reuse an existing StarterUnion
        //   and remains .NET Standard 2.0-compatible (no lambda captures of out parameters).
        // - Primary cache (_planCache) maps (OpenccConfig, punctuation) → DictRefs, ensuring
        //   complete plan reuse across conversions with identical configuration.
    }
}