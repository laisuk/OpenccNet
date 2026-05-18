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
    public sealed class CustomDictSpec
    {
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
    }
}