using System.CommandLine;
using System.Text;
using OpenccNetLib;

namespace OpenccNet;

internal static class PdfCommand
{
    private const string Blue = "\u001b[1;34m";
    private const string Reset = "\u001b[0m";

    // Same config set as ConvertCommand / OfficeCommand
    private static readonly HashSet<string> ConfigList = new(StringComparer.Ordinal)
    {
        "s2t", "t2s", "s2tw", "tw2s", "s2twp", "tw2sp", "s2hk", "hk2s",
        "t2tw", "tw2t", "t2twp", "tw2tp", "t2hk", "hk2t", "t2jp", "jp2t"
    };

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
            Description = "Enable punctuation conversion."
        };

        // Use -H so -h stays as the global help alias
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

        var compactOption = new Option<bool>("--compact")
        {
            DefaultValueFactory = _ => false,
            Description = "Use compact reflow (fewer blank lines between paragraphs). Only meaningful with --reflow."
        };

        var pdfCommand = new Command(
            "pdf",
            $"{Blue}Convert a PDF to UTF-8 text using PdfPig + OpenccNetLib, with optional CJK paragraph reflow.{Reset}")
        {
            inputFileOption,
            outputFileOption,
            configOption,
            punctOption,
            headerOption,
            reflowOption,
            compactOption
        };

        pdfCommand.SetAction(async (pr, cancellationToken) =>
        {
            var input = pr.GetValue(inputFileOption);
            var output = pr.GetValue(outputFileOption);
            var config = pr.GetValue(configOption)!;
            var punct = pr.GetValue(punctOption);
            var addHeader = pr.GetValue(headerOption);
            var reflow = pr.GetValue(reflowOption);
            var compact = pr.GetValue(compactOption);

            if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
            {
                await Console.Error.WriteLineAsync("❌ Input file does not exist.");
                return 1;
            }

            if (!string.Equals(Path.GetExtension(input), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                await Console.Error.WriteLineAsync("❌ Input file must be a .pdf file.");
                return 1;
            }

            if (compact && !reflow)
            {
                await Console.Error.WriteLineAsync("ℹ️ --compact has no effect without --reflow; ignoring.");
            }

            var resolvedOutput = output ?? Path.Combine(
                Path.GetDirectoryName(input) ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(input)}_converted.txt");

            try
            {
                await Console.Error.WriteLineAsync("⏳ Processing PDF… please wait…");

                // 1) Extract text via PdfHelper (PdfPig, pure C#)
                var extractedText = await PdfHelper.LoadPdfTextAsync(
                    filename: input,
                    addPdfPageHeader: addHeader,
                    statusCallback: status =>
                    {
                        // simple status logging to stderr
                        // Console.Error.WriteLine(status);
                        Console.Error.Write("\r" + status);
                    },
                    cancellationToken: cancellationToken);

                // 2) Optional CJK paragraph reflow
                if (reflow)
                {
                    extractedText =
                        PdfHelper.ReflowCjkParagraphs(extractedText, addPdfPageHeader: addHeader, compact: compact);
                }

                // 3) OpenCC conversion (config + punctuation)
                var converter = new Opencc(config);
                var convertedText = converter.Convert(extractedText, punctuation: punct);

                // 4) Save as UTF-8 (no BOM)
                await File.WriteAllTextAsync(
                    resolvedOutput,
                    convertedText,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken);

                await Console.Error.WriteLineAsync(
                    $"\n✅ PDF conversion succeeded.\n📁 Output: {Path.GetFullPath(resolvedOutput)}");
                return 0;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"❌ PDF conversion failed: {ex.Message}");
                return 1;
            }
        });

        return pdfCommand;
    }
}
