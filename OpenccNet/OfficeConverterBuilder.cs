using OpenccNetLib;

namespace OpenccNet;

/// <summary>
/// A builder-style class to configure and perform Office document conversion
/// using OpenCC with options such as punctuation handling and font name preservation.
/// </summary>
public class OfficeConverterBuilder
{
    private string? _inputPath;
    private string? _outputPath;
    private string? _format;
    private Opencc? _converter;
    private bool _punctuation;
    private bool _keepFont;

    /// <summary>
    /// Sets the full path to the input Office document (e.g., .docx, .odt, .epub).
    /// </summary>
    /// <param name="inputPath">The full file path to the input document.</param>
    /// <returns>The builder instance for chaining.</returns>
    public OfficeConverterBuilder SetInput(string inputPath)
    {
        _inputPath = inputPath;
        return this;
    }

    /// <summary>
    /// Sets the full path to the output file where the converted document will be saved.
    /// </summary>
    /// <param name="outputPath">The full file path to the output document.</param>
    /// <returns>The builder instance for chaining.</returns>
    public OfficeConverterBuilder SetOutput(string outputPath)
    {
        _outputPath = outputPath;
        return this;
    }

    /// <summary>
    /// Sets the document format to be converted.
    /// Valid values: "docx", "xlsx", "pptx", "odt", "ods", "odp", "epub".
    /// </summary>
    /// <param name="format">The format string, case-insensitive.</param>
    /// <returns>The builder instance for chaining.</returns>
    public OfficeConverterBuilder SetFormat(string format)
    {
        _format = format;
        return this;
    }

    /// <summary>
    /// Sets the OpenCC converter instance to use for text conversion.
    /// </summary>
    /// <param name="converter">An instance of <see cref="Opencc"/> configured with a valid OpenCC config.</param>
    /// <returns>The builder instance for chaining.</returns>
    public OfficeConverterBuilder UseConverter(Opencc converter)
    {
        _converter = converter;
        return this;
    }

    /// <summary>
    /// Enables or disables punctuation conversion during document transformation.
    /// </summary>
    /// <param name="value">True to convert punctuation; false to leave it unchanged (default: false).</param>
    /// <returns>The builder instance for chaining.</returns>
    public OfficeConverterBuilder WithPunctuation(bool value = false)
    {
        _punctuation = value;
        return this;
    }

    /// <summary>
    /// Enables or disables font name preservation during conversion.
    /// When enabled, font names will be temporarily replaced with placeholders and restored post-conversion.
    /// </summary>
    /// <param name="value">True to preserve original font names (default: false).</param>
    /// <returns>The builder instance for chaining.</returns>
    public OfficeConverterBuilder KeepFontNames(bool value = false)
    {
        _keepFont = value;
        return this;
    }

    /// <summary>
    /// Executes the conversion using the configured parameters.
    /// </summary>
    /// <returns>
    /// A tuple containing a success flag and a message describing the result or error.
    /// </returns>
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