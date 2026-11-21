namespace OD.Converter.Services;

/// <summary>
/// Locates FFmpeg and FFprobe executables on the system.
/// </summary>
public interface IFfmpegLocator
{
    /// <summary>Resolves the full path to the FFmpeg executable.</summary>
    string Resolve(string? explicitPath, string binary = "ffmpeg.exe");
    /// <summary>Resolves the full path to the FFprobe executable, preferring the sibling of an explicit FFmpeg path.</summary>
    string ResolveFfprobe(string? explicitFfmpegPath);
}