using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using OD.Converter.Commands;
using OD.Converter.Services.Modules;
using Xunit;

namespace OD.Converter.Tests;

public sealed class ModuleManagerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "OD.Converter.Tests", "modules-" + Guid.NewGuid().ToString("N"));
    private ModulePaths Paths => new(root);

    [Fact]
    public async Task EngineLease_PreventsRepairAndUninstallAndReleasesAfterDispose()
    {
        var recipe = new FakeRecipe(ModuleId.Separation);
        var manager = new ModuleManager(Paths, new FakeDownloader(), [recipe]);
        await manager.InstallAsync(recipe.Id, false, null, default);
        using (var lease = await manager.OpenAsync(recipe.Id, default))
        {
            Assert.True(File.Exists(Path.Combine(lease.Context.PayloadDirectory, "runtime/engine.exe")));
            await Assert.ThrowsAsync<IOException>(() => manager.UninstallAsync(recipe.Id, default));
            await Assert.ThrowsAsync<IOException>(() => manager.InstallAsync(recipe.Id, true, null, default));
            await Assert.ThrowsAsync<ModuleUnavailableException>(() => manager.OpenAsync(recipe.Id, default));
        }
        await manager.UninstallAsync(recipe.Id, default);
        await Assert.ThrowsAsync<ModuleUnavailableException>(() => manager.OpenAsync(recipe.Id, default));
    }

    [Fact]
    public async Task ReadOnlyCommandsAndUnavailableRecipes_DoNotCreateDirectoriesOrDownload()
    {
        var downloader = new FakeDownloader();
        var manager = new ModuleManager(Paths, downloader);
        var command = new ModulesCommand(manager);
        Assert.Equal(0, await command.RunAsync(["list"], CancellationToken.None));
        Assert.Equal(0, await command.RunAsync(["status", "transcription"], CancellationToken.None));
        Assert.Equal(2, await command.RunAsync(["verify", "transcription"], CancellationToken.None));
        Assert.Equal(2, await command.RunAsync(["install", "separation"], CancellationToken.None));
        Assert.Equal(2, await command.RunAsync(["repair", "transcription"], CancellationToken.None));
        Assert.Equal(0, await command.RunAsync(["uninstall", "transcription"], CancellationToken.None));
        Assert.False(Directory.Exists(root));
        Assert.Equal(0, downloader.Calls);
    }

    [Fact]
    public async Task InstallVerifyCorruptionAndRepair_UseCompleteManifestAndFunctionalCheck()
    {
        var recipe = new FakeRecipe(ModuleId.Separation);
        var downloader = new FakeDownloader();
        var manager = new ModuleManager(Paths, downloader, [recipe]);
        await manager.InstallAsync(recipe.Id, false, null, CancellationToken.None);
        Assert.Equal(ModuleState.Ready, (await manager.StatusAsync(recipe.Id, CancellationToken.None)).State);
        Assert.True(recipe.Checks >= 2);
        var pointer = await File.ReadAllTextAsync(Path.Combine(Paths.ModuleDirectory(recipe.Id), "active.json"));
        await manager.InstallAsync(recipe.Id, false, null, CancellationToken.None);
        Assert.Equal(1, downloader.Calls);
        await File.WriteAllTextAsync(FindPayload(recipe.Id, "runtime/engine.exe"), "corrupt runtime");
        Assert.Equal(ModuleState.NeedsRepair, (await manager.StatusAsync(recipe.Id, CancellationToken.None)).State);
        await manager.InstallAsync(recipe.Id, true, null, CancellationToken.None);
        Assert.Equal(ModuleState.Ready, (await manager.StatusAsync(recipe.Id, CancellationToken.None)).State);
        Assert.NotEqual(pointer, await File.ReadAllTextAsync(Path.Combine(Paths.ModuleDirectory(recipe.Id), "active.json")));
        Assert.Single(Directory.GetDirectories(Path.Combine(Paths.ModuleDirectory(recipe.Id), "versions")));
    }

    [Fact]
    public async Task FailedRepair_PreservesActivePointerAndWorkingModule()
    {
        var recipe = new FakeRecipe(ModuleId.Separation);
        var manager = new ModuleManager(Paths, new FakeDownloader(), [recipe]);
        await manager.InstallAsync(recipe.Id, false, null, CancellationToken.None);
        var pointerPath = Path.Combine(Paths.ModuleDirectory(recipe.Id), "active.json");
        var before = await File.ReadAllTextAsync(pointerPath);
        recipe.ProvisionError = new IOException("Disk full");
        await Assert.ThrowsAsync<IOException>(() => manager.InstallAsync(recipe.Id, true, null, CancellationToken.None));
        Assert.Equal(before, await File.ReadAllTextAsync(pointerPath));
        var status = await manager.StatusAsync(recipe.Id, CancellationToken.None);
        Assert.Equal(ModuleState.Ready, status.State);
        Assert.Contains("previous installation attempt failed", status.Message);
        Assert.Single(Directory.GetDirectories(Path.Combine(Paths.ModuleDirectory(recipe.Id), "versions")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NetworkFailureOrCancellation_IsRepairableAndNeverActivated(bool cancel)
    {
        var recipe = new FakeRecipe(ModuleId.Transcription);
        var downloader = new FakeDownloader { Error = cancel ? new OperationCanceledException() : new HttpRequestException("Offline") };
        var manager = new ModuleManager(Paths, downloader, [recipe]);
        var command = new ModulesCommand(manager);
        Assert.Equal(cancel ? 130 : 3, await command.RunAsync(["install", "transcription"], CancellationToken.None));
        Assert.Equal(ModuleState.NeedsRepair, (await manager.StatusAsync(recipe.Id, CancellationToken.None)).State);
        Assert.False(File.Exists(Path.Combine(Paths.ModuleDirectory(recipe.Id), "active.json")));
        Assert.Empty(Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories));
        downloader.Error = null;
        await manager.InstallAsync(recipe.Id, true, null, CancellationToken.None);
        Assert.Equal(ModuleState.Ready, (await manager.StatusAsync(recipe.Id, CancellationToken.None)).State);
    }

    [Fact]
    public async Task ProcessInterruptionAndCorruptPointer_CanBeRecoveredByRepair()
    {
        var recipe = new FakeRecipe(ModuleId.Transcription);
        var module = Paths.ModuleDirectory(recipe.Id);
        var abandoned = Path.Combine(module, "versions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(abandoned);
        await File.WriteAllTextAsync(Path.Combine(abandoned, "incomplete"), "partial runtime");
        await File.WriteAllTextAsync(Path.Combine(module, "active.json"), "{broken");
        await File.WriteAllTextAsync(Path.Combine(module, "download-0.partial"), "interrupted transfer");
        var manager = new ModuleManager(Paths, new FakeDownloader(), [recipe]);
        Assert.Equal(ModuleState.NeedsRepair, (await manager.StatusAsync(recipe.Id, CancellationToken.None)).State);
        await manager.InstallAsync(recipe.Id, true, null, CancellationToken.None);
        Assert.False(Directory.Exists(abandoned));
        Assert.Equal(ModuleState.Ready, (await manager.StatusAsync(recipe.Id, CancellationToken.None)).State);
    }

    [Fact]
    public async Task ConcurrentInstall_IsLockedPerModuleAndOtherModuleRemainsIndependent()
    {
        var separation = new FakeRecipe(ModuleId.Separation);
        var transcription = new FakeRecipe(ModuleId.Transcription);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloader = new FakeDownloader
        {
            BeforeDownload = async () => { entered.TrySetResult(); await release.Task; }
        };
        var manager = new ModuleManager(Paths, downloader, [separation, transcription]);
        var installation = manager.InstallAsync(separation.Id, false, null, CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ModuleState.Installing, (await manager.StatusAsync(separation.Id, CancellationToken.None)).State);
            Assert.Equal(ModuleState.NotInstalled, (await manager.StatusAsync(transcription.Id, CancellationToken.None)).State);
            await Assert.ThrowsAsync<IOException>(() => manager.InstallAsync(separation.Id, true, null, CancellationToken.None));
            await Assert.ThrowsAsync<IOException>(() => manager.UninstallAsync(separation.Id, CancellationToken.None));
            var other = new ModuleManager(Paths, new FakeDownloader(), [transcription]);
            await other.InstallAsync(transcription.Id, false, null, CancellationToken.None);
            Assert.Equal(ModuleState.Ready, (await other.StatusAsync(transcription.Id, CancellationToken.None)).State);
        }
        finally { release.TrySetResult(); await installation; }
    }

    [Fact]
    public async Task Uninstall_PreservesOtherModuleSourcesAndExports()
    {
        var separation = new FakeRecipe(ModuleId.Separation);
        var transcription = new FakeRecipe(ModuleId.Transcription);
        var manager = new ModuleManager(Paths, new FakeDownloader(), [separation, transcription]);
        await manager.InstallAsync(separation.Id, false, null, CancellationToken.None);
        await manager.InstallAsync(transcription.Id, false, null, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(root, "source.wav"), "source");
        await File.WriteAllTextAsync(Path.Combine(root, "export.wav"), "export");
        await manager.UninstallAsync(separation.Id, CancellationToken.None);
        await manager.UninstallAsync(separation.Id, CancellationToken.None);
        Assert.Equal(ModuleState.NotInstalled, (await manager.StatusAsync(separation.Id, CancellationToken.None)).State);
        Assert.Equal(ModuleState.Ready, (await manager.StatusAsync(transcription.Id, CancellationToken.None)).State);
        Assert.Equal("source", await File.ReadAllTextAsync(Path.Combine(root, "source.wav")));
        Assert.Equal("export", await File.ReadAllTextAsync(Path.Combine(root, "export.wav")));
    }

    [Fact]
    public async Task FunctionalFailureOrMissingModel_CannotBeReady()
    {
        var recipe = new FakeRecipe(ModuleId.Separation) { CheckError = new IOException("Model cannot be loaded") };
        var manager = new ModuleManager(Paths, new FakeDownloader(), [recipe]);
        await Assert.ThrowsAsync<IOException>(() => manager.InstallAsync(recipe.Id, false, null, CancellationToken.None));
        Assert.Equal(ModuleState.NeedsRepair, (await manager.StatusAsync(recipe.Id, CancellationToken.None)).State);
        recipe.CheckError = null;
        await manager.InstallAsync(recipe.Id, true, null, CancellationToken.None);
        File.Delete(FindPayload(recipe.Id, "models/weights.bin"));
        Assert.Equal(ModuleState.NeedsRepair, (await manager.StatusAsync(recipe.Id, CancellationToken.None)).State);
    }

    [Fact]
    public async Task ChangedInventoryOrManifest_IsRejected()
    {
        var recipe = new FakeRecipe(ModuleId.Separation);
        var manager = new ModuleManager(Paths, new FakeDownloader(), [recipe]);
        await manager.InstallAsync(recipe.Id, false, null, CancellationToken.None);
        var executable = FindPayload(recipe.Id, "runtime/engine.exe");
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(executable)!, "untracked.dll"), "unexpected");
        Assert.Equal(ModuleState.NeedsRepair, (await manager.StatusAsync(recipe.Id, CancellationToken.None)).State);
        await manager.InstallAsync(recipe.Id, true, null, CancellationToken.None);
        await File.WriteAllTextAsync(FindPayload(recipe.Id, "module.json"), "{}");
        Assert.Equal(ModuleState.NeedsRepair, (await manager.StatusAsync(recipe.Id, CancellationToken.None)).State);
    }

    [Theory]
    [InlineData("../escape.exe")]
    [InlineData("runtime/../../escape.exe")]
    [InlineData("C:/outside.exe")]
    [InlineData("runtime/file:stream")]
    public async Task ArchiveTraversal_IsRejectedWithoutWritingOutsidePayload(string entry)
    {
        var bytes = CreateZip(entry);
        var recipe = new FakeRecipe(ModuleId.Transcription, bytes, true);
        var manager = new ModuleManager(Paths, new FakeDownloader(bytes), [recipe]);
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.InstallAsync(recipe.Id, false, null, CancellationToken.None));
        Assert.Equal(ModuleState.NeedsRepair, (await manager.StatusAsync(recipe.Id, CancellationToken.None)).State);
        Assert.Empty(Directory.GetFiles(root, "escape.exe", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PinnedZip_IsExtractedAndActivatedAfterProvisioning()
    {
        var bytes = CreateZip("engine.exe");
        var recipe = new FakeRecipe(ModuleId.Transcription, bytes, true);
        var manager = new ModuleManager(Paths, new FakeDownloader(bytes), [recipe]);
        await manager.InstallAsync(recipe.Id, false, null, CancellationToken.None);
        Assert.Equal(ModuleState.Ready, (await manager.StatusAsync(recipe.Id, CancellationToken.None)).State);
    }

    [Fact]
    public async Task ManagerRechecksDownloaderOutput_BeforeProvisioning()
    {
        var recipe = new FakeRecipe(ModuleId.Separation);
        var manager = new ModuleManager(Paths, new FakeDownloader("bad content"u8.ToArray()), [recipe]);
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.InstallAsync(recipe.Id, false, null, CancellationToken.None));
        Assert.Equal(0, recipe.Checks);
    }

    [Fact]
    public void Environment_IsPrivateCpuOnlyAndDoesNotMutateParent()
    {
        var parentPath = Environment.GetEnvironmentVariable("PATH");
        var context = new ModuleContext(Path.Combine(root, "payload"), Path.Combine(root, "cache"));
        var environment = context.CreateEnvironment();
        Assert.Equal(Path.Combine(context.PayloadDirectory, "runtime"), environment["PATH"]);
        Assert.Equal("-1", environment["CUDA_VISIBLE_DEVICES"]);
        Assert.Equal("1", environment["PYTHONNOUSERSITE"]);
        Assert.False(environment.ContainsKey("PYTHONPATH"));
        Assert.False(environment.ContainsKey("PYTHONHOME"));
        Assert.StartsWith(context.CacheDirectory, environment["TORCH_HOME"]);
        Assert.Equal(parentPath, Environment.GetEnvironmentVariable("PATH"));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task CancellationBeforeInstall_CreatesNothing()
    {
        var manager = new ModuleManager(Paths, new FakeDownloader(), [new FakeRecipe(ModuleId.Separation)]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Equal(130, await new ModulesCommand(manager).RunAsync(["install", "separation"], cts.Token));
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpDownloader_VerifiesHashAndRemovesBadTransfers(bool corrupt)
    {
        Directory.CreateDirectory(root);
        var data = "verified bytes"u8.ToArray();
        var artifact = new ModuleDownload(new Uri("https://example.invalid/pinned.zip"),
            Convert.ToHexString(SHA256.HashData(data)), data.Length, "runtime", true);
        using var client = new HttpClient(new StubHttpHandler(corrupt ? "corrupt bytes!"u8.ToArray() : data));
        var target = Path.Combine(root, "artifact.partial");
        var progress = new RecordingProgress();
        var downloader = new ModuleDownloader(client);
        if (corrupt)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(artifact, target, progress, CancellationToken.None));
            Assert.False(File.Exists(target));
        }
        else
        {
            await downloader.DownloadAsync(artifact, target, progress, CancellationToken.None);
            Assert.Equal(data, await File.ReadAllBytesAsync(target));
            Assert.Equal(data.Length, progress.Last!.BytesReceived);
        }
    }

    [Fact]
    public async Task PrivateProcessRunner_RefusesMissingOrExternalExecutable()
    {
        var runner = new ModuleProcessRunner();
        var context = new ModuleContext(root, Path.Combine(root, "cache"));
        await Assert.ThrowsAsync<InvalidDataException>(() => runner.RunAsync(context, "../python.exe", [], TimeSpan.FromSeconds(1), CancellationToken.None));
        await Assert.ThrowsAsync<FileNotFoundException>(() => runner.RunAsync(context, "runtime/python.exe", [], TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task PrivateProcessRunner_ReportsRealFailureAndTimeout()
    {
        var runtime = Path.Combine(root, "runtime");
        Directory.CreateDirectory(runtime);
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), Path.Combine(runtime, "check.exe"));
        var runner = new ModuleProcessRunner();
        var context = new ModuleContext(root, Path.Combine(root, "private cache"));
        await runner.RunAsync(context, "runtime/check.exe", ["/d", "/c", "exit 0"], TimeSpan.FromSeconds(10), CancellationToken.None);
        var failure = await Assert.ThrowsAsync<IOException>(() => runner.RunAsync(context, "runtime/check.exe",
            ["/d", "/c", "exit 7"], TimeSpan.FromSeconds(10), CancellationToken.None));
        Assert.Contains("code 7", failure.Message);
        var timeout = await Assert.ThrowsAsync<IOException>(() => runner.RunAsync(context, "runtime/check.exe",
            ["/d", "/c", "for /l %i in (1,0,2) do @rem waiting"], TimeSpan.FromMilliseconds(150), CancellationToken.None));
        Assert.Contains("timed out", timeout.Message);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(context, "runtime/check.exe",
            ["/d", "/c", "for /l %i in (1,0,2) do @rem waiting"], TimeSpan.FromSeconds(10), cts.Token));
    }

    [Fact]
    public async Task Uninstall_RejectsJunctionWithoutTouchingExternalFiles()
    {
        var external = Path.Combine(root, "other-software");
        Directory.CreateDirectory(external);
        await File.WriteAllTextAsync(Path.Combine(external, "keep.txt"), "untouched");
        var module = Paths.ModuleDirectory(ModuleId.Separation);
        Directory.CreateDirectory(module);
        var link = Path.Combine(module, "linked-runtime");
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, external }) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        try
        {
            var manager = new ModuleManager(Paths, new FakeDownloader());
            await Assert.ThrowsAsync<InvalidDataException>(() => manager.UninstallAsync(ModuleId.Separation, CancellationToken.None));
            Assert.Equal("untouched", await File.ReadAllTextAsync(Path.Combine(external, "keep.txt")));
        }
        finally { Directory.Delete(link); }
    }

    [Theory]
    [InlineData("http://example.invalid/artifact", 64)]
    [InlineData("https://example.invalid/artifact", 63)]
    public void Downloader_RejectsUnpinnedOrInsecureArtifacts(string url, int digestLength)
    {
        Assert.Throws<InvalidDataException>(() => ModuleDownloader.Validate(
            new ModuleDownload(new Uri(url), new string('a', digestLength), 5, "runtime", true)));
    }

    private string FindPayload(ModuleId id, string relative) => Directory.GetFiles(
        Path.Combine(Paths.ModuleDirectory(id), "versions"), Path.GetFileName(relative), SearchOption.AllDirectories).Single();

    private static byte[] CreateZip(string entry)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
            writer.Write("engine content");
        }
        return stream.ToArray();
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private sealed class FakeRecipe : IModuleRecipe
    {
        public FakeRecipe(ModuleId id, byte[]? data = null, bool zip = false)
        {
            Id = id;
            data ??= FakeDownloader.DefaultData;
            Downloads = [new ModuleDownload(new Uri("https://example.invalid/pinned"), Convert.ToHexString(SHA256.HashData(data)),
                data.Length, zip ? "runtime" : "runtime/engine.exe", zip)];
        }

        public ModuleId Id { get; }
        public string Version => "test-cpu-1";
        public IReadOnlyList<ModuleDownload> Downloads { get; }
        public IReadOnlyList<string> RequiredFiles => ["runtime/engine.exe", "models/weights.bin", "licenses/LICENSE.txt"];
        public int Checks { get; private set; }
        public Exception? ProvisionError { get; set; }
        public Exception? CheckError { get; set; }

        public async Task ProvisionAsync(ModuleContext context, CancellationToken ct)
        {
            if (ProvisionError is not null) throw ProvisionError;
            Directory.CreateDirectory(Path.Combine(context.PayloadDirectory, "models"));
            Directory.CreateDirectory(Path.Combine(context.PayloadDirectory, "licenses"));
            await File.WriteAllTextAsync(Path.Combine(context.PayloadDirectory, "models/weights.bin"), "model fixture", ct);
            await File.WriteAllTextAsync(Path.Combine(context.PayloadDirectory, "licenses/LICENSE.txt"), "license fixture", ct);
        }

        public Task CheckAsync(ModuleContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Checks++;
            if (CheckError is not null) throw CheckError;
            Assert.True(File.Exists(Path.Combine(context.PayloadDirectory, "runtime/engine.exe")));
            Assert.True(File.Exists(Path.Combine(context.PayloadDirectory, "models/weights.bin")));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDownloader(byte[]? data = null) : IModuleDownloader
    {
        public static byte[] DefaultData => "private runtime fixture"u8.ToArray();
        public int Calls { get; private set; }
        public Exception? Error { get; set; }
        public Func<Task>? BeforeDownload { get; set; }

        public async Task DownloadAsync(ModuleDownload download, string destination, IProgress<ModuleProgress>? progress, CancellationToken ct)
        {
            Calls++;
            if (BeforeDownload is not null) await BeforeDownload();
            await File.WriteAllBytesAsync(destination, data ?? DefaultData, ct);
            if (Error is not null) throw Error;
        }
    }

    private sealed class StubHttpHandler(byte[] data) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data), RequestMessage = request });
    }

    private sealed class RecordingProgress : IProgress<ModuleProgress>
    {
        public ModuleProgress? Last { get; private set; }
        public void Report(ModuleProgress value) => Last = value;
    }
}
