using System.Text.Json.Serialization;

namespace OpenccNetLib
{
    [JsonSourceGenerationOptions(
        GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(DictionaryMaxlength))]
    [JsonSerializable(typeof(DictWithMaxLength))]
    internal partial class DictionaryJsonContext : JsonSerializerContext
    {
    }
}