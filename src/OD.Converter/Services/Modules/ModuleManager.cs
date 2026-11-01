using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace OD.Converter.Services.Modules;

/// <summary>Manages independent, immutable module generations with an atomic activation pointer.</summary>
public sealed class ModuleManager
{
    private readonly ModulePaths paths;
    private readonly IModuleDownloader downloader;
    private readonly IReadOnlyDictionary<ModuleId, IModuleRecipe> recipes;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const long MaximumExpandedBytes = 12L * 1024 * 1024 * 1024;

    /// <summary>Creates a manager; reviewed engine recipes are registered by their respective adapters.</summary>
    public ModuleManager(ModulePaths paths, IModuleDownloader downloader, IEnumerable<IModuleRecipe>? recipes = null)
    {
        this.paths = paths;
        this.downloader = downloader;
        this.recipes = (recipes ?? []).ToDictionary(recipe => recipe.Id);
    }

    /// <summary>Gets the private location, available recipe and pinned transfer volume without creating files.</summary>
    public string Describe(ModuleId id)
    {
        var description = $"{ModulePaths.Name(id)}: {paths.ModuleDirectory(id)}";
        return recipes.TryGetValue(id, out var recipe)
            ? $"{description}; version {recipe.Version}; CPU; download {recipe.Downloads.Sum(item => item.SizeBytes):N0} bytes"
            : $"{description}; no installable distribution included in this release";
    }

