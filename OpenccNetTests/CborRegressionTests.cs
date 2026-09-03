using System.Formats.Cbor;
using System.Security.Cryptography;
using OpenccNetLib;

namespace OpenccNetTests;

[TestClass]
public class CborRegressionTests
{
    private const string PeterOCborLegacyFixtureSha256 =
        "26A980D2F343E9918CBD0530A5D024740174E6E70A90B4B8882335C3E883AE95";

    private static readonly string[] CamelCaseSlotFields =
    {
        "dict",
        "maxLength",
        "minLength",
        "lengthMask",
        "longLengths",
        "starterLenMask"
    };

    [TestMethod]
    public void PeterOLegacyFixture_LoadsAsGoldenBackwardCompatibilityFile()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "legacy",
            "dictionary_maxlength_petero_legacy.cbor");
        var bytes = File.ReadAllBytes(path);

        Assert.AreEqual(
            PeterOCborLegacyFixtureSha256,
            Convert.ToHexString(SHA256.HashData(bytes)),
            "The checked-in PeterO CBOR compatibility fixture must remain byte-for-byte unchanged.");

        var loaded = DictionaryLib.FromCbor(path);

        Assert.AreEqual("漢", loaded.st_characters.Dict["汉"]);
        Assert.IsGreaterThan(0, loaded.jps_characters_rev.Dict.Count);
        Assert.AreEqual("漢字", new Opencc(OpenccConfig.S2T, loaded).Convert("汉字"));
    }

    [TestMethod]
    public void NewFormat_RoundTripsDictionaryAndPersistedMetadata()
    {
        var source = CreateSmallDictionary();
        var loaded = LoadFromBytes(DictionaryLib.ToCborBytes(source));

        Assert.AreSequenceEqual(source.st_characters.Dict, loaded.st_characters.Dict, SequenceOrder.InAnyOrder);
        Assert.AreEqual(source.st_characters.MaxLength, loaded.st_characters.MaxLength);
        Assert.AreEqual(source.st_characters.MinLength, loaded.st_characters.MinLength);
        Assert.AreEqual(source.st_characters.LengthMask, loaded.st_characters.LengthMask);
        Assert.AreSequenceEqual(
            source.st_characters.LongLengths!.ToArray(), loaded.st_characters.LongLengths!.ToArray(), SequenceOrder.InAnyOrder);
        Assert.AreSequenceEqual(
            source.st_characters.StarterLenMask, loaded.st_characters.StarterLenMask, SequenceOrder.InAnyOrder);
        Assert.AreEqual("漢字", new Opencc(OpenccConfig.S2T, loaded).Convert("汉字"));
    }

    [TestMethod]
    public void SaveCbor_WritesSamePayloadAsToCborBytes()
    {
        var source = CreateSmallDictionary();
        var expected = DictionaryLib.ToCborBytes(source);
        var path = Path.Combine(
            Path.GetTempPath(),
            $"OpenccNetTests_{Guid.NewGuid():N}.cbor");

        try
        {
            DictionaryLib.SaveCbor(path, source);
            Assert.AreSequenceEqual(expected, File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void NewFormat_EmitsLegacyCamelCaseSlotFieldNames()
    {
        var reader = new CborReader(
            DictionaryLib.ToCborBytes(CreateSmallDictionary()),
            CborConformanceMode.Lax);

        _ = reader.ReadStartMap();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var slotName = reader.ReadTextString();
            if (slotName != "st_characters")
            {
                reader.SkipValue();
                continue;
            }

            var actualFields = new List<string>();
            _ = reader.ReadStartMap();
            while (reader.PeekState() != CborReaderState.EndMap)
            {
                actualFields.Add(reader.ReadTextString());
                reader.SkipValue();
            }

            reader.ReadEndMap();
            Assert.AreSequenceEqual(CamelCaseSlotFields, actualFields);
            return;
        }

        Assert.Fail("Serialized CBOR did not contain the st_characters slot.");
    }

    [TestMethod]
    public void Reader_AcceptsPascalCaseSlotFieldNames()
    {
        var loaded = LoadFromBytes(BuildSmallPayload(FieldNameCasing.PascalCase));

        Assert.AreEqual("漢字", loaded.st_characters.Dict["汉字"]);
        Assert.AreEqual(2, loaded.st_characters.MaxLength);
        Assert.AreEqual(2, loaded.st_characters.MinLength);
        Assert.AreEqual(2UL, loaded.st_characters.LengthMask);
        Assert.AreEqual(2UL, loaded.st_characters.StarterLenMask!["汉"]);
    }

    [TestMethod]
    public void Reader_RebuildsMissingDerivedMetadata()
    {
        var loaded = LoadFromBytes(BuildSmallPayload(
            FieldNameCasing.CamelCase,
            includeMetadata: false));

        Assert.AreEqual(2, loaded.st_characters.MaxLength);
        Assert.AreEqual(2, loaded.st_characters.MinLength);
        Assert.AreEqual(2UL, loaded.st_characters.LengthMask);
        Assert.IsNull(loaded.st_characters.LongLengths);
        Assert.AreEqual(2UL, loaded.st_characters.StarterLenMask!["汉"]);
        Assert.AreEqual("漢字", new Opencc(OpenccConfig.S2T, loaded).Convert("汉字"));
    }

    [TestMethod]
    public void Reader_IgnoresUnknownTopLevelAndSlotFields()
    {
        var loaded = LoadFromBytes(BuildSmallPayload(
            FieldNameCasing.CamelCase,
            includeUnknownFields: true));

        Assert.AreEqual("漢字", loaded.st_characters.Dict["汉字"]);
        Assert.AreEqual("漢字", new Opencc(OpenccConfig.S2T, loaded).Convert("汉字"));
    }

    [TestMethod]
    public void Reader_RejectsMissingRequiredSlot()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            LoadFromBytes(BuildSmallPayload(
                FieldNameCasing.CamelCase,
                includeRequiredSlot: false)));

        Assert.Contains("jps_characters_rev", exception.Message);
    }

    private static DictionaryMaxlength CreateSmallDictionary()
    {
        var longKey = new string('汉', 65);

        return new DictionaryMaxlength
        {
            st_characters = new DictWithMaxLength
            {
                Dict = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["汉"] = "漢",
                    ["汉字"] = "漢字",
                    [longKey] = new string('漢', 65)
                },
                MaxLength = 65,
                MinLength = 1,
                LengthMask = 3UL,
                LongLengths = new HashSet<int> { 65 },
                StarterLenMask = new Dictionary<string, ulong>(StringComparer.Ordinal)
                {
                    ["汉"] = 3UL
                }
            },
            jps_characters_rev = CreateRequiredSlot()
        };
    }

    private static byte[] BuildSmallPayload(
        FieldNameCasing casing,
        bool includeMetadata = true,
        bool includeUnknownFields = false,
        bool includeRequiredSlot = true)
    {
        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteStartMap(null);

        writer.WriteTextString("st_characters");
        WriteSmallSlot(writer, casing, includeMetadata, includeUnknownFields);

        if (includeRequiredSlot)
        {
            writer.WriteTextString("jps_characters_rev");
            WriteRequiredSlot(writer, casing);
        }

        if (includeUnknownFields)
        {
            writer.WriteTextString("future_slot");
            writer.WriteStartMap(1);
            writer.WriteTextString("nested");
            writer.WriteStartArray(2);
            writer.WriteInt32(1);
            writer.WriteTextString("ignored");
            writer.WriteEndArray();
            writer.WriteEndMap();
        }

        writer.WriteEndMap();
        return writer.Encode();
    }

    private static void WriteSmallSlot(
        CborWriter writer,
        FieldNameCasing casing,
        bool includeMetadata,
        bool includeUnknownField)
    {
        writer.WriteStartMap(null);
        writer.WriteTextString(Field(casing, "dict", "Dict"));
        writer.WriteStartMap(1);
        writer.WriteTextString("汉字");
        writer.WriteTextString("漢字");
        writer.WriteEndMap();

        if (includeMetadata)
        {
            writer.WriteTextString(Field(casing, "maxLength", "MaxLength"));
            writer.WriteInt32(2);
            writer.WriteTextString(Field(casing, "minLength", "MinLength"));
            writer.WriteInt32(2);
            writer.WriteTextString(Field(casing, "lengthMask", "LengthMask"));
            writer.WriteUInt64(2UL);
            writer.WriteTextString(Field(casing, "longLengths", "LongLengths"));
            writer.WriteNull();
            writer.WriteTextString(Field(casing, "starterLenMask", "StarterLenMask"));
            writer.WriteStartMap(1);
            writer.WriteTextString("汉");
            writer.WriteUInt64(2UL);
            writer.WriteEndMap();
        }

        if (includeUnknownField)
        {
            writer.WriteTextString("futureMetadata");
            writer.WriteStartArray(1);
            writer.WriteBoolean(true);
            writer.WriteEndArray();
        }

        writer.WriteEndMap();
    }

    private static void WriteRequiredSlot(CborWriter writer, FieldNameCasing casing)
    {
        writer.WriteStartMap(1);
        writer.WriteTextString(Field(casing, "dict", "Dict"));
        writer.WriteStartMap(1);
        writer.WriteTextString("舊");
        writer.WriteTextString("旧");
        writer.WriteEndMap();
        writer.WriteEndMap();
    }

    private static DictWithMaxLength CreateRequiredSlot()
    {
        return new DictWithMaxLength
        {
            Dict = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["舊"] = "旧"
            },
            MaxLength = 1,
            MinLength = 1,
            LengthMask = 1UL,
            StarterLenMask = new Dictionary<string, ulong>(StringComparer.Ordinal)
            {
                ["舊"] = 1UL
            }
        };
    }

    private static string Field(
        FieldNameCasing casing,
        string camelCase,
        string pascalCase)
    {
        return casing == FieldNameCasing.CamelCase ? camelCase : pascalCase;
    }

    private static DictionaryMaxlength LoadFromBytes(byte[] bytes)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"OpenccNetTests_{Guid.NewGuid():N}.cbor");

        try
        {
            File.WriteAllBytes(path, bytes);
            return DictionaryLib.FromCbor(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private enum FieldNameCasing
    {
        CamelCase,
        PascalCase
    }
}
