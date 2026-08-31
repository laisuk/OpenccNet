using OpenccNetLib;

namespace OpenccNetTests;

[TestClass]
public class EmbeddedBuiltinResourcesTests
{
    private static readonly string[] BuiltinTableFileNames =
    {
        "dictionary_maxlength.zstd",
        "CJK_Compatibility_Ideographs.txt",
        "Unicode_Compatibility.txt",
        "CharactersTofu.txt"
    };

    [TestMethod]
    public void BuiltinTables_LoadWithoutPhysicalDictFiles()
    {
        foreach (var fileName in BuiltinTableFileNames)
        {
            Assert.IsFalse(
                File.Exists(Path.Combine(AppContext.BaseDirectory, "dicts", fileName)),
                $"Built-in table should not be copied to the test output: {fileName}");
        }

        var resourceNames = typeof(Opencc).Assembly.GetManifestResourceNames();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                EmbeddedData.CompatIdeographsResourceName,
                EmbeddedData.UnicodeCompatResourceName,
                EmbeddedData.CharactersTofuResourceName
            },
            resourceNames);
        CollectionAssert.Contains(resourceNames, "OpenccNetLib.Resources.dictionary_maxlength.zstd");

        Assert.AreEqual(
            "天龍八部書裡的喬峰是契丹人",
            new Opencc().NormalizeCompatExtended(
                "天龍八部書裡的喬峰是契丹人"));
        Assert.AreEqual("酉：", Opencc.NormalizeUnicodeCompat("⾣︰"));
        Assert.AreEqual("騑", DeTofu.Convert("𬴂", DeTofuLevel.ExtB));
        Assert.AreEqual("漢字", new Opencc(OpenccConfig.S2T).Convert("汉字"));
    }

    [TestMethod]
    public void DeTofuParser_AcceptsThreeColumnFormat()
    {
        var entries = DeTofu.ParseEntries("𬴂\t騑\tB\n");

        Assert.HasCount(1, entries);
        Assert.AreEqual(0x2CD02, entries[0].Tofu);
        Assert.AreEqual("騑", entries[0].Fallback);
        Assert.AreEqual(DeTofuLevel.ExtB, entries[0].Extension);
    }
}
