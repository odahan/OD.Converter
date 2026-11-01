using OD.Converter.Models;
using OD.Converter.Services.Modules;

namespace OD.Converter.Services;

/// <summary>Executes the shared separation workflow for either audio or video sources.</summary>
public interface IAudioSeparationService
{
    /// <summary>Reports finalized exports even when a later export fails or is cancelled.</summary>
    Task<ProcessingResult> SeparateAsync(ProcessingPlan plan, string ffmpeg, CancellationToken ct);
}

/// <summary>Coordinates private Demucs execution and aligned audio exports.</summary>
public sealed class AudioSeparationService(ModuleManager manager, IModuleProcessRunner processRunner,
    IFfmpegRunner runner, IArgumentBuilder argumentBuilder) : IAudioSeparationService
{
    /// <inheritdoc />
    public async Task<ProcessingResult> SeparateAsync(ProcessingPlan plan, string ffmpeg, CancellationToken ct)
    {
        var outputs = new List<ProcessingOutput>();
        try
        {
            var request = plan.Request;
            var destinations = request.Exports.ToDictionary(kind => kind,
                kind => AudioExportService.ValidateDestination(request, kind));
            using var module = await manager.OpenAsync(ModuleId.Separation, ct);
            using var workspace = new AudioWorkspace();
            var preparation = new AudioPreparationService(runner);
            Console.WriteLine("Preparing selected audio on the presentation timeline...");
            var reference = await preparation.PrepareAsync(plan, ffmpeg, workspace, ct);
            var stems = await new DemucsSeparator(processRunner, runner).SeparateAsync(reference, module.Context,
                ffmpeg, workspace, ct);
            var exporter = new AudioExportService(runner, argumentBuilder);
            foreach (var kind in request.Exports)
            {
                ct.ThrowIfCancellationRequested();
                var audio = kind switch
                {
                    ExportKind.Original => reference,
                    ExportKind.Vocals => stems.Vocals,
                    ExportKind.Background => stems.Background,
                    _ => throw new ArgumentException("Separation supports only original, vocals and background exports.")
                };
                // All exports default to the selected source rate and channel count, including mono sources.
                var rate = request.AudioOptions.SampleRate ?? reference.SampleRate;
                var channels = request.AudioOptions.EffectiveChannels > 0
                    ? request.AudioOptions.EffectiveChannels : reference.Channels;
                if (audio.SampleRate != rate || audio.Channels != channels)
                    audio = await preparation.ResampleAsync(audio, rate, channels,
                        plan.Source.Timeline.TargetDurationSeconds!.Value, ffmpeg,
                        Path.Combine(workspace.DirectoryPath, kind + "-export.f32"), ct);
                var destination = await exporter.ExportAsync(audio, request, destinations[kind],
                    plan.Source.Timeline.TargetDurationSeconds!.Value, ffmpeg, workspace, ct);
                outputs.Add(new ProcessingOutput(kind, destination));
            }
            return new ProcessingResult(0, outputs, [], null);
        }
        catch (OperationCanceledException) { return new ProcessingResult(130, outputs, [], "Separation cancelled."); }
        catch (ModuleUnavailableException ex) { return new ProcessingResult(2, outputs, [], ex.Message); }
        catch (ArgumentException ex) { return new ProcessingResult(1, outputs, [], ex.Message); }
        catch (Exception ex) { return new ProcessingResult(3, outputs, [], ex.Message); }
    }
}
