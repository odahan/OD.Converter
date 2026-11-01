using System.Security.Cryptography;
using Moq;
using OD.Converter.Commands;
using OD.Converter.Models;
using OD.Converter.Services;
using OD.Converter.Services.Modules;
using Xunit;

namespace OD.Converter.Tests;

public sealed class AudioTranscriptionTests
{
    private const string ValidSrt = "1\n00:00:00,500 --> 00:00:01,250\nBonjour à tous.\n\n2\n00:00:02,000 --> 00:00:02,750\nHello everyone.\n\n";
    private static string Tools => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/OD.Converter/tools"));
    private static string Ffmpeg => Path.Combine(Tools, "ffmpeg.exe");
    private static FfmpegConverter Converter => new(new ConversionOptionsValidator(new FileSystem()));

    [Fact]
    public void Formats_RetainSilenceUnicodeAndGlobalTimeline()
    {
        var cues = TranscriptFormat.Parse(ValidSrt, 3);
        Assert.Equal(500, cues[0].StartMilliseconds);
        Assert.Equal(2000, cues[1].StartMilliseconds);
        Assert.Equal(ValidSrt, TranscriptFormat.Serialize(cues, true));
        Assert.Equal("Bonjour à tous.\nHello everyone.\n", TranscriptFormat.Serialize(cues, false));
        Assert.Equal("25:00:00,123", TranscriptFormat.Timestamp(90000123));
        Assert.Empty(TranscriptFormat.Parse("", 3));
    }

    [Theory]
    [InlineData("1\n00:00:00,000 --> 00:00:01,000")]
    [InlineData("2\n00:00:00,000 --> 00:00:01,000\ntext")]
    [InlineData("1\n00:60:00,000 --> 00:60:01,000\ntext")]
    [InlineData("1\n00:00:01,000 --> 00:00:00,000\ntext")]
    [InlineData("1\n00:00:00,000 --> 00:00:04,000\ntext")]
    [InlineData("1\n00:00:00,000 --> 00:00:02,000\ntext\n\n2\n00:00:01,000 --> 00:00:03,000\ntext")]
    public void InvalidOrIncompleteSubtitles_AreRejected(string srt) =>
        Assert.Throws<InvalidDataException>(() => TranscriptFormat.Parse(srt, 3));

    [Fact]
    public void StructuralValidation_DoesNotInventAHallucinationDecisionForRepeatedSpeech()
    {
        var repeated = string.Concat(Enumerable.Range(0, 6).Select(index =>
            $"{index + 1}\n{TranscriptFormat.Timestamp(index * 2000)} --> {TranscriptFormat.Timestamp((index + 1) * 2000)}\nはい、お疲れ様でした。\n\n"));
        Assert.Equal(6, TranscriptFormat.Parse(repeated, 12).Count);
    }

    [Fact]
    public void WhisperVad_FinalPaddedWindow_IsClippedWithoutMovingOtherTimestamps()
    {
        const string srt = "1\n00:00:00,260 --> 00:00:28,460\nFirst phrase.\n\n2\n00:00:28,460 --> 00:00:30,020\nLast phrase.\n";
        Assert.Throws<InvalidDataException>(() => TranscriptFormat.Parse(srt, 30));
        var cues = TranscriptFormat.ParseWhisperOutput(srt, 30, out var clipped);
        Assert.True(clipped);
        Assert.Equal(260, cues[0].StartMilliseconds);
        Assert.Equal(28460, cues[0].EndMilliseconds);
        Assert.Equal(28460, cues[1].StartMilliseconds);
        Assert.Equal(30000, cues[1].EndMilliseconds);
        Assert.Equal(cues.ToArray(), TranscriptFormat.Parse(TranscriptFormat.Serialize(cues, true), 30).ToArray());
    }

