namespace OD.Converter.Services;

/// <summary>
/// Executes FFmpeg processes with the given arguments.
/// </summary>
public interface IFfmpegRunner
{
    /// <summary>Runs FFmpeg with the specified arguments and returns the exit code and standard error output.</summary>
    Task<(int ExitCode, string StandardError)> RunAsync(string ffmpegPath, IReadOnlyCollection<string> args, CancellationToken ct);
}