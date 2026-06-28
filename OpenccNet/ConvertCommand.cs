using System.CommandLine;
using System.Text;
using OpenccNetLib;

namespace OpenccNet;

internal static class ConvertCommand
{
    private const string Blue = "\u001b[1;34m";
    private const string Reset = "\u001b[0m";
    private static readonly object ConsoleLock = new();

    public static Command CreateCommand()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Console.InputEncoding = Console.OutputEncoding = Encoding.UTF8;

        var inputFileOption = new Option<string?>("--input", "-i")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Read original text from file <input>"
        };

        var outputFileOption = new Option<string?>("--output", "-o")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Write original text to file <output>"
        };

        var configOption = new Option<string>("--config", "-c")
        {
            Required = true,
            Description =
                "Conversion configuration.\nValid options: " +
                string.Join(", ", CliConfigNames.All)
        };

        configOption.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>();
            if (!string.IsNullOrEmpty(value) && !CliConfigNames.IsValid(value))
            {
                result.AddError(
                    $"Invalid config '{value}'. Valid options: {string.Join(", ", CliConfigNames.All)}"
                );
            }
        });

        var punctOption = new Option<bool>("--punct", "-p")
        {
            DefaultValueFactory = _ => false,
            Description = "Punctuation conversion."
        };

        var inputEncodingOption = new Option<string>("--in-enc")
        {
            DefaultValueFactory = _ => "UTF-8",
            Description = "Encoding for input: UTF-8|UNICODE|GBK|GB2312|BIG5|Shift-JIS"
        };

        var outputEncodingOption = new Option<string>("--out-enc")
        {
            DefaultValueFactory = _ => "UTF-8",
            Description = "Encoding for output: UTF-8|UNICODE|GBK|GB2312|BIG5|Shift-JIS"
        };

        var deTofuOption = new Option<string?>("--detofu")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description =
                "Apply tofu-safe fallback after conversion: all, ext-b, ext-c, ext-d, ext-e, ext-f, ext-g, ext-h, ext-i"
        };

        deTofuOption.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>();

            if (string.IsNullOrWhiteSpace(value)) return;
            try
            {
                DeTofu.ParseLevel(value);
            }
            catch (ArgumentException ex)
            {
                result.AddError(ex.Message);
            }
        });

        deTofuOption.DefaultValueFactory = _ => null;

        var deTofuFileOption = new Option<string?>("--detofu-file")
        {
            Arity = ArgumentArity.ExactlyOne,
            Description =
                "Load additional DeTofu fallback mappings from a UTF-8 text file. Custom mappings override built-in mappings (requires --detofu)"
        };

        var keepIdsOption = new Option<bool>("--keep-ids")
        {
            DefaultValueFactory = _ => false,
            Description = "Preserve Unicode IDS expressions during conversion."
        };
        
        var normCompatOption = new Option<bool>("--norm-compat")
        {
            DefaultValueFactory = _ => false,
            Description = "Normalize CJK Compatibility Ideographs before conversion."
        };

        var customDictOption = new Option<string[]>("--custom-dict")
        {
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = false,
            Description = "Load custom dictionary: <slot>:<mode>:<path>. Example: hkphrasesrev:append:my_hk_dict.txt"
        };

        customDictOption.Validators.Add(result =>
        {
            foreach (var value in result.GetValueOrDefault<string[]>())
            {
                try
                {
                    CliUtils.ParseCustomDictSpec(value);
                }
                catch (ArgumentException ex)
                {
                    result.AddError(ex.Message);
                }
            }
        });

        var convertCommand = new Command("convert", $"{Blue}Convert text using OpenccNetLib configurations.{Reset}")
        {
            inputFileOption,
            outputFileOption,
            configOption,
            punctOption,
            deTofuOption,
            deTofuFileOption,
            keepIdsOption,
            normCompatOption,
            customDictOption,
            inputEncodingOption,
            outputEncodingOption
        };

        convertCommand.Validators.Add(result =>
        {
            var deTofuResult = result.GetResult(deTofuOption);
            var deTofuFileResult = result.GetResult(deTofuFileOption);

            if (deTofuFileResult is null) return;
            if (deTofuFileResult.Errors.Any())
                return;

            if (deTofuResult is null)
                result.AddError("--detofu-file requires --detofu.");
        });

        convertCommand.SetAction(async (pr, _) =>
        {
            var inputFile = pr.GetValue(inputFileOption);
            var outputFile = pr.GetValue(outputFileOption);
            var config = pr.GetValue(configOption)!;
            var punct = pr.GetValue(punctOption);

            var deTofuResult = pr.GetResult(deTofuOption);
            var deTofuEnabled = deTofuResult?.Tokens.Count > 0;
            var deTofu = deTofuEnabled
                ? pr.GetValue(deTofuOption)
                : null;

            if (deTofuEnabled && string.IsNullOrWhiteSpace(deTofu))
                deTofu = "all";

            var deTofuFile = pr.GetValue(deTofuFileOption);
            var keepIds = pr.GetValue(keepIdsOption);
            var normCompat = pr.GetValue(normCompatOption);
            var inputEnc = pr.GetValue(inputEncodingOption)!;
            var outputEnc = pr.GetValue(outputEncodingOption)!;
            var customDicts = pr.GetValue(customDictOption) ?? Array.Empty<string>();

            return await RunConversionAsync(
                inputFile, outputFile, config, punct, inputEnc, outputEnc, deTofu, deTofuFile, keepIds,  normCompat, customDicts
            );
        });


        return convertCommand;
    }

    private static async Task<int> RunConversionAsync(
        string? inputFile,
        string? outputFile,
        string config,
        bool punct,
        string inputEncoding,
        string outputEncoding,
        string? deTofu,
        string? deTofuFile,
        bool keepIds,
        bool normCompat,
        string[] customDicts)
    {
        try
        {
            if (customDicts.Length > 0)
            {
                var dict = DictionaryLib.New();

                var specs = customDicts
                    .Select(CliUtils.ParseCustomDictSpec)
                    .ToArray();

                DictionaryLib.WithCustomDicts(dict, specs);
                Opencc.UseCustomDictionary(dict);
            }

            // Assuming OpenccNetLib provides a way to initialize Opencc with a config string
            var opencc = new Opencc(config);

            opencc.SetPreserveIds(keepIds);

            var inputStr = await ReadInputAsync(inputFile, inputEncoding);
            if (normCompat)
            {
                inputStr = opencc.NormalizeCompat(inputStr);
            }
            
            var outputStr = opencc.Convert(inputStr, punct);

            if (!string.IsNullOrWhiteSpace(deTofu))
            {
                var level = DeTofu.ParseLevel(deTofu);

                outputStr = string.IsNullOrWhiteSpace(deTofuFile)
                    ? opencc.DeTofu(outputStr, level)
                    : opencc.DeTofuWithCustomFile(outputStr, level, deTofuFile);
            }

            await WriteOutputAsync(outputFile, outputStr, outputEncoding);

            var inFrom = inputFile ?? "<stdin>";
            var outTo = outputFile ?? "<stdout>";
            lock (ConsoleLock)
            {
                var options = new List<string>();

                if (!string.IsNullOrEmpty(deTofu))
                    options.Add($"detofu:{deTofu}");

                if (keepIds)
                    options.Add("keep-ids:true");

                var optionText = options.Count > 0
                    ? ", " + string.Join(", ", options)
                    : string.Empty;

                Console.WriteLine(
                    $"✅ Conversion ({config}{optionText}): {inFrom} → {outTo}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            lock (ConsoleLock)
            {
                Console.Error.WriteLine($"Error during conversion: {ex.Message}");
            }

            return 1;
        }
    }

    private static async Task<string> ReadInputAsync(string? inputFile, string inputEncoding)
    {
        if (inputFile != null)
        {
            return await File.ReadAllTextAsync(inputFile, Encoding.GetEncoding(inputEncoding));
        }

        if (!Console.IsInputRedirected)
        {
            lock (ConsoleLock)
            {
                Console.Error.WriteLine(
                    "Input text to convert, <Ctrl+Z> (Windows) or <Ctrl+D> (Unix) then Enter to submit:");
            }
        }

        var encoding = Encoding.GetEncoding(inputEncoding);
        using var reader = new StreamReader(Console.OpenStandardInput(), encoding);
        return await reader.ReadToEndAsync();
    }

    private static async Task WriteOutputAsync(string? outputFile, string content, string encodingName)
    {
        Encoding encoding;
        if (string.Equals(encodingName, "utf-8", StringComparison.OrdinalIgnoreCase))
            encoding = new UTF8Encoding(false);
        else if (string.Equals(encodingName, "utf-16le", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(encodingName, "unicode", StringComparison.OrdinalIgnoreCase))
            encoding = new UnicodeEncoding(false, false);
        else if (string.Equals(encodingName, "utf-16be", StringComparison.OrdinalIgnoreCase))
            encoding = new UnicodeEncoding(true, false);
        else if (string.Equals(encodingName, "utf-32", StringComparison.OrdinalIgnoreCase))
            encoding = new UTF32Encoding(false, false);
        else
            encoding = Encoding.GetEncoding(encodingName);

        if (!string.IsNullOrEmpty(outputFile))
            await File.WriteAllTextAsync(outputFile, content, encoding);
        else
        {
            Console.Write(content);

            if (!Console.IsOutputRedirected &&
                !string.IsNullOrEmpty(content) &&
                !content.EndsWith('\n'))
            {
                Console.WriteLine();
            }
        }
    }
}