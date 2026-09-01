using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace OpenccNetLib
{
    // ---- Internal cache facade ------------------------------------------------------------------

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
    internal sealed class ConversionPlanCache
    {
        /// <summary>
        /// The process-wide cache used by the <see cref="Opencc"/> conversion facade.
        /// </summary>
        /// <remarks>
        /// Provider changes publish a completely new cache instance so a plan and the
        /// dictionary provider from which it is built always belong to the same snapshot.
        /// Existing conversions that retain the previous cache instance may safely
        /// finish using that instance.
        /// </remarks>
        private static ConversionPlanCache _current = new(() => DictionaryLib.Provider);

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
            /// Taiwan forward variant pair: phrase variants + character variants.
            /// </summary>
            TwVariantsPair,

            /// <summary>
            /// Taiwan reverse pair: variant reverse phrases + variant reverse characters.
            /// </summary>
            TwRevPair,

            /// <summary>
            /// Taiwan phrase and variant dictionaries:
            /// phrases + variant phrases + character variants.
            /// </summary>
            TwTriple,

            /// <summary>
            /// Reverse Taiwan phrase and variant dictionaries:
            /// reverse phrases + reverse variant phrases + reverse character variants.
            /// </summary>
            TwRevTriple,

            // --- Hong Kong-specific ---

            /// <summary>
            /// Hong Kong forward variant pair: phrase variants + character variants.
            /// </summary>
            HkVariantsPair,

            /// <summary>
            /// Hong Kong reverse pair: variant reverse phrases + variant reverse characters.
            /// </summary>
            HkRevPair,

            /// <summary>
            /// Hong Kong phrase and variant dictionaries:
            /// phrases + variants_phrases + variants.
            /// </summary>
            HkTriple,

            /// <summary>
            /// Reverse Hong Kong phrase and variant dictionaries:
            /// phrases_rev + variants_rev_phrases + variants_rev.
            /// </summary>
            HkRevTriple,

            // --- Japan-specific ---

            /// <summary>
            /// Traditional Kyujitai-to-Japanese Shinjitai characters only.
            /// </summary>
            JpsCharactersRev,

            /// <summary>
            /// Japanese Shinjitai pair:
            /// JPS phrases + JPS characters.
            /// </summary>
            JpsPair,

            /// <summary>
            /// Simplified-style punctuation → Traditional-style punctuation only,
            /// used as an optional punctuation round for direct Traditional-region conversions.
            /// </summary>
            StPunctOnly
        }

        /// <summary>
        /// Provides access to the <see cref="DictionaryMaxlength"/> instance used by this cache
        /// when building new conversion plans and <see cref="StarterUnion"/> caches.
        /// </summary>
        /// <remarks>
        /// The provider is fixed for the lifetime of this cache instance. Dictionary source
        /// changes are applied by publishing a fresh global cache.
        /// </remarks>
        private readonly Func<DictionaryMaxlength> _dictionaryProvider;

        // Primary cache: (config, punct) -> DictRefs (rounds include unions)
        private readonly ConcurrentDictionary<PlanKey, DictRefs> _planCache = new();

        // Secondary cache: round layout (list of dict IDs) -> StarterUnion
        private readonly ConcurrentDictionary<UnionKey, StarterUnion> _unionCacheByKey = new();

        // Cache the dictionary arrays for each union slot within this cache instance.
        private readonly ConcurrentDictionary<UnionKey, DictWithMaxLength[]> _dictArrayCacheByKey = new();

        /// <summary>
        /// Gets the process-wide cache currently used by the conversion facade.
        /// </summary>
        internal static ConversionPlanCache Current => Volatile.Read(ref _current);

        /// <summary>
        /// Returns the dictionary supplied by the provider bound to the active cache snapshot.
        /// </summary>
        internal static DictionaryMaxlength Provider
        {
            get
            {
                var current = Current;
                return current._dictionaryProvider();
            }
        }

        /// <summary>
        /// Compatibility helper for existing in-assembly call sites.
        /// </summary>
        internal static DictionaryMaxlength GetCurrentDictionary() => Provider;

        /// <summary>
        /// Publishes a fresh process-wide cache bound to a fixed dictionary instance.
        /// </summary>
        internal static void UseProvider(DictionaryMaxlength dictionary)
        {
            if (dictionary == null)
                throw new ArgumentNullException(nameof(dictionary));

            PublishProvider(() => dictionary);
        }

        /// <summary>
        /// Restores the process-wide cache to the built-in default dictionary provider.
        /// </summary>
        internal static void ResetProvider()
            => PublishProvider(() => DictionaryLib.Provider);

        /// <summary>
        /// Atomically publishes a fresh process-wide cache bound to
        /// <paramref name="dictionaryProvider"/>.
        /// </summary>
        /// <remarks>
        /// Replacing the complete cache discards plans and starter-union state derived from
        /// the previous provider. Conversions already using the previous snapshot may finish
        /// normally; subsequent lookups observe the replacement snapshot.
        /// </remarks>
        /// <param name="dictionaryProvider">
        /// The provider from which the replacement cache builds conversion plans.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="dictionaryProvider"/> is <see langword="null"/>.
        /// </exception>
        internal static void PublishProvider(Func<DictionaryMaxlength> dictionaryProvider)
        {
            if (dictionaryProvider == null)
                throw new ArgumentNullException(nameof(dictionaryProvider));

            var replacement = new ConversionPlanCache(dictionaryProvider);
            Interlocked.Exchange(ref _current, replacement);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConversionPlanCache"/> class.
        /// </summary>
        /// <param name="dictionaryProvider">
        /// A delegate that returns the <see cref="DictionaryMaxlength"/> instance to use when
        /// constructing new conversion plans.
        /// <para>
        /// A cache instance remains bound to this provider for its entire lifetime. The
        /// library's internal global lifecycle publishes a fresh instance when the active
        /// dictionary changes. Existing cache instances continue using their original provider.
        /// </para>
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="dictionaryProvider"/> is <see langword="null"/>.
        /// </exception>
        internal ConversionPlanCache(Func<DictionaryMaxlength> dictionaryProvider)
        {
            _dictionaryProvider = dictionaryProvider ?? throw new ArgumentNullException(nameof(dictionaryProvider));
        }

        /// <summary>
        /// Initializes an independent conversion-plan cache from a base dictionary and
        /// a retained set of per-instance custom dictionary specifications.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This constructor does not publish or replace <see cref="Current"/>. It snapshots
        /// <paramref name="baseDictionary"/> before applying non-empty custom specifications,
        /// so the built-in <see cref="DictionaryLib.Provider"/> and other cache instances are
        /// never mutated.
        /// </para>
        /// <para>
        /// Specifications are applied once during construction. The resulting dictionary
        /// provider and all plans and starter unions belong exclusively to this cache.
        /// An empty array creates an independent plan cache over the supplied base dictionary.
        /// </para>
        /// </remarks>
        /// <param name="baseDictionary">
        /// The dictionary whose mappings form the base of the isolated cache.
        /// </param>
        /// <param name="customDictSpecs">
        /// The materialized custom dictionary specifications to apply in array order.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="baseDictionary"/> or <paramref name="customDictSpecs"/> is
        /// <see langword="null"/>.
        /// </exception>
        internal ConversionPlanCache(
            DictionaryMaxlength baseDictionary,
            CustomDictSpec[] customDictSpecs)
            : this(CreateCustomDictionaryProvider(baseDictionary, customDictSpecs))
        {
        }

        /// <summary>
        /// Creates the fixed dictionary provider for an instance-owned cache.
        /// </summary>
        /// <remarks>
        /// Non-empty specifications are applied to a deep copy so no mutable dictionary
        /// table or derived lookup metadata is shared with the global default dictionary.
        /// Empty specifications preserve default conversion behavior without copying the
        /// dictionary data; the plan and union caches remain instance-owned.
        /// </remarks>
        private static Func<DictionaryMaxlength> CreateCustomDictionaryProvider(
            DictionaryMaxlength baseDictionary,
            CustomDictSpec[] customDictSpecs)
        {
            if (baseDictionary == null)
                throw new ArgumentNullException(nameof(baseDictionary));

            if (customDictSpecs == null)
                throw new ArgumentNullException(nameof(customDictSpecs));

            var dictionary = customDictSpecs.Length == 0
                ? baseDictionary
                : DictionaryLib.WithCustomDicts(CloneDictionary(baseDictionary), customDictSpecs);

            return () => dictionary;
        }

        /// <summary>
        /// Creates a fully independent copy of every mutable dictionary slot and its
        /// derived lookup metadata.
        /// </summary>
        private static DictionaryMaxlength CloneDictionary(DictionaryMaxlength source)
        {
            return new DictionaryMaxlength
            {
                st_characters = CloneSlot(source.st_characters),
                st_phrases = CloneSlot(source.st_phrases),
                ts_characters = CloneSlot(source.ts_characters),
                ts_phrases = CloneSlot(source.ts_phrases),
                tw_phrases = CloneSlot(source.tw_phrases),
                tw_phrases_rev = CloneSlot(source.tw_phrases_rev),
                tw_variants = CloneSlot(source.tw_variants),
                tw_variants_phrases = CloneSlot(source.tw_variants_phrases),
                tw_variants_rev = CloneSlot(source.tw_variants_rev),
                tw_variants_rev_phrases = CloneSlot(source.tw_variants_rev_phrases),
                hk_phrases = CloneSlot(source.hk_phrases),
                hk_phrases_rev = CloneSlot(source.hk_phrases_rev),
                hk_variants = CloneSlot(source.hk_variants),
                hk_variants_phrases = CloneSlot(source.hk_variants_phrases),
                hk_variants_rev = CloneSlot(source.hk_variants_rev),
                hk_variants_rev_phrases = CloneSlot(source.hk_variants_rev_phrases),
                jps_characters = CloneSlot(source.jps_characters),
                jps_characters_rev = CloneSlot(source.jps_characters_rev),
                jps_phrases = CloneSlot(source.jps_phrases),
                st_punctuations = CloneSlot(source.st_punctuations),
                ts_punctuations = CloneSlot(source.ts_punctuations)
            };
        }

        /// <summary>
        /// Copies one mutable dictionary slot and all metadata consumed by plan construction.
        /// </summary>
        private static DictWithMaxLength CloneSlot(DictWithMaxLength source)
        {
            if (source == null)
                return new DictWithMaxLength();

            return new DictWithMaxLength
            {
                Dict = source.Dict == null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(source.Dict, StringComparer.Ordinal),
                MaxLength = source.MaxLength,
                MinLength = source.MinLength,
                LengthMask = source.LengthMask,
                LongLengths = source.LongLengths == null
                    ? null
                    : new HashSet<int>(source.LongLengths),
                StarterLenMask = source.StarterLenMask == null
                    ? null
                    : new Dictionary<string, ulong>(source.StarterLenMask, StringComparer.Ordinal)
            };
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
        internal DictRefs GetPlan(OpenccConfig config, bool punctuation = false)
            => _planCache.GetOrAdd(new PlanKey(config, punctuation), _ => BuildPlan(config, punctuation));

        /// <summary>Clear all plan and union caches (e.g., after hot-reloading dictionaries).</summary>
        internal void Clear()
        {
            _planCache.Clear();
            _unionCacheByKey.Clear();
            _dictArrayCacheByKey.Clear();
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
        /// Complex conversions such as <c>S2Twp</c> and <c>Tw2Sp</c> use
        /// two sequential rounds. Each round is represented by its corresponding
        /// <see cref="UnionKey"/> entry. For <c>S2Twp</c>, round 1 converts
        /// Simplified Chinese to Traditional Chinese, and round 2 performs
        /// Taiwan phrase and variant normalization.
        /// Direct <c>T2Twp</c> and <c>Tw2Tp</c> conversions use the Taiwan
        /// triple dictionary group as their primary round. Likewise, direct
        /// <c>T2Hkp</c> and <c>Hk2Tp</c> conversions use the Hong Kong triple
        /// dictionary group as their primary round. When punctuation conversion
        /// is enabled, these direct Traditional-region plans append the
        /// punctuation-only <see cref="UnionKey.StPunctOnly"/> round.
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
                    var u2 = GetOrAddUnionFor(d, UnionKey.TwVariantsPair, out var r2);
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
                    var u2 = GetOrAddUnionFor(d, UnionKey.TwTriple, out var r2);
                    return new DictRefs(r1, u1).WithRound2(r2, u2);
                }

                case OpenccConfig.S2Hkp:
                {
                    var u1 = GetOrAddUnionFor(d, punctuation ? UnionKey.S2TPunct : UnionKey.S2T, out var r1);
                    var u2 = GetOrAddUnionFor(d, UnionKey.HkTriple, out var r2);
                    return new DictRefs(r1, u1).WithRound2(r2, u2);
                }

                case OpenccConfig.Tw2Sp:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.TwRevTriple, out var r1);
                    var u2 = GetOrAddUnionFor(d, punctuation ? UnionKey.T2SPunct : UnionKey.T2S, out var r2);
                    return new DictRefs(r1, u1).WithRound2(r2, u2);
                }

                case OpenccConfig.Hk2Sp:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.HkRevTriple, out var r1);
                    var u2 = GetOrAddUnionFor(d, punctuation ? UnionKey.T2SPunct : UnionKey.T2S, out var r2);
                    return new DictRefs(r1, u1).WithRound2(r2, u2);
                }

                case OpenccConfig.S2Hk:
                {
                    var u1 = GetOrAddUnionFor(d, punctuation ? UnionKey.S2TPunct : UnionKey.S2T, out var r1);
                    var u2 = GetOrAddUnionFor(d, UnionKey.HkVariantsPair, out var r2);
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
                    var u1 = GetOrAddUnionFor(d, UnionKey.TwVariantsPair, out var r1);
                    var refs = new DictRefs(r1, u1);

                    if (!punctuation)
                        return refs;

                    var u2 = GetOrAddUnionFor(
                        d, UnionKey.StPunctOnly, out var r2);

                    return refs.WithRound2(r2, u2);
                }

                case OpenccConfig.T2Twp:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.TwTriple, out var r1);
                    var refs = new DictRefs(r1, u1);

                    if (!punctuation)
                        return refs;

                    var u2 = GetOrAddUnionFor(
                        d, UnionKey.StPunctOnly, out var r2);

                    return refs.WithRound2(r2, u2);
                }

                case OpenccConfig.Tw2T:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.TwRevPair, out var r1);
                    var refs = new DictRefs(r1, u1);

                    if (!punctuation)
                        return refs;

                    var u2 = GetOrAddUnionFor(
                        d, UnionKey.StPunctOnly, out var r2);

                    return refs.WithRound2(r2, u2);
                }

                case OpenccConfig.Tw2Tp:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.TwRevTriple, out var r1);
                    var refs = new DictRefs(r1, u1);

                    if (!punctuation)
                        return refs;

                    var u2 = GetOrAddUnionFor(
                        d, UnionKey.StPunctOnly, out var r2);

                    return refs.WithRound2(r2, u2);
                }

                case OpenccConfig.T2Hk:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.HkVariantsPair, out var r1);
                    var refs = new DictRefs(r1, u1);

                    if (!punctuation)
                        return refs;

                    var u2 = GetOrAddUnionFor(
                        d, UnionKey.StPunctOnly, out var r2);

                    return refs.WithRound2(r2, u2);
                }

                case OpenccConfig.Hk2T:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.HkRevPair, out var r1);
                    var refs = new DictRefs(r1, u1);

                    if (!punctuation)
                        return refs;

                    var u2 = GetOrAddUnionFor(
                        d, UnionKey.StPunctOnly, out var r2);

                    return refs.WithRound2(r2, u2);
                }
                case OpenccConfig.T2Hkp:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.HkTriple, out var r1);
                    var refs = new DictRefs(r1, u1);

                    if (!punctuation)
                        return refs;

                    var u2 = GetOrAddUnionFor(
                        d, UnionKey.StPunctOnly, out var r2);

                    return refs.WithRound2(r2, u2);
                }

                case OpenccConfig.Hk2Tp:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.HkRevTriple, out var r1);
                    var refs = new DictRefs(r1, u1);

                    if (!punctuation)
                        return refs;

                    var u2 = GetOrAddUnionFor(
                        d, UnionKey.StPunctOnly, out var r2);

                    return refs.WithRound2(r2, u2);
                }

                case OpenccConfig.T2Jp:
                {
                    var u1 = GetOrAddUnionFor(
                        d, UnionKey.JpsCharactersRev, out var r1);
                    var refs = new DictRefs(r1, u1);

                    if (!punctuation)
                        return refs;

                    var u2 = GetOrAddUnionFor(
                        d, UnionKey.StPunctOnly, out var r2);

                    return refs.WithRound2(r2, u2);
                }

                case OpenccConfig.Jp2T:
                {
                    var u1 = GetOrAddUnionFor(
                        d, UnionKey.JpsPair, out var r1);
                    var refs = new DictRefs(r1, u1);

                    if (!punctuation)
                        return refs;

                    var u2 = GetOrAddUnionFor(
                        d, UnionKey.StPunctOnly, out var r2);

                    return refs.WithRound2(r2, u2);
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
            dicts = _dictArrayCacheByKey.GetOrAdd(key, _ => BuildDicts(d, key));

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
                case UnionKey.TwVariantsPair:
                    return new[]
                    {
                        d.tw_variants_phrases,
                        d.tw_variants
                    };

                case UnionKey.TwRevPair:
                    return new[]
                    {
                        d.tw_variants_rev_phrases,
                        d.tw_variants_rev
                    };

                case UnionKey.TwRevTriple:
                    return new[]
                    {
                        d.tw_phrases_rev,
                        d.tw_variants_rev_phrases,
                        d.tw_variants_rev
                    };

                case UnionKey.TwTriple:
                    return new[]
                    {
                        d.tw_phrases,
                        d.tw_variants_phrases,
                        d.tw_variants
                    };

                // --- HK ---
                case UnionKey.HkVariantsPair:
                    return new[]
                    {
                        d.hk_variants_phrases,
                        d.hk_variants
                    };

                case UnionKey.HkRevPair:
                    return new[]
                    {
                        d.hk_variants_rev_phrases,
                        d.hk_variants_rev
                    };

                case UnionKey.HkTriple:
                    return new[]
                    {
                        d.hk_phrases,
                        d.hk_variants_phrases,
                        d.hk_variants
                    };

                case UnionKey.HkRevTriple:
                    return new[]
                    {
                        d.hk_phrases_rev,
                        d.hk_variants_rev_phrases,
                        d.hk_variants_rev
                    };

                // --- JP ---
                case UnionKey.JpsCharactersRev:
                    return new[] { d.jps_characters_rev };

                case UnionKey.JpsPair:
                    return new[]
                    {
                        d.jps_phrases,
                        d.jps_characters
                    };

                // --- T -> T Region Punctuation
                case UnionKey.StPunctOnly:
                    return new[] { d.st_punctuations };

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
        // - Thread-safe: both caches use ConcurrentDictionary, and StarterUnion is immutable after Build().
        // - Secondary cache keyed by UnionKey instead of RoundKey.
    }
}