using OD.Converter.Services;
using Spectre.Console;

namespace OD.Converter.Commands;

/// <summary>Displays audio metadata obtained through FFprobe.</summary>
public static class ProbeCommand
{
    /// <summary>Runs the probe command.</summary>
    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        string? input = null;
        bool json = false;
        string? ffmpegOverride = null;

        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                switch (a)
                {
                    case "--json": json = true; break;
                    case "--ffmpeg": ffmpegOverride = Next(args, ref i); break;
                    case "--help" or "-h" or "-?": PrintHelp(); return 0;
                    default:
                        if (a.StartsWith("-")) return ErrorHandler.Fail($"Unknown option: \"{a}\".");
                        if (input is null) input = a;
                        else return ErrorHandler.Fail("Too many arguments.");
                        break;
                }
            }
        }
        catch (ArgumentException ex) { return ErrorHandler.Fail(ex.Message); }

        if (string.IsNullOrWhiteSpace(input))
        {
            ErrorHandler.Fail("Source file is required. Usage: odconv probe <file> [--json]");
            return 1;
        }
        input = input.Trim().Trim('"');
        if (!File.Exists(input))
        {
            ErrorHandler.Fail($"Source file not found: \"{input}\".");
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
                table.AddColumn("Field"); table.AddColumn("Value");
                table.AddRow("File", Markup.Escape(input));
                table.AddRow("Format", Markup.Escape(info.FormatName));
                table.AddRow("Codec", Markup.Escape(info.CodecName));
                table.AddRow("Duration", $"{TimeSpan.FromSeconds(info.DurationSeconds):g}");
                table.AddRow("Sample rate", info.SampleRate > 0 ? $"{info.SampleRate} Hz" : "-");
                table.AddRow("Channels", info.Channels.ToString());
                table.AddRow("Bits", info.BitsPerSample > 0 ? info.BitsPerSample.ToString() : "-");
                table.AddRow("Bitrate", info.BitRate > 0 ? $"{info.BitRate / 1000} kb/s" : "-");
                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(FfprobeService.FormatProbeHuman(info))}[/]");
            }
            return 0;
        }
        catch (Exception ex)
        {
            ErrorHandler.Fail($"Unable to read \"{input}\": the file may be corrupt or unsupported ({ex.Message.Split('\n').FirstOrDefault()?.Trim()})");
            return 1;
        }
    }

    private static string Next(string[] a, ref int i)
    {
        if (i + 1 >= a.Length) throw new ArgumentException("Missing value for --ffmpeg.");
        return a[++i];
    }

    /// <summary>Prints help for the probe command.</summary>
    public static void PrintHelp()
    {
        Console.WriteLine("Usage: odconv probe <file> [--json] [--ffmpeg <path>]");
        Console.WriteLine("Alias: odconv info <file>");
    }
}
