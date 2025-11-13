using System.Text.Json;
using OpenccNetLib;

namespace OpenccNetTests;

[TestClass]
public class DictionaryLibTests
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
        var dict = DictionaryLib.FromDicts();
        Assert.IsNotNull(dict);
        Assert.IsTrue(dict.st_characters.Dict.Count > 0 || dict.ts_characters.Dict.Count > 0);
    }

    [TestMethod]
    public void TestFromJson()
    {
        var dict = DictionaryLib.FromJson();
        Assert.IsNotNull(dict);
        Assert.IsTrue(dict.st_characters.Dict.Count > 0 || dict.ts_characters.Dict.Count > 0);
    }

    [TestMethod]
    public void TestFromCbor()
    {
        var dict = DictionaryLib.FromCbor();
        Assert.IsNotNull(dict);
        Assert.IsTrue(dict.st_characters.Dict.Count > 0 || dict.ts_characters.Dict.Count > 0);
    }

    [TestMethod]
    public void TestSerialization()
    {
        var dict = DictionaryLib.FromDicts();
        var jsonPath = Path.Combine(OutputDir, "test_dict.json");

        DictionaryLib.SerializeToJson(jsonPath);
        Assert.IsTrue(File.Exists(jsonPath));

        var loaded = DictionaryLib.DeserializedFromJson(jsonPath);
        Assert.IsNotNull(loaded);
        Assert.HasCount(dict.ts_characters.Dict.Count, loaded.ts_characters.Dict);
    }

    [TestMethod]
    public void TestSerializationUnescaped()
    {
        var dict = DictionaryLib.FromDicts();
        var jsonPath = Path.Combine(OutputDir, "test_dict_unescaped.json");

        // Serialize using unescaped Unicode output
        DictionaryLib.SerializeToJsonUnescaped(jsonPath);
        Assert.IsTrue(File.Exists(jsonPath), "Unescaped JSON file should be created.");

        // --- Fast check: only inspect the st_characters section ---
        // Read a small chunk of the JSON around "st_characters"
        string jsonSnippet;
        using (var reader = new StreamReader(jsonPath))
        {
            char[] buffer = new char[4096];
            int read = reader.ReadBlock(buffer, 0, buffer.Length);
            jsonSnippet = new string(buffer, 0, read);
        }

        // Verify that unescaped Unicode characters appear early in the file
        Assert.IsTrue(
            jsonSnippet.Contains("st_characters") &&
            !jsonSnippet.Contains("\\u4e00"),
            "Unescaped JSON should contain readable Unicode characters in st_characters section."
        );

        // Deserialize and compare dictionary sizes for consistency
        var loaded = DictionaryLib.DeserializedFromJson(jsonPath);
        Assert.IsNotNull(loaded, "Deserialized dictionary should not be null.");
        Assert.HasCount(dict.ts_characters.Dict.Count, loaded.ts_characters.Dict);
    }
    
    [TestMethod]
    public void TestSerializationUnescaped_NoSurrogates()
    {
        var jsonPath = Path.Combine(OutputDir, "test_dict_unescaped_no_surrogate.json");
        DictionaryLib.SerializeToJsonUnescaped(jsonPath);

        using var sr = new StreamReader(jsonPath);
        var buf = new char[8192];
        var n = sr.ReadBlock(buf, 0, buf.Length);
        var head = new string(buf, 0, n);

        Assert.IsFalse(head.Contains("\\uD8") || head.Contains("\\uDB") || head.Contains("\\uDC") || head.Contains("\\uDD"),
            "Unescaped JSON should not contain surrogate \\uD8xx/\\uDBxx/\\uDCxx/\\uDDxx sequences.");
    }

    [TestMethod]
    public void TestJsonSerialization()
    {
        var jsonPath = Path.Combine(OutputDir, "test_dict1.json");

        DictionaryLib.SerializeToJson(jsonPath);
        Assert.IsTrue(File.Exists(jsonPath));

        var content = File.ReadAllText(jsonPath);
        var json = JsonDocument.Parse(content);
        Assert.IsTrue(json.RootElement.TryGetProperty("ts_phrases", out _));
    }

    [TestMethod]
    [Ignore]
    public void TestDictLengthMaskAndLongLengths()
    {
        // Load all dictionaries (from dicts folder)
        var dicts = DictionaryLib.FromDicts();

        // Pick one for inspection
        var d = dicts.st_phrases;
        Assert.IsNotNull(d, "st_phrases dictionary should not be null");

        // --- LengthMask check ---
        // Decode bitmask: each bit n-1 means "key of length n exists"
        var mask = d.LengthMask;
        Console.WriteLine($"LengthMask = {mask} (0x{mask:X})");

        // Collect actual lengths from keys
        var actualLengths = new HashSet<int>();
        foreach (var k in d.Dict.Keys)
            actualLengths.Add(k.Length);

        // Verify 1..64-length keys are correctly represented in mask
        foreach (var len in actualLengths)
        {
            if (len > 64) continue;
            var bitSet = ((mask >> (len - 1)) & 1UL) != 0UL;
            Assert.IsTrue(bitSet,
                $"Expected bit for length {len} to be set in LengthMask (mask=0x{mask:X})");
        }

        // --- LongLengths check ---
        var longSet = d.LongLengths;
        if (actualLengths.Any(l => l > 64))
        {
            Assert.IsNotNull(longSet, "LongLengths should not be null if key > 64 exists");
            foreach (var len in actualLengths.Where(l => l > 64))
                Assert.Contains(len, longSet, $"Expected LongLengths to contain {len}");
        }
        else
        {
            Assert.IsTrue(longSet == null || longSet.Count == 0,
                "LongLengths should be null or empty when no key > 64");
        }

        // --- Sanity check ---
        Assert.IsTrue(d.MinLength > 0 && d.MinLength <= d.MaxLength,
            $"MinLength={d.MinLength}, MaxLength={d.MaxLength} must be valid");

        Console.WriteLine($"MinLength={d.MinLength}, MaxLength={d.MaxLength}");
        Console.WriteLine("LengthMask and LongLengths validation passed for st_phrases.");
    }
}