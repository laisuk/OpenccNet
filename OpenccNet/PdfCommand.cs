using System.CommandLine;
using System.Diagnostics;
using System.Text;
using OpenccNetLib;

namespace OpenccNet;

internal static class PdfCommand
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
            Description = "Input PDF file <input.pdf>"
        };

        var outputFileOption = new Option<string?>("--output", "-o")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Output text file <output.txt>"
        };

        var configOption = new Option<string?>("--config", "-c")
        {
            Description =
                "Conversion configuration.\nValid options: " +
                CliUtils.ConfigHelpAll
        };

        configOption.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>();

            if (!string.IsNullOrWhiteSpace(value) &&
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

        // Use -H so -h stays as the global help alias.
        var headerOption = new Option<bool>("--header", "-H")
        {
            DefaultValueFactory = _ => false,
            Description = "Add [Page x/y] headers to the extracted text."
        };

        var reflowOption = new Option<bool>("--reflow", "-r")
        {
            DefaultValueFactory = _ => false,
            Description = "Reflow CJK paragraphs into continuous lines."
        };

        var compactOption = new Option<bool>("--compact", "-C")
        {
            DefaultValueFactory = _ => false,
            Description =
                "Use compact reflow (fewer blank lines between paragraphs). " +
                "Only meaningful with --reflow."
        };

        var quietOption = new Option<bool>("--quiet", "-q")
        {
            DefaultValueFactory = _ => false,
            Description =
                "Suppress status and progress output; only errors will be shown."
        };

        var extractOption = new Option<bool>("--extract", "-e")
        {
            DefaultValueFactory = _ => false,
            Description = "Extract text from PDF only (no OpenCC conversion)."
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

        var pdfCommand = new Command(
            "pdf",
            $"{Blue}Convert a PDF to UTF-8 text using PdfPig + " +
            $"OpenccNetLib, with optional CJK paragraph reflow.{Reset}")
        {
            inputFileOption,
            outputFileOption,
            configOption,
            punctOption,
            headerOption,
            reflowOption,
            compactOption,
            quietOption,
            extractOption,
            deTofuOption,
            deTofuFileOption,
            keepIdsOption,
            normCompatOption,
            normCompatExtendedOption,
            customDictOption
        };

        pdfCommand.Validators.Add(result =>
        {
            var extractOnly = result.GetValue(extractOption);
            var config = result.GetValue(configOption);

            if (!extractOnly && string.IsNullOrWhiteSpace(config))
            {
                result.AddError(
                    "--config is required unless --extract is used.");
            }

            // Conversion-only options are ignored in extract mode.
            if (extractOnly)
                return;

            var deTofuResult = result.GetResult(deTofuOption);
            var deTofuFileResult = result.GetResult(deTofuFileOption);

            if (deTofuFileResult is null)
                return;

            // Avoid cascading errors when System.CommandLine already rejected the value.
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

        pdfCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var deTofuResult = parseResult.GetResult(deTofuOption);
            var deTofuEnabled = deTofuResult?.Tokens.Count > 0;
            var deTofu = deTofuEnabled
                ? parseResult.GetValue(deTofuOption)
                : null;

            if (deTofuEnabled && string.IsNullOrWhiteSpace(deTofu))
                deTofu = "all";

            return await RunPdfAsync(
                input: parseResult.GetValue(inputFileOption),
                output: parseResult.GetValue(outputFileOption),
                config: parseResult.GetValue(configOption),
                punctuation: parseResult.GetValue(punctOption),
                addHeader: parseResult.GetValue(headerOption),
                reflow: parseResult.GetValue(reflowOption),
                compact: parseResult.GetValue(compactOption),
                quiet: parseResult.GetValue(quietOption),
                extractOnly: parseResult.GetValue(extractOption),
                deTofu: deTofu,
                deTofuFile: parseResult.GetValue(deTofuFileOption),
                keepIds: parseResult.GetValue(keepIdsOption),
                normCompat: parseResult.GetValue(normCompatOption),
                normCompatExtended: parseResult.GetValue(normCompatExtendedOption),
                customDictArgs:
                parseResult.GetValue(customDictOption) ??
                Array.Empty<string>(),
                cancellationToken);
        });

        return pdfCommand;
    }

    private static async Task<int> RunPdfAsync(
        string? input,
        string? output,
        string? config,
        bool punctuation,
        bool addHeader,
        bool reflow,
        bool compact,
        bool quiet,
        bool extractOnly,
        string? deTofu,
        string? deTofuFile,
        bool keepIds,
        bool normCompat,
        bool normCompatExtended,
        string[] customDictArgs,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var progressLineActive = false;

        try
        {
            var resolvedInput = ValidatePdfInput(input);
            var resolvedOutput = ResolveOutputPath(
                resolvedInput,
                output,
                extractOnly);

            CliUtils.EnsureDifferentPaths(
                resolvedInput,
                resolvedOutput);

            ReportIgnoredOptions(
                extractOnly,
                config,
                punctuation,
                deTofu,
                deTofuFile,
                keepIds,
                normCompat,
                normCompatExtended,
                customDictArgs,
                quiet);

            if (!extractOnly &&
                !string.IsNullOrWhiteSpace(deTofuFile))
            {
                deTofuFile = CliUtils.ValidateInputFile(
                    deTofuFile,
                    "DeTofu mapping file");
            }

            if (compact && !reflow)
            {
                CliUtils.WriteInfo(
                    "--compact has no effect without --reflow; ignoring.",
                    quiet);
            }

            CliUtils.WriteInfo(
                "Processing PDF...",
                quiet);

            progressLineActive = !quiet;

            var finalText = await ExtractTextAsync(
                resolvedInput,
                addHeader,
                quiet,
                cancellationToken);

            FinishProgressLine(ref progressLineActive);

            finalText = Opencc.NormalizeUnicodeCompat(finalText);

            if (reflow)
            {
                CliUtils.WriteInfo(
                    "Reflowing CJK paragraphs...",
                    quiet);

                finalText = ReflowHelper.ReflowCjkParagraphs(
                    finalText,
                    addPdfPageHeader: addHeader,
                    compact: compact);
            }

            if (!extractOnly)
            {
                CliUtils.WriteInfo(
                    $"Converting ({config})...",
                    quiet);

                var textConverter = CliTextPipeline.Build(
                    config!,
                    punctuation,
                    keepIds,
                    normCompat,
                    normCompatExtended,
                    deTofu,
                    deTofuFile,
                    customDictArgs);

                finalText = textConverter(finalText);
            }

            CliUtils.WriteInfo(
                "Writing output...",
                quiet);

            await WriteOutputAsync(
                resolvedOutput,
                finalText,
                cancellationToken);

            stopwatch.Stop();

            WriteSuccess(
                resolvedOutput,
                extractOnly,
                stopwatch.Elapsed,
                quiet);

            return CliUtils.ExitSuccess;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            FinishProgressLine(ref progressLineActive);

            return CliUtils.WriteError(
                ex,
                extractOnly
                    ? "PDF extraction"
                    : "PDF conversion");
        }
    }

    private static string ValidatePdfInput(string? input)
    {
        var resolvedInput = CliUtils.ValidateInputFile(
            input,
            "Input PDF file");

        if (!string.Equals(
                Path.GetExtension(resolvedInput),
                ".pdf",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Input file must be a PDF: {resolvedInput}");
        }

        return resolvedInput;
    }

    private static string ResolveOutputPath(
        string input,
        string? output,
        bool extractOnly)
    {
        var resolvedOutput = string.IsNullOrWhiteSpace(output)
            ? Path.Combine(
                Path.GetDirectoryName(input) ?? string.Empty,
                Path.GetFileNameWithoutExtension(input) +
                (extractOnly
                    ? "_extracted.txt"
                    : "_converted.txt"))
            : output.Trim();

        return CliUtils.ResolveOutputFile(resolvedOutput);
    }

    private static void ReportIgnoredOptions(
        bool extractOnly,
        string? config,
        bool punctuation,
        string? deTofu,
        string? deTofuFile,
        bool keepIds,
        bool normCompat,
        bool normCompatExtended,
        string[] customDictArgs,
        bool quiet)
    {
        if (!extractOnly)
            return;

        if (!string.IsNullOrWhiteSpace(config))
        {
            CliUtils.WriteInfo(
                "--config is ignored in --extract mode.",
                quiet);
        }

        if (punctuation)
        {
            CliUtils.WriteInfo(
                "--punct has no effect in --extract mode.",
                quiet);
        }

        if (!string.IsNullOrWhiteSpace(deTofu))
        {
            CliUtils.WriteInfo(
                "--detofu has no effect in --extract mode.",
                quiet);
        }

        if (!string.IsNullOrWhiteSpace(deTofuFile))
        {
            CliUtils.WriteInfo(
                "--detofu-file has no effect in --extract mode.",
                quiet);
        }

        if (keepIds)
        {
            CliUtils.WriteInfo(
                "--keep-ids has no effect in --extract mode.",
                quiet);
        }

        if (normCompat)
        {
            CliUtils.WriteInfo(
                "--norm-compat has no effect in --extract mode.",
                quiet);
        }

        if (normCompatExtended)
        {
            CliUtils.WriteInfo(
                "--norm-compat-extended has no effect in --extract mode.",
                quiet);
        }

        if (customDictArgs.Length > 0)
        {
            CliUtils.WriteInfo(
                "--custom-dict has no effect in --extract mode.",
                quiet);
        }
    }

    private static Task<string> ExtractTextAsync(
        string input,
        bool addHeader,
        bool quiet,
        CancellationToken cancellationToken)
    {
        return PdfHelper.LoadPdfTextAsync(
            filename: input,
            addPdfPageHeader: addHeader,
            statusCallback: quiet
                ? null
                : status => Console.Error.Write("\r" + status),
            cancellationToken: cancellationToken);
    }

    private static Task WriteOutputAsync(
        string output,
        string text,
        CancellationToken cancellationToken)
    {
        return File.WriteAllTextAsync(
            output,
            text,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }

    private static void FinishProgressLine(
        ref bool progressLineActive)
    {
        if (!progressLineActive)
            return;

        Console.Error.WriteLine();
        progressLineActive = false;
    }

    private static void WriteSuccess(
        string output,
        bool extractOnly,
        TimeSpan elapsed,
        bool quiet)
    {
        if (quiet)
            return;

        CliUtils.WriteSuccess(
            $"PDF {(extractOnly ? "extraction" : "conversion")} succeeded.");

        Console.Error.WriteLine(
            $"⏱ Elapsed: {FormatElapsed(elapsed)}");
        Console.Error.WriteLine($"📁 Output: {output}");
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return elapsed.TotalMinutes >= 1
            ? $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}.{elapsed.Milliseconds:000}"
            : $"{elapsed.TotalSeconds:F2} s";
    }
}