using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace OpenccNetLib
{
    /// <summary>
    /// Represents a fully resolved OpenCC conversion plan consisting of up to three sequential rounds.
    /// Each round contains its dictionaries (<see cref="DictWithMaxLength"/> array),
    /// a prebuilt <see cref="StarterUnion"/> for fast lookups,
    /// and the maximum word length across all dictionaries in that round.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="DictRefs"/> instances are immutable once constructed and can be safely shared across threads.
    /// </para>
    /// <para>
    /// The conversion engine invokes <see cref="ApplySegmentReplace"/> to process each round sequentially,
    /// applying dictionary replacements and reusing the precomputed union tables for fast gating.
    /// </para>
    /// </remarks>
    public sealed class DictRefs
    {
        private readonly struct Round
        {
            public Round(DictWithMaxLength[] dicts, StarterUnion union, int maxLen)
            {
                Dicts = dicts;
                Union = union;
                MaxLength = maxLen;
            }

            public DictWithMaxLength[] Dicts { get; }
            public StarterUnion Union { get; }
            public int MaxLength { get; }
        }

        private readonly Round _round1;
        private Round? _round2;
        private Round? _round3;

        /// <summary>
        /// round1: if union is null, it will be built.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DictRefs(DictWithMaxLength[] round1, StarterUnion union1 = null)
        {
            var max1 = round1.Length > 0 ? round1.Max(d => d.MaxLength) : 1;
            _round1 = new Round(round1, union1 ?? StarterUnion.Build(round1), max1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DictRefs WithRound2(DictWithMaxLength[] round2, StarterUnion union2 = null)
        {
            var max2 = round2.Length > 0 ? round2.Max(d => d.MaxLength) : 1;
            _round2 = new Round(round2, union2 ?? StarterUnion.Build(round2), max2);
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DictRefs WithRound3(DictWithMaxLength[] round3, StarterUnion union3 = null)
        {
            var max3 = round3.Length > 0 ? round3.Max(d => d.MaxLength) : 1;
            _round3 = new Round(round3, union3 ?? StarterUnion.Build(round3), max3);
            return this;
        }

        /// <summary>
        /// Bridge: Apply SegmentReplace using (dicts, union, maxLen) per round.
        /// </summary>
        public string ApplySegmentReplace(
            string inputText,
            Func<string, DictWithMaxLength[], StarterUnion, int, string> segmentReplace)
        {
            var output = segmentReplace(inputText, _round1.Dicts, _round1.Union, _round1.MaxLength);
            if (_round2 is Round r2)
                output = segmentReplace(output, r2.Dicts, r2.Union, r2.MaxLength);
            if (_round3 is Round r3)
                output = segmentReplace(output, r3.Dicts, r3.Union, r3.MaxLength);
            return output;
        }
    }
}