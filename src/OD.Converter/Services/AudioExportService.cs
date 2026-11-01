using OD.Converter.Models;

namespace OD.Converter.Services;

/// <summary>Encodes aligned PCM using shared audio settings and finalizes only complete files.</summary>
public sealed class AudioExportService(IFfmpegRunner runner, IArgumentBuilder argumentBuilder)
{
    /// <summary>Checks a destination before expensive processing begins.</summary>
    public static string ValidateDestination(ProcessingRequest request, ExportKind kind)
    {
        var destination = Path.GetFullPath(ProcessingConventions.OutputPath(request, kind));
        if (string.Equals(destination, Path.GetFullPath(request.InputPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An export cannot replace the source file. Choose a different prefix or directory.");
        if (Directory.Exists(destination) || (File.Exists(destination) && request.AudioOptions.Overwrite != "always"))
            throw new ArgumentException($"Output already exists: {destination}. Use --overwrite always or a different destination.");
        return destination;
    }

    /// <summary>Resamples, measures and encodes one export without changing the PCM origin.</summary>
    public async Task<string> ExportAsync(PcmAudio audio, ProcessingRequest request, string destination,
        double targetDuration, string ffmpeg, AudioWorkspace workspace, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var preparation = new AudioPreparationService(runner);
        var settings = request.AudioOptions;
        var rate = settings.SampleRate ?? audio.SampleRate;
        var channels = settings.EffectiveChannels > 0 ? settings.EffectiveChannels : audio.Channels;
        if (rate != audio.SampleRate || channels != audio.Channels)
            audio = await preparation.ResampleAsync(audio, rate, channels, targetDuration,
                ffmpeg, Path.Combine(workspace.DirectoryPath, "export.f32"), ct);
        Console.WriteLine($"Prepared {audio.SampleCount} PCM frames at {audio.SampleRate} Hz, {audio.Channels} channel(s).");
        var partial = Path.Combine(Path.GetDirectoryName(destination)!, $".odconv-{Guid.NewGuid():N}{Path.GetExtension(destination)}");
        try
        {
            var options = new ConversionOptions
            {
                InputPath = audio.Path, OutputPath = partial, TargetFormat = settings.TargetFormat,
                Bitrate = settings.Bitrate, Quality = settings.Quality, BitDepth = settings.BitDepth,
                CompressionLevel = settings.CompressionLevel, Overwrite = "never"
            };
            var args = argumentBuilder.BuildArguments(options);
            var inputIndex = args.IndexOf("-i");
            args.InsertRange(inputIndex, ["-f", "f32le", "-ar", audio.SampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-ac", audio.Channels.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            args.Insert(0, "-nostdin");
            if (FormatRegistry.Get(settings.TargetFormat)?.Key == "wav")
                args.InsertRange(args.Count - 1, ["-rf64", "auto"]);
            Console.WriteLine("Encoding audio export...");
            var result = await runner.RunAsync(ffmpeg, args, ct);
            ct.ThrowIfCancellationRequested();
            if (result.ExitCode != 0 || !File.Exists(partial) || new FileInfo(partial).Length == 0)
                throw new InvalidOperationException($"FFmpeg audio export failed: {result.StandardError}");
            File.Move(partial, destination, overwrite: settings.Overwrite == "always");
            return destination;
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }
}
