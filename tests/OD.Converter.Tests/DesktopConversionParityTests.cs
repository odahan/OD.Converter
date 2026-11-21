using OD.Converter.Models;
using OD.Converter.Services;
using Xunit;

namespace OD.Converter.Tests;

public sealed class DesktopConversionParityTests
{
    [Theory]
    [InlineData("speech", "speech-mono", "speech-mono")]
    [InlineData("speech-MONO", "speech-MONO-mono", "speech-MONO")]
    public void ClassicSuggestionKeepsAnExistingMonoSuffix(string sourceStem, string batchStem, string classicStem)
    {
        using var workspace = new AudioWorkspace();
        var source = Path.Combine(workspace.DirectoryPath, sourceStem + ".wav");
        File.WriteAllText(source, "");
        var service = CreateService(new FixtureRunner());
        var options = new ConversionOptions { TargetFormat = "mp3", Mono = true, DryRun = true };

        var batch = service.Plan(new ConversionRequest([source], options));
        var suggestion = FormatRegistry.DefaultOutputPathMono(source, options.TargetFormat);
        var classic = service.Plan(new ConversionRequest([source], options, suggestion));

        Assert.Equal(batchStem + ".mp3", Path.GetFileName(Assert.Single(batch.Entries).Options.OutputPath));
        Assert.Equal(classicStem + ".mp3", Path.GetFileName(Assert.Single(classic.Entries).Options.OutputPath));
        Assert.Contains("-ac", Assert.Single(classic.Entries).Arguments!);
    }

    [Fact]
    public void RecursivePreviewUsesTheSameGlobAndDoesNotCreateTheOutputDirectory()
    {
        using var workspace = new AudioWorkspace();
        var nested = Directory.CreateDirectory(Path.Combine(workspace.DirectoryPath, "nested")).FullName;
        var source = Path.Combine(nested, "keep.wav");
        File.WriteAllText(source, "");
        File.WriteAllText(Path.Combine(nested, "skip.txt"), "");
        var output = Path.Combine(workspace.DirectoryPath, "destination");

        var plan = CreateService(new FixtureRunner()).Plan(new ConversionRequest([workspace.DirectoryPath],
            new ConversionOptions { TargetFormat = "mp4", DryRun = true }, OutputDirectory: output,
            Pattern: "*.wav", Recursive: true));

        var entry = Assert.Single(plan.Entries);
        Assert.Equal(source, entry.Options.InputPath);
        Assert.EndsWith("keep.m4a", entry.Options.OutputPath);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NewlyCreatedDestinationIsPreservedAndOnlyApprovedFilesCanBeReplaced(bool newConflict)
    {
        using var workspace = new AudioWorkspace();
        var source = Path.Combine(workspace.DirectoryPath, "source.wav");
        var destination = Path.Combine(workspace.DirectoryPath, "output.mp3");
        var next = Path.Combine(workspace.DirectoryPath, "next.mp3");
        File.WriteAllText(source, "input");
        if (!newConflict) File.WriteAllText(destination, "old output");
        var approval = new OverwriteApproval([destination, next]);
        var runner = new FixtureRunner(file =>
        {
            if (newConflict && !File.Exists(destination)) File.WriteAllText(destination, "other process");
            File.WriteAllText(file, "new audio");
        });
        var service = CreateService(runner);
        var options = new ConversionOptions { TargetFormat = "mp3", Overwrite = "always" };
        var firstPlan = service.Plan(new ConversionRequest([source], options, destination));
        var first = await service.ExecuteAsync(firstPlan.Executable, firstPlan.Entries.Single(), default, approval: approval);

        Assert.Equal(!newConflict, first.ExitCode == 0);
        Assert.Equal(newConflict ? "other process" : "new audio", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(workspace.DirectoryPath, ".odconv-*"));
        var nextPlan = service.Plan(new ConversionRequest([source], options, next));
        var second = await service.ExecuteAsync(nextPlan.Executable, nextPlan.Entries.Single(), default, approval: approval);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal("new audio", File.ReadAllText(next));
    }

    private static ConversionService CreateService(IFfmpegRunner runner)
    {
        var files = new OD.Converter.Services.FileSystem();
        var validator = new ConversionOptionsValidator(files);
        return new ConversionService(runner, new FixtureLocator(), files, new FfmpegConverter(validator), validator);
    }

    private sealed class FixtureRunner(Action<string>? write = null) : IFfmpegRunner
    {
        public Task<(int ExitCode, string StandardError)> RunAsync(string executable, IReadOnlyCollection<string> arguments,
            CancellationToken ct)
        {
            write?.Invoke(arguments.Last());
            return Task.FromResult((0, ""));
        }
    }

    private sealed class FixtureLocator : IFfmpegLocator
    {
        public string Resolve(string? explicitPath, string binary = "ffmpeg.exe") => binary;
        public string ResolveFfprobe(string? explicitFfmpegPath) => "ffprobe.exe";
    }
}
