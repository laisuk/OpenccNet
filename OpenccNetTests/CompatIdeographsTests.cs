using System.Text;
using OpenccNetLib;

namespace OpenccNetTests
{
    [TestClass]
    public class CompatIdeographsTests
    {
        [TestMethod]
        public void FromText_Normalize_BmpCompatibilityIdeograph()
        {
            var compat = CompatIdeographs.FromText("金\t金\n");

            Assert.AreEqual("金", compat.Normalize("金"));
            Assert.AreEqual("測試金字", compat.Normalize("測試金字"));
        }

        [TestMethod]
        public void FromText_Normalize_NonBmpCompatibilityIdeograph()
        {
            var compat = CompatIdeographs.FromText(
                "鼖\t鼖\n" +
                "鼻\t鼻\n" +
                "𪘀\t𪘀\n");

            Assert.AreEqual("鼖", compat.NormalizeScalar("鼖"));
            Assert.AreEqual("鼻", compat.NormalizeScalar("鼻"));
            Assert.AreEqual("𪘀", compat.NormalizeScalar("𪘀"));
            Assert.AreEqual("A鼖鼻𪘀Z", compat.Normalize("A鼖鼻𪘀Z"));
        }

        [TestMethod]
        public void Normalize_PreservesUnmappedText()
        {
            var compat = CompatIdeographs.FromText("鼖\t鼖\n");

            Assert.AreEqual("普通文本ABC鼻", compat.Normalize("普通文本ABC鼻"));
        }

        [TestMethod]
        public void NormalizeInPlace_RebuildsStringBuilderForSupplementaryScalars()
        {
            var compat = CompatIdeographs.FromText("𪘀\t𪘀\n");
            var builder = new StringBuilder("前𪘀後");

            compat.NormalizeInPlace(builder);

            Assert.AreEqual("前𪘀後", builder.ToString());
        }

        [TestMethod]
        public void FromText_RejectsSourceOutsideCompatibilityIdeographRanges()
        {
            Assert.Throws<ArgumentException>(() => CompatIdeographs.FromText("漢\t汉\n"));
        }

        [TestMethod]
        public void NormalizeScalar_RejectsMultipleScalars()
        {
            var compat = CompatIdeographs.FromText("鼖\t鼖\n");

            Assert.Throws<ArgumentException>(() => compat.NormalizeScalar("鼖鼻"));
        }

        [TestMethod]
        public void NormalizeScalar_RejectsEmptyString()
        {
            var compat = CompatIdeographs.FromText(string.Empty);

            Assert.Throws<ArgumentException>(() => compat.NormalizeScalar(string.Empty));
        }

        [TestMethod]
        public void NormalizeScalar_RejectsIsolatedHighSurrogate()
        {
            var compat = CompatIdeographs.FromText(string.Empty);

            Assert.Throws<ArgumentException>(() => compat.NormalizeScalar("\uD800"));
        }

        [TestMethod]
        public void NormalizeScalar_RejectsIsolatedLowSurrogate()
        {
            var compat = CompatIdeographs.FromText(string.Empty);

            Assert.Throws<ArgumentException>(() => compat.NormalizeScalar("\uDC00"));
        }

        [TestMethod]
        public void FromText_RejectsIsolatedSurrogateMapping()
        {
            Assert.Throws<ArgumentException>(() => CompatIdeographs.FromText("\uD800\t金\n"));

            Assert.Throws<ArgumentException>(() => CompatIdeographs.FromText("金\t\uDC00\n"));
        }

        [TestMethod]
        public void Normalize_PreservesIsolatedSurrogates()
        {
            var compat = CompatIdeographs.FromText(string.Empty);
            const string input = "A\uD800B\uDC00C";

            Assert.AreEqual(input, compat.Normalize(input));
        }

        [TestMethod]
        public void Normalize_ReturnsSameReference_WhenNoCandidateExists()
        {
            var compat = CompatIdeographs.FromText("金\t金\n");
            const string input = "普通文本 ABC 😀";

            Assert.AreSame(input, compat.Normalize(input));
        }

        [TestMethod]
        public void Normalize_ReturnsSameReference_ForUnmappedCandidates()
        {
            var compat = CompatIdeographs.FromText("金\t金\n");
            const string input = "A更丸Z";

            Assert.AreSame(input, compat.Normalize(input));
        }

        [TestMethod]
        public void Normalize_PreservesSurrogatesOutsideCompatibilityRange()
        {
            var compat = CompatIdeographs.FromText("金\t金\n");
            const string input = "😀\uD87E\uDFFF\uD87E\uDBFF\uDC00";

            Assert.AreSame(input, compat.Normalize(input));
        }

        [TestMethod]
        public void Normalize_MixedMappedAndUnmappedText()
        {
            var compat = CompatIdeographs.FromText(
                "金\t金\n" +
                "鼖\t鼖\n");
            const string input = "前更金😀鼻鼖後\uD800\uDC00";

            Assert.AreEqual("前更金😀鼻鼖後\uD800\uDC00", compat.Normalize(input));
        }

        [TestMethod]
        public void Normalize_HandlesMappingsThatChangeUtf16Length()
        {
            var compat = CompatIdeographs.FromText(
                "金\t𪘀\n" +
                "鼖\t鼖\n");

            Assert.AreEqual("A𪘀B鼖C", compat.Normalize("A金B鼖C"));
        }

