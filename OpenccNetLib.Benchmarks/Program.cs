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

    public static class Program
    {
        public static void Main(string[] args)
        {
            if (args.Contains("--ids", StringComparer.OrdinalIgnoreCase))
            {
                BenchmarkRunner.Run<IdsPreservationBenchmarks>();
                return;
            }

            BenchmarkRunner.Run<OpenccConvertBenchmarks>();
        }
    }
}