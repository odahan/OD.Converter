using System.Diagnostics;
using System.Text.Json;
using OD.Converter.Models;

namespace OD.Converter.Services;

/// <summary>
/// Legacy audio summary and complete source metadata returned by FFprobe.
/// </summary>
/// <param name="FormatName">Container format name.</param>
/// <param name="CodecName">Audio codec name.</param>
/// <param name="SampleRate">Sample rate in Hz.</param>
/// <param name="Channels">Number of audio channels.</param>
/// <param name="BitsPerSample">Bit depth (bits per sample).</param>
/// <param name="DurationSeconds">Duration in seconds.</param>
/// <param name="BitRate">Bit rate in bits per second.</param>
/// <param name="RawJson">Raw JSON output from FFprobe.</param>
public sealed record ProbeInfo(
    string FormatName,
    string CodecName,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    double DurationSeconds,
    long BitRate,
    string RawJson)
{
    /// <summary>Gets the complete source metadata; legacy callers may omit this value.</summary>
    public MediaSource? Source { get; init; }
}

/// <summary>
/// Service that invokes FFprobe to extract audio and video source metadata.
/// </summary>
public class FfprobeService : IFfprobeService
{
    /// <inheritdoc />
    public async Task<ProbeInfo?> TryProbeAsync(string input, string ffprobePath, CancellationToken ct = default)
    {
        try { return await ProbeAsync(input, ffprobePath, ct); }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    /// <inheritdoc />
    public async Task<ProbeInfo> ProbeAsync(string input, string ffprobePath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ffprobePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("quiet");
        psi.ArgumentList.Add("-print_format"); psi.ArgumentList.Add("json");
        psi.ArgumentList.Add("-show_format");
        psi.ArgumentList.Add("-show_streams");
        psi.ArgumentList.Add(input);

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        using var registration = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        });
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (proc.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);

        var source = MediaProbeParser.Parse(stdout);
        var first = source.AudioTracks.FirstOrDefault();
        using var document = JsonDocument.Parse(stdout);
        var bitRate = document.RootElement.TryGetProperty("format", out var format)
            && format.TryGetProperty("bit_rate", out var rate) && long.TryParse(rate.ToString(), out var parsedRate) ? parsedRate : 0;
        return new ProbeInfo(source.FormatName, first?.CodecName ?? "", first?.SampleRate ?? 0,
            first?.Channels ?? 0, first?.BitsPerSample ?? 0, source.Timeline.TargetDurationSeconds ?? 0,
            bitRate, stdout) { Source = source };
    }

    /// <inheritdoc />
    public string FormatProbeHuman(ProbeInfo p)
    {
        var dur = TimeSpan.FromSeconds(Math.Min(p.DurationSeconds, TimeSpan.MaxValue.TotalSeconds / 2));
        var durStr = dur.TotalHours >= 1 ? dur.ToString(@"h\:mm\:ss") : dur.ToString(@"m\:ss");
        if (p.Source is not null && p.Source.Timeline.TargetDurationSeconds is null) durStr = "unknown duration";
        var chStr = p.Channels == 1 ? "mono" : p.Channels == 2 ? "stereo" : $"{p.Channels} channels";
        var srStr = p.SampleRate > 0 ? $"{p.SampleRate / 1000.0:0.#} kHz" : "?";
        var bpsStr = p.BitsPerSample > 0 ? $"{p.BitsPerSample} bit" : "";
        return $"{p.CodecName} {srStr} {chStr} {bpsStr} {durStr}".Trim();
    }
}
