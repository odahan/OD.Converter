using OD.Converter.Models;
using OD.Converter.Services.Modules;

namespace OD.Converter.Services;

/// <summary>Exposes direct and separated global transcription to shared command orchestration.</summary>
public interface IAudioTranscriptionService
{
    /// <summary>Returns finalized outputs even if a later export fails.</summary>
    Task<ProcessingResult> TranscribeAsync(ProcessingPlan plan, string ffmpeg, CancellationToken ct);
}

/// <summary>Coordinates aligned preparation, optional separation, native transcription and exports.</summary>
public sealed class AudioTranscriptionService(ModuleManager manager, IModuleProcessRunner processRunner,
    IFfmpegRunner runner, IArgumentBuilder argumentBuilder) : IAudioTranscriptionService
{
    /// <inheritdoc />
    public async Task<ProcessingResult> TranscribeAsync(ProcessingPlan plan, string ffmpeg, CancellationToken ct)
    {
        var outputs = new List<ProcessingOutput>();
        var warnings = new List<string>();
        try
        {
            var request = plan.Request;
            var destinations = request.Exports.ToDictionary(kind => kind,
                kind => AudioExportService.ValidateDestination(request, kind));
            using var whisper = await manager.OpenAsync(ModuleId.Transcription, ct);
            using var demucs = request.Action == ProcessingAction.SeparateTranscribe
                ? await manager.OpenAsync(ModuleId.Separation, ct) : null;
            using var workspace = new AudioWorkspace();
            var preparation = new AudioPreparationService(runner);
            Console.WriteLine("Preparing selected audio on the presentation timeline...");
            var reference = await preparation.PrepareAsync(plan, ffmpeg, workspace, ct);
            var stems = demucs is null ? null : await new DemucsSeparator(processRunner, runner)
                .SeparateAsync(reference, demucs.Context, ffmpeg, workspace, ct);
            IReadOnlyList<TranscriptCue>? cues = null;
            if (request.Exports.Any(kind => kind is ExportKind.Transcript or ExportKind.Subtitles))
            {
                cues = await new WhisperTranscriber(processRunner, runner)
                    .TranscribeAsync(stems?.Vocals ?? reference, whisper.Context, request.Language, ffmpeg, ct);
                warnings.Add(cues.Count == 0
                    ? "No speech was recognized. Requested TXT/SRT exports are empty; review non-silent sources for missed speech."
                    : "Whisper text and phrase boundaries are estimates. Review accuracy, missing speech and possible hallucinations, especially on music or noise.");
            }
            var exporter = new AudioExportService(runner, argumentBuilder);
            foreach (var kind in request.Exports)
            {
                ct.ThrowIfCancellationRequested();
                var destination = destinations[kind];
                if (kind is ExportKind.Transcript or ExportKind.Subtitles)
                    await TranscriptFormat.ExportAsync(TranscriptFormat.Serialize(cues!, kind == ExportKind.Subtitles),
                        destination, request.AudioOptions.Overwrite == "always", ct);
                else
                {
                    var audio = kind switch
                    {
                        ExportKind.Original => reference,
                        ExportKind.Vocals => stems!.Vocals,
                        ExportKind.Background => stems!.Background,
                        _ => throw new ArgumentException("Unsupported transcription export.")
                    };
                    var rate = request.AudioOptions.SampleRate ?? reference.SampleRate;
                    var channels = request.AudioOptions.EffectiveChannels > 0
                        ? request.AudioOptions.EffectiveChannels : reference.Channels;
                    if (audio.SampleRate != rate || audio.Channels != channels)
                        audio = await preparation.ResampleAsync(audio, rate, channels,
                            plan.Source.Timeline.TargetDurationSeconds!.Value, ffmpeg,
                            Path.Combine(workspace.DirectoryPath, kind + "-export.f32"), ct);
                    await exporter.ExportAsync(audio, request, destination,
                        plan.Source.Timeline.TargetDurationSeconds!.Value, ffmpeg, workspace, ct);
                }
                outputs.Add(new ProcessingOutput(kind, destination));
            }
            return new ProcessingResult(0, outputs, warnings, null);
        }
        catch (OperationCanceledException) { return new ProcessingResult(130, outputs, warnings, "Transcription cancelled."); }
        catch (ModuleUnavailableException ex) { return new ProcessingResult(2, outputs, warnings, ex.Message); }
        catch (ArgumentException ex) { return new ProcessingResult(1, outputs, warnings, ex.Message); }
        catch (Exception ex) { return new ProcessingResult(3, outputs, warnings, ex.Message); }
    }
}
