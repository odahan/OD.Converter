namespace OD.Converter.Services;

public interface IFfmpegLocator
{
    string Resolve(string? explicitPath, string binary = "ffmpeg.exe");
    string ResolveFfprobe(string? explicitFfmpegPath);
}