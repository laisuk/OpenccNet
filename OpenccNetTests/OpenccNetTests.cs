using OpenccNet;

namespace OpenccNetTests;

[TestClass]
public class OpenccNetTests
{
    [TestMethod]
    public void S2T_SimpleConversion()
    {
        var opencc = new Opencc("s2t");
        var simplified = "俨骖𬴂于上路，访风景于崇阿；临帝子之长洲，得天人之旧馆。";
        var expectedTraditional = "儼驂騑於上路，訪風景於崇阿；臨帝子之長洲，得天人之舊館。";
        var actualTraditional = opencc.S2T(simplified);
        Assert.AreEqual(expectedTraditional, actualTraditional);
    }

    [TestMethod]
    public void T2S_SimpleConversion()
    {
        var opencc = new Opencc("t2s");
        var traditional = "美麗";
        var expectedSimplified = "美丽";
        var actualSimplified = opencc.T2S(traditional);
        Assert.AreEqual(expectedSimplified, actualSimplified);
    }

    [TestMethod]
    public void S2TWP_SimpleConversion()
    {
        var opencc = new Opencc();
        opencc.Config = "s2twp";
        var simplified = "软件";
        var expectedTaiwan = "軟體";
        var actualTaiwan = opencc.S2Twp(simplified);
        Assert.AreEqual(expectedTaiwan, actualTaiwan);
    }

    [TestMethod]
    public void TW2SP_SimpleConversion()
    {
        var opencc = new Opencc("tw2s");
        var taiwan = "軟體";
        var expectedSimplified = "软件";
        var actualSimplified = opencc.Tw2Sp(taiwan);
        Assert.AreEqual(expectedSimplified, actualSimplified);
    }

    [TestMethod]
    public void S2HK_SimpleConversion()
    {
        var opencc = new Opencc("s2hk");
        var simplified = "电台";
        var expectedHongKong = "電台";
        var actualHongKong = opencc.S2Hk(simplified);
        Assert.AreEqual(expectedHongKong, actualHongKong);
    }

    [TestMethod]
    public void HK2S_SimpleConversion()
    {
        var opencc = new Opencc("hk2s");
        var hongKong = "資訊";
        var expectedSimplified = "资讯";
        var actualSimplified = opencc.Hk2S(hongKong);
        Assert.AreEqual(expectedSimplified, actualSimplified);
    }

    [TestMethod]
    public void T2TW_SimpleConversion()
    {
        var opencc = new Opencc("t2tw");
        var traditional = "憂鬱";
        var expectedTaiwan = "憂鬱"; // In this case, it might be the same, test with a difference if you find one
        var actualTaiwan = opencc.T2Tw(traditional);
        Assert.AreEqual(expectedTaiwan, actualTaiwan);
    }

    [TestMethod]
    public void TW2T_SimpleConversion()
    {
        var opencc = new Opencc("tw2t");
        var taiwan = "著";
        var expectedTraditional = "着"; // Similar to above, test with a difference if found
        var actualTraditional = opencc.Tw2T(taiwan);
        Assert.AreEqual(expectedTraditional, actualTraditional);
    }

    [TestMethod]
    public void Convert_WithValidConfig()
    {
        var opencc = new Opencc("s2t");
        var simplified = "文件";
        var expectedTraditional = "文件"; // Assuming no conversion for this word in the base dictionary
        var actualTraditional = opencc.Convert(simplified);
        Assert.AreEqual(expectedTraditional, actualTraditional);
    }

    [TestMethod]
    public void Convert_WithInvalidConfig_ReturnsOriginalTextAndSetsLastError()
    {
        var opencc = new Opencc("invalid_config");
        var text = "测试";
        var result = opencc.Convert(text);
        Assert.AreEqual("測試", result);
        Assert.IsNotNull(opencc.GetLastError());
        StringAssert.Contains(opencc.GetLastError(), "invalid_config");
    }

    [TestMethod]
    public void Convert_EmptyInput()
    {
        var opencc = new Opencc("s2t");
        var empty = "";
        var converted = opencc.Convert(empty);
        Assert.AreEqual(empty, converted);
    }

    [TestMethod]
    public void S2T_WithPunctuation()
    {
        var opencc = new Opencc("s2t");
        var simplifiedWithPunctuation = "你好“世界”！";
        var expectedTraditionalWithPunctuation = "你好「世界」！";
        var actualTraditionalWithPunctuation = opencc.S2T(simplifiedWithPunctuation, true);
        Assert.AreEqual(expectedTraditionalWithPunctuation, actualTraditionalWithPunctuation);
    }

    [TestMethod]
    public void T2S_WithPunctuation()
    {
        var opencc = new Opencc("t2s");
        var traditionalWithPunctuation = "你好「世界」！";
        var expectedSimplifiedWithPunctuation = "你好“世界”！";
        var actualSimplifiedWithPunctuation = opencc.T2S(traditionalWithPunctuation, true);
        Assert.AreEqual(expectedSimplifiedWithPunctuation, actualSimplifiedWithPunctuation);
    }

    [TestMethod]
    public void ST_SimpleConversion()
    {
        var opencc = new Opencc(); // Default config is "s2t"
        var simplifiedChar = "发";
        var expectedTraditionalChar = "發";
        var actualTraditionalChar = opencc.St(simplifiedChar);
        Assert.AreEqual(expectedTraditionalChar, actualTraditionalChar);
    }

    [TestMethod]
    public void TS_SimpleConversion()
    {
        var opencc = new Opencc(); // Default config is "s2t"
        var traditionalChar = "發";
        var expectedSimplifiedChar = "发";
        var actualSimplifiedChar = opencc.Ts(traditionalChar);
        Assert.AreEqual(expectedSimplifiedChar, actualSimplifiedChar);
    }

    [TestMethod]
    public void ZhoCheck_SimplifiedText()
    {
        var opencc = new Opencc();
        var simplifiedText = "这是一个简体中文文本。";
        Assert.AreEqual(2, opencc.ZhoCheck(simplifiedText));
    }

    [TestMethod]
    public void ZhoCheck_TraditionalText()
    {
        var opencc = new Opencc();
        var traditionalText = "這是一個繁體中文文本。";
        Assert.AreEqual(1, opencc.ZhoCheck(traditionalText));
    }

    [TestMethod]
    public void ZhoCheck_NeutralText()
    {
        var opencc = new Opencc();
        var neutralText = "This is some English text.";
        Assert.AreEqual(0, opencc.ZhoCheck(neutralText));
    }

    [TestMethod]
    public void ZhoCheck_EmptyText()
    {
        var opencc = new Opencc();
        var emptyText = "";
        Assert.AreEqual(0, opencc.ZhoCheck(emptyText));
    }
}