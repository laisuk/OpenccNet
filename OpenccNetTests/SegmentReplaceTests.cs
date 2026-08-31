using OpenccNetLib;

namespace OpenccNetTests;

[TestClass]
public class SegmentReplaceTests
{
    [TestMethod]
    public void ParallelPolicy_DisablesChunking_WhenOnlyOneProcessorIsAvailable()
    {
        Assert.IsFalse(Opencc.ShouldRunSegmentReplaceInParallel(
            int.MaxValue,
            int.MaxValue,
            processorCount: 1));
        Assert.IsFalse(Opencc.ShouldRunSegmentReplaceInParallel(
            int.MaxValue,
            int.MaxValue,
            processorCount: 0));
    }

    [TestMethod]
    public void ParallelPolicy_PreservesExistingMulticoreGates()
    {
        Assert.IsFalse(Opencc.ShouldRunSegmentReplaceInParallel(
            textLength: 1,
            rangeCount: 1,
            processorCount: 2));
        Assert.IsTrue(Opencc.ShouldRunSegmentReplaceInParallel(
            textLength: 200_000,
            rangeCount: 1,
            processorCount: 2));
        Assert.IsTrue(Opencc.ShouldRunSegmentReplaceInParallel(
            textLength: 1,
            rangeCount: 1_001,
            processorCount: 2));
    }

    [TestMethod]
    public void SingleProcessorPath_PreservesDelimiterBoundariesAndOutputOrder()
    {
        var dictionary = CreateDictionary(new Dictionary<string, string>
        {
            ["甲。乙"] = "跨界",
            ["甲"] = "A",
            ["乙"] = "B",
        });
        var dictionaries = new[] { dictionary };
        var union = StarterUnion.Build(dictionaries);
        var input = string.Concat(Enumerable.Repeat("甲。乙！", 2_501));

        var sequential = Opencc.SegmentReplaceForProcessorCount(
            input, dictionaries, union, preserveIds: false, processorCount: 1);
        var parallel = Opencc.SegmentReplaceForProcessorCount(
            input, dictionaries, union, preserveIds: false, processorCount: 2);
        var expected = string.Concat(Enumerable.Repeat("A。B！", 2_501));

        Assert.AreEqual(expected, sequential);
        Assert.AreEqual(parallel, sequential);
        Assert.IsFalse(sequential.Contains("跨界", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SingleProcessorPath_PreservesIdsExpressions()
    {
        var dictionary = CreateDictionary(new Dictionary<string, string>
        {
            ["木"] = "X",
        });
        var dictionaries = new[] { dictionary };
        var union = StarterUnion.Build(dictionaries);
        var input = string.Concat(Enumerable.Repeat("⿰木木。", 2_001));

        var actual = Opencc.SegmentReplaceForProcessorCount(
            input, dictionaries, union, preserveIds: true, processorCount: 1);

        Assert.AreEqual(input, actual);
    }

    private static DictWithMaxLength CreateDictionary(Dictionary<string, string> entries)
    {
        var lengths = entries.Keys.Select(key => key.Length).Distinct().ToArray();
        ulong lengthMask = 0;
        foreach (var length in lengths)
            lengthMask |= 1UL << (length - 1);

        return new DictWithMaxLength
        {
            Dict = entries,
            MinLength = lengths.Min(),
            MaxLength = lengths.Max(),
            LengthMask = lengthMask,
        };
    }
}
