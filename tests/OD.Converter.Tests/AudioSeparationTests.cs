using System.Security.Cryptography;
using Moq;
using OD.Converter.Commands;
using OD.Converter.Models;
using OD.Converter.Services;
using OD.Converter.Services.Modules;
using Xunit;

namespace OD.Converter.Tests;

public sealed class AudioSeparationTests
{
    private static readonly string ToolDirectory = FindTools();
    private static string Ffmpeg => Path.Combine(ToolDirectory, "ffmpeg.exe");
    private static string Ffprobe => Path.Combine(ToolDirectory, "ffprobe.exe");
    private static FfmpegConverter Converter => new(new ConversionOptionsValidator(new FileSystem()));

    [Theory]
    [InlineData(22050, 1, false)]
    [InlineData(48000, 2, false)]
    [InlineData(44100, 2, true)]
    public async Task SimulatedEngine_RealEncoding_PreservesMarkersLengthAndSelectedExports(int rate, int channels, bool customEncoding)
    {
        using var workspace = new AudioWorkspace();
        var plan = await CreatePlan(workspace, rate, channels);
        if (customEncoding)
        {
            rate = 48000;
            channels = 1;
            plan = plan with { Request = plan.Request with { AudioOptions = new ConversionOptions
            {
                TargetFormat = "flac", SampleRate = rate, Mono = true, CompressionLevel = 8
            } } };
        }
        var manager = await CreateManager(workspace);
        var engine = CopyingEngine();
        var result = await new AudioSeparationService(manager, engine.Object, Converter, Converter)
            .SeparateAsync(plan, Ffmpeg, default);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(3, result.Outputs.Count);
        foreach (var output in result.Outputs)
        {
            var info = await new FfprobeService().ProbeAsync(output.Path, Ffprobe, default);
            Assert.Equal(rate, info.SampleRate);
            Assert.Equal(channels, info.Channels);
            if (customEncoding) Assert.Equal("flac", info.CodecName);
            var decoded = Path.Combine(workspace.DirectoryPath, output.Kind + ".f32");
            await Run(["-i", output.Path, "-f", "f32le", decoded]);
            Assert.Equal(rate * 3, PcmLength.Measure(decoded, channels));
            var bytes = await File.ReadAllBytesAsync(decoded);
            var samples = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
            foreach (var time in new[] { 0.25, 1.5, 2.75 })
            {
                var frame = (int)(time * rate);
                var peak = Enumerable.Range(frame - 10, 21).MaxBy(index => Math.Abs(samples[index * channels]));
                Assert.InRange(Math.Abs(peak - frame), 0, 1);
                Assert.True(Math.Abs(samples[peak * channels]) > 0.1);
            }
        }
        engine.Verify(value => value.RunAsync(It.IsAny<ModuleContext>(), "runtime/python.exe",
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(Directory.GetFiles(plan.Request.OutputDirectory, ".odconv-*"));
    }

    [Fact]
    public async Task CollisionInLastExport_PreventsEngineAndPreservesExistingFiles()
    {
        using var workspace = new AudioWorkspace();
        var plan = await CreatePlan(workspace, 44100, 1);
        Directory.CreateDirectory(plan.Request.OutputDirectory);
        var destination = ProcessingConventions.OutputPath(plan.Request, ExportKind.Background);
        await File.WriteAllTextAsync(destination, "existing export");
        var engine = new Mock<IModuleProcessRunner>(MockBehavior.Strict);
        var result = await new AudioSeparationService(await CreateManager(workspace), engine.Object, Converter, Converter)
            .SeparateAsync(plan, Ffmpeg, default);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Outputs);
        Assert.Equal("existing export", await File.ReadAllTextAsync(destination));
        engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingModule_ReturnsActionableExitTwoWithoutCreatingModuleDirectories()
    {
        using var workspace = new AudioWorkspace();
        var plan = await CreatePlan(workspace, 44100, 1);
        var paths = new ModulePaths(Path.Combine(workspace.DirectoryPath, "absent"));
        var result = await new AudioSeparationService(new ModuleManager(paths, new FixtureDownload()),
            CopyingEngine().Object, Converter, Converter).SeparateAsync(plan, Ffmpeg, default);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("modules install separation", result.ErrorMessage);
        Assert.False(Directory.Exists(paths.Root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EngineFailureOrCancellation_PreservesOldDestinationAndCleansWorkspace(bool cancel)
    {
        using var workspace = new AudioWorkspace();
        var plan = await CreatePlan(workspace, 44100, 1);
        plan = plan with { Request = plan.Request with { AudioOptions = new ConversionOptions { TargetFormat = "wav", Overwrite = "always" } } };
        Directory.CreateDirectory(plan.Request.OutputDirectory);
        var destination = ProcessingConventions.OutputPath(plan.Request, ExportKind.Vocals);
        await File.WriteAllTextAsync(destination, "previous");
        string? operationDirectory = null;
        var engine = new Mock<IModuleProcessRunner>();
        engine.Setup(value => value.RunAsync(It.IsAny<ModuleContext>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<ModuleContext, string, IReadOnlyList<string>, TimeSpan, CancellationToken>((_, _, args, _, _) =>
            {
                operationDirectory = Path.GetDirectoryName(args[2]);
                return Task.FromException(cancel ? new OperationCanceledException() : new IOException("engine failed"));
            });
        var result = await new AudioSeparationService(await CreateManager(workspace), engine.Object, Converter, Converter)
            .SeparateAsync(plan, Ffmpeg, default);
        Assert.Equal(cancel ? 130 : 3, result.ExitCode);
        Assert.Empty(result.Outputs);
        Assert.Equal("previous", await File.ReadAllTextAsync(destination));
        Assert.NotNull(operationDirectory);
        Assert.False(Directory.Exists(operationDirectory));
    }

    [Fact]
    public async Task TruncatedEngineOutput_IsRejectedBeforeExport()
    {
        using var workspace = new AudioWorkspace();
        var plan = await CreatePlan(workspace, 44100, 2);
        var result = await new AudioSeparationService(await CreateManager(workspace), CopyingEngine(truncate: true).Object,
            Converter, Converter).SeparateAsync(plan, Ffmpeg, default);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains("length differs", result.ErrorMessage);
        Assert.Empty(result.Outputs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateExportFailure_ReportsFinalizedFilesAndPreservesOldUnfinishedExport(bool cancel)
    {
        using var workspace = new AudioWorkspace();
        var plan = await CreatePlan(workspace, 44100, 1);
        plan = plan with { Request = plan.Request with { AudioOptions = new ConversionOptions { TargetFormat = "wav", Overwrite = "always" } } };
        Directory.CreateDirectory(plan.Request.OutputDirectory);
        var vocals = ProcessingConventions.OutputPath(plan.Request, ExportKind.Vocals);
        await File.WriteAllTextAsync(vocals, "old vocals");
        var runner = new FailingExportRunner(cancel);
        var result = await new AudioSeparationService(await CreateManager(workspace), CopyingEngine().Object, runner, Converter)
            .SeparateAsync(plan, Ffmpeg, default);
        Assert.Equal(cancel ? 130 : 3, result.ExitCode);
        Assert.Equal(ExportKind.Original, Assert.Single(result.Outputs).Kind);
        Assert.True(File.Exists(result.Outputs[0].Path));
        Assert.Equal("old vocals", await File.ReadAllTextAsync(vocals));
        Assert.Empty(Directory.GetFiles(plan.Request.OutputDirectory, ".odconv-*"));
    }

    [Fact]
    public async Task RepeatedRequest_RunsEngineAgainWithFreshWorkspaceDespiteExistingExports()
    {
        using var workspace = new AudioWorkspace();
        var plan = await CreatePlan(workspace, 44100, 1);
        plan = plan with { Request = plan.Request with { AudioOptions = new ConversionOptions { TargetFormat = "wav", Overwrite = "always" } } };
        var engine = CopyingEngine();
        var service = new AudioSeparationService(await CreateManager(workspace), engine.Object, Converter, Converter);
        Assert.Equal(0, (await service.SeparateAsync(plan, Ffmpeg, default)).ExitCode);
        Assert.Equal(0, (await service.SeparateAsync(plan, Ffmpeg, default)).ExitCode);
        var operations = engine.Invocations.Select(call => ((IReadOnlyList<string>)call.Arguments[2])[5]).ToArray();
        Assert.Equal(2, operations.Length);
        Assert.Equal(2, operations.Distinct().Count());
        Assert.All(operations, directory => Assert.False(Directory.Exists(directory)));
    }

    private sealed class FailingExportRunner(bool cancel) : IFfmpegRunner
    {
        private int exports;

        public async Task<(int ExitCode, string StandardError)> RunAsync(string ffmpegPath, IReadOnlyCollection<string> args, CancellationToken ct)
        {
            if (Path.GetFileName(args.Last()).StartsWith(".odconv-", StringComparison.Ordinal) && ++exports == 2)
            {
                await File.WriteAllTextAsync(args.Last(), "partial", ct);
                if (cancel) throw new OperationCanceledException();
                return (1, "encoding failure");
            }
            return await Converter.RunAsync(ffmpegPath, args, ct);
        }
    }

    private static Mock<IModuleProcessRunner> CopyingEngine(bool truncate = false)
    {
        var engine = new Mock<IModuleProcessRunner>();
        engine.Setup(value => value.RunAsync(It.IsAny<ModuleContext>(), "runtime/python.exe", It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<ModuleContext, string, IReadOnlyList<string>, TimeSpan, CancellationToken>(async (context, _, args, _, ct) =>
            {
                Assert.Equal(new[] { "-I", "-B" }, args.Take(2));
                Assert.StartsWith(context.PayloadDirectory, args[3]);
                Assert.Contains("weights_only=False", await File.ReadAllTextAsync(args[2], ct));
                Directory.CreateDirectory(args[5]);
                foreach (var name in new[] { "vocals", "background" })
                {
                    var destination = Path.Combine(args[5], name + ".wav");
                    if (truncate) await Run(["-i", args[4], "-t", "0.5", "-c:a", "pcm_f32le", destination]);
                    else File.Copy(args[4], destination);
                }
            });
        return engine;
    }

    private static async Task<ProcessingPlan> CreatePlan(AudioWorkspace workspace, int rate, int channels)
    {
        var raw = Path.Combine(workspace.DirectoryPath, "markers.f32");
        var samples = new float[rate * channels * 3];
        foreach (var time in new[] { 0.25, 1.5, 2.75 })
            for (var channel = 0; channel < channels; channel++) samples[(int)(time * rate) * channels + channel] = 0.75f;
        var bytes = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        await File.WriteAllBytesAsync(raw, bytes);
        var input = Path.Combine(workspace.DirectoryPath, "source avec espaces é.wav");
        await Run(["-f", "f32le", "-ar", rate.ToString(), "-ac", channels.ToString(), "-i", raw, "-c:a", "pcm_f32le", input]);
        var request = ProcessCommand.ParseArguments([input, "--action", "separate", "--exports", "original,vocals,background",
            "--output-dir", Path.Combine(workspace.DirectoryPath, "exports é")]);
        var source = (await new FfprobeService().ProbeAsync(input, Ffprobe, default)).Source!;
        return new ProcessingPlan(request, source, source.SelectAudioTrack(null));
    }

    private static async Task<ModuleManager> CreateManager(AudioWorkspace workspace)
    {
        var manager = new ModuleManager(new ModulePaths(Path.Combine(workspace.DirectoryPath, "private modules é")),
            new FixtureDownload(), [new FixtureRecipe()]);
        await manager.InstallAsync(ModuleId.Separation, false, null, default);
        return manager;
    }

    private sealed class FixtureDownload : IModuleDownloader
    {
        public Task DownloadAsync(ModuleDownload download, string destination, IProgress<ModuleProgress>? progress, CancellationToken ct) =>
            File.WriteAllBytesAsync(destination, [1], ct);
    }

    private sealed class FixtureRecipe : IModuleRecipe
    {
        public ModuleId Id => ModuleId.Separation;
        public string Version => "fixture";
        public IReadOnlyList<ModuleDownload> Downloads => [new(new Uri("https://example.invalid/engine"),
            Convert.ToHexString(SHA256.HashData(new byte[] { 1 })), 1, "runtime/python.exe", false)];
        public IReadOnlyList<string> RequiredFiles => ["runtime/python.exe"];
        public Task ProvisionAsync(ModuleContext context, CancellationToken ct) => Task.CompletedTask;
        public Task CheckAsync(ModuleContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private static async Task Run(IReadOnlyCollection<string> args)
    {
        var result = await Converter.RunAsync(Ffmpeg, new[] { "-nostdin", "-v", "error", "-y" }.Concat(args).ToArray(), default);
        Assert.True(result.ExitCode == 0, result.StandardError);
    }

    private static string FindTools()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "OD.Converter", "tools");
            if (File.Exists(Path.Combine(candidate, "ffmpeg.exe"))) return candidate;
        }
        throw new FileNotFoundException("Bundled FFmpeg is required.");
    }
}
