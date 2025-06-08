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

        var convertCommand = new Command("convert", $"{Blue}Convert text using OpenccNetLib configurations.{Reset}")
        {
            inputFileOption,
            outputFileOption,
            configOption,
            punctOption,
            inputEncodingOption,
            outputEncodingOption
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

            var exitCode =
                await RunConversionAsync(inputFile, outputFile, config, punct, inputEncoding, outputEncoding);
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
        string outputEncoding)
    {
        if (string.IsNullOrEmpty(config))
        {
            lock (ConsoleLock)
            {
                Console.Error.WriteLine("Error: Conversion configuration is required.");
            }

            return 1;
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