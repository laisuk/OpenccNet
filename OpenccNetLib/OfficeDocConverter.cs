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
    /// are not semantically modified, and their payload content is preserved.
    /// </para>
    /// <para>
    /// Use this enumeration with either the <see cref="Opencc"/> convenience overloads
    /// or the <see cref="OfficeTextConverter"/> delegate overloads for strongly typed
    /// format selection.
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
        /// Text is stored primarily in <c>xl/sharedStrings.xml</c> and inline-string worksheet cells.
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
    /// Represents a caller-supplied transformation for text-bearing content inside
    /// an Office or EPUB package.
    /// </summary>
    /// <param name="text">The decoded text fragment selected for conversion.</param>
    /// <returns>
    /// The transformed text. Implementations must return a non-null string.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <see cref="OfficeDocConverter"/> owns package parsing, ZIP reconstruction,
    /// entry selection, XLSX inline-string handling, EPUB packaging rules, and optional
    /// font protection. The delegate owns only the text transformation itself.
    /// </para>
    /// <para>
    /// This allows callers to compose OpenCC conversion with preprocessing or
    /// postprocessing steps such as compatibility normalization or DeToFu without
    /// coupling the Office/EPUB package layer to those policies.
    /// </para>
    /// </remarks>
    public delegate string OfficeTextConverter(string text);

    /// <summary>
    /// Provides high-level APIs for converting text-bearing content inside Office and
    /// EPUB packages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Callers may supply either an <see cref="Opencc"/> instance through the convenience
    /// overloads or an <see cref="OfficeTextConverter"/> delegate through the extensible
    /// overloads. All overloads share the same ZIP/XML/XHTML conversion core.
    /// </para>
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
    /// The <c>byte[]</c> APIs are intentionally pure in-memory APIs: the caller supplies
    /// the complete package bytes and receives a newly allocated converted package.
    /// They are well suited to web, server, IPC, and other memory-oriented workflows.
    /// </para>
    /// <para>
    /// The file-based APIs use a separate streaming file-I/O path. The input package is
    /// read directly from a <see cref="FileStream"/>. Only selected XML/XHTML entries are
    /// materialized as strings for conversion.
    /// </para>
    /// <para>
    /// The rebuilt package is written to a sibling temporary file. After conversion, the
    /// temporary package is validated and then published to the requested output path.
    /// The complete input or output package is never materialized as a managed
    /// <c>byte[]</c> during normal file conversion.
    /// </para>
    /// </remarks>
    public static class OfficeDocConverter
    {
        private static readonly Regex XlsxInlineStringCellRegex = new(
            "<c\\b(?=[^>]*\\bt=(?:\"inlineStr\"|'inlineStr'))[^>]*>.*?</c>",
            RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex XlsxTextNodeRegex = new(
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

        // =====================================================================
        // Public delegate-based APIs
        // =====================================================================

        /// <summary>
        /// Converts an Office or EPUB package in memory by applying a caller-supplied
        /// text transformation to its convertible content.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the primary extensible in-memory API. The delegate receives only the
        /// text fragments selected by the package layer; it does not need to understand
        /// ZIP structure, document formats, or font protection.
        /// </para>
        /// <para>
        /// The complete source and rebuilt package are memory-resident. For large files
        /// backed by the filesystem, prefer <see cref="ConvertOfficeFile(string,string,OfficeFormat,OfficeTextConverter,bool)"/>.
        /// </para>
        /// </remarks>
        /// <param name="inputBytes">Raw bytes of the Office or EPUB package.</param>
        /// <param name="format">The strongly typed Office or EPUB container format.</param>
        /// <param name="textConverter">
        /// The text transformation applied to convertible document content.
        /// </param>
        /// <param name="keepFont">
        /// Whether supported font declarations should be protected and restored while
        /// text transformation is performed.
        /// </param>
        /// <returns>A newly allocated byte array containing the rebuilt package.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="inputBytes"/> or <paramref name="textConverter"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="inputBytes"/> is empty.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the package is invalid, no convertible content is found, or the
        /// supplied text converter fails.
        /// </exception>
        public static byte[] ConvertOfficeBytes(
            byte[] inputBytes,
            OfficeFormat format,
            OfficeTextConverter textConverter,
            bool keepFont = false)
        {
            ValidateInputBytes(inputBytes);
            if (textConverter == null) throw new ArgumentNullException(nameof(textConverter));

            var result = ConvertOfficeBytesCore(inputBytes, format, textConverter, keepFont);

            if (!result.Success || result.OutputBytes == null)
                throw new InvalidOperationException(result.Message, result.Error);

            return result.OutputBytes;
        }

        /// <summary>
        /// Converts an Office or EPUB package in memory using a caller-supplied text
        /// transformation and a case-insensitive format name.
        /// </summary>
        /// <remarks>
        /// This string-based overload is retained for callers that resolve formats at
        /// runtime. New code with a known format should prefer the <see cref="OfficeFormat"/>
        /// overload for stronger type safety.
        /// </remarks>
        /// <param name="inputBytes">Raw bytes of the Office or EPUB package.</param>
        /// <param name="format">Format name such as <c>docx</c>, <c>xlsx</c>, or <c>epub</c>.</param>
        /// <param name="textConverter">The text transformation applied to convertible content.</param>
        /// <param name="keepFont">Whether supported font declarations should be preserved.</param>
        /// <returns>A newly allocated byte array containing the rebuilt package.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="inputBytes"/>, <paramref name="format"/>, or
        /// <paramref name="textConverter"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when the input is empty or <paramref name="format"/> is unsupported.
        /// </exception>
        /// <exception cref="InvalidOperationException">Thrown when package conversion fails.</exception>
        public static byte[] ConvertOfficeBytes(
            byte[] inputBytes,
            string format,
            OfficeTextConverter textConverter,
            bool keepFont = false)
        {
            ValidateInputBytes(inputBytes);
            if (textConverter == null) throw new ArgumentNullException(nameof(textConverter));
            format = ValidateFormat(format);

            return ConvertOfficeBytes(
                inputBytes,
                OfficeFormatUtils.ParseOfficeFormat(format),
                textConverter,
                keepFont);
        }

        /// <summary>
        /// Asynchronously converts an in-memory Office or EPUB package using a
        /// caller-supplied text transformation.
        /// </summary>
        /// <remarks>
        /// The underlying package conversion is synchronous and CPU-bound. This method
        /// uses <see cref="Task.Run(Action)"/> semantics so UI or request-handling code
        /// can await it without occupying the calling thread. Cancellation is honored
        /// before the conversion task begins; once conversion is running, it completes normally.
        /// </remarks>
        /// <param name="inputBytes">Raw bytes of the Office or EPUB package.</param>
        /// <param name="format">The strongly typed Office or EPUB container format.</param>
        /// <param name="textConverter">The text transformation applied to convertible content.</param>
        /// <param name="keepFont">Whether supported font declarations should be preserved.</param>
        /// <param name="cancellationToken">Token used to cancel before conversion starts.</param>
        /// <returns>A task that resolves to the rebuilt package bytes.</returns>
        public static Task<byte[]> ConvertOfficeBytesAsync(
            byte[] inputBytes,
            OfficeFormat format,
            OfficeTextConverter textConverter,
            bool keepFont = false,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.Run(
                () => ConvertOfficeBytes(inputBytes, format, textConverter, keepFont),
                cancellationToken);
        }

        /// <summary>
        /// Asynchronously converts an in-memory Office or EPUB package using a
        /// caller-supplied text transformation and a format name.
        /// </summary>
        /// <param name="inputBytes">Raw bytes of the Office or EPUB package.</param>
        /// <param name="format">Format name such as <c>docx</c>, <c>xlsx</c>, or <c>epub</c>.</param>
        /// <param name="textConverter">The text transformation applied to convertible content.</param>
        /// <param name="keepFont">Whether supported font declarations should be preserved.</param>
        /// <param name="cancellationToken">Token used to cancel before conversion starts.</param>
        /// <returns>A task that resolves to the rebuilt package bytes.</returns>
        public static Task<byte[]> ConvertOfficeBytesAsync(
            byte[] inputBytes,
            string format,
            OfficeTextConverter textConverter,
            bool keepFont = false,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            format = ValidateFormat(format);
            return ConvertOfficeBytesAsync(
                inputBytes,
                OfficeFormatUtils.ParseOfficeFormat(format),
                textConverter,
                keepFont,
                cancellationToken);
        }

        /// <summary>
        /// Converts a filesystem-backed Office or EPUB package using a caller-supplied
        /// text transformation and writes the rebuilt package to disk.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the primary extensible file API. The source archive is streamed from
        /// disk and the rebuilt archive is written to a sibling temporary file. Only
        /// selected text-bearing entries are materialized as strings.
        /// </para>
        /// <para>
        /// After conversion, the temporary package is validated and atomically published
        /// to <paramref name="outputPath"/>. Non-target package content is preserved.
        /// </para>
        /// </remarks>
        /// <param name="inputPath">Path to the source Office or EPUB package.</param>
        /// <param name="outputPath">Path where the rebuilt package will be written.</param>
        /// <param name="format">The strongly typed Office or EPUB container format.</param>
        /// <param name="textConverter">The text transformation applied to convertible content.</param>
        /// <param name="keepFont">Whether supported font declarations should be preserved.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when a required path or <paramref name="textConverter"/> is null.
        /// </exception>
        /// <exception cref="FileNotFoundException">Thrown when the input file does not exist.</exception>
        /// <exception cref="InvalidOperationException">Thrown when package conversion fails.</exception>
        public static void ConvertOfficeFile(
            string inputPath,
            string outputPath,
            OfficeFormat format,
            OfficeTextConverter textConverter,
            bool keepFont = false)
        {
            ValidatePath(inputPath, nameof(inputPath));
            ValidatePath(outputPath, nameof(outputPath));
            if (textConverter == null) throw new ArgumentNullException(nameof(textConverter));
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Input file not found.", inputPath);

            ConvertOfficeFileCore(
                inputPath,
                outputPath,
                format,
                textConverter,
                keepFont);
        }

        /// <summary>
        /// Converts a filesystem-backed Office or EPUB package using a caller-supplied
        /// text transformation and a case-insensitive format name.
        /// </summary>
        /// <param name="inputPath">Path to the source Office or EPUB package.</param>
        /// <param name="outputPath">Path where the rebuilt package will be written.</param>
        /// <param name="format">Format name such as <c>docx</c>, <c>xlsx</c>, or <c>epub</c>.</param>
        /// <param name="textConverter">The text transformation applied to convertible content.</param>
        /// <param name="keepFont">Whether supported font declarations should be preserved.</param>
        public static void ConvertOfficeFile(
            string inputPath,
            string outputPath,
            string format,
            OfficeTextConverter textConverter,
            bool keepFont = false)
        {
            ValidatePath(inputPath, nameof(inputPath));
            ValidatePath(outputPath, nameof(outputPath));
            if (textConverter == null) throw new ArgumentNullException(nameof(textConverter));
            format = ValidateFormat(format);

            ConvertOfficeFile(
                inputPath,
                outputPath,
                OfficeFormatUtils.ParseOfficeFormat(format),
                textConverter,
                keepFont);
        }

        /// <summary>
        /// Asynchronously converts a filesystem-backed Office or EPUB package using a
        /// caller-supplied text transformation.
        /// </summary>
        /// <remarks>
        /// The synchronous streaming file conversion is executed on a background thread.
        /// Cancellation is honored before that work begins.
        /// </remarks>
        /// <param name="inputPath">Path to the source package.</param>
        /// <param name="outputPath">Path where the rebuilt package will be written.</param>
        /// <param name="format">The strongly typed Office or EPUB container format.</param>
        /// <param name="textConverter">The text transformation applied to convertible content.</param>
        /// <param name="keepFont">Whether supported font declarations should be preserved.</param>
        /// <param name="cancellationToken">Token used to cancel before conversion starts.</param>
        /// <returns>A task representing the conversion operation.</returns>
        public static Task ConvertOfficeFileAsync(
            string inputPath,
            string outputPath,
            OfficeFormat format,
            OfficeTextConverter textConverter,
            bool keepFont = false,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.Run(
                () => ConvertOfficeFile(inputPath, outputPath, format, textConverter, keepFont),
                cancellationToken);
        }

        /// <summary>
        /// Asynchronously converts a filesystem-backed Office or EPUB package using a
        /// caller-supplied text transformation and a format name.
        /// </summary>
        /// <param name="inputPath">Path to the source package.</param>
        /// <param name="outputPath">Path where the rebuilt package will be written.</param>
        /// <param name="format">Format name such as <c>docx</c>, <c>xlsx</c>, or <c>epub</c>.</param>
        /// <param name="textConverter">The text transformation applied to convertible content.</param>
        /// <param name="keepFont">Whether supported font declarations should be preserved.</param>
        /// <param name="cancellationToken">Token used to cancel before conversion starts.</param>
        /// <returns>A task representing the conversion operation.</returns>
        public static Task ConvertOfficeFileAsync(
            string inputPath,
            string outputPath,
            string format,
            OfficeTextConverter textConverter,
            bool keepFont = false,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            format = ValidateFormat(format);
            return ConvertOfficeFileAsync(
                inputPath,
                outputPath,
                OfficeFormatUtils.ParseOfficeFormat(format),
                textConverter,
                keepFont,
                cancellationToken);
        }

        // =====================================================================
        // Opencc convenience and compatibility overloads
        // =====================================================================

        /// <summary>
        /// Converts an Office or EPUB package in memory using an initialized
        /// <see cref="Opencc"/> instance.
        /// </summary>
        /// <remarks>
        /// This convenience overload preserves the established OpenccNetLib API and
        /// adapts <paramref name="converter"/> to the delegate-based conversion core.
        /// </remarks>
        /// <param name="inputBytes">Raw bytes of the Office or EPUB package.</param>
        /// <param name="format">The strongly typed Office or EPUB container format.</param>
        /// <param name="converter">The initialized OpenCC converter.</param>
        /// <param name="punctuation">Whether OpenCC punctuation conversion is enabled.</param>
        /// <param name="keepFont">Whether supported font declarations should be preserved.</param>
        /// <returns>A newly allocated byte array containing the rebuilt package.</returns>
        public static byte[] ConvertOfficeBytes(
            byte[] inputBytes,
            OfficeFormat format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            return ConvertOfficeBytes(
                inputBytes,
                format,
                text => converter.Convert(text, punctuation),
                keepFont);
        }

        /// <summary>
        /// Converts an Office or EPUB package in memory using an initialized
        /// <see cref="Opencc"/> instance and a format name.
        /// </summary>
        /// <remarks>
        /// This is the legacy string-format convenience overload. New code with a known
        /// format should prefer the <see cref="OfficeFormat"/> overload.
        /// </remarks>
        /// <param name="inputBytes">Raw bytes of the Office or EPUB package.</param>
        /// <param name="format">Case-insensitive format name.</param>
        /// <param name="converter">The initialized OpenCC converter.</param>
        /// <param name="punctuation">Whether OpenCC punctuation conversion is enabled.</param>
        /// <param name="keepFont">Whether supported font declarations should be preserved.</param>
        /// <returns>A newly allocated byte array containing the rebuilt package.</returns>
        public static byte[] ConvertOfficeBytes(
            byte[] inputBytes,
            string format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            return ConvertOfficeBytes(
                inputBytes,
                format,
                text => converter.Convert(text, punctuation),
                keepFont);
        }

        /// <summary>
        /// Asynchronously converts an in-memory Office or EPUB package using an
        /// initialized <see cref="Opencc"/> instance.
        /// </summary>
        /// <param name="inputBytes">Raw bytes of the Office or EPUB package.</param>
        /// <param name="format">The strongly typed Office or EPUB container format.</param>
        /// <param name="converter">The initialized OpenCC converter.</param>
        /// <param name="punctuation">Whether OpenCC punctuation conversion is enabled.</param>
        /// <param name="keepFont">Whether supported font declarations should be preserved.</param>
        /// <param name="cancellationToken">Token used to cancel before conversion starts.</param>
        /// <returns>A task that resolves to the rebuilt package bytes.</returns>
        public static Task<byte[]> ConvertOfficeBytesAsync(
            byte[] inputBytes,
            OfficeFormat format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false,
            CancellationToken cancellationToken = default)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            return ConvertOfficeBytesAsync(
                inputBytes,
                format,
                text => converter.Convert(text, punctuation),
                keepFont,
                cancellationToken);
        }

        /// <summary>
        /// Asynchronously converts an in-memory Office or EPUB package using an
        /// initialized <see cref="Opencc"/> instance and a format name.
        /// </summary>
        /// <param name="inputBytes">Raw bytes of the Office or EPUB package.</param>
        /// <param name="format">Case-insensitive format name.</param>
        /// <param name="converter">The initialized OpenCC converter.</param>
        /// <param name="punctuation">Whether OpenCC punctuation conversion is enabled.</param>
        /// <param name="keepFont">Whether supported font declarations should be preserved.</param>
        /// <param name="cancellationToken">Token used to cancel before conversion starts.</param>
        /// <returns>A task that resolves to the rebuilt package bytes.</returns>
        public static Task<byte[]> ConvertOfficeBytesAsync(
            byte[] inputBytes,
            string format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false,
            CancellationToken cancellationToken = default)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            return ConvertOfficeBytesAsync(
                inputBytes,
                format,
                text => converter.Convert(text, punctuation),
                keepFont,
                cancellationToken);
        }

        /// <summary>
        /// Converts a filesystem-backed Office or EPUB package using an initialized
        /// <see cref="Opencc"/> instance.
        /// </summary>
        /// <remarks>
        /// This convenience overload preserves the established OpenccNetLib API and
        /// forwards OpenCC conversion through the delegate-based streaming core.
        /// </remarks>
        /// <param name="inputPath">Path to the source Office or EPUB package.</param>
        /// <param name="outputPath">Path where the rebuilt package will be written.</param>
        /// <param name="format">The strongly typed Office or EPUB container format.</param>
        /// <param name="converter">The initialized OpenCC converter.</param>
        /// <param name="punctuation">Whether OpenCC punctuation conversion is enabled.</param>
        /// <param name="keepFont">Whether supported font declarations should be preserved.</param>
        public static void ConvertOfficeFile(
            string inputPath,
            string outputPath,
            OfficeFormat format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            ConvertOfficeFile(
                inputPath,
                outputPath,
                format,
                text => converter.Convert(text, punctuation),
                keepFont);
        }

        /// <summary>
        /// Converts a filesystem-backed Office or EPUB package using an initialized
        /// <see cref="Opencc"/> instance and a format name.
        /// </summary>
        /// <param name="inputPath">Path to the source Office or EPUB package.</param>
        /// <param name="outputPath">Path where the rebuilt package will be written.</param>
        /// <param name="format">Case-insensitive format name.</param>
        /// <param name="converter">The initialized OpenCC converter.</param>
        /// <param name="punctuation">Whether OpenCC punctuation conversion is enabled.</param>
        /// <param name="keepFont">Whether supported font declarations should be preserved.</param>
        public static void ConvertOfficeFile(
            string inputPath,
            string outputPath,
            string format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            ConvertOfficeFile(
                inputPath,
                outputPath,
                format,
                text => converter.Convert(text, punctuation),
                keepFont);
        }

        /// <summary>
        /// Asynchronously converts a filesystem-backed Office or EPUB package using an
        /// initialized <see cref="Opencc"/> instance.
        /// </summary>
        /// <param name="inputPath">Path to the source package.</param>
        /// <param name="outputPath">Path where the rebuilt package will be written.</param>
        /// <param name="format">The strongly typed Office or EPUB container format.</param>
        /// <param name="converter">The initialized OpenCC converter.</param>
        /// <param name="punctuation">Whether OpenCC punctuation conversion is enabled.</param>
        /// <param name="keepFont">Whether supported font declarations should be preserved.</param>
        /// <param name="cancellationToken">Token used to cancel before conversion starts.</param>
        /// <returns>A task representing the conversion operation.</returns>
        public static Task ConvertOfficeFileAsync(
            string inputPath,
            string outputPath,
            OfficeFormat format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false,
            CancellationToken cancellationToken = default)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            return ConvertOfficeFileAsync(
                inputPath,
                outputPath,
                format,
                text => converter.Convert(text, punctuation),
                keepFont,
                cancellationToken);
        }

        /// <summary>
        /// Asynchronously converts a filesystem-backed Office or EPUB package using an
        /// initialized <see cref="Opencc"/> instance and a format name.
        /// </summary>
        /// <param name="inputPath">Path to the source package.</param>
        /// <param name="outputPath">Path where the rebuilt package will be written.</param>
        /// <param name="format">Case-insensitive format name.</param>
        /// <param name="converter">The initialized OpenCC converter.</param>
        /// <param name="punctuation">Whether OpenCC punctuation conversion is enabled.</param>
        /// <param name="keepFont">Whether supported font declarations should be preserved.</param>
        /// <param name="cancellationToken">Token used to cancel before conversion starts.</param>
        /// <returns>A task representing the conversion operation.</returns>
        public static Task ConvertOfficeFileAsync(
            string inputPath,
            string outputPath,
            string format,
            Opencc converter,
            bool punctuation = false,
            bool keepFont = false,
            CancellationToken cancellationToken = default)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            return ConvertOfficeFileAsync(
                inputPath,
                outputPath,
                format,
                text => converter.Convert(text, punctuation),
                keepFont,
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
        /// converted with the supplied <see cref="OfficeTextConverter"/>, while all other entries are copied
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
        /// <param name="textConverter">The caller-supplied text transformation.</param>
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
            OfficeTextConverter textConverter,
            bool keepFont)
        {
            var formatId = OfficeFormatUtils.OfficeFormatToString(format);

            try
            {
                using var inputStream = new MemoryStream(
                    inputBytes,
                    0,
                    inputBytes.Length,
                    writable: false,
                    publiclyVisible: false);
                using var inputArchive = new ZipArchive(inputStream, ZipArchiveMode.Read, leaveOpen: false);
                using var outputStream = new MemoryStream();
                int convertedCount;

                using (var outputArchive = new ZipArchive(
                           outputStream,
                           ZipArchiveMode.Create,
                           leaveOpen: true))
                {
                    convertedCount = ProcessArchive(
                        inputArchive,
                        outputArchive,
                        format,
                        textConverter,
                        keepFont);
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
        /// Converts a filesystem-backed Office/EPUB package using streaming file I/O.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Unlike the in-memory <c>ConvertOfficeBytes</c> APIs,
        /// this path never calls <see cref="File.ReadAllBytes(string)"/> and never builds
        /// the complete output package in a <see cref="MemoryStream"/>. Only selected
        /// XML/XHTML entries are materialized as strings.
        /// </para>
        /// <para>
        /// Non-target ZIP entries are streamed entry-to-entry by
        /// <see cref="CopyEntry(ZipArchiveEntry,ZipArchive,CompressionLevel)"/>.
        /// <see cref="System.IO.Compression.ZipArchive"/> may decompress and recompress
        /// those entries internally, but their complete payloads are not retained in
        /// managed memory.
        /// </para>
        /// <para>
        /// Output is first written to a sibling temporary file. The completed package is
        /// then reopened and validated before publication, preventing a failed conversion
        /// or partial write from corrupting the requested destination.
        /// </para>
        /// </remarks>
        private static void ConvertOfficeFileCore(
            string inputPath,
            string outputPath,
            OfficeFormat format,
            OfficeTextConverter textConverter,
            bool keepFont)
        {
            var formatId = OfficeFormatUtils.OfficeFormatToString(format);
            var fullInputPath = Path.GetFullPath(inputPath);
            var fullOutputPath = Path.GetFullPath(outputPath);
            var outputDirectory = Path.GetDirectoryName(fullOutputPath);

            if (string.IsNullOrEmpty(outputDirectory))
                throw new ArgumentException(
                    "Output path must include a valid directory.",
                    nameof(outputPath));

            Directory.CreateDirectory(outputDirectory);

            var tempPath = Path.Combine(
                outputDirectory,
                "." + Path.GetFileName(fullOutputPath) + "." +
                Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                try
                {
                    int convertedCount;

                    using (var inputStream = new FileStream(
                               fullInputPath,
                               FileMode.Open,
                               FileAccess.Read,
                               FileShare.Read))
                    using (var inputArchive = new ZipArchive(
                               inputStream,
                               ZipArchiveMode.Read,
                               leaveOpen: false))
                    using (var outputStream = new FileStream(
                               tempPath,
                               FileMode.CreateNew,
                               FileAccess.ReadWrite,
                               FileShare.None))
                    {
                        using (var outputArchive = new ZipArchive(
                                   outputStream,
                                   ZipArchiveMode.Create,
                                   leaveOpen: true))
                        {
                            convertedCount = ProcessArchive(
                                inputArchive,
                                outputArchive,
                                format,
                                textConverter,
                                keepFont);
                        }

                        outputStream.Flush(true);
                    }

                    if (convertedCount == 0)
                    {
                        throw new InvalidDataException(
                            "No convertible XML/XHTML fragments found for format '" +
                            formatId + "'.");
                    }

                    ValidateZipFile(tempPath);
                    PublishTempFile(tempPath, fullOutputPath);
                    tempPath = null;
                }
                catch (Exception ex) when (!(ex is InvalidOperationException))
                {
                    throw new InvalidOperationException(
                        "Conversion failed: " + ex.Message,
                        ex);
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        /// <summary>
        /// Processes an opened Office/EPUB ZIP package and writes its rebuilt entries to
        /// the supplied output archive.
        /// </summary>
        /// <remarks>
        /// This is the single package-processing implementation shared by the pure
        /// in-memory <c>byte[]</c> API and the streaming file-I/O API.
        /// </remarks>
        /// <returns>The number of text-bearing XML/XHTML entries converted.</returns>
        private static int ProcessArchive(
            ZipArchive inputArchive,
            ZipArchive outputArchive,
            OfficeFormat format,
            OfficeTextConverter textConverter,
            bool keepFont)
        {
            var convertedCount = 0;

            if (format == OfficeFormat.Epub)
            {
                var mimetypeEntry = FindEpubMimetypeEntry(inputArchive);
                if (mimetypeEntry == null)
                {
                    throw new InvalidDataException(
                        "'mimetype' file is missing; a valid EPUB requires it as the first entry.");
                }

                CopyEntry(
                    mimetypeEntry,
                    outputArchive,
                    CompressionLevel.NoCompression);

                foreach (var entry in inputArchive.Entries)
                {
                    if (string.Equals(entry.FullName, "mimetype", StringComparison.Ordinal))
                        continue;

                    ProcessEntry(
                        entry,
                        outputArchive,
                        format,
                        textConverter,
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
                        textConverter,
                        keepFont,
                        ref convertedCount);
                }
            }

            return convertedCount;
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
            OfficeTextConverter textConverter,
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
                textConverter,
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
            OfficeTextConverter textConverter,
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
                ? ConvertXlsxXmlPart(xmlContent, entryName, textConverter)
                : ApplyTextConverter(textConverter, xmlContent);

            if (fontMap == null) return convertedXml;
            foreach (var pair in fontMap)
                convertedXml = convertedXml.Replace(pair.Key, pair.Value);

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
            using var stream = entry.Open();
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
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

            using var stream = outputEntry.Open();
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write(text);
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

            using var input = inputEntry.Open();
            using var output = outputEntry.Open();
            input.CopyTo(output);
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

        /// <summary>
        /// Returns whether font declarations should be protected for the specified
        /// converted package entry.
        /// </summary>
        private static bool ShouldMaskFonts(OfficeFormat format, string relativePath)
        {
            if (format != OfficeFormat.Xlsx)
                return true;

            var normalizedPath = relativePath.Replace('\\', '/');
            return string.Equals(normalizedPath, "xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Applies the caller-supplied text transformation and enforces the non-null
        /// return contract of <see cref="OfficeTextConverter"/>.
        /// </summary>
        private static string ApplyTextConverter(
            OfficeTextConverter textConverter,
            string text)
        {
            var converted = textConverter(text);
            if (converted == null)
                throw new InvalidOperationException("Office text converter returned null.");
            return converted;
        }

        /// <summary>
        /// Applies text conversion to an XLSX text-bearing XML part while leaving
        /// formulas, numeric cells, and non-inline worksheet metadata untouched.
        /// </summary>
        /// <remarks>
        /// Shared strings are converted as a whole XML part. Worksheet XML is handled
        /// narrowly: only text nodes inside <c>inlineStr</c> cells are transformed.
        /// </remarks>
        private static string ConvertXlsxXmlPart(
            string xmlContent,
            string relativePath,
            OfficeTextConverter textConverter)
        {
            var normalizedPath = relativePath.Replace('\\', '/');

            if (string.Equals(normalizedPath, "xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase))
                return ApplyTextConverter(textConverter, xmlContent);

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

                        var convertedText = ApplyTextConverter(textConverter, innerText);
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
            return !IsSupportedFormat(normalized)
                ? throw new ArgumentException(
                    $"Unsupported Office/EPUB format: '{normalized}'.",
                    nameof(format))
                : normalized;
        }

        /// <summary>Confirms that generated bytes contain a readable ZIP package.</summary>
        /// <param name="bytes">The generated package bytes.</param>
        private static void ValidateZipBytes(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            _ = archive.Entries.Count;
        }

        /// <summary>Confirms that a completed filesystem package is a readable ZIP archive.</summary>
        /// <param name="path">Path to the generated temporary package.</param>
        private static void ValidateZipFile(string path)
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            _ = archive.Entries.Count;
        }

        /// <summary>
        /// Publishes a fully written and validated temporary package to its final path.
        /// </summary>
        /// <remarks>
        /// The temporary file is created in the destination directory so publication
        /// does not require copying the package through managed memory.
        /// </remarks>
        private static void PublishTempFile(string tempPath, string outputPath)
        {
            if (File.Exists(outputPath))
                File.Replace(tempPath, outputPath, null);
            else
                File.Move(tempPath, outputPath);
        }

        // =====================================================================
        // Shared ZIP + XML/XHTML conversion pipeline
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