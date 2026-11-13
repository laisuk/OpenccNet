using System.IO.Compression;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenccNetLib;

namespace OpenccNetTests;

[TestClass]
public sealed class OfficeFontRestorationTests
{
    [TestMethod]
    [DataRow("前__F_O_N_T_1____F_O_N_T_0__ / __F_O_N_T_0__后", "前Arial宋体 / 宋体后")]
    [DataRow("__F_O_N_T_99__ __F_O_N_T_bad__F_O_N_T_0__ __F_O_N_T___F_O_N_T_1__ __F_O_N_T_", "__F_O_N_T_99__ __F_O_N_T_bad宋体 __F_O_N_T_Arial __F_O_N_T_")]
    [DataRow("普通文本", "普通文本")]
    [DataRow("", "")]
    public async Task RestoresKnownMarkersAndPreservesUnknownMarkers(string converted, string expected)
    {
        var actual = await Convert("<w:rFonts w:ascii=\"宋体\" w:eastAsia=\"Arial\"/>", _ => converted);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public async Task RestorationDoesNotRescanInsertedFontNames()
    {
        const string xml = "<w:rFonts w:ascii=\"__F_O_N_T_1__\" w:eastAsia=\"Arial\"/>";
        Assert.AreEqual(xml, await Convert(xml, text => text));
    }

    [TestMethod]
    public async Task EmptyFontMapPreservesMarkers()
    {
        const string xml = "<w:t>__F_O_N_T_0__</w:t>";
        Assert.AreEqual(xml, await Convert(xml, text => text));
    }

    private static async Task<string> Convert(string xml, TextConverter converter)
    {
        using var input = new MemoryStream();
        using (var archive = new ZipArchive(input, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open(), Encoding.UTF8);
            writer.Write(xml);
        }
        var outputBytes = await OfficeDocConverter.ConvertOfficeBytesAsync(input.ToArray(), "docx", converter, keepFont: true);
        using var output = new MemoryStream(outputBytes);
        using var resultArchive = new ZipArchive(output, ZipArchiveMode.Read);
        using var reader = new StreamReader(resultArchive.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
