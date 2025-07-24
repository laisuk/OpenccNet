using System.CommandLine;
using System.Text;
using OpenccNetLib;

namespace OpenccNet;

internal static class ConvertCommand
{
    private const string Blue = "\u001b[1;34m";
    private const string Reset = "\u001b[0m";
    private static readonly object ConsoleLock = new();

    private static readonly HashSet<string> ConfigList = new(StringComparer.Ordinal)
    {
        "s2t", "t2s", "s2tw", "tw2s", "s2twp", "tw2sp", "s2hk", "hk2s", "t2tw", "tw2t", "t2twp", "tw2tp",
        "t2hk", "hk2t", "t2jp", "jp2t"
    };

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
            Description = "Conversion configuration: s2t|s2tw|s2twp|s2hk|t2s|tw2s|tw2sp|hk2s|jp2t|t2jp"
        };

        configOption.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>();
            if (!string.IsNullOrEmpty(value) && !ConfigList.Contains(value))
            {
                result.AddError($"Invalid config '{value}'. Valid options: {string.Join(", ", ConfigList)}");
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

        var convertCommand = new Command("convert", $"{Blue}Convert text using OpenccNetLib configurations.{Reset}")
        {
            inputFileOption,
            outputFileOption,
            configOption,
            punctOption,
            inputEncodingOption,
            outputEncodingOption,
        };

        convertCommand.SetAction(async (pr, _) =>
        {
            var inputFile = pr.GetValue(inputFileOption);
            var outputFile = pr.GetValue(outputFileOption);
            var config = pr.GetValue(configOption)!;
            var punct = pr.GetValue(punctOption);
            var inputEnc = pr.GetValue(inputEncodingOption)!;
            var outputEnc = pr.GetValue(outputEncodingOption)!;

            return await RunConversionAsync(
                inputFile, outputFile, config, punct, inputEnc, outputEnc
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
        string outputEncoding)
    {
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
                Console.Error.WriteLine($"✅ Conversion ({config}): {inFrom} → {outTo}");
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
            Console.WriteLine(content);
    }
}