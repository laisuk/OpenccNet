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
                CliUtils.ConfigHelpAll
        };

        configOption.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>();
            if (!string.IsNullOrEmpty(value) && !CliConfigNames.IsValid(value))
            {
                result.AddError(
                    $"Invalid config '{value}'. Valid options: {CliUtils.ConfigHelpAll}"
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

        var keepIdsOption = new Option<bool>("--keep-ids", "-I")
        {
            DefaultValueFactory = _ => false,
            Description = "Preserve Unicode IDS expressions during conversion."
        };

        var normCompatOption = new Option<bool>("--norm-compat", "-n")
        {
            DefaultValueFactory = _ => false,
            Description = "Normalize CJK Compatibility Ideographs before conversion."
        };

        var normCompatExtendedOption = new Option<bool>("--norm-compat-extended", "-E")
        {
            DefaultValueFactory = _ => false,
            Description =
                "Apply extended Unicode compatibility normalization before conversion."
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

        var convertCommand = new Command(
            "convert",
            $"{Blue}Convert text using OpenccNetLib configurations.{Reset}")
        {
            inputFileOption,
            outputFileOption,
            configOption,
            punctOption,
            deTofuOption,
            deTofuFileOption,
            keepIdsOption,
            normCompatOption,
            normCompatExtendedOption,
            customDictOption,
            inputEncodingOption,
            outputEncodingOption
        };

        convertCommand.Validators.Add(result =>
        {
            // var deTofuResult = result.GetResult(deTofuOption);
            var deTofuFileResult = result.GetResult(deTofuFileOption);

            // --detofu-file was not supplied.
            if (deTofuFileResult is null)
                return;

            // Avoid cascading errors when System.CommandLine already rejected the value.
            if (deTofuFileResult.Errors.Any())
                return;

            // Presence matters here, because "--detofu" with no value means level "all".
            var deTofuEnabled = result.Tokens.Any(token => token.Value is "--detofu");

            if (!deTofuEnabled)
            {
                result.AddError("--detofu-file requires --detofu.");
                return;
            }

            var path = deTofuFileResult.GetValueOrDefault<string>();

            try
            {
                CliUtils.ValidateInputFile(
                    path,
                    "DeTofu mapping file");
            }
            catch (ArgumentException ex)
            {
                result.AddError(ex.Message);
            }
            catch (IOException ex)
            {
                result.AddError(ex.Message);
            }
        });

        convertCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            // var deTofuResult = parseResult.GetResult(deTofuOption);

            var deTofuEnabled = parseResult.Tokens.Any(token => token.Value is "--detofu");

            var deTofu = deTofuEnabled
                ? parseResult.GetValue(deTofuOption)
                : null;

            if (deTofuEnabled &&
                string.IsNullOrWhiteSpace(deTofu))
            {
                deTofu = "all";
            }

            return await RunConversionAsync(
                inputFile: parseResult.GetValue(inputFileOption),
                outputFile: parseResult.GetValue(outputFileOption),
                config: parseResult.GetValue(configOption)!,
                punctuation: parseResult.GetValue(punctOption),
                inputEncoding: parseResult.GetValue(inputEncodingOption)!,
                outputEncoding: parseResult.GetValue(outputEncodingOption)!,
                deTofu: deTofu,
                deTofuFile: parseResult.GetValue(deTofuFileOption),
                keepIds: parseResult.GetValue(keepIdsOption),
                normCompat: parseResult.GetValue(normCompatOption),
                normCompatExtended:
                parseResult.GetValue(normCompatExtendedOption),
                customDictArgs:
                parseResult.GetValue(customDictOption) ??
                Array.Empty<string>(),
                cancellationToken: cancellationToken);
        });

        return convertCommand;
    }

    private static async Task<int> RunConversionAsync(
        string? inputFile,
        string? outputFile,
        string config,
        bool punctuation,
        string inputEncoding,
        string outputEncoding,
        string? deTofu,
        string? deTofuFile,
        bool keepIds,
        bool normCompat,
        bool normCompatExtended,
        string[] customDictArgs,
        CancellationToken cancellationToken)
    {
        try
        {
            var inputEnc = CliUtils.ResolveEncoding(inputEncoding);
            var outputEnc = CliUtils.ResolveEncoding(outputEncoding);

            if (!string.IsNullOrWhiteSpace(inputFile))
                inputFile = CliUtils.ValidateInputFile(inputFile);

            if (!string.IsNullOrWhiteSpace(outputFile))
            {
                outputFile = CliUtils.ResolveOutputFile(outputFile);

                if (!string.IsNullOrWhiteSpace(inputFile))
                    CliUtils.EnsureDifferentPaths(inputFile, outputFile);
            }

            if (!string.IsNullOrWhiteSpace(deTofuFile))
            {
                deTofuFile = CliUtils.ValidateInputFile(
                    deTofuFile,
                    "DeTofu mapping file");
            }

            var textConverter = CliTextPipeline.Build(
                config,
                punctuation,
                keepIds,
                normCompat,
                normCompatExtended,
                deTofu,
                deTofuFile,
                customDictArgs);

            var inputText = await ReadInputAsync(
                inputFile,
                inputEnc,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var outputText = textConverter(inputText);

            await WriteOutputAsync(
                outputFile,
                outputText,
                outputEnc,
                cancellationToken);

            var inFrom = inputFile ?? "<stdin>";
            var outTo = outputFile ?? "<stdout>";

            lock (ConsoleLock)
            {
                var options = new List<string>();

                if (normCompatExtended)
                    options.Add("norm-compat-extended");
                else if (normCompat)
                    options.Add("norm-compat");

                if (!string.IsNullOrEmpty(deTofu))
                    options.Add($"detofu:{deTofu}");

                if (keepIds)
                    options.Add("keep-ids:true");

                var optionText = options.Count > 0
                    ? ", " + string.Join(", ", options)
                    : string.Empty;

                CliUtils.WriteSuccess(
                    $"Conversion ({config}{optionText}): {inFrom} → {outTo}");
            }

            return CliUtils.ExitSuccess;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            lock (ConsoleLock)
            {
                return CliUtils.WriteError(
                    ex,
                    "Conversion");
            }
        }
    }

    private static async Task<string> ReadInputAsync(
        string? inputFile,
        Encoding inputEncoding,
        CancellationToken cancellationToken)
    {
        if (inputFile != null)
        {
            return await File.ReadAllTextAsync(
                inputFile,
                inputEncoding,
                cancellationToken);
        }

        if (!Console.IsInputRedirected)
        {
            lock (ConsoleLock)
            {
                Console.Error.WriteLine(
                    "Input text to convert, <Ctrl+Z> (Windows) or <Ctrl+D> (Unix) then Enter to submit:");
            }
        }

        using var reader = new StreamReader(
            Console.OpenStandardInput(),
            inputEncoding);

        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static async Task WriteOutputAsync(
        string? outputFile,
        string content,
        Encoding outputEncoding,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(outputFile))
        {
            await File.WriteAllTextAsync(
                outputFile,
                content,
                outputEncoding,
                cancellationToken);
        }
        else
        {
            cancellationToken.ThrowIfCancellationRequested();

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