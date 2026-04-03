using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using OpenccNetLib;

namespace OpenccNet;

/// <summary>
/// Provides functionality to convert Office document formats (.docx, .xlsx, .pptx, .odt, .ods, .odp)
/// using the Opencc converter with optional font name preservation.
/// </summary>
public static class OfficeConverter
{
    public static readonly HashSet<string> OfficeFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "docx", "xlsx", "pptx", "odt", "ods", "odp", "epub"
    };

    /// <summary>
    /// Matches an XLSX inline-string cell:
    /// <![CDATA[<c ... t="inlineStr" ...>...</c>]]>
    /// </summary>
    private static readonly Regex XlsxInlineStringCellRegex = new(
        "<c\\b(?=[^>]*\\bt=(?:\"inlineStr\"|'inlineStr'))[^>]*>.*?</c>",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    /// <summary>
    /// Matches a text node inside XLSX inline-string content:
    /// <![CDATA[<t ...>TEXT</t>]]>
    /// </summary>
    private static readonly Regex XlsxTextNodeRegex = new(
        "(<t\\b[^>]*>)(.*?)(</t>)",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    /// <summary>
    /// Determines whether the given file format is a supported Office or EPUB document format.
    /// </summary>
    /// <param name="format">
    /// The file format string to validate (e.g., "docx", "xlsx", "epub").
    /// The comparison is case-insensitive.
    /// </param>
    /// <returns>
    /// <c>true</c> if the format is one of the supported values ("docx", "xlsx", "pptx", "odt", "ods", "odp", "epub");
    /// otherwise, <c>false</c>.
    /// </returns>
    public static bool IsValidOfficeFormat(string? format)
    {
        return !string.IsNullOrWhiteSpace(format) && OfficeFormats.Contains(format);
    }

    /// <summary>
    /// Converts an Office document by applying OpenCC conversion on specific XML parts.
    /// Optionally preserves original font names to prevent them from being altered.
    /// </summary>
    /// <param name="inputPath">The full path to the input Office document (e.g., .docx).</param>
    /// <param name="outputPath">The desired full path to the converted output file.</param>
    /// <param name="format">The document format ("docx", "xlsx", "pptx", "odt", "ods", "odp", or "epub").</param>
    /// <param name="converter">The OpenCC converter instance used for conversion.</param>
    /// <param name="punctuation">Whether to convert punctuation during OpenCC transformation.</param>
    /// <param name="keepFont">If true, font names are preserved using placeholder markers during conversion.</param>
    /// <returns>A tuple indicating whether the conversion succeeded and a status message.</returns>
    public static async Task<(bool Success, string Message)> ConvertOfficeDocAsync(
        string inputPath,
        string outputPath,
        string format,
        Opencc converter,
        bool punctuation = false,
        bool keepFont = false)
    {
        ArgumentNullException.ThrowIfNull(inputPath);
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(converter);

        if (!File.Exists(inputPath))
            return (false, $"❌ Input file not found: {inputPath}");

        if (!IsValidOfficeFormat(format))
            return (false, $"❌ Unsupported or invalid format: {format}");

        var normalizedFormat = format.ToLowerInvariant();

        // Create a temporary working directory
        var tempDir = Path.Combine(Path.GetTempPath(), $"{normalizedFormat}_temp_" + Guid.NewGuid());

        try
        {
            // Extract the input Office archive into the temp folder
            var (ok, error) = ExtractZipSafely(inputPath, tempDir);
            if (!ok)
                return (false, $"Failed to extract input file: {error}");

            // Identify target XML files for each Office format
            var targetXmlPaths = normalizedFormat switch
            {
                "docx" => new List<string> { Path.Combine("word", "document.xml") },

                "xlsx" => CollectXlsxTargetXmlPaths(tempDir),

                "pptx" => Directory.Exists(Path.Combine(tempDir, "ppt"))
                    ? Directory.GetFiles(Path.Combine(tempDir, "ppt"), "*.xml", SearchOption.AllDirectories)
                        .Where(path =>
                            Path.GetFileName(path).StartsWith("slide", StringComparison.OrdinalIgnoreCase) ||
                            path.Contains("notesSlide", StringComparison.OrdinalIgnoreCase) ||
                            path.Contains("slideMaster", StringComparison.OrdinalIgnoreCase) ||
                            path.Contains("slideLayout", StringComparison.OrdinalIgnoreCase) ||
                            path.Contains("comment", StringComparison.OrdinalIgnoreCase))
                        .Select(path => Path.GetRelativePath(tempDir, path))
                        .ToList()
                    : new List<string>(),

                "odt" or "ods" or "odp" => new List<string> { "content.xml" },

                "epub" => Directory.Exists(tempDir)
                    ? Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories)
                        .Where(f =>
                            f.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".opf", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".ncx", StringComparison.OrdinalIgnoreCase))
                        .Select(f => Path.GetRelativePath(tempDir, f))
                        .ToList()
                    : new List<string>(),

                _ => null
            };

            // Check for unsupported or missing format
            if (targetXmlPaths == null || targetXmlPaths.Count == 0)
            {
                return (false, $"❌ Unsupported or invalid format: {format}");
            }

            var convertedCount = 0;

            // Process each target XML file
            foreach (var relativePath in targetXmlPaths)
            {
                var fullPath = Path.Combine(tempDir, relativePath);
                if (!File.Exists(fullPath))
                    continue;

                var xmlContent = await File.ReadAllTextAsync(fullPath, Encoding.UTF8).ConfigureAwait(false);

                Dictionary<string, string> fontMap = new();

                // Pre-process: replace font names with unique markers if keepFont is enabled
                if (keepFont && ShouldMaskFonts(normalizedFormat, relativePath))
                {
                    var fontCounter = 0;
                    var pattern = normalizedFormat switch
                    {
                        "docx" => @"(w:eastAsia=""|w:ascii=""|w:hAnsi=""|w:cs="")(.*?)("")",
                        "xlsx" => @"(val="")(.*?)("")",
                        "pptx" => @"(typeface="")(.*?)("")",
                        "odt" or "ods" or "odp" =>
                            @"((?:style:font-name(?:-asian|-complex)?|svg:font-family|style:name)=[""'])([^""']+)([""'])",
                        "epub" => @"(font-family\s*:\s*)([^;""']+)([;""'])?",
                        _ => null
                    };

                    if (pattern is not null)
                    {
                        xmlContent = Regex.Replace(xmlContent, pattern, match =>
                        {
                            var originalFont = match.Groups[2].Value;
                            var marker = $"__F_O_N_T_{fontCounter++}__";
                            fontMap[marker] = originalFont;

                            var suffix = match.Groups.Count >= 4 ? match.Groups[3].Value : string.Empty;
                            return match.Groups[1].Value + marker + suffix;
                        });
                    }
                }

                string convertedXml;

                if (normalizedFormat == "xlsx")
                {
                    convertedXml = ConvertXlsxXmlPart(xmlContent, relativePath, converter, punctuation);
                }
                else
                {
                    convertedXml = converter.Convert(xmlContent, punctuation);
                }

                // Post-process: restore font names from markers
                if (fontMap.Count > 0)
                {
                    foreach (var kvp in fontMap)
                    {
                        convertedXml = convertedXml.Replace(kvp.Key, kvp.Value, StringComparison.Ordinal);
                    }
                }

                // Overwrite the file with the converted content
                await File.WriteAllTextAsync(fullPath, convertedXml, Encoding.UTF8).ConfigureAwait(false);
                convertedCount++;
            }

            // Return if no valid XML fragments found
            if (convertedCount == 0)
            {
                return (false,
                    $"⚠️ No valid XML fragments were found for conversion. Is the format '{format}' correct?");
            }

            // Create the new ZIP archive with the converted files
            if (File.Exists(outputPath))
                File.Delete(outputPath);

            if (normalizedFormat == "epub")
            {
                var (zipSuccess, zipMessage) = CreateEpubZipWithSpec(tempDir, outputPath);
                if (!zipSuccess)
                    return (false, zipMessage);
            }
            else
            {
                ZipFile.CreateFromDirectory(tempDir, outputPath, CompressionLevel.Optimal, false);
            }

            return (true, $"✅ Successfully converted {convertedCount} fragment(s) in {format} document.");
        }
        catch (Exception ex)
        {
            return (false, $"❌ Conversion failed: {ex.Message}");
        }
        finally
        {
            // Clean up temp directory
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // ignore cleanup errors
                }
            }
        }
    }

    /// <summary>
    /// Collects XLSX XML parts that may contain user-visible text.
    /// Includes the shared string table and worksheet XML files for inline strings.
    /// </summary>
    private static List<string> CollectXlsxTargetXmlPaths(string tempDir)
    {
        var results = new List<string>();

        var sharedStringsPath = Path.Combine(tempDir, "xl", "sharedStrings.xml");
        if (File.Exists(sharedStringsPath))
            results.Add(Path.Combine("xl", "sharedStrings.xml"));

        var worksheetsDir = Path.Combine(tempDir, "xl", "worksheets");
        if (Directory.Exists(worksheetsDir))
        {
            results.AddRange(
                Directory.GetFiles(worksheetsDir, "*.xml", SearchOption.AllDirectories)
                    .Select(path => Path.GetRelativePath(tempDir, path))
            );
        }

        return results;
    }

    /// <summary>
    /// Returns whether font masking should be applied for the given part.
    /// For XLSX, masking is limited to sharedStrings.xml only.
    /// </summary>
    private static bool ShouldMaskFonts(string normalizedFormat, string relativePath)
    {
        if (!string.Equals(normalizedFormat, "xlsx", StringComparison.Ordinal))
            return true;

        var normalizedPath = relativePath.Replace('\\', '/');
        return string.Equals(normalizedPath, "xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Converts an XLSX XML part using narrow rules:
    /// sharedStrings.xml is converted as a whole file,
    /// worksheet XML converts only inline-string cell text nodes,
    /// and all other XLSX XML parts are left unchanged.
    /// </summary>
    private static string ConvertXlsxXmlPart(
        string xmlContent,
        string relativePath,
        Opencc converter,
        bool punctuation)
    {
        var normalizedPath = relativePath.Replace('\\', '/');

        if (string.Equals(normalizedPath, "xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase))
        {
            return converter.Convert(xmlContent, punctuation);
        }

        if (normalizedPath.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
            normalizedPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return XlsxInlineStringCellRegex.Replace(xmlContent, cellMatch =>
            {
                var cellXml = cellMatch.Value;

                return XlsxTextNodeRegex.Replace(cellXml, textMatch =>
                {
                    var openTag = textMatch.Groups[1].Value;
                    var innerText = textMatch.Groups[2].Value;
                    var closeTag = textMatch.Groups[3].Value;

                    if (string.IsNullOrEmpty(innerText))
                        return textMatch.Value;

                    var convertedText = converter.Convert(innerText, punctuation);
                    return openTag + convertedText + closeTag;
                });
            });
        }

        return xmlContent;
    }

    /// <summary>
    /// Creates a valid EPUB-compliant ZIP archive from the specified source directory.
    /// Ensures the <c>mimetype</c> file is the first entry and uncompressed,
    /// as required by the EPUB specification.
    /// </summary>
    /// <param name="sourceDir">The temporary directory containing EPUB unpacked contents.</param>
    /// <param name="outputPath">The full path of the output EPUB file to be created.</param>
    /// <returns>Tuple indicating success and an informative message.</returns>
    private static (bool Success, string Message) CreateEpubZipWithSpec(string sourceDir, string outputPath)
    {
        var mimePath = Path.Combine(sourceDir, "mimetype");

        try
        {
            using var fs = new FileStream(outputPath, FileMode.Create);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

            // 1. Add mimetype first, uncompressed
            if (File.Exists(mimePath))
            {
                var mimeEntry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
                using var entryStream = mimeEntry.Open();
                using var fileStream = File.OpenRead(mimePath);
                fileStream.CopyTo(entryStream);
            }
            else
            {
                return (false, "❌ 'mimetype' file is missing. EPUB requires this as the first entry.");
            }

            // 2. Add the rest (recursively)
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFullPath(file) == Path.GetFullPath(mimePath))
                    continue;

                var entryPath = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
                var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(file);
                fileStream.CopyTo(entryStream);
            }

            return (true, "✅ EPUB archive created successfully.");
        }
        catch (Exception ex)
        {
            return (false, $"❌ Failed to create EPUB: {ex.Message}");
        }
    }

    private static (bool Success, string Error) ExtractZipSafely(string zipPath, string extractDir)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue; // Skip folders

                var destPath = Path.GetFullPath(Path.Combine(extractDir, entry.FullName));
                if (!destPath.StartsWith(Path.GetFullPath(extractDir), StringComparison.Ordinal))
                    return (false, $"Unsafe path detected in ZIP entry: {entry.FullName}");

                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, overwrite: true);
            }

            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}