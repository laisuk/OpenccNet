using System.CommandLine;
using OpenccNetLib;

namespace OpenccNet;

internal static class DictGenCommand
{
    private const string Blue = "\u001b[1;34m";
    private const string Reset = "\u001b[0m";

    internal static Command CreateCommand()
    {
        var formatOption = new Option<string>(
            ["-f", "--format"],
            () => "zstd",
            "Dictionary format: [zstd|cbor|json]"
        ).FromAmong("zstd", "cbor", "json");

        var outputOption = new Option<string>(
            ["-o", "--output"],
            "Output filename. Default: dictionary_maxlength.<ext>"
        );

        var baseDirOption = new Option<string>(
            ["-b", "--base-dir"],
            () => "dicts",
            "Base directory containing source dictionary files"
        );

        var dictGenCommand = new Command("dictgen", $"{Blue}Generate OpenccNetLib dictionary files.{Reset}")
        {
            formatOption,
            outputOption,
            baseDirOption
        };

        dictGenCommand.SetHandler((format, output, baseDir) =>
        {
            // Determine output filename
            var defaultOutput = $"dictionary_maxlength.{format}";
            var outputFile = string.IsNullOrWhiteSpace(output) ? defaultOutput : output;

            Console.WriteLine($"{Blue}Generating dictionary from '{baseDir}'...{Reset}");

            // Assuming DictionaryLib needs to be initialized or loaded before saving
            // You'll need to ensure OpenccNetLib provides a way to load the dictionary data.
            // var dict = DictionaryLib.FromDicts(baseDir);

            switch (format)
            {
                case "zstd":
                    DictionaryLib.SaveCompressed(outputFile);
                    break;
                case "cbor":
                    DictionaryLib.SaveCbor(outputFile);
                    break;
                case "json":
                    DictionaryLib.SerializeToJson(outputFile);
                    break;
            }

            Console.WriteLine($"{Blue}Dictionary saved as '{outputFile}' in {format.ToUpper()} format.{Reset}");
        }, formatOption, outputOption, baseDirOption);

        return dictGenCommand;
    }
}