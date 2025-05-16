using System.Text.Json;
using DictionaryLib;

namespace DictionaryLibTests;

[TestClass]
public class DictionaryMaxlengthTests
{
    private const string OutputDir = "test_output";

    [TestInitialize]
    public void Init()
    {
        if (!Directory.Exists(OutputDir))
            Directory.CreateDirectory(OutputDir);
    }

    [TestMethod]
    public void TestFromDicts()
    {
        var dict = DictionaryMaxlength.FromDicts();
        Assert.IsNotNull(dict);
        Assert.IsTrue(dict.st_characters.Data.Count > 0 || dict.ts_characters.Data.Count > 0);
    }

    [TestMethod]
    public void TestSerialization()
    {
        var dict = DictionaryMaxlength.FromDicts();
        var jsonPath = Path.Combine(OutputDir, "test_dict.json");

        dict.SerializeToJson(jsonPath);
        Assert.IsTrue(File.Exists(jsonPath));

        var loaded = DictionaryMaxlength.DeserializedFromJson(jsonPath);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(dict.ts_characters.Data.Count, loaded.ts_characters.Data.Count);
    }


    [TestMethod]
    public void TestJsonSerialization()
    {
        var dict = DictionaryMaxlength.FromDicts();
        var jsonPath = Path.Combine(OutputDir, "test_dict1.json");

        dict.SerializeToJson(jsonPath);
        Assert.IsTrue(File.Exists(jsonPath));

        var content = File.ReadAllText(jsonPath);
        var json = JsonDocument.Parse(content);
        Assert.IsTrue(json.RootElement.TryGetProperty("ts_phrases", out _));
    }
}