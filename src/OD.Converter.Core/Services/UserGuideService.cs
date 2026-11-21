using System.Diagnostics;

namespace OD.Converter.Services;

/// <summary>Extracts the embedded bilingual guide next to the executable and opens the default browser.</summary>
public sealed class UserGuideService(string? applicationDirectory = null, Action<string>? browserLauncher = null)
{
    /// <summary>Gets the reserved filename of the generated help document.</summary>
    public const string FileName = "odconv-user-guide.html";

    /// <summary>Writes the current embedded guide atomically, keeping an identical existing copy untouched.</summary>
    public string Extract()
    {
        var directory = applicationDirectory ?? ExecutableDirectory();
        var destination = Path.Combine(directory, FileName);
        using var resource = typeof(UserGuideService).Assembly.GetManifestResourceStream("OD.Converter.UserGuide.html")
            ?? throw new InvalidOperationException("The embedded user guide is missing from this build.");
        using var content = new MemoryStream();
        resource.CopyTo(content);
        var bytes = content.ToArray();
        if (File.Exists(destination) && File.ReadAllBytes(destination).AsSpan().SequenceEqual(bytes)) return destination;

        var temporary = Path.Combine(directory, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return destination;
    }

    /// <summary>Extracts the guide before requesting the operating system's default browser.</summary>
    public string Open()
    {
        var path = Extract();
        if (browserLauncher is not null) browserLauncher(path);
        else Process.Start(new ProcessStartInfo(new Uri(Path.GetFullPath(path)).AbsoluteUri) { UseShellExecute = true });
        return path;
    }

    private static string ExecutableDirectory() =>
        Environment.ProcessPath is { } processPath && !Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(processPath)!
            : AppContext.BaseDirectory;
}
