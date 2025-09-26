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
            _opencc = new Opencc("t2s");
            var fullText = File.ReadAllText("Samples/QuanZhiDuZheShiJiao_Hant.txt");
            _inputText = fullText[..Math.Min(Size, fullText.Length)];
        }

        [Benchmark]
        public void BM_Convert_Sized()
        {
            var _ = _opencc!.Convert(_inputText!);
        }
    }

    public static class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkRunner.Run<OpenccConvertBenchmarks>();
        }
    }
}