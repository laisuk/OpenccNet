using System;
using System.Collections.Concurrent;
using System.Threading;

namespace OpenccNetLib
{
    /// <summary>
    /// Internal process-wide cache for resolved conversion plans and starter unions.
    /// The active dictionary provider belongs to the cache snapshot so provider and
    /// derived plans are always replaced atomically as one unit.
    /// </summary>
    internal sealed class ConversionPlanCache
    {
        private static ConversionPlanCache _current =
            new ConversionPlanCache(() => DictionaryLib.Provider);

        private enum UnionKey
        {
            S2T,
            S2TPunct,
            T2S,
            T2SPunct,
            TwVariantsPair,
            TwRevPair,
            TwTriple,
            TwRevTriple,
            HkVariantsPair,
            HkRevPair,
            HkTriple,
            HkRevTriple,
            JpsCharactersRev,
            JpsPair,
            StPunctOnly
        }

        private readonly Func<DictionaryMaxlength> _dictionaryProvider;

        private readonly ConcurrentDictionary<PlanKey, DictRefs> _planCache =
            new ConcurrentDictionary<PlanKey, DictRefs>();

        private readonly ConcurrentDictionary<UnionKey, StarterUnion> _unionCacheByKey =
            new ConcurrentDictionary<UnionKey, StarterUnion>();

        private readonly ConcurrentDictionary<UnionKey, DictWithMaxLength[]> _dictArrayCacheByKey =
            new ConcurrentDictionary<UnionKey, DictWithMaxLength[]>();

        internal static ConversionPlanCache Current => Volatile.Read(ref _current);

        /// <summary>
        /// Gets the dictionary provider bound to the active cache snapshot.
        /// </summary>
        internal static DictionaryMaxlength Provider => Current._dictionaryProvider();

        internal static DictRefs GetCurrentPlan(OpenccConfig config, bool punctuation = false)
            => Current.GetPlan(config, punctuation);

        /// <summary>
        /// Publishes a fresh cache snapshot using the supplied dictionary.
        /// Existing conversions may finish with the previous snapshot.
        /// </summary>
        internal static void UseProvider(DictionaryMaxlength dictionary)
        {
            if (dictionary == null)
                throw new ArgumentNullException(nameof(dictionary));

            PublishProvider(() => dictionary);
        }

        /// <summary>
        /// Restores the active provider to the built-in default dictionary.
        /// </summary>
        internal static void ResetProvider()
            => PublishProvider(() => DictionaryLib.Provider);

        private static void PublishProvider(Func<DictionaryMaxlength> dictionaryProvider)
        {
            if (dictionaryProvider == null)
                throw new ArgumentNullException(nameof(dictionaryProvider));

            var replacement = new ConversionPlanCache(dictionaryProvider);
            Interlocked.Exchange(ref _current, replacement);
        }

        private ConversionPlanCache(Func<DictionaryMaxlength> dictionaryProvider)
        {
            _dictionaryProvider = dictionaryProvider ?? throw new ArgumentNullException(nameof(dictionaryProvider));
        }

        private DictRefs GetPlan(OpenccConfig config, bool punctuation = false)
            => _planCache.GetOrAdd(new PlanKey(config, punctuation), _ => BuildPlan(config, punctuation));

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
                    return WithOptionalTraditionalPunctuation(d, new DictRefs(r1, u1), punctuation);
                }

