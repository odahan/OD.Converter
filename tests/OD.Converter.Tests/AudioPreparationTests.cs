using OD.Converter.Commands;
using OD.Converter.Models;
using OD.Converter.Services;
using Xunit;

namespace OD.Converter.Tests;

public sealed class AudioPreparationTests
{
    [Fact]
    public async Task Normalize_MeasuresFramesPadsZerosAndTrimsOnlyTail()
    {
        using var workspace = new AudioWorkspace();
        var path = Path.Combine(workspace.DirectoryPath, "samples.f32");
        var samples = new float[] { 0.25f, -0.25f, 0.5f, -0.5f };
        await File.WriteAllBytesAsync(path, ToBytes(samples));
        Assert.Equal(2, PcmLength.Measure(path, 2));
        await PcmLength.NormalizeAsync(path, 48000, 2, 5, default);
        Assert.Equal(samples.Concat(new float[6]), ReadSamples(path));
        await PcmLength.NormalizeAsync(path, 48000, 2, 1, default);
        Assert.Equal(samples.Take(2), ReadSamples(path));
        await File.WriteAllBytesAsync(path, new byte[3]);
        Assert.Throws<InvalidOperationException>(() => PcmLength.Measure(path, 2));
    }

    [Fact]
    public async Task Cancellation_CleansOperationDirectory()
    {
        string directory;
        using (var workspace = new AudioWorkspace())
        {
            directory = workspace.DirectoryPath;
            var path = Path.Combine(directory, "partial.f32");
            await File.WriteAllBytesAsync(path, ToBytes([0.5f]));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                PcmLength.NormalizeAsync(path, 48000, 1, 48000, new CancellationToken(true)));
        }
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void TimestampMapping_AppliesSegmentAndMeasuredDelayOnce()
    {
        Assert.Equal(0, new EngineTimeline().ToPresentationSeconds(0));
        Assert.Equal(12.25, new EngineTimeline(10, 0.25).ToPresentationSeconds(2.5));
        Assert.Equal(-0.25, new EngineTimeline(0, 0.25).ToPresentationSeconds(0));
        Assert.Equal(3, PcmLength.TargetSamples(2.5 / 16000, 16000));
    }

    [Theory]
    [InlineData(48000, 2, "wav")]
    [InlineData(44100, 1, "wav")]
    [InlineData(44100, 2, "mp3")]
    [InlineData(22050, 1, "mp3")]
    public async Task RealAudio_PreparesNativeAndEnginePcm(int rate, int channels, string extension)
    {
        using var workspace = new AudioWorkspace();
        var source = Path.Combine(workspace.DirectoryPath, $"audio.{extension}");
        var raw = Path.Combine(workspace.DirectoryPath, "input.f32");
        await File.WriteAllBytesAsync(raw, ToBytes(CreateMarkers(rate, channels, 3)));
        await Run(["-f", "f32le", "-ar", rate.ToString(), "-ac", channels.ToString(), "-i", raw, source]);
        var plan = await Plan(source);
        var service = new AudioPreparationService(Runner);
        var audio = await service.PrepareAsync(plan, Ffmpeg, workspace, default);
        Assert.Equal(PcmLength.TargetSamples(plan.Source.Timeline.TargetDurationSeconds!.Value, rate), audio.SampleCount);
        Assert.Equal(audio.SampleCount, PcmLength.Measure(audio.Path, channels));
        AssertMarkers(ReadSamples(audio.Path), rate, channels, [0.25, 1.5, 2.75], extension == "wav" ? 1 : rate / 100);
        var separation = await service.CreateSeparationWaveAsync(audio, Ffmpeg, workspace, default);
        var whisper = await service.CreateWhisperWaveAsync(audio, Ffmpeg, workspace, default);
        var probe = new FfprobeService();
        var splitInfo = await probe.ProbeAsync(separation, Ffprobe, default);
        Assert.Equal("pcm_f32le", splitInfo.CodecName);
        Assert.Equal(rate, splitInfo.SampleRate);
        Assert.Equal(channels, splitInfo.Channels);
        var whisperInfo = await probe.ProbeAsync(whisper, Ffprobe, default);
        Assert.Equal("pcm_s16le", whisperInfo.CodecName);
        Assert.Equal(16000, whisperInfo.SampleRate);
        Assert.Equal(1, whisperInfo.Channels);
        var decoded = Path.Combine(workspace.DirectoryPath, "whisper-check.f32");
        await Run(["-i", whisper, "-f", "f32le", decoded]);
        Assert.Equal(PcmLength.TargetSamples((double)audio.SampleCount / rate, 16000), PcmLength.Measure(decoded, 1));
        AssertMarkers(ReadSamples(decoded), 16000, 1, [0.25, 1.5, 2.75], extension == "wav" ? 1 : 160);
    }

