using System.CommandLine;

namespace OpenccNet;

public static class Program
{
    private const string Blue = "\u001b[1;34m";
    private const string Reset = "\u001b[0m";

    public static async Task<int> Main(string[] args)
    {
        var rootCommand =
            new RootCommand(
                $"{Blue}OpenccNet: A CLI tool for OpenccNetLib dictionary generation and Open Chinese text conversion.{Reset}")
            {
                // You can add global options here if any, but none from original code
            };

        // --- Add DictGen Subcommand ---
        var dictGenCommand = DictGenCommand.CreateCommand();
        rootCommand.AddCommand(dictGenCommand);

        // --- Add Convert Subcommand ---
        var convertCommand = ConvertCommand.CreateCommand();
        rootCommand.AddCommand(convertCommand);

        // Invoke the command line parser
        return await rootCommand.InvokeAsync(args);
    }
}