using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenccNetLib
{
    /// <summary>
    /// Helper class for managing multiple rounds of dictionary references for multi-stage text conversion.
    /// Each round contains its own dictionary list and precomputed maximum word length to optimize processing.
    /// </summary>
    public class DictRefs
    {
        private readonly (List<DictWithMaxLength> Dicts, int MaxLength) _round1;
        private (List<DictWithMaxLength> Dicts, int MaxLength)? _round2;
        private (List<DictWithMaxLength> Dicts, int MaxLength)? _round3;

        /// <summary>
        /// Initializes a new instance of the <see cref="DictRefs"/> class with the first round of dictionaries.
        /// The maximum word length is calculated once for performance.
        /// </summary>
        /// <param name="round1">The first round of dictionaries to use for text conversion.</param>
        public DictRefs(List<DictWithMaxLength> round1)
        {
            _round1 = (round1, round1.Count > 0 ? round1.Max(d => d.MaxLength) : 1);
        }

        /// <summary>
        /// Sets the second round of dictionaries for multi-stage text conversion.
        /// The maximum word length for this round is computed during assignment.
        /// </summary>
        /// <param name="round2">The second round of dictionaries.</param>
        /// <returns>The current <see cref="DictRefs"/> instance for fluent chaining.</returns>
        public DictRefs WithRound2(List<DictWithMaxLength> round2)
        {
            _round2 = (round2, round2.Count > 0 ? round2.Max(d => d.MaxLength) : 1);
            return this;
        }

        /// <summary>
        /// Sets the third round of dictionaries for multi-stage text conversion.
        /// The maximum word length for this round is computed during assignment.
        /// </summary>
        /// <param name="round3">The third round of dictionaries.</param>
        /// <returns>The current <see cref="DictRefs"/> instance for fluent chaining.</returns>
        public DictRefs WithRound3(List<DictWithMaxLength> round3)
        {
            _round3 = (round3, round3.Count > 0 ? round3.Max(d => d.MaxLength) : 1);
            return this;
        }

        /// <summary>
        /// Applies the given segment replacement function to the input text using all active rounds of dictionaries.
        /// Each round uses its own precomputed maximum word length for optimal segmentation.
        /// </summary>
        /// <param name="inputText">The input text to convert.</param>
        /// <param name="segmentReplace">
        /// A function that performs segment-based dictionary replacement, accepting:
        /// - input text
        /// - a list of dictionaries
        /// - the maximum word length for that dictionary group
        /// </param>
        /// <returns>The converted text after all applicable rounds.</returns>
        public string ApplySegmentReplace(string inputText,
            Func<string, List<DictWithMaxLength>, int, string> segmentReplace)
        {
            var output = segmentReplace(inputText, _round1.Dicts, _round1.MaxLength);
            if (_round2 != null)
                output = segmentReplace(output, _round2.Value.Dicts, _round2.Value.MaxLength);
            if (_round3 != null)
                output = segmentReplace(output, _round3.Value.Dicts, _round3.Value.MaxLength);
            return output;
        }
    }
}
