using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpenccNetLib
{
    /// <summary>
    /// Provides high-level APIs for converting Office / EPUB documents using an <see cref="Opencc"/> instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Supported formats:
    /// <c>.docx</c>, <c>.xlsx</c>, <c>.pptx</c>, <c>.odt</c>, <c>.ods</c>, <c>.odp</c>, <c>.epub</c>.
    /// </para>
    /// <para>
    /// The core API operates on <c>byte[]</c> containers, enabling use in GUI, server, and interop
    /// scenarios (e.g. Blazor). Optional file-based wrappers are provided for desktop / CLI usage.
    /// </para>
    /// <para>
    /// Internally, the implementation extracts the container to a temporary directory, converts
    /// relevant XML/XHTML fragments via <see cref="Opencc"/>, and then rebuilds a new ZIP/EPUB
    /// container. This design handles large documents efficiently by relying on the filesystem
    /// instead of keeping all intermediate files in memory.
    /// </para>
    /// </remarks>
    public static class OfficeDocConverter
    {
        /// <summary>
        /// Set of logical format names supported by this converter.
        /// </summary>
        public static readonly ISet<string> SupportedFormats =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "docx", "xlsx", "pptx",
                "odt", "ods", "odp",
                "epub"
            };

        /// <summary>
        /// Returns <c>true</c> if the specified format is supported by
        /// <see cref="OfficeDocConverter"/> (<c>docx/xlsx/pptx/odt/ods/odp/epub</c>).
        /// </summary>
        /// <param name="format">Logical format name (e.g. "docx"). Case-insensitive.</param>
        public static bool IsSupportedFormat(string format)
        {
            return !string.IsNullOrWhiteSpace(format) && SupportedFormats.Contains(format);
        }

        /// <summary>
        /// Converts an Office or EPUB container represented as a byte buffer and returns the converted buffer.
        /// </summary>
        /// <param name="inputBytes">Raw contents of the Office/EPUB container.</param>
        /// <param name="format">
        /// Logical format name (e.g. "docx", "xlsx", "pptx", "odt", "ods", "odp", "epub").
        /// Case-insensitive.
        /// </param>
        /// <param name="converter">The <see cref="Opencc"/> instance to use for text conversion.</param>
        /// <param name="punctuation">Whether to enable punctuation conversion.</param>
        /// <param name="keepFont">
        /// If <c>true</c>, attempts to preserve original font-family declarations by temporarily
        /// replacing them with markers before conversion and restoring them afterward.
        /// </param>
        /// <returns>The converted Office/EPUB container bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="inputBytes"/> or <paramref name="converter"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Thrown if <paramref name="format"/> is not a supported format.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the document structure is invalid or conversion fails.</exception>
        public static byte[] ConvertOfficeBytes(
            byte[] inputBytes,
            string format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false)
        {
            if (inputBytes == null) throw new ArgumentNullException(nameof(inputBytes));
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            if (!IsSupportedFormat(format))
                throw new ArgumentException("Unsupported Office/EPUB format: '" + format + "'.", nameof(format));

            var result = ConvertOfficeBytesCore(inputBytes, format, converter, punctuation, keepFont);

            if (!result.Success || result.OutputBytes == null)
                throw new InvalidOperationException(result.Message);

            return result.OutputBytes;
        }

        /// <summary>
        /// Asynchronously converts an Office or EPUB container represented as a byte buffer
        /// and returns the converted buffer.
        /// </summary>
        /// <remarks>
        /// On .NET Standard 2.0 this method internally uses <see cref="Task.Run(Action)"/> to
        /// execute the synchronous conversion logic on a background thread.
        /// </remarks>
        public static Task<byte[]> ConvertOfficeBytesAsync(
            byte[] inputBytes,
            string format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // netstandard2.0-friendly async wrapper around synchronous core
            return Task.Run(
                () => ConvertOfficeBytes(inputBytes, format, converter, punctuation, keepFont),
                cancellationToken);
        }

        /// <summary>
        /// Convenience wrapper for desktop/CLI use.
        /// Reads an Office/EPUB file, converts its contents, and writes the result to the output path.
        /// </summary>
        public static void ConvertOfficeFile(
            string inputPath,
            string outputPath,
            string format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false)
        {
            if (inputPath == null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));
            if (converter == null) throw new ArgumentNullException(nameof(converter));

            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Input file not found.", inputPath);

            var inputBytes = File.ReadAllBytes(inputPath);
            var outputBytes = ConvertOfficeBytes(inputBytes, format, converter, punctuation, keepFont);

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(outputPath, outputBytes);
        }

        /// <summary>
        /// Asynchronously reads an Office/EPUB file, converts its contents,
        /// and writes the result to the output path.
        /// </summary>
        /// <remarks>
        /// On .NET Standard 2.0 this method internally uses <see cref="Task.Run(Action)"/> to
        /// execute the synchronous conversion logic on a background thread.
        /// </remarks>
        public static Task ConvertOfficeFileAsync(
            string inputPath,
            string outputPath,
            string format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(
                () => { ConvertOfficeFile(inputPath, outputPath, format, converter, punctuation, keepFont); },
                cancellationToken);
        }

        // =====================================================================
        // Internal core pipeline (temp directory + XML/XHTML conversion)
        // =====================================================================

        private struct CoreResult
        {
            public bool Success;
            public string Message;
            public byte[] OutputBytes;
        }

        private static CoreResult ConvertOfficeBytesCore(
            byte[] inputBytes,
            string format,
            Opencc converter,
            bool punctuation,
            bool keepFont)
        {
            var normalizedFormat = format.ToLowerInvariant();
            var tempDir = Path.Combine(Path.GetTempPath(),
                normalizedFormat + "_Opencc_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(tempDir);

                // 1. Extract ZIP container into temp directory
                ExtractZipToDirectory(inputBytes, tempDir);

                // 2. Identify target XML/XHTML paths
                var targetXmlPaths = GetTargetXmlPaths(tempDir, normalizedFormat);
                if (targetXmlPaths == null || targetXmlPaths.Count == 0)
                {
                    return new CoreResult
                    {
                        Success = false,
                        Message = "No convertible XML/XHTML fragments found for format '" + format + "'.",
                        OutputBytes = null
                    };
                }

                // 3. Convert each target fragment
                var convertedCount = 0;

                foreach (var relativePath in targetXmlPaths)
                {
                    var fullPath = Path.Combine(tempDir, relativePath);
                    if (!File.Exists(fullPath)) continue;

                    var xmlContent = File.ReadAllText(fullPath, Encoding.UTF8);

                    Dictionary<string, string> fontMap = null;

                    if (keepFont)
                    {
                        string pattern = null;

                        if (normalizedFormat == "docx")
                            pattern = @"(w:eastAsia=""|w:ascii=""|w:hAnsi=""|w:cs="")(.*?)("")";
                        else if (normalizedFormat == "xlsx")
                            pattern = @"(val="")(.*?)("")";
                        else if (normalizedFormat == "pptx")
                            pattern = @"(typeface="")(.*?)("")";
                        else if (normalizedFormat == "odt" || normalizedFormat == "ods" || normalizedFormat == "odp")
                            pattern =
                                @"((?:style:font-name(?:-asian|-complex)?|svg:font-family|style:name)=[""'])([^""']+)([""'])";
                        else if (normalizedFormat == "epub")
                            pattern = @"(font-family\s*:\s*)([^;""']+)([;""'])?";

                        if (pattern != null)
                        {
                            fontMap = new Dictionary<string, string>();
                            var fontCounter = 0;

                            xmlContent = Regex.Replace(
                                xmlContent,
                                pattern,
                                delegate(Match m)
                                {
                                    var original = m.Groups[2].Value;
                                    var marker = "__F_O_N_T_" + (fontCounter++) + "__";
                                    fontMap[marker] = original;

                                    var tail = (m.Groups.Count >= 4) ? m.Groups[3].Value : string.Empty;
                                    return m.Groups[1].Value + marker + tail;
                                });
                        }
                    }

                    // Opencc conversion
                    var convertedXml = converter.Convert(xmlContent, punctuation);

                    // Restore fonts
                    if (fontMap != null)
                    {
                        foreach (var kv in fontMap)
                        {
                            convertedXml = convertedXml.Replace(kv.Key, kv.Value);
                        }
                    }

                    File.WriteAllText(fullPath, convertedXml, Encoding.UTF8);
                    convertedCount++;
                }

                if (convertedCount == 0)
                {
                    return new CoreResult
                    {
                        Success = false,
                        Message = "No valid XML/XHTML fragments were actually converted for format '" + format + "'.",
                        OutputBytes = null
                    };
                }

                // 4. Rebuild container
                byte[] resultBytes;

                if (normalizedFormat == "epub")
                {
                    var epubResult = CreateEpubZipWithSpec(tempDir);
                    if (!epubResult.Success || epubResult.OutputBytes == null)
                    {
                        return new CoreResult
                        {
                            Success = false,
                            Message = epubResult.Message,
                            OutputBytes = null
                        };
                    }

                    resultBytes = epubResult.OutputBytes;
                }
                else
                {
                    resultBytes = CreateZipFromDirectory(tempDir);
                }

                return new CoreResult
                {
                    Success = true,
                    Message = "Converted " + convertedCount + " fragment(s) successfully.",
                    OutputBytes = resultBytes
                };
            }
            catch (Exception ex)
            {
                return new CoreResult
                {
                    Success = false,
                    Message = "Conversion failed: " + ex.Message,
                    OutputBytes = null
                };
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch
                {
                    // ignore cleanup errors
                }
            }
        }

        private static void ExtractZipToDirectory(byte[] inputBytes, string extractDir)
        {
            using (var ms = new MemoryStream(inputBytes, 0, inputBytes.Length, writable: false, publiclyVisible: false))
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                var root = Path.GetFullPath(extractDir);

                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.FullName))
                        continue;

                    var destPath = Path.GetFullPath(Path.Combine(extractDir, entry.FullName));

                    // 防止 Zip Slip
                    if (!destPath.StartsWith(root, StringComparison.Ordinal))
                        throw new InvalidOperationException("Unsafe entry path in ZIP: " + entry.FullName);

                    var dir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    // 目錄 entry：略過
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                        entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    using (var entryStream = entry.Open())
                    using (var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        entryStream.CopyTo(fileStream);
                    }
                }
            }
        }

        private static List<string> GetTargetXmlPaths(string tempDir, string normalizedFormat)
        {
            if (normalizedFormat == "docx")
            {
                return new List<string> { Path.Combine("word", "document.xml") };
            }

            if (normalizedFormat == "xlsx")
            {
                return new List<string> { Path.Combine("xl", "sharedStrings.xml") };
            }

            if (normalizedFormat == "pptx")
            {
                var pptDir = Path.Combine(tempDir, "ppt");
                if (!Directory.Exists(pptDir))
                    return new List<string>();

                var files = Directory.GetFiles(pptDir, "*.xml", SearchOption.AllDirectories);
                var list = new List<string>();

                foreach (var path in files)
                {
                    var fileName = Path.GetFileName(path);
                    if (fileName.StartsWith("slide", StringComparison.OrdinalIgnoreCase) ||
                        path.IndexOf("notesSlide", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        path.IndexOf("slideMaster", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        path.IndexOf("slideLayout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        path.IndexOf("comment", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        list.Add(GetRelativePath(tempDir, path));
                    }
                }

                return list;
            }

            if (normalizedFormat == "odt" || normalizedFormat == "ods" || normalizedFormat == "odp")
            {
                return new List<string> { "content.xml" };
            }

            if (normalizedFormat == "epub")
            {
                if (!Directory.Exists(tempDir))
                    return new List<string>();

                var files = Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories);
                var list = new List<string>();
                foreach (var f in files)
                {
                    if (f.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".opf", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".ncx", StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(GetRelativePath(tempDir, f));
                    }
                }

                return list;
            }

            return new List<string>();
        }

        private static byte[] CreateZipFromDirectory(string sourceDir)
        {
            using (var ms = new MemoryStream())
            {
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var relativePath = GetRelativePath(sourceDir, file).Replace('\\', '/');
                        var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);

                        using (var entryStream = entry.Open())
                        using (var fileStream = File.OpenRead(file))
                        {
                            fileStream.CopyTo(entryStream);
                        }
                    }
                }

                return ms.ToArray();
            }
        }

        private struct EpubResult
        {
            public bool Success;
            public string Message;
            public byte[] OutputBytes;
        }

        /// <summary>
        /// Creates an EPUB-compliant ZIP archive in memory from the specified source directory.
        /// Ensures the "mimetype" file (if present) is the first entry and stored uncompressed,
        /// as required by the EPUB specification.
        /// </summary>
        private static EpubResult CreateEpubZipWithSpec(string sourceDir)
        {
            var mimePath = Path.Combine(sourceDir, "mimetype");

            try
            {
                if (!File.Exists(mimePath))
                {
                    return new EpubResult
                    {
                        Success = false,
                        Message = "'mimetype' file is missing; a valid EPUB requires it as the first entry.",
                        OutputBytes = null
                    };
                }

                using (var ms = new MemoryStream())
                {
                    using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        // 1. mimetype first, uncompressed
                        var mimeEntry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
                        using (var entryStream = mimeEntry.Open())
                        using (var fileStream = File.OpenRead(mimePath))
                        {
                            fileStream.CopyTo(entryStream);
                        }

                        // 2. Remaining files
                        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            var fullMime = Path.GetFullPath(mimePath);
                            var fullFile = Path.GetFullPath(file);
                            if (string.Equals(fullFile, fullMime, StringComparison.OrdinalIgnoreCase))
                                continue;

                            var relativePath = GetRelativePath(sourceDir, file).Replace('\\', '/');

                            var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
                            using (var entryStream = entry.Open())
                            using (var fileStream = File.OpenRead(file))
                            {
                                fileStream.CopyTo(entryStream);
                            }
                        }
                    }

                    return new EpubResult
                    {
                        Success = true,
                        Message = string.Empty,
                        OutputBytes = ms.ToArray()
                    };
                }
            }
            catch (Exception ex)
            {
                return new EpubResult
                {
                    Success = false,
                    Message = "Failed to create EPUB archive: " + ex.Message,
                    OutputBytes = null
                };
            }
        }

        // -----------------------------------------------------------------
        // Helpers: Path.GetRelativePath replacement for netstandard2.0
        // -----------------------------------------------------------------

        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (string.IsNullOrEmpty(basePath)) return fullPath;
            if (string.IsNullOrEmpty(fullPath)) return string.Empty;

            var baseUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(basePath)));
            var fullUri = new Uri(Path.GetFullPath(fullPath));

            if (baseUri.Scheme != fullUri.Scheme)
            {
                // cannot be made relative, return fullPath
                return fullPath;
            }

            var relativeUri = baseUri.MakeRelativeUri(fullUri);
            var relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            if (string.Equals(fullUri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            }

            return relativePath;
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            char lastChar = path[path.Length - 1];
            if (lastChar != Path.DirectorySeparatorChar && lastChar != Path.AltDirectorySeparatorChar)
            {
                return path + Path.DirectorySeparatorChar;
            }

            return path;
        }
    }
}