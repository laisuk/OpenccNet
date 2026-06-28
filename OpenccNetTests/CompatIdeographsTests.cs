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
            Assert.Throws<ArgumentException>(
                () => CompatIdeographs.FromText("漢\t汉\n"));
        }

        [TestMethod]
        public void NormalizeScalar_RejectsMultipleScalars()
        {
            var compat = CompatIdeographs.FromText("鼖\t鼖\n");

            Assert.Throws<ArgumentException>(
                () => compat.NormalizeScalar("鼖鼻"));
        }
    }
}
