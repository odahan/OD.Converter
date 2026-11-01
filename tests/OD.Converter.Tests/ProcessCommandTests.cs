using Moq;
using OD.Converter.Commands;
using OD.Converter.Models;
using OD.Converter.Services;
using Xunit;

namespace OD.Converter.Tests;

public sealed class ProcessCommandTests
{
    [Theory]
    [InlineData("interview.wav")]
    [InlineData("interview.mp4")]
    public void Parse_SharesDefaultsAcrossSourceContainers(string input)
    {
        var request = ProcessCommand.ParseArguments([input, "--action", "extract"]);
        Assert.Equal(ProcessingAction.Extract, request.Action);
        Assert.Null(request.AudioStreamIndex);
        Assert.Equal("auto", request.Language);
        Assert.Equal("wav", request.AudioOptions.TargetFormat);
        Assert.Equal("never", request.AudioOptions.Overwrite);
        Assert.Equal([ExportKind.Original], request.Exports);
        Assert.Equal("interview-original.wav", Path.GetFileName(ProcessingConventions.OutputPath(request, ExportKind.Original)));
    }

    [Fact]
    public void Parse_CombinedAction_UsesSelectedExportsAndEncoding()
    {
        var request = ProcessCommand.ParseArguments(["clip vidéo.mp4", "--action", "separate-transcribe",
            "--audio-track", "3", "--exports", "original,transcript,subtitles", "--language", "fr",
            "--output-dir", "destination with spaces", "--prefix", "interview", "--to", "flac", "--compression", "8"]);
        Assert.Equal(3, request.AudioStreamIndex);
        Assert.Equal("fr", request.Language);
        Assert.Equal(8, request.AudioOptions.CompressionLevel);
        Assert.Equal(Path.Combine("destination with spaces", "interview-original.flac"), ProcessingConventions.OutputPath(request, ExportKind.Original));
        Assert.EndsWith("interview-transcript.txt", ProcessingConventions.OutputPath(request, ExportKind.Transcript));
        Assert.EndsWith("interview-subtitles.srt", ProcessingConventions.OutputPath(request, ExportKind.Subtitles));
    }

    [Theory]
    [InlineData("--audio-track", "-1")]
    [InlineData("--audio-track", "abc")]
    [InlineData("--exports", "vocals")]
    [InlineData("--exports", "original,original")]
    [InlineData("--exports", "")]
    [InlineData("--prefix", "../escape")]
    [InlineData("--prefix", "CON")]
    [InlineData("--overwrite", "yes")]
    [InlineData("--language", "fr")]
    [InlineData("--unknown", "value")]
    public void Parse_RejectsInvalidRequests(string option, string value)
    {
        Assert.Throws<ArgumentException>(() => ProcessCommand.ParseArguments(["clip.mp4", "--action", "extract", option, value]));
    }

