using OD.Converter.Services;
using Spectre.Console;

namespace OD.Converter.Commands;

/// <summary>Displays audio metadata obtained through FFprobe.</summary>
public class ProbeCommand
{
    private readonly IFfmpegLocator _ffmpegLocator;
    private readonly IFileSystem _fileSystem;
    private readonly IFfprobeService _ffprobeService;

    /// <summary>Initializes a new instance of the <see cref="ProbeCommand"/> class.</summary>
    /// <param name="ffmpegLocator">The FFmpeg locator service.</param>
    /// <param name="fileSystem">The file system abstraction.</param>
    /// <param name="ffprobeService">The FFprobe service.</param>
    public ProbeCommand(IFfmpegLocator ffmpegLocator, IFileSystem fileSystem, IFfprobeService ffprobeService)
    {
        _ffmpegLocator = ffmpegLocator;
        _fileSystem = fileSystem;
        _ffprobeService = ffprobeService;
    }

    /// <summary>Runs the probe command.</summary>
    public async Task<int> RunAsync(string[] args, CancellationToken ct)
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
        if (!_fileSystem.FileExists(input))
        {
            ErrorHandler.Fail($"Source file not found: \"{input}\".");
            return 1;
        }

        string ffprobe;
        try { ffprobe = _ffmpegLocator.ResolveFfprobe(ffmpegOverride); }
        catch (Exception ex) { ErrorHandler.Fail(ex.Message); return 2; }

        try
        {
            var info = await _ffprobeService.ProbeAsync(input, ffprobe, ct);
            if (json) Console.WriteLine(info.RawJson);
            else
            {
                ConsoleTheme.Section(AnsiConsole.Console, "Source information");
                var table = ConsoleTheme.CreateTable("Field", "Value");

                table.AddRow("File", Markup.Escape(input));
                table.AddRow("Format", Markup.Escape(info.FormatName));
                table.AddRow("Codec", Markup.Escape(info.CodecName));
                table.AddRow("Duration", info.Source is not null && info.Source.Timeline.TargetDurationSeconds is null
                    ? "unknown" : $"{info.DurationSeconds:0.######} s");
                table.AddRow("Sample rate", info.SampleRate > 0 ? $"{info.SampleRate} Hz" : "-");
                table.AddRow("Channels", info.Channels.ToString());
                table.AddRow("Bits", info.BitsPerSample > 0 ? info.BitsPerSample.ToString() : "-");
                table.AddRow("Bitrate", info.BitRate > 0 ? $"{info.BitRate / 1000} kb/s" : "-");
                AnsiConsole.Write(table);
                if (info.Source is { } source)
                {
                    var tracks = ConsoleTheme.CreateTable();
                    foreach (var column in new[] { "Stream index", "Audio ordinal", "Codec", "Language", "Title" })
                        tracks.AddColumn(new TableColumn($"[bold cyan1]{column}[/]"));
                    foreach (var track in source.AudioTracks)
                        tracks.AddRow(track.StreamIndex.ToString(), track.AudioIndex.ToString(), Markup.Escape(track.CodecName),
                            Markup.Escape(track.Language ?? "-"), Markup.Escape(track.Title ?? "-"));
                    AnsiConsole.Write(tracks);
                    foreach (var track in source.AudioTracks)
                    {
                        AnsiConsole.WriteLine($"Stream {track.StreamIndex}: {track.SampleRate} Hz, {track.Channels} channels ({track.ChannelLayout ?? "unknown layout"}), {track.BitsPerSample} bits, {track.BitRate} bit/s");
                        AnsiConsole.WriteLine($"  Start: {Seconds(track.StartSeconds)} s; duration: {Seconds(track.DurationSeconds)} s; presentation offset: {Seconds(track.GetOffsetSeconds(source.Timeline))} s");
                    }
                    AnsiConsole.WriteLine($"Presentation origin: {Seconds(source.Timeline.OriginSeconds)} s ({source.Timeline.OriginReliability})");
                    AnsiConsole.WriteLine($"Target duration: {Seconds(source.Timeline.TargetDurationSeconds)} s ({source.Timeline.DurationReliability}); reported metadata does not validate alignment.");
                    if (source.AudioTracks.Count == 0) AnsiConsole.WriteLine("No audio tracks. This source cannot be processed as audio.");
                    foreach (var warning in source.Timeline.Warnings) AnsiConsole.WriteLine($"Warning: {warning}");
                }
                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(_ffprobeService.FormatProbeHuman(info))}[/]");
            }
            return 0;
        }
        catch (OperationCanceledException) { return 130; }
        catch (Exception ex)
        {
            ErrorHandler.Fail($"Unable to read \"{input}\": the file may be corrupt or unsupported ({ex.Message.Split('\n').FirstOrDefault()?.Trim()})");
            return 1;
        }
    }

    private static string Seconds(double? value) => value?.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";

    private static string Next(string[] a, ref int i)
    {
        if (i + 1 >= a.Length) throw new ArgumentException("Missing value for --ffmpeg.");
        return a[++i];
    }

    /// <summary>Prints help for the probe command.</summary>
    public static void PrintHelp()
    {
        ConsoleTheme.Section(AnsiConsole.Console, "Source inspection / command reference");
        Console.WriteLine("Usage: odconv probe <file> [--json] [--ffmpeg <path>]");
        Console.WriteLine("Alias: odconv info <file>");
    }
}