        [TestMethod]
        public void Normalize_PreservesMalformedSurrogatesAroundMappings()
        {
            var compat = CompatIdeographs.FromText("金\t金\n");
            const string input = "A\uD800金\uDC00Z";

            Assert.AreEqual("A\uD800金\uDC00Z", compat.Normalize(input));
        }

        [TestMethod]
        public void Normalize_EmptyAndNullKeepEstablishedSemantics()
        {
            var compat = CompatIdeographs.FromText(string.Empty);

            Assert.AreSame(string.Empty, compat.Normalize(string.Empty));
            Assert.AreEqual(string.Empty, compat.Normalize(null!));
        }

        [TestMethod]
        public void NormalizeUnicodeCompat_DoesNotApplyBuiltinCompatIdeographs()
        {
            const string input = "天龍八部書裡的喬峰是契丹人";

            Assert.AreSame(
                input,
                Opencc.NormalizeUnicodeCompat(input));
        }

        [TestMethod]
        public void NormalizeUnicodeCompat_NormalizesExtendedChineseMappings()
        {
            Assert.AreEqual(
                "酉十厶㘽㖈吞尚出夐耇飲",
                Opencc.NormalizeUnicodeCompat("⾣〸ム㦳䎛呑尙岀敻耈飮"));
        }

        [TestMethod]
        public void NormalizeUnicodeCompat_NormalizesSupplementarySourceMappings()
        {
            Assert.AreEqual(
                "前㒨𠓲㶷後",
                Opencc.NormalizeUnicodeCompat("前𠑗𣔕𤈎後"));
        }

        [TestMethod]
        public void NormalizeUnicodeCompat_NormalizesCompatibilityPunctuation()
        {
            Assert.AreEqual(
                "甲：乙，丙、丁；戊：己？庚！",
                Opencc.NormalizeUnicodeCompat("甲︰乙﹐丙﹑丁﹔戊﹕己﹖庚﹗"));
        }

        [TestMethod]
        public void NormalizeUnicodeCompat_PreservesUnmappedText()
        {
            const string input = "普通文本 ABC 😀 한글 ﾆｯﾎﾟﾝ";

            Assert.AreSame(input, Opencc.NormalizeUnicodeCompat(input));
        }

        [TestMethod]
        public void NormalizeUnicodeCompat_PreservesMalformedSurrogates()
        {
            const string input = "A\uD800中\uDC00Z";

            Assert.AreEqual(input, Opencc.NormalizeUnicodeCompat(input));
        }

        [TestMethod]
        public void NormalizeCompatExtended_IncludesCompatAndExtendedMappings()
        {
            var cc = new Opencc();

            Assert.AreEqual(
                "天龍八部書裡的喬峰是契丹人·：",
                cc.NormalizeCompatExtended(
                    "天龍八部書裡的喬峰是契丹人‧︰"));
        }

        [TestMethod]
        public void NormalizeCompat_Default_DoesNotApplyExtendedMappings()
        {
            var cc = new Opencc();
            const string input = "普通文本‧︰﹐";

            Assert.AreSame(
                input,
                cc.NormalizeCompat(input));
        }

        [TestMethod]
        public void PublicNormalizationApis_PreserveNullEmptyUnmappedAndMalformedText()
        {
            var cc = new Opencc();
            const string unmapped = "普通文本 ABC 😀";
            const string malformed = "A\uD800中\uDC00Z";

            Assert.AreEqual(string.Empty, cc.NormalizeCompat(null!));
            Assert.AreEqual(string.Empty, Opencc.NormalizeUnicodeCompat(null!));
            Assert.AreEqual(string.Empty, cc.NormalizeCompatExtended(null!));
            Assert.AreSame(string.Empty, cc.NormalizeCompat(string.Empty));
            Assert.AreSame(string.Empty, Opencc.NormalizeUnicodeCompat(string.Empty));
            Assert.AreSame(string.Empty, cc.NormalizeCompatExtended(string.Empty));
            Assert.AreSame(unmapped, cc.NormalizeCompat(unmapped));
            Assert.AreSame(unmapped, Opencc.NormalizeUnicodeCompat(unmapped));
            Assert.AreSame(unmapped, cc.NormalizeCompatExtended(unmapped));
            Assert.AreEqual(malformed, cc.NormalizeCompat(malformed));
            Assert.AreEqual(malformed, Opencc.NormalizeUnicodeCompat(malformed));
            Assert.AreEqual(malformed, cc.NormalizeCompatExtended(malformed));
        }

        [TestMethod]
        public void NormalizeCompatExtended_DoesNotChainReplacementScalars()
        {
            var cc = new Opencc();

            Assert.AreEqual("䂖", cc.NormalizeCompatExtended("䂖"));
            Assert.AreEqual("石", Opencc.NormalizeUnicodeCompat("䂖"));
        }

        [TestMethod]
        public void NormalizeUnicodeCompat_NormalizesExtendedWithoutCompatPass()
        {
            const string input = "金‧︰";

            Assert.AreEqual(
                "金·：",
                Opencc.NormalizeUnicodeCompat(input));
        }
    }
}