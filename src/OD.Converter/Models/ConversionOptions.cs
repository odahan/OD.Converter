namespace OD.Converter.Models;

/// <summary>
/// Represents the validated settings for one audio conversion.
/// </summary>
public sealed class ConversionOptions
{
    /// <summary>Gets the source file path.</summary>
    public string InputPath { get; init; } = string.Empty;
    /// <summary>Gets the destination file path.</summary>
    public string OutputPath { get; init; } = string.Empty;
    /// <summary>Gets the requested output format.</summary>
    public string TargetFormat { get; init; } = string.Empty;
    /// <summary>Gets the requested audio bitrate.</summary>
    public string? Bitrate { get; init; }
    /// <summary>Gets the requested variable bitrate quality.</summary>
    public int? Quality { get; init; }
    /// <summary>Gets the requested sample rate.</summary>
    public int? SampleRate { get; init; }
    /// <summary>Gets the requested channel count.</summary>
    public int? Channels { get; init; }
    /// <summary>Gets a value indicating whether mono output is requested.</summary>
    public bool Mono { get; init; }
    /// <summary>Gets the requested PCM bit depth.</summary>
    public int? BitDepth { get; init; }
    /// <summary>Gets the requested FLAC compression level.</summary>
    public int? CompressionLevel { get; init; }
    /// <summary>Gets the overwrite behavior.</summary>
    public string Overwrite { get; init; } = "never";
    /// <summary>Gets the optional custom FFmpeg path.</summary>
    public string? FfmpegPath { get; init; }
    /// <summary>Gets a value indicating whether execution is skipped.</summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// Gets the requested channel count, or zero when the source channel layout is preserved.
    /// </summary>
    public int EffectiveChannels => Mono ? 1 : Channels ?? 0;
}

/// <summary>
/// Represents the result of a conversion operation.
/// </summary>
public sealed class ConversionResult
{
    /// <summary>Gets a value indicating whether the conversion succeeded.</summary>
    public bool Success { get; init; }
    /// <summary>Gets the generated output path.</summary>
    public string? OutputPath { get; init; }
    /// <summary>Gets the error message when conversion failed.</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>Gets the process exit code.</summary>
    public int ExitCode { get; init; }
}
