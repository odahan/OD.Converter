using OD.Converter.Services;
using Spectre.Console;

namespace OD.Converter.Commands;

/// <summary>Lists available output formats.</summary>
public static class FormatsCommand
{
    /// <summary>Runs the formats command.</summary>
    public static Task<int> RunAsync(string[] args, CancellationToken ct)
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
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Format"); table.AddColumn("Ext"); table.AddColumn("Codec"); table.AddColumn("Type"); table.AddColumn("Description"); table.AddColumn("Default");
        foreach (var f in FormatRegistry.All)
        {
            table.AddRow(
                Markup.Escape(f.Key),
                Markup.Escape(f.Extension),
                Markup.Escape(f.Codec),
                f.IsLossless ? "lossless" : "lossy",
                Markup.Escape(f.Description),
                Markup.Escape(string.IsNullOrEmpty(f.DefaultBitrate) ? "-" : f.DefaultBitrate));
        }
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("[grey]Use: odconv convert input.wav --to mp3 --bitrate 320k --mono[/]");
        return Task.FromResult(0);
    }
}
