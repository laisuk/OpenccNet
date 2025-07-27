using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using OpenccNetLib;

namespace OpenccNet;

/// <summary>
/// Provides functionality to convert Office document formats (.docx, .xlsx, .pptx, .odt, .ods, .odp)
/// using the Opencc converter with optional font name preservation.
/// </summary>
public static class OfficeDocModel
{
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
        bool punctuation,
        bool keepFont = false)
    {
        // Create a temporary working directory
        var tempDir = Path.Combine(Path.GetTempPath(), $"{format}_temp_" + Guid.NewGuid());

        try
        {
            // Extract the input Office archive into the temp folder
            ZipFile.ExtractToDirectory(inputPath, tempDir);

            // Identify target XML files for each Office format
            var targetXmlPaths = format switch
            {
                "docx" => new List<string> { Path.Combine("word", "document.xml") },
                "xlsx" => new List<string> { Path.Combine("xl", "sharedStrings.xml") },
                "pptx" => Directory.Exists(Path.Combine(tempDir, "ppt"))
                    ? Directory.GetFiles(Path.Combine(tempDir, "ppt"), "*.xml", SearchOption.AllDirectories)
                        .Where(path => Path.GetFileName(path).StartsWith("slide") ||
                                       path.Contains("notesSlide") ||
                                       path.Contains("slideMaster") ||
                                       path.Contains("slideLayout") ||
                                       path.Contains("comment"))
                        .Select(path => Path.GetRelativePath(tempDir, path))
                        .ToList()
                    : new List<string>(),
                // 🆕 Add ODT family: all use "content.xml"
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
                if (!File.Exists(fullPath)) continue;

                var xmlContent = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);

                Dictionary<string, string> fontMap = new();

                // Pre-process: replace font names with unique markers if keepFont is enabled
                if (keepFont)
                {
                    var fontCounter = 0;
                    var pattern = format switch
                    {
                        "docx" => @"(w:eastAsia=""|w:ascii=""|w:hAnsi=""|w:cs="")(.*?)("")",
                        "xlsx" => @"(val="")(.*?)("")",
                        "pptx" => @"(typeface="")(.*?)("")",
                        // 🆕 Improved regex for odt/ods/odp – avoids converting Chinese font names
                        "odt" or "ods" or "odp" =>
                            @"((?:style:font-name(?:-asian|-complex)?|svg:font-family|style:name)=[""'])([^""']+)([""'])",
                        "epub" => @"(font-family\s*:\s*)([^;""']+)",
                        _ => null
                    };

                    if (pattern != null)
                    {
                        xmlContent = Regex.Replace(xmlContent, pattern, match =>
                        {
                            string originalFont = match.Groups[2].Value;
                            var marker = $"__F_O_N_T_{fontCounter++}__";
                            fontMap[marker] = originalFont;

                            return format switch
                            {
                                "odt" or "ods" or "odp" => match.Groups[1].Value + marker + match.Groups[3].Value,
                                "epub" => match.Groups[1].Value + marker,
                                _ => match.Groups[1].Value + marker + match.Groups[3].Value
                            };
                        });
                    }
                }

                // Run OpenCC conversion on the XML content
                var convertedXml = converter.Convert(xmlContent, punctuation);

                // Post-process: restore font names from markers
                if (keepFont)
                {
                    foreach (var kvp in fontMap)
                    {
                        convertedXml = convertedXml.Replace(kvp.Key, kvp.Value);
                    }
                }

                // Overwrite the file with the converted content
                await File.WriteAllTextAsync(fullPath, convertedXml, Encoding.UTF8);
                convertedCount++;
            }

            // Return if no valid XML fragments found
            if (convertedCount == 0)
            {
                return (false,
                    $"⚠️ No valid XML fragments were found for conversion. Is the format '{format}' correct?");
            }

            // Create the new ZIP archive with the converted files
            if (File.Exists(outputPath)) File.Delete(outputPath);
            if (format == "epub")
            {
                var (zipSuccess, zipMessage) = CreateEpubZipWithSpec(tempDir, outputPath);
                if (!zipSuccess) return (false, zipMessage);
            }
            else
                ZipFile.CreateFromDirectory(tempDir, outputPath, CompressionLevel.Optimal, false);

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
                Directory.Delete(tempDir, true);
        }
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
}