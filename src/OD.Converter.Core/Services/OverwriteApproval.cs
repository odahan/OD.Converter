namespace OD.Converter.Services;

/// <summary>Limits replacement to the versions of files shown in an overwrite confirmation.</summary>
public sealed class OverwriteApproval
{
    private readonly Dictionary<string, FileStamp> files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Captures existing destination files before asking for confirmation.</summary>
    public OverwriteApproval(IEnumerable<string> destinations)
    {
        foreach (var destination in destinations)
        {
            var file = new FileInfo(Path.GetFullPath(destination));
            if (file.Exists) files[file.FullName] = FileStamp.Read(file);
        }
    }

    /// <summary>Gets the files that require explicit confirmation.</summary>
    public IEnumerable<string> Paths => files.Keys;

    /// <summary>Rejects new or changed conflicts and indicates whether the current file can be replaced.</summary>
    public bool CanReplace(string destination)
    {
        var file = new FileInfo(Path.GetFullPath(destination));
        if (!file.Exists) return false;
        if (files.TryGetValue(file.FullName, out var stamp) && stamp == FileStamp.Read(file)) return true;
        throw new IOException($"Output already exists or has changed since review: {file.FullName}");
    }

    private sealed record FileStamp(long Length, DateTime Created, DateTime Modified)
    {
        public static FileStamp Read(FileInfo file) => new(file.Length, file.CreationTimeUtc, file.LastWriteTimeUtc);
    }
}
