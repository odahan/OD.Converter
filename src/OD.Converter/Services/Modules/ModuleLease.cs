namespace OD.Converter.Services.Modules;

/// <summary>Keeps a verified module generation alive until processing finishes.</summary>
public sealed class ModuleLease(ModuleContext context, IDisposable handle) : IDisposable
{
    /// <summary>Gets the verified private engine paths.</summary>
    public ModuleContext Context { get; } = context;

    /// <summary>Releases the module operation lock.</summary>
    public void Dispose() => handle.Dispose();
}

/// <summary>Reports an unavailable optional engine with explicit recovery commands.</summary>
public sealed class ModuleUnavailableException(ModuleId id, string reason) : InvalidOperationException(
    $"{reason} Run odconv modules install {ModulePaths.Name(id)} (or odconv modules repair {ModulePaths.Name(id)} for a damaged installation).");
