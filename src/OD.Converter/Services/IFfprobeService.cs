namespace OD.Converter.Services;

public interface IFfprobeService
{
    Task<ProbeInfo?> TryProbeAsync(string input, string ffprobePath, CancellationToken ct = default);
    Task<ProbeInfo> ProbeAsync(string input, string ffprobePath, CancellationToken ct);
    string FormatProbeHuman(ProbeInfo p);
}