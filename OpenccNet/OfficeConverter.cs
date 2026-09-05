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
    /// <param name="format">
    /// The Office or EPUB format name to validate, such as <c>docx</c>, <c>xlsx</c>,
    /// <c>pptx</c>, <c>odt</c>, <c>ods</c>, <c>odp</c>, or <c>epub</c>.
    /// </param>
    /// <returns>
    /// <c>true</c> when <paramref name="format"/> is supported; otherwise, <c>false</c>.
    /// </returns>
    public static bool IsValidOfficeFormat(string? format)
    {
        return format is not null &&
               OfficeDocConverter.IsSupportedFormat(format);
    }

    /// <summary>
    /// Converts an Office or EPUB container entirely in memory through
    /// OpenccNetLib's entry-by-entry package pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The input and output containers are represented as <c>byte[]</c>.
    /// <see cref="OfficeDocConverter"/> processes the ZIP package directly in
    /// memory without extracting it to a temporary working directory.
    /// Convertible XML/XHTML content is transformed through
    /// <paramref name="textConverter"/>, while non-target package entries are
    /// copied unchanged into the rebuilt container.
    /// </para>
    /// <para>
    /// EPUB output preserves the required package layout, including an
    /// uncompressed <c>mimetype</c> entry written first.
    /// </para>
    /// <para>
    /// Cancellation is honored by the underlying async wrapper before
    /// synchronous package conversion begins. Once conversion is running,
    /// it continues to completion.
    /// </para>
    /// </remarks>
    /// <param name="inputBytes">
    /// Raw bytes of the source Office or EPUB container.
    /// </param>
    /// <param name="format">
    /// The document format, such as <c>docx</c>, <c>xlsx</c>, <c>pptx</c>,
    /// <c>odt</c>, <c>ods</c>, <c>odp</c>, or <c>epub</c>.
    /// </param>
    /// <param name="textConverter">
    /// Caller-supplied transformation applied to text-bearing document content.
    /// </param>
    /// <param name="keepFont">
    /// Whether supported font declarations should be preserved during conversion.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel the operation before the underlying synchronous
    /// conversion begins.
    /// </param>
    /// <returns>
    /// A tuple containing the success flag, a status message, and the converted
    /// package bytes. <c>OutputBytes</c> is <c>null</c> when conversion fails.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="inputBytes"/> or
    /// <paramref name="textConverter"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is canceled before
    /// conversion begins.
    /// </exception>
    public static async Task<(bool Success, string Message, byte[]? OutputBytes)>
        ConvertOfficeBytesAsync(
            byte[] inputBytes,
            string format,
            OfficeTextConverter textConverter,
            bool keepFont = false,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputBytes);
        ArgumentNullException.ThrowIfNull(textConverter);

        if (!IsValidOfficeFormat(format))
            return (false, $"Unsupported or invalid format: {format}", null);

        try
        {
            var outputBytes =
                await OfficeDocConverter.ConvertOfficeBytesAsync(
                        inputBytes,
                        format,
                        textConverter,
                        keepFont,
                        cancellationToken)
                    .ConfigureAwait(false);

            return (
                true,
                $"Successfully converted {format.ToLowerInvariant()} document.",
                outputBytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (
                false,
                $"Conversion failed: {ex.Message}",
                null);
        }
    }

    /// <summary>
    /// Converts an Office or EPUB file through OpenccNetLib's streaming package
    /// pipeline and atomically publishes the completed output file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="OfficeDocConverter"/> owns package parsing, text-bearing entry
    /// selection, ZIP reconstruction, format-specific handling, validation, and
    /// atomic output publication.
    /// </para>
    /// <para>
    /// Text content selected by the package layer is transformed through
    /// <paramref name="textConverter"/>. The CLI can therefore compose
    /// normalization, OpenCC conversion, DeTofu, or other text policies without
    /// coupling them to Office or EPUB package mechanics.
    /// </para>
    /// <para>
    /// Cancellation is honored by the underlying async wrapper before
    /// synchronous package conversion begins. Once conversion is running,
    /// it continues to completion.
    /// </para>
    /// </remarks>
    /// <param name="inputPath">
    /// Path to the source Office or EPUB document.
    /// </param>
    /// <param name="outputPath">
    /// Path where the converted document should be written.
    /// </param>
    /// <param name="format">
    /// The document format, such as <c>docx</c>, <c>xlsx</c>, <c>pptx</c>,
    /// <c>odt</c>, <c>ods</c>, <c>odp</c>, or <c>epub</c>.
    /// </param>
    /// <param name="textConverter">
    /// Caller-supplied transformation applied to text-bearing document content.
    /// </param>
    /// <param name="keepFont">
    /// Whether supported font declarations should be preserved during conversion.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel the operation before the underlying synchronous
    /// conversion begins.
    /// </param>
    /// <returns>
    /// A tuple containing the success flag and a status message.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="inputPath"/>,
    /// <paramref name="outputPath"/>, or
    /// <paramref name="textConverter"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is canceled before
    /// conversion begins.
    /// </exception>
    public static async Task<(bool Success, string Message)>
        ConvertOfficeDocAsync(
            string inputPath,
            string outputPath,
            string format,
            OfficeTextConverter textConverter,
            bool keepFont = false,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputPath);
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(textConverter);

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
                    textConverter,
                    keepFont,
                    cancellationToken)
                .ConfigureAwait(false);

            return (
                true,
                $"Successfully converted {format.ToLowerInvariant()} document.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (
                false,
                $"Conversion failed: {ex.Message}");
        }
    }
}