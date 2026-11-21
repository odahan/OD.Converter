using OD.Converter.Models;
using OD.Converter.Services;
using OD.Converter.Services.Modules;
using Spectre.Console;

namespace OD.Converter.Commands;

/// <summary>Resolves shared audio/video workflows and executes extraction, separation or transcription.</summary>
public sealed class ProcessCommand(IFfmpegLocator locator, IFileSystem fileSystem, IFfprobeService probe,
    IAudioExtractionService? extraction = null, IAudioSeparationService? separation = null,
    IAudioTranscriptionService? transcription = null)
{
    /// <summary>Validates a request and its track, then executes the available workflow.</summary>
    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Any(arg => arg is "--help" or "-h" or "-?")) { PrintHelp(); return 0; }
        try
        {
            var request = ParseArguments(args);
            var diagnostics = new ConsoleMediaProgress();
            var converter = new FfmpegConverter(new ConversionOptionsValidator(fileSystem));
            using var client = new HttpClient();
            var manager = new ModuleManager(new ModulePaths(), new ModuleDownloader(client), BuiltInModuleRecipes.Create(), diagnostics);
            var runner = new ModuleProcessRunner(Console.Write);
            var service = new ProcessingService(locator, fileSystem, probe, manager,
                extraction ?? new AudioExtractionService(converter, converter, diagnostics),
                separation ?? new AudioSeparationService(manager, runner, converter, converter, diagnostics),
                transcription ?? new AudioTranscriptionService(manager, runner, converter, converter, diagnostics));

            var info = await service.AnalyzeAsync(request.InputPath, request.AudioOptions.FfmpegPath, ct);
            var plan = ProcessingService.Plan(request, info);
            var engineStarted = false;
            var result = await service.ExecuteAsync(plan, ct, selected =>
            {
                ConsoleTheme.Section(AnsiConsole.Console, "Audio / video processing");
                AnsiConsole.MarkupLine($"[cyan1]Selected audio stream {selected.Track.StreamIndex}[/] [grey](audio ordinal {selected.Track.AudioIndex}).[/]");
                foreach (var warning in selected.Source.Timeline.Warnings) ConsoleTheme.Warning(AnsiConsole.Console, warning);
            }, checkModules: request.Action == ProcessingAction.Separate ? separation is null : transcription is null,
                executing: () => engineStarted = true);

            foreach (var export in result.Outputs) ConsoleTheme.Success(AnsiConsole.Console, $"Created: {export.Path}");
            foreach (var warning in result.Warnings) ConsoleTheme.Warning(AnsiConsole.Console, warning);
            if (ProcessingService.EncoderDelayWarning(request, result.Outputs) is { } encoderWarning)
                ConsoleTheme.Warning(AnsiConsole.Console, encoderWarning);
            if (result.ExitCode != 0 && engineStarted && request.Action != ProcessingAction.Extract)
                foreach (var missing in request.Exports.Except(result.Outputs.Select(output => output.Kind)))
                    ConsoleTheme.Warning(AnsiConsole.Console, $"Not created: {ProcessingConventions.OutputPath(request, missing)}");
            return result.ErrorMessage is { } error ? ErrorHandler.Fail(error, result.ExitCode) : result.ExitCode;
        }
        catch (OperationCanceledException) { return 130; }
        catch (MediaToolException ex) { return ErrorHandler.Fail(ex.Message, 2); }
        catch (ArgumentException ex) { return ErrorHandler.Fail(ex.Message); }
        catch (Exception ex) { return ErrorHandler.Fail($"Source processing failed: {ex.Message}", 3); }
    }
    /// <summary>Parses the fixed workflow syntax without starting processes or accessing the network.</summary>
    public static ProcessingRequest ParseArguments(string[] args)
    {
        string? input = null, action = null, directory = null, prefix = null, exports = null, ffmpeg = null;
        string language = "auto", format = "wav", overwrite = "never";
        int? track = null, quality = null, sampleRate = null, channels = null, depth = null, compression = null;
        string? bitrate = null;
        bool mono = false;
        for (int i = 0; i < args.Length; i++)
        {
            var option = args[i];
            string Next()
            {
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i])) throw new ArgumentException($"Missing value for {option}.");
                return args[i];
            }
            int NextInt() => int.TryParse(Next(), out var value) ? value : throw new ArgumentException($"Invalid integer for {option}.");
            switch (option)
            {
                case "--action": action = Next(); break;
                case "--audio-track": track = NextInt(); break;
                case "--output-dir": directory = Next(); break;
                case "--prefix": prefix = Next(); break;
                case "--exports": exports = Next(); break;
                case "--language": language = Next(); break;
                case "--to" or "-t": format = Next(); break;
                case "--overwrite": overwrite = Next(); break;
                case "--ffmpeg": ffmpeg = Next(); break;
                case "--bitrate" or "-b": bitrate = Next(); break;
                case "--quality" or "-q": quality = NextInt(); break;
                case "--sample-rate": sampleRate = NextInt(); break;
                case "--channels": channels = NextInt(); break;
                case "--bit-depth": depth = NextInt(); break;
                case "--compression": compression = NextInt(); break;
                case "--mono": mono = true; break;
                default:
                    if (option.StartsWith('-')) throw new ArgumentException($"Unknown option: {option}. Use process --help.");
                    if (input is not null) throw new ArgumentException("process accepts exactly one source file.");
                    input = option;
                    break;
            }
        }
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("A source file is required.");
        var workflow = action switch
        {
            "extract" => ProcessingAction.Extract,
            "separate" => ProcessingAction.Separate,
            "transcribe" => ProcessingAction.Transcribe,
            "separate-transcribe" => ProcessingAction.SeparateTranscribe,
            _ => throw new ArgumentException("--action is required: extract, separate, transcribe or separate-transcribe.")
        };
        if (track < 0) throw new ArgumentException("--audio-track must be a nonnegative absolute stream index.");
        if (language != "auto" && (language.Length != 2 || language.Any(character => character is < 'a' or > 'z')))
            throw new ArgumentException("--language expects auto or a lowercase two-letter language code, for example en or fr.");
        if (language != "auto" && workflow is ProcessingAction.Extract or ProcessingAction.Separate)
            throw new ArgumentException("--language applies only to transcription workflows.");
        var selected = exports is null ? ProcessingConventions.DefaultExports(workflow) : exports.Split(',').Select(value => value switch
        {
            "original" => ExportKind.Original,
            "vocals" => ExportKind.Vocals,
            "background" => ExportKind.Background,
            "transcript" => ExportKind.Transcript,
            "subtitles" => ExportKind.Subtitles,
            _ => throw new ArgumentException($"Unknown export: {value}.")
        }).ToArray();
        var audioOptions = new ConversionOptions
        {
            TargetFormat = format, Bitrate = bitrate, Quality = quality, SampleRate = sampleRate, Channels = channels,
            BitDepth = depth, CompressionLevel = compression, Mono = mono, Overwrite = overwrite, FfmpegPath = ffmpeg
        };
        prefix ??= Path.GetFileNameWithoutExtension(input);
        var request = new ProcessingRequest(input, workflow, track, directory ?? Path.GetDirectoryName(input) ?? ".", prefix,
            selected, language, audioOptions);
        ProcessingService.ValidateRequest(request);
        return request;
    }

    /// <summary>Prints the fixed syntax and the current implementation boundary.</summary>
    public static void PrintHelp()
    {
        ConsoleTheme.Section(AnsiConsole.Console, "Audio / video processing / command reference");
        Console.WriteLine("Usage: odconv process <file> --action <extract|separate|transcribe|separate-transcribe> [options]");
        Console.WriteLine("  --audio-track <index>   Absolute FFprobe audio stream index; defaults to first audio stream");
        Console.WriteLine("  --exports <list>        Comma-separated: original,vocals,background,transcript,subtitles");
        Console.WriteLine("  --output-dir <dir>      Destination directory (default: next to source)");
        Console.WriteLine("  --prefix <name>         Filename prefix (default: source stem)");
        Console.WriteLine("  --language <code>       auto (default) or two-letter source language, e.g. en, fr; no translation");
        Console.WriteLine("  --to <format>           Audio export format (default: wav); convert encoding options also apply");
        Console.WriteLine("  --bitrate/-b <rate>     Lossy audio bitrate; --quality/-q <n> selects MP3/Ogg quality instead");
        Console.WriteLine("  --sample-rate <n>       22050, 44100, 48000 or 96000; --channels <n>: 1, 2 or 6");
        Console.WriteLine("  --mono                 Force mono; --bit-depth <n>: WAV/AIFF 16, 24 or 32");
        Console.WriteLine("  --compression <n>      FLAC compression from 0 to 8");
        Console.WriteLine("  --overwrite <value>     never (default) or always; --ffmpeg <path> overrides bundled tools");
        Console.WriteLine("Status: extract runs offline with FFmpeg; separate uses the installed private Demucs CPU module.");
        Console.WriteLine("Transcribe uses private Whisper CPU; separate-transcribe uses Demucs then Whisper. TXT/SRT use the source timeline.");
        Console.WriteLine("Separation requires separation; transcription requires transcription; combined action requires both modules.");
    }
}
