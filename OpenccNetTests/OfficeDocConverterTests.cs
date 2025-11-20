using System.IO.Compression;
using System.Text;
using OpenccNetLib;

namespace OpenccNetTests
{
    [TestClass]
    public class OfficeDocConverterTests
    {
        private string? _testDocxPath;

        [TestInitialize]
        public void SetUp()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            _testDocxPath = Path.Combine(baseDir, "滕王阁序.docx");

            Assert.IsTrue(
                File.Exists(_testDocxPath),
                $"Test file not found: {_testDocxPath}. " +
                "Ensure '滕王阁序.docx' is marked as Content and CopyToOutputDirectory=PreserveNewest.");
        }

        [TestMethod]
        public void ConvertOfficeBytes_Docx_S2T_Succeeds_And_ProducesValidDocx()
        {
            // Arrange
            var inputBytes = File.ReadAllBytes(_testDocxPath!);

            // ✅ OpenccNetLib 核心類別正確名稱是 Opencc，不是 OpenCC
            var opencc = new Opencc("s2t");

            // Act
            var outputBytes = OfficeDocConverter.ConvertOfficeBytes(
                inputBytes,
                format: "docx",
                converter: opencc,
                punctuation: false,
                keepFont: true);

            // Assert 基本檢查
            Assert.IsNotNull(outputBytes, "Output bytes should not be null.");
            Assert.AreNotEqual(0, outputBytes.Length, "Output bytes should not be empty.");

            // Assert: DOCX 容器仍為合法 ZIP
            using var ms = new MemoryStream(outputBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            var documentEntry = archive.GetEntry("word/document.xml");
            Assert.IsNotNull(documentEntry, "Converted DOCX missing 'word/document.xml'.");

            using var entryStream = documentEntry.Open();
            using var reader =
                new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var xml = reader.ReadToEnd();

            Assert.IsFalse(string.IsNullOrWhiteSpace(xml), "document.xml should not be empty.");

            // 如果滕王閣序內容一定包含此關鍵字，可打開這條：
            // StringAssert.Contains(xml, "滕王", "Converted XML should contain expected text.");
        }

        [TestMethod]
        public void ConvertOfficeFile_Docx_S2T_WritesOutputFile()
        {
            // Arrange
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var outputPath = Path.Combine(baseDir, "滕王阁序.converted.docx");

            if (File.Exists(outputPath))
                File.Delete(outputPath);

            var opencc = new Opencc("s2t");

            // Act
            OfficeDocConverter.ConvertOfficeFile(
                inputPath: _testDocxPath,
                outputPath: outputPath,
                format: "docx",
                converter: opencc,
                punctuation: false,
                keepFont: true);

            // Assert
            Assert.IsTrue(File.Exists(outputPath), "Converted DOCX file was not created.");

            var outBytes = File.ReadAllBytes(outputPath);
            Assert.AreNotEqual(0, outBytes.Length, "Converted DOCX file should not be empty.");

            using var ms = new MemoryStream(outBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            var documentEntry = archive.GetEntry("word/document.xml");
            Assert.IsNotNull(documentEntry, "Converted DOCX missing 'word/document.xml'.");
        }
    }
}