    [Theory]
    [InlineData("mp4", false, 0)]
    [InlineData("mkv", true, 0)]
    public async Task RealVideo_RetainsDelayedTrackGapsAndTrailingSilence(string extension, bool gap, int origin)
    {
        using var workspace = new AudioWorkspace();
        var raw = Path.Combine(workspace.DirectoryPath, "markers.f32");
        await File.WriteAllBytesAsync(raw, ToBytes(CreateMarkers(48000, 1, 3)));
        var wave = Path.Combine(workspace.DirectoryPath, "input.wav");
        await Run(["-f", "f32le", "-ar", "48000", "-ac", "1", "-i", raw, "-c:a", "pcm_f32le", wave]);
        var source = Path.Combine(workspace.DirectoryPath, $"vidéo avec espaces.{extension}");
        var args = new List<string> { "-f", "lavfi", "-i", "color=c=black:s=32x32:r=25:d=5",
            "-itsoffset", "0.5", "-i", wave,
            "-f", "lavfi", "-i", "anullsrc=r=48000:cl=mono:d=5",
            "-map", "0:v", "-map", "2:a", "-map", "1:a", "-c:v", "mpeg4", "-c:a", extension == "mp4" ? "alac" : "pcm_f32le" };
        if (gap) args.AddRange(["-af", "aselect=not(between(t\\,0.7\\,1.1))"]);
        if (origin != 0) args.AddRange(["-output_ts_offset", origin.ToString()]);
        args.Add(source);
        await Run(args);
        var plan = await Plan(source, ["--audio-track", "2"]);
        Assert.True(plan.Source.HasVideo);
        Assert.Equal(2, plan.Track.StreamIndex);
        Assert.Equal(origin, plan.Source.Timeline.OriginSeconds!.Value, 3);
        var audio = await new AudioPreparationService(Runner).PrepareAsync(plan, Ffmpeg, workspace, default);
        Assert.Equal(240000, audio.SampleCount);
        var samples = ReadSamples(audio.Path);
        AssertMarkers(samples, 48000, 1, [0.75, 2, 3.25], extension == "mp4" ? 1 : 48);
        Assert.All(samples.Take(20000), value => Assert.Equal(0, value));
        Assert.All(samples.Skip(180000), value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task Extraction_ExportsSelectedTrackAtExactRequestedRateAndProtectsCollision()
    {
        using var workspace = new AudioWorkspace();
        var raw = Path.Combine(workspace.DirectoryPath, "markers.f32");
        await File.WriteAllBytesAsync(raw, ToBytes(CreateMarkers(44100, 2, 3)));
        var source = Path.Combine(workspace.DirectoryPath, "source.wav");
        await Run(["-f", "f32le", "-ar", "44100", "-ac", "2", "-i", raw, source]);
        var plan = await Plan(source, ["--sample-rate", "48000", "--mono", "--bit-depth", "24"]);
        var service = new AudioExtractionService(Runner, Runner);
        var output = await service.ExtractAsync(plan, Ffmpeg, default);
        var decoded = Path.Combine(workspace.DirectoryPath, "result.f32");
        await Run(["-i", output, "-f", "f32le", decoded]);
        Assert.Equal(144000, PcmLength.Measure(decoded, 1));
        AssertMarkers(ReadSamples(decoded), 48000, 1, [0.25, 1.5, 2.75], 1);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ExtractAsync(plan, Ffmpeg, default));
        Assert.Empty(Directory.GetFiles(workspace.DirectoryPath, ".odconv-*"));
    }

    [Theory]
    [InlineData(null, 3.0, TimelineReliability.Container)]
    [InlineData(0.0, null, TimelineReliability.Unknown)]
    [InlineData(0.0, 3.0, TimelineReliability.Inconsistent)]
    public async Task Preparation_RejectsUnreliableTimeline(double? origin, double? duration, TimelineReliability reliability)
    {
        using var workspace = new AudioWorkspace();
        var request = ProcessCommand.ParseArguments(["source.wav", "--action", "extract"]);
        var track = new AudioTrackInfo(0, 0, "pcm_f32le", null, null, 48000, 1, null, 32, 0, 0, 3);
        var source = new MediaSource("wav", false, [track], new PresentationTimeline(origin,
            TimelineReliability.Container, duration, reliability, []));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AudioPreparationService(Runner)
            .PrepareAsync(new ProcessingPlan(request, source, track), Ffmpeg, workspace, default));
    }

