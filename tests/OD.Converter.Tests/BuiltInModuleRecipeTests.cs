using System.IO.Compression;
using OD.Converter.Services.Modules;
using Xunit;

namespace OD.Converter.Tests;

public sealed class BuiltInModuleRecipeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "OD.Converter.Tests", "recipes é " + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Catalogue_PinsIndependentCpuEnginesDependenciesModelsAndLicenses()
    {
        var recipes = BuiltInModuleRecipes.Create();
        Assert.Equal(2, recipes.Count);
        foreach (var recipe in recipes)
        {
            Assert.Contains("cpu", recipe.Version);
            Assert.Contains(recipe.RequiredFiles, file => file.StartsWith("models/"));
            Assert.Contains(recipe.RequiredFiles, file => file.StartsWith("licenses/"));
            Assert.All(recipe.Downloads, artifact =>
            {
                ModuleDownloader.Validate(artifact);
                Assert.DoesNotContain("/latest", artifact.Url.AbsolutePath);
                Assert.DoesNotContain("cublas", artifact.Url.AbsolutePath);
                Assert.False(Path.IsPathRooted(artifact.Destination));
                ModulePaths.Child(root, artifact.Destination);
            });
        }
        var separation = recipes.Single(recipe => recipe.Id == ModuleId.Separation);
        Assert.Contains(separation.Downloads, item => item.Url.AbsolutePath.Contains("torch-2.6.0%2Bcpu"));
        Assert.Contains(separation.Downloads, item => item.Destination == "models/955717e8-8726e21a.th");
        var transcription = recipes.Single(recipe => recipe.Id == ModuleId.Transcription);
        Assert.DoesNotContain(transcription.Downloads, item => item.Url.AbsolutePath.Contains("python"));
        Assert.Contains(transcription.Downloads, item => item.Url.AbsolutePath.Contains("5359861c739e955e79d9a303bcbc70fb988958b1"));
        Assert.Contains(transcription.Downloads, item => item.Destination == "models/ggml-silero-v5.1.2.bin"
            && item.Url.AbsolutePath.Contains("9ffd54a1e1ee413ddf265af9913beaf518d1639b"));
        Assert.Contains("licenses/silero-vad-LICENSE", transcription.RequiredFiles);
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData(ModuleId.Separation)]
    [InlineData(ModuleId.Transcription)]
    public async Task Provision_UsesLocalReleaseDllsAndOfflinePrivateInstaller(ModuleId id)
    {
        var runner = new RecordingRunner();
        var recipe = BuiltInModuleRecipes.Create(runner).Single(item => item.Id == id);
        var context = CreateContext();
        PrepareVisualCppArchive(context);
        if (id == ModuleId.Transcription)
        {
            var native = ModulePaths.Child(context.PayloadDirectory, "runtime/Release");
            Directory.CreateDirectory(native);
            await File.WriteAllTextAsync(ModulePaths.Child(native, "whisper-cli.exe"), "fixture");
            await File.WriteAllTextAsync(ModulePaths.Child(native, "SDL2.dll"), "unused optional dependency");
            await File.WriteAllTextAsync(ModulePaths.Child(native, "whisper-server.exe"), "unused optional executable");
        }
        await recipe.ProvisionAsync(context, CancellationToken.None);
        var runtime = id == ModuleId.Separation ? "runtime" : "runtime/Release";
        Assert.Equal("release", await File.ReadAllTextAsync(ModulePaths.Child(context.PayloadDirectory, runtime + "/msvcp140.dll")));
        Assert.False(Directory.Exists(ModulePaths.Child(context.PayloadDirectory, "packages")));
        Assert.DoesNotContain(Directory.EnumerateFiles(context.PayloadDirectory, "*", SearchOption.AllDirectories), path => path.Contains("debug_nonredist"));
        Assert.True(File.Exists(ModulePaths.Child(context.PayloadDirectory, "licenses/THIRD-PARTY-NOTICES.txt")));
        if (id == ModuleId.Separation)
        {
            var install = runner.Calls.Single(call => call.Arguments.Contains("install"));
            Assert.Equal("runtime/python.exe", install.Executable);
            Assert.Contains("--no-index", install.Arguments);
            Assert.Contains("--no-deps", install.Arguments);
            Assert.Contains("--no-build-isolation", install.Arguments);
            Assert.Contains("-B", install.Arguments);
            Assert.Equal(ModulePaths.Child(context.PayloadDirectory, "runtime"), install.Arguments[install.Arguments.IndexOf("--prefix") + 1]);
            Assert.Contains(runner.Calls, call => call.Arguments.SequenceEqual(new[] { "-I", "-B", "-m", "pip", "check" }));
        }
        else
        {
            Assert.Empty(runner.Calls);
            Assert.False(File.Exists(ModulePaths.Child(context.PayloadDirectory, "runtime/Release/SDL2.dll")));
            Assert.False(File.Exists(ModulePaths.Child(context.PayloadDirectory, "runtime/Release/whisper-server.exe")));
        }
    }

    [Theory]
    [InlineData(ModuleId.Separation, false)]
    [InlineData(ModuleId.Transcription, false)]
    [InlineData(ModuleId.Transcription, true)]
    public async Task FunctionalCheck_UsesPrivateModelAndCleansTemporaryAudio(ModuleId id, bool fail)
    {
        var runner = new RecordingRunner { Fail = fail };
        var recipe = BuiltInModuleRecipes.Create(runner).Single(item => item.Id == id);
        var context = CreateContext();
        if (fail) await Assert.ThrowsAsync<IOException>(() => recipe.CheckAsync(context, CancellationToken.None));
        else await recipe.CheckAsync(context, CancellationToken.None);
        Assert.Empty(Directory.GetDirectories(context.CacheDirectory));
        var call = Assert.Single(runner.Calls);
        Assert.StartsWith(context.CacheDirectory, call.Context.CacheDirectory);
        Assert.Contains(call.Arguments, arg => Path.GetFullPath(arg, call.Context.CacheDirectory).StartsWith(Path.Combine(context.PayloadDirectory, "models")));
        if (id == ModuleId.Transcription)
        {
            Assert.Contains("-ng", call.Arguments);
            Assert.DoesNotContain(call.Arguments.SelectMany(argument => argument), character => character > 127);
            Assert.Equal(32044, runner.CheckedWavBytes);
        }
        else Assert.Equal("runtime/python.exe", call.Executable);
    }

    private ModuleContext CreateContext()
    {
        var context = new ModuleContext(Path.Combine(root, "payload"), Path.Combine(root, "cache"));
        Directory.CreateDirectory(context.PayloadDirectory);
        Directory.CreateDirectory(Path.Combine(context.PayloadDirectory, "models"));
        Directory.CreateDirectory(context.CacheDirectory);
        return context;
    }

    private static void PrepareVisualCppArchive(ModuleContext context)
    {
        var packages = ModulePaths.Child(context.PayloadDirectory, "packages");
        Directory.CreateDirectory(packages);
        using var archive = ZipFile.Open(Path.Combine(packages, "vc-runtime.zip"), ZipArchiveMode.Create);
        var prefix = "Contents/VC/Redist/MSVC/14.44.35112/x64/";
        foreach (var file in new[] { "msvcp140.dll", "msvcp140_1.dll", "msvcp140_2.dll", "msvcp140_atomic_wait.dll",
            "msvcp140_codecvt_ids.dll", "concrt140.dll", "vcruntime140.dll", "vcruntime140_1.dll", "vcruntime140_threads.dll" })
        {
            using var writer = new StreamWriter(archive.CreateEntry(prefix + "Microsoft.VC143.CRT/" + file).Open());
            writer.Write("release");
        }
        using (var writer = new StreamWriter(archive.CreateEntry(prefix + "Microsoft.VC143.OpenMP/vcomp140.dll").Open())) writer.Write("release");
        using (var writer = new StreamWriter(archive.CreateEntry("debug_nonredist/msvcp140d.dll").Open())) writer.Write("must not be deployed");
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private sealed record Invocation(ModuleContext Context, string Executable, List<string> Arguments);

    private sealed class RecordingRunner : IModuleProcessRunner
    {
        public List<Invocation> Calls { get; } = [];
        public bool Fail { get; init; }
        public long CheckedWavBytes { get; private set; }

        public async Task RunAsync(ModuleContext context, string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
        {
            Calls.Add(new Invocation(context, executable, arguments.ToList()));
            if (executable.EndsWith("whisper-cli.exe"))
            {
                var args = arguments.ToList();
                var input = Path.GetFullPath(args[args.IndexOf("-f") + 1], context.CacheDirectory);
                CheckedWavBytes = new FileInfo(input).Length;
                if (Fail) throw new IOException("Functional check failure");
                await File.WriteAllTextAsync(Path.GetFullPath(args[args.IndexOf("-of") + 1] + ".txt", context.CacheDirectory), "", ct);
            }
            else if (Fail) throw new IOException("Functional check failure");
        }
    }
}
