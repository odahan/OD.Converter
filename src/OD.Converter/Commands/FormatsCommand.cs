using OD.Converter.Services;
using Spectre.Console;

namespace OD.Converter.Commands;

/// <summary>Lists available output formats.</summary>
public class FormatsCommand
{
    /// <summary>Displays a table of all supported formats.</summary>
    public Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Any(a => a is "--help" or "-h" or "-?"))
        {
            Console.WriteLine("Usage: odconv formats");
            return Task.FromResult(0);
        }
        if (args.Length > 0)
        {
            ErrorHandler.Fail($"Unknown argument: \"{args[0]}\". Usage: odconv formats");
            return Task.FromResult(1);
        }
        ConsoleTheme.Section(AnsiConsole.Console, "Supported audio formats");
        var table = ConsoleTheme.CreateTable("Format", "Ext", "Codec", "Type", "Description", "Default");

        foreach (var f in FormatRegistry.All)
        {
            table.AddRow(
                Markup.Escape(f.Key),
                Markup.Escape(f.Extension),
                Markup.Escape(f.Codec),
                f.IsLossless ? "[green]lossless[/]" : "[yellow]lossy[/]",
                Markup.Escape(f.Description),
                Markup.Escape(string.IsNullOrEmpty(f.DefaultBitrate) ? "-" : f.DefaultBitrate));
        }
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("[grey]Use: odconv convert input.wav --to mp3 --bitrate 320k --mono[/]");
        return Task.FromResult(0);
    }
}
