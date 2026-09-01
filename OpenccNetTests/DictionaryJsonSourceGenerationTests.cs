using OpenccNetLib;

namespace OpenccNetTests;

[TestClass]
public class DictionaryJsonSourceGenerationTests
{
    [TestMethod]
    public void SaveAndLoadJsonCompressed_RoundTripsDictionary()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "OpenccNetTests_" + Guid.NewGuid().ToString("N") + ".zstd");

        try
        {
            DictionaryLib.SaveJsonCompressed(path, DictionaryLib.Provider);
            var loaded = DictionaryLib.LoadJsonCompressed(path);

            Assert.HasCount(
                DictionaryLib.Provider.st_characters.Dict.Count,
                loaded.st_characters.Dict);
            Assert.IsTrue(loaded.st_characters.StarterLenMask is { Count: > 0 });
            Assert.AreEqual("漢字", new Opencc(OpenccConfig.S2T, loaded).Convert("汉字"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public void UseDictionaryFromJsonString_LoadsAndNormalizesDictionary()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "OpenccNetTests_" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            DictionaryLib.SerializeToJson(path, DictionaryLib.Provider);
            Opencc.UseDictionaryFromJsonString(File.ReadAllText(path));

            Assert.AreEqual("漢字", new Opencc(OpenccConfig.S2T).Convert("汉字"));
        }
        finally
        {
            Opencc.UseDefaultDictionary();
            File.Delete(path);
        }
    }
}
