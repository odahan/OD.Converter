using OD.Converter.Services;
using Spectre.Console;

namespace OD.Converter.Commands;

public static class ProbeCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        string? input = null;
        bool json = false;
        string? ffmpegOverride = null;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--json": json = true; break;
                case "--ffmpeg": ffmpegOverride = Next(args, ref i); break;
                case "--help": case "-h": PrintHelp(); return 0;
                default:
                    if (a.StartsWith("-")) { ErrorHandler.Fail($"option inconnue : \"{a}\""); return 1; }
                    if (input is null) input = a;
                    else { ErrorHandler.Fail("trop d'arguments"); return 1; }
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            ErrorHandler.Fail("fichier manquant. Usage : odconv probe <fichier> [--json]");
            return 1;
        }
        input = input.Trim().Trim('"');
        if (!File.Exists(input))
        {
            ErrorHandler.Fail($"fichier introuvable : \"{input}\"");
            return 1;
        }

        string ffprobe;
        try { ffprobe = FfmpegLocator.ResolveFfprobe(ffmpegOverride); }
        catch (Exception ex) { ErrorHandler.Fail(ex.Message); return 2; }

        try
        {
            var info = await FfprobeService.ProbeAsync(input, ffprobe, ct);
            if (json) Console.WriteLine(info.RawJson);
            else
            {
                var table = new Table().Border(TableBorder.Rounded);
                table.AddColumn("Champ"); table.AddColumn("Valeur");
                table.AddRow("Fichier", Markup.Escape(input));
                table.AddRow("Format", Markup.Escape(info.FormatName));
                table.AddRow("Codec", Markup.Escape(info.CodecName));
                table.AddRow("Durée", $"{TimeSpan.FromSeconds(info.DurationSeconds):g}");
                table.AddRow("Sample rate", info.SampleRate > 0 ? $"{info.SampleRate} Hz" : "-");
                table.AddRow("Canaux", info.Channels.ToString());
                table.AddRow("Bits", info.BitsPerSample > 0 ? info.BitsPerSample.ToString() : "-");
                table.AddRow("Bitrate", info.BitRate > 0 ? $"{info.BitRate / 1000} kb/s" : "-");
                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(FfprobeService.FormatProbeHuman(info))}[/]");
            }
            return 0;
        }
        catch (Exception ex)
        {
            ErrorHandler.Fail($"impossible de lire \"{input}\" : fichier corrompu ou format non reconnu ({ex.Message.Split('\n').FirstOrDefault()?.Trim()})");
            return 1;
        }
    }

    private static string Next(string[] a, ref int i)
    {
        if (i + 1 >= a.Length) throw new ArgumentException("valeur manquante pour --ffmpeg");
        return a[++i];
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Usage: odconv probe <fichier> [--json] [--ffmpeg <path>]");
        Console.WriteLine("Alias: odconv info <fichier>");
    }
}
