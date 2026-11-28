using System.Text;
using OpenccNet;
using Kind = OpenccNet.CjkEncodingDetector.EncodingKind;

namespace OpenccNetTests;

[TestClass]
public class CjkEncodingDetectorTests
{
    [TestMethod]
    public async Task RecognizesUnicodeAndLegacyEncodings()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        const string traditional = "繁體中文是我們使用的文字，這個測試包含常見的詞語和標點符號。";
        const string simplified = "简体中文是我们使用的文字，这个测试包含常见的词语和标点符号。";
        const string japanese = "これは日本語の文章です。ひらがなとカタカナが含まれています。";

        await CheckAsync(Array.Empty<byte>(), Kind.Ascii);
        await CheckAsync(Encoding.ASCII.GetBytes("Plain ASCII"), Kind.Ascii);
        await CheckAsync(Encoding.UTF8.GetBytes(traditional), Kind.Utf8);
        await CheckAsync(WithBom(Encoding.UTF8, traditional), Kind.Utf8Bom, 3);
        await CheckAsync(WithBom(Encoding.Unicode, traditional), Kind.Utf16LeBom, 2);
        await CheckAsync(WithBom(Encoding.BigEndianUnicode, traditional), Kind.Utf16BeBom, 2);
        await CheckAsync(WithBom(Encoding.UTF32, traditional), Kind.Utf32LeBom, 4);
        await CheckAsync(WithBom(new UTF32Encoding(true, true), traditional), Kind.Utf32BeBom, 4);
        await CheckAsync(Encoding.Unicode.GetBytes("Plain ASCII in UTF-16"), Kind.Utf16Le);
        await CheckAsync(Encoding.BigEndianUnicode.GetBytes("Plain ASCII in UTF-16"), Kind.Utf16Be);
        var big5 = Encoding.GetEncoding(950).GetBytes(traditional);
        await CheckAsync(big5, Kind.Big5);
        await CheckAsync(big5.Append((byte)0xff).ToArray(), Kind.Unknown);
        var gb18030 = Encoding.GetEncoding(54936).GetBytes(simplified);
        await CheckAsync(gb18030, Kind.Gb18030);
        await CheckAsync(gb18030.Append((byte)0xff).ToArray(), Kind.Unknown);
        var shiftJis = Encoding.GetEncoding(932).GetBytes(japanese);
        await CheckAsync(shiftJis, Kind.ShiftJis);
        await CheckAsync(shiftJis.Append((byte)0xff).ToArray(), Kind.Unknown);
        await CheckAsync(new byte[] { 0xa4, 0xa4 }, Kind.Unknown);
    }

    [TestMethod]
    public async Task StrictUtf8CarriesSequencesAcrossBoundariesAndChecksTheWholeFile()
    {
        var prefix = new string('a', 128 * 1024 - 1);
        foreach (var suffix in new[] { "é", "中", "😀" })
        {
            await CheckAsync(Encoding.UTF8.GetBytes(prefix + suffix), Kind.Utf8);
        }

        var laterPrefix = Encoding.ASCII.GetBytes(prefix + new string('a', 16 * 1024));
        await CheckAsync(laterPrefix.Concat(Encoding.UTF8.GetBytes("😀")).ToArray(), Kind.Utf8);

        foreach (var invalid in new[]
                 {
                     new byte[] { 0xc0, 0xaf },
                     new byte[] { 0xe0, 0x9f, 0x80 },
                     new byte[] { 0xed, 0xa0, 0x80 },
                     new byte[] { 0xf0, 0x8f, 0xbf, 0xbf },
                     new byte[] { 0xf4, 0x90, 0x80, 0x80 },
                     new byte[] { 0xf0, 0x9f, 0x98 }
                 })
        {
            await CheckAsync(laterPrefix.Concat(invalid).ToArray(), Kind.Unknown);
        }
    }

    private static byte[] WithBom(Encoding encoding, string text) =>
        encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();

    private static async Task CheckAsync(byte[] bytes, Kind expected, int bomSize = 0)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            await using var stream = File.OpenRead(path);
            var result = await CjkEncodingDetector.DetectAsync(stream, CancellationToken.None);
            Assert.AreEqual(expected, result.Encoding);
            Assert.AreEqual(bomSize, result.BomSize);
            Assert.AreEqual(result.Encoding, CjkEncodingDetector.Detect(bytes).Encoding);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
