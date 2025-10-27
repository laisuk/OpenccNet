using System.CommandLine;
using System.Text;
using OpenccNetLib;

namespace OpenccNet;

internal static class OfficeCommand
{
    private const string Blue = "\u001b[1;34m";
    private const string Reset = "\u001b[0m";

    private static readonly HashSet<string> ConfigList = new(StringComparer.Ordinal)
    {
        "s2t", "t2s", "s2tw", "tw2s", "s2twp", "tw2sp", "s2hk", "hk2s", "t2tw", "tw2t", "t2twp", "tw2tp",
        "t2hk", "hk2t", "t2jp", "jp2t"
    };

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

        var formatOption = new Option<string?>("--format", "-f")
        {
            Description = "Force Office document format: docx | xlsx | pptx | odt | ods | odp | epub"
        };

        formatOption.Validators.Add(result =>
        {
            var format = result.GetValueOrDefault<string>();

            if (!string.IsNullOrWhiteSpace(format) && !OfficeConverter.OfficeFormats.Contains(format))
                result.AddError(
                    $"Invalid format '{format}'. Valid: {string.Join(" | ", OfficeConverter.OfficeFormats)})");
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

        var officeCommand = new Command("office", $"{Blue}Convert Office documents or Epub using OpenccNetLib.{Reset}")
        {
            inputFileOption, outputFileOption, configOption, punctOption,
            formatOption, keepFontOption, autoExtOption
        };

        officeCommand.SetAction(async (pr, _) =>
        {
            var input = pr.GetValue(inputFileOption);
            var output = pr.GetValue(outputFileOption);
            var config = pr.GetValue(configOption)!;
            var punct = pr.GetValue(punctOption);
            var format = pr.GetValue(formatOption);
            var keepFont = pr.GetValue(keepFontOption);
            var autoExt = pr.GetValue(autoExtOption);

            if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
            {
                await Console.Error.WriteLineAsync("❌ Input file does not exist.");
                return 1;
            }

            var resolvedFormat = format ?? Path.GetExtension(input).TrimStart('.').ToLowerInvariant();
            if (!OfficeConverter.IsValidOfficeFormat(resolvedFormat))
            {
                await Console.Error.WriteLineAsync(
                    $"❌ Unsupported file format. Supported: {string.Join(", ", OfficeConverter.OfficeFormats)}");
                return 1;
            }

            var resolvedOutput = output ?? Path.Combine(
                Path.GetDirectoryName(input) ?? "",
                $"{Path.GetFileNameWithoutExtension(input)}_converted.{resolvedFormat}"
            );

            if (autoExt &&
                !string.Equals(Path.GetExtension(resolvedOutput), "." + resolvedFormat,
                    StringComparison.OrdinalIgnoreCase))
            {
                resolvedOutput = Path.ChangeExtension(resolvedOutput, resolvedFormat);
                await Console.Error.WriteLineAsync($"ℹ️ Output file extension adjusted to: {resolvedOutput}");
            }

            try
            {
                var builder = new OfficeConverterBuilder()
                    .SetInput(input)
                    .SetOutput(resolvedOutput)
                    .SetFormat(resolvedFormat)
                    .UseConverter(new Opencc(config))
                    .WithPunctuation(punct)
                    .KeepFontNames(keepFont);

                var (success, message) = await builder.ConvertAsync();

                var status = success
                    ? $"{message}\n📁 Output: {Path.GetFullPath(resolvedOutput)}"
                    : $"❌ Office document conversion failed: {message}";
                await Console.Error.WriteLineAsync(status);
                return success ? 0 : 1;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"❌ Exception: {ex.Message}");
                return 1;
            }
        });

        return officeCommand;
    }
}