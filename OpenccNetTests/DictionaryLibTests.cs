using System.Text;
using System.Text.Json;
using PeterO.Cbor;
using OpenccNetLib;

namespace OpenccNetTests;

[TestClass]
[DoNotParallelize]
public class DictionaryLibTests
{
    private const string OutputDir = "test_output";

    private static void AssertMetadataValid(DictWithMaxLength d)
    {
        Assert.IsGreaterThan(0, d.Count);
        Assert.IsGreaterThan(0, d.MaxLength);
        Assert.IsGreaterThan(0, d.MinLength);
        Assert.IsLessThanOrEqualTo(d.MaxLength, d.MinLength);
        Assert.AreNotEqual((ulong)0, d.LengthMask);
        Assert.IsTrue(d.StarterLenMask is { Count: > 0 });
    }

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
    public void TestFromDicts_LoadsForwardVariantPhraseSlots()
    {
        var dict = DictionaryLib.FromDicts();

        Assert.IsTrue(Enum.IsDefined(typeof(DictSlot), DictSlot.TWVariantsPhrases));
        Assert.IsTrue(Enum.IsDefined(typeof(DictSlot), DictSlot.HKVariantsPhrases));
        Assert.IsTrue(Enum.IsDefined(typeof(DictSlot), DictSlot.HKPhrases));
        Assert.IsTrue(Enum.IsDefined(typeof(DictSlot), DictSlot.HKPhrasesRev));
        Assert.IsTrue(Enum.IsDefined(typeof(DictSlot), DictSlot.JPSCharactersRev));
        AssertMetadataValid(dict.tw_variants_phrases);
        AssertMetadataValid(dict.hk_variants_phrases);
        AssertMetadataValid(dict.hk_phrases);
        AssertMetadataValid(dict.hk_phrases_rev);
        AssertMetadataValid(dict.jps_characters_rev);
    }

    [TestMethod]
    public void TestDictSlot_PreservesPublishedNumericValues()
    {
        var publishedValues = new Dictionary<DictSlot, int>
        {
            [DictSlot.STCharacters] = 0,
            [DictSlot.STPhrases] = 1,
            [DictSlot.STPunctuations] = 2,
            [DictSlot.TSCharacters] = 3,
            [DictSlot.TSPhrases] = 4,
            [DictSlot.TSPunctuations] = 5,
            [DictSlot.TWPhrases] = 6,
            [DictSlot.TWPhrasesRev] = 7,
            [DictSlot.TWVariants] = 8,
            [DictSlot.TWVariantsRev] = 9,
            [DictSlot.TWVariantsRevPhrases] = 10,
            [DictSlot.HKVariants] = 11,
            [DictSlot.HKVariantsRev] = 12,
            [DictSlot.HKVariantsRevPhrases] = 13,
            [DictSlot.JPSCharacters] = 14,
            [DictSlot.JPSPhrases] = 15
        };

        foreach (var pair in publishedValues)
            Assert.AreEqual(pair.Value, (int)pair.Key);

        Assert.IsTrue(Enum.TryParse("JPVariants", out DictSlot jpVariants));
        Assert.AreEqual(16, (int)jpVariants);
        Assert.IsTrue(Enum.TryParse("JPVariantsRev", out DictSlot jpVariantsRev));
        Assert.AreEqual(17, (int)jpVariantsRev);

        Assert.IsNotNull(typeof(DictSlot).GetField("JPVariants")
            ?.GetCustomAttributes(typeof(ObsoleteAttribute), false).SingleOrDefault());
        Assert.IsNotNull(typeof(DictSlot).GetField("JPVariantsRev")
            ?.GetCustomAttributes(typeof(ObsoleteAttribute), false).SingleOrDefault());

        Assert.IsGreaterThan(17, (int)DictSlot.TWVariantsPhrases);
        Assert.IsGreaterThan(17, (int)DictSlot.HKVariantsPhrases);
        Assert.IsGreaterThan(17, (int)DictSlot.JPSCharactersRev);
        Assert.IsGreaterThan(17, (int)DictSlot.HKPhrases);
        Assert.IsGreaterThan(17, (int)DictSlot.HKPhrasesRev);
    }

