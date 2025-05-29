using System.CommandLine;
using System.Text;
using OpenccNetLib; // Assuming OpenccNetLib is still a valid dependency

namespace OpenccConvert;

internal static class OpenccConvert
{
    private static readonly object ConsoleLock = new();

    private static async Task<int> Main(string[] args)
    {
        // Register the CodePages encoding provider at application startup to enable using single and double byte encodings.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Set to UTF-8 explicitly for console input/output.
        // Note: System.CommandLine generally handles encoding well, but explicit setting for console can be useful.
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        // Define options
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

        // You could add validation for config values if needed:
        configOption.AddValidator(result =>
        {
            var validConfigs = new HashSet<string>
                { "s2t", "s2tw", "s2twp", "s2hk", "t2s", "tw2s", "tw2sp", "hk2s", "jp2t", "t2jp" };
            if (!validConfigs.Contains(result.GetValueForOption(configOption)!))
            {
                result.ErrorMessage =
                    $"Invalid config '{result.GetValueForOption(configOption)}'. Valid options are: {string.Join(", ", validConfigs)}";
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

        // Create a root command
        var rootCommand = new RootCommand("OpenCC Converter for command-line text conversion.")
        {
            inputFileOption,
            outputFileOption,
            configOption,
            punctOption,
            inputEncodingOption,
            outputEncodingOption
        };

        // Set the handler for the root command
        rootCommand.SetHandler(async (context) =>
        {
            var inputFile = context.ParseResult.GetValueForOption(inputFileOption);
            var outputFile = context.ParseResult.GetValueForOption(outputFileOption);
            var config = context.ParseResult.GetValueForOption(configOption)!; // Config is required, so can use !
            var punct = context.ParseResult.GetValueForOption(punctOption);
            var inputEncoding = context.ParseResult.GetValueForOption(inputEncodingOption)!;
            var outputEncoding = context.ParseResult.GetValueForOption(outputEncodingOption)!;

            int exitCode =
                await RunConversionAsync(inputFile, outputFile, config, punct, inputEncoding, outputEncoding);
            context.ExitCode = exitCode;
        });

        // Invoke the command line parser
        return await rootCommand.InvokeAsync(args);
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
                Console.Error.WriteLine("Please set conversion configuration.");
            }

            return 1;
        }

        try
        {
            var inputStr = await ReadInputAsync(inputFile, inputEncoding);
            var opencc = new Opencc(config); // OpenccNetLib library instance
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
        var encoding = outputEncoding.Equals("utf-8", StringComparison.InvariantCultureIgnoreCase)
            ? new UTF8Encoding(false) // false = no BOM
            : Encoding.GetEncoding(outputEncoding);

        if (outputFile != null)
        {
            await File.WriteAllTextAsync(outputFile, outputStr, encoding);
        }
        else
        {
            lock (ConsoleLock)
            {
                // For console output, use Console.Write/WriteLine directly.
                // It's generally UTF-8 by default in modern .NET console apps,
                // but we explicitly set it earlier for consistency.
                Console.Error.Write(outputStr); // Writing to Error stream as per original code for stdout
            }
        }
    }
}