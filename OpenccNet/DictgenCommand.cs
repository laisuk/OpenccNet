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
            var value = result.GetValueOrDefault<string>()
                .ToLowerInvariant();

            if (value is not ("zstd" or "cbor" or "json"))
            {
                result.AddError(
                    "Format must be one of: zstd, cbor, json.");
            }
        });

        var outputOption = new Option<string>("--output", "-o")
        {
            Description = "Output filename. Default: dictionary_maxlength.<ext>"
        };

        var baseDirOption = new Option<string>("--base-dir", "-b")
        {
            DefaultValueFactory = _ => "dicts",
            Description = "Base directory containing OpenCC-style .txt dictionary sources (for dictgen)"
        };

        var unescapeOption = new Option<bool>("--unescape", "-u")
        {
            Description = "For JSON format only: write readable Unicode characters instead of \\uXXXX escapes"
        };

        var customDictOption = new Option<string[]>("--custom-dict", "-D")
        {
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = false,
            Description =
                "Load custom dictionary: <slot>:<mode>:<path>.\n" +
                "Example: HkPhrasesRev:append:my_hk_dict.txt\n" +
                "Available slots: " +
                CliUtils.SlotHelpAll
        };

        CliUtils.AddCustomDictValidator(customDictOption);

        var dictGenCommand = new Command(
            "dictgen",
            $"{Blue}Generate OpenccNetLib dictionary files.{Reset}\n\n" +
            "Examples:\n" +
            "  OpenccNet dictgen\n" +
            "    Generate default Zstd dictionary (dictionary_maxlength.zstd)\n\n" +
            "  OpenccNet dictgen -f cbor\n" +
            "    Generate CBOR dictionary for interop\n\n" +
            "  OpenccNet dictgen -f json --unescape\n" +
            "    Generate readable JSON dictionary without \\uXXXX escapes\n"
        )
        {
            formatOption,
            outputOption,
            baseDirOption,
            unescapeOption,
            customDictOption,
        };

        dictGenCommand.Validators.Add(result =>
        {
            if (result.GetValue(unescapeOption) &&
                !string.Equals(
                    result.GetValue(formatOption),
                    "json",
                    StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("--unescape can only be used with --format json.");
            }
        });

        dictGenCommand.SetAction(pr => RunDictgen(
            pr.GetValue(formatOption)!,
            pr.GetValue(outputOption),
            pr.GetValue(baseDirOption)!,
            pr.GetValue(unescapeOption),
            pr.GetValue(customDictOption) ?? Array.Empty<string>()));

        return dictGenCommand;
    }

    private static int RunDictgen(
        string format,
        string? output,
        string baseDir,
        bool unescape,
        string[] customDictArgs)
    {
        try
        {
            var normalizedFormat = format.Trim().ToLowerInvariant();

            var baseDirectory = CliUtils.ValidateDirectory(
                baseDir,
                "Dictionary base directory");

            var outputFile = CliUtils.ResolveOutputFile(
                string.IsNullOrWhiteSpace(output)
                    ? $"dictionary_maxlength.{normalizedFormat}"
                    : output);

            var customSpecs =
                CliUtils.ParseAndValidateCustomDictSpecs(customDictArgs);

            CliUtils.WriteInfo(
                $"Loading base dictionaries from '{baseDirectory}'...");

            // Dictgen regenerates the complete provider from raw source files.
            // Do not replace this with DictionaryLib.New().
            var dictionary = DictionaryLib.FromDicts(baseDirectory);

            if (customSpecs.Length > 0)
            {
                CliUtils.WriteInfo(
                    $"Applying {customSpecs.Length} custom dictionary spec(s)...");

                DictionaryLib.WithCustomDicts(dictionary, customSpecs);
            }

            SaveDictionary(
                normalizedFormat,
                outputFile,
                dictionary,
                unescape);

            CliUtils.WriteSuccess(
                $"{Blue}Dictionary saved as '{outputFile}' in " +
                $"{normalizedFormat.ToUpperInvariant()} format.{Reset}");

            return CliUtils.ExitSuccess;
        }
        catch (Exception ex)
        {
            return CliUtils.WriteError(
                ex,
                "Dictionary generation");
        }
    }

    private static void SaveDictionary(
        string format,
        string outputFile,
        DictionaryMaxlength dictionary,
        bool unescape)
    {
        switch (format)
        {
            case "zstd":
                DictionaryLib.SaveJsonCompressed(outputFile, dictionary);
                break;

            case "cbor":
                DictionaryLib.SaveCbor(outputFile, dictionary);
                break;

            case "json" when unescape:
                DictionaryLib.SerializeToJsonUnescaped(outputFile, dictionary);
                break;

            case "json":
                DictionaryLib.SerializeToJson(outputFile, dictionary);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(format),
                    format,
                    "Unsupported dictionary format.");
        }
    }
}