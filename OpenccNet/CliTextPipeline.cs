using OpenccNetLib;

namespace OpenccNet;

/// <summary>
/// Builds the shared CLI text-conversion pipeline used by commands that apply
/// OpenccNetLib conversion to text.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline keeps command-specific input/output handling separate from the
/// text transformation policy. Commands remain responsible for reading text,
/// processing document containers, extracting PDF content, and writing output.
/// </para>
/// <para>
/// Text transformation is applied in the following order:
/// </para>
/// <code>
/// Compatibility normalization (optional)
///     → OpenCC conversion
///     → DeTofu display fallback (optional)
/// </code>
/// <para>
/// Extended compatibility normalization takes precedence over basic
/// compatibility-ideograph normalization when both options are enabled.
/// Custom OpenCC dictionary specifications and IDS preservation are applied when
/// the <see cref="Opencc"/> instance is constructed.
/// </para>
/// <para>
/// DeTofu is prepared once when the pipeline is built so reusable document
/// pipelines do not rebuild the fallback map for every text fragment.
/// </para>
/// </remarks>
internal static class CliTextPipeline
{
    /// <summary>
    /// Builds a reusable text converter from the common OpenccNet CLI conversion
    /// options.
    /// </summary>
    /// <param name="config">
    /// The OpenCC conversion configuration, such as <c>s2t</c>, <c>t2s</c>,
    /// <c>s2twp</c>, or <c>hk2sp</c>.
    /// </param>
    /// <param name="punctuation">
    /// Whether OpenCC punctuation conversion should be applied.
    /// </param>
    /// <param name="keepIds">
    /// Whether Unicode Ideographic Description Sequence (IDS) expressions should
    /// be preserved during OpenCC conversion.
    /// </param>
    /// <param name="normCompat">
    /// Whether CJK Compatibility Ideographs should be normalized before OpenCC
    /// conversion.
    /// </param>
    /// <param name="normCompatExtended">
    /// Whether extended Unicode compatibility normalization should be applied
    /// before OpenCC conversion. This takes precedence over
    /// <paramref name="normCompat"/>.
    /// </param>
    /// <param name="deTofu">
    /// Optional DeTofu level name. A null or whitespace value disables DeTofu.
    /// </param>
    /// <param name="deTofuFile">
    /// Optional UTF-8 DeTofu mapping file whose entries extend and override the
    /// built-in fallback mappings. This is used only when DeTofu is enabled.
    /// </param>
    /// <param name="customDictArgs">
    /// Portable custom dictionary specifications in
    /// <c>&lt;slot&gt;:&lt;mode&gt;:&lt;path&gt;</c> form.
    /// </param>
    /// <returns>
    /// A reusable <see cref="OfficeTextConverter"/> that applies the configured
    /// normalization, OpenCC conversion, and optional DeTofu pipeline.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when a DeTofu level or custom dictionary specification is invalid.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when a required custom dictionary or DeTofu mapping file cannot be
    /// read.
    /// </exception>
    internal static OfficeTextConverter Build(
        string config,
        bool punctuation,
        bool keepIds,
        bool normCompat,
        bool normCompatExtended,
        string? deTofu,
        string? deTofuFile,
        string[] customDictArgs)
    {
        var customSpecs =
            CliUtils.ParseAndValidateCustomDictSpecs(customDictArgs);

        var opencc = customSpecs.Length == 0
            ? new Opencc(config, isPreserveIds: keepIds)
            : new Opencc(
                config,
                customDictSpecs: customSpecs,
                isPreserveIds: keepIds);

        DeTofuMap? deTofuMap = null;

        if (!string.IsNullOrWhiteSpace(deTofu))
        {
            var level = DeTofu.ParseLevel(deTofu);
            deTofuMap = DeTofuMap.Builtin(level);

            if (!string.IsNullOrWhiteSpace(deTofuFile))
                deTofuMap = deTofuMap.WithCustomFile(deTofuFile);
        }

        return text =>
        {
            if (normCompatExtended)
            {
                text = opencc.NormalizeCompatExtended(text);
            }
            else if (normCompat)
            {
                text = opencc.NormalizeCompat(text);
            }

            text = opencc.Convert(text, punctuation);

            return deTofuMap?.Convert(text) ?? text;
        };
    }
}