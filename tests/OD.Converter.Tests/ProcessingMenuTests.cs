using Moq;
using OD.Converter.Commands;
using OD.Converter.Models;
using OD.Converter.Services;
using OD.Converter.Services.Modules;
using Spectre.Console;
using Spectre.Console.Rendering;
using Xunit;

namespace OD.Converter.Tests;

public sealed class ProcessingMenuTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Availability_CoversFourModuleCombinations(bool separation, bool transcription)
    {
        var statuses = new Dictionary<ModuleId, ModuleStatus>
        {
            [ModuleId.Separation] = Status(ModuleId.Separation, separation ? ModuleState.Ready : ModuleState.NotInstalled),
            [ModuleId.Transcription] = Status(ModuleId.Transcription, transcription ? ModuleState.Ready : ModuleState.NotInstalled)
        };
        Assert.Null(ProcessingAvailability.UnavailableReason(ProcessingAction.Extract, statuses));
        Assert.Equal(separation, ProcessingAvailability.UnavailableReason(ProcessingAction.Separate, statuses) is null);
        Assert.Equal(transcription, ProcessingAvailability.UnavailableReason(ProcessingAction.Transcribe, statuses) is null);
        Assert.Equal(separation && transcription, ProcessingAvailability.UnavailableReason(ProcessingAction.SeparateTranscribe, statuses) is null);
    }

    [Theory]
    [InlineData(ModuleState.NotInstalled, "install")]
    [InlineData(ModuleState.NeedsRepair, "repair")]
    [InlineData(ModuleState.Installing, "status")]
    public void Availability_DisablesNonReadyStatesWithActionableCommand(ModuleState state, string command)
    {
        var statuses = new Dictionary<ModuleId, ModuleStatus> { [ModuleId.Separation] = Status(ModuleId.Separation, state) };
        Assert.Contains($"odconv modules {command} separation", ProcessingAvailability.UnavailableReason(ProcessingAction.Separate, statuses));
        Assert.Contains("odconv modules install transcription", ProcessingAvailability.UnavailableReason(ProcessingAction.SeparateTranscribe, statuses));
    }

    [Fact]
    public async Task ExtractionAvailability_DoesNotInspectOrCreateModules()
    {
        using var workspace = new AudioWorkspace();
        var paths = new ModulePaths(Path.Combine(workspace.DirectoryPath, "absent"));
        var downloader = new Mock<IModuleDownloader>(MockBehavior.Strict);
        Assert.Null(await ProcessingAvailability.CheckAsync(ProcessingAction.Extract, new ModuleManager(paths, downloader.Object), default));
        Assert.False(Directory.Exists(paths.Root));
        downloader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Command_LastExportCollisionPreventsAnyEngineCall()
    {
        using var workspace = new AudioWorkspace();
        var input = Path.Combine(workspace.DirectoryPath, "source été & [one].mp4");
        var collision = Path.Combine(workspace.DirectoryPath, "result-subtitles.srt");
        await File.WriteAllTextAsync(input, "fixture");
        await File.WriteAllTextAsync(collision, "keep existing");
        var transcription = new Mock<IAudioTranscriptionService>(MockBehavior.Strict);
        var probe = Probe();
        var command = new ProcessCommand(Locator(), new FileSystem(), probe.Object, transcription: transcription.Object);
        Assert.Equal(1, await command.RunAsync([input, "--action", "separate-transcribe", "--output-dir", workspace.DirectoryPath,
            "--prefix", "result", "--exports", "original,vocals,background,transcript,subtitles"], default));
        Assert.Equal("keep existing", await File.ReadAllTextAsync(collision));
        Assert.False(File.Exists(Path.Combine(workspace.DirectoryPath, "result-original.wav")));
        transcription.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Menu_ExtractsUsingRealFfmpegWithSpecialPathsAndUnavailableActions()
    {
        using var workspace = new AudioWorkspace();
        var input = Path.Combine(workspace.DirectoryPath, "été & [clip] original.wav");
        var converter = new FfmpegConverter(new ConversionOptionsValidator(new FileSystem()));
        var ffmpeg = Locator().Resolve(null);
        var generated = await converter.RunAsync(ffmpeg, ["-nostdin", "-f", "lavfi", "-i", "sine=frequency=440:duration=0.1", "-c:a", "pcm_s16le", input], default);
        Assert.Equal(0, generated.ExitCode);
        var keys = new List<ConsoleKeyInfo> { Key(ConsoleKey.Enter) };
        keys.AddRange(input.Select(character => new ConsoleKeyInfo(character, ConsoleKey.A, false, false, false)));
        keys.Add(Key(ConsoleKey.Enter));
        keys.AddRange(Enumerable.Repeat(Key(ConsoleKey.Enter), 9));
        keys.AddRange([Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter)]);
        var (console, output) = ConsoleWithInput(keys);
        var menu = Menu(workspace.DirectoryPath, console, new FfprobeService());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Assert.Equal(0, await menu.RunAsync(deadline.Token));
        Assert.True(File.Exists(Path.Combine(workspace.DirectoryPath, "été & [clip] original-original.wav")), output.ToString());
        Assert.Contains("Requires separation", output.ToString());
        Assert.Contains("Requires transcription", output.ToString());
        Assert.Contains("Processing completed", output.ToString());
        Assert.False(Directory.Exists(Path.Combine(workspace.DirectoryPath, "modules-root")));
    }

    [Fact]
    public async Task Menu_OptionalModulesAndBackDoNotInstallAnything()
    {
        using var workspace = new AudioWorkspace();
        var (console, output) = ConsoleWithInput([
            Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter),
            Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter),
            Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter)]);
        Assert.Equal(0, await Menu(workspace.DirectoryPath, console, Probe().Object).RunAsync(default));
        Assert.Contains("Not installed", output.ToString());
        Assert.False(Directory.Exists(Path.Combine(workspace.DirectoryPath, "modules-root")));
    }

    [Fact]
    public async Task Menu_UserGuideExtractsAndLaunchesThenReturnsToMainMenu()
    {
        using var workspace = new AudioWorkspace();
        string? opened = null;
        var (console, output) = ConsoleWithInput([
            Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter),
            Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter)]);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        Assert.Equal(0, await Menu(workspace.DirectoryPath, console, Probe().Object,
            new UserGuideService(workspace.DirectoryPath, path => opened = path)).RunAsync(deadline.Token));

        Assert.Equal(Path.Combine(workspace.DirectoryPath, UserGuideService.FileName), opened);
        Assert.True(File.Exists(opened));
        Assert.Contains("User guide opened", output.ToString());
        Assert.Contains("OD.Converter", output.ToString());
        Assert.False(Directory.Exists(Path.Combine(workspace.DirectoryPath, "modules-root")));
    }

    [Fact]
    public async Task Menu_UserGuideFailureIsReportedAndMenuRemainsUsable()
    {
        using var workspace = new AudioWorkspace();
        var (console, output) = ConsoleWithInput([
            Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter),
            Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter)]);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        Assert.Equal(0, await Menu(workspace.DirectoryPath, console, Probe().Object,
            new UserGuideService(workspace.DirectoryPath, _ => throw new System.ComponentModel.Win32Exception("No browser"))).RunAsync(deadline.Token));

        Assert.Contains("Unable to open the user guide", output.ToString());
        Assert.True(File.Exists(Path.Combine(workspace.DirectoryPath, UserGuideService.FileName)));
    }

    [Fact]
    public async Task Menu_SupportBackDoesNotOpenBrowser()
    {
        using var workspace = new AudioWorkspace();
        var keys = Enumerable.Repeat(Key(ConsoleKey.DownArrow), 4).Append(Key(ConsoleKey.Enter))
            .Concat([Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter)])
            .Concat(Enumerable.Repeat(Key(ConsoleKey.DownArrow), 5)).Append(Key(ConsoleKey.Enter));
        var (console, output) = ConsoleWithInput(keys);
        var opened = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        Assert.Equal(0, await Menu(workspace.DirectoryPath, console, Probe().Object,
            openExternalLink: _ => opened = true).RunAsync(deadline.Token));

        Assert.False(opened);
        Assert.Contains("KNHSynths", output.ToString());
        Assert.Contains("voluntary", output.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Menu_SupportUsesHarmoToolsUrlAndReturnsAfterBrowserResult(bool browserFails)
    {
        using var workspace = new AudioWorkspace();
        var keys = Enumerable.Repeat(Key(ConsoleKey.DownArrow), 4)
            .Concat([Key(ConsoleKey.Enter), Key(ConsoleKey.Enter)])
            .Concat(Enumerable.Repeat(Key(ConsoleKey.DownArrow), 5)).Append(Key(ConsoleKey.Enter));
        var (console, output) = ConsoleWithInput(keys);
        string? opened = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        Assert.Equal(0, await Menu(workspace.DirectoryPath, console, Probe().Object, openExternalLink: url =>
        {
            opened = url;
            if (browserFails) throw new System.ComponentModel.Win32Exception("No browser");
        }).RunAsync(deadline.Token));

        Assert.Equal("https://buymeacoffee.com/knhsynths", opened);
        Assert.Contains(browserFails ? "Unable to open Buy Me a Coffee" : "Buy Me a Coffee opened", output.ToString());
        Assert.False(Directory.Exists(Path.Combine(workspace.DirectoryPath, "modules-root")));
    }

    [Fact]
    public async Task Menu_CancellationWhileWaitingForInputReturns130()
    {
        using var workspace = new AudioWorkspace();
        var (console, _) = ConsoleWithInput([]);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Assert.Equal(130, await Menu(workspace.DirectoryPath, console, Probe().Object).RunAsync(deadline.Token));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Menu_DecliningModuleMutationDoesNotCreateFiles(int operationIndex)
    {
        using var workspace = new AudioWorkspace();
        var keys = new List<ConsoleKeyInfo> { Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter), Key(ConsoleKey.Enter) };
        keys.AddRange(Enumerable.Repeat(Key(ConsoleKey.DownArrow), operationIndex));
        keys.AddRange([Key(ConsoleKey.Enter), Key(ConsoleKey.Enter),
            Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter),
            Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter)]);
        var (console, _) = ConsoleWithInput(keys);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.Equal(0, await Menu(workspace.DirectoryPath, console, Probe().Object).RunAsync(deadline.Token));
        Assert.False(Directory.Exists(Path.Combine(workspace.DirectoryPath, "modules-root")));
    }

    private static InteractiveProcessingMenu Menu(string root, IAnsiConsole console, IFfprobeService probe, UserGuideService? guide = null, Action<string>? openExternalLink = null)
    {
        var files = new FileSystem();
        var validator = new ConversionOptionsValidator(files);
        var converter = new FfmpegConverter(validator);
        var modules = new ModuleManager(new ModulePaths(Path.Combine(root, "modules-root")), new Mock<IModuleDownloader>(MockBehavior.Strict).Object);
        return new InteractiveProcessingMenu(Locator(), files, probe,
            new InteractiveConverter(Locator(), converter, probe, files, converter, validator), modules, console, guide, openExternalLink);
    }

    private static IFfmpegLocator Locator()
    {
        var locator = new Mock<IFfmpegLocator>();
        var tools = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/OD.Converter/tools"));
        locator.Setup(value => value.Resolve(null, It.IsAny<string>())).Returns(Path.Combine(tools, "ffmpeg.exe"));
        locator.Setup(value => value.ResolveFfprobe(null)).Returns(Path.Combine(tools, "ffprobe.exe"));
        return locator.Object;
    }

    private static Mock<IFfprobeService> Probe()
    {
        var probe = new Mock<IFfprobeService>();
        var source = MediaProbeParser.Parse("""{"format":{"duration":"1","start_time":"0"},"streams":[{"index":1,"codec_type":"audio","codec_name":"pcm_s16le","sample_rate":"44100","channels":1,"start_time":"0","duration":"1"}]}""");
        probe.Setup(value => value.ProbeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProbeInfo("", "", 0, 0, 0, 0, 0, "") { Source = source });
        return probe;
    }

    private static (IAnsiConsole Console, StringWriter Output) ConsoleWithInput(IEnumerable<ConsoleKeyInfo> keys)
    {
        var queue = new Queue<ConsoleKeyInfo>(keys);
        var output = new StringWriter();
        var actual = AnsiConsole.Create(new AnsiConsoleSettings { Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes, Out = new AnsiConsoleOutput(output) });
        var input = new Mock<IAnsiConsoleInput>();
        input.Setup(value => value.IsKeyAvailable()).Returns(() => queue.Count > 0);
        input.Setup(value => value.ReadKeyAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(async (bool _, CancellationToken ct) =>
            {
                if (queue.TryDequeue(out var key)) return (ConsoleKeyInfo?)key;
                await Task.Delay(Timeout.Infinite, ct);
                return null;
            });
        var console = new Mock<IAnsiConsole>();
        console.SetupGet(value => value.Profile).Returns(actual.Profile);
        console.SetupGet(value => value.Cursor).Returns(actual.Cursor);
        console.SetupGet(value => value.Input).Returns(input.Object);
        console.SetupGet(value => value.ExclusivityMode).Returns(actual.ExclusivityMode);
        console.SetupGet(value => value.Pipeline).Returns(actual.Pipeline);
        console.Setup(value => value.Write(It.IsAny<IRenderable>())).Callback<IRenderable>(actual.Write);
        return (console.Object, output);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new(key == ConsoleKey.Enter ? '\r' : '\0', key, false, false, false);

    private static ModuleStatus Status(ModuleId id, ModuleState state) => new(id, state, "private", "fixture");
}
