using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

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
    /// <see cref="Opencc.OpenccConfig"/> and punctuation setting) to a  
    /// <see cref="DictRefs"/> instance, which contains the dictionary sequence  
    /// ("rounds") and any per-round <see cref="StarterUnion"/> for fast lookups.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>Secondary cache:</b> Maps a <see cref="RoundKey"/> (list of  
    /// <see cref="BaseDictId"/> values) to a shared <see cref="StarterUnion"/>.  
    /// This allows multiple plans with identical round layouts to reuse  
    /// the same union, saving memory and build time.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    public sealed class ConversionPlanCache
    {
        private readonly Func<DictionaryMaxlength> _dictionaryProvider;

        // Primary cache: (config, punct) -> DictRefs (rounds include unions)
        private readonly ConcurrentDictionary<PlanKey, DictRefs> _planCache =
            new ConcurrentDictionary<PlanKey, DictRefs>();

        // Secondary cache: round layout (list of dict IDs) -> StarterUnion
        private readonly ConcurrentDictionary<RoundKey, StarterUnion> _unionCache =
            new ConcurrentDictionary<RoundKey, StarterUnion>();

        /// <summary>
        /// Initializes a new instance of <see cref="ConversionPlanCache"/>.
        /// </summary>
        /// <param name="dictionaryProvider">
        /// A function that returns the current <see cref="DictionaryMaxlength"/>  
        /// instance to be used when building new plans.  
        /// Typically wraps the main <c>Dictionary</c> from the <c>Opencc</c> instance.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="dictionaryProvider"/> is null.
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
        public DictRefs GetPlan(Opencc.OpenccConfig config, bool punctuation = false)
            => _planCache.GetOrAdd(new PlanKey(config, punctuation), _ => BuildPlan(config, punctuation));

        /// <summary>Clear all plan and union caches (e.g., after hot-reloading dictionaries).</summary>
        public void Clear()
        {
            _planCache.Clear();
            _unionCache.Clear();
        }

        // ---- Plan building ----------------------------------------------------------------------

        /// <summary>
        /// Constructs a fully-resolved <see cref="DictRefs"/> conversion plan for the given  
        /// <paramref name="config"/> and punctuation setting.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method is the core factory for creating conversion plans. It determines the  
        /// dictionary sequence ("rounds") based on the <see cref="Opencc.OpenccConfig"/> value,  
        /// optionally including punctuation dictionaries, and attaches a per-round  
        /// <see cref="StarterUnion"/> built (or retrieved from cache) for that round's dictionary set.
        /// </para>
        /// <para>
        /// Some configurations (e.g., <c>S2Tw</c>, <c>Tw2S</c>, <c>HK2S</c>) require two sequential  
        /// rounds of dictionary application, while others (e.g., <c>S2T</c>, <c>T2S</c>) use only one.
        /// Each round's <see cref="StarterUnion"/> is fetched via <see cref="GetOrAddUnionFor"/> so  
        /// that identical dictionary layouts reuse the same union instance.
        /// </para>
        /// </remarks>
        /// <param name="config">The OpenCC conversion configuration to build a plan for.</param>
        /// <param name="punctuation">
        /// Whether to include punctuation dictionaries in the plan.
        /// </param>
        /// <returns>
        /// A <see cref="DictRefs"/> object containing the dictionary layout and starter-union  
        /// acceleration data for each round.
        /// </returns>
        private DictRefs BuildPlan(Opencc.OpenccConfig config, bool punctuation)
        {
            var d = _dictionaryProvider();

            List<DictWithMaxLength> r1, r2;
            DictRefs plan;

            switch (config)
            {
                case Opencc.OpenccConfig.S2T:
                    r1 = new List<DictWithMaxLength> { d.st_phrases, d.st_characters };
                    if (punctuation) r1.Add(d.st_punctuations);
                    plan = new DictRefs(r1, GetOrAddUnionFor(r1));
                    break;

                case Opencc.OpenccConfig.T2S:
                    r1 = new List<DictWithMaxLength> { d.ts_phrases, d.ts_characters };
                    if (punctuation) r1.Add(d.ts_punctuations);
                    plan = new DictRefs(r1, GetOrAddUnionFor(r1));
                    break;

                case Opencc.OpenccConfig.S2Tw:
                    r1 = new List<DictWithMaxLength> { d.st_phrases, d.st_characters };
                    if (punctuation) r1.Add(d.st_punctuations);
                    r2 = new List<DictWithMaxLength> { d.tw_variants };
                    plan = new DictRefs(r1, GetOrAddUnionFor(r1))
                        .WithRound2(r2, GetOrAddUnionFor(r2));
                    break;

                case Opencc.OpenccConfig.Tw2S:
                    r1 = new List<DictWithMaxLength> { d.tw_variants_rev_phrases, d.tw_variants_rev };
                    r2 = new List<DictWithMaxLength> { d.ts_phrases, d.ts_characters };
                    if (punctuation) r2.Add(d.ts_punctuations);
                    plan = new DictRefs(r1, GetOrAddUnionFor(r1))
                        .WithRound2(r2, GetOrAddUnionFor(r2));
                    break;

                case Opencc.OpenccConfig.S2Twp:
                    r1 = new List<DictWithMaxLength> { d.st_phrases, d.st_characters };
                    if (punctuation) r1.Add(d.st_punctuations);
                    r2 = new List<DictWithMaxLength> { d.tw_phrases };
                    var r3 = new List<DictWithMaxLength> { d.tw_variants };
                    plan = new DictRefs(r1, GetOrAddUnionFor(r1))
                        .WithRound2(r2, GetOrAddUnionFor(r2))
                        .WithRound3(r3, GetOrAddUnionFor(r3));
                    break;

                case Opencc.OpenccConfig.Tw2Sp:
                    r1 = new List<DictWithMaxLength> { d.tw_phrases_rev, d.tw_variants_rev_phrases, d.tw_variants_rev };
                    r2 = new List<DictWithMaxLength> { d.ts_phrases, d.ts_characters };
                    if (punctuation) r2.Add(d.ts_punctuations);
                    plan = new DictRefs(r1, GetOrAddUnionFor(r1))
                        .WithRound2(r2, GetOrAddUnionFor(r2));
                    break;

                case Opencc.OpenccConfig.S2Hk:
                    r1 = new List<DictWithMaxLength> { d.st_phrases, d.st_characters };
                    if (punctuation) r1.Add(d.st_punctuations);
                    r2 = new List<DictWithMaxLength> { d.hk_variants };
                    plan = new DictRefs(r1, GetOrAddUnionFor(r1))
                        .WithRound2(r2, GetOrAddUnionFor(r2));
                    break;

                case Opencc.OpenccConfig.Hk2S:
                    r1 = new List<DictWithMaxLength> { d.hk_variants_rev_phrases, d.hk_variants_rev };
                    r2 = new List<DictWithMaxLength> { d.ts_phrases, d.ts_characters };
                    if (punctuation) r2.Add(d.ts_punctuations);
                    plan = new DictRefs(r1, GetOrAddUnionFor(r1))
                        .WithRound2(r2, GetOrAddUnionFor(r2));
                    break;

                case Opencc.OpenccConfig.T2Tw:
                    r1 = new List<DictWithMaxLength> { d.tw_variants };
                    plan = new DictRefs(r1, GetOrAddUnionFor(r1));
                    break;

                case Opencc.OpenccConfig.T2Twp:
                    r1 = new List<DictWithMaxLength> { d.tw_phrases };
                    r2 = new List<DictWithMaxLength> { d.tw_variants };
                    plan = new DictRefs(r1, GetOrAddUnionFor(r1))
                        .WithRound2(r2, GetOrAddUnionFor(r2));
                    break;

                case Opencc.OpenccConfig.Tw2T:
                    r1 = new List<DictWithMaxLength> { d.tw_variants_rev_phrases, d.tw_variants_rev };
                    plan = new DictRefs(r1, GetOrAddUnionFor(r1));
                    break;

                case Opencc.OpenccConfig.Tw2Tp:
                    r1 = new List<DictWithMaxLength> { d.tw_variants_rev_phrases, d.tw_variants_rev };
                    r2 = new List<DictWithMaxLength> { d.tw_phrases_rev };
                    plan = new DictRefs(r1, GetOrAddUnionFor(r1))
                        .WithRound2(r2, GetOrAddUnionFor(r2));
                    break;

                case Opencc.OpenccConfig.T2Hk:
                    r1 = new List<DictWithMaxLength> { d.hk_variants };
                    plan = new DictRefs(r1, GetOrAddUnionFor(r1));
                    break;

                case Opencc.OpenccConfig.Hk2T:
                    r1 = new List<DictWithMaxLength> { d.hk_variants_rev_phrases, d.hk_variants_rev };
                    plan = new DictRefs(r1, GetOrAddUnionFor(r1));
                    break;

                case Opencc.OpenccConfig.T2Jp:
                    r1 = new List<DictWithMaxLength> { d.jp_variants };
                    plan = new DictRefs(r1, GetOrAddUnionFor(r1));
                    break;

                case Opencc.OpenccConfig.Jp2T:
                    r1 = new List<DictWithMaxLength> { d.jps_phrases, d.jps_characters, d.jp_variants_rev };
                    plan = new DictRefs(r1, GetOrAddUnionFor(r1));
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(config), config, null);
            }

            return plan;
        }

        // ---- Secondary union cache helpers ------------------------------------------------------

        /// <summary>
        /// Retrieves a cached <see cref="StarterUnion"/> for the given dictionary set,
        /// or builds and caches a new one if no identical round layout exists.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A "round layout" is defined by the sequence of base dictionary IDs derived from  
        /// the provided <paramref name="dicts"/>. This method maps each dictionary to its  
        /// corresponding <see cref="BaseDictId"/> via <see cref="MapDictToId"/>, creating a  
        /// <see cref="RoundKey"/> that serves as the cache key.
        /// </para>
        /// <para>
        /// If a matching <see cref="StarterUnion"/> is already in the union cache, it is reused.  
        /// Otherwise, <see cref="StarterUnion.Build"/> is called to create a new union, which is  
        /// then stored in the cache for future use.
        /// </para>
        /// <para>
        /// This ensures that conversion rounds sharing the exact same dictionary sequence also  
        /// share the same <see cref="StarterUnion"/> instance, reducing memory usage and build time.
        /// </para>
        /// </remarks>
        /// <param name="dicts">
        /// The list of <see cref="DictWithMaxLength"/> objects representing the dictionaries  
        /// for this round, in the exact sequence they will be applied.
        /// </param>
        /// <returns>
        /// A cached or newly built <see cref="StarterUnion"/> for the specified dictionary set.
        /// </returns>
        private StarterUnion GetOrAddUnionFor(List<DictWithMaxLength> dicts)
        {
            var ids = new ushort[dicts.Count];
            var d = _dictionaryProvider();

            for (int i = 0; i < dicts.Count; i++)
                ids[i] = (ushort)MapDictToId(d, dicts[i]);

            var key = new RoundKey(ids);
            return _unionCache.GetOrAdd(key, _ => StarterUnion.Build(dicts));
        }

        /// <summary>
        /// Maps a specific <see cref="DictWithMaxLength"/> instance to its corresponding
        /// <see cref="BaseDictId"/> enumeration value.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method uses <see cref="object.ReferenceEquals"/> to determine which of the predefined  
        /// dictionaries in <paramref name="d"/> matches the provided <paramref name="dict"/>.  
        /// This ensures mapping is based on actual object identity, not on content equality.
        /// </para>
        /// <para>
        /// This mapping is essential for generating stable, repeatable IDs in  
        /// <see cref="ConversionPlanCache"/> when building <see cref="RoundKey"/> values for  
        /// union caching. By consistently resolving dictionaries to fixed IDs, it ensures  
        /// that identical round layouts share the same <see cref="StarterUnion"/> instance.
        /// </para>
        /// </remarks>
        /// <param name="d">
        /// The <see cref="DictionaryMaxlength"/> containing all base dictionaries for the OpenCC configuration.
        /// </param>
        /// <param name="dict">
        /// The dictionary instance to map to a <see cref="BaseDictId"/>.
        /// </param>
        /// <returns>
        /// The <see cref="BaseDictId"/> corresponding to the provided dictionary instance.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown if the provided dictionary instance does not match any known  
        /// dictionary in <paramref name="d"/>.
        /// </exception>
        private static BaseDictId MapDictToId(DictionaryMaxlength d, DictWithMaxLength dict)
        {
            // Keep mappings tight & explicit for ReferenceEquals-based identity
            if (ReferenceEquals(dict, d.st_phrases)) return BaseDictId.ST_Phrases;
            if (ReferenceEquals(dict, d.st_characters)) return BaseDictId.ST_Characters;
            if (ReferenceEquals(dict, d.st_punctuations)) return BaseDictId.ST_Punctuations;

            if (ReferenceEquals(dict, d.ts_phrases)) return BaseDictId.TS_Phrases;
            if (ReferenceEquals(dict, d.ts_characters)) return BaseDictId.TS_Characters;
            if (ReferenceEquals(dict, d.ts_punctuations)) return BaseDictId.TS_Punctuations;

            if (ReferenceEquals(dict, d.tw_variants)) return BaseDictId.TW_Variants;
            if (ReferenceEquals(dict, d.tw_phrases)) return BaseDictId.TW_Phrases;
            if (ReferenceEquals(dict, d.tw_phrases_rev)) return BaseDictId.TW_Phrases_Rev;
            if (ReferenceEquals(dict, d.tw_variants_rev)) return BaseDictId.TW_Variants_Rev;
            if (ReferenceEquals(dict, d.tw_variants_rev_phrases)) return BaseDictId.TW_Variants_Rev_Phrases;

            if (ReferenceEquals(dict, d.hk_variants)) return BaseDictId.HK_Variants;
            if (ReferenceEquals(dict, d.hk_variants_rev)) return BaseDictId.HK_Variants_Rev;
            if (ReferenceEquals(dict, d.hk_variants_rev_phrases)) return BaseDictId.HK_Variants_Rev_Phrases;

            if (ReferenceEquals(dict, d.jp_variants)) return BaseDictId.JP_Variants;
            if (ReferenceEquals(dict, d.jps_phrases)) return BaseDictId.JPS_Phrases;
            if (ReferenceEquals(dict, d.jps_characters)) return BaseDictId.JPS_Characters;
            if (ReferenceEquals(dict, d.jp_variants_rev)) return BaseDictId.JP_Variants_Rev;

            throw new InvalidOperationException("Unknown dictionary instance (not mapped).");
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
    /// <see cref="Opencc.OpenccConfig"/> value and whether punctuation handling  
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
    /// <see cref="Opencc.OpenccConfig"/> with the punctuation flag using a prime  
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
    internal readonly struct PlanKey : IEquatable<PlanKey>
    {
        private readonly Opencc.OpenccConfig _config;
        private readonly bool _punctuation;

        public PlanKey(Opencc.OpenccConfig config, bool punctuation)
        {
            _config = config;
            _punctuation = punctuation;
        }

        public bool Equals(PlanKey other) => _config == other._config && _punctuation == other._punctuation;
        public override bool Equals(object obj) => obj is PlanKey pk && Equals(pk);
        public override int GetHashCode() => ((int)_config * 397) ^ (_punctuation ? 1 : 0);
        public override string ToString() => _config + (_punctuation ? "_punct" : "");
    }

    /// <summary>
    /// Enumerates the canonical IDs for all built-in OpenCC dictionaries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each value corresponds to a specific dictionary file or logical group
    /// in <see cref="DictionaryMaxlength"/> and is used internally to build
    /// <see cref="StarterUnion"/> instances and cache <see cref="DictRefs"/> plans.
    /// </para>
    /// <para>
    /// The identifiers intentionally use snake_case segments (with underscores)
    /// to remain consistent with the original OpenCC data source naming convention
    /// and to make cross-language mapping (Rust, Python, Java) easier.
    /// </para>
    /// <para>
    /// When mapping dictionaries to IDs, reference comparison is used to ensure
    /// the exact dictionary instance is matched, avoiding accidental lookups by
    /// value or content.
    /// </para>
    /// </remarks>
    /// <example>
    /// Example usage:
    /// <code>
    /// var dictId = MapDictToId(dictionaryMaxlength, dictionaryMaxlength.st_phrases);
    /// if (dictId == BaseDictId.ST_Phrases)
    /// {
    ///     // Do something specific for ST_Phrases
    /// }
    /// </code>
    /// </example>
    // ReSharper disable InconsistentNaming
    internal enum BaseDictId : ushort
    {
        ST_Phrases,
        ST_Characters,
        ST_Punctuations,
        TS_Phrases,
        TS_Characters,
        TS_Punctuations,
        TW_Variants,
        TW_Phrases,
        TW_Phrases_Rev,
        TW_Variants_Rev,
        TW_Variants_Rev_Phrases,
        HK_Variants,
        HK_Variants_Rev,
        HK_Variants_Rev_Phrases,
        JP_Variants,
        JPS_Phrases,
        JPS_Characters,
        JP_Variants_Rev,
    }
    // ReSharper restore InconsistentNaming

    /// <summary>
    /// Immutable, hashable key representing the ordered list of dictionary IDs
    /// that make up one conversion round.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each <see cref="RoundKey"/> corresponds to the exact sequence of
    /// <see cref="BaseDictId"/> values used for a given conversion round.
    /// This allows <see cref="ConversionPlanCache"/> to share the same
    /// <see cref="StarterUnion"/> instance across different plans that
    /// happen to have identical dictionary layouts.
    /// </para>
    /// <para>
    /// The underlying <c>ushort[]</c> is stored in the order passed to the
    /// constructor; the sequence must be stable and identical for equality
    /// and hash code calculations to match.
    /// </para>
    /// <para>
    /// Since dictionary layouts are small (typically 1–3 entries per round),
    /// the equality and hash logic is implemented directly without
    /// allocating intermediate objects, making it fast enough for
    /// high-frequency cache lookups.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// var ids = new ushort[] { (ushort)BaseDictId.ST_Phrases, (ushort)BaseDictId.ST_Characters };
    /// var key = new RoundKey(ids);
    /// var union = _unionCache.GetOrAdd(key, _ => StarterUnion.Build(dicts));
    /// </code>
    /// </para>
    /// </remarks>
    internal readonly struct RoundKey : IEquatable<RoundKey>
    {
        private readonly ushort[] _ids; // ordered per-round dict IDs

        public RoundKey(ushort[] ids)
        {
            _ids = ids;
        }

        public bool Equals(RoundKey other)
        {
            if (ReferenceEquals(_ids, other._ids)) return true;
            if (_ids == null || other._ids == null || _ids.Length != other._ids.Length) return false;
            for (var i = 0; i < _ids.Length; i++)
                if (_ids[i] != other._ids[i])
                    return false;
            return true;
        }

        public override bool Equals(object obj) => obj is RoundKey k && Equals(k);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = 17;
                if (_ids == null) return h;
                for (var i = 0; i < _ids.Length; i++)
                    h = h * 31 + _ids[i];
                return h;
            }
        }

        public override string ToString() =>
            _ids == null ? "[]" : "[" + string.Join(",", _ids.Select(x => x.ToString())) + "]";
    }

    // ---- Notes --------------------------------------------------------------------------------
    // - This file assumes existing types in your project:
    //   - OpenccConfig (enum), DictRefs (rounds with optional StarterUnion args), DictWithMaxLength,
    //     StarterUnion (with static Build(IReadOnlyList<DictWithMaxLength>)).
    // - Inject your lazy dictionary via the constructor: () => _lazyDictionary.Value
    // - Thread-safe: caches are ConcurrentDictionary and StarterUnion is immutable after Build().
    // - To hot-reload dicts: rebuild DictionaryMaxlength, then call cache.Clear().
}