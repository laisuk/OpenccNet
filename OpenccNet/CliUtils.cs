using System.CommandLine;
using System.Text;
using OpenccNetLib;

namespace OpenccNet;

internal static class CliUtils
{
    internal const int ExitSuccess = 0;
    internal const int ExitFailure = 1;
    internal const int ExitCancelled = 130;

    /// <summary>
    /// Adds canonical custom-dictionary token validation to an option.
    ///
    /// This validates only the token structure, slot, mode, and non-empty path.
    /// File existence is checked later by
    /// <see cref="ApplyCustomDictionaryProvider"/>.
    /// </summary>
    internal static void AddCustomDictValidator(
        Option<string[]> customDictOption)
    {
        ArgumentNullException.ThrowIfNull(customDictOption);

        customDictOption.Validators.Add(result =>
        {
            foreach (var value in
                     result.GetValueOrDefault<string[]>())
            {
                try
                {
                    CustomDictSpec.Parse(value);
                }
                catch (ArgumentException ex)
                {
                    result.AddError(ex.Message);
                }
            }
        });
    }

    /// <summary>
    /// Parses, validates, and resolves custom dictionary specifications.
    ///
    /// This validates:
    /// - Canonical slot names.
    /// - Dictionary mode.
    /// - Token structure.
    /// - Referenced dictionary files.
    ///
    /// An empty input returns <see cref="Array.Empty{T}"/>.
    /// </summary>
    internal static CustomDictSpec[] ParseAndValidateCustomDictSpecs(
        IEnumerable<string>? customDictArgs)
    {
        if (customDictArgs is null)
            return Array.Empty<CustomDictSpec>();

        var values = customDictArgs as string[] ??
                     customDictArgs.ToArray();

        if (values.Length == 0)
            return Array.Empty<CustomDictSpec>();

        var specs = values
            .Select(CustomDictSpec.Parse)
            .ToArray();

        ValidateCustomDictionaryFiles(specs);

        return specs;
    }

    /// <summary>
    /// Parses, validates, loads, and activates custom dictionary specifications.
    ///
    /// This method must be called before constructing any
    /// <see cref="Opencc"/> instances.
    ///
    /// When no custom dictionary arguments are supplied, this method performs
    /// no action and does not reset or rebuild the default provider.
    /// </summary>
    internal static void ApplyCustomDictionaryProvider(
        IEnumerable<string>? customDictArgs)
    {
        var specs = ParseAndValidateCustomDictSpecs(customDictArgs);

        if (specs.Length == 0)
            return;

        // DictionaryLib.New() returns the default dictionary provider and
        // resets the global plan provider before customization.
        var dictionary = DictionaryLib.New();

        DictionaryLib.WithCustomDicts(dictionary, specs);

        // Global provider selection must happen before Opencc construction.
        Opencc.UseCustomDictionary(dictionary);
    }

    /// <summary>
    /// Validates all file paths referenced by custom dictionary specifications.
    /// </summary>
    private static void ValidateCustomDictionaryFiles(
        IEnumerable<CustomDictSpec> specs)
    {
        foreach (var spec in specs)
        {
            if (spec.Paths is null)
                continue;

            foreach (var path in spec.Paths)
                ValidateInputFile(
                    path,
                    "Custom dictionary file",
                    relativeToAppBase: true);
        }
    }

    /// <summary>
    /// Validates and resolves an existing input file.
    /// Relative paths are resolved against
    /// <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    internal static string ValidateInputFile(
        string? path,
        string description = "Input file",
        bool relativeToAppBase = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException(
                $"{description} path must not be empty.");

        var trimmedPath = path.Trim();

        var fullPath = Path.GetFullPath(
            relativeToAppBase && !Path.IsPathRooted(trimmedPath)
                ? Path.Combine(AppContext.BaseDirectory, trimmedPath)
                : trimmedPath);

        FileAttributes attributes;

        try
        {
            attributes = File.GetAttributes(fullPath);
        }
        catch (FileNotFoundException)
        {
            throw new FileNotFoundException(
                $"{description} not found: {fullPath}",
                fullPath);
        }
        catch (DirectoryNotFoundException)
        {
            throw new FileNotFoundException(
                $"{description} not found: {fullPath}",
                fullPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException(
                $"Cannot access {description.ToLowerInvariant()}: {fullPath}",
                ex);
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            throw new ArgumentException(
                $"{description} path is not a file: {fullPath}");
        }

        return fullPath;
    }