    /// <summary>Performs an offline integrity and functional check; absent modules do not create directories.</summary>
    public async Task<ModuleStatus> StatusAsync(ModuleId id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var lockPath = paths.LockFile(id);
            using var readLock = File.Exists(lockPath)
                ? new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None)
                : null;
            return await InspectAsync(id, ct);
        }
        catch (IOException ex) when (IsSharingViolation(ex))
        {
            return Status(id, ModuleState.Installing, "A module operation is in progress. Try again when it finishes.");
        }
        catch (Exception ex) when (IsModuleFailure(ex))
        {
            return Status(id, ModuleState.NeedsRepair, ex.Message);
        }
    }

    /// <summary>Installs a complete generation; repair rebuilds from pinned artifacts without trusting old files.</summary>
    public async Task InstallAsync(ModuleId id, bool repair, IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!recipes.TryGetValue(id, out var recipe)) throw new ModuleRecipeUnavailableException(id);
        ValidateRecipe(recipe);
        foreach (var artifact in recipe.Downloads) ModulePaths.Child(paths.ModuleDirectory(id), artifact.Destination);
        foreach (var file in recipe.RequiredFiles) ModulePaths.Child(paths.ModuleDirectory(id), file);
        using var mutationLock = AcquireLock(id);
        var current = await InspectAsync(id, ct);
        if (!repair && current.State == ModuleState.Ready) return;

        var module = paths.ModuleDirectory(id);
        Directory.CreateDirectory(module);
        string? active;
        try { active = ReadActiveGeneration(module); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { active = null; }
        CleanAbandonedGenerations(module, active);
        var generation = Guid.NewGuid().ToString("N");
        var payload = ModulePaths.Child(module, $"versions/{generation}");
        var cache = ModulePaths.Child(module, "cache");
        var attempt = ModulePaths.Child(module, "attempt.json");
        var activated = false;
        try
        {
            await WriteJsonAsync(attempt, new { recipe.Version, Generation = generation, StartedUtc = DateTimeOffset.UtcNow }, ct);
            Directory.CreateDirectory(payload);
            Directory.CreateDirectory(cache);
            var context = new ModuleContext(payload, cache);
            for (var index = 0; index < recipe.Downloads.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var artifact = recipe.Downloads[index];
                var download = ModulePaths.Child(module, $"download-{index}.partial");
                try
                {
                    await downloader.DownloadAsync(artifact, download, progress, ct);
                    if (new FileInfo(download).Length != artifact.SizeBytes ||
                        !string.Equals(await HashAsync(download, ct), artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Downloaded artifact failed the activation integrity check.");
                    progress?.Report(new ModuleProgress("Unpacking verified distribution"));
                    var destination = ModulePaths.Child(payload, artifact.Destination);
                    if (artifact.IsZip) await ExtractAsync(download, destination, ct);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Move(download, destination);
                    }
                }
                finally
                {
                    File.Delete(download);
                }
            }

            progress?.Report(new ModuleProgress("Configuring private runtime"));
            await recipe.ProvisionAsync(context, ct);
            EnsureRequiredFiles(recipe, payload);
            progress?.Report(new ModuleProgress("Checking runtime, dependencies and model offline"));
            await recipe.CheckAsync(context, ct);
            progress?.Report(new ModuleProgress("Indexing installed files"));
            ModulePaths.ValidateTree(payload, ct);
            var inventory = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await Parallel.ForEachAsync(Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories),
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (file, token) =>
            {
                var relative = Path.GetRelativePath(payload, file).Replace('\\', '/');
                if (relative.Equals("module.json", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The distribution must not supply the manager's module.json.");
                if (!inventory.TryAdd(relative, await HashAsync(file, token))) throw new InvalidDataException("Duplicate module inventory path.");
            });
            var manifest = new ModuleManifest(1, ModulePaths.Name(id), recipe.Version, "cpu",
                inventory.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).ToDictionary());
            await WriteJsonAsync(ModulePaths.Child(payload, "module.json"), manifest, ct);
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ModuleProgress("Activating verified module"));
            var temporaryPointer = ModulePaths.Child(module, "active.json.tmp");
            await WriteJsonAsync(temporaryPointer, new ActiveGeneration(generation), ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporaryPointer, ModulePaths.Child(module, "active.json"), overwrite: true);
            activated = true;
        }
        finally
        {
            if (!activated) TryClean(() => ModulePaths.DeleteTree(module, $"versions/{generation}"));
            TryClean(() => File.Delete(ModulePaths.Child(module, "active.json.tmp")));
            if (activated)
            {
                TryClean(() => File.Delete(attempt));
                TryClean(() => CleanAbandonedGenerations(module, generation));
            }
        }
    }

    /// <summary>Removes only this module and its cache, leaving sources, exports and the other module untouched.</summary>
    public Task UninstallAsync(ModuleId id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(paths.ModuleDirectory(id)) && !File.Exists(paths.LockFile(id))) return Task.CompletedTask;
        using var mutationLock = AcquireLock(id);
        ct.ThrowIfCancellationRequested();
        ModulePaths.DeleteTree(ModulePaths.Child(paths.Root, "modules"), ModulePaths.Name(id));
        return Task.CompletedTask;
    }

    /// <summary>Verifies and locks a generation for the entire engine execution, preventing repair or uninstall.</summary>
    public async Task<ModuleLease> OpenAsync(ModuleId id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(paths.ModuleDirectory(id)))
            throw new ModuleUnavailableException(id, "No module is installed.");
        FileStream handle;
        try { handle = AcquireLock(id); }
        catch (IOException ex) when (ex.InnerException is IOException inner && IsSharingViolation(inner))
        {
            throw new ModuleUnavailableException(id, ex.Message);
        }
        try
        {
            var status = await InspectAsync(id, ct);
            if (status.State != ModuleState.Ready) throw new ModuleUnavailableException(id, status.Message);
            var module = paths.ModuleDirectory(id);
            var payload = ModulePaths.Child(module, $"versions/{ReadActiveGeneration(module)}");
            return new ModuleLease(new ModuleContext(payload, ModulePaths.Child(module, "cache")), handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private async Task<ModuleStatus> InspectAsync(ModuleId id, CancellationToken ct)
    {
        var module = paths.ModuleDirectory(id);
        if (!Directory.Exists(module)) return Status(id, ModuleState.NotInstalled, "No module is installed.");
        try
        {
            var generation = ReadActiveGeneration(module);
            if (generation is null)
                return Status(id, ModuleState.NeedsRepair, "No active generation; installation was interrupted or is incomplete.");
            if (!recipes.TryGetValue(id, out var recipe))
                return Status(id, ModuleState.NeedsRepair, "The installed engine recipe is not supported by this application version.");
            var payload = ModulePaths.Child(module, $"versions/{generation}");
            var manifestPath = ModulePaths.Child(payload, "module.json");
            var manifest = JsonSerializer.Deserialize<ModuleManifest>(await File.ReadAllTextAsync(manifestPath, ct))
                ?? throw new InvalidDataException("Missing module manifest.");
            if (manifest.SchemaVersion != 1 || manifest.Id != ModulePaths.Name(id) || manifest.Version != recipe.Version ||
                manifest.Device != "cpu" || manifest.Files is null || manifest.Files.Count == 0)
                throw new InvalidDataException("Unsupported or incomplete module manifest.");
            EnsureRequiredFiles(recipe, payload);
            ModulePaths.ValidateTree(payload, ct);
            var actualFiles = Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories)
                .Where(file => !file.Equals(manifestPath, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(file => Path.GetRelativePath(payload, file).Replace('\\', '/'), file => file, StringComparer.OrdinalIgnoreCase);
            if (actualFiles.Count != manifest.Files.Count || manifest.Files.Keys.Any(key => !actualFiles.ContainsKey(key)))
                throw new InvalidDataException("Module inventory differs from the manifest.");
            await Parallel.ForEachAsync(manifest.Files, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
                async (entry, token) =>
            {
                if (!string.Equals(await HashAsync(actualFiles[entry.Key], token), entry.Value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Module integrity check failed: {entry.Key}");
            });
            var cache = ModulePaths.Child(module, "cache");
            await recipe.CheckAsync(new ModuleContext(payload, cache), ct);
            var warning = File.Exists(ModulePaths.Child(module, "attempt.json"))
                ? " The previous installation attempt failed; the active generation remains usable. Run repair to clean it up."
                : "";
            return Status(id, ModuleState.Ready, $"Version {recipe.Version}; CPU; integrity and functional checks passed.{warning}");
        }
        catch (Exception ex) when (IsModuleFailure(ex))
        {
            return Status(id, ModuleState.NeedsRepair, $"{ex.Message} Run odconv modules repair {ModulePaths.Name(id)}.");
        }
    }

    private FileStream AcquireLock(ModuleId id)
    {
        var lockPath = paths.LockFile(id);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) when (IsSharingViolation(ex))
        {
            throw new IOException($"Another operation owns the {ModulePaths.Name(id)} module. Try again when it finishes.", ex);
        }
    }

    private static bool IsSharingViolation(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33;

    private static bool IsModuleFailure(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or
        JsonException or InvalidOperationException or System.ComponentModel.Win32Exception;

    private ModuleStatus Status(ModuleId id, ModuleState state, string message) => new(id, state, paths.ModuleDirectory(id), message);

    private static string? ReadActiveGeneration(string module)
    {
        var pointer = ModulePaths.Child(module, "active.json");
        if (!File.Exists(pointer)) return null;
        var active = JsonSerializer.Deserialize<ActiveGeneration>(File.ReadAllText(pointer));
        if (active is null || !Guid.TryParseExact(active.Generation, "N", out _))
            throw new InvalidDataException("Invalid active module generation.");
        return active.Generation;
    }

    private static void ValidateRecipe(IModuleRecipe recipe)
    {
        if (string.IsNullOrWhiteSpace(recipe.Version) || recipe.Downloads.Count == 0 || recipe.RequiredFiles.Count == 0)
            throw new InvalidDataException("A complete versioned module recipe is required.");
        foreach (var artifact in recipe.Downloads) ModuleDownloader.Validate(artifact);
    }

    private static void EnsureRequiredFiles(IModuleRecipe recipe, string payload)
    {
        foreach (var relative in recipe.RequiredFiles)
        {
            var file = ModulePaths.Child(payload, relative);
            if (!File.Exists(file) || new FileInfo(file).Length == 0)
                throw new InvalidDataException($"Required module file is missing or empty: {relative}");
        }
    }

    private static async Task<string> HashAsync(string file, CancellationToken ct)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    private static Task WriteJsonAsync<T>(string path, T value, CancellationToken ct) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), ct);

    private static async Task ExtractAsync(string archivePath, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > 100_000) throw new InvalidDataException("Module archive contains too many entries.");
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            total = checked(total + entry.Length);
            if (total > MaximumExpandedBytes)
                throw new InvalidDataException("Module archive exceeds the extraction limit.");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Module archives cannot contain symbolic links.");
            var target = ModulePaths.Child(destination, entry.FullName);
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) Directory.CreateDirectory(target);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = entry.Open();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, ct);
            }
        }
    }

    private static void CleanAbandonedGenerations(string module, string? active)
    {
        var versions = ModulePaths.Child(module, "versions");
        if (Directory.Exists(versions))
        {
            foreach (var directory in Directory.EnumerateDirectories(versions))
            {
                if (Path.GetFileName(directory) != active) ModulePaths.DeleteTree(versions, Path.GetFileName(directory));
            }
        }
        foreach (var file in Directory.EnumerateFiles(module, "download-*.partial"))
        {
            ModulePaths.EnsureNoLinks(file);
            File.Delete(file);
        }
    }

    private static void TryClean(Action cleanup)
    {
        try { cleanup(); }
        catch (Exception ex) when (IsModuleFailure(ex))
        {
            Console.Error.WriteLine($"Warning: private module cleanup failed: {ex.Message}");
        }
    }

    private sealed record ActiveGeneration(string Generation);
    private sealed record ModuleManifest(int SchemaVersion, string Id, string Version, string Device, Dictionary<string, string> Files);
}
