using OD.Converter.Models;

namespace OD.Converter.Services;

/// <summary>Shares workflow defaults and export naming between CLI and interactive orchestration.</summary>
public static class ProcessingConventions
{
    /// <summary>Returns the exports supported by an action.</summary>
    public static IReadOnlyList<ExportKind> AllowedExports(ProcessingAction action) => action switch
    {
        ProcessingAction.Extract => [ExportKind.Original],
        ProcessingAction.Separate => [ExportKind.Original, ExportKind.Vocals, ExportKind.Background],
        ProcessingAction.Transcribe => [ExportKind.Transcript, ExportKind.Subtitles],
        ProcessingAction.SeparateTranscribe => Enum.GetValues<ExportKind>(),
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    /// <summary>Returns the default exports; the complete soundtrack is opt-in for separation.</summary>
    public static IReadOnlyList<ExportKind> DefaultExports(ProcessingAction action) => action switch
    {
        ProcessingAction.Separate => [ExportKind.Vocals, ExportKind.Background],
        ProcessingAction.SeparateTranscribe => [ExportKind.Vocals, ExportKind.Background, ExportKind.Transcript, ExportKind.Subtitles],
        _ => AllowedExports(action)
    };

    /// <summary>Builds a new workflow filename, leaving legacy conversion naming unchanged.</summary>
    public static string OutputPath(ProcessingRequest request, ExportKind kind)
    {
        if (!request.Exports.Contains(kind)) throw new ArgumentException("The export was not requested.");
        ValidatePrefix(request.OutputPrefix);
        var extension = kind switch
        {
            ExportKind.Transcript => ".txt",
            ExportKind.Subtitles => ".srt",
            _ => FormatRegistry.Get(request.AudioOptions.TargetFormat)?.Extension
                ?? throw new ArgumentException("Unsupported audio export format.")
        };
        return Path.Combine(request.OutputDirectory, $"{request.OutputPrefix}-{kind.ToString().ToLowerInvariant()}{extension}");
    }

    /// <summary>Rejects prefixes that could escape the destination directory or form invalid Windows filenames.</summary>
    public static void ValidatePrefix(string prefix)
    {
        var stem = prefix.Split('.')[0];
        var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        if (string.IsNullOrWhiteSpace(prefix) || prefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || prefix.IndexOfAny(['/', '\\', ':']) >= 0 || prefix.EndsWith('.') || prefix.EndsWith(' ')
            || reserved.Contains(stem, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("--prefix must be a valid filename without directories.");
    }
}
