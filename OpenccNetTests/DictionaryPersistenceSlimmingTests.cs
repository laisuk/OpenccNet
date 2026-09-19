using System.Formats.Cbor;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenccNetLib;
using ZstdSharp;

namespace OpenccNetTests;

[TestClass]
public class DictionaryPersistenceSlimmingTests
{
    [TestMethod]
    [DataRow("汉", 1UL, true)]
    [DataRow("𠀀", 2UL, true)]
    [DataRow("汉|𠀀", 3UL, true)]
    [DataRow("汉|汉字词", 5UL, false)]
    [DataRow("汉字", 2UL, true)] // UTF-16 metadata also covers two-BMP phrases.
    [DataRow("汉|LONG", 1UL, false)]
    public void AllWriters_SelectStorageWithoutMutatingRuntime_AndRestoreOnLoad(
        string keys, ulong mask, bool omitted)
    {
        var slot = CreateSlot(keys.Replace("LONG", new string('长', 65)).Split('|'));
        Assert.AreEqual(mask, slot.LengthMask);
        var source = new DictionaryMaxlength
        {
            st_characters = slot,
            jps_characters_rev = CreateSlot(new[] { "舊" })
        };
        var originalMap = slot.StarterLenMask;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var format in new[] { "json", "unescaped", "zstd", "cbor" })
            {
                DictionaryMaxlength loaded;
                if (format == "cbor")
                {
                    var bytes = DictionaryLib.ToCborBytes(source);
                    var reader = new CborReader(bytes);
                    reader.ReadStartMap();
                    var found = false;
                    while (reader.PeekState() != CborReaderState.EndMap)
                    {
                        if (reader.ReadTextString() != "st_characters")
                        {
                            reader.SkipValue();
                            continue;
                        }

                        Assert.AreEqual(omitted ? 5 : 6, reader.ReadStartMap());
                        while (reader.PeekState() != CborReaderState.EndMap)
                        {
                            if (reader.ReadTextString() == "starterLenMask") found = true;
                            reader.SkipValue();
                        }

                        reader.ReadEndMap();
                    }

                    reader.ReadEndMap();
                    Assert.AreEqual(0, reader.BytesRemaining);
                    Assert.AreEqual(!omitted, found);
                    File.WriteAllBytes(path, bytes);
                    loaded = DictionaryLib.FromCbor(path);
                }
                else
                {
                    string json;
                    if (format == "zstd")
                    {
                        DictionaryLib.SaveJsonCompressed(path, source);
                        using var decompressor = new Decompressor();
                        json = System.Text.Encoding.UTF8.GetString(decompressor.Unwrap(File.ReadAllBytes(path))
                            .ToArray());
                        loaded = DictionaryLib.FromZstd(path);
                    }
                    else
                    {
                        if (format == "json") DictionaryLib.SerializeToJson(path, source);
                        else DictionaryLib.SerializeToJsonUnescaped(path, source);
                        json = File.ReadAllText(path);
                        loaded = DictionaryLib.FromJson(path);
                    }

                    using var document = JsonDocument.Parse(json);
                    Assert.AreEqual(!omitted, document.RootElement.GetProperty("st_characters")
                        .TryGetProperty("StarterLenMask", out _));
                }

                Assert.AreSame(originalMap, slot.StarterLenMask);
                Assert.AreSequenceEqual(originalMap, loaded.st_characters.StarterLenMask, SequenceOrder.InAnyOrder);
                Assert.AreEqual(slot.LengthMask, loaded.st_characters.LengthMask);
                if (slot.Dict.ContainsKey("𠀀")) Assert.AreEqual(2UL, loaded.st_characters.StarterLenMask["𠀀"]);
                if (slot.Dict.ContainsKey("汉字")) Assert.AreEqual(2UL, loaded.st_characters.StarterLenMask["汉"]);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow("absent")]
    [DataRow("null")]
    [DataRow("empty")]
    [DataRow("supplied")]
    public void LegacyMetadata_PreservesSuppliedMap_AndExistingRepairSemantics(string state)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            // Deliberately non-derived supplied value proves the reader does not replace it.
            var root = JsonNode.Parse(
                    "{\"st_characters\":{\"Dict\":{\"𠀀\":\"汉\"},\"MaxLength\":2,\"MinLength\":2,\"LengthMask\":2},\"jps_characters_rev\":{\"Dict\":{\"舊\":\"旧\"}}}")
                !;
            if (state != "absent")
                root["st_characters"]!["StarterLenMask"] = state switch
                {
                    "null" => null, "empty" => new JsonObject(),
                    _ => new JsonObject { ["𠀀"] = 17UL }
                };
            var json = root.ToJsonString();
            File.WriteAllText(path, json);
            Assert.AreEqual(state == "supplied" ? 17UL : 2UL,
                DictionaryLib.FromJson(path).st_characters.StarterLenMask["𠀀"]);
            using var compressor = new Compressor(1);
            File.WriteAllBytes(path, compressor.Wrap(System.Text.Encoding.UTF8.GetBytes(json)).ToArray());
            Assert.AreEqual(state == "supplied" ? 17UL : 2UL,
                DictionaryLib.FromZstd(path).st_characters.StarterLenMask["𠀀"]);

            var writer = new CborWriter();
            writer.WriteStartMap(2);
            writer.WriteTextString("st_characters");
            writer.WriteStartMap(state == "absent" ? 4 : 5);
            writer.WriteTextString("dict");
            writer.WriteStartMap(1);
            writer.WriteTextString("𠀀");
            writer.WriteTextString("汉");
            writer.WriteEndMap();
            writer.WriteTextString("maxLength");
            writer.WriteInt32(2);
            writer.WriteTextString("minLength");
            writer.WriteInt32(2);
            writer.WriteTextString("lengthMask");
            writer.WriteUInt64(2);
            if (state != "absent")
            {
                writer.WriteTextString("starterLenMask");
                if (state == "null") writer.WriteNull();
                else
                {
                    writer.WriteStartMap(state == "empty" ? 0 : 1);
                    if (state == "supplied")
                    {
                        writer.WriteTextString("𠀀");
                        writer.WriteUInt64(17);
                    }

                    writer.WriteEndMap();
                }
            }

            writer.WriteEndMap();
            writer.WriteTextString("jps_characters_rev");
            writer.WriteStartMap(1);
            writer.WriteTextString("dict");
            writer.WriteStartMap(1);
            writer.WriteTextString("舊");
            writer.WriteTextString("旧");
            writer.WriteEndMap();
            writer.WriteEndMap();
            writer.WriteEndMap();
            File.WriteAllBytes(path, writer.Encode());
            Assert.AreEqual(state == "supplied" ? 17UL : 2UL,
                DictionaryLib.FromCbor(path).st_characters.StarterLenMask["𠀀"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static DictWithMaxLength CreateSlot(string[] keys)
    {
        var slot = new DictWithMaxLength
        {
            MinLength = keys.Min(k => k.Length), MaxLength = keys.Max(k => k.Length),
            StarterLenMask = new Dictionary<string, ulong>(StringComparer.Ordinal)
        };
        foreach (var key in keys)
        {
            slot.Dict[key] = key;
            var starter = key.Substring(0, char.IsSurrogatePair(key, 0) ? 2 : 1);
            slot.StarterLenMask.TryGetValue(starter, out var bits);
            if (key.Length <= 64)
            {
                slot.LengthMask |= 1UL << (key.Length - 1);
                bits |= 1UL << (key.Length - 1);
            }
            else (slot.LongLengths ??= new HashSet<int>()).Add(key.Length);

            slot.StarterLenMask[starter] = bits;
        }

        return slot;
    }
}