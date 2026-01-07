using System.CommandLine;
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

        var configOption = new Option<string>("--config", "-c")
        {
            // Required = true,
            Description =
                "Conversion configuration.\nValid options: " +
                string.Join(", ", Opencc.GetSupportedConfigs())
        };

        configOption.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>();
            if (!string.IsNullOrEmpty(value) && !Opencc.IsValidConfig(value))
            {
                result.AddError(
                    $"Invalid config '{value}'. Valid options: {string.Join(", ", Opencc.GetSupportedConfigs())}"
                );
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

        var quietOption = new Option<bool>("--quiet", "-q")
        {
            DefaultValueFactory = _ => false,
            Description = "Suppress status and progress output; only errors will be shown."
        };

        var extractOption = new Option<bool>("--extract", "-e")
        {
            DefaultValueFactory = _ => false,
            Description = "Extract text from PDF only (no OpenCC conversion)."
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
            compactOption,
            quietOption,
            extractOption,
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
            var quiet = pr.GetValue(quietOption);
            var extract = pr.GetValue(extractOption);

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

            if (!extract)
            {
                if (string.IsNullOrWhiteSpace(config))
                {
                    await Console.Error.WriteLineAsync("❌ Missing --config (required unless --extract is used).");
                    return 1;
                }
            }
            else
            {
                if (!quiet && !string.IsNullOrEmpty(config))
                    await Console.Error.WriteLineAsync("ℹ️ --config is ignored in --extract mode.");

                if (!quiet && punct)
                    await Console.Error.WriteLineAsync("ℹ️ --punct has no effect in --extract mode.");
            }


            if (compact && !reflow && !quiet)
            {
                await Console.Error.WriteLineAsync("ℹ️ --compact has no effect without --reflow; ignoring.");
            }

            var resolvedOutput = output ?? Path.Combine(
                Path.GetDirectoryName(input) ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(input)}" +
                (extract ? "_extracted.txt" : "_converted.txt"));

            try
            {
                if (!quiet)
                    await Console.Error.WriteLineAsync("⏳ Processing PDF… please wait…");

                // 1) Extract text via PdfHelper (PdfPig, pure C#)
                var extractedText = await PdfHelper.LoadPdfTextAsync(
                    filename: input,
                    addPdfPageHeader: addHeader,
                    statusCallback: status =>
                    {
                        // simple status logging to stderr
                        // Console.Error.WriteLine(status);
                        if (!quiet)
                            Console.Error.Write("\r" + status);
                    },
                    cancellationToken: cancellationToken);

                var finalText = extractedText;

                // 2) Optional CJK paragraph reflow
                if (reflow)
                {
                    finalText = ReflowHelper.ReflowCjkParagraphs(
                        finalText,
                        addPdfPageHeader: addHeader,
                        compact: compact);
                }

                // 3) OpenCC conversion (only if not extract)
                if (!extract)
                {
                    var converter = new Opencc(config);
                    finalText = converter.Convert(finalText, punctuation: punct);
                }

                // 4) Save UTF-8
                await File.WriteAllTextAsync(
                    resolvedOutput,
                    finalText,
                    new UTF8Encoding(false),
                    cancellationToken);

                if (!quiet)
                {
                    await Console.Error.WriteLineAsync(
                        $"\n✅ PDF {(extract ? "extraction" : "conversion")} succeeded.\n📁 Output: {Path.GetFullPath(resolvedOutput)}");
                }

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