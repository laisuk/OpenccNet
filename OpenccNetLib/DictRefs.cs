using System;
using System.Runtime.CompilerServices;

namespace OpenccNetLib
{
    /// <summary>
    /// Represents a fully resolved OpenCC conversion plan consisting of
    /// one to three sequential replacement rounds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each round contains:
    /// <list type="bullet">
    ///   <item><description>
    ///     A fixed array of <see cref="DictWithMaxLength"/> objects describing the
    ///     dictionary entries to apply in that round.
    ///   </description></item>
    ///   <item><description>
    ///     A prebuilt <see cref="StarterUnion"/> that provides the fast lookup metadata
    ///     (starter presence, key-length masks, per-starter caps, surrogate detection, etc.)
    ///     derived from those dictionaries.
    ///   </description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// A <see cref="DictRefs"/> instance is immutable after construction and may be safely
    /// shared across multiple threads.  All heavy preprocessing is performed once during
    /// construction of the <see cref="StarterUnion"/>, ensuring that runtime conversions
    /// only pay the minimal cost of applying replacements.
    /// </para>
    ///
    /// <para>
    /// The conversion engine invokes <see cref="ApplySegmentReplace"/> to process one round
    /// at a time.  The output of each round becomes the input to the next.
    /// </para>
    /// </remarks>
    public sealed class DictRefs
    {
        /// <summary>
        /// Internal container for a single conversion round.
        /// Holds both the raw dictionaries and the precomputed union metadata.
        /// </summary>
        private readonly struct Round
        {
            public Round(DictWithMaxLength[] dicts, StarterUnion union)
            {
                Dicts = dicts;
                Union = union;
            }

            /// <summary>
            /// The dictionaries used for this round.  The engine performs
            /// greedy, longest-match replacement using entries from these dictionaries.
            /// </summary>
            public DictWithMaxLength[] Dicts { get; }

            /// <summary>
            /// The precomputed starter metadata used to accelerate lookups.
            /// </summary>
            public StarterUnion Union { get; }
        }

        private readonly Round _round1;
        private Round? _round2;
        private Round? _round3;

        /// <summary>
        /// Creates a <see cref="DictRefs"/> instance with the required first round.
        /// </summary>
        /// <param name="round1">
        /// The dictionary array for the first conversion round.
        /// </param>
        /// <param name="union1">
        /// Optional prebuilt <see cref="StarterUnion"/>.  
        /// If <c>null</c>, a new union is built from <paramref name="round1"/>.
        /// </param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DictRefs(DictWithMaxLength[] round1, StarterUnion union1 = null)
        {
            _round1 = new Round(round1, union1 ?? StarterUnion.Build(round1));
        }

        /// <summary>
        /// Adds an optional second conversion round.
        /// </summary>
        /// <param name="round2">The dictionaries for the second round.</param>
        /// <param name="union2">
        /// Optional prebuilt <see cref="StarterUnion"/> for these dictionaries.
        /// If <c>null</c>, it will be built automatically.
        /// </param>
        /// <returns>The same <see cref="DictRefs"/> instance for fluent chaining.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DictRefs WithRound2(DictWithMaxLength[] round2, StarterUnion union2 = null)
        {
            _round2 = new Round(round2, union2 ?? StarterUnion.Build(round2));
            return this;
        }

        /// <summary>
        /// Adds an optional third conversion round.
        /// </summary>
        /// <param name="round3">The dictionaries for the third round.</param>
        /// <param name="union3">
        /// Optional prebuilt <see cref="StarterUnion"/>.
        /// If <c>null</c>, it will be built automatically.
        /// </param>
        /// <returns>The same <see cref="DictRefs"/> instance for fluent chaining.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DictRefs WithRound3(DictWithMaxLength[] round3, StarterUnion union3 = null)
        {
            _round3 = new Round(round3, union3 ?? StarterUnion.Build(round3));
            return this;
        }

        /// <summary>
        /// Executes the full conversion plan by applying the provided
        /// <paramref name="segmentReplace"/> function for each round.
        /// </summary>
        /// <param name="inputText">The input text to convert.</param>
        /// <param name="segmentReplace">
        /// A function that performs a single conversion round.
        /// <para>
        ///   Signature:
        ///   <c>(string input, DictWithMaxLength[] dicts, StarterUnion union) → string</c>
        /// </para>
        /// <para>
        ///   The function is expected to run a greedy, longest-match replacement using
        ///   the dictionaries supplied for the round together with the associated
        ///   <see cref="StarterUnion"/> lookup metadata.
        /// </para>
        /// </param>
        /// <returns>
        /// The fully converted text after all configured rounds have been applied.
        /// </returns>
        public string ApplySegmentReplace(
            string inputText,
            Func<string, DictWithMaxLength[], StarterUnion, string> segmentReplace)
        {
            var output = segmentReplace(inputText, _round1.Dicts, _round1.Union);
            if (_round2 is Round r2)
                output = segmentReplace(output, r2.Dicts, r2.Union);
            if (_round3 is Round r3)
                output = segmentReplace(output, r3.Dicts, r3.Union);
            return output;
        }
    }
}