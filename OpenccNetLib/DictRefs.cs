using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenccNetLib
{
    /// <summary>
    /// Multi-round plan: each round = (dicts, maxLen, union).
    /// </summary>
    public sealed class DictRefs
    {
        private readonly struct Round
        {
            public Round(List<DictWithMaxLength> dicts, StarterUnion union, int maxLen)
            {
                Dicts = dicts;
                Union = union;
                MaxLength = maxLen;
            }

            public List<DictWithMaxLength> Dicts { get; }
            public StarterUnion Union { get; }
            public int MaxLength { get; }
        }

        private readonly Round _round1;
        private Round? _round2;
        private Round? _round3;

        /// <summary>
        /// round1: if union is null, it will be built.
        /// </summary>
        public DictRefs(List<DictWithMaxLength> round1, StarterUnion union1 = null)
        {
            var max1 = round1.Count > 0 ? round1.Max(d => d.MaxLength) : 1;
            _round1 = new Round(round1, union1 ?? StarterUnion.Build(round1), max1);
        }

        public DictRefs WithRound2(List<DictWithMaxLength> round2, StarterUnion union2 = null)
        {
            var max2 = round2.Count > 0 ? round2.Max(d => d.MaxLength) : 1;
            _round2 = new Round(round2, union2 ?? StarterUnion.Build(round2), max2);
            return this;
        }

        public DictRefs WithRound3(List<DictWithMaxLength> round3, StarterUnion union3 = null)
        {
            var max3 = round3.Count > 0 ? round3.Max(d => d.MaxLength) : 1;
            _round3 = new Round(round3, union3 ?? StarterUnion.Build(round3), max3);
            return this;
        }

        /// <summary>
        /// Bridge: Apply SegmentReplace using (dicts, union, maxLen) per round.
        /// </summary>
        public string ApplySegmentReplace(
            string inputText,
            Func<string, List<DictWithMaxLength>, StarterUnion, int, string> segmentReplace)
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