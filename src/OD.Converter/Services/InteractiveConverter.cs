using OD.Converter.Models;
using Spectre.Console;

namespace OD.Converter.Services;

/// <summary>Runs the guided interactive conversion workflow.</summary>
public class InteractiveConverter
{
    private readonly IFfmpegLocator _ffmpegLocator;
    private readonly IFfmpegRunner _ffmpegRunner;
    private readonly IFfprobeService _ffprobeService;
    private readonly IFileSystem _fileSystem;
    private readonly IArgumentBuilder _argumentBuilder;
    private readonly IConversionOptionsValidator _validator;

    /// <summary>Initializes a new instance of the <see cref="InteractiveConverter"/> class.</summary>
    public InteractiveConverter(IFfmpegLocator ffmpegLocator, IFfmpegRunner ffmpegRunner, IFfprobeService ffprobeService, IFileSystem fileSystem, IArgumentBuilder argumentBuilder, IConversionOptionsValidator validator)
    {
        _ffmpegLocator = ffmpegLocator;
        _ffmpegRunner = ffmpegRunner;
        _ffprobeService = ffprobeService;
        _fileSystem = fileSystem;
        _argumentBuilder = argumentBuilder;
        _validator = validator;
    }

    /// <summary>Runs the interactive conversion loop until the user exits.</summary>
    public async Task<int> RunAsync(string? initialInput, string? ffmpegOverride, CancellationToken ct)
    {
        while (true)
        {
            Console.Clear();
            AnsiConsole.Write(new FigletText("odconv").Color(Color.Cyan1));
            AnsiConsole.MarkupLine($"[grey]Audio conversion — version {ApplicationInfo.Version}. Leave blank and press Enter to exit.[/]");

            // Prompt for source file if not provided as an argument
            string inputPath = initialInput ?? "";
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                inputPath = AnsiConsole.Prompt(
                    new TextPrompt<string>("Source file [grey](drag and drop or path)[/]:")
                        .AllowEmpty()
                        .Validate(p =>
                        {
                            if (string.IsNullOrWhiteSpace(p)) return ValidationResult.Success();
                            var t = p.Trim().Trim('"');
                            return _fileSystem.FileExists(t) ? ValidationResult.Success() : ValidationResult.Error("File not found");
                        }));
                inputPath = inputPath.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(inputPath))
                    return 0;
            }
            else
            {
                inputPath = inputPath.Trim().Trim('"');
            }

            // Validate existence
            if (!_fileSystem.FileExists(inputPath))
            {
                ErrorHandler.FailInteractive($"Source file not found: \"{inputPath}\".");
                initialInput = null;
                continue;
            }

            // Attempt to probe the source file for metadata
            ProbeInfo? probe = null;
            try
            {
                var ffprobe = _ffmpegLocator.ResolveFfprobe(ffmpegOverride);
                probe = await _ffprobeService.TryProbeAsync(inputPath, ffprobe, ct);
            }
            catch { }

            if (probe is not null)
                AnsiConsole.MarkupLine($"[grey]Source: {Markup.Escape(_ffprobeService.FormatProbeHuman(probe))} — {Markup.Escape(_fileSystem.GetFileName(inputPath))}[/]");
            else
                AnsiConsole.MarkupLine($"[grey]Source: {Markup.Escape(_fileSystem.GetFileName(inputPath))}[/]");

