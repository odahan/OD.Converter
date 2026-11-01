using System.Reflection;
using OD.Converter.Services.Modules;

namespace OD.Converter.Services;

/// <summary>Contains the two estimated stems on the prepared PCM timeline.</summary>
public sealed record SeparatedAudio(PcmAudio Vocals, PcmAudio Background);

/// <summary>Runs the private CPU engine without external Python, model discovery or cached media reuse.</summary>
public sealed class DemucsSeparator(IModuleProcessRunner processRunner, IFfmpegRunner ffmpegRunner)
{
    /// <summary>Separates a complete aligned soundtrack, retaining its origin and normalizing only the tail.</summary>
    public async Task<SeparatedAudio> SeparateAsync(PcmAudio reference, ModuleContext module,
        string ffmpeg, AudioWorkspace workspace, CancellationToken ct)
    {
        var preparation = new AudioPreparationService(ffmpegRunner);
        var duration = (double)reference.SampleCount / reference.SampleRate;
        var engineAudio = await preparation.ResampleAsync(reference, 44100, 2, duration, ffmpeg,
            Path.Combine(workspace.DirectoryPath, "demucs-input.f32"), ct);
        var wave = await preparation.CreateSeparationWaveAsync(engineAudio, ffmpeg, workspace, ct);
        var script = Path.Combine(workspace.DirectoryPath, "separate.py");
        using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "OD.Converter.Services.Modules.Resources.separate.py")!)
        await using (var output = File.Create(script))
            await resource.CopyToAsync(output, ct);
        var outputDirectory = Path.Combine(workspace.DirectoryPath, "stems");
        Console.WriteLine("Separating vocals and background with htdemucs (CPU)...");
        await processRunner.RunAsync(module, "runtime/python.exe",
            ["-I", "-B", script, ModulePaths.Child(module.PayloadDirectory, "models/955717e8-8726e21a.th"),
                wave, outputDirectory], TimeSpan.FromDays(7), ct);

        async Task<PcmAudio> DecodeAsync(string name)
        {
            var path = Path.Combine(workspace.DirectoryPath, name + ".f32");
            var result = await ffmpegRunner.RunAsync(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i",
                Path.Combine(outputDirectory, name + ".wav"), "-ar", "44100", "-ac", "2", "-c:a", "pcm_f32le", "-f", "f32le", path], ct);
            ct.ThrowIfCancellationRequested();
            if (result.ExitCode != 0) throw new InvalidDataException($"Cannot decode Demucs {name}: {result.StandardError}");
            var count = PcmLength.Measure(path, 2);
            if (Math.Abs(count - engineAudio.SampleCount) > 1)
                throw new InvalidDataException($"Demucs {name} length differs from the prepared timeline ({count} vs {engineAudio.SampleCount} frames).");
            return await PcmLength.NormalizeAsync(path, 44100, 2, engineAudio.SampleCount, ct);
        }

        return new SeparatedAudio(await DecodeAsync("vocals"), await DecodeAsync("background"));
    }
}
