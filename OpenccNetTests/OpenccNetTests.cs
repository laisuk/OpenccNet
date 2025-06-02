using OpenccNetLib;

namespace OpenccNetTests;

[TestClass]
public class OpenccNetTests
{
    [TestMethod]
    public void S2T_SimpleConversion()
    {
        var opencc = new Opencc("s2t");
        const string simplified = "俨骖𬴂于上路，访风景于崇阿；临帝子之长洲，得天人之旧馆。";
        const string expectedTraditional = "儼驂騑於上路，訪風景於崇阿；臨帝子之長洲，得天人之舊館。";
        var actualTraditional = opencc.S2T(simplified);
        Assert.AreEqual(expectedTraditional, actualTraditional);
    }

    [TestMethod]
    public void T2S_SimpleConversion()
    {
        var opencc = new Opencc("t2s");
        const string traditional = "美麗";
        const string expectedSimplified = "美丽";
        var actualSimplified = opencc.T2S(traditional);
        Assert.AreEqual(expectedSimplified, actualSimplified);
    }

    [TestMethod]
    public void S2TWP_SimpleConversion()
    {
        var opencc = new Opencc
        {
            Config = "s2twp"
        };
        const string simplified = "软件";
        const string expectedTaiwan = "軟體";
        var actualTaiwan = opencc.S2Twp(simplified);
        Assert.AreEqual(expectedTaiwan, actualTaiwan);
    }

    [TestMethod]
    public void TW2SP_SimpleConversion()
    {
        var opencc = new Opencc("tw2s");
        const string taiwan = "軟體";
        const string expectedSimplified = "软件";
        var actualSimplified = opencc.Tw2Sp(taiwan);
        Assert.AreEqual(expectedSimplified, actualSimplified);
    }

    [TestMethod]
    public void S2HK_SimpleConversion()
    {
        var opencc = new Opencc("s2hk");
        const string simplified = "电台";
        const string expectedHongKong = "電台";
        var actualHongKong = opencc.S2Hk(simplified);
        Assert.AreEqual(expectedHongKong, actualHongKong);
    }

    [TestMethod]
    public void HK2S_SimpleConversion()
    {
        var opencc = new Opencc("hk2s");
        const string hongKong = "資訊";
        const string expectedSimplified = "资讯";
        var actualSimplified = opencc.Hk2S(hongKong);
        Assert.AreEqual(expectedSimplified, actualSimplified);
    }

    [TestMethod]
    public void T2TW_SimpleConversion()
    {
        var opencc = new Opencc("t2tw");
        const string traditional = "憂鬱";
        const string expectedTaiwan = "憂鬱"; // In this case, it might be the same, test with a difference if you find one
        var actualTaiwan = opencc.T2Tw(traditional);
        Assert.AreEqual(expectedTaiwan, actualTaiwan);
    }

    [TestMethod]
    public void TW2T_SimpleConversion()
    {
        var opencc = new Opencc("tw2t");
        const string taiwan = "著";
        const string expectedTraditional = "着"; // Similar to above, test with a difference if found
        var actualTraditional = opencc.Tw2T(taiwan);
        Assert.AreEqual(expectedTraditional, actualTraditional);
    }

    [TestMethod]
    public void Convert_WithValidConfig()
    {
        var opencc = new Opencc("s2t");
        const string simplified = "文件";
        const string expectedTraditional = "文件"; // Assuming no conversion for this word in the base dictionary
        var actualTraditional = opencc.Convert(simplified);
        Assert.AreEqual(expectedTraditional, actualTraditional);
    }

    [TestMethod]
    public void Convert_WithInvalidConfig_ReturnsOriginalTextAndSetsLastError()
    {
        var opencc = new Opencc("invalid_config");
        const string text = "测试";
        var result = opencc.Convert(text);
        Assert.AreEqual("測試", result);
        Assert.IsNotNull(opencc.GetLastError());
        StringAssert.Contains(opencc.GetLastError(), "invalid_config");
    }

    [TestMethod]
    public void Convert_EmptyInput()
    {
        var opencc = new Opencc("s2t");
        const string empty = "";
        var converted = opencc.Convert(empty);
        Assert.AreEqual(empty, converted);
    }

    [TestMethod]
    public void S2T_WithPunctuation()
    {
        var opencc = new Opencc("s2t");
        const string simplifiedWithPunctuation = "你好“世界”！“龙马精神”";
        const string expectedTraditionalWithPunctuation = "你好「世界」！「龍馬精神」";
        var actualTraditionalWithPunctuation = opencc.S2T(simplifiedWithPunctuation, true);
        Assert.AreEqual(expectedTraditionalWithPunctuation, actualTraditionalWithPunctuation);
    }

    [TestMethod]
    public void T2S_WithPunctuation()
    {
        var opencc = new Opencc("t2s");
        const string traditionalWithPunctuation = "你好「世界」！";
        const string expectedSimplifiedWithPunctuation = "你好“世界”！";
        var actualSimplifiedWithPunctuation = opencc.T2S(traditionalWithPunctuation, true);
        Assert.AreEqual(expectedSimplifiedWithPunctuation, actualSimplifiedWithPunctuation);
    }
    
    [TestMethod]
    public void switch_conversion()
    {
        var opencc = new Opencc("s2t"); 
        var result = opencc.Convert("动态切换转换方式");
        Assert.AreEqual("動態切換轉換方式", result);
        // opencc.Config = "t2s";
        opencc.SetConfig("t2s");
        result = opencc.Convert("動態切換轉換方式");
        Assert.AreEqual("动态切换转换方式", result);   
    }

    [TestMethod]
    public void ST_SimpleConversion()
    {
        const string simplifiedChar = "发";
        const string expectedTraditionalChar = "發";
        var actualTraditionalChar = Opencc.St(simplifiedChar);
        Assert.AreEqual(expectedTraditionalChar, actualTraditionalChar);
    }

    [TestMethod]
    public void TS_SimpleConversion()
    {
        const string traditionalChar = "發";
        const string expectedSimplifiedChar = "发";
        var actualSimplifiedChar = Opencc.Ts(traditionalChar);
        Assert.AreEqual(expectedSimplifiedChar, actualSimplifiedChar);
    }

    [TestMethod]
    public void ZhoCheck_SimplifiedText()
    {
        const string simplifiedText = "这是一个简体中文文本。";
        Assert.AreEqual(2, Opencc.ZhoCheck(simplifiedText));
    }

    [TestMethod]
    public void ZhoCheck_TraditionalText()
    {
        const string traditionalText = "這是一個繁體中文文本。";
        Assert.AreEqual(1, Opencc.ZhoCheck(traditionalText));
    }

    [TestMethod]
    public void ZhoCheck_NeutralText()
    {
        const string neutralText = "This is some English text.";
        Assert.AreEqual(0, Opencc.ZhoCheck(neutralText));
    }

    [TestMethod]
    public void ZhoCheck_EmptyText()
    {
        const string emptyText = "";
        Assert.AreEqual(0, Opencc.ZhoCheck(emptyText));
    }
    
    [TestMethod]
    public void UseCustomDictionary()
    {
        Opencc.UseCustomDictionary(DictionaryLib.FromDicts());
        var opencc = new Opencc("s2t");
        const string sText = "美丽汉字";
        const string tText = "美麗漢字";
        Assert.AreEqual(tText, opencc.Convert(sText));
    }
}