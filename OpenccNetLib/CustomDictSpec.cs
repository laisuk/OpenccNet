using System.Collections.Generic;

namespace OpenccNetLib
{
    /// <summary>
    /// Defines how custom dictionary entries are applied to a dictionary slot.
    /// </summary>
    public enum CustomDictMode
    {
        /// <summary>
        /// Merge custom entries into the existing slot.
        /// Later entries overwrite earlier entries with the same key.
        /// </summary>
        Append,

        /// <summary>
        /// Replace the entire target slot with the custom entries.
        /// </summary>
        Override
    }

    /// <summary>
    /// Describes custom dictionary data to apply to one OpenCC dictionary slot.
    /// </summary>
    /// <remarks>
    /// Use <see cref="Parse(string)"/> for portable
    /// <c>&lt;slot&gt;:&lt;append|override&gt;:&lt;path&gt;</c> tokens and
    /// <see cref="FromFile(DictSlot,string,CustomDictMode)"/> when constructing a
    /// file-backed specification from strongly typed C# values. Specifications are
    /// applied with <see cref="DictionaryLib.WithCustomDicts"/>; construction and
    /// parsing do not load dictionary files.
    /// </remarks>
    public sealed class CustomDictSpec
    {
        private const string ExpectedFormat = "<slot>:<append|override>:<path>";

        /// <summary>
        /// Target OpenCC dictionary slot.
        /// </summary>
        public DictSlot Slot { get; set; }

        /// <summary>
        /// Optional custom dictionary file paths.
        /// Files are applied in array order.
        /// Later files overwrite earlier duplicate keys.
        /// </summary>
        public string[] Paths { get; set; }

        /// <summary>
        /// Optional in-memory custom dictionary pairs.
        /// When both <see cref="Paths"/> and <see cref="Pairs"/> are provided,
        /// file entries are applied first, then pairs are applied last.
        /// Therefore, pairs overwrite duplicate keys from files.
        /// </summary>
        public IDictionary<string, string> Pairs { get; set; }

        /// <summary>
        /// Custom dictionary merge mode.
        /// Defaults to <see cref="CustomDictMode.Append"/>.
        /// </summary>
        public CustomDictMode Mode { get; set; } = CustomDictMode.Append;

        /// <summary>
        /// Creates a custom dictionary specification backed by one file.
        /// </summary>
        /// <remarks>
        /// This method validates the slot, mode, and path text but does not check
        /// whether the file exists. File access remains the responsibility of the
        /// dictionary loader when the specification is applied.
        /// </remarks>
        /// <param name="slot">The active dictionary slot to customize.</param>
        /// <param name="mode">Whether to append to or replace the target slot.</param>
        /// <param name="path">The dictionary file path. The file is not opened or checked here.</param>
        /// <returns>A custom dictionary specification for <paramref name="path"/>.</returns>
        /// <exception cref="System.ArgumentException">
        /// The slot or mode is unsupported, or <paramref name="path"/> is empty.
        /// </exception>
        public static CustomDictSpec FromFile(DictSlot slot, string path, CustomDictMode mode)
        {
            if (!slot.IsActive())
                throw new System.ArgumentException("Unknown dictionary slot: " + slot, nameof(slot));

            if (mode != CustomDictMode.Append && mode != CustomDictMode.Override)
                throw new System.ArgumentException("Unknown custom dictionary mode: " + mode, nameof(mode));

            if (string.IsNullOrWhiteSpace(path))
                throw new System.ArgumentException("Custom dictionary path cannot be empty.", nameof(path));

            return new CustomDictSpec
            {
                Slot = slot,
                Mode = mode,
                Paths = new[] { path.Trim() }
            };
        }

        /// <summary>
        /// Parses a textual custom dictionary specification in the form
        /// <c>&lt;slot&gt;:&lt;append|override&gt;:&lt;path&gt;</c>.
        /// </summary>
        /// <remarks>
        /// Slot and mode matching is case-insensitive. Splitting is limited to three
        /// fields so Windows drive paths and relative paths containing additional
        /// colons are preserved. Parsing does not check whether the file exists.
        /// </remarks>
        /// <param name="value">The textual specification to parse.</param>
        /// <returns>The parsed custom dictionary specification.</returns>
        /// <exception cref="System.ArgumentException">
        /// <paramref name="value"/> is null or malformed; contains a numeric,
        /// unknown, or obsolete slot; contains an unsupported mode; or has an empty
        /// path.
        /// </exception>
        public static CustomDictSpec Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new System.ArgumentException(
                    "Custom dictionary specification cannot be null or empty.",
                    nameof(value));

            var parts = value.Split(new[] { ':' }, 3);
            if (parts.Length != 3)
                throw new System.ArgumentException(
                    "Invalid custom dictionary specification '" + value + "'. Expected: " + ExpectedFormat,
                    nameof(value));

            var slot = DictSlotExtensions.Parse(parts[0]);
            CustomDictMode mode;

            if (string.Equals(parts[1].Trim(), "append", System.StringComparison.OrdinalIgnoreCase))
                mode = CustomDictMode.Append;
            else if (string.Equals(parts[1].Trim(), "override", System.StringComparison.OrdinalIgnoreCase))
                mode = CustomDictMode.Override;
            else
                throw new System.ArgumentException(
                    "Unknown custom dictionary mode '" + parts[1] + "'. Valid values: append, override.",
                    nameof(value));

            return FromFile(slot, parts[2], mode);
        }
    }
}