    [TestMethod]
    public void TestWithCustomDicts_RejectsRetiredNumericSlots()
    {
        foreach (var retiredValue in new[] { 16, 17 })
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                DictionaryLib.WithCustomDicts(
                    DictionaryLib.New(),
                    new[]
                    {
                        new CustomDictSpec
                        {
                            Slot = (DictSlot)retiredValue,
                            Pairs = new Dictionary<string, string>
                            {
                                ["test"] = "value"
                            }
                        }
                    }));

            Assert.Contains("Unknown dictionary slot", ex.Message);
        }
    }

    [TestMethod]
    public void TestProvider_LoadsForwardVariantPhraseSlotsFromZstd()
    {
        var dict = DictionaryLib.Provider;

        AssertMetadataValid(dict.tw_variants_phrases);
        AssertMetadataValid(dict.hk_variants_phrases);
        AssertMetadataValid(dict.hk_phrases);
        AssertMetadataValid(dict.hk_phrases_rev);
        AssertMetadataValid(dict.jps_characters_rev);
    }

    [TestMethod]
    public void TestFromDicts_RequiresForwardVariantPhraseSlots()
    {
        var sourceDir = Path.Combine(AppContext.BaseDirectory, "dicts");

        foreach (var missingFile in new[] { "TWVariantsPhrases.txt", "HKVariantsPhrases.txt", "HKPhrases.txt", "HKPhrasesRev.txt", "JPShinjitaiCharactersRev.txt" })
        {
            var tempDir = Path.Combine(OutputDir, "dicts_missing_" + Path.GetFileNameWithoutExtension(missingFile));

            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);

            Directory.CreateDirectory(tempDir);

            foreach (var sourceFile in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(sourceFile);
                if (fileName == missingFile)
                    continue;

                File.Copy(sourceFile, Path.Combine(tempDir, fileName));
            }

            var ex = Assert.Throws<FileNotFoundException>(() => DictionaryLib.FromDicts(tempDir));
            StringAssert.Contains(ex.Message, missingFile);
        }
    }

    [TestMethod]
    public void TestFromDicts_DoesNotRequireLegacyJpVariantFiles()
    {
        var sourceDir = Path.Combine(AppContext.BaseDirectory, "dicts");
        var tempDir = Path.Combine(OutputDir, "dicts_without_legacy_jp_variants");

        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);

        Directory.CreateDirectory(tempDir);

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDir))
        {
            var fileName = Path.GetFileName(sourceFile);

            if (fileName == "JPVariants.txt" || fileName == "JPVariantsRev.txt")
                continue;

            File.Copy(sourceFile, Path.Combine(tempDir, fileName));
        }

        var dict = DictionaryLib.FromDicts(tempDir);

        AssertMetadataValid(dict.jps_characters_rev);
    }

    [TestMethod]
    public void TestFromJson()
    {
        var dict = DictionaryLib.FromJson("Data/dictionary_maxlength.json");
        Assert.IsNotNull(dict);
        Assert.IsTrue(dict.st_characters.Dict.Count > 0 || dict.ts_characters.Dict.Count > 0);
        AssertMetadataValid(dict.jps_characters_rev);
    }

    [TestMethod]
    public void TestFromCbor()
    {
        var dict = DictionaryLib.FromCbor("Data/dictionary_maxlength.cbor");
        Assert.IsNotNull(dict);
        Assert.IsTrue(dict.st_characters.Dict.Count > 0 || dict.ts_characters.Dict.Count > 0);
        AssertMetadataValid(dict.jps_characters_rev);
    }


    [TestMethod]
    public void TestFromCbor_RebuildsMissingDerivedMetadataForBackwardCompatibility()
    {
        var legacyPath = Path.Combine(OutputDir, "legacy_dict_missing_metadata.cbor");
        var currentBytes = DictionaryLib.ToCborBytes();
        var root = CBORObject.DecodeFromBytes(currentBytes, CBOREncodeOptions.Default);

        foreach (var key in root.Keys)
        {
            var dictObject = root[key];
            dictObject.Remove(CBORObject.FromObject("maxLength"));
            dictObject.Remove(CBORObject.FromObject("minLength"));
            dictObject.Remove(CBORObject.FromObject("lengthMask"));
            dictObject.Remove(CBORObject.FromObject("longLengths"));
            dictObject.Remove(CBORObject.FromObject("starterLenMask"));
        }

        File.WriteAllBytes(legacyPath, root.EncodeToBytes());

        var loaded = DictionaryLib.FromCbor(legacyPath);
        Assert.IsNotNull(loaded);
        Assert.IsGreaterThan(0, loaded.st_characters.MaxLength, "MaxLength should be rebuilt for legacy CBOR.");
        Assert.IsGreaterThan(0, loaded.st_characters.MinLength, "MinLength should be rebuilt for legacy CBOR.");
        Assert.AreNotEqual((ulong)0, loaded.st_characters.LengthMask, "LengthMask should be rebuilt for legacy CBOR.");
        Assert.IsTrue(loaded.st_characters.StarterLenMask is { Count: > 0 },
            "StarterLenMask should be rebuilt for legacy CBOR.");

        Opencc.UseCustomDictionary(loaded);
        try
        {
            var opencc = new Opencc("s2t");
            Assert.AreEqual("漢字", opencc.Convert("汉字"));
        }
        finally
        {
            DictionaryLib.ResetDictionaryProviderToDefault();
        }
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
        Assert.HasCount(dict.jps_characters_rev.Dict.Count, loaded.jps_characters_rev.Dict);
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
            var buffer = new char[4096];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
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

        Assert.IsFalse(
            head.Contains("\\uD8") || head.Contains("\\uDB") || head.Contains("\\uDC") || head.Contains("\\uDD"),
            @"Unescaped JSON should not contain surrogate \uD8xx/\uDBxx/\uDCxx/\uDDxx sequences.");
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
        Assert.IsTrue(json.RootElement.TryGetProperty("tw_variants_phrases", out _));
        Assert.IsTrue(json.RootElement.TryGetProperty("hk_phrases", out _));
        Assert.IsTrue(json.RootElement.TryGetProperty("hk_phrases_rev", out _));
        Assert.IsTrue(json.RootElement.TryGetProperty("hk_variants_phrases", out _));
        Assert.IsTrue(json.RootElement.TryGetProperty("jps_characters_rev", out _));

        var loaded = DictionaryLib.DeserializedFromJson(jsonPath);
        Assert.AreEqual(
            DictionaryLib.FromDicts().hk_phrases.Count,
            loaded.hk_phrases.Count);
        Assert.AreEqual(
            DictionaryLib.FromDicts().hk_phrases_rev.Count,
            loaded.hk_phrases_rev.Count);
        Assert.AreEqual(
            DictionaryLib.FromDicts().jps_characters_rev.Count,
            loaded.jps_characters_rev.Count);
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

    // File level Custom dictionary test

    [TestMethod]
    public void TestFromDicts_AppendsCustomStPhrase()
    {
        var customPath = Path.Combine(OutputDir, "custom_st_phrases.txt");
        File.WriteAllText(
            customPath,
            "# Custom company terms\n帕兰蒂尔\t帕蘭蒂爾\n",
            Encoding.UTF8);

        var dict = DictionaryLib.FromDicts(
            appends: new Dictionary<DictSlot, string>
            {
                [DictSlot.STPhrases] = customPath
            });

        Assert.AreEqual("帕蘭蒂爾", dict.st_phrases.Dict["帕兰蒂尔"]);
        Assert.IsGreaterThanOrEqualTo("帕兰蒂尔".Length, dict.st_phrases.MaxLength);
        Assert.IsTrue(dict.st_phrases.StarterLenMask.ContainsKey("帕"));
        AssertMetadataValid(dict.st_phrases);
    }

    [TestMethod]
    public void TestFromDicts_AppendsCustomTwVariantPhrase()
    {
        var customPath = Path.Combine(OutputDir, "custom_tw_variant_phrases.txt");
        File.WriteAllText(
            customPath,
            "測試片語\t測試片語\n",
            Encoding.UTF8);

        var dict = DictionaryLib.FromDicts(
            appends: new Dictionary<DictSlot, string>
            {
                [DictSlot.TWVariantsPhrases] = customPath
            });

        Assert.AreEqual("測試片語", dict.tw_variants_phrases.Dict["測試片語"]);
        Assert.IsTrue(dict.tw_variants_phrases.StarterLenMask.ContainsKey("測"));
        AssertMetadataValid(dict.tw_variants_phrases);
    }

    [TestMethod]
    public void TestFromDicts_AppendsCustomHkPhrase()
    {
        var customPath = Path.Combine(OutputDir, "custom_hk_phrases.txt");
        File.WriteAllText(
            customPath,
            "小女孩\t妹丁\n",
            Encoding.UTF8);

        var dict = DictionaryLib.FromDicts(
            appends: new Dictionary<DictSlot, string>
            {
                [DictSlot.HKPhrases] = customPath
            });

        Assert.AreEqual("妹丁", dict.hk_phrases.Dict["小女孩"]);
        Assert.IsTrue(dict.hk_phrases.StarterLenMask.ContainsKey("小"));
        AssertMetadataValid(dict.hk_phrases);
    }

    [TestMethod]
    public void TestFromDicts_AppendsCustomJPSCharactersRev()
    {
        var customPath = Path.Combine(OutputDir, "custom_jps_characters_rev.txt");
        File.WriteAllText(
            customPath,
            "測試舊字\t測試新字\n",
            Encoding.UTF8);

        var dict = DictionaryLib.FromDicts(
            appends: new Dictionary<DictSlot, string>
            {
                [DictSlot.JPSCharactersRev] = customPath
            });

        Assert.AreEqual("測試新字", dict.jps_characters_rev.Dict["測試舊字"]);
        Assert.IsTrue(dict.jps_characters_rev.StarterLenMask.ContainsKey("測"));
        AssertMetadataValid(dict.jps_characters_rev);
    }

    [TestMethod]
    public void TestFromDicts_AppendsCustomStPhraseDuplicateKeyCustomValueWins()
    {
        var customPath = Path.Combine(OutputDir, "custom_st_phrases_duplicate.txt");
        File.WriteAllText(
            customPath,
            "# Duplicate built-in key should use custom value\nSQL注入\t客製SQL注入\n",
            Encoding.UTF8);

        var dict = DictionaryLib.FromDicts(
            appends: new Dictionary<DictSlot, string>
            {
                [DictSlot.STPhrases] = customPath
            });

        Assert.AreEqual("客製SQL注入", dict.st_phrases.Dict["SQL注入"]);
        AssertMetadataValid(dict.st_phrases);
    }

    [TestMethod]
    public void TestFromDicts_OverridesStPhrasesReplacesWholeSlot()
    {
        var customPath = Path.Combine(OutputDir, "override_st_phrases.txt");
        File.WriteAllText(
            customPath,
            "帕兰蒂尔\t帕蘭蒂爾\n",
            Encoding.UTF8);

        var dict = DictionaryLib.FromDicts(
            overrides: new Dictionary<DictSlot, string>
            {
                [DictSlot.STPhrases] = customPath
            });

        Assert.AreEqual(1, dict.st_phrases.Count);
        Assert.AreEqual("帕蘭蒂爾", dict.st_phrases.Dict["帕兰蒂尔"]);
        Assert.IsFalse(dict.st_phrases.Dict.ContainsKey("SQL注入"));
        AssertMetadataValid(dict.st_phrases);
    }

    [TestMethod]
    public void TestFromDicts_OverridesHkPhrasesReplacesWholeSlot()
    {
        var customPath = Path.Combine(OutputDir, "override_hk_phrases.txt");
        File.WriteAllText(
            customPath,
            "小女孩\t妹丁\n",
            Encoding.UTF8);

        var dict = DictionaryLib.FromDicts(
            overrides: new Dictionary<DictSlot, string>
            {
                [DictSlot.HKPhrases] = customPath
            });

        Assert.AreEqual(1, dict.hk_phrases.Count);
        Assert.AreEqual("妹丁", dict.hk_phrases.Dict["小女孩"]);
        AssertMetadataValid(dict.hk_phrases);
    }

    [TestMethod]
    public void TestFromDicts_AppendedCustomHkPhraseWorksInS2HkpConversion()
    {
        var customPath = Path.Combine(OutputDir, "custom_hk_phrases_convert.txt");
        File.WriteAllText(
            customPath,
            "小女孩\t妹丁\n",
            Encoding.UTF8);

        var dict = DictionaryLib.FromDicts(
            appends: new Dictionary<DictSlot, string>
            {
                [DictSlot.HKPhrases] = customPath
            });

        Opencc.UseCustomDictionary(dict);

        try
        {
            var opencc = new Opencc("s2hkp");
            Assert.AreEqual(
                "妹丁侵犯個人私隱權",
                opencc.Convert("小女孩侵犯个人隐私权"));
        }
        finally
        {
            DictionaryLib.ResetDictionaryProviderToDefault();
        }
    }

    [TestMethod]
    public void TestFromDicts_AppendedCustomDictWorksInConversion()
    {
        var customPath = Path.Combine(OutputDir, "custom_st_phrases_convert.txt");
        File.WriteAllText(
            customPath,
            "# Custom company terms\n帕兰蒂尔\t帕蘭蒂爾\n",
            Encoding.UTF8);

        var dict = DictionaryLib.FromDicts(
            appends: new Dictionary<DictSlot, string>
            {
                [DictSlot.STPhrases] = customPath
            });

        Opencc.UseCustomDictionary(dict);

        try
        {
            var opencc = new Opencc("s2t");
            Assert.AreEqual(
                "帕蘭蒂爾是一家公司",
                opencc.Convert("帕兰蒂尔是一家公司"));
        }
        finally
        {
            DictionaryLib.ResetDictionaryProviderToDefault();
        }
    }

    [TestMethod]
    public void TestFromDicts_RejectsInvalidCustomSlot()
    {
        var customPath = Path.Combine(OutputDir, "custom_user_dict.txt");

        File.WriteAllText(
            customPath,
            "帕兰蒂尔\t帕蘭蒂爾\n",
            Encoding.UTF8);

        var ex = Assert.Throws<ArgumentException>(() =>
            DictionaryLib.FromDicts(
                appends: new Dictionary<DictSlot, string>
                {
                    [(DictSlot)9999] = customPath
                }));

        Assert.Contains("Unknown dictionary slot", ex.Message);
    }

    // Post-load custom dictionary test

    [TestMethod]
    public void TestWithCustomDicts_AppendsCustomStPhraseFromPairs()
    {
        var dict = DictionaryLib.New();

        DictionaryLib.WithCustomDicts(
            dict,
            [
                new CustomDictSpec
                {
                    Slot = DictSlot.STPhrases,
                    Mode = CustomDictMode.Append,
                    Pairs = new Dictionary<string, string>
                    {
                        ["帕兰蒂尔"] = "帕蘭蒂爾"
                    }
                }
            ]);

        Assert.AreEqual("帕蘭蒂爾", dict.st_phrases.Dict["帕兰蒂尔"]);
        Assert.IsTrue(dict.st_phrases.StarterLenMask.ContainsKey("帕"));
        AssertMetadataValid(dict.st_phrases);
    }

    [TestMethod]
    public void TestWithCustomDicts_AppendsCustomHkPhraseFromPairs()
    {
        var dict = DictionaryLib.New();

        DictionaryLib.WithCustomDicts(
            dict,
            [
                new CustomDictSpec
                {
                    Slot = DictSlot.HKPhrases,
                    Mode = CustomDictMode.Append,
                    Pairs = new Dictionary<string, string>
                    {
                        ["小女孩"] = "妹丁"
                    }
                }
            ]);

        Assert.AreEqual("妹丁", dict.hk_phrases.Dict["小女孩"]);
        Assert.IsTrue(dict.hk_phrases.StarterLenMask.ContainsKey("小"));
        AssertMetadataValid(dict.hk_phrases);
    }

    [TestMethod]
    public void TestWithCustomDicts_OverridesCustomHkPhraseFromPairs()
    {
        var dict = DictionaryLib.New();

        DictionaryLib.WithCustomDicts(
            dict,
            [
                new CustomDictSpec
                {
                    Slot = DictSlot.HKPhrases,
                    Mode = CustomDictMode.Override,
                    Pairs = new Dictionary<string, string>
                    {
                        ["小女孩"] = "妹丁"
                    }
                }
            ]);

        Assert.AreEqual(1, dict.hk_phrases.Count);
        Assert.AreEqual("妹丁", dict.hk_phrases.Dict["小女孩"]);
        AssertMetadataValid(dict.hk_phrases);
    }

    [TestMethod]
    public void TestWithCustomDicts_AppendsCustomHkPhraseRevFromPairs()
    {
        var dict = DictionaryLib.New();

        DictionaryLib.WithCustomDicts(
            dict,
            [
                new CustomDictSpec
                {
                    Slot = DictSlot.HKPhrasesRev,
                    Mode = CustomDictMode.Append,
                    Pairs = new Dictionary<string, string>
                    {
                        ["妹丁"] = "小女孩"
                    }
                }
            ]);

        Assert.AreEqual("小女孩", dict.hk_phrases_rev.Dict["妹丁"]);
        Assert.IsTrue(dict.hk_phrases_rev.StarterLenMask.ContainsKey("妹"));
        AssertMetadataValid(dict.hk_phrases_rev);
    }

    [TestMethod]
    public void TestWithCustomDicts_OverridesJPSCharactersRevFromPairs()
    {
        var dict = DictionaryLib.New();

        DictionaryLib.WithCustomDicts(
            dict,
            [
                new CustomDictSpec
                {
                    Slot = DictSlot.JPSCharactersRev,
                    Mode = CustomDictMode.Override,
                    Pairs = new Dictionary<string, string>
                    {
                        ["測試舊字"] = "測試新字"
                    }
                }
            ]);

        Assert.AreEqual(1, dict.jps_characters_rev.Count);
        Assert.AreEqual("測試新字", dict.jps_characters_rev.Dict["測試舊字"]);
        AssertMetadataValid(dict.jps_characters_rev);
    }

    [TestMethod]
    public void TestWithCustomDicts_AppendsCustomStPhraseFromFile()
    {
        var customPath = Path.Combine(OutputDir, "post_load_custom_st_phrases.txt");
        File.WriteAllText(
            customPath,
            "# Custom company terms\n帕兰蒂尔\t帕蘭蒂爾\n",
            Encoding.UTF8);

        var dict = DictionaryLib.New();

        DictionaryLib.WithCustomDicts(
            dict,
            new[]
            {
                new CustomDictSpec
                {
                    Slot = DictSlot.STPhrases,
                    Mode = CustomDictMode.Append,
                    Paths = new[] { customPath }
                }
            });

        Assert.AreEqual("帕蘭蒂爾", dict.st_phrases.Dict["帕兰蒂尔"]);
        AssertMetadataValid(dict.st_phrases);
    }

    [TestMethod]
    public void TestWithCustomDicts_AppendsPathsThenPairsPairsWin()
    {
        var customPath = Path.Combine(OutputDir, "post_load_paths_then_pairs.txt");
        File.WriteAllText(
            customPath,
            "帕兰蒂尔\t檔案值\n",
            Encoding.UTF8);

        var dict = DictionaryLib.New();

        DictionaryLib.WithCustomDicts(
            dict,
            new[]
            {
                new CustomDictSpec
                {
                    Slot = DictSlot.STPhrases,
                    Mode = CustomDictMode.Append,
                    Paths = new[] { customPath },
                    Pairs = new Dictionary<string, string>
                    {
                        ["帕兰蒂尔"] = "記憶體值"
                    }
                }
            });

        Assert.AreEqual("記憶體值", dict.st_phrases.Dict["帕兰蒂尔"]);
        AssertMetadataValid(dict.st_phrases);
    }

    [TestMethod]
    public void TestWithCustomDicts_OverrideReplacesWholeSlot()
    {
        var dict = DictionaryLib.New();

        DictionaryLib.WithCustomDicts(
            dict,
            new[]
            {
                new CustomDictSpec
                {
                    Slot = DictSlot.STPhrases,
                    Mode = CustomDictMode.Override,
                    Pairs = new Dictionary<string, string>
                    {
                        ["帕兰蒂尔"] = "帕蘭蒂爾"
                    }
                }
            });

        Assert.AreEqual(1, dict.st_phrases.Count);
        Assert.AreEqual("帕蘭蒂爾", dict.st_phrases.Dict["帕兰蒂尔"]);
        Assert.IsFalse(dict.st_phrases.Dict.ContainsKey("SQL注入"));
        AssertMetadataValid(dict.st_phrases);
    }

    [TestMethod]
    public void TestWithCustomDicts_OverrideReplacesHkVariantPhraseSlot()
    {
        var dict = DictionaryLib.FromDicts();

        DictionaryLib.WithCustomDicts(
            dict,
            new[]
            {
                new CustomDictSpec
                {
                    Slot = DictSlot.HKVariantsPhrases,
                    Mode = CustomDictMode.Override,
                    Pairs = new Dictionary<string, string>
                    {
                        ["無線新聞"] = "無綫新聞"
                    }
                }
            });

        Assert.AreEqual(1, dict.hk_variants_phrases.Count);
        Assert.AreEqual("無綫新聞", dict.hk_variants_phrases.Dict["無線新聞"]);
        AssertMetadataValid(dict.hk_variants_phrases);
    }

    [TestMethod]
    public void TestTwVariantPhraseAppliesBeforeCharacterVariant()
    {
        var dict = DictionaryLib.FromDicts();
        Opencc.UseCustomDictionary(dict);

        try
        {
            var opencc = new Opencc("t2tw");
            Assert.AreEqual("喫茶小舖", opencc.Convert("喫茶小舖"));
        }
        finally
        {
            DictionaryLib.ResetDictionaryProviderToDefault();
        }
    }

    [TestMethod]
    public void TestHkVariantPhraseAppliesBeforeCharacterVariant()
    {
        var dict = DictionaryLib.FromDicts();
        Opencc.UseCustomDictionary(dict);

        try
        {
            var opencc = new Opencc("t2hk");
            Assert.AreEqual("喫茶小舖", opencc.Convert("喫茶小舖"));
        }
        finally
        {
            DictionaryLib.ResetDictionaryProviderToDefault();
        }
    }

    [TestMethod]
    public void TestT2JpUsesJPSCharactersRevOnly()
    {
        var dict = DictionaryLib.FromDicts();

        DictionaryLib.WithCustomDicts(
            dict,
            [
                new CustomDictSpec
                {
                    Slot = DictSlot.JPSCharacters,
                    Mode = CustomDictMode.Override,
                    Pairs = new Dictionary<string, string> { ["惡"] = "不應使用" }
                },
                new CustomDictSpec
                {
                    Slot = DictSlot.JPSPhrases,
                    Mode = CustomDictMode.Override,
                    Pairs = new Dictionary<string, string> { ["惡德"] = "不應使用" }
                },
                new CustomDictSpec
                {
                    Slot = DictSlot.JPSCharactersRev,
                    Mode = CustomDictMode.Override,
                    Pairs = new Dictionary<string, string> { ["惡"] = "悪" }
                }
            ]);

        Opencc.UseCustomDictionary(dict);

        try
        {
            var opencc = new Opencc("t2jp");
            Assert.AreEqual("悪德", opencc.Convert("惡德"));
        }
        finally
        {
            DictionaryLib.ResetDictionaryProviderToDefault();
        }
    }

    [TestMethod]
    public void TestJp2TUsesJpsPhrasesAndCharactersOnly()
    {
        var dict = DictionaryLib.FromDicts();

        DictionaryLib.WithCustomDicts(
            dict,
            [
                new CustomDictSpec
                {
                    Slot = DictSlot.JPSCharactersRev,
                    Mode = CustomDictMode.Override,
                    Pairs = new Dictionary<string, string> { ["惡"] = "不應使用" }
                }
            ]);

        Opencc.UseCustomDictionary(dict);

        try
        {
            var opencc = new Opencc("jp2t");
            Assert.AreEqual("辨當と惡", opencc.Convert("弁当と悪"));
        }
        finally
        {
            DictionaryLib.ResetDictionaryProviderToDefault();
        }
    }

    [TestMethod]
    public void TestWithCustomDicts_AppendedCustomDictWorksInConversion()
    {
        var dict = DictionaryLib.New();

        DictionaryLib.WithCustomDicts(
            dict,
            new[]
            {
                new CustomDictSpec
                {
                    Slot = DictSlot.STPhrases,
                    Mode = CustomDictMode.Append,
                    Pairs = new Dictionary<string, string>
                    {
                        ["帕兰蒂尔"] = "帕蘭蒂爾"
                    }
                }
            });

        Opencc.UseCustomDictionary(dict);

        try
        {
            var opencc = new Opencc("s2t");
            Assert.AreEqual(
                "帕蘭蒂爾是一家公司",
                opencc.Convert("帕兰蒂尔是一家公司"));
        }
        finally
        {
            DictionaryLib.ResetDictionaryProviderToDefault();
        }
    }

    [TestMethod]
    public void TestWithCustomDicts_RejectsEmptySpec()
    {
        var dict = DictionaryLib.New();

        var ex = Assert.Throws<ArgumentException>(() =>
            DictionaryLib.WithCustomDicts(
                dict,
                new[]
                {
                    new CustomDictSpec
                    {
                        Slot = DictSlot.STPhrases
                    }
                }));

        Assert.Contains("must provide at least one dictionary source", ex.Message);
    }

    [TestMethod]
    public void TestWithCustomDicts_RejectsInvalidCustomSlot()
    {
        var dict = DictionaryLib.New();

        var ex = Assert.Throws<ArgumentException>(() =>
            DictionaryLib.WithCustomDicts(
                dict,
                new[]
                {
                    new CustomDictSpec
                    {
                        Slot = (DictSlot)9999,
                        Pairs = new Dictionary<string, string>
                        {
                            ["帕兰蒂尔"] = "帕蘭蒂爾"
                        }
                    }
                }));

        Assert.Contains("Unknown dictionary slot", ex.Message);
    }
}
