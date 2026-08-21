namespace OD.Converter.Services;

public sealed record FormatInfo(
    string Key,
    string Extension,
    string Container,
    string Codec,
    bool IsLossless,
    string DefaultBitrate,
    string Description,
    string FfmpegArgs);

public static class FormatRegistry
{
    private static readonly Dictionary<string, FormatInfo> _formats = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mp3"]  = new("mp3",  ".mp3",  "mp3",  "libmp3lame", false, "320k", "MP3 (MPEG Layer 3)", "-c:a libmp3lame -b:a {bitrate}"),
        ["wav"]  = new("wav",  ".wav",  "wav",  "pcm_s16le",  true,  "",     "WAV PCM", "-c:a pcm_s16le"),
        ["flac"] = new("flac", ".flac", "flac", "flac",       true,  "",     "FLAC lossless", "-c:a flac -compression_level {level}"),
        ["ogg"]  = new("ogg",  ".ogg",  "ogg",  "libvorbis",  false, "192k", "Ogg Vorbis", "-c:a libvorbis -q:a {quality}"),
        ["opus"] = new("opus", ".opus", "opus", "libopus",    false, "128k", "Opus", "-c:a libopus -b:a {bitrate}"),
        ["aac"]  = new("aac",  ".aac",  "adts", "aac",        false, "192k", "AAC", "-c:a aac -b:a {bitrate}"),
        ["m4a"]  = new("m4a",  ".m4a",  "mp4",  "aac",        false, "192k", "M4A / AAC (MP4)", "-c:a aac -b:a {bitrate}"),
        ["aiff"] = new("aiff", ".aiff", "aiff", "pcm_s16le",  true,  "",     "AIFF PCM", "-c:a pcm_s16le"),
        ["wma"]  = new("wma",  ".wma",  "asf",  "wmav2",      false, "192k", "WMA", "-c:a wmav2 -b:a {bitrate}"),
        ["wv"]   = new("wv",   ".wv",   "wv",   "wavpack",    true,  "",     "WavPack", "-c:a wavpack"),
        ["alac"] = new("alac", ".m4a",  "mp4",  "alac",       true,  "",     "ALAC (Apple Lossless)", "-c:a alac"),
    };

    // aliases
    private static readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mp4"] = "m4a",
        ["m4b"] = "m4a",
    };

    public static FormatInfo? Get(string key)
    {
        if (key.StartsWith('.')) key = key[1..];
        if (_aliases.TryGetValue(key, out var a)) key = a;
        return _formats.TryGetValue(key, out var f) ? f : null;
    }

    public static bool IsSupported(string key) => Get(key) is not null;

    public static IEnumerable<FormatInfo> All => _formats.Values.OrderBy(f => f.Key);

    public static string SupportedList => string.Join(", ", All.Select(f => f.Key));

    public static string DefaultOutputPath(string inputPath, string targetFormat)
    {
        var fmt = Get(targetFormat);
        var ext = fmt?.Extension ?? "." + targetFormat.TrimStart('.');
        var dir = Path.GetDirectoryName(inputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(inputPath);
        return Path.Combine(dir, $"{name}{ext}");
    }

    public static string DefaultOutputPathMono(string inputPath, string targetFormat)
    {
        var fmt = Get(targetFormat);
        var ext = fmt?.Extension ?? "." + targetFormat.TrimStart('.');
        var dir = Path.GetDirectoryName(inputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(inputPath);
        // avoid double -mono
        if (name.EndsWith("-mono", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(dir, $"{name}{ext}");
        return Path.Combine(dir, $"{name}-mono{ext}");
    }
}
