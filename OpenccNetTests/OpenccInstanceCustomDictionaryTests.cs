using System.Reflection;
using OpenccNetLib;

namespace OpenccNetTests;

[TestClass]
[DoNotParallelize]
public class OpenccInstanceCustomDictionaryTests
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
    public void CustomConstructor_RejectsNullSpecs()
    {
        Assert.Throws<ArgumentNullException>(() => new Opencc(
            OpenccConfig.S2T,
            customDictSpecs: null!));

        var source = new Opencc(OpenccConfig.S2T);

        Assert.Throws<ArgumentNullException>(() => source.WithCustomDictionary(
            null!));
    }

    [TestMethod]
    public void CustomBaseConstructors_RejectNullBase()
    {
        Assert.Throws<ArgumentNullException>(() => new Opencc(
            OpenccConfig.S2T,
            customBase: null!));

        Assert.Throws<ArgumentNullException>(() => new Opencc(
            "s2t",
            customBase: null!));
    }

    [TestMethod]
    public void CustomBaseConstructors_UseSuppliedDictionaryForConversion()
    {
        var customBase = CreateDictionaryWithMappings(
            (DictSlot.STCharacters, "汉", "甲"),
            (DictSlot.TSCharacters, "漢", "乙"));

        var byEnum = new Opencc(OpenccConfig.S2T, customBase);
        var byName = new Opencc("t2s", customBase);

        Assert.AreEqual("甲", byEnum.Convert("汉"));
        Assert.AreEqual("乙", byName.Convert("漢"));
    }

    [TestMethod]
    public void CustomBaseInstance_RemainsIsolatedAcrossConfigAndGlobalChanges()
    {
        var customBase = CreateDictionaryWithMappings(
            (DictSlot.STCharacters, "汉", "甲"),
            (DictSlot.TSCharacters, "漢", "乙"));
        var custom = new Opencc(OpenccConfig.S2T, customBase);
        var privateCache = GetPrivateField<ConversionPlanCache>(custom, "_planCache");
        var globalCache = ConversionPlanCache.Current;
        var globalProvider = ConversionPlanCache.Provider;

        Assert.IsNotNull(privateCache);
        Assert.AreNotSame(globalCache, privateCache);
        Assert.AreEqual("甲", custom.Convert("汉"));

        var replacementGlobal = CreateDictionaryWithMappings(
            (DictSlot.STCharacters, "汉", "全"),
            (DictSlot.TSCharacters, "漢", "另"));
        Opencc.UseCustomDictionary(replacementGlobal);

        custom.SetConfig(OpenccConfig.T2S);

        Assert.AreEqual("乙", custom.Convert("漢"));
        Assert.AreEqual("另", new Opencc(OpenccConfig.T2S).Convert("漢"));
        Assert.AreSame(replacementGlobal, ConversionPlanCache.Provider);
        Assert.AreNotSame(globalProvider, ConversionPlanCache.Provider);
        Assert.AreNotSame(globalCache, ConversionPlanCache.Current);
    }

    [TestMethod]
    public void CustomBaseConstructor_RetainsFrozenAndPreserveIdsSemantics()
    {
        var customBase = CreateDictionaryWithMappings(
            (DictSlot.STCharacters, "汉", "甲"));
        var custom = new Opencc(
            OpenccConfig.S2T,
            customBase,
            isPreserveIds: true,
            isFrozen: true);

        Assert.IsTrue(custom.IsFrozen);
        Assert.IsTrue(custom.IsPreserveIds);
        Assert.AreEqual("甲", custom.Convert("汉"));
        Assert.Throws<InvalidOperationException>(() => custom.SetConfig(OpenccConfig.T2S));
    }

    [TestMethod]
    public void CustomConstructor_MaterializesSpecsExactlyOnce()
    {
        var enumerationCount = 0;

        IEnumerable<CustomDictSpec> EnumerateSpecs()
        {
            enumerationCount++;
            yield return CreateSpec(DictSlot.STCharacters, "汉", "甲");
        }

        var custom = new Opencc(OpenccConfig.S2T, EnumerateSpecs());

        Assert.AreEqual(1, enumerationCount);
        Assert.AreEqual("甲", custom.Convert("汉"));
        Assert.AreEqual("甲", custom.Convert("汉"));
        Assert.AreEqual(1, enumerationCount);
    }

    [TestMethod]
    public void CustomInstance_UsesCustomMapping_WithoutChangingOrdinaryOrGlobalState()
    {
        var globalCache = ConversionPlanCache.Current;
        var globalProvider = ConversionPlanCache.Provider;
        var custom = new Opencc(
            OpenccConfig.S2T,
            new[] { CreateSpec(DictSlot.STCharacters, "汉", "甲") });
        var ordinary = new Opencc(OpenccConfig.S2T);

        Assert.AreEqual("甲", custom.Convert("汉"));
        Assert.AreEqual("漢", ordinary.Convert("汉"));
        Assert.AreSame(globalCache, ConversionPlanCache.Current);
        Assert.AreSame(globalProvider, ConversionPlanCache.Provider);
        Assert.AreSame(DictionaryLib.Provider, ConversionPlanCache.Provider);
    }

    [TestMethod]
    public void CustomInstances_WithDifferentSpecs_RemainIsolated()
    {
        var first = new Opencc(
            OpenccConfig.S2T,
            new[] { CreateSpec(DictSlot.STCharacters, "汉", "甲") });
        var second = new Opencc(
            OpenccConfig.S2T,
            new[] { CreateSpec(DictSlot.STCharacters, "汉", "乙") });

        Assert.AreEqual("甲", first.Convert("汉"));
        Assert.AreEqual("乙", second.Convert("汉"));
        Assert.AreEqual("甲", first.Convert("汉"));
    }

    [TestMethod]
    public void CustomInstance_ConfigSwitch_PreservesAllCustomSpecs()
    {
        var custom = new Opencc(
            OpenccConfig.S2T,
            new[]
            {
                CreateSpec(DictSlot.STCharacters, "汉", "甲"),
                CreateSpec(DictSlot.TSCharacters, "漢", "乙")
            });

        Assert.AreEqual("甲", custom.Convert("汉"));

        custom.SetConfig(OpenccConfig.T2S);

        Assert.AreEqual(OpenccConfig.T2S, custom.GetConfigId());
        Assert.AreEqual("乙", custom.Convert("漢"));

        custom.Config = "s2t";

        Assert.AreEqual("甲", custom.Convert("汉"));
    }

    [TestMethod]
    public void WithCustomDictionary_ReturnsNewInstance_AndPreservesSourceState()
    {
        var source = new Opencc(OpenccConfig.T2S, isPreserveIds: true);
        var custom = source.WithCustomDictionary(
            new[] { CreateSpec(DictSlot.TSCharacters, "漢", "乙") });

        Assert.AreNotSame(source, custom);
        Assert.AreEqual(OpenccConfig.T2S, source.GetConfigId());
        Assert.AreEqual(OpenccConfig.T2S, custom.GetConfigId());
        Assert.IsTrue(source.IsPreserveIds);
        Assert.IsTrue(custom.IsPreserveIds);
        Assert.AreEqual("汉", source.Convert("漢"));
        Assert.AreEqual("乙", custom.Convert("漢"));
        Assert.AreEqual("汉", source.Convert("漢"));
    }

    [TestMethod]
    public void CustomInstance_PunctuationModes_UseSeparateInstancePlans()
    {
        var custom = new Opencc(
            OpenccConfig.S2T,
            new[] { CreateSpec(DictSlot.STPunctuations, "“", "【") });

        Assert.AreEqual("“漢", custom.Convert("“汉", punctuation: false));
        Assert.AreEqual("【漢", custom.Convert("“汉", punctuation: true));
        Assert.AreEqual("“漢", custom.Convert("“汉", punctuation: false));
    }

    [TestMethod]
    public void EmptySpecs_CreateIsolatedCache_WithDefaultBehavior()
    {
        var globalCache = ConversionPlanCache.Current;
        var custom = new Opencc(
            OpenccConfig.S2T,
            Array.Empty<CustomDictSpec>());
        var privateCache = GetPrivateField<ConversionPlanCache>(custom, "_planCache");

        Assert.IsNotNull(privateCache);
        Assert.AreNotSame(globalCache, privateCache);
        Assert.AreSame(globalCache, ConversionPlanCache.Current);
        Assert.AreEqual("漢字", custom.Convert("汉字"));
        Assert.AreEqual("漢字", new Opencc(OpenccConfig.S2T).Convert("汉字"));
    }

    [TestMethod]
    public void OrdinaryInstance_DoesNotAllocatePrivateCustomState()
    {
        var ordinaryByEnum = new Opencc(OpenccConfig.S2T);
        var ordinaryByName = new Opencc("s2t");

        Assert.IsNull(GetPrivateField<ConversionPlanCache>(
            ordinaryByEnum,
            "_planCache"));

        Assert.IsNull(GetPrivateField<ConversionPlanCache>(
            ordinaryByName,
            "_planCache"));
    }

    [TestMethod]
    public void CustomConstructor_UsesActiveGlobalProviderAsBase()
    {
        var globalDictionary = DictionaryLib.FromDicts();
        DictionaryLib.WithCustomDicts(
            globalDictionary,
            new[] { CreateSpec(DictSlot.STCharacters, "汉", "全") });

        Opencc.UseCustomDictionary(globalDictionary);

        var isolated = new Opencc(
            OpenccConfig.S2T,
            new[] { CreateSpec(DictSlot.STCharacters, "字", "例") });

        Assert.AreEqual("全例", isolated.Convert("汉字"));

        // Instance customization must not mutate the active global dictionary.
        Assert.AreEqual("全字", new Opencc(OpenccConfig.S2T).Convert("汉字"));
    }

    [TestMethod]
    public void CustomInstance_SnapshotsActiveGlobalProviderAtConstruction()
    {
        var firstGlobal = DictionaryLib.FromDicts();
        DictionaryLib.WithCustomDicts(
            firstGlobal,
            new[] { CreateSpec(DictSlot.STCharacters, "汉", "全") });

        Opencc.UseCustomDictionary(firstGlobal);

        var isolated = new Opencc(
            OpenccConfig.S2T,
            new[] { CreateSpec(DictSlot.STCharacters, "字", "例") });

        var secondGlobal = DictionaryLib.FromDicts();
        DictionaryLib.WithCustomDicts(
            secondGlobal,
            new[] { CreateSpec(DictSlot.STCharacters, "汉", "另") });

        Opencc.UseCustomDictionary(secondGlobal);

        Assert.AreEqual("全例", isolated.Convert("汉字"));
        Assert.AreEqual("另字", new Opencc(OpenccConfig.S2T).Convert("汉字"));
    }

    [TestMethod]
    public void CustomConstructor_SnapshotsSpecData_AgainstCallerMutation()
    {
        var spec = CreateSpec(DictSlot.STCharacters, "汉", "甲");
        var specs = new[] { spec };

        var custom = new Opencc(OpenccConfig.S2T, specs);

        spec.Pairs!["汉"] = "乙";
        spec.Pairs["字"] = "丙";

        Assert.AreEqual("甲字", custom.Convert("汉字"));
    }

    [TestMethod]
    public void WithCustomDictionary_OnCustomSource_DoesNotLayerExistingSpecs()
    {
        var source = new Opencc(
            OpenccConfig.S2T,
            new[] { CreateSpec(DictSlot.STCharacters, "汉", "甲") });

        var replacement = source.WithCustomDictionary(
            new[] { CreateSpec(DictSlot.STCharacters, "字", "乙") });

        Assert.AreEqual("甲字", source.Convert("汉字"));
        Assert.AreEqual("漢乙", replacement.Convert("汉字"));
        Assert.AreEqual("漢字", new Opencc(OpenccConfig.S2T).Convert("汉字"));
    }

    [TestMethod]
    public void LegacyGlobalDictionaryApis_RemainGlobal()
    {
        var isolated = new Opencc(
            OpenccConfig.S2T,
            new[] { CreateSpec(DictSlot.STCharacters, "汉", "例") });
        var globalDictionary = DictionaryLib.FromDicts();
        DictionaryLib.WithCustomDicts(
            globalDictionary,
            new[] { CreateSpec(DictSlot.STCharacters, "汉", "全") });

        Opencc.UseCustomDictionary(globalDictionary);

        Assert.AreSame(globalDictionary, ConversionPlanCache.Provider);
        Assert.AreEqual("全", new Opencc(OpenccConfig.S2T).Convert("汉"));
        Assert.AreEqual("例", isolated.Convert("汉"));

        Opencc.UseDefaultDictionary();

        Assert.AreSame(DictionaryLib.Provider, ConversionPlanCache.Provider);
        Assert.AreEqual("漢", new Opencc(OpenccConfig.S2T).Convert("汉"));
        Assert.AreEqual("例", isolated.Convert("汉"));
    }

    private static CustomDictSpec CreateSpec(
        DictSlot slot,
        string key,
        string value)
    {
        return new CustomDictSpec
        {
            Slot = slot,
            Mode = CustomDictMode.Append,
            Pairs = new Dictionary<string, string>
            {
                [key] = value
            }
        };
    }

    private static DictionaryMaxlength CreateDictionaryWithMappings(
        params (DictSlot Slot, string Key, string Value)[] mappings)
    {
        var dictionary = DictionaryLib.FromDicts();
        DictionaryLib.WithCustomDicts(
            dictionary,
            mappings.Select(mapping => CreateSpec(
                mapping.Slot,
                mapping.Key,
                mapping.Value)));
        return dictionary;
    }

    private static T? GetPrivateField<T>(Opencc instance, string name)
        where T : class
    {
        var field = typeof(Opencc).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(field, "Expected private field '" + name + "'.");
        return (T?)field.GetValue(instance);
    }
}
