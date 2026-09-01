namespace OD.Converter.Services;

/// <summary>
/// Provides audio probing capabilities using FFprobe.
/// </summary>
public interface IFfprobeService
{
    /// <summary>Attempts to probe the input file; returns null on failure.</summary>
    Task<ProbeInfo?> TryProbeAsync(string input, string ffprobePath, CancellationToken ct = default);
    /// <summary>Probes the input file and returns detailed metadata; throws on failure.</summary>
    Task<ProbeInfo> ProbeAsync(string input, string ffprobePath, CancellationToken ct);
    /// <summary>Formats probe information into a human-readable string.</summary>
    string FormatProbeHuman(ProbeInfo p);
}