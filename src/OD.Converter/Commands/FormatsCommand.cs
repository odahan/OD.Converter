using OD.Converter.Services;
using Spectre.Console;

namespace OD.Converter.Commands;

public static class FormatsCommand
{
    public static Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Any(a => a is "--help" or "-h"))
        {
            Console.WriteLine("Usage: odconv formats");
            return Task.FromResult(0);
        }
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Format"); table.AddColumn("Ext"); table.AddColumn("Codec"); table.AddColumn("Type"); table.AddColumn("Description"); table.AddColumn("Défaut");
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
        AnsiConsole.MarkupLine("[grey]Utilisez : odconv convert input.wav --to mp3 --bitrate 320k --mono[/]");
        return Task.FromResult(0);
    }
}
