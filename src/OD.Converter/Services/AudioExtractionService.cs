using OD.Converter.Models;

namespace OD.Converter.Services;

/// <summary>Executes offline extraction with private intermediates and atomic destination finalization.</summary>
public interface IAudioExtractionService
{
    /// <summary>Exports the selected soundtrack without installing optional engines.</summary>
    Task<string> ExtractAsync(ProcessingPlan plan, string ffmpeg, CancellationToken ct);
}

/// <summary>Connects timeline-aware PCM preparation to the existing audio encoders.</summary>
public sealed class AudioExtractionService(IFfmpegRunner runner, IArgumentBuilder argumentBuilder) : IAudioExtractionService
{
    /// <inheritdoc />
    public async Task<string> ExtractAsync(ProcessingPlan plan, string ffmpeg, CancellationToken ct)
    {
        var request = plan.Request;
        var destination = AudioExportService.ValidateDestination(request, ExportKind.Original);
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var workspace = new AudioWorkspace();
        var preparation = new AudioPreparationService(runner);
        Console.WriteLine("Preparing selected audio on the presentation timeline...");
        var audio = await preparation.PrepareAsync(plan, ffmpeg, workspace, ct);
        return await new AudioExportService(runner, argumentBuilder).ExportAsync(audio, request, destination,
            plan.Source.Timeline.TargetDurationSeconds!.Value, ffmpeg, workspace, ct);
    }
}
