namespace OD.Converter.Services.Modules;

/// <summary>Constrains module operations to the application's private per-user workspace.</summary>
public sealed class ModulePaths
{
    /// <summary>Creates paths without touching the filesystem; an explicit root supports isolated tests.</summary>
    public ModulePaths(string? root = null)
    {
        Root = Path.GetFullPath(root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OD.Converter"));
    }

    /// <summary>Gets the application-owned root.</summary>
    public string Root { get; }

    /// <summary>Gets a stable module identifier safe for use as a directory name.</summary>
    public static string Name(ModuleId id) => id switch
    {
        ModuleId.Separation => "separation",
        ModuleId.Transcription => "transcription",
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };

    /// <summary>Gets the private module directory.</summary>
    public string ModuleDirectory(ModuleId id) => Child(Root, $"modules/{Name(id)}");

    /// <summary>Gets the per-module mutation lock, kept outside replaceable payload directories.</summary>
    public string LockFile(ModuleId id) => Child(Root, $"modules/{Name(id)}.lock");

    /// <summary>Resolves a relative descendant and refuses rooted paths, traversal, streams and linked ancestors.</summary>
    public static string Child(string parent, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') ||
            relative.Split('/', '\\').Any(part => part is ".." or "."))
            throw new InvalidDataException($"Unsafe module path: {relative}");

        var fullParent = Path.GetFullPath(parent);
        var result = Path.GetFullPath(Path.Combine(fullParent, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(Path.TrimEndingDirectorySeparator(fullParent) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Path leaves the private module directory: {relative}");
        EnsureNoLinks(result);
        return result;
    }

    /// <summary>Rejects reparse points before reading, writing or recursively deleting private paths.</summary>
    public static void EnsureNoLinks(string path)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current is not null; current = current.Parent)
        {
            if ((File.Exists(current.FullName) || Directory.Exists(current.FullName)) &&
                (File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Linked module paths are not allowed: {current.FullName}");
        }
    }

    /// <summary>Deletes a validated private descendant after checking the whole tree for links.</summary>
    public static void DeleteTree(string parent, string relative)
    {
        var path = Child(parent, relative);
        if (!Directory.Exists(path)) return;
        ValidateTree(path);
        Directory.Delete(path, recursive: true);
    }

    /// <summary>Checks each directory before descending so traversal never follows a junction.</summary>
    public static void ValidateTree(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureNoLinks(path);
        ValidateDescendants(path, ct);
    }

    private static void ValidateDescendants(string path, CancellationToken ct)
    {
        foreach (var child in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            ct.ThrowIfCancellationRequested();
            var attributes = child.Attributes;
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Linked module paths are not allowed: {child.FullName}");
            if ((attributes & FileAttributes.Directory) != 0) ValidateDescendants(child.FullName, ct);
        }
    }
}
