using OD.Converter.Models;
using OD.Converter.Services;
using Spectre.Console;

namespace OD.Converter.Commands;

/// <summary>Handles the non-interactive audio conversion command.</summary>
public class ConvertCommand
{
    private readonly IFfmpegRunner _ffmpegRunner;
    private readonly IFfmpegLocator _ffmpegLocator;
    private readonly IFileSystem _fileSystem;
    private readonly IArgumentBuilder _argumentBuilder;
    private readonly IConversionOptionsValidator _validator;

    public ConvertCommand(IFfmpegRunner ffmpegRunner, IFfmpegLocator ffmpegLocator, IFileSystem fileSystem, IArgumentBuilder argumentBuilder, IConversionOptionsValidator validator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffmpegLocator = ffmpegLocator;
        _fileSystem = fileSystem;
        _argumentBuilder = argumentBuilder;
        _validator = validator;
    }

    /// <summary>Parses conversion arguments and runs FFmpeg.</summary>
    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        try
        {
            var parsed = ParseArguments(args);
            if (parsed.ShowHelp) { PrintHelp(); return 0; }
            if (parsed.Positional.Count == 0)
                return ErrorHandler.Fail("Source file is required. Usage: odconv convert <file> --to <format> [-o output]");
            if (parsed.Positional.Count > 1)
                return ErrorHandler.Fail($"Too many positional arguments: \"{string.Join(" ", parsed.Positional.Skip(1))}\".");
            if (string.IsNullOrWhiteSpace(parsed.TargetFormat))
                return ErrorHandler.Fail("Target format is required. Use --to <format>, for example --to mp3.");

            var input = parsed.Positional[0].Trim().Trim('"');
            var targetFormat = parsed.TargetFormat.Trim().TrimStart('.').ToLowerInvariant();
            var format = FormatRegistry.Get(targetFormat);
                if (!_fileSystem.FileExists(input)) return ErrorHandler.Fail($"Source file not found: \"{input}\".");
            if (format is null) return ErrorHandler.Fail($"Unsupported format: \"{targetFormat}\". Formats: {FormatRegistry.SupportedList}");

            var output = string.IsNullOrWhiteSpace(parsed.OutputPath)
                ? parsed.Mono ? FormatRegistry.DefaultOutputPathMono(input, targetFormat) : FormatRegistry.DefaultOutputPath(input, targetFormat)
                : parsed.OutputPath.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(Path.GetExtension(output))) output += format.Extension;

            var options = new ConversionOptions
            {
                InputPath = input, OutputPath = output, TargetFormat = targetFormat, Bitrate = parsed.Bitrate,
                Quality = parsed.Quality, SampleRate = parsed.SampleRate, Channels = parsed.Channels, Mono = parsed.Mono,
                BitDepth = parsed.BitDepth, CompressionLevel = parsed.CompressionLevel, Overwrite = parsed.Overwrite,
                FfmpegPath = parsed.FfmpegPath, DryRun = parsed.DryRun,
            };

            _validator.Validate(options);
            _validator.EnsureDifferentPaths(input, output);
            if (_fileSystem.FileExists(output) && options.Overwrite == "never" && !options.DryRun)
                return ErrorHandler.Fail($"Output file already exists: \"{output}\". Use --overwrite always or choose another name.");

            if (!options.DryRun)
            {
                var outputDirectory = _fileSystem.GetDirectoryName(_fileSystem.GetFullPath(output));
                if (!string.IsNullOrEmpty(outputDirectory) && !_fileSystem.DirectoryExists(outputDirectory)) _fileSystem.CreateDirectory(outputDirectory);
            }

            string ffmpeg;
            try { ffmpeg = _ffmpegLocator.Resolve(parsed.FfmpegPath); }
            catch (Exception ex) { return ErrorHandler.Fail(ex.Message, 2); }

            var ffmpegArguments = _argumentBuilder.BuildArguments(options);
            if (options.DryRun)
            {
                AnsiConsole.MarkupLine("[grey]Dry run:[/]");
                Console.WriteLine(FfmpegConverter.BuildCommandLine(ffmpeg, ffmpegArguments));
                return 0;
            }

            AnsiConsole.MarkupLine($"[grey]Converting:[/] {Markup.Escape(Path.GetFileName(input))} → {Markup.Escape(Path.GetFileName(output))} [[{format.Key}]]");
            var (exitCode, standardError) = await _ffmpegRunner.RunAsync(ffmpeg, ffmpegArguments, ct);
            if (exitCode == 0)
            {
                if (!_fileSystem.FileExists(output)) return ErrorHandler.Fail($"Conversion completed but output file was not found: \"{output}\".", 3);
                var fileInfo = _fileSystem.GetFileInfo(output);
                AnsiConsole.MarkupLine($"[green]OK[/] {Markup.Escape(output)} ({fileInfo.Length / 1024} KB)");
                return 0;
            }

            var detail = standardError.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? $"FFmpeg exited with code {exitCode}";
            return ErrorHandler.Fail($"Conversion failed ({detail[..Math.Min(detail.Length, 300)]}). Check the source file or try another format.", 3);
        }
        catch (OperationCanceledException) { AnsiConsole.MarkupLine("[yellow]Conversion cancelled.[/]"); return 130; }
        catch (ArgumentException ex) { return ErrorHandler.Fail(ex.Message); }
        catch (Exception ex) { return ErrorHandler.Fail($"Unexpected error: {ex.Message}", 3); }
    }

    /// <summary>Prints help for the conversion command.</summary>
    public static void PrintHelp()
    {
        AnsiConsole.WriteLine("Usage: odconv convert <file> --to <format> [options]");
        AnsiConsole.WriteLine("\nFormats: " + FormatRegistry.SupportedList + "\n\nOptions:");
        AnsiConsole.WriteLine("  -t, --to <format>     Target format (required)");
        AnsiConsole.WriteLine("  -o, --output <file>   Output file (default: source name + extension)");
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
    }
}