    /// <summary>
    /// Validates and resolves an existing directory.
    /// </summary>
    internal static string ValidateDirectory(
        string? path,
        string description)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                $"{description} path must not be empty.");
        }

        var fullPath = ResolveUserPath(path);

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"{description} not found: {fullPath}");
        }

        return fullPath;
    }

    /// <summary>
    /// Resolves a user-provided path.
    /// Relative paths are based on
    /// <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    internal static string ResolveUserPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Path must not be null or empty.",
                nameof(path));
        }

        path = path.Trim().Replace(
            Path.AltDirectorySeparatorChar,
            Path.DirectorySeparatorChar);

        return Path.GetFullPath(
            Path.IsPathRooted(path)
                ? path
                : Path.Combine(AppContext.BaseDirectory, path));
    }

    /// <summary>
    /// Resolves and validates an output file path.
    /// </summary>
    internal static string ResolveOutputFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Output path must not be empty.",
                nameof(path));
        }

        var fullPath = Path.GetFullPath(path.Trim());

        if (Directory.Exists(fullPath))
        {
            throw new ArgumentException(
                $"Output path is a directory: {fullPath}");
        }

        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory) &&
            !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"Output directory not found: {directory}");
        }

        return fullPath;
    }

    /// <summary>
    /// Ensures that input and output do not resolve to the same path.
    /// </summary>
    internal static void EnsureDifferentPaths(
        string inputPath,
        string outputPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(
                Path.GetFullPath(inputPath),
                Path.GetFullPath(outputPath),
                comparison))
        {
            throw new ArgumentException(
                "Input and output paths must be different.");
        }
    }

    /// <summary>
    /// Resolves a supported text encoding.
    /// UTF encodings are created without a byte-order mark for output.
    /// </summary>
    internal static Encoding ResolveEncoding(string encodingName)
    {
        if (string.IsNullOrWhiteSpace(encodingName))
        {
            throw new ArgumentException(
                "Encoding name must not be empty.",
                nameof(encodingName));
        }

        try
        {
            if (string.Equals(
                    encodingName,
                    "utf-8",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false);
            }

            if (string.Equals(
                    encodingName,
                    "utf-16le",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    encodingName,
                    "unicode",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new UnicodeEncoding(
                    bigEndian: false,
                    byteOrderMark: false);
            }

            if (string.Equals(
                    encodingName,
                    "utf-16be",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new UnicodeEncoding(
                    bigEndian: true,
                    byteOrderMark: false);
            }

            if (string.Equals(
                    encodingName,
                    "utf-32",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new UTF32Encoding(
                    bigEndian: false,
                    byteOrderMark: false);
            }

            return Encoding.GetEncoding(encodingName);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(
                $"Unknown or unsupported encoding: {encodingName}",
                nameof(encodingName),
                ex);
        }
    }

    /// <summary>
    /// Gets a comma-separated list of all supported OpenCC conversion
    /// configuration names for CLI help text.
    /// </summary>
    internal static string ConfigHelpAll =>
        string.Join(", ", CliConfigNames.All);

    /// <summary>
    /// Gets a comma-separated list of all active custom dictionary slot
    /// names for CLI help text.
    /// </summary>
    internal static string SlotHelpAll =>
        string.Join(", ",
            DictSlotExtensions.ActiveSlots
                .Select(s => s.ToCanonicalName()));

    /// <summary>
    /// Writes a consistently formatted CLI error and returns its exit code.
    /// </summary>
    internal static int WriteError(
        Exception exception,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled.");
            return ExitCancelled;
        }

        var message = exception switch
        {
            FileNotFoundException or
                DirectoryNotFoundException or
                UnauthorizedAccessException or
                ArgumentException or
                NotSupportedException
                => exception.Message,

            InvalidDataException
                => $"Invalid or corrupted input: {exception.Message}",

            IOException
                => $"I/O error: {exception.Message}",

            _ => $"{operation} failed unexpectedly: {exception.Message}"
        };

        Console.Error.WriteLine($"❌ {message}");
        return ExitFailure;
    }

    internal static void WriteInfo(
        string message,
        bool quiet = false)
    {
        if (!quiet)
            Console.Error.WriteLine($"ℹ️ {message}");
    }

    internal static void WriteSuccess(
        string message,
        bool quiet = false)
    {
        if (!quiet)
            Console.Error.WriteLine($"✅ {message}");
    }
}