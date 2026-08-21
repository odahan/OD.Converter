namespace OD.Converter.Models;

public sealed class ConversionOptions
{
    public string InputPath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string TargetFormat { get; init; } = string.Empty;
    public string? Bitrate { get; init; }
    public int? Quality { get; init; }
    public int? SampleRate { get; init; }
    public int? Channels { get; init; }
    public bool Mono { get; init; }
    public int? BitDepth { get; init; }
    public int? CompressionLevel { get; init; }
    public string Overwrite { get; init; } = "never";
    public string? FfmpegPath { get; init; }
    public bool DryRun { get; init; }

    public int EffectiveChannels => Mono ? 1 : Channels ?? 0; // 0 = keep source
}

public sealed class ConversionResult
{
    public bool Success { get; init; }
    public string? OutputPath { get; init; }
    public string? ErrorMessage { get; init; }
    public int ExitCode { get; init; }
}
