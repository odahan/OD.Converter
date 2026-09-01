using System.Diagnostics;
using System.Text.Json;

namespace OD.Converter.Services;

/// <summary>
/// Audio metadata returned by FFprobe.
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
    string RawJson);

/// <summary>
/// Service that invokes FFprobe to extract audio metadata.
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
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        // Extract format-level information
        var formatName = root.TryGetProperty("format", out var fmt) && fmt.TryGetProperty("format_name", out var fn) ? fn.GetString() ?? "" : "";
        var duration = fmt.TryGetProperty("duration", out var d) && double.TryParse(d.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dd) ? dd : 0;
        var bitRate = fmt.TryGetProperty("bit_rate", out var br) && long.TryParse(br.GetString(), out var brr) ? brr : 0;

        // Locate the first audio stream
        string codec = "";
        int sr = 0, ch = 0, bps = 0;
        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in streams.EnumerateArray())
            {
                if (s.TryGetProperty("codec_type", out var ct2) && ct2.GetString() == "audio")
                {
                    codec = s.TryGetProperty("codec_name", out var cn) ? cn.GetString() ?? "" : "";
                    sr = s.TryGetProperty("sample_rate", out var srr) && int.TryParse(srr.GetString(), out var sri) ? sri : sr;
                    ch = s.TryGetProperty("channels", out var chh) ? chh.GetInt32() : ch;
                    bps = s.TryGetProperty("bits_per_sample", out var bpp) ? bpp.GetInt32() : bps;
                    if (bps == 0 && s.TryGetProperty("bits_per_raw_sample", out var bpr)) bps = int.TryParse(bpr.GetString(), out var v) ? v : bps;
                    break;
                }
            }
        }

        return new ProbeInfo(formatName, codec, sr, ch, bps, duration, bitRate, stdout);
    }

    /// <inheritdoc />
    public string FormatProbeHuman(ProbeInfo p)
    {
        var dur = TimeSpan.FromSeconds(p.DurationSeconds);
        var durStr = dur.TotalHours >= 1 ? dur.ToString(@"h\:mm\:ss") : dur.ToString(@"m\:ss");
        var chStr = p.Channels == 1 ? "mono" : p.Channels == 2 ? "stéréo" : $"{p.Channels} canaux";
        var srStr = p.SampleRate > 0 ? $"{p.SampleRate / 1000.0:0.#} kHz" : "?";
        var bpsStr = p.BitsPerSample > 0 ? $"{p.BitsPerSample} bit" : "";
        return $"{p.CodecName} {srStr} {chStr} {bpsStr} {durStr}".Trim();
    }
}