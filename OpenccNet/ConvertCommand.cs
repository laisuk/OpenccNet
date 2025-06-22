using System.CommandLine;
using System.Text;
using OpenccNetLib;

namespace OpenccNet;

internal static class ConvertCommand
{
    private const string Blue = "\u001b[1;34m";
    private const string Reset = "\u001b[0m";

    private static readonly object ConsoleLock = new(); // For thread-safe console writing

    // Supported configuration names for conversion directions.
    private static readonly HashSet<string> ConfigList = new(StringComparer.Ordinal)
    {
        "s2t", "t2s", "s2tw", "tw2s", "s2twp", "tw2sp", "s2hk", "hk2s", "t2tw", "tw2t", "t2twp", "tw2tp",
        "t2hk", "hk2t", "t2jp", "jp2t"
    };

    // Supported Office file formats for Office documents conversion.
    private static readonly HashSet<string> OfficeFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "docx", "xlsx", "pptx", "odt", "ods", "odp"
    };

    internal static Command CreateCommand()
    {
        // --- Global Setup for Console Encoding ---
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        // ------------------------------------------

        // Define options for the convert command
        var inputFileOption = new Option<string?>(
            ["-i", "--input"],
            description: "Read original text from file <input>."
        );

        var outputFileOption = new Option<string?>(
            ["-o", "--output"],
            description: "Write converted text to file <output>."
        );

        var configOption = new Option<string>(
            ["-c", "--config"],
            description: "Conversion configuration: [s2t|s2tw|s2twp|s2hk|t2s|tw2s|tw2sp|hk2s|jp2t|t2jp]"
        )
        {
            IsRequired = true // Mark as required
        };

        // Add validation for config values
        configOption.AddValidator(result =>
        {
            if (result.GetValueForOption(configOption) is { } configValue && !ConfigList.Contains(configValue))
            {
                result.ErrorMessage =
                    $"Invalid config '{configValue}'. Valid options are: {string.Join(", ", ConfigList)}";
            }
        });

        var punctOption = new Option<bool>(
            ["-p", "--punct"],
            getDefaultValue: () => false, // Default value
            description: "Punctuation conversion: True|False"
        );

        var inputEncodingOption = new Option<string>(
            name: "--in-enc",
            getDefaultValue: () => "UTF-8", // Default value
            description: "Encoding for input: [UTF-8|UNICODE|GBK|GB2312|BIG5|Shift-JIS]"
        );

        var outputEncodingOption = new Option<string>(
            name: "--out-enc",
            getDefaultValue: () => "UTF-8", // Default value
            description: "Encoding for output: [UTF-8|UNICODE|GBK|GB2312|BIG5|Shift-JIS]"
        );

        var officeOption = new Option<bool>(
            "--office",
            getDefaultValue: () => false,
            description: "Convert Office documents (.docx | .xlsx | .pptx | .odt | .ods | .odp)"
        );

        var formatOption = new Option<string?>(
            "--format",
            description: "Force Office document format: docx | xlsx | pptx | odt | ods | odp"
        );

        formatOption.AddValidator(result =>
        {
            var formatValue = result.GetValueForOption(formatOption);
            var officeValue = result.GetValueForOption(officeOption);

            if (string.IsNullOrEmpty(formatValue)) return;
            if (!officeValue)
            {
                result.ErrorMessage = "--format can only be used together with --office.";
            }
            else if (!OfficeFormats.Contains(formatValue))
            {
                result.ErrorMessage =
                    $"Invalid format '{formatValue}'. Valid options are: {string.Join(" | ", OfficeFormats)}";
            }
        });

        var keepFontOption = new Option<bool>(
            "--keep-font",
            getDefaultValue: () => true,
            description: "Preserve original font names in Office documents during conversion.\n" +
                         "Default: true. To disable, use: --keep-font:false"
        );

        var autoExtOption = new Option<bool>(
            "--auto-ext",
            getDefaultValue: () => true,
            description: "Automatically append correct Office document extension to output file if missing (e.g., .docx, .xlsx).\n" +
                         "Default: true. To disable, use: --auto-ext:false"
        );

        var convertCommand = new Command("convert", $"{Blue}Convert text using OpenccNetLib configurations.{Reset}")
        {
            inputFileOption,
            outputFileOption,
            configOption,
            punctOption,
            inputEncodingOption,
            outputEncodingOption,
            officeOption,
            formatOption,
            keepFontOption,
            autoExtOption
        };

        // Set the handler for the convert command
        convertCommand.SetHandler(async (context) =>
        {
            var inputFile = context.ParseResult.GetValueForOption(inputFileOption);
            var outputFile = context.ParseResult.GetValueForOption(outputFileOption);
            var config = context.ParseResult.GetValueForOption(configOption)!; // Config is required, so can use !
            var punct = context.ParseResult.GetValueForOption(punctOption);
            var inputEncoding = context.ParseResult.GetValueForOption(inputEncodingOption)!;
            var outputEncoding = context.ParseResult.GetValueForOption(outputEncodingOption)!;
            var office = context.ParseResult.GetValueForOption(officeOption);
            var format = context.ParseResult.GetValueForOption(formatOption);
            var keepFont = context.ParseResult.GetValueForOption(keepFontOption);
            var autoExt = context.ParseResult.GetValueForOption(autoExtOption);

            var exitCode =
                await RunConversionAsync(inputFile, outputFile, config, punct, inputEncoding, outputEncoding, office,
                    format, keepFont, autoExt);
            context.ExitCode = exitCode;
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
        bool office,
        string? format,
        bool keepFont,
        bool autoExt)
    {
        if (string.IsNullOrEmpty(config))
        {
            lock (ConsoleLock)
            {
                Console.Error.WriteLine("Error: Conversion configuration is required.");
                Console.Error.WriteLine($"Valid values are: {string.Join(", ", ConfigList)}");
            }

            return 1;
        }

        if (office)
        {
            if (string.IsNullOrEmpty(inputFile) && string.IsNullOrEmpty(outputFile))
            {
                await Console.Error.WriteLineAsync("❌ Input and output files are missing.");
                return 1;
            }

            if (string.IsNullOrEmpty(inputFile))
            {
                await Console.Error.WriteLineAsync("❌ Input file is missing.");
                return 1;
            }

            // Auto-assign output if missing
            if (string.IsNullOrEmpty(outputFile))
            {
                var inputName = Path.GetFileNameWithoutExtension(inputFile);
                var inputDir = Path.GetDirectoryName(inputFile) ?? Directory.GetCurrentDirectory();

                var ext = (autoExt && !string.IsNullOrEmpty(format) && OfficeFormats.Contains(format))
                    ? $".{format}"
                    : Path.GetExtension(inputFile); // default to match input

                outputFile = Path.Combine(inputDir, $"{inputName}_converted{ext}");

                await Console.Error.WriteLineAsync($"ℹ️ Output file not specified. Using: {outputFile}");
            }

            if (string.IsNullOrEmpty(format))
            {
                var fileExt = Path.GetExtension(inputFile).ToLowerInvariant();
                if (!OfficeFormats.Contains(fileExt[1..]))
                {
                    await Console.Error.WriteLineAsync($"❌ Invalid Office file extension: {fileExt}");
                    await Console.Error.WriteLineAsync(
                        $"   Valid extensions: .docx | .xlsx | .pptx | .odt | .ods | .odp");
                    return 1;
                }

                format = fileExt[1..];
            }

            if (office && autoExt && !string.IsNullOrWhiteSpace(outputFile))
            {
                var ext = Path.GetExtension(outputFile);
                if (string.IsNullOrEmpty(ext) && OfficeFormats.Contains(format))
                {
                    outputFile += $".{format}";
                    await Console.Error.WriteLineAsync($"ℹ️ Auto-extension applied: {outputFile}");
                }
            }

            try
            {
                var (success, message) = await OfficeDocModel.ConvertOfficeDocAsync(
                    inputFile,
                    outputFile,
                    format,
                    new Opencc(config),
                    punct,
                    keepFont
                );

                var status = success
                    ? message + $"\n📁 Output saved to: {Path.GetFullPath(outputFile)}"
                    : $"❌ Conversion failed: {message}";
                await Console.Error.WriteLineAsync(status);

                return success ? 0 : 1;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"❌ Error during Office document conversion: {ex.Message}");
                return 1;
            }
        }

        try
        {
            var inputStr = await ReadInputAsync(inputFile, inputEncoding);
            // Assuming OpenccNetLib provides a way to initialize Opencc with a config string
            var opencc = new Opencc(config);
            var outputStr = opencc.Convert(inputStr, punct);
            await WriteOutputAsync(outputFile, outputStr, outputEncoding);

            var inFrom = inputFile ?? "<stdin>";
            var outTo = outputFile ?? "<stdout>";
            lock (ConsoleLock)
            {
                Console.Error.WriteLine($"Conversion completed ({config}): {inFrom} -> {outTo}");
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

        lock (ConsoleLock)
        {
            Console.Error.WriteLine(
                "Input text to convert, <Ctrl+Z> (Windows) or <Ctrl+D> (Unix) then Enter to submit:");
        }

        using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.GetEncoding(inputEncoding));
        return await reader.ReadToEndAsync();
    }

    private static async Task WriteOutputAsync(string? outputFile, string outputStr, string outputEncoding)
    {
        // Normalize outputEncoding to lowercase for easier comparison
        var normalizedOutputEncoding = outputEncoding.ToLowerInvariant();

        var encoding = normalizedOutputEncoding switch
        {
            "utf-8" => new UTF8Encoding(false) // No BOM
            ,
            "utf-16le" or "unicode" => // "Unicode" often maps to UTF-16LE in .NET
                new UnicodeEncoding(false, false) // false for bigEndian, false for emitBOM
            ,
            "utf-16be" => new UnicodeEncoding(true, false) // true for bigEndian, false for emitBOM
            ,
            "utf-32" => new UTF32Encoding(false, false) // false for bigEndian, false for emitBOM
            ,
            _ => Encoding.GetEncoding(outputEncoding)
        };

        if (outputFile != null)
        {
            await File.WriteAllTextAsync(outputFile, outputStr, encoding);
        }
        else
        {
            lock (ConsoleLock)
            {
                // For console output, use Console.Write/WriteLine directly.
                Console.Out.WriteLine(outputStr);
            }
        }
    }
}