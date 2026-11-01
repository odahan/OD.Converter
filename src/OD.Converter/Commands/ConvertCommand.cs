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

    /// <summary>Initializes a new instance of the <see cref="ConvertCommand"/> class.</summary>
    /// <param name="ffmpegRunner">The FFmpeg runner service.</param>
    /// <param name="ffmpegLocator">The FFmpeg locator service.</param>
    /// <param name="fileSystem">The file system abstraction.</param>
    /// <param name="argumentBuilder">The argument builder service.</param>
    /// <param name="validator">The conversion options validator.</param>
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
                return ErrorHandler.Fail("Source file or directory is required. Usage: odconv convert <file>... --to <format> [options]");
            if (string.IsNullOrWhiteSpace(parsed.TargetFormat))
                return ErrorHandler.Fail("Target format is required. Use --to <format>, for example --to mp3.");

            // Disallow --output when multiple inputs or directory
            var hasMultiple = parsed.Positional.Count > 1 || parsed.Positional.Any(p => _fileSystem.DirectoryExists(p));
            if (!string.IsNullOrEmpty(parsed.OutputPath) && hasMultiple)
                return ErrorHandler.Fail("--output cannot be used with multiple input files or directories. Use --output-dir instead.");

            // Collect all input files
            var inputFiles = new List<string>();
            var pattern = parsed.Pattern ?? "*";
            foreach (var pos in parsed.Positional.Select(p => p.Trim().Trim('"')))
            {
                if (_fileSystem.DirectoryExists(pos))
                {
                    var searchOption = parsed.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    var files = Directory.GetFiles(pos, pattern, searchOption);
                    inputFiles.AddRange(files);
                }
                else
                {
                    if (!_fileSystem.FileExists(pos))
                        return ErrorHandler.Fail($"Source file not found: \"{pos}\".");
                    inputFiles.Add(pos);
                }
            }

            if (inputFiles.Count == 0)
                return ErrorHandler.Fail("No input files found.");

            var targetFormat = parsed.TargetFormat.Trim().TrimStart('.').ToLowerInvariant();
            var format = FormatRegistry.Get(targetFormat);
            if (format is null) return ErrorHandler.Fail($"Unsupported format: \"{targetFormat}\". Formats: {FormatRegistry.SupportedList}");

            // If only one file and output is specified, use it; else output-dir or default
            var isSingle = inputFiles.Count == 1;
            string? singleOutput = null;
            if (isSingle && !string.IsNullOrEmpty(parsed.OutputPath))
                singleOutput = parsed.OutputPath.Trim().Trim('"');

            // Resolve ffmpeg path once
            string ffmpeg;
            try { ffmpeg = _ffmpegLocator.Resolve(parsed.FfmpegPath); }
            catch (Exception ex) { return ErrorHandler.Fail(ex.Message, 2); }

            int exitCode = 0;
            var anyFailed = false;
            foreach (var input in inputFiles)
            {
                var output = singleOutput;
                if (string.IsNullOrEmpty(output))
                {
                    // Determine output path
                    if (!string.IsNullOrEmpty(parsed.OutputDir))
                    {
                        // Preserve relative structure
                        var baseDir = Path.GetDirectoryName(input) ?? ".";
                        var fileName = Path.GetFileName(input);
                        var relative = Path.GetRelativePath(Environment.CurrentDirectory, input);
                        // If input is inside current directory, use relative; else use filename only
                        var dir = Path.GetDirectoryName(relative);
                        if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(parsed.OutputDir))
                        {
                            var outDir = Path.Combine(parsed.OutputDir, dir);
                            output = Path.Combine(outDir, Path.GetFileNameWithoutExtension(input) + format.Extension);
                            // Ensure directory exists
                            if (!_fileSystem.DirectoryExists(outDir) && !parsed.DryRun)
                                _fileSystem.CreateDirectory(outDir);
                        }
                        else
                        {
                            output = Path.Combine(parsed.OutputDir, Path.GetFileNameWithoutExtension(input) + format.Extension);
                        }
                    }
                    else
                    {
                        // Default: same directory as input
                        var dir = Path.GetDirectoryName(input) ?? ".";
                        var name = Path.GetFileNameWithoutExtension(input);
                        output = parsed.Mono ? Path.Combine(dir, $"{name}-mono{format.Extension}") : Path.Combine(dir, $"{name}{format.Extension}");
                    }
                }
                if (string.IsNullOrWhiteSpace(Path.GetExtension(output))) output += format.Extension;

                var options = new ConversionOptions
                {
                    InputPath = input, OutputPath = output, TargetFormat = targetFormat, Bitrate = parsed.Bitrate,
                    Quality = parsed.Quality, SampleRate = parsed.SampleRate, Channels = parsed.Channels, Mono = parsed.Mono,
                    BitDepth = parsed.BitDepth, CompressionLevel = parsed.CompressionLevel, Overwrite = parsed.Overwrite,
                    FfmpegPath = parsed.FfmpegPath, DryRun = parsed.DryRun,
                };

                try
                {
                    _validator.Validate(options);
                    _validator.EnsureDifferentPaths(input, output);
                    if (_fileSystem.FileExists(output) && options.Overwrite == "never" && !options.DryRun)
                    {
                        ErrorHandler.Fail($"Output file already exists: \"{output}\". Use --overwrite always or choose another name.");
                        anyFailed = true;
                        exitCode = 1;
                        continue;
                    }

                    if (!options.DryRun)
                    {
                        var outputDirectory = _fileSystem.GetDirectoryName(_fileSystem.GetFullPath(output));
                        if (!string.IsNullOrEmpty(outputDirectory) && !_fileSystem.DirectoryExists(outputDirectory))
                            _fileSystem.CreateDirectory(outputDirectory);
                    }

                    var ffmpegArguments = _argumentBuilder.BuildArguments(options);
                    if (options.DryRun)
                    {
                        AnsiConsole.MarkupLine("[grey]Dry run:[/]");
                        Console.WriteLine(FfmpegConverter.BuildCommandLine(ffmpeg, ffmpegArguments));
                        continue;
                    }

                    AnsiConsole.MarkupLine($"[grey]Converting:[/] {Markup.Escape(Path.GetFileName(input))} → {Markup.Escape(Path.GetFileName(output))} [[{format.Key}]]");
                    var (procExit, standardError) = await _ffmpegRunner.RunAsync(ffmpeg, ffmpegArguments, ct);
                    if (procExit == 0)
                    {
                        if (!_fileSystem.FileExists(output))
                        {
                            ErrorHandler.Fail($"Conversion completed but output file was not found: \"{output}\".", 3);
                            anyFailed = true;
                            exitCode = 3;
                            continue;
                        }
                        var fileInfo = _fileSystem.GetFileInfo(output);
                        AnsiConsole.MarkupLine($"[green]OK[/] {Markup.Escape(output)} ({fileInfo.Length / 1024} KB)");
                    }
                    else
                    {
                        var detail = standardError.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? $"FFmpeg exited with code {procExit}";
                        ErrorHandler.Fail($"Conversion failed ({detail[..Math.Min(detail.Length, 300)]}). Check the source file or try another format.", 3);
                        anyFailed = true;
                        exitCode = 3;
                    }
                }
                catch (OperationCanceledException)
                {
                    AnsiConsole.MarkupLine("[yellow]Conversion cancelled.[/]");
                    return 130;
                }
                catch (ArgumentException ex)
                {
                    ErrorHandler.Fail(ex.Message);
                    anyFailed = true;
                    exitCode = 1;
                }
                catch (Exception ex)
                {
                    ErrorHandler.Fail($"Unexpected error: {ex.Message}", 3);
                    anyFailed = true;
                    exitCode = 3;
                }
            }

            return anyFailed ? exitCode : 0;
        }
        catch (OperationCanceledException) { AnsiConsole.MarkupLine("[yellow]Conversion cancelled.[/]"); return 130; }
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
