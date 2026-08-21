namespace OD.Converter.Services;

public static class FfmpegLocator
{
    public static string Resolve(string? explicitPath, string binary = "ffmpeg.exe")
    {
        // 1. explicit --ffmpeg
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (File.Exists(explicitPath)) return Path.GetFullPath(explicitPath);
            var dir = explicitPath;
            var candidate = Path.Combine(dir, binary);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            throw new FileNotFoundException($"ffmpeg introuvable : \"{explicitPath}\"");
        }

        // 2. env OD_CONVERTER_FFMPEG
        var env = Environment.GetEnvironmentVariable("OD_CONVERTER_FFMPEG");
        if (!string.IsNullOrWhiteSpace(env))
        {
            if (File.Exists(env)) return env;
            var c = Path.Combine(env, binary);
            if (File.Exists(c)) return c;
        }

        // 3. beside exe -> tools/ffmpeg.exe, ./ffmpeg.exe, ./tools/ffmpeg.exe
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

        // 4. C:\ffmpeg\bin
        var legacy = Path.Combine(@"C:\ffmpeg\bin", binary);
        if (File.Exists(legacy)) return legacy;

        // 5. PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                var c = Path.Combine(dir.Trim(), binary);
                if (File.Exists(c)) return c;
                // also without .exe
                var noExt = Path.Combine(dir.Trim(), Path.GetFileNameWithoutExtension(binary));
                if (File.Exists(noExt)) return noExt;
            }
            catch { }
        }

        throw new FileNotFoundException(
            "ffmpeg introuvable. Vérifiez que tools/ffmpeg.exe est présent ou utilisez --ffmpeg <chemin>");
    }

    public static string ResolveFfprobe(string? explicitFfmpegPath)
    {
        // if explicit ffmpeg given, try sibling ffprobe
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
