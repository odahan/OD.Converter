using OD.Converter.Models;
using OD.Converter.Services.Modules;

namespace OD.Converter.Services;

/// <summary>Analyzes media, validates typed workflows and dispatches the shared processing engines.</summary>
public sealed class ProcessingService(IFfmpegLocator locator, IFileSystem files, IFfprobeService probe,
    ModuleManager modules, IAudioExtractionService extraction, IAudioSeparationService separation,
    IAudioTranscriptionService transcription)
{
    /// <summary>Probes one source without requiring an audio track or optional module.</summary>
    public Task<ProbeInfo> AnalyzeAsync(string input, string? ffmpegPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!files.FileExists(input)) throw new ArgumentException($"Source file not found: \"{input}\".");
        string executable;
        try { executable = locator.ResolveFfprobe(ffmpegPath); }
        catch (Exception ex) { throw new MediaToolException(ex); }
        return probe.ProbeAsync(input, executable, ct);
    }

    /// <summary>Checks options shared by CLI arguments and the desktop workflow.</summary>
    public static void ValidateRequest(ProcessingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath)) throw new ArgumentException("A source file is required.");
        if (request.AudioStreamIndex < 0) throw new ArgumentException("--audio-track must be a nonnegative absolute stream index.");
        var language = request.Language;
        if (language != "auto" && (language.Length != 2 || language.Any(character => character is < 'a' or > 'z')))
            throw new ArgumentException("--language expects auto or a lowercase two-letter language code, for example en or fr.");
        if (language != "auto" && request.Action is ProcessingAction.Extract or ProcessingAction.Separate)
            throw new ArgumentException("--language applies only to transcription workflows.");
        var exports = request.Exports;
        if (exports.Count == 0 || exports.Distinct().Count() != exports.Count ||
            exports.Except(ProcessingConventions.AllowedExports(request.Action)).Any())
            throw new ArgumentException("Exports must be unique and supported by the selected action.");
        new ConversionOptionsValidator(new FileSystem()).Validate(request.AudioOptions);
        ProcessingConventions.ValidatePrefix(request.OutputPrefix);
    }

    /// <summary>Resolves the track and validates parameters before displaying a workflow for review.</summary>
    public static ProcessingPlan Plan(ProcessingRequest request, ProbeInfo analysis)
    {
        ValidateRequest(request);
        var source = analysis.Source ?? MediaProbeParser.Parse(analysis.RawJson);
        return new ProcessingPlan(request, source, source.SelectAudioTrack(request.AudioStreamIndex));
    }

    /// <summary>Executes a reviewed workflow and preserves the list of actually finalized files.</summary>
    public async Task<ProcessingResult> ExecuteAsync(ProcessingPlan plan, CancellationToken ct,
        Action<ProcessingPlan>? started = null, bool checkModules = true, Action? executing = null)
    {
        var request = plan.Request;
        try
        {
            ct.ThrowIfCancellationRequested();
            foreach (var kind in request.Exports) AudioExportService.ValidateDestination(request, kind);
            started?.Invoke(plan);

            string ffmpeg;
            try { ffmpeg = locator.Resolve(request.AudioOptions.FfmpegPath); }
            catch (Exception ex) { throw new MediaToolException(ex); }
            if (checkModules)
            {
                var unavailable = await ProcessingAvailability.CheckAsync(request.Action, modules, ct);
                if (unavailable is not null) return new ProcessingResult(2, [], [], unavailable);
            }

            executing?.Invoke();
            return request.Action switch
            {
                ProcessingAction.Extract => new ProcessingResult(0,
                    [new ProcessingOutput(ExportKind.Original, await extraction.ExtractAsync(plan, ffmpeg, ct))], [], null),
                ProcessingAction.Separate => await separation.SeparateAsync(plan, ffmpeg, ct),
                _ => await transcription.TranscribeAsync(plan, ffmpeg, ct)
            };
        }
        catch (OperationCanceledException) { return new ProcessingResult(130, [], [], null); }
        catch (MediaToolException ex) { return new ProcessingResult(2, [], [], ex.Message); }
        catch (ArgumentException ex) { return new ProcessingResult(1, [], [], ex.Message); }
        catch (Exception ex) { return new ProcessingResult(3, [], [], $"Source processing failed: {ex.Message}"); }
    }

    /// <summary>Returns the common encoder delay warning for lossy audio exports.</summary>
    public static string? EncoderDelayWarning(ProcessingRequest request, IEnumerable<ProcessingOutput> outputs) =>
        outputs.Any(output => ProcessingConventions.IsAudioExport(output.Kind)) &&
        FormatRegistry.Get(request.AudioOptions.TargetFormat)?.IsLossless == false
            ? "Compressed exports may include encoder delay; PCM is the editing reference."
            : null;
}
