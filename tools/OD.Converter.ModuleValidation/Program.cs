using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using OD.Converter.Services.Modules;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Real module validation requires Windows x64.");
    return 1;
}

var resume = args.LastOrDefault() == "--resume";
if (resume) args = args[..^1];
if (args.Length is not (2 or 3) || args[1] is not ("separation" or "transcription" or "both"))
{
    Console.Error.WriteLine("Usage: ModuleValidation <dedicated-root> <separation|transcription|both> [verified-artifact-cache] [--resume]");
    return 1;
}

var root = Path.GetFullPath(args[0]);
var marker = Path.Combine(root, ".module-validation");
const string markerText = "OD.Converter.ModuleValidation/v1";
if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any() &&
    (!resume || !File.Exists(marker) || File.ReadAllText(marker) != markerText))
{
    Console.Error.WriteLine("Validation requires a new directory, or --resume with its dedicated validation marker.");
    return 1;
}

Directory.CreateDirectory(root);
File.WriteAllText(marker, markerText);
Console.WriteLine($"Validation workspace: {root}");
Console.WriteLine($"Administrator token: {new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)}");
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, value) => { value.Cancel = true; cts.Cancel(); };
using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
var paths = new ModulePaths(Path.Combine(root, "private modules with spaces é"));
var downloader = new ValidationCacheDownloader(new ModuleDownloader(client), args.Length == 3 ? Path.GetFullPath(args[2]) : Path.Combine(root, "downloads"));
var recipes = BuiltInModuleRecipes.Create();
var manager = new ModuleManager(paths, downloader, recipes);
Directory.CreateDirectory(paths.Root);
var ids = args[1] switch
{
    "separation" => new[] { ModuleId.Separation },
    "transcription" => new[] { ModuleId.Transcription },
    _ => Enum.GetValues<ModuleId>()
};
var report = new List<object>();
var progress = new ValidationProgress();
await File.WriteAllTextAsync(Path.Combine(paths.Root, "source.wav"), "source sentinel", cts.Token);
await File.WriteAllTextAsync(Path.Combine(paths.Root, "export.wav"), "export sentinel", cts.Token);
try
{
    foreach (var id in ids)
    {
        Console.WriteLine(manager.Describe(id));
        var status = await manager.StatusAsync(id, cts.Token);
        if (status.State != ModuleState.Ready)
        {
            await manager.InstallAsync(id, false, progress, cts.Token);
            status = await manager.StatusAsync(id, cts.Token);
        }
        Console.WriteLine($"Installed: {status}");
        if (status.State != ModuleState.Ready) throw new InvalidDataException(status.Message);
        report.Add(new { Module = id.ToString(), Step = "Installed and verified", Status = status.State.ToString() });
    }

    downloader.AllowNetwork = false;
    foreach (var id in ids)
    {
        var modelName = id == ModuleId.Separation ? "955717e8-8726e21a.th" : "ggml-large-v3-turbo-q5_0.bin";
        var model = Directory.EnumerateFiles(paths.ModuleDirectory(id), modelName, SearchOption.AllDirectories).Single();
        await using (var stream = new FileStream(model, FileMode.Open, FileAccess.Write))
        {
            stream.Position = stream.Length - 1;
            stream.WriteByte(0xA5);
        }
        var damaged = await manager.StatusAsync(id, cts.Token);
        if (damaged.State != ModuleState.NeedsRepair) throw new InvalidDataException("Corruption was not detected.");
        Console.WriteLine($"Damaged: {damaged}");
        report.Add(new { Module = id.ToString(), Step = "Corruption detected", Status = damaged.State.ToString() });
        foreach (var other in ids.Where(other => other != id))
        {
            if ((await manager.StatusAsync(other, cts.Token)).State != ModuleState.Ready)
                throw new InvalidDataException("Corruption affected the other module.");
        }
        await manager.InstallAsync(id, true, progress, cts.Token);
        var repaired = await manager.StatusAsync(id, cts.Token);
        if (repaired.State != ModuleState.Ready) throw new InvalidDataException(repaired.Message);
        Console.WriteLine($"Repaired: {repaired}");
        report.Add(new { Module = id.ToString(), Step = "Repaired from verified cached upstream artifacts", Status = repaired.State.ToString() });
    }

    foreach (var id in ids)
    {
        await manager.UninstallAsync(id, cts.Token);
        var status = await manager.StatusAsync(id, cts.Token);
        if (status.State != ModuleState.NotInstalled) throw new InvalidDataException(status.Message);
        report.Add(new { Module = id.ToString(), Step = "Uninstalled", Status = status.State.ToString() });
        foreach (var other in ids.Where(other => other != id && Directory.Exists(paths.ModuleDirectory(other))))
        {
            if ((await manager.StatusAsync(other, cts.Token)).State != ModuleState.Ready)
                throw new InvalidDataException("Uninstall affected the other module.");
        }
    }

    if (await File.ReadAllTextAsync(Path.Combine(paths.Root, "source.wav"), cts.Token) != "source sentinel" ||
        await File.ReadAllTextAsync(Path.Combine(paths.Root, "export.wav"), cts.Token) != "export sentinel")
        throw new InvalidDataException("Module lifecycle touched sources or exports.");
    await File.WriteAllTextAsync(Path.Combine(root, "results.json"), JsonSerializer.Serialize(report,
        new JsonSerializerOptions { WriteIndented = true }), cts.Token);
    Console.WriteLine("Real module lifecycle validation passed. Sources, exports and other modules were preserved.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    await File.WriteAllTextAsync(Path.Combine(root, "failure.txt"), ex.ToString());
    return 3;
}

sealed class ValidationProgress : IProgress<ModuleProgress>
{
    private string? stage;
    private long bytes;

    public void Report(ModuleProgress value)
    {
        if (stage == value.Stage && value.BytesReceived - bytes < 64 * 1024 * 1024 && value.BytesReceived != value.TotalBytes) return;
        stage = value.Stage;
        bytes = value.BytesReceived;
        Console.WriteLine($"{value.Stage}: {value.BytesReceived} / {value.TotalBytes}");
    }
}

sealed class ValidationCacheDownloader(IModuleDownloader network, string cache) : IModuleDownloader
{
    public bool AllowNetwork { get; set; } = true;

    public async Task DownloadAsync(ModuleDownload artifact, string destination, IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(cache);
        var cached = ModulePaths.Child(cache, artifact.Sha256);
        if (!File.Exists(cached))
        {
            if (!AllowNetwork) throw new IOException("Validation repair attempted an unexpected network access.");
            await network.DownloadAsync(artifact, cached, progress, ct);
        }

        await using (var stream = File.OpenRead(cached))
        {
            if (stream.Length != artifact.SizeBytes ||
                !Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid validation cache artifact.");
        }
        File.Copy(cached, destination, overwrite: false);
    }
}
