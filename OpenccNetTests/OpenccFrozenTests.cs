using System.Reflection;
using OpenccNetLib;

namespace OpenccNetTests;

[TestClass]
[DoNotParallelize]
public class OpenccFrozenTests
{
    [TestInitialize]
    public void ResetGlobalProvider()
    {
        Opencc.UseDefaultDictionary();
    }

    [TestCleanup]
    public void RestoreGlobalProvider()
    {
        Opencc.UseDefaultDictionary();
    }

    [TestMethod]
    public void IsFrozen_DefaultsToFalse_AndTrueWhenRequested()
    {
        Assert.IsFalse(new Opencc(OpenccConfig.S2T).IsFrozen);
        Assert.IsTrue(new Opencc(OpenccConfig.S2T, isFrozen: true).IsFrozen);
    }

    [TestMethod]
    public void FrozenInstance_RejectsEveryConfigMutationPath()
    {
        var frozen = new Opencc(OpenccConfig.S2T, isFrozen: true);

        Assert.Throws<InvalidOperationException>(() => frozen.Config = "t2s");
        Assert.Throws<InvalidOperationException>(() => frozen.SetConfig("t2s"));
        Assert.Throws<InvalidOperationException>(() => frozen.SetConfig(OpenccConfig.T2S));
        Assert.AreEqual(OpenccConfig.S2T, frozen.GetConfigId());
    }

    [TestMethod]
    public void FrozenInstance_RejectsEveryPreserveIdsMutationPath()
    {
        var frozen = new Opencc(OpenccConfig.S2T, isFrozen: true);

        Assert.Throws<InvalidOperationException>(() => frozen.IsPreserveIds = true);
        Assert.Throws<InvalidOperationException>(() => frozen.SetPreserveIds(true));
        Assert.IsFalse(frozen.IsPreserveIds);
    }

    [TestMethod]
    public void NonFrozenInstance_RetainsMutableConfiguration()
    {
        var mutable = new Opencc(OpenccConfig.S2T);

        mutable.Config = "t2s";
        Assert.AreEqual(OpenccConfig.T2S, mutable.GetConfigId());

        mutable.SetConfig("s2t");
        Assert.AreEqual(OpenccConfig.S2T, mutable.GetConfigId());

        mutable.SetConfig(OpenccConfig.T2S);
        Assert.AreEqual(OpenccConfig.T2S, mutable.GetConfigId());

        mutable.IsPreserveIds = true;
        Assert.IsTrue(mutable.IsPreserveIds);

        mutable.SetPreserveIds(false);
        Assert.IsFalse(mutable.IsPreserveIds);
    }

    [TestMethod]
    public void PlainFrozenInstance_IgnoresLaterGlobalProviderChangesAndReset()
    {
        var frozen = new Opencc(OpenccConfig.S2T, isFrozen: true);
        var globalDictionary = CreateDictionaryWithMapping("汉", "全");

        Opencc.UseCustomDictionary(globalDictionary);

        Assert.AreEqual("漢", frozen.Convert("汉"));
        Assert.AreEqual("全", new Opencc(OpenccConfig.S2T).Convert("汉"));
        Assert.IsNotNull(GetPrivatePlanCache(frozen));

        Opencc.UseDefaultDictionary();

        Assert.AreEqual("漢", frozen.Convert("汉"));
    }

    [TestMethod]
    public void PlainFrozenInstance_UsesBuiltInProviderWhenGlobalCustomIsAlreadyActive()
    {
        Opencc.UseCustomDictionary(CreateDictionaryWithMapping("汉", "全"));

        var frozen = new Opencc(OpenccConfig.S2T, isFrozen: true);

        Assert.AreEqual("漢", frozen.Convert("汉"));
        Assert.AreEqual("全", new Opencc(OpenccConfig.S2T).Convert("汉"));
    }

    [TestMethod]
    public void FrozenCustomInstance_UsesBuiltInBaseAndCustomMappings_AndIgnoresGlobalChanges()
    {
        Opencc.UseCustomDictionary(CreateDictionaryWithMapping("汉", "全"));

        var frozen = new Opencc(
            OpenccConfig.S2T,
            customDictSpecs: new[] { CreateSpec("字", "例") },
            isFrozen: true);

        Assert.IsTrue(frozen.IsFrozen);
        Assert.AreEqual("漢例", frozen.Convert("汉字"));

        Opencc.UseCustomDictionary(CreateDictionaryWithMapping("汉", "另"));
        Assert.AreEqual("漢例", frozen.Convert("汉字"));
        Assert.AreEqual("另字", new Opencc(OpenccConfig.S2T).Convert("汉字"));

        Opencc.UseDefaultDictionary();
        Assert.AreEqual("漢例", frozen.Convert("汉字"));
    }

    private static DictionaryMaxlength CreateDictionaryWithMapping(string key, string value)
    {
        var dictionary = DictionaryLib.FromDicts();
        DictionaryLib.WithCustomDicts(dictionary, new[] { CreateSpec(key, value) });
        return dictionary;
    }

    private static CustomDictSpec CreateSpec(string key, string value)
    {
        return new CustomDictSpec
        {
            Slot = DictSlot.STCharacters,
            Mode = CustomDictMode.Append,
            Pairs = new Dictionary<string, string>
            {
                [key] = value
            }
        };
    }

    private static object? GetPrivatePlanCache(Opencc instance)
    {
        var field = typeof(Opencc).GetField(
            "_planCache",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(field);
        return field.GetValue(instance);
    }
}
