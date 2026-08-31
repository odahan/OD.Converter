namespace OD.Converter.Services;

public interface IFfmpegRunner
{
    Task<(int ExitCode, string StandardError)> RunAsync(string ffmpegPath, IReadOnlyCollection<string> args, CancellationToken ct);
}