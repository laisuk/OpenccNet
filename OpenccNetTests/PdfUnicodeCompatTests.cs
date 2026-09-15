using System.Text;
using OpenccNetLib;
using UglyToad.PdfPig;

namespace OpenccNetTests;

[TestClass]
public class PdfUnicodeCompatTests
{
    [TestMethod]
    public void SanWenHant_PdfPig_KangxiRadicals_AreNormalized()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "SanWenHant.pdf");

        Assert.IsTrue(File.Exists(path), $"Test PDF not found: {path}");

        var extracted = new StringBuilder();

        using (var pdf = PdfDocument.Open(path))
        {
            foreach (var page in pdf.GetPages())
                extracted.Append(page.Text);
        }

        var text = extracted.ToString();

        // PdfPig currently extracts 文 as U+2F42 KANGXI RADICAL SCRIPT
        // in this fixture, e.g. 散⽂選集.
        Assert.Contains("\u2F42", text);

        var normalized = UnicodeCompat.Builtin().Normalize(text);

        Assert.IsFalse(
            normalized.Contains("\u2F42", StringComparison.Ordinal));

        Assert.Contains("文", normalized);

#if DEBUG
        var mappings = UnicodeCompat.Builtin()
            .CollectNormalizedMappings(text);

        foreach (var pair in mappings)
        {
            Console.WriteLine(
                $"U+{pair.Source:X4} '{char.ConvertFromUtf32(pair.Source)}' -> " +
                $"U+{pair.Target:X4} '{char.ConvertFromUtf32(pair.Target)}'");
        }

        Assert.IsTrue(Array.Exists(
                mappings,
                pair => pair is { Source: 0x2F42, Target: 0x6587 }),
            "Expected U+2F42 '⽂' -> U+6587 '文' mapping was not collected.");

        Assert.IsTrue(Array.Exists(
            mappings,
            p => p is { Source: 0x2F42, Target: 0x6587 })); // ⽂ -> 文

        Assert.IsTrue(Array.Exists(
            mappings,
            p => p is { Source: 0x2ED1, Target: 0x9577 })); // ⻑ -> 長

        Assert.IsTrue(Array.Exists(
            mappings,
            p => p is { Source: 0x2329, Target: 0x3008 })); // 〈 -> 〈
#endif
    }
}