    [Fact]
    public async Task RealTransportStream_NormalizesNonzeroOriginWithoutMovingMarkers()
    {
        using var workspace = new AudioWorkspace();
        var raw = Path.Combine(workspace.DirectoryPath, "input.f32");
        var wave = Path.Combine(workspace.DirectoryPath, "input.wav");
        var source = Path.Combine(workspace.DirectoryPath, "nonzero.ts");
        await File.WriteAllBytesAsync(raw, ToBytes(CreateMarkers(48000, 1, 3)));
        await Run(["-f", "f32le", "-ar", "48000", "-ac", "1", "-i", raw, wave]);
        await Run(["-f", "lavfi", "-i", "color=s=32x32:r=25:d=5", "-itsoffset", "0.5", "-i", wave,
            "-map", "0:v", "-map", "1:a", "-c:v", "mpeg2video", "-c:a", "mp2", "-output_ts_offset", "3",
            "-mpegts_copyts", "1", source]);
        var plan = await Plan(source);
        Assert.Equal(3, plan.Source.Timeline.OriginSeconds);
        var audio = await new AudioPreparationService(Runner).PrepareAsync(plan, Ffmpeg, workspace, default);
        Assert.Equal(240000, audio.SampleCount);
        AssertMarkers(ReadSamples(audio.Path), 48000, 1, [0.75, 2, 3.25], 2);
    }

    [Fact]
    public async Task RealNegativePackets_ClipsOnlySamplesBeforeExplicitPresentationOrigin()
    {
        using var workspace = new AudioWorkspace();
        var raw = Path.Combine(workspace.DirectoryPath, "input.f32");
        var wave = Path.Combine(workspace.DirectoryPath, "input.wav");
        var source = Path.Combine(workspace.DirectoryPath, "negative.mkv");
        await File.WriteAllBytesAsync(raw, ToBytes(CreateMarkers(48000, 1, 3)));
        await Run(["-f", "f32le", "-ar", "48000", "-ac", "1", "-i", raw, "-c:a", "pcm_f32le", wave]);
        await Run(["-copyts", "-f", "lavfi", "-i", "color=s=32x32:r=25:d=5", "-itsoffset", "-0.5", "-i", wave,
            "-map", "0:v", "-map", "1:a", "-c:v", "mpeg4", "-c:a", "pcm_f32le", "-avoid_negative_ts", "disabled", source]);
        var plan = await Plan(source);
        Assert.Equal(-0.5, plan.Track.StartSeconds);
        plan = plan with { Source = plan.Source with { Timeline = new PresentationTimeline(0,
            TimelineReliability.Streams, 5, TimelineReliability.Streams, []) } };
        var audio = await new AudioPreparationService(Runner).PrepareAsync(plan, Ffmpeg, workspace, default);
        var samples = ReadSamples(audio.Path);
        Assert.Equal(240000, audio.SampleCount);
        AssertMarkers(samples, 48000, 1, [1, 2.25], 48);
        Assert.All(samples.Take(30000), value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportFailureOrCancellation_RemovesPartialsAndPreservesPreviousOutput(bool cancel)
    {
        using var workspace = new AudioWorkspace();
        var source = Path.Combine(workspace.DirectoryPath, "source.wav");
        await Run(["-f", "lavfi", "-i", "sine=frequency=440:duration=1", source]);
        var plan = await Plan(source, ["--overwrite", "always"]);
        var destination = ProcessingConventions.OutputPath(plan.Request, ExportKind.Original);
        await File.WriteAllTextAsync(destination, "previous output");
        var runner = new FailingExportRunner(cancel);
        var service = new AudioExtractionService(runner, Runner);
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExtractAsync(plan, Ffmpeg, default));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExtractAsync(plan, Ffmpeg, default));
        Assert.Equal("previous output", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetFiles(workspace.DirectoryPath, ".odconv-*"));
        Assert.NotNull(runner.WorkDirectory);
        Assert.False(Directory.Exists(runner.WorkDirectory));
    }