                case OpenccConfig.T2Twp:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.TwTriple, out var r1);
                    return WithOptionalTraditionalPunctuation(d, new DictRefs(r1, u1), punctuation);
                }

                case OpenccConfig.Tw2T:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.TwRevPair, out var r1);
                    return WithOptionalTraditionalPunctuation(d, new DictRefs(r1, u1), punctuation);
                }

                case OpenccConfig.Tw2Tp:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.TwRevTriple, out var r1);
                    return WithOptionalTraditionalPunctuation(d, new DictRefs(r1, u1), punctuation);
                }

                case OpenccConfig.T2Hk:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.HkVariantsPair, out var r1);
                    return WithOptionalTraditionalPunctuation(d, new DictRefs(r1, u1), punctuation);
                }

                case OpenccConfig.Hk2T:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.HkRevPair, out var r1);
                    return WithOptionalTraditionalPunctuation(d, new DictRefs(r1, u1), punctuation);
                }

                case OpenccConfig.T2Hkp:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.HkTriple, out var r1);
                    return WithOptionalTraditionalPunctuation(d, new DictRefs(r1, u1), punctuation);
                }

                case OpenccConfig.Hk2Tp:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.HkRevTriple, out var r1);
                    return WithOptionalTraditionalPunctuation(d, new DictRefs(r1, u1), punctuation);
                }

                case OpenccConfig.T2Jp:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.JpsCharactersRev, out var r1);
                    return new DictRefs(r1, u1);
                }

                case OpenccConfig.Jp2T:
                {
                    var u1 = GetOrAddUnionFor(d, UnionKey.JpsPair, out var r1);
                    return new DictRefs(r1, u1);
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(config), config, null);
            }
        }

        private DictRefs WithOptionalTraditionalPunctuation(
            DictionaryMaxlength d,
            DictRefs refs,
            bool punctuation)
        {
            if (!punctuation)
                return refs;

            var u2 = GetOrAddUnionFor(d, UnionKey.StPunctOnly, out var r2);
            return refs.WithRound2(r2, u2);
        }

        private StarterUnion GetOrAddUnionFor(
            DictionaryMaxlength d,
            UnionKey key,
            out DictWithMaxLength[] dicts)
        {
            dicts = _dictArrayCacheByKey.GetOrAdd(key, _ => BuildDicts(d, key));

            if (_unionCacheByKey.TryGetValue(key, out var existing))
                return existing;

            var built = StarterUnion.Build(dicts);
            return _unionCacheByKey.GetOrAdd(key, built);
        }

        private static DictWithMaxLength[] BuildDicts(DictionaryMaxlength d, UnionKey key)
        {
            switch (key)
            {
                case UnionKey.S2T:
                    return new[] { d.st_phrases, d.st_characters };

                case UnionKey.S2TPunct:
                    return new[] { d.st_phrases, d.st_characters, d.st_punctuations };

                case UnionKey.T2S:
                    return new[] { d.ts_phrases, d.ts_characters };

                case UnionKey.T2SPunct:
                    return new[] { d.ts_phrases, d.ts_characters, d.ts_punctuations };

                case UnionKey.TwVariantsPair:
                    return new[] { d.tw_variants_phrases, d.tw_variants };

                case UnionKey.TwRevPair:
                    return new[] { d.tw_variants_rev_phrases, d.tw_variants_rev };

                case UnionKey.TwRevTriple:
                    return new[] { d.tw_phrases_rev, d.tw_variants_rev_phrases, d.tw_variants_rev };

                case UnionKey.TwTriple:
                    return new[] { d.tw_phrases, d.tw_variants_phrases, d.tw_variants };

                case UnionKey.HkVariantsPair:
                    return new[] { d.hk_variants_phrases, d.hk_variants };

                case UnionKey.HkRevPair:
                    return new[] { d.hk_variants_rev_phrases, d.hk_variants_rev };

                case UnionKey.HkTriple:
                    return new[] { d.hk_phrases, d.hk_variants_phrases, d.hk_variants };

                case UnionKey.HkRevTriple:
                    return new[] { d.hk_phrases_rev, d.hk_variants_rev_phrases, d.hk_variants_rev };

                case UnionKey.JpsCharactersRev:
                    return new[] { d.jps_characters_rev };

                case UnionKey.JpsPair:
                    return new[] { d.jps_phrases, d.jps_characters };

                case UnionKey.StPunctOnly:
                    return new[] { d.st_punctuations };

                default:
                    throw new ArgumentOutOfRangeException(nameof(key), key, null);
            }
        }

        private readonly struct PlanKey : IEquatable<PlanKey>
        {
            private readonly OpenccConfig _config;
            private readonly bool _punctuation;

            public PlanKey(OpenccConfig config, bool punctuation)
            {
                _config = config;
                _punctuation = punctuation;
            }

            public bool Equals(PlanKey other)
                => _config == other._config && _punctuation == other._punctuation;

            public override bool Equals(object obj)
                => obj is PlanKey pk && Equals(pk);

            public override int GetHashCode()
                => ((int)_config * 397) ^ (_punctuation ? 1 : 0);
        }
    }
}
