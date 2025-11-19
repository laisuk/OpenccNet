using System.Text.RegularExpressions;

namespace OpenccNetTests
{
    /// <summary>
    /// Verifies that JSON-style UTF-16 surrogate pair escape sequences
    /// are correctly decoded into full Unicode scalar values.
    /// Uses the rare CJK Ext-B character 𡃁 (U+210C1).
    /// </summary>
    [TestClass]
    public class DecodeJsonSurrogatePairsTests
    {
        private static readonly Regex SurrogatePairRegex =
            new Regex(
                @"\\u(?<hi>[dD][89ABab][0-9A-Fa-f]{2})\\u(?<lo>[dD][CDEFcdef][0-9A-Fa-f]{2})",
                RegexOptions.Compiled);

        private static string DecodeJsonSurrogatePairs(string json)
        {
            return SurrogatePairRegex.Replace(json, m =>
            {
                var hi = Convert.ToInt32(m.Groups["hi"].Value, 16);
                var lo = Convert.ToInt32(m.Groups["lo"].Value, 16);

                var codepoint =
                    0x10000 +
                    ((hi - 0xD800) << 10) +
                    (lo - 0xDC00);

                return char.ConvertFromUtf32(codepoint);
            });
        }

        // 𡃁 = U+210C1 = high: D844, low: DCC1

        [TestMethod]
        public void Decodes_ExtB_SurrogatePair_To_Single_Character()
        {
            // Correct surrogate pair: "\uD844\uDCC1" == 𡃁
            const string input = "{\"title\":\"\\uD844\\uDCC1\"}";

            var output = DecodeJsonSurrogatePairs(input);

            // Should contain decoded character
            Assert.Contains("𡃁", output);

            // Should NOT contain surrogate escape anymore
            Assert.DoesNotContain(@"\uD844\uDCC1",
                output, "Output still contains the surrogate escape sequence.");
        }

        [TestMethod]
        public void Decodes_Lowercase_Hex_SurrogatePair()
        {
            const string input = "\"title\":\"\\uD844\\uDCC1\"";

            var output = DecodeJsonSurrogatePairs(input);

            Assert.Contains("𡃁", output);
            Assert.IsLessThan(
                0,
                output.IndexOf(@"\uD844\uDCC1", StringComparison.OrdinalIgnoreCase),
                "Output should not contain the original lowercase surrogate escape.");
        }

        [TestMethod]
        public void Decodes_SurrogatePair_Inside_Mixed_Text()
        {
            const string input = "\"msg\":\"開放中文\\uD844\\uDCC1轉換\"";

            var output = DecodeJsonSurrogatePairs(input);

            Assert.AreEqual("\"msg\":\"開放中文𡃁轉換\"", output);
        }

        [TestMethod]
        public void Leaves_Strings_Without_Surrogates_Unchanged()
        {
            const string input = "{\"title\":\"開放中文轉換\"}";

            var output = DecodeJsonSurrogatePairs(input);

            Assert.AreEqual(input, output);
        }

        [TestMethod]
        public void Leaves_Lone_High_Surrogate_Escape_Unchanged()
        {
            const string input = "\"broken\":\"\\uD844\""; // only high half

            var output = DecodeJsonSurrogatePairs(input);

            Assert.AreEqual(input, output);
        }

        [TestMethod]
        public void Is_Idempotent_When_Applied_Multiple_Times()
        {
            const string input = "\"title\":\"\\uD844\\uDCC1\"";

            var once = DecodeJsonSurrogatePairs(input);
            var twice = DecodeJsonSurrogatePairs(once);

            Assert.AreEqual(once, twice);
            Assert.Contains("𡃁", once);
        }
    }
}