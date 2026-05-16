namespace OpenccNetLib
{
    // ReSharper disable InconsistentNaming
    
    /// <summary>
    /// Identifies a built-in OpenCC dictionary slot that can be loaded,
    /// overridden, or appended through <see cref="DictionaryLib.FromDicts"/>.
    /// </summary>
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
