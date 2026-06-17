namespace OpenccNetLib
{
    // ReSharper disable InconsistentNaming

    /// <summary>
    /// Identifies a built-in OpenCC dictionary slot used by file-level custom
    /// dictionary loading and post-load custom dictionary customization.
    /// </summary>
    /// <remarks>
    /// Slots can be customized through <see cref="DictionaryLib.FromDicts"/>
    /// during dictionary creation, or through <see cref="DictionaryLib.WithCustomDicts"/>
    /// after a <see cref="DictionaryMaxlength"/> has already been loaded.
    /// </remarks>
    public enum DictSlot
    {
        /// <summary>Simplified-to-Traditional character mappings.</summary>
        STCharacters = 0,

        /// <summary>Simplified-to-Traditional phrase mappings.</summary>
        STPhrases = 1,

        /// <summary>Simplified-to-Traditional punctuation mappings.</summary>
        STPunctuations = 2,

        /// <summary>Traditional-to-Simplified character mappings.</summary>
        TSCharacters = 3,

        /// <summary>Traditional-to-Simplified phrase mappings.</summary>
        TSPhrases = 4,

        /// <summary>Traditional-to-Simplified punctuation mappings.</summary>
        TSPunctuations = 5,

        /// <summary>Traditional-to-Taiwan phrase mappings.</summary>
        TWPhrases = 6,

        /// <summary>Taiwan-to-Traditional phrase mappings.</summary>
        TWPhrasesRev = 7,

        /// <summary>Traditional-to-Taiwan character variant mappings.</summary>
        TWVariants = 8,

        /// <summary>Taiwan-to-Traditional character variant mappings.</summary>
        TWVariantsRev = 9,

        /// <summary>Taiwan-to-Traditional phrase variant mappings.</summary>
        TWVariantsRevPhrases = 10,

        /// <summary>Traditional-to-Hong Kong character variant mappings.</summary>
        HKVariants = 11,

        /// <summary>Hong Kong-to-Traditional character variant mappings.</summary>
        HKVariantsRev = 12,

        /// <summary>Hong Kong-to-Traditional phrase variant mappings.</summary>
        HKVariantsRevPhrases = 13,

        /// <summary>Japanese Shinjitai-to-Traditional Kyujitai character mappings.</summary>
        JPSCharacters = 14,

        /// <summary>Japanese Shinjitai-to-Traditional Kyujitai phrase mappings.</summary>
        JPSPhrases = 15,

        /// <summary>
        /// Retired Japanese variant dictionary slot retained only for source and numeric compatibility.
        /// </summary>
        /// <remarks>This slot is not accepted by custom dictionary APIs.</remarks>
        [System.Obsolete("JPVariants is no longer an active dictionary slot.", false)]
        JPVariants = 16,

        /// <summary>
        /// Retired reverse Japanese variant dictionary slot retained only for source and numeric compatibility.
        /// </summary>
        /// <remarks>This slot is not accepted by custom dictionary APIs.</remarks>
        [System.Obsolete("JPVariantsRev is no longer an active dictionary slot.", false)]
        JPVariantsRev = 17,

        /// <summary>Traditional-to-Taiwan phrase variant mappings applied before <see cref="TWVariants"/>.</summary>
        TWVariantsPhrases = 18,

        /// <summary>Traditional-to-Hong Kong phrase variant mappings applied before <see cref="HKVariants"/>.</summary>
        HKVariantsPhrases = 19,

        /// <summary>Traditional Kyujitai-to-Japanese Shinjitai character mappings from JPShinjitaiCharactersRev.txt.</summary>
        JPSCharactersRev = 20,

        /// <summary>
        /// Traditional-to-Hong Kong phrase mappings.
        /// Added after existing values to preserve enum numeric stability.
        /// </summary>
        HKPhrases = 21,

        /// <summary>
        /// Hong Kong-to-Traditional phrase mappings.
        /// Added after existing values to preserve enum numeric stability.
        /// </summary>
        HKPhrasesRev = 22
    }

    // ReSharper restore InconsistentNaming
}