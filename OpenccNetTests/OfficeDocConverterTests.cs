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
            var opencc = new Opencc(OpenccConfig.S2T);

            // Act
            var outputBytes = OfficeDocConverter.ConvertOfficeBytes(
                inputBytes,
                format: "docx",
                converter: opencc,
                punctuation: false,
                keepFont: true);

            // Assert 基本檢查
            Assert.IsNotNull(outputBytes, "Output bytes should not be null.");
            Assert.IsNotEmpty(outputBytes, "Output bytes should not be empty.");

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

            var opencc = new Opencc(OpenccConfig.S2T);

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
            Assert.IsNotEmpty(outBytes, "Converted DOCX file should not be empty.");

            using var ms = new MemoryStream(outBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            var documentEntry = archive.GetEntry("word/document.xml");
            Assert.IsNotNull(documentEntry, "Converted DOCX missing 'word/document.xml'.");
        }

        [TestMethod]
        public void ConvertOfficeBytes_DocxEnum_S2T_Succeeds_And_ProducesValidDocx()
        {
            // Arrange
            var inputBytes = File.ReadAllBytes(_testDocxPath!);
            var opencc = new Opencc(OpenccConfig.S2T);

            // Act
            var outputBytes = OfficeDocConverter.ConvertOfficeBytes(
                inputBytes,
                format: OfficeFormat.Docx, // <<< enum version
                converter: opencc,
                punctuation: false,
                keepFont: true);

            // Assert: basic checks
            Assert.IsNotNull(outputBytes, "Output bytes should not be null.");
            Assert.IsNotEmpty(outputBytes, "Output bytes should not be empty.");

            // Assert: container still valid ZIP
            using var ms = new MemoryStream(outputBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

            var documentEntry = archive.GetEntry("word/document.xml");
            Assert.IsNotNull(documentEntry, "Converted DOCX missing 'word/document.xml'.");

            using var entryStream = documentEntry.Open();
            using var reader = new StreamReader(entryStream, Encoding.UTF8, true);
            var xml = reader.ReadToEnd();

            Assert.IsFalse(string.IsNullOrWhiteSpace(xml), "document.xml should not be empty.");
        }

        [TestMethod]
        public void ConvertOfficeFile_DocxEnum_S2T_WritesOutputFile()
        {
            // Arrange
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var outputPath = Path.Combine(baseDir, "滕王阁序.enum.converted.docx");

            if (File.Exists(outputPath))
                File.Delete(outputPath);

            var opencc = new Opencc(OpenccConfig.S2T);

            // Act
            OfficeDocConverter.ConvertOfficeFile(
                inputPath: _testDocxPath!,
                outputPath: outputPath,
                format: OfficeFormat.Docx, // <<< enum version
                converter: opencc,
                punctuation: false,
                keepFont: true);

            // Assert
            Assert.IsTrue(File.Exists(outputPath), "Converted DOCX file was not created.");

            var outBytes = File.ReadAllBytes(outputPath);
            Assert.IsNotEmpty(outBytes, "Converted DOCX content should not be empty.");

            // Verify DOCX structure remains valid ZIP
            using var ms = new MemoryStream(outBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            var documentEntry = archive.GetEntry("word/document.xml");
            Assert.IsNotNull(documentEntry, "Converted DOCX missing 'word/document.xml'.");
        }

        [TestMethod]
        public void ConvertOfficeBytes_Xlsx_ConvertsSharedStringsAndInlineStrings()
        {
            var inputBytes = CreateMinimalXlsx(
                @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><sst xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" count=""1"" uniqueCount=""1""><si><t>汉字</t></si></sst>",
                @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""><sheetData><row r=""1""><c r=""A1"" t=""s""><v>0</v></c><c r=""B1"" t=""inlineStr""><is><t>汉字</t></is></c></row></sheetData></worksheet>");

            var opencc = new Opencc(OpenccConfig.S2T);

            var outputBytes = OfficeDocConverter.ConvertOfficeBytes(
                inputBytes,
                OfficeFormat.Xlsx,
                opencc,
                punctuation: false,
                keepFont: false);

            using var ms = new MemoryStream(outputBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

            using (var sharedReader = new StreamReader(archive.GetEntry("xl/sharedStrings.xml")!.Open(), Encoding.UTF8, true))
            {
                var sharedXml = sharedReader.ReadToEnd();
                Assert.Contains("漢字", sharedXml);
            }

            using (var sheetReader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open(), Encoding.UTF8, true))
            {
                var sheetXml = sheetReader.ReadToEnd();
                                StringAssert.Contains(sheetXml, "t=\"inlineStr\"");
                Assert.Contains("漢字", sheetXml);
            }
        }

        private static byte[] CreateMinimalXlsx(string sharedStringsXml, string worksheetXml)
        {
            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddEntry(archive, "[Content_Types].xml",
                    """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/></Types>""");
                AddEntry(archive, "_rels/.rels",
                    """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
                AddEntry(archive, "xl/workbook.xml",
                    """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>""");
                AddEntry(archive, "xl/_rels/workbook.xml.rels",
                    """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/></Relationships>""");
                AddEntry(archive, "xl/sharedStrings.xml", sharedStringsXml);
                AddEntry(archive, "xl/worksheets/sheet1.xml", worksheetXml);
            }

            return ms.ToArray();
        }

        private static void AddEntry(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
    }
}