            // Select target format
            var format = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Target format:")
                    .AddChoices(FormatRegistry.All.Select(f => f.Key).ToArray()));

            var fmtInfo = FormatRegistry.Get(format)!;

            string? bitrate = null;
            int? quality = null;
            int? compression = null;
            int? bitDepth = null;

            // Lossy formats: ask for bitrate or quality
            if (!fmtInfo.IsLossless)
            {
                if (format == "mp3")
                {
                    var brChoice = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("MP3 bitrate:")
                            .AddChoices(new[] { "320k (recommended)", "256k", "192k", "128k", "VBR q0 (highest quality)", "VBR q2" }));
                    if (brChoice.Contains("VBR q0")) quality = 0;
                    else if (brChoice.Contains("VBR q2")) quality = 2;
                    else bitrate = brChoice.Split(' ')[0];
                }
                else if (format == "ogg")
                {
                    var qChoice = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("Ogg Vorbis quality (0-10):")
                            .AddChoices(new[] { "6 (~192k, recommended)", "8 (~256k)", "4 (~128k)", "10 (maximum)", "2 (low)" }));
                    var qStr = qChoice.Split(' ')[0];
                    quality = int.Parse(qStr);
                }
                else if (format == "opus")
                {
                    bitrate = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("Bitrate Opus :")
                            .AddChoices(new[] { "128k (recommended)", "96k", "64k", "192k", "256k" })
                    ).Split(' ')[0];
                }
                else
                {
                    bitrate = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title($"{format} bitrate:")
                            .AddChoices(new[] { "192k (recommended)", "128k", "256k", "320k" })
                    ).Split(' ')[0];
                }
            }
            else
            {
                // Lossless formats: additional parameters
                if (format == "flac")
                {
                    compression = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("FLAC compression (0-8):")
                            .AddChoices(new[] { "5 (recommended)", "8 (maximum)", "0 (fast)", "3" })
                    ).Split(' ')[0] is var s && int.TryParse(s, out var lv) ? lv : 5;
                }
                if (format == "wav" || format == "aiff")
                {
                    var bd = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                        .Title("Bit depth:")
                        .AddChoices(new[] { "16-bit (recommended)", "24-bit", "32-bit" }));
                    bitDepth = bd.StartsWith("24") ? 24 : bd.StartsWith("32") ? 32 : 16;
                }
            }

            var mono = AnsiConsole.Confirm("Mono (downmix to one channel)?", defaultValue: probe?.Channels == 1);

            int? sampleRate = null;
            var keepSr = AnsiConsole.Confirm("Keep the source sample rate?", true);
            if (!keepSr)
            {
                var srChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Sample rate:")
                        .AddChoices(new[] { "44100", "48000", "22050", "96000" }));
                sampleRate = int.Parse(srChoice);
            }

            // Suggest a default output filename
            var defaultOut = mono
                ? FormatRegistry.DefaultOutputPathMono(inputPath, format)
                : FormatRegistry.DefaultOutputPath(inputPath, format);

            var outputPath = AnsiConsole.Prompt(
                new TextPrompt<string>($"Output file [[default {Path.GetFileName(defaultOut)}]]:")
                    .AllowEmpty()
                    .DefaultValue(defaultOut));

            outputPath = string.IsNullOrWhiteSpace(outputPath) ? defaultOut : outputPath.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(Path.GetExtension(outputPath)))
                outputPath += fmtInfo.Extension;

            // Check for existing file
            if (_fileSystem.FileExists(outputPath))
            {
                var ow = AnsiConsole.Confirm($"The file already exists: \"{Path.GetFileName(outputPath)}\". Overwrite?", false);
                if (!ow)
                {
                    initialInput = inputPath;
                    continue;
                }
            }

            // Show summary table
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Option"); table.AddColumn("Value");
            table.AddRow("Source", Markup.Escape(inputPath));
            table.AddRow("Target", $"{format} → {Markup.Escape(outputPath)}");
            if (bitrate != null) table.AddRow("Bitrate", bitrate);
            if (quality != null) table.AddRow("Quality", quality.Value.ToString());
            if (bitDepth != null) table.AddRow("Bit depth", $"{bitDepth} bit");
            if (compression != null) table.AddRow("Compression", compression.Value.ToString());
            table.AddRow("Mono", mono ? "yes" : "no");
            if (sampleRate != null) table.AddRow("Sample rate", sampleRate.Value.ToString());
            AnsiConsole.Write(table);

            // Ensure input and output are not the same file
            try { _validator.EnsureDifferentPaths(inputPath, outputPath); }
            catch (ArgumentException ex) { ErrorHandler.FailInteractive(ex.Message); initialInput = inputPath; continue; }

            // Confirm conversion
            if (!AnsiConsole.Confirm("Start conversion?", true))
            {
                initialInput = inputPath;
                continue;
            }

            // Create output directory if it does not exist
            try
            {
                var fullPath = _fileSystem.GetFullPath(outputPath);
                var outputDirectory = _fileSystem.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(outputDirectory) && !_fileSystem.DirectoryExists(outputDirectory))
                    _fileSystem.CreateDirectory(outputDirectory);
            }
            catch (Exception ex)
            {
                ErrorHandler.FailInteractive($"Unable to create the output directory: {ex.Message}");
                initialInput = inputPath;
                continue;
            }

            // Locate FFmpeg
            string ffmpeg;
            try { ffmpeg = _ffmpegLocator.Resolve(ffmpegOverride); }
            catch (Exception ex) { ErrorHandler.FailInteractive(ex.Message); return 2; }

            // Build conversion options
            var opts = new ConversionOptions
            {
                InputPath = inputPath,
                OutputPath = outputPath,
                TargetFormat = format,
                Bitrate = bitrate,
                Quality = quality,
                SampleRate = sampleRate,
                Mono = mono,
                BitDepth = bitDepth,
                CompressionLevel = compression,
                Overwrite = "always",
                FfmpegPath = ffmpegOverride,
            };

            List<string> args;
            try { args = _argumentBuilder.BuildArguments(opts); } // uses injected validator internally
            catch (Exception ex) { ErrorHandler.FailInteractive(ex.Message); continue; }

            // Run conversion
            AnsiConsole.MarkupLine("[grey]Conversion in progress...[/]");
            (int code, string stderr) conversionResult;
            try { conversionResult = await _ffmpegRunner.RunAsync(ffmpeg, args, ct); }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]Conversion cancelled.[/]");
                return 130;
            }
            var (code, stderr) = conversionResult;
            if (code == 0)
            {
                var fi = _fileSystem.GetFileInfo(outputPath);
                AnsiConsole.MarkupLine($"[green]OK[/] {Markup.Escape(outputPath)} ({fi.Length / 1024} KB)");
            }
            else
            {
                // Extract last meaningful error line from FFmpeg stderr
                var msg = string.IsNullOrWhiteSpace(stderr) ? $"ffmpeg code {code}" : stderr.Split('\n').Last(s => !string.IsNullOrWhiteSpace(s)).Trim();
                if (msg.Length > 300) msg = msg[..300];
                ErrorHandler.FailInteractive($"Conversion failed: {msg}");
                initialInput = inputPath;
                continue;
            }

            // Ask to convert another file
            if (!AnsiConsole.Confirm("Convert another file?", false))
                return 0;

            initialInput = null;
        }
    }
}
