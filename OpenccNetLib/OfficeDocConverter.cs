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
    /// Specifies the supported ZIP-based document container formats that can be
    /// processed by <see cref="OfficeDocConverter"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All formats listed here share a common structural characteristic:
    /// they are ZIP containers containing XML-based document parts.  
    /// Only the text-bearing XML / XHTML fragments are modified during conversion;
    /// all other assets (images, metadata, relationships, fonts, stylesheets, etc.)
    /// are preserved exactly.
    /// </para>
    /// <para>
    /// Use this enumeration when calling any of the strongly typed overloads:
    /// <see cref="OfficeDocConverter.ConvertOfficeBytes(byte[], OfficeFormat, Opencc, bool, bool)"/> or  
    /// <see cref="OfficeDocConverter.ConvertOfficeFile(string, string, OfficeFormat, Opencc, bool, bool)"/>.
    /// </para>
    /// </remarks>
    public enum OfficeFormat
    {
        /// <summary>
        /// Microsoft Word document in Office Open XML format (WordprocessingML).  
        /// Text is stored primarily in <c>word/document.xml</c>.
        /// </summary>
        Docx,

        /// <summary>
        /// Microsoft Excel workbook in Office Open XML format (SpreadsheetML).  
        /// Text is stored mainly in <c>xl/sharedStrings.xml</c>.
        /// </summary>
        Xlsx,

        /// <summary>
        /// Microsoft PowerPoint presentation in Office Open XML format
        /// (PresentationML).  
        /// Text is found within slide XMLs (<c>ppt/slides/slide*.xml</c>),
        /// layouts, masters, and notes.
        /// </summary>
        Pptx,

        /// <summary>
        /// OpenDocument Text format (<c>.odt</c>).  
        /// Content is stored in <c>content.xml</c>, using ODF vocabulary.
        /// </summary>
        Odt,

        /// <summary>
        /// OpenDocument Spreadsheet format (<c>.ods</c>).  
        /// Content is stored in <c>content.xml</c>.
        /// </summary>
        Ods,

        /// <summary>
        /// OpenDocument Presentation format (<c>.odp</c>).  
        /// Content is stored in <c>content.xml</c>.
        /// </summary>
        Odp,

        /// <summary>
        /// EPUB 2/3 digital book container.  
        /// Text is stored in XHTML/HTML files, while metadata resides in
        /// <c>content.opf</c> and navigation in <c>.ncx</c>.  
        /// Requires special EPUB packaging rules (e.g., uncompressed <c>mimetype</c> first).
        /// </summary>
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
    /// The core <c>byte[]</c> API is fully in-memory. It reads the input ZIP package
    /// directly from memory, converts selected XML/XHTML entries, streams unchanged
    /// entries into a new in-memory archive, and returns the rebuilt package as
    /// <c>byte[]</c>. No temporary working directory is used by byte-array conversion.
    /// </para>
    /// <para>
    /// Optional file-based wrappers are provided for desktop and CLI workflows. Those
    /// wrappers read and write caller-specified files while reusing the same in-memory
    /// package conversion core.
    /// </para>
    /// </remarks>
    public static class OfficeDocConverter
    {
        private static readonly Regex XlsxInlineStringCellRegex = new Regex(
            "<c\\b(?=[^>]*\\bt=(?:\"inlineStr\"|'inlineStr'))[^>]*>.*?</c>",
            RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex XlsxTextNodeRegex = new Regex(
            "(<t\\b[^>]*>)(.*?)(</t>)",
            RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly ISet<string> SupportedFormatSet =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "docx", "xlsx", "pptx",
                "odt", "ods", "odp",
                "epub"
            };

        /// <summary>
        /// Gets the logical format names supported by this converter.
        /// </summary>
        /// <remarks>
        /// The returned collection is read-only. Use <see cref="IsSupportedFormat"/>
        /// for case-insensitive validation.
        /// </remarks>
        public static readonly IReadOnlyCollection<string> SupportedFormats =
            Array.AsReadOnly(new[] { "docx", "xlsx", "pptx", "odt", "ods", "odp", "epub" });

        /// <summary>
        /// Returns <c>true</c> if the specified format is supported by
        /// <see cref="OfficeDocConverter"/> (<c>docx/xlsx/pptx/odt/ods/odp/epub</c>).
        /// </summary>
        /// <param name="format">Logical format name (e.g. "docx"). Case-insensitive.</param>
        /// <returns>
        /// <see langword="true"/> if <paramref name="format"/> identifies a supported format;
        /// otherwise, <see langword="false"/>. Null, empty, and whitespace-only values return
        /// <see langword="false"/>.
        /// </returns>
        public static bool IsSupportedFormat(string format)
        {
            return !string.IsNullOrWhiteSpace(format) && SupportedFormatSet.Contains(format.Trim());
        }

        /// <summary>
        /// Converts an Office or EPUB document represented as a byte array and
        /// returns a fully reconstructed container with all textual content converted
        /// according to the specified <see cref="Opencc"/> configuration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method is the in-memory counterpart to
        /// <see cref="ConvertOfficeFile(string,string,OfficeFormat,Opencc,bool,bool)"/>.
        /// It does not create a temporary working directory or temporary package file.
        /// </para>
        /// <para>
        /// The input ZIP-based container (DOCX/XLSX/PPTX/ODT/ODS/ODP/EPUB) is read
        /// directly from memory. Text-bearing XML/XHTML entries are converted one at
        /// a time, while all other entries are streamed into a new in-memory archive.
        /// This avoids materializing the entire decompressed package at once.
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
        /// This value determines which XML/XHTML parts are inspected and how font
        /// preservation and conversion rules are applied.
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
        /// <exception cref="InvalidOperationException">
        /// Thrown when the container structure is invalid, the ZIP cannot be read,
        /// or the conversion pipeline fails.
        /// </exception>
        /// <example>
        /// Convert an EPUB in memory:
        /// <code>
        /// var epubBytes = File.ReadAllBytes("novel.epub");
        /// var cc = new Opencc("t2s");
        /// var converted = ConvertOfficeBytes(
        ///     epubBytes,
        ///     OfficeFormat.Epub,
        ///     cc,
        ///     punctuation: true);
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
            ValidateInputBytes(inputBytes);
            if (converter == null) throw new ArgumentNullException(nameof(converter));

            var result = ConvertOfficeBytesCore(inputBytes, format, converter, punctuation, keepFont);

            if (!result.Success || result.OutputBytes == null)
                throw new InvalidOperationException(result.Message, result.Error);

            return result.OutputBytes;
        }

        /// <summary>
        /// Converts an Office or EPUB document represented as a byte array and
        /// returns a fully reconstructed container with all textual content converted
        /// according to the specified <see cref="Opencc"/> configuration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the legacy string-based overload. It validates the format string
        /// and then delegates to the strongly typed overload
        /// <see cref="ConvertOfficeBytes(byte[],OfficeFormat,Opencc,bool,bool)"/>.
        /// For new code, prefer the <see cref="OfficeFormat"/> enum overload for
        /// stronger type safety.
        /// </para>
        /// <para>
        /// This method is the in-memory counterpart to
        /// <see cref="ConvertOfficeFile(string,string,string,Opencc,bool,bool)"/>
        /// and its enum-based overload
        /// <see cref="ConvertOfficeFile(string,string,OfficeFormat,Opencc,bool,bool)"/>.
        /// It does not create a temporary working directory or temporary package file.
        /// </para>
        /// <para>
        /// The input ZIP-based container is read directly from memory. Text-bearing
        /// XML/XHTML entries are converted one at a time, while all other entries are
        /// streamed into a new in-memory archive.
        /// </para>
        /// <para>
        /// If <paramref name="keepFont"/> is enabled, the converter temporarily
        /// annotates spans with protected font markers before text conversion
        /// and restores the original font-family declarations afterward.
        /// </para>
        /// </remarks>
        /// <param name="inputBytes">Raw bytes of the Office/EPUB container.</param>
        /// <param name="format">
        /// Normalized format identifier (e.g. <c>"docx"</c>, <c>"xlsx"</c>,
        /// <c>"pptx"</c>, <c>"odt"</c>, <c>"ods"</c>, <c>"odp"</c>, <c>"epub"</c>).
        /// Case-insensitive. Must be one of the supported format strings.
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
        /// Thrown when the container structure is invalid, the ZIP cannot be read,
        /// or the conversion pipeline fails.
        /// </exception>
        /// <example>
        /// Convert an EPUB in memory:
        /// <code>
        /// var epubBytes = File.ReadAllBytes("novel.epub");
        /// var cc = new Opencc("t2s");
        /// var converted = ConvertOfficeBytes(
        ///     epubBytes,
        ///     "epub",
        ///     cc,
        ///     punctuation: true);
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
            ValidateInputBytes(inputBytes);
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            format = ValidateFormat(format);

            var parsed = OfficeFormatUtils.ParseOfficeFormat(format);
            var result = ConvertOfficeBytesCore(inputBytes, parsed, converter, punctuation, keepFont);

            if (!result.Success || result.OutputBytes == null)
                throw new InvalidOperationException(result.Message, result.Error);

            return result.OutputBytes;
        }

        /// <summary>
        /// Asynchronously converts an Office or EPUB document represented as a byte array
        /// and returns the converted container as a byte array.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method mirrors <see cref="ConvertOfficeBytes(byte[],OfficeFormat,Opencc,bool,bool)"/>
        /// but performs the work asynchronously.  
        /// </para>
        /// <para>
        /// The underlying package conversion is synchronous and memory-based.
        /// This wrapper offloads that CPU-bound work to a background thread with
        /// <see cref="Task.Run(Action)"/> so callers can await it without blocking
        /// a UI or request-handling thread.
        /// </para>
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
        /// Optional cancellation token. Cancellation is honored before the background
        /// conversion task starts; once the synchronous conversion is running, it
        /// continues to completion.
        /// </param>
        /// <returns>
        /// A task that resolves to the converted Office/EPUB container bytes.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="inputBytes"/> or <paramref name="converter"/> is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the conversion process fails or the input container is invalid.
        /// </exception>
        /// <example>
        /// <code>
        /// byte[] result = await ConvertOfficeBytesAsync(
        ///     inputBytes,
        ///     OfficeFormat.Docx,
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
            cancellationToken.ThrowIfCancellationRequested();
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
        /// The underlying package conversion is synchronous and memory-based.
        /// This wrapper delegates that work to a background thread using
        /// <see cref="Task.Run(Action)"/> so callers can await it without blocking
        /// the calling thread.
        /// </para>
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
        /// Optional cancellation token. Cancellation is honored before the background
        /// conversion task starts; once the synchronous conversion is running, it
        /// continues to completion.
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
        /// For new code, prefer the OfficeFormat overload for stronger type safety.
        /// </example>
        public static Task<byte[]> ConvertOfficeBytesAsync(
            byte[] inputBytes,
            string format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            format = ValidateFormat(format);
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
        /// This value determines which XML/XHTML parts are inspected and how font
        /// preservation and conversion rules are applied.
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
        /// ConvertOfficeFile(
        ///     "input.docx",
        ///     "out.docx",
        ///     OfficeFormat.Docx,
        ///     cc,
        ///     punctuation: true);
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
            ValidatePath(inputPath, nameof(inputPath));
            ValidatePath(outputPath, nameof(outputPath));
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Input file not found.", inputPath);

            var bytes = File.ReadAllBytes(inputPath);
            var output = ConvertOfficeBytes(bytes, format, converter, punctuation, keepFont);

            WriteAllBytesAtomic(outputPath, output);
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
        /// This overload accepts a raw string and is preserved for backward compatibility.
        /// For new code, prefer the OfficeFormat enum overload.
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
            ValidatePath(inputPath, nameof(inputPath));
            ValidatePath(outputPath, nameof(outputPath));
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            format = ValidateFormat(format);

            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Input file not found.", inputPath);

            var inputBytes = File.ReadAllBytes(inputPath);
            var parsed = OfficeFormatUtils.ParseOfficeFormat(format);
            var outputBytes = ConvertOfficeBytes(inputBytes, parsed, converter, punctuation, keepFont);

            WriteAllBytesAtomic(outputPath, outputBytes);
        }

        /// <summary>
        /// Asynchronously converts an Office or EPUB document and writes the
        /// converted result to the specified output file.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method is an asynchronous wrapper around the synchronous file
        /// conversion pipeline and delegates the work to a background thread using
        /// <see cref="Task.Run(Action)"/>.
        /// </para>
        /// <para>
        /// The behavior and conversion rules are identical to
        /// <see cref="ConvertOfficeFile(string,string,OfficeFormat,Opencc,bool,bool)"/>.
        /// and its string-based overload
        /// <see cref="ConvertOfficeFile(string,string,string,Opencc,bool,bool)"/>.
        /// </para>
        /// <para>
        /// This wrapper is useful when synchronous package conversion should not
        /// occupy the calling thread.
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
        /// Cancellation is honored before the background conversion task starts;
        /// once the synchronous conversion is running, it continues to completion.
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
            cancellationToken.ThrowIfCancellationRequested();
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
        /// This method is an asynchronous wrapper around the synchronous file
        /// conversion pipeline and delegates the work to a background thread using
        /// <see cref="Task.Run(Action)"/>.
        /// </para>
        /// <para>
        /// The behavior and conversion rules are identical to
        /// <see cref="ConvertOfficeFile(string,string,string,Opencc,bool,bool)"/>.
        /// </para>
        /// <para>
        /// This wrapper is useful when synchronous package conversion should not
        /// occupy the calling thread.
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
        /// Cancellation is honored before the background conversion task starts;
        /// once the synchronous conversion is running, it continues to completion.
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
            cancellationToken.ThrowIfCancellationRequested();
            format = ValidateFormat(format);
            var parsed = OfficeFormatUtils.ParseOfficeFormat(format);
            return Task.Run(
                () => { ConvertOfficeFile(inputPath, outputPath, parsed, converter, punctuation, keepFont); },
                cancellationToken);
        }

        /// <summary>
        /// Core in-memory conversion engine for Office/EPUB ZIP containers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The input package is opened directly from <paramref name="inputBytes"/> and
        /// rebuilt into a new <see cref="MemoryStream"/> without extracting the archive
        /// to the filesystem. Entries are processed sequentially so non-text assets can
        /// be streamed directly from the input archive to the output archive.
        /// </para>
        /// <para>
        /// Only text-bearing XML/XHTML entries selected for the specified
        /// <see cref="OfficeFormat"/> are materialized as strings. Those entries are
        /// converted with <see cref="Opencc"/>, while all other entries are copied
        /// unchanged at the payload level and repackaged into the new container.
        /// </para>
        /// <para>
        /// EPUB output follows the required packaging rule: the <c>mimetype</c> entry
        /// is emitted first and stored without compression.
        /// </para>
        /// <para>
        /// This method never throws to its caller. Exceptions are captured in the
        /// returned <see cref="CoreResult"/> so the public APIs can expose a consistent
        /// <see cref="InvalidOperationException"/> contract.
        /// </para>
        /// </remarks>
        /// <param name="inputBytes">Raw ZIP container bytes from the input document.</param>
        /// <param name="format">The strongly typed Office/EPUB document format.</param>
        /// <param name="converter">The active <see cref="Opencc"/> converter.</param>
        /// <param name="punctuation">Whether punctuation conversion should be applied.</param>
        /// <param name="keepFont">
        /// Whether supported font declarations should be protected with temporary
        /// markers while text conversion is performed.
        /// </param>
        /// <returns>
        /// A <see cref="CoreResult"/> containing the conversion status, diagnostic
        /// message, generated package bytes on success, and any captured exception.
        /// </returns>
        private static CoreResult ConvertOfficeBytesCore(
            byte[] inputBytes,
            OfficeFormat format,
            Opencc converter,
            bool punctuation,
            bool keepFont)
        {
            var formatId = OfficeFormatUtils.OfficeFormatToString(format);

            try
            {
                using (var inputStream = new MemoryStream(
                           inputBytes,
                           0,
                           inputBytes.Length,
                           writable: false,
                           publiclyVisible: false))
                using (var inputArchive = new ZipArchive(inputStream, ZipArchiveMode.Read, leaveOpen: false))
                using (var outputStream = new MemoryStream())
                {
                    var convertedCount = 0;

                    using (var outputArchive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        if (format == OfficeFormat.Epub)
                        {
                            var mimetypeEntry = FindEpubMimetypeEntry(inputArchive);
                            if (mimetypeEntry == null)
                            {
                                return new CoreResult
                                {
                                    Success = false,
                                    Message =
                                        "'mimetype' file is missing; a valid EPUB requires it as the first entry.",
                                    OutputBytes = null
                                };
                            }

                            CopyEntry(
                                mimetypeEntry,
                                outputArchive,
                                CompressionLevel.NoCompression);

                            foreach (var entry in inputArchive.Entries)
                            {
                                // Emit exactly one canonical mimetype entry first.
                                if (string.Equals(entry.FullName, "mimetype", StringComparison.Ordinal))
                                    continue;

                                ProcessEntry(
                                    entry,
                                    outputArchive,
                                    format,
                                    converter,
                                    punctuation,
                                    keepFont,
                                    ref convertedCount);
                            }
                        }
                        else
                        {
                            foreach (var entry in inputArchive.Entries)
                            {
                                ProcessEntry(
                                    entry,
                                    outputArchive,
                                    format,
                                    converter,
                                    punctuation,
                                    keepFont,
                                    ref convertedCount);
                            }
                        }
                    }

                    if (convertedCount == 0)
                    {
                        return new CoreResult
                        {
                            Success = false,
                            Message = "No convertible XML/XHTML fragments found for format '" + formatId + "'.",
                            OutputBytes = null
                        };
                    }

                    var resultBytes = outputStream.ToArray();
                    ValidateZipBytes(resultBytes);

                    return new CoreResult
                    {
                        Success = true,
                        Message = "Converted " + convertedCount + " fragment(s) successfully.",
                        OutputBytes = resultBytes
                    };
                }
            }
            catch (Exception ex)
            {
                return new CoreResult
                {
                    Success = false,
                    Message = "Conversion failed: " + ex.Message,
                    OutputBytes = null,
                    Error = ex
                };
            }
        }

        /// <summary>
        /// Processes one ZIP entry and writes its counterpart to the output package.
        /// </summary>
        /// <remarks>
        /// Directory and non-target entries are streamed directly. Text-bearing target
        /// entries are decoded as UTF-8, converted, and then written back as UTF-8.
        /// </remarks>
        private static void ProcessEntry(
            ZipArchiveEntry inputEntry,
            ZipArchive outputArchive,
            OfficeFormat format,
            Opencc converter,
            bool punctuation,
            bool keepFont,
            ref int convertedCount)
        {
            if (IsDirectoryEntry(inputEntry) ||
                !ShouldConvertEntry(format, inputEntry.FullName))
            {
                CopyEntry(inputEntry, outputArchive, CompressionLevel.Optimal);
                return;
            }

            var xmlContent = ReadEntryText(inputEntry);
            var convertedXml = ConvertTextEntry(
                xmlContent,
                format,
                inputEntry.FullName,
                converter,
                punctuation,
                keepFont);

            WriteTextEntry(
                outputArchive,
                inputEntry,
                convertedXml,
                CompressionLevel.Optimal);

            convertedCount++;
        }

        /// <summary>
        /// Determines whether a ZIP entry contains text that should be converted for
        /// the specified document format.
        /// </summary>
        private static bool ShouldConvertEntry(OfficeFormat format, string entryName)
        {
            if (string.IsNullOrEmpty(entryName))
                return false;

            var normalizedPath = entryName.Replace('\\', '/');

            switch (format)
            {
                case OfficeFormat.Docx:
                    return string.Equals(
                        normalizedPath,
                        "word/document.xml",
                        StringComparison.OrdinalIgnoreCase);

                case OfficeFormat.Xlsx:
                    return string.Equals(
                               normalizedPath,
                               "xl/sharedStrings.xml",
                               StringComparison.OrdinalIgnoreCase) ||
                           (normalizedPath.StartsWith(
                                "xl/worksheets/",
                                StringComparison.OrdinalIgnoreCase) &&
                            normalizedPath.EndsWith(
                                ".xml",
                                StringComparison.OrdinalIgnoreCase));

                case OfficeFormat.Pptx:
                    if (!normalizedPath.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase) ||
                        !normalizedPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    var fileName = GetZipEntryFileName(normalizedPath);
                    return fileName.StartsWith("slide", StringComparison.OrdinalIgnoreCase) ||
                           normalizedPath.IndexOf("notesSlide", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           normalizedPath.IndexOf("slideMaster", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           normalizedPath.IndexOf("slideLayout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           normalizedPath.IndexOf("comment", StringComparison.OrdinalIgnoreCase) >= 0;

                case OfficeFormat.Odt:
                case OfficeFormat.Ods:
                case OfficeFormat.Odp:
                    return string.Equals(
                        normalizedPath,
                        "content.xml",
                        StringComparison.OrdinalIgnoreCase);

                case OfficeFormat.Epub:
                    return normalizedPath.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase) ||
                           normalizedPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                           normalizedPath.EndsWith(".opf", StringComparison.OrdinalIgnoreCase) ||
                           normalizedPath.EndsWith(".ncx", StringComparison.OrdinalIgnoreCase);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Converts one selected XML/XHTML entry while preserving protected font
        /// declarations when requested.
        /// </summary>
        private static string ConvertTextEntry(
            string xmlContent,
            OfficeFormat format,
            string entryName,
            Opencc converter,
            bool punctuation,
            bool keepFont)
        {
            Dictionary<string, string> fontMap = null;

            if (keepFont && ShouldMaskFonts(format, entryName))
            {
                var pattern = GetFontMaskPattern(format);
                if (pattern != null)
                {
                    fontMap = new Dictionary<string, string>();
                    var fontCounter = 0;

                    xmlContent = Regex.Replace(
                        xmlContent,
                        pattern,
                        delegate(Match match)
                        {
                            var marker = "__F_O_N_T_" + (fontCounter++) + "__";
                            fontMap[marker] = match.Groups[2].Value;

                            var tail = match.Groups.Count >= 4
                                ? match.Groups[3].Value
                                : string.Empty;

                            return match.Groups[1].Value + marker + tail;
                        });
                }
            }

            var convertedXml = format == OfficeFormat.Xlsx
                ? ConvertXlsxXmlPart(xmlContent, entryName, converter, punctuation)
                : converter.Convert(xmlContent, punctuation);

            if (convertedXml == null)
                throw new InvalidOperationException("OpenCC conversion returned null.");

            if (fontMap != null)
            {
                foreach (var pair in fontMap)
                    convertedXml = convertedXml.Replace(pair.Key, pair.Value);
            }

            return convertedXml;
        }

        /// <summary>
        /// Returns the format-specific regular expression used to protect font
        /// declarations during text conversion.
        /// </summary>
        private static string GetFontMaskPattern(OfficeFormat format)
        {
            switch (format)
            {
                case OfficeFormat.Docx:
                    return @"(w:eastAsia=""|w:ascii=""|w:hAnsi=""|w:cs="")(.*?)("")";

                case OfficeFormat.Xlsx:
                    return @"(val="")(.*?)("")";

                case OfficeFormat.Pptx:
                    return @"(typeface="")(.*?)("")";

                case OfficeFormat.Odt:
                case OfficeFormat.Ods:
                case OfficeFormat.Odp:
                    return
                        @"((?:style:font-name(?:-asian|-complex)?|svg:font-family|style:name)=[""'])([^""']+)([""'])";

                case OfficeFormat.Epub:
                    return @"(font-family\s*:\s*)([^;""']+)([;""'])?";

                default:
                    return null;
            }
        }

        /// <summary>
        /// Reads one ZIP entry as UTF-8 text, honoring a leading byte-order mark when
        /// present.
        /// </summary>
        private static string ReadEntryText(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            using (var reader = new StreamReader(
                       stream,
                       Encoding.UTF8,
                       detectEncodingFromByteOrderMarks: true))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// Writes converted UTF-8 text to a newly created ZIP entry.
        /// </summary>
        /// <remarks>
        /// <see cref="Encoding.UTF8"/> is intentionally used to retain the historical
        /// encoding behavior of the former <c>File.WriteAllText(..., Encoding.UTF8)</c>
        /// implementation.
        /// </remarks>
        private static void WriteTextEntry(
            ZipArchive outputArchive,
            ZipArchiveEntry sourceEntry,
            string text,
            CompressionLevel compressionLevel)
        {
            var outputEntry = CreateOutputEntry(
                outputArchive,
                sourceEntry,
                compressionLevel);

            using (var stream = outputEntry.Open())
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                writer.Write(text);
            }
        }

        /// <summary>
        /// Streams one ZIP entry directly into the output package.
        /// </summary>
        private static void CopyEntry(
            ZipArchiveEntry inputEntry,
            ZipArchive outputArchive,
            CompressionLevel compressionLevel)
        {
            var outputEntry = CreateOutputEntry(
                outputArchive,
                inputEntry,
                compressionLevel);

            if (IsDirectoryEntry(inputEntry))
                return;

            using (var input = inputEntry.Open())
            using (var output = outputEntry.Open())
            {
                input.CopyTo(output);
            }
        }

        /// <summary>
        /// Creates the output entry corresponding to an input entry and preserves its
        /// ZIP timestamp where possible.
        /// </summary>
        private static ZipArchiveEntry CreateOutputEntry(
            ZipArchive outputArchive,
            ZipArchiveEntry sourceEntry,
            CompressionLevel compressionLevel)
        {
            var outputEntry = outputArchive.CreateEntry(
                sourceEntry.FullName,
                compressionLevel);

            try
            {
                outputEntry.LastWriteTime = sourceEntry.LastWriteTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                // Keep ZipArchive's default timestamp if the source value cannot
                // be represented by the output ZIP implementation.
            }

            return outputEntry;
        }

        /// <summary>
        /// Finds the canonical EPUB <c>mimetype</c> entry.
        /// </summary>
        private static ZipArchiveEntry FindEpubMimetypeEntry(ZipArchive archive)
        {
            foreach (var entry in archive.Entries)
            {
                if (string.Equals(entry.FullName, "mimetype", StringComparison.Ordinal))
                    return entry;
            }

            return null;
        }

        /// <summary>
        /// Returns whether an archive entry represents a directory.
        /// </summary>
        private static bool IsDirectoryEntry(ZipArchiveEntry entry)
        {
            return string.IsNullOrEmpty(entry.Name) ||
                   entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                   entry.FullName.EndsWith("\\", StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns the final path component of a normalized ZIP entry name.
        /// </summary>
        private static string GetZipEntryFileName(string normalizedPath)
        {
            var slash = normalizedPath.LastIndexOf('/');
            return slash >= 0
                ? normalizedPath.Substring(slash + 1)
                : normalizedPath;
        }

        private static bool ShouldMaskFonts(OfficeFormat format, string relativePath)
        {
            if (format != OfficeFormat.Xlsx)
                return true;

            var normalizedPath = relativePath.Replace('\\', '/');
            return string.Equals(normalizedPath, "xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase);
        }

        private static string ConvertXlsxXmlPart(
            string xmlContent,
            string relativePath,
            Opencc converter,
            bool punctuation)
        {
            var normalizedPath = relativePath.Replace('\\', '/');

            if (string.Equals(normalizedPath, "xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase))
                return converter.Convert(xmlContent, punctuation);

            if (normalizedPath.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                normalizedPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                return XlsxInlineStringCellRegex.Replace(xmlContent, delegate(Match cellMatch)
                {
                    var cellXml = cellMatch.Value;

                    return XlsxTextNodeRegex.Replace(cellXml, delegate(Match textMatch)
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

        /// <summary>Validates that an in-memory package was supplied.</summary>
        /// <param name="inputBytes">The package bytes to validate.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="inputBytes"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="inputBytes"/> is empty.</exception>
        private static void ValidateInputBytes(byte[] inputBytes)
        {
            if (inputBytes == null)
                throw new ArgumentNullException(nameof(inputBytes));
            if (inputBytes.Length == 0)
                throw new ArgumentException("Input package bytes must not be empty.", nameof(inputBytes));
        }

        /// <summary>Validates a public file path argument.</summary>
        /// <param name="path">The path to validate.</param>
        /// <param name="paramName">The public parameter name used by validation exceptions.</param>
        private static void ValidatePath(string path, string paramName)
        {
            if (path == null)
                throw new ArgumentNullException(paramName);
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path must not be empty or whitespace.", paramName);
        }

        /// <summary>Validates and trims a logical Office or EPUB format name.</summary>
        /// <param name="format">The logical format name.</param>
        /// <returns>The trimmed format name.</returns>
        private static string ValidateFormat(string format)
        {
            if (format == null)
                throw new ArgumentNullException(nameof(format));

            var normalized = format.Trim();
            if (normalized.Length == 0)
                throw new ArgumentException("Format must not be empty or whitespace.", nameof(format));
            if (!IsSupportedFormat(normalized))
                throw new ArgumentException("Unsupported Office/EPUB format: '" + normalized + "'.", nameof(format));

            return normalized;
        }

        /// <summary>Confirms that generated bytes contain a readable ZIP package.</summary>
        /// <param name="bytes">The generated package bytes.</param>
        private static void ValidateZipBytes(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes, writable: false))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var entryCount = archive.Entries.Count;
            }
        }

        /// <summary>Writes to a sibling temporary file and atomically publishes the completed output.</summary>
        /// <param name="outputPath">The final output path.</param>
        /// <param name="bytes">The fully generated and validated package bytes.</param>
        private static void WriteAllBytesAtomic(string outputPath, byte[] bytes)
        {
            var fullOutputPath = Path.GetFullPath(outputPath);
            var outputDirectory = Path.GetDirectoryName(fullOutputPath);
            if (string.IsNullOrEmpty(outputDirectory))
                throw new ArgumentException("Output path must include a valid directory.", nameof(outputPath));

            Directory.CreateDirectory(outputDirectory);
            var tempPath = Path.Combine(
                outputDirectory,
                "." + Path.GetFileName(fullOutputPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(fullOutputPath))
                    File.Replace(tempPath, fullOutputPath, null);
                else
                    File.Move(tempPath, fullOutputPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        // =====================================================================
        // Internal in-memory ZIP + XML/XHTML conversion pipeline
        // =====================================================================

        private struct CoreResult
        {
            public bool Success;
            public string Message;
            public byte[] OutputBytes;
            public Exception Error;
        }
    }
}