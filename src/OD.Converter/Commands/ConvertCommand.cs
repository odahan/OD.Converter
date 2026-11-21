using OD.Converter.Models;
using OD.Converter.Services;
using Spectre.Console;

namespace OD.Converter.Commands;

/// <summary>Handles the non-interactive audio conversion command.</summary>
public class ConvertCommand
{
    private readonly ConversionService _conversion;

    /// <summary>Initializes a new instance of the <see cref="ConvertCommand"/> class.</summary>
    /// <param name="ffmpegRunner">The FFmpeg runner service.</param>
    /// <param name="ffmpegLocator">The FFmpeg locator service.</param>
    /// <param name="fileSystem">The file system abstraction.</param>
    /// <param name="argumentBuilder">The argument builder service.</param>
    /// <param name="validator">The conversion options validator.</param>
    public ConvertCommand(IFfmpegRunner ffmpegRunner, IFfmpegLocator ffmpegLocator, IFileSystem fileSystem, IArgumentBuilder argumentBuilder, IConversionOptionsValidator validator)
    {
        _conversion = new ConversionService(ffmpegRunner, ffmpegLocator, fileSystem, argumentBuilder, validator);
    }

    /// <summary>Parses conversion arguments and runs FFmpeg.</summary>
    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        try
        {
            var parsed = ParseArguments(args);
            if (parsed.ShowHelp) { PrintHelp(); return 0; }
            var encoding = new ConversionOptions
            {
                TargetFormat = parsed.TargetFormat ?? "", Bitrate = parsed.Bitrate, Quality = parsed.Quality,
                SampleRate = parsed.SampleRate, Channels = parsed.Channels, Mono = parsed.Mono,
                BitDepth = parsed.BitDepth, CompressionLevel = parsed.CompressionLevel, Overwrite = parsed.Overwrite,
                FfmpegPath = parsed.FfmpegPath, DryRun = parsed.DryRun
            };
            var plan = _conversion.Plan(new ConversionRequest(parsed.Positional, encoding, parsed.OutputPath,
                parsed.OutputDir, parsed.Pattern, parsed.Recursive), ct, prepareCommands: parsed.DryRun);

            int exitCode = 0;
            foreach (var entry in plan.Entries)
            {
                var options = entry.Options;
                var output = options.OutputPath;
                if (options.DryRun && entry.Arguments is not null)
                {
                    AnsiConsole.MarkupLine("[grey]Dry run:[/]");
                    Console.WriteLine(FfmpegConverter.BuildCommandLine(plan.Executable, entry.Arguments));
                    continue;
                }

                var result = await _conversion.ExecuteAsync(plan.Executable, entry, ct, () =>
                    AnsiConsole.MarkupLine($"[grey]Converting:[/] {Markup.Escape(Path.GetFileName(options.InputPath))} → {Markup.Escape(Path.GetFileName(output))} [[{FormatRegistry.Get(options.TargetFormat)!.Key}]]"));
                if (result.Error is not null)
                    exitCode = ErrorHandler.Fail(result.Error, result.ExitCode);
                else
                    AnsiConsole.MarkupLine($"[green]OK[/] {Markup.Escape(output)} ({result.OutputBytes / 1024} KB)");
            }
            return exitCode;
        }
        catch (OperationCanceledException) { AnsiConsole.MarkupLine("[yellow]Conversion cancelled.[/]"); return 130; }
        catch (MediaToolException ex) { return ErrorHandler.Fail(ex.Message, 2); }
        catch (ArgumentException ex) { return ErrorHandler.Fail(ex.Message); }
        catch (Exception ex) { return ErrorHandler.Fail($"Unexpected error: {ex.Message}", 3); }
    }

    /// <summary>Prints help for the conversion command.</summary>
    public static void PrintHelp()
    {
        ConsoleTheme.Section(AnsiConsole.Console, "Audio conversion / command reference");
        AnsiConsole.WriteLine("Usage: odconv convert <file>... --to <format> [options]");
        AnsiConsole.WriteLine("       Supports multiple files and directories (use with --recursive and --pattern).");
        AnsiConsole.WriteLine("\nFormats: " + FormatRegistry.SupportedList + "\n\nOptions:");
        AnsiConsole.WriteLine("  -t, --to <format>     Target format (required)");
        AnsiConsole.WriteLine("  -o, --output <file>   Output file (only for single input)");
        AnsiConsole.WriteLine("      --output-dir <dir> Output directory (for multiple inputs, preserves structure)");
        AnsiConsole.WriteLine("      --recursive        Process subdirectories recursively");
        AnsiConsole.WriteLine("      --pattern <glob>   File pattern, e.g. \"*.wav\" (default: all files)");
        AnsiConsole.WriteLine("  -b, --bitrate <rate>  Bitrate, for example 320k or 192k");
        AnsiConsole.WriteLine("  -q, --quality <n>     VBR quality: MP3 0-9, Ogg 0-10");
        AnsiConsole.WriteLine("      --sample-rate <n> 22050, 44100, 48000, or 96000");
        AnsiConsole.WriteLine("      --channels <n>    1, 2, or 6");
        AnsiConsole.WriteLine("      --mono            Force mono output");
        AnsiConsole.WriteLine("      --bit-depth <n>   16, 24, or 32 for WAV/AIFF");
        AnsiConsole.WriteLine("      --compression <n> 0-8 for FLAC");
        AnsiConsole.WriteLine("      --overwrite <v>   never or always (default: never)");
        AnsiConsole.WriteLine("      --ffmpeg <path>   Path to ffmpeg.exe or its directory");
        AnsiConsole.WriteLine("      --dry-run         Print the command without running it");
    }

    private static ParsedArguments ParseArguments(string[] args)
    {
        var parsed = new ParsedArguments();
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--to" or "-t": parsed.TargetFormat = Next(args, ref index, "--to"); break;
                case "--output" or "-o": parsed.OutputPath = Next(args, ref index, "--output"); break;
                case "--bitrate" or "-b": parsed.Bitrate = Next(args, ref index, "--bitrate"); break;
                case "--quality" or "-q": parsed.Quality = NextInteger(args, ref index, "--quality"); break;
                case "--sample-rate": parsed.SampleRate = NextInteger(args, ref index, "--sample-rate"); break;
                case "--channels": parsed.Channels = NextInteger(args, ref index, "--channels"); break;
                case "--mono": parsed.Mono = true; break;
                case "--bit-depth": parsed.BitDepth = NextInteger(args, ref index, "--bit-depth"); break;
                case "--compression": parsed.CompressionLevel = NextInteger(args, ref index, "--compression"); break;
                case "--overwrite": parsed.Overwrite = Next(args, ref index, "--overwrite"); break;
                case "--ffmpeg": parsed.FfmpegPath = Next(args, ref index, "--ffmpeg"); break;
                case "--dry-run": parsed.DryRun = true; break;
                case "--recursive": parsed.Recursive = true; break;
                case "--pattern": parsed.Pattern = Next(args, ref index, "--pattern"); break;
                case "--output-dir": parsed.OutputDir = Next(args, ref index, "--output-dir"); break;
                case "--help" or "-h" or "-?": parsed.ShowHelp = true; break;
                case var value when value.StartsWith('-'): throw new ArgumentException($"Unknown option: \"{value}\". Use --help.");
                default: parsed.Positional.Add(args[index]); break;
            }
        }
        return parsed;
    }

    private static string Next(string[] args, ref int index, string option)
    {
        if (++index >= args.Length) throw new ArgumentException($"Missing value for {option}.");
        return args[index];
    }

    private static int NextInteger(string[] args, ref int index, string option)
    {
        var value = Next(args, ref index, option);
        if (!int.TryParse(value, out var result)) throw new ArgumentException($"Invalid value for {option}: \"{value}\". An integer is required.");
        return result;
    }

    private sealed class ParsedArguments
    {
        public List<string> Positional { get; } = [];
        public string? TargetFormat { get; set; }
        public string? OutputPath { get; set; }
        public string? Bitrate { get; set; }
        public int? Quality { get; set; }
        public int? SampleRate { get; set; }
        public int? Channels { get; set; }
        public bool Mono { get; set; }
        public int? BitDepth { get; set; }
        public int? CompressionLevel { get; set; }
        public string Overwrite { get; set; } = "never";
        public string? FfmpegPath { get; set; }
        public bool DryRun { get; set; }
        public bool ShowHelp { get; set; }
        public bool Recursive { get; set; }
        public string? Pattern { get; set; }
        public string? OutputDir { get; set; }
    }
}
