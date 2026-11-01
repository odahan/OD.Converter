using System.Globalization;
using OD.Converter.Models;

namespace OD.Converter.Services;

/// <summary>Decodes explicitly selected audio while retaining its presentation timestamps and gaps.</summary>
public sealed class AudioPreparationService(IFfmpegRunner runner)
{
    /// <summary>Prepares float PCM at the source rate and channel count without lossy compression.</summary>
    public async Task<PcmAudio> PrepareAsync(ProcessingPlan plan, string ffmpeg, AudioWorkspace workspace,
        CancellationToken ct)
    {
        var timeline = plan.Source.Timeline;
        var sampleBasedWave = !plan.Source.HasVideo && plan.Source.FormatName == "wav"
            && plan.Track.CodecName.StartsWith("pcm_", StringComparison.Ordinal);
        if (sampleBasedWave && timeline.OriginSeconds is null && plan.Track.StartSeconds is null)
        {
            Console.WriteLine("PCM WAV uses a sample-based timeline: sample zero is the presentation origin.");
            timeline = timeline with { OriginSeconds = 0, OriginReliability = TimelineReliability.Streams };
        }
        if (timeline.OriginSeconds is not double origin || !double.IsFinite(origin)
            || timeline.TargetDurationSeconds is not double duration || !double.IsFinite(duration) || duration <= 0
            || timeline.OriginReliability is TimelineReliability.Unknown or TimelineReliability.Inconsistent
            || timeline.DurationReliability is TimelineReliability.Unknown or TimelineReliability.Inconsistent
            || plan.Track.StartSeconds is null && !sampleBasedWave)
            throw new InvalidOperationException("Cannot prepare aligned audio: presentation origin, track start or target duration is unknown or inconsistent. Repair the source timestamps before processing.");
        if (plan.Track.SampleRate <= 0 || plan.Track.Channels <= 0)
            throw new InvalidOperationException("Cannot prepare PCM with an unknown sample rate or channel count.");
        var rate = plan.Track.SampleRate;
        var count = PcmLength.TargetSamples(duration, rate);
        var path = Path.Combine(workspace.DirectoryPath, "selected.f32");
        var originText = origin.ToString("R", CultureInfo.InvariantCulture);
        var filter = $"asetpts=PTS-({originText})/TB,aresample={rate}:async=1:first_pts=0:min_hard_comp=0:max_soft_comp=0,atrim=end_sample={count}";
        await RunCheckedAsync(ffmpeg, ["-nostdin", "-v", "error", "-y", "-copyts", "-i", plan.Request.InputPath,
            "-map", $"0:{plan.Track.StreamIndex}", "-vn", "-sn", "-dn", "-af", filter,
            "-ar", rate.ToString(CultureInfo.InvariantCulture), "-ac", plan.Track.Channels.ToString(CultureInfo.InvariantCulture),
            "-c:a", "pcm_f32le", "-f", "f32le", path], ct);
        return await PcmLength.NormalizeAsync(path, rate, plan.Track.Channels, count, ct);
    }

    /// <summary>Creates a float WAV suitable for separation, preserving the source rate and channels.</summary>
    public async Task<string> CreateSeparationWaveAsync(PcmAudio audio, string ffmpeg,
        AudioWorkspace workspace, CancellationToken ct)
    {
        var path = Path.Combine(workspace.DirectoryPath, "separation.wav");
        var args = RawInputArguments(audio);
        args.AddRange(["-c:a", "pcm_f32le", "-rf64", "auto", path]);
        await RunCheckedAsync(ffmpeg, args, ct);
        return path;
    }

    /// <summary>Creates 16 kHz mono signed 16-bit WAV for Whisper from already aligned PCM.</summary>
    public async Task<string> CreateWhisperWaveAsync(PcmAudio audio, string ffmpeg,
        AudioWorkspace workspace, CancellationToken ct)
    {
        var mono = await ResampleAsync(audio, 16000, 1, (double)audio.SampleCount / audio.SampleRate,
            ffmpeg, Path.Combine(workspace.DirectoryPath, "whisper.f32"), ct);
        var path = Path.Combine(workspace.DirectoryPath, "whisper.wav");
        var args = RawInputArguments(mono);
        args.AddRange(["-c:a", "pcm_s16le", "-rf64", "auto", path]);
        await RunCheckedAsync(ffmpeg, args, ct);
        return path;
    }

    /// <summary>Resamples without changing speed, then measures and normalizes the target frame count.</summary>
    public async Task<PcmAudio> ResampleAsync(PcmAudio audio, int rate, int channels, double targetDuration,
        string ffmpeg, string outputPath, CancellationToken ct)
    {
        var count = PcmLength.TargetSamples(targetDuration, rate);
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
        var args = RawInputArguments(audio);
        args.AddRange(["-ar", rate.ToString(CultureInfo.InvariantCulture), "-ac", channels.ToString(CultureInfo.InvariantCulture),
            "-c:a", "pcm_f32le", "-f", "f32le", outputPath]);
        await RunCheckedAsync(ffmpeg, args, ct);
        return await PcmLength.NormalizeAsync(outputPath, rate, channels, count, ct);
    }

    /// <summary>Builds a raw PCM input without inferring timestamps or formats from a filename.</summary>
    public static List<string> RawInputArguments(PcmAudio audio) =>
        ["-nostdin", "-v", "error", "-y", "-f", "f32le", "-ar", audio.SampleRate.ToString(CultureInfo.InvariantCulture),
            "-ac", audio.Channels.ToString(CultureInfo.InvariantCulture), "-i", audio.Path];

    private async Task RunCheckedAsync(string ffmpeg, IReadOnlyCollection<string> args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = await runner.RunAsync(ffmpeg, args, ct);
        ct.ThrowIfCancellationRequested();
        if (result.ExitCode != 0) throw new InvalidOperationException($"FFmpeg audio preparation failed: {result.StandardError}");
    }
}
