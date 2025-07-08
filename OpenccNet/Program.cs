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
                // Add any global options here if needed
            };

        // Add subcommands
        rootCommand.Subcommands.Add(DictGenCommand.CreateCommand());
        rootCommand.Subcommands.Add(ConvertCommand.CreateCommand());

        // System.CommandLine beta 5 config wrapper
        var config = new CommandLineConfiguration(rootCommand);

        return await config.InvokeAsync(args);
    }
}