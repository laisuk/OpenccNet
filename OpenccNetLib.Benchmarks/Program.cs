using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Running;

namespace OpenccNetLib.Benchmarks
{
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 1, iterationCount: 10)]
    [MinColumn, MaxColumn, RankColumn]
    [CsvExporter(CsvSeparator.Comma)]
    [MarkdownExporter, RPlotExporter]
    public class OpenccConvertBenchmarks
    {
        private Opencc? _opencc;
        private string? _inputText;

        [Params(100, 1_000, 10_000, 100_000, 1_000_000)]
        public int Size;

        [GlobalSetup]
        public void Setup()
        {
            _opencc = new Opencc("s2t");
            var fullText = File.ReadAllText("Samples/QuanZhiDuZheShiJiao_Hans.txt");
            _inputText = fullText[..Math.Min(Size, fullText.Length)];
        }

        [Benchmark]
        public void BM_Convert_Sized()
        {
            var _ = _opencc!.Convert(_inputText!);
        }
    }

    // Internal diagnostic only. Run explicitly with `--ids`; do not use these
    // results as part of the published release benchmark table.
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 1, iterationCount: 10)]
    [MinColumn, MaxColumn, RankColumn]
    public class IdsPreservationBenchmarks
    {
        private Opencc? _preserveIdsDisabled;
        private Opencc? _preserveIdsEnabled;
        private string? _inputText;

        [Params(100, 1_000, 10_000, 100_000, 1_000_000)]
        public int Size;

        [GlobalSetup]
        public void Setup()
        {
            _preserveIdsDisabled = new Opencc("s2t", isPreserveIds: false);
            _preserveIdsEnabled = new Opencc("s2t", isPreserveIds: true);

            var fullText = File.ReadAllText("Samples/QuanZhiDuZheShiJiao_Hans.txt");
            _inputText = fullText[..Math.Min(Size, fullText.Length)];
        }

        [Benchmark(Baseline = true)]
        public string PreserveIds_Disabled()
        {
            return _preserveIdsDisabled!.Convert(_inputText!);
        }

        [Benchmark]
        public string PreserveIds_Enabled()
        {
            return _preserveIdsEnabled!.Convert(_inputText!);
        }
    }

    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 2, iterationCount: 6)]
    [MinColumn, MaxColumn, RankColumn]
    public class CompatIdeographsHotPathBenchmarks
    {
        private CompatIdeographs? _compat;
        private string? _input;

        [Params("NoCandidates", "MappingAtBeginning", "MappingAtEnd", "ManyMappings")]
        public string Scenario = null!;

        [GlobalSetup]
        public void Setup()
        {
            _compat = CompatIdeographs.FromText("金\t金\n");
            var ordinary = RepeatToLength("漢字轉換效能測試文本", 100_000);
            _input = Scenario switch
            {
                "NoCandidates" => ordinary,
                "MappingAtBeginning" => "金" + ordinary,
                "MappingAtEnd" => ordinary + "金",
                "ManyMappings" => RepeatToLength("漢字金文本金", 100_000),
                _ => throw new InvalidOperationException(Scenario),
            };
        }

        [Benchmark(Baseline = true)]
        public string Original()
        {
            var input = _input!;
            var output = new System.Text.StringBuilder(input.Length);

            for (var i = 0; i < input.Length; i++)
            {
                var ch = input[i];
                int codePoint;
                if (char.IsHighSurrogate(ch) && i + 1 < input.Length &&
                    char.IsLowSurrogate(input[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(ch, input[++i]);
                }
                else
                {
                    codePoint = ch;
                }

                if (codePoint == 0xF900)
                    output.Append("金");
                else if (codePoint >= 0xD800 && codePoint <= 0xDFFF)
                    output.Append((char)codePoint);
                else
                    output.Append(char.ConvertFromUtf32(codePoint));
            }

            return output.ToString();
        }

        [Benchmark]
        public string Proposed() => _compat!.Normalize(_input!);

        private static string RepeatToLength(string seed, int length)
        {
            return string.Concat(Enumerable.Repeat(seed, (length + seed.Length - 1) / seed.Length))[..length];
        }
    }

    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 2, iterationCount: 6)]
    [MinColumn, MaxColumn, RankColumn]
    public class SplitterHotPathBenchmarks
    {
        private string? _input;

        [Params("DelimiterSparse", "PunctuationHeavy", "NoDelimiters")]
        public string Scenario = null!;

        [GlobalSetup]
        public void Setup()
        {
            _input = Scenario switch
            {
                "DelimiterSparse" => RepeatToLength("這是一段用來測試長篇中文內容的普通文字每隔一段才會出現標點。", 250_000),
                "PunctuationHeavy" => RepeatToLength("甲，乙。丙！丁？", 250_000),
                "NoDelimiters" => RepeatToLength("這是完全沒有任何標點符號的長篇中文文本", 250_000),
                _ => throw new InvalidOperationException(Scenario),
            };
        }

        [Benchmark(Baseline = true)]
        public int Original()
        {
            return Opencc.GetSplitRangesSpanCompatibility(_input.AsSpan(), inclusive: true).Count;
        }

        [Benchmark]
        public int Proposed()
        {
            return Opencc.GetSplitRangesSpanModern(_input.AsSpan(), inclusive: true).Count;
        }

        private static string RepeatToLength(string seed, int length)
        {
            return string.Concat(Enumerable.Repeat(seed, (length + seed.Length - 1) / seed.Length))[..length];
        }
    }

    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 2, iterationCount: 6)]
    [MinColumn, MaxColumn, RankColumn]
    public class IdsSplitterHotPathBenchmarks
    {
        private string? _input;

        [GlobalSetup]
        public void Setup()
        {
            const string seed = "普通文本⿰木木，更多文本⿱日月。";
            _input = string.Concat(Enumerable.Repeat(seed, 10_000));
        }

        [Benchmark]
        public int IdsAwareCompatibilityPath()
        {
            return Opencc.GetSplitRangesSpan(
                _input.AsSpan(), inclusive: true, preserveIds: true).Count;
        }
    }

    public static class Program
    {
        public static void Main(string[] args)
        {
            if (args.Contains("--ids", StringComparer.OrdinalIgnoreCase))
            {
                BenchmarkRunner.Run<IdsPreservationBenchmarks>();
                return;
            }

            if (args.Contains("--hotpaths", StringComparer.OrdinalIgnoreCase))
            {
                BenchmarkRunner.Run(new[]
                {
                    typeof(CompatIdeographsHotPathBenchmarks),
                    typeof(SplitterHotPathBenchmarks),
                    typeof(IdsSplitterHotPathBenchmarks),
                });
                return;
            }

            BenchmarkRunner.Run<OpenccConvertBenchmarks>();
        }
    }
}
