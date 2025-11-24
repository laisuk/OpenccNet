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
    /// Supported ZIP-based Office and EPUB document formats.
    /// </summary>
    public enum OfficeFormat
    {
        Docx,
        Xlsx,
        Pptx,
        Odt,
        Ods,
        Odp,
        Epub
    }

    internal static class OfficeFormatUtils
    {
        /// <summary>
        /// Parses a format string (e.g. "docx") into an <see cref="OfficeFormat"/> value.
        /// </summary>
        internal static OfficeFormat ParseOfficeFormat(string format)
        {
            if (string.IsNullOrWhiteSpace(format))
                throw new ArgumentException("Format must not be null or empty.", nameof(format));

            switch (format.Trim().ToLowerInvariant())
            {
                case "docx": return OfficeFormat.Docx;
                case "xlsx": return OfficeFormat.Xlsx;
                case "pptx": return OfficeFormat.Pptx;
                case "odt": return OfficeFormat.Odt;
                case "ods": return OfficeFormat.Ods;
                case "odp": return OfficeFormat.Odp;
                case "epub": return OfficeFormat.Epub;

                default:
                    throw new ArgumentException(
                        $"Unsupported Office/EPUB format: '{format}'.",
                        nameof(format));
            }
        }

        /// <summary>
        /// Converts an <see cref="OfficeFormat"/> value to its canonical lowercase string.
        /// </summary>
        internal static string OfficeFormatToString(OfficeFormat format)
        {
            switch (format)
            {
                case OfficeFormat.Docx: return "docx";
                case OfficeFormat.Xlsx: return "xlsx";
                case OfficeFormat.Pptx: return "pptx";
                case OfficeFormat.Odt: return "odt";
                case OfficeFormat.Ods: return "ods";
                case OfficeFormat.Odp: return "odp";
                case OfficeFormat.Epub: return "epub";
                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown OfficeFormat value.");
            }
        }
    }

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
        /// Converts an Office or EPUB document represented as a byte array and
        /// returns a fully reconstructed container with all textual content converted
        /// according to the specified <see cref="Opencc"/> configuration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method is the in-memory counterpart to
        /// <see cref="ConvertOfficeFile(string,string,string,Opencc,bool,bool)"/>.
        /// It is designed for scenarios where the caller does not want or cannot use
        /// temporary files, such as:
        /// </para>
        /// <list type="bullet">
        ///   <item><description>Web APIs (ASP.NET, Spring Boot via JNI, Node hosts)</description></item>
        ///   <item><description>Blazor WebAssembly</description></item>
        ///   <item><description>Mobile apps (Xamarin, MAUI, Android Java interop)</description></item>
        ///   <item><description>Unit tests and pipelines</description></item>
        ///   <item><description>Byte-stream pipelines and CLI piping</description></item>
        /// </list>
        /// <para>
        /// The converter unpacks the ZIP-based container (DOCX/XLSX/PPTX/ODT/ODS/ODP/EPUB),
        /// modifies only the text-bearing XML/XHTML parts, and repackages the archive.
        /// All non-textual assets—images, stylesheets, fonts, relationships, metadata,
        /// and directory layout—are preserved exactly as in the input.
        /// </para>
        /// <para>
        /// If <paramref name="keepFont"/> is enabled, the converter temporarily
        /// annotates spans with protected font markers before text conversion
        /// and restores the original font-family declarations afterward.
        /// </para>
        /// </remarks>
        /// <param name="inputBytes">Raw bytes of the Office/EPUB container.</param>
        /// <param name="format">
        /// Specifies the document type using the strongly typed
        /// <see cref="OfficeFormat"/> enumeration.  
        /// This value determines how the container is unpacked, which XML/XHTML
        /// parts are inspected, and how font preservation and conversion rules
        /// are applied.
        ///
        /// Supported values are:
        /// <list type="bullet">
        ///   <item><description><see cref="OfficeFormat.Docx"/> – WordprocessingML</description></item>
        ///   <item><description><see cref="OfficeFormat.Xlsx"/> – SpreadsheetML (shared strings)</description></item>
        ///   <item><description><see cref="OfficeFormat.Pptx"/> – PresentationML</description></item>
        ///   <item><description><see cref="OfficeFormat.Odt"/> – OpenDocument Text</description></item>
        ///   <item><description><see cref="OfficeFormat.Ods"/> – OpenDocument Spreadsheet</description></item>
        ///   <item><description><see cref="OfficeFormat.Odp"/> – OpenDocument Presentation</description></item>
        ///   <item><description><see cref="OfficeFormat.Epub"/> – EPUB 2/3 container (XHTML/HTML/OPF/NCX)</description></item>
        /// </list>
        /// </param>
        /// <param name="converter">
        /// An initialized <see cref="Opencc"/> instance controlling the desired
        /// Simplified/Traditional variant transformation.
        /// </param>
        /// <param name="punctuation">
        /// Whether punctuation normalization is applied (e.g., 「」 → “”).  
        /// Default is <c>false</c>.
        /// </param>
        /// <param name="keepFont">
        /// If <c>true</c>, attempts to preserve or re-inject font declarations in
        /// supported document types.
        /// </param>
        /// <returns>
        /// A fully converted Office/EPUB container as a byte array.  
        /// The returned buffer is safe to write directly to disk or serve to clients.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="inputBytes"/> or <paramref name="converter"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="format"/> is not one of the supported formats.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the container structure is invalid, the ZIP cannot be unpacked,
        /// or the conversion pipeline fails.
        /// </exception>
        /// <example>
        /// Convert an EPUB in memory:
        /// <code>
        /// var epubBytes = File.ReadAllBytes("novel.epub");
        /// var cc = new Opencc("t2s");
        /// var converted = ConvertOfficeBytes(epubBytes, "epub", cc, punctuation: true);
        /// File.WriteAllBytes("novel_simplified.epub", converted);
        /// </code>
        /// </example>
        public static byte[] ConvertOfficeBytes(
            byte[] inputBytes,
            OfficeFormat format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false)
        {
            if (inputBytes == null) throw new ArgumentNullException(nameof(inputBytes));
            if (converter == null) throw new ArgumentNullException(nameof(converter));

            var result = ConvertOfficeBytesCore(inputBytes, format, converter, punctuation, keepFont);

            if (!result.Success || result.OutputBytes == null)
                throw new InvalidOperationException(result.Message);

            return result.OutputBytes;
        }

        /// <summary>
        /// Converts an Office or EPUB document represented as a byte array and
        /// returns a fully reconstructed container with all textual content converted
        /// according to the specified <see cref="Opencc"/> configuration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method is the in-memory counterpart to
        /// <see cref="ConvertOfficeFile(string,string,string,Opencc,bool,bool)"/>.
        /// It is designed for scenarios where the caller does not want or cannot use
        /// temporary files, such as:
        /// </para>
        /// <list type="bullet">
        ///   <item><description>Web APIs (ASP.NET, Spring Boot via JNI, Node hosts)</description></item>
        ///   <item><description>Blazor WebAssembly</description></item>
        ///   <item><description>Mobile apps (Xamarin, MAUI, Android Java interop)</description></item>
        ///   <item><description>Unit tests and pipelines</description></item>
        ///   <item><description>Byte-stream pipelines and CLI piping</description></item>
        /// </list>
        /// <para>
        /// The converter unpacks the ZIP-based container (DOCX/XLSX/PPTX/ODT/ODS/ODP/EPUB),
        /// modifies only the text-bearing XML/XHTML parts, and repackages the archive.
        /// All non-textual assets—images, stylesheets, fonts, relationships, metadata,
        /// and directory layout—are preserved exactly as in the input.
        /// </para>
        /// <para>
        /// If <paramref name="keepFont"/> is enabled, the converter temporarily
        /// annotates spans with protected font markers before text conversion
        /// and restores the original font-family declarations afterward.
        /// </para>
        /// </remarks>
        /// <param name="inputBytes">Raw bytes of the Office/EPUB container.</param>
        /// <param name="format">
        /// Logical format of the container (e.g. <c>"docx"</c>, <c>"xlsx"</c>,
        /// <c>"pptx"</c>, <c>"odt"</c>, <c>"ods"</c>, <c>"odp"</c>, <c>"epub"</c>).
        /// Case-insensitive.
        /// </param>
        /// <param name="converter">
        /// An initialized <see cref="Opencc"/> instance controlling the desired
        /// Simplified/Traditional variant transformation.
        /// </param>
        /// <param name="punctuation">
        /// Whether punctuation normalization is applied (e.g., 「」 → “”).  
        /// Default is <c>false</c>.
        /// </param>
        /// <param name="keepFont">
        /// If <c>true</c>, attempts to preserve or re-inject font declarations in
        /// supported document types.
        /// </param>
        /// <returns>
        /// A fully converted Office/EPUB container as a byte array.  
        /// The returned buffer is safe to write directly to disk or serve to clients.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="inputBytes"/> or <paramref name="converter"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="format"/> is not one of the supported formats.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the container structure is invalid, the ZIP cannot be unpacked,
        /// or the conversion pipeline fails.
        /// </exception>
        /// <example>
        /// Convert an EPUB in memory:
        /// <code>
        /// var epubBytes = File.ReadAllBytes("novel.epub");
        /// var cc = new Opencc("t2s");
        /// var converted = ConvertOfficeBytes(epubBytes, "epub", cc, punctuation: true);
        /// File.WriteAllBytes("novel_simplified.epub", converted);
        /// </code>
        /// </example>
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

            var parsed = OfficeFormatUtils.ParseOfficeFormat(format);
            var result = ConvertOfficeBytesCore(inputBytes, parsed, converter, punctuation, keepFont);

            if (!result.Success || result.OutputBytes == null)
                throw new InvalidOperationException(result.Message);

            return result.OutputBytes;
        }

        /// <summary>
        /// Asynchronously converts an Office or EPUB document represented as a byte array
        /// and returns the converted container as a byte array.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method mirrors <see cref="ConvertOfficeBytes(byte[],string,Opencc,bool,bool)"/>
        /// but performs the work asynchronously.  
        /// </para>
        /// <para>
        /// On .NET Standard 2.0, where true asynchronous file I/O is unavailable,
        /// the method safely delegates synchronous work to a background thread using
        /// <see cref="Task.Run{TResult}(Func{TResult})"/>.  
        /// This prevents blocking the UI thread in GUI or web applications.
        /// </para>
        /// <para>
        /// Ideal for:
        /// </para>
        /// <list type="bullet">
        ///   <item><description>WPF / Avalonia front-ends</description></item>
        ///   <item><description>Mobile apps (Android/iOS)</description></item>
        ///   <item><description>Blazor WebAssembly</description></item>
        ///   <item><description>High-throughput API servers</description></item>
        /// </list>
        /// <para>
        /// The returned byte array is a full ZIP container ready to be written to disk,
        /// streamed to a browser, or re-opened by Office/EPUB readers.
        /// </para>
        /// </remarks>
        /// <param name="inputBytes">Raw contents of the document to convert.</param>
        /// <param name="format">
        /// Document container type expressed as an <see cref="OfficeFormat"/> value
        /// (e.g. <see cref="OfficeFormat.Docx"/>, <see cref="OfficeFormat.Epub"/>).
        /// </param>
        /// <param name="converter">The active <see cref="Opencc"/> converter.</param>
        /// <param name="punctuation">Whether punctuation conversion is applied.</param>
        /// <param name="keepFont">Whether to preserve font declarations where possible.</param>
        /// <param name="cancellationToken">
        /// Optional cancellation token. Cancels before repackaging or output allocation.
        /// </param>
        /// <returns>
        /// A task that resolves to the converted Office/EPUB container bytes.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="inputBytes"/> or <paramref name="converter"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="format"/> is not recognized.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the conversion process fails or the input container is invalid.
        /// </exception>
        /// <example>
        /// <code>
        /// byte[] result = await ConvertOfficeBytesAsync(
        ///     inputBytes,
        ///     "docx",
        ///     new Opencc("s2tw"),
        ///     punctuation: true,
        ///     keepFont: false,
        ///     cancellationToken);
        /// </code>
        /// </example>
        public static Task<byte[]> ConvertOfficeBytesAsync(
            byte[] inputBytes,
            OfficeFormat format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(
                () => ConvertOfficeBytes(inputBytes, format, converter, punctuation, keepFont),
                cancellationToken);
        }

        /// <summary>
        /// Asynchronously converts an Office or EPUB document represented as a byte array
        /// and returns the converted container as a byte array.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method mirrors <see cref="ConvertOfficeBytes(byte[],string,Opencc,bool,bool)"/>
        /// but performs the work asynchronously.  
        /// </para>
        /// <para>
        /// On .NET Standard 2.0, where true asynchronous file I/O is unavailable,
        /// the method safely delegates synchronous work to a background thread using
        /// <see cref="Task.Run{TResult}(Func{TResult})"/>.  
        /// This prevents blocking the UI thread in GUI or web applications.
        /// </para>
        /// <para>
        /// Ideal for:
        /// </para>
        /// <list type="bullet">
        ///   <item><description>WPF / Avalonia front-ends</description></item>
        ///   <item><description>Mobile apps (Android/iOS)</description></item>
        ///   <item><description>Blazor WebAssembly</description></item>
        ///   <item><description>High-throughput API servers</description></item>
        /// </list>
        /// <para>
        /// The returned byte array is a full ZIP container ready to be written to disk,
        /// streamed to a browser, or re-opened by Office/EPUB readers.
        /// </para>
        /// </remarks>
        /// <param name="inputBytes">Raw contents of the document to convert.</param>
        /// <param name="format">Document format (e.g. <c>"docx"</c>, <c>"epub"</c>).</param>
        /// <param name="converter">The active <see cref="Opencc"/> converter.</param>
        /// <param name="punctuation">Whether punctuation conversion is applied.</param>
        /// <param name="keepFont">Whether to preserve font declarations where possible.</param>
        /// <param name="cancellationToken">
        /// Optional cancellation token. Cancels before repackaging or output allocation.
        /// </param>
        /// <returns>
        /// A task that resolves to the converted Office/EPUB container bytes.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="inputBytes"/> or <paramref name="converter"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="format"/> is not recognized.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the conversion process fails or the input container is invalid.
        /// </exception>
        /// <example>
        /// <code>
        /// byte[] result = await ConvertOfficeBytesAsync(
        ///     inputBytes,
        ///     "docx",
        ///     new Opencc("s2tw"),
        ///     punctuation: true,
        ///     keepFont: false,
        ///     cancellationToken);
        /// </code>
        /// </example>
        public static Task<byte[]> ConvertOfficeBytesAsync(
            byte[] inputBytes,
            string format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false,
            CancellationToken cancellationToken = default)
        {
            var parsed = OfficeFormatUtils.ParseOfficeFormat(format);
            // netstandard2.0-friendly async wrapper around synchronous core
            return Task.Run(
                () => ConvertOfficeBytes(inputBytes, parsed, converter, punctuation, keepFont),
                cancellationToken);
        }

        /// <summary>
        /// Converts an Office or EPUB document on disk and writes the converted
        /// result to the specified output file.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method is the primary high-level API for desktop, CLI tooling,
        /// and automation scripts. It reads the entire input file into memory,
        /// performs OpenCC conversion on all text-bearing XML/XHTML parts inside
        /// the archive (DOCX/XLSX/PPTX/ODT/ODS/ODP/EPUB), and writes a fully
        /// reconstructed output archive.
        /// </para>
        /// <para>
        /// The method preserves non-text assets (images, media, stylesheets,
        /// relationships, metadata) exactly as they appear in the original
        /// container. Only the text within target XML-based parts is modified.
        /// </para>
        /// <para>
        /// Supported formats:
        /// <list type="bullet">
        ///   <item><description><c>docx</c> – WordprocessingML</description></item>
        ///   <item><description><c>xlsx</c> – SpreadsheetML (shared strings only)</description></item>
        ///   <item><description><c>pptx</c> – PresentationML slides/notes/layouts/masters</description></item>
        ///   <item><description><c>odt</c>/<c>ods</c>/<c>odp</c> – OpenDocument Text/Spreadsheet/Presentation</description></item>
        ///   <item><description><c>epub</c> – XHTML/HTML/OPF/NCX documents</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// If <paramref name="keepFont"/> is enabled, the converter injects font
        /// attributes into target text spans, allowing regional substitutions
        /// (e.g., Traditional → Simplified fonts) to be preserved in the output.
        /// </para>
        /// </remarks>
        /// <param name="inputPath">Full path to the source Office/EPUB file.</param>
        /// <param name="outputPath">
        /// Path where the converted file will be written.  
        /// The parent directory is created automatically if it does not already exist.
        /// </param>
        /// <param name="format">
        /// Specifies the document type using the strongly typed
        /// <see cref="OfficeFormat"/> enumeration.  
        /// This value determines how the container is unpacked, which XML/XHTML
        /// parts are inspected, and how font preservation and conversion rules
        /// are applied.
        ///
        /// Supported values are:
        /// <list type="bullet">
        ///   <item><description><see cref="OfficeFormat.Docx"/> – WordprocessingML</description></item>
        ///   <item><description><see cref="OfficeFormat.Xlsx"/> – SpreadsheetML (shared strings)</description></item>
        ///   <item><description><see cref="OfficeFormat.Pptx"/> – PresentationML</description></item>
        ///   <item><description><see cref="OfficeFormat.Odt"/> – OpenDocument Text</description></item>
        ///   <item><description><see cref="OfficeFormat.Ods"/> – OpenDocument Spreadsheet</description></item>
        ///   <item><description><see cref="OfficeFormat.Odp"/> – OpenDocument Presentation</description></item>
        ///   <item><description><see cref="OfficeFormat.Epub"/> – EPUB 2/3 container (XHTML/HTML/OPF/NCX)</description></item>
        /// </list>
        /// </param>
        /// <param name="converter">
        /// An initialized <see cref="Opencc"/> instance containing the desired conversion configuration.
        /// </param>
        /// <param name="punctuation">
        /// Whether punctuation should also be converted using OpenCC rules (e.g.,「」 → “”).  
        /// Default is <c>false</c>.
        /// </param>
        /// <param name="keepFont">
        /// Preserves or injects font attributes in converted output when supported.  
        /// Default is <c>false</c>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="inputPath"/>, <paramref name="outputPath"/>, or <paramref name="converter"/> is null.
        /// </exception>
        /// <exception cref="FileNotFoundException">
        /// Thrown when the input file does not exist.
        /// </exception>
        /// <exception cref="InvalidDataException">
        /// Thrown if the document is not a valid ZIP-based Office/EPUB container.
        /// </exception>
        /// <example>
        /// Convert Traditional Chinese DOCX → Simplified (retain punctuation):
        /// <code>
        /// Opencc cc = new Opencc("t2s");
        /// ConvertOfficeFile("input.docx", "out.docx", "docx", cc, punctuation: true);
        /// </code>
        /// </example>
        public static void ConvertOfficeFile(
            string inputPath,
            string outputPath,
            OfficeFormat format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false)
        {
            if (inputPath == null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Input file not found.", inputPath);

            var bytes = File.ReadAllBytes(inputPath);
            var output = ConvertOfficeBytes(bytes, format, converter, punctuation, keepFont);

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(outputPath, output);
        }

        /// <summary>
        /// Converts an Office or EPUB document on disk and writes the converted
        /// result to the specified output file.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method is the primary high-level API for desktop, CLI tooling,
        /// and automation scripts. It reads the entire input file into memory,
        /// performs OpenCC conversion on all text-bearing XML/XHTML parts inside
        /// the archive (DOCX/XLSX/PPTX/ODT/ODS/ODP/EPUB), and writes a fully
        /// reconstructed output archive.
        /// </para>
        /// <para>
        /// The method preserves non-text assets (images, media, stylesheets,
        /// relationships, metadata) exactly as they appear in the original
        /// container. Only the text within target XML-based parts is modified.
        /// </para>
        /// <para>
        /// Supported formats:
        /// <list type="bullet">
        ///   <item><description><c>docx</c> – WordprocessingML</description></item>
        ///   <item><description><c>xlsx</c> – SpreadsheetML (shared strings only)</description></item>
        ///   <item><description><c>pptx</c> – PresentationML slides/notes/layouts/masters</description></item>
        ///   <item><description><c>odt</c>/<c>ods</c>/<c>odp</c> – OpenDocument Text/Spreadsheet/Presentation</description></item>
        ///   <item><description><c>epub</c> – XHTML/HTML/OPF/NCX documents</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// If <paramref name="keepFont"/> is enabled, the converter injects font
        /// attributes into target text spans, allowing regional substitutions
        /// (e.g., Traditional → Simplified fonts) to be preserved in the output.
        /// </para>
        /// </remarks>
        /// <param name="inputPath">Full path to the source Office/EPUB file.</param>
        /// <param name="outputPath">
        /// Path where the converted file will be written.  
        /// The parent directory is created automatically if it does not already exist.
        /// </param>
        /// <param name="format">
        /// Normalized format identifier (e.g. <c>"docx"</c>, <c>"xlsx"</c>, <c>"epub"</c>).  
        /// Must match the container type of <paramref name="inputPath"/>.
        /// </param>
        /// <param name="converter">
        /// An initialized <see cref="Opencc"/> instance containing the desired conversion configuration.
        /// </param>
        /// <param name="punctuation">
        /// Whether punctuation should also be converted using OpenCC rules (e.g.,「」 → “”).  
        /// Default is <c>false</c>.
        /// </param>
        /// <param name="keepFont">
        /// Preserves or injects font attributes in converted output when supported.  
        /// Default is <c>false</c>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="inputPath"/>, <paramref name="outputPath"/>, or <paramref name="converter"/> is null.
        /// </exception>
        /// <exception cref="FileNotFoundException">
        /// Thrown when the input file does not exist.
        /// </exception>
        /// <exception cref="InvalidDataException">
        /// Thrown if the document is not a valid ZIP-based Office/EPUB container.
        /// </exception>
        /// <example>
        /// Convert Traditional Chinese DOCX → Simplified (retain punctuation):
        /// <code>
        /// Opencc cc = new Opencc("t2s");
        /// ConvertOfficeFile("input.docx", "out.docx", "docx", cc, punctuation: true);
        /// </code>
        /// </example>
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
            var parsed = OfficeFormatUtils.ParseOfficeFormat(format);
            var outputBytes = ConvertOfficeBytes(inputBytes, parsed, converter, punctuation, keepFont);

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(outputPath, outputBytes);
        }

        /// <summary>
        /// Asynchronously converts an Office or EPUB document and writes the
        /// converted result to the specified output file.
        /// </summary>
        /// <remarks>
        /// <para>
        /// On frameworks without native async file APIs (e.g., .NET Standard 2.0),
        /// this method offloads the synchronous conversion work to a background
        /// thread using <see cref="Task.Run(Func{Task})"/>.
        /// </para>
        /// <para>
        /// The behavior and conversion rules are identical to
        /// <see cref="ConvertOfficeFile(string,string,string,Opencc,bool,bool)"/>.
        /// </para>
        /// <para>
        /// This method is suitable for GUI applications (WPF, Avalonia, JavaFX
        /// via JNI), Blazor WebAssembly, mobile apps, and CLI tools that require
        /// non-blocking operation.
        /// </para>
        /// </remarks>
        /// <param name="inputPath">Full path to the source Office/EPUB file.</param>
        /// <param name="outputPath">Destination path for the converted file.</param>
        /// <param name="format">
        /// Document container type expressed as an <see cref="OfficeFormat"/> value
        /// (e.g. <see cref="OfficeFormat.Docx"/>, <see cref="OfficeFormat.Epub"/>).
        /// </param>
        /// <param name="converter">The active OpenCC converter instance.</param>
        /// <param name="punctuation">Whether punctuation should also be converted.</param>
        /// <param name="keepFont">Whether font attributes should be preserved.</param>
        /// <param name="cancellationToken">
        /// Optional cancellation token.  
        /// Cancellation is cooperative and stops before I/O write-back.
        /// </param>
        /// <returns>
        /// A task that represents the asynchronous conversion operation.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when required arguments are null.
        /// </exception>
        /// <exception cref="FileNotFoundException">
        /// Thrown when the input file does not exist.
        /// </exception>
        /// <example>
        /// <code>
        /// await ConvertOfficeFileAsync(
        ///     "book.epub",
        ///     "book_converted.epub",
        ///     OfficeFormat.Epub,
        ///     new Opencc("s2twp"),
        ///     punctuation: true,
        ///     keepFont: true,
        ///     cancellationToken);
        /// </code>
        /// </example>
        public static Task ConvertOfficeFileAsync(
            string inputPath,
            string outputPath,
            OfficeFormat format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(
                () => ConvertOfficeFile(inputPath, outputPath, format, converter, punctuation, keepFont),
                cancellationToken);
        }

        /// <summary>
        /// Asynchronously converts an Office or EPUB document and writes the
        /// converted result to the specified output file.
        /// </summary>
        /// <remarks>
        /// <para>
        /// On frameworks without native async file APIs (e.g., .NET Standard 2.0),
        /// this method offloads the synchronous conversion work to a background
        /// thread using <see cref="Task.Run(Func{Task})"/>.
        /// </para>
        /// <para>
        /// The behavior and conversion rules are identical to
        /// <see cref="ConvertOfficeFile(string,string,string,Opencc,bool,bool)"/>.
        /// </para>
        /// <para>
        /// This method is suitable for GUI applications (WPF, Avalonia, JavaFX
        /// via JNI), Blazor WebAssembly, mobile apps, and CLI tools that require
        /// non-blocking operation.
        /// </para>
        /// </remarks>
        /// <param name="inputPath">Full path to the source Office/EPUB file.</param>
        /// <param name="outputPath">Destination path for the converted file.</param>
        /// <param name="format">Document format (e.g. <c>"docx"</c>, <c>"epub"</c>).</param>
        /// <param name="converter">The active OpenCC converter instance.</param>
        /// <param name="punctuation">Whether punctuation should also be converted.</param>
        /// <param name="keepFont">Whether font attributes should be preserved.</param>
        /// <param name="cancellationToken">
        /// Optional cancellation token.  
        /// Cancellation is cooperative and stops before I/O write-back.
        /// </param>
        /// <returns>
        /// A task that represents the asynchronous conversion operation.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when required arguments are null.
        /// </exception>
        /// <exception cref="FileNotFoundException">
        /// Thrown when the input file does not exist.
        /// </exception>
        /// <example>
        /// <code>
        /// await ConvertOfficeFileAsync(
        ///     "book.epub",
        ///     "book_converted.epub",
        ///     "epub",
        ///     new Opencc("s2twp"),
        ///     punctuation: true,
        ///     keepFont: true,
        ///     cancellationToken);
        /// </code>
        /// </example>
        public static Task ConvertOfficeFileAsync(
            string inputPath,
            string outputPath,
            string format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false,
            CancellationToken cancellationToken = default)
        {
            var parsed = OfficeFormatUtils.ParseOfficeFormat(format);
            return Task.Run(
                () => { ConvertOfficeFile(inputPath, outputPath, parsed, converter, punctuation, keepFont); },
                cancellationToken);
        }

        /// <summary>
        /// Core conversion engine for Office/EPUB containers.  
        /// Unpacks the input ZIP-based document, applies OpenCC text conversion
        /// to all relevant XML/XHTML fragments, and rebuilds the container.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method represents the internal processing pipeline shared by all
        /// public Office/EPUB conversion APIs. It performs the following steps:
        /// </para>
        /// <list type="number">
        ///   <item><description>
        ///   Extracts the ZIP container (<c>DOCX</c>, <c>XLSX</c>, <c>PPTX</c>,
        ///   <c>ODT</c>/<c>ODS</c>/<c>ODP</c>, <c>EPUB</c>) into a temporary directory.
        ///   </description></item>
        ///   <item><description>
        ///   Locates all text-bearing XML or XHTML files based on the specified
        ///   <see cref="OfficeFormat"/>. The discovery rules differ per format.
        ///   </description></item>
        ///   <item><description>
        ///   Optionally protects font-family declarations with internal markers so that
        ///   OpenCC conversion does not alter them, restoring the original fonts after
        ///   the textual segments have been processed.
        ///   </description></item>
        ///   <item><description>
        ///   Applies OpenCC conversion to each fragment, respecting punctuation and
        ///   font-preservation settings.
        ///   </description></item>
        ///   <item><description>
        ///   Repackages the modified directory tree into a new ZIP container.  
        ///   EPUB files follow the EPUB specification (uncompressed <c>mimetype</c> entry).
        ///   </description></item>
        /// </list>
        /// <para>
        /// This method never throws. All exceptions are caught and wrapped into a
        /// <see cref="CoreResult"/> structure with <c>Success = false</c>.
        /// </para>
        /// </remarks>
        /// <param name="inputBytes">Raw ZIP container bytes from the input document.</param>
        /// <param name="format">
        /// The strongly typed Office/EPUB format that determines extraction and
        /// XML/XHTML discovery rules.
        /// </param>
        /// <param name="converter">The active <see cref="Opencc"/> instance.</param>
        /// <param name="punctuation">Whether punctuation normalization should be applied.</param>
        /// <param name="keepFont">
        /// Whether to preserve font-family declarations using temporary markers
        /// during conversion.
        /// </param>
        /// <returns>
        /// A <see cref="CoreResult"/> containing the success flag, diagnostics message,
        /// and the fully rebuilt converted container bytes (if successful).
        /// </returns>
        private static CoreResult ConvertOfficeBytesCore(
            byte[] inputBytes,
            OfficeFormat format,
            Opencc converter,
            bool punctuation,
            bool keepFont)
        {
            // Canonical lowercase string id for messages, temp dir naming, and helpers
            var formatId = OfficeFormatUtils.OfficeFormatToString(format);

            var tempDir = Path.Combine(
                Path.GetTempPath(),
                formatId + "_Opencc_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(tempDir);

                // 1. Extract ZIP container into temp directory
                ExtractZipToDirectory(inputBytes, tempDir);

                // 2. Identify target XML/XHTML paths
                //    (helper still takes normalized string format)
                var targetXmlPaths = GetTargetXmlPaths(tempDir, formatId);
                if (targetXmlPaths == null || targetXmlPaths.Count == 0)
                {
                    return new CoreResult
                    {
                        Success = false,
                        Message = "No convertible XML/XHTML fragments found for format '" + formatId + "'.",
                        OutputBytes = null
                    };
                }

                // 3. Convert each target fragment
                var convertedCount = 0;

                foreach (var relativePath in targetXmlPaths)
                {
                    var fullPath = Path.Combine(tempDir, relativePath);
                    if (!File.Exists(fullPath))
                        continue;

                    var xmlContent = File.ReadAllText(fullPath, Encoding.UTF8);

                    Dictionary<string, string> fontMap = null;

                    if (keepFont)
                    {
                        string pattern = null;

                        // Choose font-preservation regex based on OfficeFormat
                        switch (format)
                        {
                            case OfficeFormat.Docx:
                                pattern = @"(w:eastAsia=""|w:ascii=""|w:hAnsi=""|w:cs="")(.*?)("")";
                                break;

                            case OfficeFormat.Xlsx:
                                pattern = @"(val="")(.*?)("")";
                                break;

                            case OfficeFormat.Pptx:
                                pattern = @"(typeface="")(.*?)("")";
                                break;

                            case OfficeFormat.Odt:
                            case OfficeFormat.Ods:
                            case OfficeFormat.Odp:
                                pattern =
                                    @"((?:style:font-name(?:-asian|-complex)?|svg:font-family|style:name)=[""'])([^""']+)([""'])";
                                break;

                            case OfficeFormat.Epub:
                                pattern = @"(font-family\s*:\s*)([^;""']+)([;""'])?";
                                break;
                        }

                        if (pattern != null)
                        {
                            fontMap = new Dictionary<string, string>();
                            var fontCounter = 0;

                            xmlContent = Regex.Replace(
                                xmlContent,
                                pattern,
                                m =>
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
                        Message = "No valid XML/XHTML fragments were actually converted for format '" + formatId + "'.",
                        OutputBytes = null
                    };
                }

                // 4. Rebuild container
                byte[] resultBytes;

                if (format == OfficeFormat.Epub)
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

                    // Prevent Zip Slip
                    if (!destPath.StartsWith(root, StringComparison.Ordinal))
                        throw new InvalidOperationException("Unsafe entry path in ZIP: " + entry.FullName);

                    var dir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    // Directory entry：skip
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

        /// <summary>
        /// Computes the list of XML/XHTML-based files inside an extracted archive
        /// that should be processed for text conversion, based on the given format.
        /// </summary>
        /// <param name="tempDir">
        /// Absolute path to the temporary directory where the input archive
        /// (DOCX/XLSX/PPTX/ODT/ODS/ODP/EPUB) has been unpacked.
        /// This is treated as the logical "ZIP root".
        /// </param>
        /// <param name="normalizedFormat">
        /// File format in normalized lowercase form without leading dot, e.g.
        /// <c>"docx"</c>, <c>"xlsx"</c>, <c>"pptx"</c>, <c>"odt"</c>, <c>"epub"</c>.
        /// </param>
        /// <returns>
        /// A list of relative paths (from <paramref name="tempDir"/>), pointing to
        /// XML/XHTML files that should be run through the converter.  
        /// Returns an empty list if the format is unsupported, the directory is missing,
        /// or no candidate files are found.
        /// </returns>
        private static List<string> GetTargetXmlPaths(string tempDir, string normalizedFormat)
        {
            if (string.IsNullOrWhiteSpace(normalizedFormat))
                return new List<string>();

            switch (normalizedFormat)
            {
                case "docx":
                    // Main WordprocessingML document.
                    return new List<string> { Path.Combine("word", "document.xml") };

                case "xlsx":
                    // Shared string table (cell text).
                    return new List<string> { Path.Combine("xl", "sharedStrings.xml") };

                case "pptx":
                    // All slide/notes/layout/master/comment XML parts.
                    return GetPptxTargetXmlPaths(tempDir);

                case "odt":
                case "ods":
                case "odp":
                    // OpenDocument formats store main content in content.xml.
                    return new List<string> { "content.xml" };

                case "epub":
                    // EPUB: scan the entire tree for XHTML/HTML/OPF/NCX files.
                    return GetEpubTargetPaths(tempDir);

                default:
                    // Unsupported or unknown format.
                    return new List<string>();
            }
        }

        /// <summary>
        /// Returns all relevant XML parts inside a PPTX package that may contain user-visible text,
        /// such as slides, notes, slide layouts, masters, and comments.
        /// </summary>
        /// <param name="tempDir">
        /// Root temporary directory where the PPTX archive has been extracted.
        /// </param>
        /// <returns>
        /// List of relative paths (from <paramref name="tempDir"/>)
        /// to PPTX XML parts that should be converted.  
        /// Returns an empty list if the <c>ppt</c> folder does not exist or no matching files are found.
        /// </returns>
        private static List<string> GetPptxTargetXmlPaths(string tempDir)
        {
            var pptDir = Path.Combine(tempDir, "ppt");
            if (!Directory.Exists(pptDir))
                return new List<string>();

            var files = Directory.GetFiles(pptDir, "*.xml", SearchOption.AllDirectories);
            var list = new List<string>(files.Length);

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

        /// <summary>
        /// Scans an extracted EPUB directory and returns all textual metadata
        /// and content files that should be converted (XHTML/HTML/OPF/NCX).
        /// </summary>
        /// <param name="tempDir">
        /// Root temporary directory where the EPUB archive has been extracted.
        /// </param>
        /// <returns>
        /// List of relative paths (from <paramref name="tempDir"/>)
        /// to EPUB content files.  
        /// Returns an empty list if the directory does not exist or no candidates are found.
        /// </returns>
        private static List<string> GetEpubTargetPaths(string tempDir)
        {
            if (string.IsNullOrWhiteSpace(tempDir) || !Directory.Exists(tempDir))
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

        /// <summary>
        /// Recreates a ZIP archive from a directory and returns the ZIP as a byte array.
        /// </summary>
        /// <remarks>
        /// Used internally by Office/EPUB conversion to rebuild the container after
        /// text-modified XML parts have been written to the temp directory.
        /// Preserves directory structure and file names exactly.
        /// </remarks>
        private static byte[] CreateZipFromDirectory(string sourceDir)
        {
            // MemoryStream → final ZIP buffer
            using (var ms = new MemoryStream())
            {
                // Create a new ZIP archive inside the stream
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    // Enumerate all files inside the directory tree
                    var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);

                    foreach (var file in files)
                    {
                        // Convert full path to ZIP-relative path (forward slashes required)
                        var relativePath = GetRelativePath(sourceDir, file).Replace('\\', '/');

                        // Create ZIP entry
                        var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);

                        // Copy file → ZIP entry stream
                        using (var entryStream = entry.Open())
                        using (var fileStream = File.OpenRead(file))
                        {
                            fileStream.CopyTo(entryStream);
                        }
                    }
                }

                // Return the completed ZIP as byte[]
                return ms.ToArray();
            }
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

            var lastChar = path[path.Length - 1];
            if (lastChar != Path.DirectorySeparatorChar && lastChar != Path.AltDirectorySeparatorChar)
            {
                return path + Path.DirectorySeparatorChar;
            }

            return path;
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

        private struct EpubResult
        {
            public bool Success;
            public string Message;
            public byte[] OutputBytes;
        }
    }
}