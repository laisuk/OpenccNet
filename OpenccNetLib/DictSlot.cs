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
        STCharacters,

        /// <summary>Simplified-to-Traditional phrase mappings.</summary>
        STPhrases,

        /// <summary>Simplified-to-Traditional punctuation mappings.</summary>
        STPunctuations,

        /// <summary>Traditional-to-Simplified character mappings.</summary>
        TSCharacters,

        /// <summary>Traditional-to-Simplified phrase mappings.</summary>
        TSPhrases,

        /// <summary>Traditional-to-Simplified punctuation mappings.</summary>
        TSPunctuations,

        /// <summary>Traditional-to-Taiwan phrase mappings.</summary>
        TWPhrases,

        /// <summary>Taiwan-to-Traditional phrase mappings.</summary>
        TWPhrasesRev,

        /// <summary>Traditional-to-Taiwan character variant mappings.</summary>
        TWVariants,

        /// <summary>Traditional-to-Taiwan phrase variant mappings applied before <see cref="TWVariants"/>.</summary>
        TWVariantsPhrases,

        /// <summary>Taiwan-to-Traditional character variant mappings.</summary>
        TWVariantsRev,

        /// <summary>Taiwan-to-Traditional phrase variant mappings.</summary>
        TWVariantsRevPhrases,

        /// <summary>Traditional-to-Hong Kong character variant mappings.</summary>
        HKVariants,

        /// <summary>Traditional-to-Hong Kong phrase variant mappings applied before <see cref="HKVariants"/>.</summary>
        HKVariantsPhrases,

        /// <summary>Hong Kong-to-Traditional character variant mappings.</summary>
        HKVariantsRev,

        /// <summary>Hong Kong-to-Traditional phrase variant mappings.</summary>
        HKVariantsRevPhrases,

        /// <summary>Traditional Kyujitai-to-Japanese Shinjitai character mappings.</summary>
        JPSCharacters,

        /// <summary>Traditional Kyujitai-to-Japanese Shinjitai phrase mappings.</summary>
        JPSPhrases,

        /// <summary>Traditional-to-Japanese character variant mappings.</summary>
        JPVariants,

        /// <summary>Japanese variant-to-Traditional character mappings.</summary>
        JPVariantsRev,

        /// <summary>
        /// Traditional-to-Hong Kong phrase mappings.
        /// Added after existing values to preserve enum numeric stability.
        /// </summary>
        HKPhrases,

        /// <summary>
        /// Hong Kong-to-Traditional phrase mappings.
        /// Added after existing values to preserve enum numeric stability.
        /// </summary>
        HKPhrasesRev
    }

    // ReSharper restore InconsistentNaming
}