    [Theory]
    [InlineData(30, "1\n00:00:28,460 --> 00:00:30,030\nToo far.")]
    [InlineData(30, "1\n00:00:30,000 --> 00:00:30,020\nEntirely outside.")]
    [InlineData(30, "1\n00:00:29,000 --> 00:00:30,020\nNot final.\n\n2\n00:00:30,020 --> 00:00:30,030\nOutside.")]
    [InlineData(30, "1\n00:00:00,000 --> 00:00:29,000\nFirst.\n\n2\n00:00:28,460 --> 00:00:30,020\nOverlap.")]
    [InlineData(12, "1\n00:00:11,000 --> 00:00:12,020\nNo partial VAD window.")]
    public void WhisperVad_InvalidEndsAndOverlapsRemainRejected(double duration, string srt) =>
        Assert.Throws<InvalidDataException>(() => TranscriptFormat.ParseWhisperOutput(srt, duration, out _));

    [Fact]
    public void WhisperVad_InRangeOutputRemainsUnchanged()
    {
        var cues = TranscriptFormat.ParseWhisperOutput(ValidSrt, 3, out var clipped);
        Assert.False(clipped);
        Assert.Equal(ValidSrt, TranscriptFormat.Serialize(cues, true));
    }

    [Theory]
    [InlineData(false, "fr")]
    [InlineData(false, "en")]
    [InlineData(true, "auto")]
    public async Task Workflow_RealPreparationAndSimulatedEngines_ProduceGlobalExports(bool combined, string language)
    {
        using var workspace = new AudioWorkspace();
        var plan = await Plan(workspace, combined, language);
        var engine = Engine();
        var result = await new AudioTranscriptionService(await Manager(workspace), engine.Object, Converter, Converter)
            .TranscribeAsync(plan, Ffmpeg, default);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(combined ? 4 : 2, result.Outputs.Count);
        Assert.Equal(ValidSrt, await File.ReadAllTextAsync(result.Outputs.Single(output => output.Kind == ExportKind.Subtitles).Path));
        var transcript = await File.ReadAllBytesAsync(result.Outputs.Single(output => output.Kind == ExportKind.Transcript).Path);
        Assert.Equal("Bonjour à tous.\nHello everyone.\n", System.Text.Encoding.UTF8.GetString(transcript));
        engine.Verify(value => value.RunAsync(It.IsAny<ModuleContext>(), "runtime/Release/whisper-cli.exe",
            It.Is<IReadOnlyList<string>>(args => args.Contains(language) && args.Contains("-ng")
                && args.Contains("--vad") && args.Contains("-vm") && !args.Contains("-tr")),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(Directory.GetFiles(plan.Request.OutputDirectory, ".odconv-*"));
    }

    [Fact]
    public async Task DigitalSilence_ProducesEmptyExportsWithoutCallingRecognition()
    {
        using var workspace = new AudioWorkspace();
        var plan = await Plan(workspace, false, "en", silence: true);
        var engine = new Mock<IModuleProcessRunner>(MockBehavior.Strict);
        var result = await new AudioTranscriptionService(await Manager(workspace), engine.Object, Converter, Converter)
            .TranscribeAsync(plan, Ffmpeg, default);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, result.Outputs.Count);
        foreach (var output in result.Outputs) Assert.Equal(0, new FileInfo(output.Path).Length);
        Assert.Contains(result.Warnings, warning => warning.Contains("No speech"));
        engine.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("collision", 1)]
    [InlineData("missing-module", 2)]
    [InlineData("missing-output", 3)]
    [InlineData("invalid-output", 3)]
    [InlineData("failure", 3)]
    [InlineData("cancel", 130)]
    public async Task Failures_PreservePreviousExportsAndReportAccurateStatus(string mode, int expected)
    {
        using var workspace = new AudioWorkspace();
        var plan = await Plan(workspace, false, "fr");
        Directory.CreateDirectory(plan.Request.OutputDirectory);
        var destination = ProcessingConventions.OutputPath(plan.Request, ExportKind.Subtitles);
        await File.WriteAllTextAsync(destination, "previous subtitles");
        if (mode != "collision") plan = plan with { Request = plan.Request with
            { AudioOptions = new ConversionOptions { TargetFormat = "wav", Overwrite = "always" } } };
        var manager = mode == "missing-module"
            ? new ModuleManager(new ModulePaths(Path.Combine(workspace.DirectoryPath, "absent")), new Download())
            : await Manager(workspace);
        var engine = Engine(mode);
        var result = await new AudioTranscriptionService(manager, engine.Object, Converter, Converter)
            .TranscribeAsync(plan, Ffmpeg, default);
        Assert.Equal(expected, result.ExitCode);
        Assert.Empty(result.Outputs);
        Assert.Equal("previous subtitles", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetFiles(plan.Request.OutputDirectory, ".odconv-*"));
        if (expected is 1 or 2) engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AtomicExport_CancellationPreservesDestinationAndRemovesPartial()
    {
        using var workspace = new AudioWorkspace();
        var path = Path.Combine(workspace.DirectoryPath, "previous.txt");
        await File.WriteAllTextAsync(path, "previous");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TranscriptFormat.ExportAsync("new", path, true, new CancellationToken(true)));
        Assert.Equal("previous", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(workspace.DirectoryPath, ".odconv-*"));
    }

    [Fact]
    public async Task LateExportFailure_ReportsTheAlreadyFinalizedTranscript()
    {
        using var workspace = new AudioWorkspace();
        var plan = await Plan(workspace, false, "fr");
        var destination = ProcessingConventions.OutputPath(plan.Request, ExportKind.Subtitles);
        var engine = Engine();
        engine.Setup(value => value.RunAsync(It.IsAny<ModuleContext>(), "runtime/Release/whisper-cli.exe",
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<ModuleContext, string, IReadOnlyList<string>, TimeSpan, CancellationToken>(async (context, _, _, _, ct) =>
            {
                await File.WriteAllTextAsync(Path.Combine(context.CacheDirectory, "transcription.srt"), ValidSrt, ct);
                Directory.CreateDirectory(destination);
            });
        var result = await new AudioTranscriptionService(await Manager(workspace), engine.Object, Converter, Converter)
            .TranscribeAsync(plan, Ffmpeg, default);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal(ExportKind.Transcript, Assert.Single(result.Outputs).Kind);
        Assert.True(File.Exists(result.Outputs[0].Path));
        Assert.Empty(Directory.GetFiles(plan.Request.OutputDirectory, ".odconv-*"));
    }

    [Fact]
    public async Task DelayedVideoTrack_WhisperReceivesSilenceAndKeepsPresentationRelativeCues()
    {
        using var workspace = new AudioWorkspace();
        var plan = await Plan(workspace, false, "fr");
        var video = Path.Combine(workspace.DirectoryPath, "video multipiste é.mkv");
        var encoded = await Converter.RunAsync(Ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i",
            "color=c=black:s=160x90:r=10:d=5", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo:d=5",
            "-itsoffset", "0.5", "-i", plan.Request.InputPath, "-map", "0:v", "-map", "1:a", "-map", "2:a",
            "-c:v", "ffv1", "-c:a", "pcm_f32le", video], default);
        Assert.Equal(0, encoded.ExitCode);
        var source = (await new FfprobeService().ProbeAsync(video, Path.Combine(Tools, "ffprobe.exe"), default)).Source!;
        plan = new ProcessingPlan(plan.Request with { InputPath = video, AudioStreamIndex = 2 }, source, source.SelectAudioTrack(2));
        var engine = Engine();
        engine.Setup(value => value.RunAsync(It.IsAny<ModuleContext>(), "runtime/Release/whisper-cli.exe",
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<ModuleContext, string, IReadOnlyList<string>, TimeSpan, CancellationToken>(async (context, _, _, _, ct) =>
            {
                var raw = Path.Combine(context.CacheDirectory, "whisper.f32");
                Assert.Equal(5 * 16000, PcmLength.Measure(raw, 1));
                var bytes = await File.ReadAllBytesAsync(raw, ct);
                Assert.All(bytes.Take(7900 * 4), value => Assert.Equal((byte)0, value));
                Assert.All(bytes.Skip(57000 * 4), value => Assert.Equal((byte)0, value));
                await File.WriteAllTextAsync(Path.Combine(context.CacheDirectory, "transcription.srt"), ValidSrt, ct);
            });
        var result = await new AudioTranscriptionService(await Manager(workspace), engine.Object, Converter, Converter)
            .TranscribeAsync(plan, Ffmpeg, default);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ValidSrt, await File.ReadAllTextAsync(result.Outputs.Single(output => output.Kind == ExportKind.Subtitles).Path));
    }

    private static Mock<IModuleProcessRunner> Engine(string mode = "success")
    {
        var engine = new Mock<IModuleProcessRunner>();
        engine.Setup(value => value.RunAsync(It.IsAny<ModuleContext>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<ModuleContext, string, IReadOnlyList<string>, TimeSpan, CancellationToken>(async (context, executable, args, _, ct) =>
            {
                if (executable == "runtime/python.exe")
                {
                    Directory.CreateDirectory(args[5]);
                    File.Copy(args[4], Path.Combine(args[5], "vocals.wav"));
                    File.Copy(args[4], Path.Combine(args[5], "background.wav"));
                    return;
                }
                if (mode == "cancel") throw new OperationCanceledException();
                if (mode == "failure") throw new IOException("engine failed");
                if (mode == "missing-output") return;
                Assert.False(Path.IsPathRooted(args[1]));
                Assert.Equal("whisper.wav", args[3]);
                await File.WriteAllTextAsync(Path.Combine(context.CacheDirectory, "transcription.srt"),
                    mode == "invalid-output" ? "incomplete" : ValidSrt, ct);
            });
        return engine;
    }

    private static async Task<ProcessingPlan> Plan(AudioWorkspace workspace, bool combined, string language, bool silence = false)
    {
        var input = Path.Combine(workspace.DirectoryPath, "source é.wav");
        var result = await Converter.RunAsync(Ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i",
            silence ? "anullsrc=r=44100:cl=mono:d=3" : "sine=frequency=440:sample_rate=44100:duration=3",
            "-c:a", "pcm_f32le", input], default);
        Assert.Equal(0, result.ExitCode);
        var request = ProcessCommand.ParseArguments([input, "--action", combined ? "separate-transcribe" : "transcribe",
            "--language", language, "--output-dir", Path.Combine(workspace.DirectoryPath, "exports é")]);
        var source = (await new FfprobeService().ProbeAsync(input, Path.Combine(Tools, "ffprobe.exe"), default)).Source!;
        return new ProcessingPlan(request, source, source.SelectAudioTrack(null));
    }

    private static async Task<ModuleManager> Manager(AudioWorkspace workspace)
    {
        var manager = new ModuleManager(new ModulePaths(Path.Combine(workspace.DirectoryPath, "private modules é")),
            new Download(), [new Recipe(ModuleId.Transcription), new Recipe(ModuleId.Separation)]);
        await manager.InstallAsync(ModuleId.Transcription, false, null, default);
        await manager.InstallAsync(ModuleId.Separation, false, null, default);
        return manager;
    }

    private sealed class Download : IModuleDownloader
    {
        public Task DownloadAsync(ModuleDownload download, string destination, IProgress<ModuleProgress>? progress, CancellationToken ct) =>
            File.WriteAllBytesAsync(destination, [1], ct);
    }

    private sealed class Recipe(ModuleId id) : IModuleRecipe
    {
        public ModuleId Id => id;
        public string Version => "fixture";
        public IReadOnlyList<ModuleDownload> Downloads => [new(new Uri("https://example.invalid/engine"),
            Convert.ToHexString(SHA256.HashData(new byte[] { 1 })), 1, "runtime/engine.exe", false)];
        public IReadOnlyList<string> RequiredFiles => ["runtime/engine.exe"];
        public Task ProvisionAsync(ModuleContext context, CancellationToken ct) => Task.CompletedTask;
        public Task CheckAsync(ModuleContext context, CancellationToken ct) => Task.CompletedTask;
    }
}
