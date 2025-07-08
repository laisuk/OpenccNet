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
        "docx", "xlsx", "pptx", "odt", "ods", "odp", "epub"
    };

    // Only the changes around CreateCommand() are shown. All RunConversionAsync etc. remain unchanged

    internal static Command CreateCommand()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

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
            Description = "Encoding for input: UTF-8|UNICODE|GBK|GB2312|BIG5|Shift-JIS [default: UTF-8]"
        };

        var outputEncodingOption = new Option<string>("--out-enc")
        {
            DefaultValueFactory = _ => "UTF-8",
            Description = "Encoding for output: UTF-8|UNICODE|GBK|GB2312|BIG5|Shift-JIS [default: UTF-8]"
        };

        var officeOption = new Option<bool>("--office")
        {
            DefaultValueFactory = _ => false,
            Description = "Convert Office documents (.docx | .xlsx | .pptx | .odt | .ods | .odp | .epub) [default: False]"
        };

        var formatOption = new Option<string?>("--format")
        {
            Description = "Force Office document format: docx | xlsx | pptx | odt | ods | odp | epub"
        };

        formatOption.Validators.Add(result =>
        {
            var format = result.GetValueOrDefault<string>();
            var office = result.Parent!.GetValue(officeOption);

            if (string.IsNullOrEmpty(format)) return;
            if (!office)
                result.AddError("--format can only be used with --office.");
            else if (!OfficeFormats.Contains(format))
                result.AddError($"Invalid format '{format}'. Valid: {string.Join(" | ", OfficeFormats)})");
        });

        var keepFontOption = new Option<bool>("--keep-font")
        {
            DefaultValueFactory = _ => true,
            Description = "Preserve font names in Office documents [default: true]. Use --keep-font:false to disable."
        };

        var autoExtOption = new Option<bool>("--auto-ext")
        {
            DefaultValueFactory = _ => true,
            Description =
                "Auto append correct extension to Office output files [default: true]. Use --auto-ext:false to disable."
        };

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

        convertCommand.SetAction(async (pr, _) =>
        {
            var inputFile = pr.GetValue(inputFileOption);
            var outputFile = pr.GetValue(outputFileOption);
            var config = pr.GetValue(configOption)!;
            var punct = pr.GetValue(punctOption);
            var inputEnc = pr.GetValue(inputEncodingOption)!;
            var outputEnc = pr.GetValue(outputEncodingOption)!;
            var office = pr.GetValue(officeOption);
            var format = pr.GetValue(formatOption);
            var keepFont = pr.GetValue(keepFontOption);
            var autoExt = pr.GetValue(autoExtOption);

            return await RunConversionAsync(
                inputFile, outputFile, config, punct, inputEnc, outputEnc,
                office, format, keepFont, autoExt
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
        bool office,
        string? format,
        bool keepFont,
        bool autoExt)
    {
        if (office)
        {
            if (string.IsNullOrEmpty(inputFile) && string.IsNullOrEmpty(outputFile))
            {
                await Console.Error.WriteLineAsync("❌ Input and output files are missing.");
                return 1;
            }
            
            if (string.IsNullOrEmpty(inputFile) || !File.Exists(inputFile))
            {
                await Console.Error.WriteLineAsync("❌ Input file is missing or does not exist.");
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
                        $"   Valid extensions: .docx | .xlsx | .pptx | .odt | .ods | .odp | epub");
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
                    : $"❌ Office document conversion failed: {message}";
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