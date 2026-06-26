using OpenccNetLib;

namespace OpenccNet;

internal static class CliUtils
{
    internal static CustomDictSpec ParseCustomDictSpec(string value)
    {
        var parts = value.Split(':', 3);

        if (parts.Length != 3)
            throw new ArgumentException(
                $"Invalid --custom-dict '{value}'. Expected: <slot>:<mode>:<path>");

        if (!TryParseDictSlot(parts[0], out var slot))
            throw new ArgumentException($"Unknown dictionary slot '{parts[0]}'.");

        if (!Enum.TryParse(parts[1], true, out CustomDictMode mode))
            throw new ArgumentException(
                $"Unknown custom dictionary mode '{parts[1]}'. Valid values: append, override.");

        var path = parts[2].Trim();

        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Custom dictionary path cannot be empty.");

        return new CustomDictSpec
        {
            Slot = slot,
            Mode = mode,
            Paths = new[] { path }
        };
    }

    private static bool TryParseDictSlot(string value, out DictSlot slot)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "stcharacters":
                slot = DictSlot.STCharacters;
                return true;
            case "stphrases":
                slot = DictSlot.STPhrases;
                return true;
            case "stpunctuations":
                slot = DictSlot.STPunctuations;
                return true;

            case "tscharacters":
                slot = DictSlot.TSCharacters;
                return true;
            case "tsphrases":
                slot = DictSlot.TSPhrases;
                return true;
            case "tspunctuations":
                slot = DictSlot.TSPunctuations;
                return true;

            case "twphrases":
                slot = DictSlot.TWPhrases;
                return true;
            case "twphrasesrev":
                slot = DictSlot.TWPhrasesRev;
                return true;

            case "twvariants":
                slot = DictSlot.TWVariants;
                return true;
            case "twvariantsphrases":
                slot = DictSlot.TWVariantsPhrases;
                return true;
            case "twvariantsrev":
                slot = DictSlot.TWVariantsRev;
                return true;
            case "twvariantsrevphrases":
                slot = DictSlot.TWVariantsRevPhrases;
                return true;

            case "hkphrases":
                slot = DictSlot.HKPhrases;
                return true;
            case "hkphrasesrev":
                slot = DictSlot.HKPhrasesRev;
                return true;

            case "hkvariants":
                slot = DictSlot.HKVariants;
                return true;
            case "hkvariantsphrases":
                slot = DictSlot.HKVariantsPhrases;
                return true;
            case "hkvariantsrev":
                slot = DictSlot.HKVariantsRev;
                return true;
            case "hkvariantsrevphrases":
                slot = DictSlot.HKVariantsRevPhrases;
                return true;

            case "jpscharacters":
                slot = DictSlot.JPSCharacters;
                return true;
            case "jpscharactersrev":
                slot = DictSlot.JPSCharactersRev;
                return true;
            case "jpsphrases":
                slot = DictSlot.JPSPhrases;
                return true;

            default:
                return Enum.TryParse(value, true, out slot);
        }
    }
}