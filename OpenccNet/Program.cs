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
                $"{Blue}OpenccNet – Convert Chinese text or Office documents using OpenccNetLib configurations and generate dictionaries.{Reset}")
            {
                // Add any global options here if needed
            };

        // Add subcommands
        rootCommand.Subcommands.Add(DictgenCommand.CreateCommand());
        rootCommand.Subcommands.Add(ConvertCommand.CreateCommand());
        rootCommand.Subcommands.Add(OfficeCommand.CreateCommand());
        rootCommand.Subcommands.Add(PdfCommand.CreateCommand()); // 👈 new

        // System.CommandLine beta 5 config wrapper
        // var config = new CommandLineConfiguration(rootCommand);

        // return await config.InvokeAsync(args);
        
        // No CommandLineConfiguration wrapper needed anymore
        return await rootCommand.Parse(args).InvokeAsync();
    }
}