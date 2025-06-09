using System;
using System.Collections.Generic;

namespace OpenccNetLib
{
    /// <summary>
    /// Helper class for managing multiple rounds of dictionary references for multi-stage text conversion.
    /// </summary>
    public class DictRefs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DictRefs"/> class with the first round of dictionaries.
        /// </summary>
        /// <param name="round1">The first round of dictionaries to use for conversion.</param>
        public DictRefs(List<DictWithMaxLength> round1)
        {
            Round1 = round1;
        }

        private List<DictWithMaxLength> Round1 { get; }
        private List<DictWithMaxLength> Round2 { get; set; }
        private List<DictWithMaxLength> Round3 { get; set; }

        /// <summary>
        /// Sets the second round of dictionaries for conversion.
        /// </summary>
        /// <param name="round2">The second round of dictionaries.</param>
        /// <returns>The current <see cref="DictRefs"/> instance for chaining.</returns>
        public DictRefs WithRound2(List<DictWithMaxLength> round2)
        {
            Round2 = round2;
            return this;
        }

        /// <summary>
        /// Sets the third round of dictionaries for conversion.
        /// </summary>
        /// <param name="round3">The third round of dictionaries.</param>
        /// <returns>The current <see cref="DictRefs"/> instance for chaining.</returns>
        public DictRefs WithRound3(List<DictWithMaxLength> round3)
        {
            Round3 = round3;
            return this;
        }

        /// <summary>
        /// Applies the segment replacement function for each round of dictionaries.
        /// </summary>
        /// <param name="inputText">The input text to convert.</param>
        /// <param name="segmentReplace">The segment replacement function.</param>
        /// <returns>The converted text after all rounds.</returns>
        public string ApplySegmentReplace(string inputText,
            Func<string, List<DictWithMaxLength>, string> segmentReplace)
        {
            var output = segmentReplace(inputText, Round1);
            if (Round2 != null) output = segmentReplace(output, Round2);
            if (Round3 != null) output = segmentReplace(output, Round3);
            return output;
        }
    }

}