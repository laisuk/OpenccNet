using OpenccNetLib;

namespace OpenccNet;

/// <summary>
/// Adapts the shared <see cref="OfficeDocConverter"/> implementation for CLI use.
/// The document conversion pipeline lives in OpenccNetLib so the CLI does not
/// maintain a second Office/EPUB extractor and package rebuilder.
/// </summary>
public static class OfficeConverter
{
    /// <summary>
    /// Gets the Office and EPUB formats supported by OpenccNetLib.
    /// </summary>
    public static IReadOnlyCollection<string> OfficeFormats =>
        OfficeDocConverter.SupportedFormats;

    /// <summary>
    /// Determines whether the supplied format is supported by OpenccNetLib.
    /// </summary>
    public static bool IsValidOfficeFormat(string? format)
    {
        return format is not null && OfficeDocConverter.IsSupportedFormat(format);
    }

    /// <summary>
    /// Converts an Office or EPUB file through OpenccNetLib's in-memory,
    /// entry-by-entry package pipeline and atomically publishes the output file.
    /// </summary>
    public static async Task<(bool Success, string Message)> ConvertOfficeDocAsync(
        string inputPath,
        string outputPath,
        string format,
        Opencc converter,
        bool punctuation = false,
        bool keepFont = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputPath);
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(converter);

        if (!File.Exists(inputPath))
            return (false, $"Input file not found: {inputPath}");

        if (!IsValidOfficeFormat(format))
            return (false, $"Unsupported or invalid format: {format}");

        try
        {
            await OfficeDocConverter.ConvertOfficeFileAsync(
                    inputPath,
                    outputPath,
                    format,
                    converter,
                    punctuation,
                    keepFont,
                    cancellationToken)
                .ConfigureAwait(false);

            return (true, $"Successfully converted {format.ToLowerInvariant()} document.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, $"Conversion failed: {ex.Message}");
        }
    }
}