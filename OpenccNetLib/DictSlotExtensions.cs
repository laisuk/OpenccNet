using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OpenccNetLib
{
    /// <summary>
    /// Provides the canonical names, active values, and strict parsing rules for
    /// <see cref="DictSlot"/>.
    /// </summary>
    /// <remarks>
    /// Parsing is case-insensitive, but accepts only canonical active-slot names.
    /// Numeric enum strings and undefined values are not accepted.
    /// Use <see cref="ActiveSlots"/> for discovery and
    /// <see cref="CustomDictSpec.Parse(string)"/> to parse a complete portable
    /// custom-dictionary token.
    /// </remarks>
    public static class DictSlotExtensions
    {
        private static readonly DictSlot[] ActiveSlotArray =
        {
            DictSlot.STCharacters,
            DictSlot.STPhrases,
            DictSlot.STPunctuations,
            DictSlot.TSCharacters,
            DictSlot.TSPhrases,
            DictSlot.TSPunctuations,
            DictSlot.TWPhrases,
            DictSlot.TWPhrasesRev,
            DictSlot.TWVariants,
            DictSlot.TWVariantsRev,
            DictSlot.TWVariantsRevPhrases,
            DictSlot.HKVariants,
            DictSlot.HKVariantsRev,
            DictSlot.HKVariantsRevPhrases,
            DictSlot.JPSCharacters,
            DictSlot.JPSPhrases,
            DictSlot.TWVariantsPhrases,
            DictSlot.HKVariantsPhrases,
            DictSlot.JPSCharactersRev,
            DictSlot.HKPhrases,
            DictSlot.HKPhrasesRev
        };

        private static readonly Dictionary<string, DictSlot> SlotsByName = CreateSlotsByName();

        private static readonly HashSet<DictSlot> ActiveSlotSet =
            new HashSet<DictSlot>(ActiveSlotArray);

        /// <summary>
        /// Gets all currently supported dictionary slots in stable enum order.
        /// Undefined numeric gaps are excluded.
        /// </summary>
        public static IReadOnlyList<DictSlot> ActiveSlots { get; } = new ReadOnlyCollection<DictSlot>(ActiveSlotArray);

        /// <summary>
        /// Determines whether a slot is currently supported by custom dictionary APIs.
        /// </summary>
        /// <param name="slot">The slot to inspect.</param>
        /// <returns><c>true</c> for an active slot; otherwise, <c>false</c>.</returns>
        public static bool IsActive(this DictSlot slot)
        {
            return ActiveSlotSet.Contains(slot);
        }

        /// <summary>
        /// Returns the canonical C# enum name for an active dictionary slot.
        /// </summary>
        /// <param name="slot">The active dictionary slot.</param>
        /// <returns>The canonical slot name.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="slot"/> is undefined.
        /// </exception>
        public static string ToCanonicalName(this DictSlot slot)
        {
            if (!slot.IsActive())
                throw new ArgumentOutOfRangeException(
                    nameof(slot),
                    slot,
                    "Unknown dictionary slot.");

            return slot.ToString();
        }

        /// <summary>
        /// Strictly parses a canonical dictionary slot name.
        /// </summary>
        /// <param name="value">The canonical name to parse. Matching is case-insensitive.</param>
        /// <param name="slot">The parsed active slot when this method returns <c>true</c>.</param>
        /// <returns>
        /// <c>true</c> when <paramref name="value"/> names an active slot; otherwise,
        /// <c>false</c>. Numeric enum strings return <c>false</c>.
        /// </returns>
        public static bool TryParse(string value, out DictSlot slot)
        {
            slot = default;
            return value != null && SlotsByName.TryGetValue(value.Trim(), out slot);
        }

        /// <summary>
        /// Strictly parses a canonical dictionary slot name.
        /// </summary>
        /// <param name="value">The canonical name to parse. Matching is case-insensitive.</param>
        /// <returns>The parsed active dictionary slot.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="value"/> is null, empty, numeric, or unknown.
        /// </exception>
        public static DictSlot Parse(string value)
        {
            return TryParse(value, out var slot)
                ? slot
                : throw new ArgumentException("Unknown dictionary slot '" + value + "'.", nameof(value));
        }

        private static Dictionary<string, DictSlot> CreateSlotsByName()
        {
            var slots = new Dictionary<string, DictSlot>(StringComparer.OrdinalIgnoreCase);
            foreach (var slot in ActiveSlotArray)
                slots.Add(slot.ToString(), slot);

            return slots;
        }
    }
}
