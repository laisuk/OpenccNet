using OpenccNetLib;

namespace OpenccNet;

public class OfficeConverterBuilder
{
    private string? _inputPath;
    private string? _outputPath;
    private string? _format;
    private Opencc? _converter;
    private bool _punctuation;
    private bool _keepFont;

    public OfficeConverterBuilder SetInput(string inputPath)
    {
        _inputPath = inputPath;
        return this;
    }

    public OfficeConverterBuilder SetOutput(string outputPath)
    {
        _outputPath = outputPath;
        return this;
    }

    public OfficeConverterBuilder SetFormat(string format)
    {
        _format = format;
        return this;
    }

    public OfficeConverterBuilder UseConverter(Opencc converter)
    {
        _converter = converter;
        return this;
    }

    public OfficeConverterBuilder WithPunctuation(bool value = false)
    {
        _punctuation = value;
        return this;
    }

    public OfficeConverterBuilder KeepFontNames(bool value = false)
    {
        _keepFont = value;
        return this;
    }

    public async Task<(bool Success, string Message)> ConvertAsync()
    {
        if (string.IsNullOrEmpty(_inputPath) ||
            string.IsNullOrEmpty(_outputPath) ||
            string.IsNullOrEmpty(_format) ||
            _converter == null)
        {
            return (false, "❌ Missing required parameters. Ensure input, output, format, and converter are set.");
        }

        return await OfficeConverter.ConvertOfficeDocAsync(
            _inputPath, _outputPath, _format, _converter, _punctuation, _keepFont
        );
    }
}