using OpenccNetLib;

namespace OpenccNetTests;

[TestClass]
public class CustomDictSpecTests
{
    [TestMethod]
    [DataRow("stphrases:append:custom.txt", DictSlot.STPhrases, CustomDictMode.Append, "custom.txt")]
    [DataRow("HKPhrasesRev:override:hk.txt", DictSlot.HKPhrasesRev, CustomDictMode.Override, "hk.txt")]
    [DataRow(" StPhrases : ApPeNd : custom.txt ", DictSlot.STPhrases, CustomDictMode.Append, "custom.txt")]
    public void Parse_ValidSpecifications(
        string value,
        DictSlot expectedSlot,
        CustomDictMode expectedMode,
        string expectedPath)
    {
        var spec = CustomDictSpec.Parse(value);

        Assert.AreEqual(expectedSlot, spec.Slot);
        Assert.AreEqual(expectedMode, spec.Mode);
        CollectionAssert.AreEqual(new[] { expectedPath }, spec.Paths);
    }

    [TestMethod]
    [DataRow(@"stphrases:append:C:\data\custom.txt", @"C:\data\custom.txt")]
    [DataRow("stphrases:override:data:regional:custom.txt", "data:regional:custom.txt")]
    public void Parse_PreservesColonsInPath(string value, string expectedPath)
    {
        var spec = CustomDictSpec.Parse(value);

        CollectionAssert.AreEqual(new[] { expectedPath }, spec.Paths);
    }

    [TestMethod]
    public void Parse_RejectsNullInput()
    {
        Assert.Throws<ArgumentException>(() => CustomDictSpec.Parse(null!));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("stphrases")]
    [DataRow("stphrases:append")]
    [DataRow("stphrases:append:")]
    [DataRow(":append:custom.txt")]
    public void Parse_RejectsEmptyOrMalformedInput(string value)
    {
        Assert.Throws<ArgumentException>(() => CustomDictSpec.Parse(value));
    }

    [TestMethod]
    [DataRow("unknown:append:custom.txt")]
    [DataRow("1:append:custom.txt")]
    [DataRow("16:append:custom.txt")]
    [DataRow("JPVariants:append:custom.txt")]
    [DataRow("JPVariantsRev:append:custom.txt")]
    public void Parse_RejectsUnknownNumericAndRetiredSlots(string value)
    {
        Assert.Throws<ArgumentException>(() => CustomDictSpec.Parse(value));
    }

    [TestMethod]
    [DataRow("stphrases:merge:custom.txt")]
    [DataRow("stphrases:0:custom.txt")]
    [DataRow("stphrases:2:custom.txt")]
    public void Parse_RejectsUnknownAndNumericModes(string value)
    {
        Assert.Throws<ArgumentException>(() => CustomDictSpec.Parse(value));
    }

    [TestMethod]
    public void FromFile_ConstructsWithoutCheckingFileExistence()
    {
        var path = Path.Combine("missing", Guid.NewGuid() + ".txt");

        var spec = CustomDictSpec.FromFile(DictSlot.TSPhrases, path, CustomDictMode.Override);

        Assert.AreEqual(DictSlot.TSPhrases, spec.Slot);
        Assert.AreEqual(CustomDictMode.Override, spec.Mode);
        CollectionAssert.AreEqual(new[] { path }, spec.Paths);
    }

    [TestMethod]
    public void DictSlotCompanion_ExposesCanonicalNamesAndOnlyActiveSlots()
    {
        var expected = new[]
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

        CollectionAssert.AreEqual(expected, DictSlotExtensions.ActiveSlots.ToArray());

        foreach (var slot in expected)
        {
            var canonicalName = slot.ToString();
            Assert.AreEqual(canonicalName, slot.ToCanonicalName());
            Assert.IsTrue(DictSlotExtensions.TryParse(canonicalName.ToLowerInvariant(), out var parsed));
            Assert.AreEqual(slot, parsed);
            Assert.IsTrue(slot.IsActive());
        }

        Assert.IsFalse(DictSlotExtensions.TryParse("16", out _));
        Assert.IsFalse(DictSlotExtensions.TryParse("JPVariants", out _));
        Assert.IsFalse(((DictSlot)16).IsActive());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((DictSlot)16).ToCanonicalName());
    }
}
