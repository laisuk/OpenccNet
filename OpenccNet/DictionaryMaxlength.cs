using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using PeterO.Cbor;
using ZstdNet;

namespace OpenccNet
{
    public class DictWithMaxLength
    {
        public ConcurrentDictionary<string, string> Data { get; set; } = new ConcurrentDictionary<string, string>();
        public int MaxLength { get; set; }
    }

    public class DictionaryMaxlength
    {
        public DictWithMaxLength st_characters { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength st_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength ts_characters { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength ts_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength tw_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength tw_phrases_rev { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength tw_variants { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength tw_variants_rev { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength tw_variants_rev_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength hk_variants { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength hk_variants_rev { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength hk_variants_rev_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength jps_characters { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength jps_phrases { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength jp_variants { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength jp_variants_rev { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength st_punctuations { get; set; } = new DictWithMaxLength();
        public DictWithMaxLength ts_punctuations { get; set; } = new DictWithMaxLength();

        public static DictionaryMaxlength New()
        {
            return FromZstd();
        }

        public static DictionaryMaxlength FromJson(string relativePath = "dicts/dictionary_maxlength.json")
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var fullPath = Path.Combine(baseDir, relativePath);

                if (!File.Exists(fullPath))
                    throw new FileNotFoundException($"JSON dictionary file not found: {fullPath}");

                var json = File.ReadAllText(fullPath);
                return JsonSerializer.Deserialize<DictionaryMaxlength>(json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to load dictionary from JSON.", ex);
            }
        }

        public static DictionaryMaxlength FromZstd(string relativePath = "dicts/dictionary_maxlength.zstd")
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var fullPath = Path.Combine(baseDir, relativePath);

                if (!File.Exists(fullPath))
                    throw new FileNotFoundException($"Zstd dictionary file not found: {fullPath}");

                var compressed = File.ReadAllBytes(fullPath);

                using (var decompressor = new Decompressor())
                {
                    var jsonBytes = decompressor.Unwrap(compressed);
                    var json = Encoding.UTF8.GetString(jsonBytes);
                    return JsonSerializer.Deserialize<DictionaryMaxlength>(json);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to load dictionary from Zstd.", ex);
            }
        }

        public void SerializeToJson(string path)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        }

        public static DictionaryMaxlength DeserializedFromJson(string path)
        {
            return FromJson(path);
        }

        public static DictionaryMaxlength FromDicts(string relativeBaseDir = "dicts")
        {
            var baseDir = Path.Combine(AppContext.BaseDirectory, relativeBaseDir);
            var instance = new DictionaryMaxlength();

            instance.st_characters = LoadFile(Path.Combine(baseDir, "STCharacters.txt"));
            instance.st_phrases = LoadFile(Path.Combine(baseDir, "STPhrases.txt"));
            instance.ts_characters = LoadFile(Path.Combine(baseDir, "TSCharacters.txt"));
            instance.ts_phrases = LoadFile(Path.Combine(baseDir, "TSPhrases.txt"));
            instance.tw_phrases = LoadFile(Path.Combine(baseDir, "TWPhrases.txt"));
            instance.tw_phrases_rev = LoadFile(Path.Combine(baseDir, "TWPhrasesRev.txt"));
            instance.tw_variants = LoadFile(Path.Combine(baseDir, "TWVariants.txt"));
            instance.tw_variants_rev = LoadFile(Path.Combine(baseDir, "TWVariantsRev.txt"));
            instance.tw_variants_rev_phrases = LoadFile(Path.Combine(baseDir, "TWVariantsRevPhrases.txt"));
            instance.hk_variants = LoadFile(Path.Combine(baseDir, "HKVariants.txt"));
            instance.hk_variants_rev = LoadFile(Path.Combine(baseDir, "HKVariantsRev.txt"));
            instance.hk_variants_rev_phrases = LoadFile(Path.Combine(baseDir, "HKVariantsRevPhrases.txt"));
            instance.jps_characters = LoadFile(Path.Combine(baseDir, "JPShinjitaiCharacters.txt"));
            instance.jps_phrases = LoadFile(Path.Combine(baseDir, "JPShinjitaiPhrases.txt"));
            instance.jp_variants = LoadFile(Path.Combine(baseDir, "JPVariants.txt"));
            instance.jp_variants_rev = LoadFile(Path.Combine(baseDir, "JPVariantsRev.txt"));
            instance.st_punctuations = LoadFile(Path.Combine(baseDir, "STPunctuations.txt"));
            instance.ts_punctuations = LoadFile(Path.Combine(baseDir, "TSPunctuations.txt"));

            return instance;
        }

        private static DictWithMaxLength LoadFile(string path)
        {
            var dict = new ConcurrentDictionary<string, string>();
            var maxLength = 1;

            if (!File.Exists(path)) throw new FileNotFoundException($"Dictionary file not found: {path}");
            // return new DictWithMaxLength
            // {
            //     Data = dict,
            //     MaxLength = maxLength
            // };
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var key = parts[0];
                    var value = parts[1];
                    dict[key] = value;
                    // Use SetItem to return a new dictionary
                    // dict = dict.SetItem(key, value);
                    // int keyLength = new StringInfo(key).LengthInTextElements;
                    var keyLength = key.Length;
                    maxLength = Math.Max(maxLength, keyLength);
                }
            }

            return new DictWithMaxLength
            {
                Data = dict,
                MaxLength = maxLength
            };
        }

        public void SaveCbor(string path)
        {
            var cbor = CBORObject.FromObject(this);
            File.WriteAllBytes(path, cbor.EncodeToBytes());
        }

        public static DictionaryMaxlength FromCbor(string relativePath = "dicts/dictionary_maxlength.cbor")
        {
            var baseDir = AppContext.BaseDirectory;
            var fullPath = Path.Combine(baseDir, relativePath);
            var bytes = File.ReadAllBytes(fullPath);
            var cbor = CBORObject.DecodeFromBytes(bytes);
            return cbor.ToObject<DictionaryMaxlength>();
        }

        public byte[] ToCborBytes()
        {
            return CBORObject.FromObject(this).EncodeToBytes();
        }

        public void SaveCompressed(string path)
        {
            // var json = JsonSerializer.Serialize(this);
            // var jsonBytes = Encoding.UTF8.GetBytes(json);
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(this);

            using (var options = new CompressionOptions(19))
            using (var compressor = new Compressor(options))
            {
                var compressed = compressor.Wrap(jsonBytes);
                File.WriteAllBytes(path, compressed);
            }
        }

        public static DictionaryMaxlength LoadCompressed(string path)
        {
            var compressed = File.ReadAllBytes(path);

            using (var decompressor = new Decompressor())
            {
                var jsonBytes = decompressor.Unwrap(compressed);
                var json = Encoding.UTF8.GetString(jsonBytes);
                return JsonSerializer.Deserialize<DictionaryMaxlength>(json);
            }
        }
    }
}