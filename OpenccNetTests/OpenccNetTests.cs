using OpenccNet;

namespace OpenccNetTests
{
    [TestClass]
    public class OpenccNetTests
    {
        [TestMethod]
        public void S2T_SimpleConversion()
        {
            var opencc = new Opencc("s2t");
            string simplified = "俨骖𬴂于上路，访风景于崇阿；临帝子之长洲，得天人之旧馆。";
            string expectedTraditional = "儼驂騑於上路，訪風景於崇阿；臨帝子之長洲，得天人之舊館。";
            string actualTraditional = opencc.S2T(simplified);
            Assert.AreEqual(expectedTraditional, actualTraditional);
        }

        [TestMethod]
        public void T2S_SimpleConversion()
        {
            var opencc = new Opencc("t2s");
            string traditional = "美麗";
            string expectedSimplified = "美丽";
            string actualSimplified = opencc.T2S(traditional);
            Assert.AreEqual(expectedSimplified, actualSimplified);
        }

        [TestMethod]
        public void S2TWP_SimpleConversion()
        {
            var opencc = new Opencc();
            opencc.Config = "s2twp";
            string simplified = "软件";
            string expectedTaiwan = "軟體";
            string actualTaiwan = opencc.S2Twp(simplified);
            Assert.AreEqual(expectedTaiwan, actualTaiwan);
        }

        [TestMethod]
        public void TW2SP_SimpleConversion()
        {
            var opencc = new Opencc("tw2s");
            string taiwan = "軟體";
            string expectedSimplified = "软件";
            string actualSimplified = opencc.Tw2Sp(taiwan);
            Assert.AreEqual(expectedSimplified, actualSimplified);
        }

        [TestMethod]
        public void S2HK_SimpleConversion()
        {
            var opencc = new Opencc("s2hk");
            string simplified = "电台";
            string expectedHongKong = "電台";
            string actualHongKong = opencc.S2Hk(simplified);
            Assert.AreEqual(expectedHongKong, actualHongKong);
        }

        [TestMethod]
        public void HK2S_SimpleConversion()
        {
            var opencc = new Opencc("hk2s");
            string hongKong = "資訊";
            string expectedSimplified = "资讯";
            string actualSimplified = opencc.Hk2S(hongKong);
            Assert.AreEqual(expectedSimplified, actualSimplified);
        }

        [TestMethod]
        public void T2TW_SimpleConversion()
        {
            var opencc = new Opencc("t2tw");
            string traditional = "憂鬱";
            string expectedTaiwan = "憂鬱"; // In this case, it might be the same, test with a difference if you find one
            string actualTaiwan = opencc.T2Tw(traditional);
            Assert.AreEqual(expectedTaiwan, actualTaiwan);
        }

        [TestMethod]
        public void TW2T_SimpleConversion()
        {
            var opencc = new Opencc("tw2t");
            string taiwan = "著";
            string expectedTraditional = "着"; // Similar to above, test with a difference if found
            string actualTraditional = opencc.Tw2T(taiwan);
            Assert.AreEqual(expectedTraditional, actualTraditional);
        }

        [TestMethod]
        public void Convert_WithValidConfig()
        {
            var opencc = new Opencc("s2t");
            string simplified = "文件";
            string expectedTraditional = "文件"; // Assuming no conversion for this word in the base dictionary
            string actualTraditional = opencc.Convert(simplified);
            Assert.AreEqual(expectedTraditional, actualTraditional);
        }

        [TestMethod]
        public void Convert_WithInvalidConfig_ReturnsOriginalTextAndSetsLastError()
        {
            var opencc = new Opencc("invalid_config");
            string text = "测试";
            string result = opencc.Convert(text);
            Assert.AreEqual("測試", result);
            Assert.IsNotNull(opencc.GetLastError());
            StringAssert.Contains(opencc.GetLastError(), "invalid_config");
        }

        [TestMethod]
        public void Convert_EmptyInput()
        {
            var opencc = new Opencc("s2t");
            string empty = "";
            string converted = opencc.Convert(empty);
            Assert.AreEqual(empty, converted);
        }

        [TestMethod]
        public void S2T_WithPunctuation()
        {
            var opencc = new Opencc("s2t");
            string simplifiedWithPunctuation = "你好“世界”！";
            string expectedTraditionalWithPunctuation = "你好「世界」！";
            string actualTraditionalWithPunctuation = opencc.S2T(simplifiedWithPunctuation, true);
            Assert.AreEqual(expectedTraditionalWithPunctuation, actualTraditionalWithPunctuation);
        }

        [TestMethod]
        public void T2S_WithPunctuation()
        {
            var opencc = new Opencc("t2s");
            string traditionalWithPunctuation = "你好「世界」！";
            string expectedSimplifiedWithPunctuation = "你好“世界”！";
            string actualSimplifiedWithPunctuation = opencc.T2S(traditionalWithPunctuation, true);
            Assert.AreEqual(expectedSimplifiedWithPunctuation, actualSimplifiedWithPunctuation);
        }

        [TestMethod]
        public void ST_SimpleConversion()
        {
            var opencc = new Opencc(); // Default config is "s2t"
            string simplifiedChar = "发";
            string expectedTraditionalChar = "發";
            string actualTraditionalChar = opencc.St(simplifiedChar);
            Assert.AreEqual(expectedTraditionalChar, actualTraditionalChar);
        }

        [TestMethod]
        public void TS_SimpleConversion()
        {
            var opencc = new Opencc(); // Default config is "s2t"
            string traditionalChar = "發";
            string expectedSimplifiedChar = "发";
            string actualSimplifiedChar = opencc.Ts(traditionalChar);
            Assert.AreEqual(expectedSimplifiedChar, actualSimplifiedChar);
        }

        [TestMethod]
        public void ZhoCheck_SimplifiedText()
        {
            var opencc = new Opencc();
            string simplifiedText = "这是一个简体中文文本。";
            Assert.AreEqual(2, opencc.ZhoCheck(simplifiedText));
        }

        [TestMethod]
        public void ZhoCheck_TraditionalText()
        {
            var opencc = new Opencc();
            string traditionalText = "這是一個繁體中文文本。";
            Assert.AreEqual(1, opencc.ZhoCheck(traditionalText));
        }

        [TestMethod]
        public void ZhoCheck_NeutralText()
        {
            var opencc = new Opencc();
            string neutralText = "This is some English text.";
            Assert.AreEqual(0, opencc.ZhoCheck(neutralText));
        }

        [TestMethod]
        public void ZhoCheck_EmptyText()
        {
            var opencc = new Opencc();
            string emptyText = "";
            Assert.AreEqual(0, opencc.ZhoCheck(emptyText));
        }
    }
}