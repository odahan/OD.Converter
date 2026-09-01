namespace OD.Converter.Services;

/// <summary>Locates FFmpeg and FFprobe executables.</summary>
public class FfmpegLocator : IFfmpegLocator
{
    /// <inheritdoc />
    public string Resolve(string? explicitPath, string binary = "ffmpeg.exe")
    {
        // If an explicit path is given, treat it as either a full file path or a directory.
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (File.Exists(explicitPath)) return Path.GetFullPath(explicitPath);
            var dir = explicitPath;
            var candidate = Path.Combine(dir, binary);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            throw new FileNotFoundException($"FFmpeg was not found: \"{explicitPath}\".");
        }

        // Check environment variable.
        var env = Environment.GetEnvironmentVariable("OD_CONVERTER_FFMPEG");
        if (!string.IsNullOrWhiteSpace(env))
        {
            if (File.Exists(env)) return env;
            var c = Path.Combine(env, binary);
            if (File.Exists(c)) return c;
        }

        // Search well-known locations relative to the application.
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "tools", binary),
            Path.Combine(baseDir, binary),
            Path.Combine(Directory.GetCurrentDirectory(), "tools", binary),
            Path.Combine(Directory.GetCurrentDirectory(), binary),
            Path.Combine(baseDir, "..", "tools", binary),
        };
        foreach (var p in candidates)
        {
            var full = Path.GetFullPath(p);
            if (File.Exists(full)) return full;
        }

        // Fallback to a legacy location.
        var legacy = Path.Combine(@"C:\ffmpeg\bin", binary);
        if (File.Exists(legacy)) return legacy;

        // Finally, search the system PATH.
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                var c = Path.Combine(dir.Trim(), binary);
                if (File.Exists(c)) return c;
                var noExt = Path.Combine(dir.Trim(), Path.GetFileNameWithoutExtension(binary));
                if (File.Exists(noExt)) return noExt;
            }
            catch { }
        }

        throw new FileNotFoundException(
            "FFmpeg was not found. Ensure tools/ffmpeg.exe is present or use --ffmpeg <path>.");
    }

    /// <inheritdoc />
    public string ResolveFfprobe(string? explicitFfmpegPath)
    {
        // Prefer the ffprobe sibling of the explicitly provided ffmpeg path.
        if (!string.IsNullOrWhiteSpace(explicitFfmpegPath))
        {
            try
            {
                var ffmpeg = Resolve(explicitFfmpegPath, "ffmpeg.exe");
                var probeSibling = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
                if (File.Exists(probeSibling)) return probeSibling;
            }
            catch { }
        }
        return Resolve(null, "ffprobe.exe");
    }
}
