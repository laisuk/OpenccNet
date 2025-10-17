using System.CommandLine;
using OpenccNetLib;

namespace OpenccNet;

internal static class DictgenCommand
{
    private const string Blue = "\u001b[1;34m";
    private const string Reset = "\u001b[0m";

    internal static Command CreateCommand()
    {
        var formatOption = new Option<string>("--format", "-f")
        {
            DefaultValueFactory = _ => "zstd",
            Description = "Dictionary format: zstd|cbor|json",
        };

        formatOption.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>().ToLowerInvariant();
            if (value is not ("zstd" or "cbor" or "json"))
            {
                result.AddError("Format must be one of: zstd, cbor, json.");
            }
        });

        var outputOption = new Option<string>("--output", "-o")
        {
            Description = "Output filename. Default: dictionary_maxlength.<ext>"
        };

        var baseDirOption = new Option<string>("--base-dir", "-b")
        {
            DefaultValueFactory = _ => "dicts",
            Description = "Base directory containing source dictionary files"
        };

        var dictGenCommand = new Command("dictgen", $"{Blue}Generate OpenccNetLib dictionary files.{Reset}")
        {
            formatOption,
            outputOption,
            baseDirOption
        };

        dictGenCommand.SetAction(pr =>
        {
            var format = pr.GetValue(formatOption)!;
            var output = pr.GetValue(outputOption);
            var baseDir = pr.GetValue(baseDirOption)!;

            var defaultOutput = $"dictionary_maxlength.{format}";
            var outputFile = string.IsNullOrWhiteSpace(output) ? defaultOutput : output;

            Console.WriteLine($"{Blue}Generating dictionary from '{baseDir}'...{Reset}");

            switch (format.ToLowerInvariant())
            {
                case "zstd":
                    DictionaryLib.SaveJsonCompressed(outputFile);
                    break;
                case "cbor":
                    DictionaryLib.SaveCbor(outputFile);
                    break;
                case "json":
                    DictionaryLib.SerializeToJson(outputFile);
                    break;
                default:
                    Console.Error.WriteLine($"❌ Unknown format: {format}");
                    return 1;
            }

            Console.WriteLine(
                $"{Blue}Dictionary saved as '{Path.GetFullPath(outputFile)}' in {format.ToUpper()} format.{Reset}");

            return 0;
        });

        return dictGenCommand;
    }
}