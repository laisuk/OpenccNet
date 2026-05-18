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
        STCharacters,
        STPhrases,
        STPunctuations,
        TSCharacters,
        TSPhrases,
        TSPunctuations,
        TWPhrases,
        TWPhrasesRev,
        TWVariants,
        TWVariantsRev,
        TWVariantsRevPhrases,
        HKVariants,
        HKVariantsRev,
        HKVariantsRevPhrases,
        JPSCharacters,
        JPSPhrases,
        JPVariants,
        JPVariantsRev
    }

    // ReSharper restore InconsistentNaming
}