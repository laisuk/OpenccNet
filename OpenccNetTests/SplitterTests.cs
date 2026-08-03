using OpenccNetLib;

namespace OpenccNetTests
{
    [TestClass]
    public class SplitterTests
    {
        private const string FullDelimiters =
            " \t\n\r!\"#$%&'()*+,-./:;<=>?@[\\]^_{}|~＝、。﹁﹂—－（）《》〈〉？！…／＼︒︑︔︓︿﹀︹︺︙︐［﹇］﹈︕︖︰︳︴︽︾︵︶｛︷｝︸﹃﹄【︻】︼　～．，；：";

        [TestMethod]
        public void CompatibilityPath_HandlesEmptyAndOneCharacterInputs()
        {
            AssertRangesEqual(
                Opencc.GetSplitRangesSpanCompatibility(ReadOnlySpan<char>.Empty),
                Opencc.GetSplitRangesSpan(ReadOnlySpan<char>.Empty));

            foreach (var input in new[] { "A", "。", "😀", "\uD800", "\uDC00" })
            {
                AssertEquivalent(input, inclusive: false);
                AssertEquivalent(input, inclusive: true);
            }
        }

        [TestMethod]
        public void ModernPath_MatchesCompatibilityPath_ForEveryDelimiter()
        {
            foreach (var delimiter in FullDelimiters)
            {
                var input = "甲" + delimiter + "乙";
                AssertEquivalent(input, inclusive: false);
                AssertEquivalent(input, inclusive: true);
            }
        }

        [TestMethod]
        public void ModernPath_MatchesCompatibilityPath_ForBoundaryCases()
        {
            var inputs = new[]
            {
                string.Empty,
                "delimiterfree",
                "沒有標點符號",
                "。leading",
                "trailing。",
                "。。consecutive。。",
                "甲。乙！丙",
                "ASCII words, punctuation... and tabs\ttoo",
                "甲😀乙。鼖",
                "A\uD800B。C\uDC00D",
            };

            foreach (var input in inputs)
            {
                AssertEquivalent(input, inclusive: false);
                AssertEquivalent(input, inclusive: true);
            }
        }

        [TestMethod]
        public void Dispatcher_PreservesIdsCompatibilityPath()
        {
            var inputs = new[]
            {
                "⿰木木。林",
                "前⿱日月後！",
                "⿲木木木",
                "A\uD800⿰木木。B\uDC00",
            };

            foreach (var input in inputs)
            {
                foreach (var inclusive in new[] { false, true })
                {
                    var expected = Opencc.GetSplitRangesSpanCompatibility(
                        input.AsSpan(), inclusive, preserveIds: true);
                    var actual = Opencc.GetSplitRangesSpan(
                        input.AsSpan(), inclusive, preserveIds: true);
                    AssertRangesEqual(expected, actual);
                }
            }
        }

        [TestMethod]
        public void ModernPath_MatchesCompatibilityPath_ForRandomUtf16()
        {
            var random = new Random(0x5EED);
            var alphabet = ("甲乙ABC" + FullDelimiters + "😀鼖\uD800\uDC00").ToCharArray();

            for (var iteration = 0; iteration < 500; iteration++)
            {
                var chars = new char[random.Next(0, 257)];
                for (var i = 0; i < chars.Length; i++)
                    chars[i] = alphabet[random.Next(alphabet.Length)];

                var input = new string(chars);
                AssertEquivalent(input, inclusive: false);
                AssertEquivalent(input, inclusive: true);
            }
        }

        private static void AssertEquivalent(string input, bool inclusive)
        {
            var expected = Opencc.GetSplitRangesSpanCompatibility(input.AsSpan(), inclusive);
            var actual = Opencc.GetSplitRangesSpan(input.AsSpan(), inclusive);
            AssertRangesEqual(expected, actual);
        }

        private static void AssertRangesEqual(IReadOnlyList<Opencc.Range> expected,
            IReadOnlyList<Opencc.Range> actual)
        {
            Assert.AreEqual(expected.Count, actual.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(expected[i].Start, actual[i].Start, $"Start differs at range {i}");
                Assert.AreEqual(expected[i].Length, actual[i].Length, $"Length differs at range {i}");
            }
        }
    }
}
