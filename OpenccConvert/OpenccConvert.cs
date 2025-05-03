using System.Text;
using CommandLine;
using OpenccNet;

namespace OpenccConvert
{
    internal static class OpenccConvert
    {
        private static readonly object ConsoleLock = new();
        private class Options
        {
            [Option('i', "input", Required = false, HelpText = "Read original text from <file>.")]
            public string? InputFile { get; set; } = null;

            [Option('o', "output", Required = false, HelpText = "Write converted text to <file>.")]
            public string? OutputFile { get; set; } = null;

            [Option('c', "config", Required = true,
                HelpText = "Conversion configuration: [s2t|s2tw|s2twp|s2hk|t2s|tw2s|tw2sp|hk2s|jp2t|t2jp]")]
            public string Config { get; set; } = string.Empty;

            [Option('p', "punct", Default = false, HelpText = "Punctuation conversion: True/False")]
            public bool Punct { get; set; } = false;

            [Option("in-enc", Default = "UTF-8", HelpText = "Encoding for input")]
            public string InputEncoding { get; set; } = "UTF-8";

            [Option("out-enc", Default = "UTF-8", HelpText = "Encoding for output")]
            public string OutputEncoding { get; set; } = "UTF-8";
        }

        private static int Main(string[] args)
        {
            //Console.WriteLine($"[Before] OutputEncoding: {Console.OutputEncoding.EncodingName} ({Console.OutputEncoding.WebName})");
            //Console.WriteLine($"[Before] InputEncoding:  {Console.InputEncoding.EncodingName} ({Console.InputEncoding.WebName})");

            // Set to UTF-8 explicitly
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            //Console.WriteLine($"[After] OutputEncoding: {Console.OutputEncoding.EncodingName} ({Console.OutputEncoding.WebName})");
            //Console.WriteLine($"[After] InputEncoding:  {Console.InputEncoding.EncodingName} ({Console.InputEncoding.WebName})");

            return Parser.Default.ParseArguments<Options>(args)
                .MapResult(RunOptionsAndReturnExitCode, _ => 1);
        }

        private static int RunOptionsAndReturnExitCode(Options opts)
        {
            if (string.IsNullOrEmpty(opts.Config))
            {
                lock (ConsoleLock)
                {
                    Console.Error.WriteLine("Please set conversion configuration.");
                }
                return 1;
            }

            var inputStr = ReadInput(opts.InputFile, opts.InputEncoding);
            var opencc = new Opencc(opts.Config);
            var outputStr = opencc.Convert(inputStr, opts.Punct);
            WriteOutput(opts.OutputFile, outputStr, opts.OutputEncoding);

            var inFrom = opts.InputFile ?? "<stdin>";
            var outTo = opts.OutputFile ?? "<stdout>";
            lock (ConsoleLock)
            {
                Console.Error.WriteLine($"Conversion completed ({opts.Config}): {inFrom} -> {outTo}");
            }

            return 0;
        }

        private static string ReadInput(string? inputFile, string inputEncoding)
        {
            if (inputFile != null)
            {
                return File.ReadAllText(inputFile, Encoding.GetEncoding(inputEncoding));
            }
            lock (ConsoleLock)
            {
                Console.Error.WriteLine("Input text to convert, <ctrl-z> or <ctrl-d> to summit：");
            }
            using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.GetEncoding(inputEncoding));
            return reader.ReadToEnd();
        }

        private static void WriteOutput(string? outputFile, string outputStr, string outputEncoding)
        {
            var encoding = outputEncoding.Equals("utf-8", StringComparison.InvariantCultureIgnoreCase)
                ? new UTF8Encoding(false) // false = no BOM
                : Encoding.GetEncoding(outputEncoding);

            if (outputFile != null)
            {
                File.WriteAllText(outputFile, outputStr, encoding);
            }
            else
            {                
                lock (ConsoleLock)
                {
                    //var bytes = Encoding.UTF8.GetBytes(outputStr);
                    //Console.WriteLine($"Output UTF-8 bytes: {BitConverter.ToString(bytes)}");

                    Console.Error.Write(outputStr);
                }
            }
        }
    }
}
