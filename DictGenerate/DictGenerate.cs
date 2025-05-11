using System.CommandLine;
using DictionaryLib;

namespace DictGenerate
{
    public static class DictGenerate
    {
        private const string Blue = "\u001b[1;34m";
        private const string Reset = "\u001b[0m";

        static int Main(string[] args)
        {
            var formatOption = new Option<string>(
                aliases: new[] { "-f", "--format" },
                getDefaultValue: () => "zstd",
                description: "Dictionary format: [zstd|cbor|json]"
            ).FromAmong("zstd", "cbor", "json");

            var outputOption = new Option<string>(
                aliases: new[] { "-o", "--output" },
                description: "Output filename. Default: dictionary_maxlength.<ext>"
            );

            var baseDirOption = new Option<string>(
                aliases: new[] { "-b", "--base-dir" },
                getDefaultValue: () => "dicts",
                description: "Base directory containing source dictionary files"
            );

            var rootCommand = new RootCommand($"{Blue}Dict Generator CLI Tool{Reset}")
            {
                formatOption,
                outputOption,
                baseDirOption
            };

            rootCommand.SetHandler((format, output, baseDir) =>
            {
                // Determine output filename
                string defaultOutput = $"dictionary_maxlength.{format}";
                string outputFile = string.IsNullOrWhiteSpace(output) ? defaultOutput : output;

                Console.WriteLine($"{Blue}Generating dictionary from '{baseDir}'...{Reset}");

                var dict = DictionaryMaxlength.FromDicts(baseDir);

                switch (format)
                {
                    case "zstd":
                        dict.SaveCompressed(outputFile);
                        break;
                    case "cbor":
                        dict.SaveCbor(outputFile);
                        break;
                    case "json":
                        dict.SerializeToJson(outputFile);
                        break;
                }

                Console.WriteLine($"{Blue}Dictionary saved as '{outputFile}' in {format.ToUpper()} format.{Reset}");
            }, formatOption, outputOption, baseDirOption);

            return rootCommand.Invoke(args);
        }
    }
}