    [Fact]
    public async Task RealLongPcm_ResamplingPreservesBeginningMiddleAndEndWithoutDrift()
    {
        using var workspace = new AudioWorkspace();
        var times = new[] { 0.25, 30.0, 59.75 };
        var samples = new float[44100 * 60];
        foreach (var time in times) samples[(int)(time * 44100)] = 0.75f;
        var raw = Path.Combine(workspace.DirectoryPath, "minute.f32");
        var wave = Path.Combine(workspace.DirectoryPath, "minute.wav");
        await File.WriteAllBytesAsync(raw, ToBytes(samples));
        await Run(["-f", "f32le", "-ar", "44100", "-ac", "1", "-i", raw, "-c:a", "pcm_f32le", wave]);
        var plan = await Plan(wave);
        var service = new AudioPreparationService(Runner);
        var native = await service.PrepareAsync(plan, Ffmpeg, workspace, default);
        var converted = await service.ResampleAsync(native, 48000, 1, 60, Ffmpeg,
            Path.Combine(workspace.DirectoryPath, "resampled.f32"), default);
        Assert.Equal(2880000, PcmLength.Measure(converted.Path, 1));
        AssertMarkers(ReadSamples(converted.Path), 48000, 1, times, 1);
    }

    private sealed class FailingExportRunner(bool cancel) : IFfmpegRunner
    {
        public string? WorkDirectory { get; private set; }

        public async Task<(int ExitCode, string StandardError)> RunAsync(string ffmpegPath,
            IReadOnlyCollection<string> args, CancellationToken ct)
        {
            var output = args.Last();
            if (output.EndsWith("selected.f32", StringComparison.Ordinal))
            {
                WorkDirectory = Path.GetDirectoryName(output);
                return await Runner.RunAsync(ffmpegPath, args, ct);
            }
            await File.WriteAllTextAsync(output, "incomplete", ct);
            if (cancel) throw new OperationCanceledException();
            return (3, "Simulated encoder failure.");
        }
    }

    private static readonly FfmpegConverter Runner = new(new ConversionOptionsValidator(new FileSystem()));
    private static string Ffmpeg => Path.Combine(ToolRoot(), "ffmpeg.exe");
    private static string Ffprobe => Path.Combine(ToolRoot(), "ffprobe.exe");

    private static string ToolRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "OD.Converter", "tools");
            if (File.Exists(Path.Combine(candidate, "ffmpeg.exe"))) return candidate;
        }
        throw new FileNotFoundException("Bundled FFmpeg is required for real PCM alignment tests.");
    }

    private static async Task Run(IReadOnlyCollection<string> args)
    {
        var result = await Runner.RunAsync(Ffmpeg, new[] { "-nostdin", "-v", "error", "-y" }.Concat(args).ToArray(), default);
        Assert.True(result.ExitCode == 0, result.StandardError);
    }

    private static async Task<ProcessingPlan> Plan(string path, string[]? options = null)
    {
        var request = ProcessCommand.ParseArguments(new[] { path, "--action", "extract" }.Concat(options ?? []).ToArray());
        var source = (await new FfprobeService().ProbeAsync(path, Ffprobe, default)).Source!;
        return new ProcessingPlan(request, source, source.SelectAudioTrack(request.AudioStreamIndex));
    }

    private static float[] CreateMarkers(int rate, int channels, int seconds)
    {
        var data = new float[rate * channels * seconds];
        foreach (var time in new[] { 0.25, 1.5, 2.75 })
            for (var channel = 0; channel < channels; channel++)
                data[(int)(time * rate) * channels + channel] = 0.75f;
        return data;
    }

    private static byte[] ToBytes(float[] samples)
    {
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] ReadSamples(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var samples = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        return samples;
    }

    private static void AssertMarkers(float[] samples, int rate, int channels, double[] times, int tolerance)
    {
        foreach (var time in times)
        {
            var target = (int)Math.Round(time * rate);
            var start = Math.Max(0, target - rate / 20);
            var end = Math.Min(samples.Length / channels, target + rate / 20);
            var peak = Enumerable.Range(start, end - start).MaxBy(index => Math.Abs(samples[index * channels]));
            Assert.True(Math.Abs(samples[peak * channels]) > 0.01, $"No marker near {time}s.");
            Assert.True(Math.Abs(peak - target) <= tolerance, $"Marker at {peak}, expected {target}, tolerance {tolerance} samples.");
        }
    }
}
