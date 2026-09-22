using OpenccNetLib;

namespace OpenccNetTests;

[TestClass]
public class SealPlanTests
{
    [TestMethod]
    [DataRow(OpenccConfig.S2Seal, "s2seal")]
    [DataRow(OpenccConfig.T2Seal, "t2seal")]
    [DataRow(OpenccConfig.Seal2S, "seal2s")]
    [DataRow(OpenccConfig.Seal2T, "seal2t")]
    public void SealConfig_IsRegistered(OpenccConfig config, string name)
    {
        Assert.AreEqual(config, new Opencc(config).GetConfigId());
        Assert.AreEqual(name, new Opencc(config).Config);
        Assert.AreEqual(config, new Opencc(name).GetConfigId());
        Assert.IsTrue(Opencc.TryParseConfig(name, out var parsed));
        Assert.AreEqual(config, parsed);
        Assert.Contains(name, Opencc.GetSupportedConfigs().ToArray());
    }

    [TestMethod]
    public void T2Seal_PlanUsesExpectedSlotsAndIntermediateOutputs()
    {
        var d = DictionaryLib.Provider;
        var cache = new ConversionPlanCache(() => d);
        var plan = cache.GetPlan(OpenccConfig.T2Seal, true);
        var slots = new[] { d.seal_variants, d.seal_characters_rev, d.st_punctuations };
        var expected = new[]
        {
            "你好，𡭔篆“國際編碼18”",
            "你𿒛，𽌠𽴖“𾇓𿭖𿛛碼18”",
            "你𿒛，𽌠𽴖「𾇓𿭖𿛛碼18」"
        };
        var round = 0;
        var result = plan.ApplySegmentReplace("你好，小篆“國際編碼18”", (input, dicts, union, preserve) =>
        {
            Assert.HasCount(1, dicts);
            Assert.AreSame(slots[round], dicts[0]);
            var output = Opencc.SegmentReplaceForProcessorCount(input, dicts, union, preserve, 1);
            Assert.AreEqual(expected[round], output, $"Round {round + 1}");
            Console.WriteLine($"Round {++round}: {input} -> {output}");
            return output;
        }, false);
        Assert.AreEqual(3, round);
        Assert.AreEqual(expected[2], result);
        Assert.AreSame(plan, cache.GetPlan(OpenccConfig.T2Seal, true));
    }

    [TestMethod]
    public void SealPlans_DoNotShareSlotsAcrossDictionaryInstances()
    {
        var populated = DictionaryLib.Provider;
        var empty = new DictionaryMaxlength();
        var populatedCache = new ConversionPlanCache(() => populated);
        var emptyCache = new ConversionPlanCache(() => empty);
        foreach (var cache in new[] { emptyCache, populatedCache, emptyCache, populatedCache })
        {
            var expected = ReferenceEquals(cache, emptyCache) ? empty : populated;
            var slots = new[] { expected.seal_variants, expected.seal_characters_rev };
            var round = 0;
            cache.GetPlan(OpenccConfig.T2Seal).ApplySegmentReplace("好", (input, dicts, _, _) =>
            {
                Assert.AreSame(slots[round++], dicts[0]);
                return input;
            }, false);
            Assert.AreEqual(2, round);
        }
    }
}
