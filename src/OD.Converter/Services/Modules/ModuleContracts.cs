namespace OD.Converter.Services.Modules;

/// <summary>Identifies the two independently managed optional modules.</summary>
public enum ModuleId
{
    /// <summary>Demucs source separation.</summary>
    Separation,
    /// <summary>Native Whisper transcription.</summary>
    Transcription
}

/// <summary>Describes availability without inferring readiness from directory existence.</summary>
public enum ModuleState
{
    /// <summary>No installation has been requested.</summary>
    NotInstalled,
    /// <summary>Another process owns the module mutation lock.</summary>
    Installing,
    /// <summary>Integrity and the functional check both passed.</summary>
    Ready,
    /// <summary>An interrupted, incomplete or damaged installation needs attention.</summary>
    NeedsRepair
}

/// <summary>Reports a module state and an actionable explanation.</summary>
public sealed record ModuleStatus(ModuleId Id, ModuleState State, string Location, string Message);

/// <summary>Reports real transfer counts, without fabricating a percentage.</summary>
public sealed record ModuleProgress(string Stage, long BytesReceived = 0, long? TotalBytes = null);

/// <summary>Describes one immutable HTTPS artifact pinned by the application's recipe.</summary>
public sealed record ModuleDownload(Uri Url, string Sha256, long SizeBytes, string Destination, bool IsZip);

/// <summary>Provides the private payload and cache paths for an engine adapter.</summary>
public sealed record ModuleContext(string PayloadDirectory, string CacheDirectory)
{
    /// <summary>Builds an isolated environment; no global Python, pip, model or user cache is inherited.</summary>
    public IReadOnlyDictionary<string, string> CreateEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "SystemRoot", "WINDIR" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is not null) environment[name] = value;
        }

        environment["PATH"] = Path.Combine(PayloadDirectory, "runtime");
        environment["HOME"] = CacheDirectory;
        environment["USERPROFILE"] = CacheDirectory;
        environment["LOCALAPPDATA"] = CacheDirectory;
        environment["APPDATA"] = CacheDirectory;
        environment["TEMP"] = CacheDirectory;
        environment["TMP"] = CacheDirectory;
        environment["XDG_CACHE_HOME"] = CacheDirectory;
        environment["TORCH_HOME"] = Path.Combine(CacheDirectory, "torch");
        environment["HF_HOME"] = Path.Combine(CacheDirectory, "huggingface");
        environment["PIP_CACHE_DIR"] = Path.Combine(CacheDirectory, "pip");
        environment["PIP_CONFIG_FILE"] = "NUL";
        environment["PYTHONNOUSERSITE"] = "1";
        environment["PYTHONDONTWRITEBYTECODE"] = "1";
        environment["HF_HUB_OFFLINE"] = "1";
        environment["CUDA_VISIBLE_DEVICES"] = "-1";
        return environment;
    }
}

/// <summary>Defines a reviewed engine-specific recipe, compiled into the application rather than loaded from user JSON.</summary>
public interface IModuleRecipe
{
    /// <summary>Gets the module managed by this recipe.</summary>
    ModuleId Id { get; }
    /// <summary>Gets the immutable distribution/dependency/model revision.</summary>
    string Version { get; }
    /// <summary>Gets all artifacts, with fixed sizes and SHA-256 values.</summary>
    IReadOnlyList<ModuleDownload> Downloads { get; }
    /// <summary>Gets required runtime, dependency, model and license paths relative to the payload.</summary>
    IReadOnlyList<string> RequiredFiles { get; }
    /// <summary>Configures only the private payload; it must not download unpinned dependencies.</summary>
    Task ProvisionAsync(ModuleContext context, CancellationToken ct);
    /// <summary>Checks runtime, imports, model loading and short execution offline; throws on failure.</summary>
    Task CheckAsync(ModuleContext context, CancellationToken ct);
}

/// <summary>Downloads one pinned artifact to an exclusively owned temporary destination.</summary>
public interface IModuleDownloader
{
    /// <summary>Transfers and verifies content before returning successfully.</summary>
    Task DownloadAsync(ModuleDownload download, string destination, IProgress<ModuleProgress>? progress, CancellationToken ct);
}

/// <summary>Runs only private executables with isolated environment and cancellation.</summary>
public interface IModuleProcessRunner
{
    /// <summary>Executes a private engine, reporting nonzero exit codes and enforcing a deadline.</summary>
    Task RunAsync(ModuleContext context, string relativeExecutable, IReadOnlyList<string> arguments,
        TimeSpan timeout, CancellationToken ct);
}

/// <summary>Signals that an engine's reviewed distribution recipe is not available yet.</summary>
public sealed class ModuleRecipeUnavailableException(ModuleId id) : InvalidOperationException(
    $"This release does not include an installable {id.ToString().ToLowerInvariant()} distribution yet. " +
    "No download was started and existing installations were preserved.");
