using System.CommandLine;
using System.Text;
using OpenccNetLib;

namespace OpenccNet;

internal static class OfficeCommand
{
    private const string Blue = "\u001b[1;34m";
    private const string Reset = "\u001b[0m";

    internal static Command CreateCommand()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        var inputFileOption = new Option<string?>("--input", "-i")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Input Office document <input>"
        };

        var outputFileOption = new Option<string?>("--output", "-o")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Output Office document <output>"
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

            if (!string.IsNullOrEmpty(value) &&
                !CliConfigNames.IsValid(value))
            {
                result.AddError(
                    $"Invalid config '{value}'. Valid options: " +
                    CliUtils.ConfigHelpAll);
            }
        });

        var punctOption = new Option<bool>("--punct", "-p")
        {
            DefaultValueFactory = _ => false,
            Description = "Enable punctuation conversion."
        };

        var formatOption = new Option<string?>("--format", "-f")
        {
            Description =
                "Force Office document format: " +
                "docx | xlsx | pptx | odt | ods | odp | epub"
        };

        formatOption.Validators.Add(result =>
        {
            var format = result.GetValueOrDefault<string>();

            if (!string.IsNullOrWhiteSpace(format) &&
                !OfficeConverter.IsValidOfficeFormat(format))
            {
                result.AddError(
                    $"Invalid format '{format}'. Valid: " +
                    string.Join(" | ", OfficeConverter.OfficeFormats));
            }
        });

        var keepFontOption = new Option<bool>("--keep-font", "-k")
        {
            DefaultValueFactory = _ => true,
            Description =
                "Preserve font names in Office documents [default: true]. " +
                "Use --keep-font:false to disable."
        };

        var quietOption = new Option<bool>("--quiet", "-q")
        {
            DefaultValueFactory = _ => false,
            Description =
                "Suppress status and progress output; only errors will be shown."
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

            if (string.IsNullOrWhiteSpace(value))
                return;

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
                "Load additional DeTofu fallback mappings from a UTF-8 text file. " +
                "Custom mappings override built-in mappings (requires --detofu)"
        };

        var keepIdsOption = new Option<bool>("--keep-ids", "-I")
        {
            DefaultValueFactory = _ => false,
            Description = "Preserve Unicode IDS expressions during conversion."
        };

        var normCompatOption = new Option<bool>("--norm-compat", "-n")
        {
            DefaultValueFactory = _ => false,
            Description =
                "Normalize CJK Compatibility Ideographs before conversion."
        };

        var normCompatExtendedOption =
            new Option<bool>("--norm-compat-extended", "-E")
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

        var officeCommand = new Command(
            "office",
            $"{Blue}Convert Office documents or EPUB using OpenccNetLib.{Reset}")
        {
            inputFileOption,
            outputFileOption,
            configOption,
            punctOption,
            formatOption,
            keepFontOption,
            quietOption,
            deTofuOption,
            deTofuFileOption,
            keepIdsOption,
            normCompatOption,
            normCompatExtendedOption,
            customDictOption
        };

        officeCommand.Validators.Add(result =>
        {
            var deTofuResult = result.GetResult(deTofuOption);
            var deTofuFileResult = result.GetResult(deTofuFileOption);

            if (deTofuFileResult is null)
                return;

            if (deTofuFileResult.Errors.Any())
                return;

            // Presence matters because "--detofu" with no value means "all".
            var deTofuEnabled = deTofuResult?.Tokens.Count > 0;

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

        officeCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var deTofuResult =
                parseResult.GetResult(deTofuOption);

            var deTofuEnabled =
                deTofuResult?.Tokens.Count > 0;

            var deTofu = deTofuEnabled
                ? parseResult.GetValue(deTofuOption)
                : null;

            if (deTofuEnabled &&
                string.IsNullOrWhiteSpace(deTofu))
            {
                deTofu = "all";
            }

            return await RunConversionAsync(
                input: parseResult.GetValue(inputFileOption),
                output: parseResult.GetValue(outputFileOption),
                config: parseResult.GetValue(configOption)!,
                punctuation: parseResult.GetValue(punctOption),
                format: parseResult.GetValue(formatOption),
                keepFont: parseResult.GetValue(keepFontOption),
                quiet: parseResult.GetValue(quietOption),
                deTofu: deTofu,
                deTofuFile:
                parseResult.GetValue(deTofuFileOption),
                keepIds:
                parseResult.GetValue(keepIdsOption),
                normCompat:
                parseResult.GetValue(normCompatOption),
                normCompatExtended:
                parseResult.GetValue(normCompatExtendedOption),
                customDictArgs:
                parseResult.GetValue(customDictOption) ??
                Array.Empty<string>(),
                cancellationToken: cancellationToken);
        });

        return officeCommand;
    }

    private static async Task<int> RunConversionAsync(
        string? input,
        string? output,
        string config,
        bool punctuation,
        string? format,
        bool keepFont,
        bool quiet,
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
            var resolvedInput = CliUtils.ValidateInputFile(
                input,
                "Input Office document");

            var resolvedFormat =
                ResolveFormat(resolvedInput, format);

            var resolvedOutput = ResolveOutputPath(
                resolvedInput,
                output,
                resolvedFormat,
                quiet);

            CliUtils.EnsureDifferentPaths(
                resolvedInput,
                resolvedOutput);

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

            var (success, message) =
                await OfficeConverter.ConvertOfficeDocAsync(
                    resolvedInput,
                    resolvedOutput,
                    resolvedFormat,
                    textConverter,
                    keepFont,
                    cancellationToken);

            if (!success)
            {
                await Console.Error.WriteLineAsync(
                    $"❌ Office document conversion failed: {message}");

                return CliUtils.ExitFailure;
            }

            if (!quiet)
            {
                await Console.Error.WriteLineAsync(
                    $"{message}\n📁 Output: {resolvedOutput}");
            }

            return CliUtils.ExitSuccess;
        }
        catch (Exception ex)
        {
            return CliUtils.WriteError(
                ex,
                "Office document conversion");
        }
    }

    private static string ResolveFormat(
        string input,
        string? format)
    {
        var resolvedFormat = !string.IsNullOrWhiteSpace(format)
            ? format.Trim().ToLowerInvariant()
            : Path.GetExtension(input)
                .TrimStart('.')
                .ToLowerInvariant();

        if (!OfficeConverter.IsValidOfficeFormat(resolvedFormat))
        {
            throw new NotSupportedException(
                "Unsupported file format. Supported: " +
                string.Join(", ", OfficeConverter.OfficeFormats));
        }

        return resolvedFormat;
    }

    private static string ResolveOutputPath(
        string input,
        string? output,
        string format,
        bool quiet)
    {
        var resolvedOutput = string.IsNullOrWhiteSpace(output)
            ? Path.Combine(
                Path.GetDirectoryName(input) ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(input)}_converted.{format}")
            : output.Trim();

        if (!string.IsNullOrWhiteSpace(output) &&
            string.IsNullOrEmpty(Path.GetExtension(resolvedOutput)))
        {
            resolvedOutput = $"{resolvedOutput}.{format}";

            CliUtils.WriteInfo(
                $"Output file extension adjusted to: {resolvedOutput}",
                quiet);
        }

        return CliUtils.ResolveOutputFile(resolvedOutput);
    }
}