    [Fact]
    public async Task Run_RejectsMuteVideoBeforeWorkflowExecution()
    {
        var source = MediaProbeParser.Parse("""{"streams":[{"index":0,"codec_type":"video"}]}""");
        var probe = new Mock<IFfprobeService>();
        var files = new Mock<IFileSystem>();
        var locator = new Mock<IFfmpegLocator>();
        files.Setup(value => value.FileExists("mute.mp4")).Returns(true);
        locator.Setup(value => value.ResolveFfprobe(null)).Returns("ffprobe.exe");
        probe.Setup(value => value.ProbeAsync("mute.mp4", "ffprobe.exe", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProbeInfo("", "", 0, 0, 0, 0, 0, "") { Source = source });
        var command = new ProcessCommand(locator.Object, files.Object, probe.Object);
        Assert.Equal(1, await command.RunAsync(["mute.mp4", "--action", "extract"], CancellationToken.None));
    }

    [Fact]
    public async Task Run_ProbeCancellation_Returns130()
    {
        var probe = new Mock<IFfprobeService>();
        var files = new Mock<IFileSystem>();
        var locator = new Mock<IFfmpegLocator>();
        files.Setup(value => value.FileExists("clip.wav")).Returns(true);
        locator.Setup(value => value.ResolveFfprobe(null)).Returns("ffprobe.exe");
        probe.Setup(value => value.ProbeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        Assert.Equal(130, await new ProcessCommand(locator.Object, files.Object, probe.Object)
            .RunAsync(["clip.wav", "--action", "extract"], CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(130)]
    public async Task Run_SeparationDispatch_PreservesWorkflowExitCode(int exitCode)
    {
        var source = MediaProbeParser.Parse("""{"format":{"duration":"3","start_time":"0"},"streams":[{"index":1,"codec_type":"audio","codec_name":"pcm_s16le","sample_rate":"44100","channels":2,"start_time":"0","duration":"3"}]}""");
        var probe = new Mock<IFfprobeService>();
        var files = new Mock<IFileSystem>();
        var locator = new Mock<IFfmpegLocator>();
        var separator = new Mock<IAudioSeparationService>();
        files.Setup(value => value.FileExists("clip.wav")).Returns(true);
        locator.Setup(value => value.ResolveFfprobe(null)).Returns("ffprobe.exe");
        locator.Setup(value => value.Resolve(null)).Returns("ffmpeg.exe");
        probe.Setup(value => value.ProbeAsync("clip.wav", "ffprobe.exe", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProbeInfo("", "", 0, 0, 0, 0, 0, "") { Source = source });
        separator.Setup(value => value.SeparateAsync(It.Is<ProcessingPlan>(plan => plan.Track.StreamIndex == 1),
            "ffmpeg.exe", It.IsAny<CancellationToken>())).ReturnsAsync(new ProcessingResult(exitCode, [], [], exitCode == 0 ? null : "fixture error"));
        var command = new ProcessCommand(locator.Object, files.Object, probe.Object, separation: separator.Object);
        Assert.Equal(exitCode, await command.RunAsync(["clip.wav", "--action", "separate"], default));
        separator.VerifyAll();
    }

    [Theory]
    [InlineData("install", "separation", 2)]
    [InlineData("verify", "transcription", 2)]
    [InlineData("install", "unknown", 1)]
    [InlineData("remove", "separation", 1)]
    [InlineData("--help", null, 0)]
    public async Task Modules_ValidatesSyntaxAndReportsMissingRecipes(string action, string? module, int expected)
    {
        using var client = new HttpClient();
        var paths = new OD.Converter.Services.Modules.ModulePaths(Path.Combine(Path.GetTempPath(), "OD.Converter.Tests", Guid.NewGuid().ToString("N")));
        var manager = new OD.Converter.Services.Modules.ModuleManager(paths, new OD.Converter.Services.Modules.ModuleDownloader(client));
        Assert.Equal(expected, await new ModulesCommand(manager).RunAsync(module is null ? [action] : [action, module], CancellationToken.None));
        Assert.False(Directory.Exists(paths.Root));
    }

    [Theory]
    [InlineData("transcribe", 0)]
    [InlineData("transcribe", 2)]
    [InlineData("separate-transcribe", 3)]
    [InlineData("separate-transcribe", 130)]
    public async Task Run_TranscriptionDispatch_PreservesWorkflowExitCode(string action, int exitCode)
    {
        var source = MediaProbeParser.Parse("""{"format":{"duration":"3","start_time":"0"},"streams":[{"index":1,"codec_type":"audio","codec_name":"pcm_s16le","sample_rate":"44100","channels":2,"start_time":"0","duration":"3"}]}""");
        var probe = new Mock<IFfprobeService>();
        var files = new Mock<IFileSystem>();
        var locator = new Mock<IFfmpegLocator>();
        var transcription = new Mock<IAudioTranscriptionService>();
        files.Setup(value => value.FileExists("clip.wav")).Returns(true);
        locator.Setup(value => value.ResolveFfprobe(null)).Returns("ffprobe.exe");
        locator.Setup(value => value.Resolve(null)).Returns("ffmpeg.exe");
        probe.Setup(value => value.ProbeAsync("clip.wav", "ffprobe.exe", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProbeInfo("", "", 0, 0, 0, 0, 0, "") { Source = source });
        transcription.Setup(value => value.TranscribeAsync(It.Is<ProcessingPlan>(plan => plan.Track.StreamIndex == 1 && plan.Request.Language == "fr"),
            "ffmpeg.exe", It.IsAny<CancellationToken>())).ReturnsAsync(new ProcessingResult(exitCode, [], [], exitCode == 0 ? null : "fixture error"));
        var command = new ProcessCommand(locator.Object, files.Object, probe.Object, transcription: transcription.Object);
        Assert.Equal(exitCode, await command.RunAsync(["clip.wav", "--action", action, "--language", "fr"], default));
        transcription.VerifyAll();
    }
}
