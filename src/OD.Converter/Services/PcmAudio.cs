namespace OD.Converter.Services;

/// <summary>Describes measured, interleaved floating-point PCM on the presentation timeline.</summary>
public sealed record PcmAudio(string Path, int SampleRate, int Channels, long SampleCount)
{
    /// <summary>Gets the expected size of one interleaved sample frame.</summary>
    public int BlockAlign => checked(Channels * sizeof(float));
}

/// <summary>Centralizes PCM frame measurement and tail-only length normalization.</summary>
public static class PcmLength
{
    /// <summary>Rounds a presentation duration to the nearest sample, with halves rounded away from zero.</summary>
    public static long TargetSamples(double durationSeconds, int sampleRate)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0 || sampleRate <= 0)
            throw new ArgumentException("A finite positive duration and sample rate are required.");
        return checked((long)Math.Round(durationSeconds * sampleRate, MidpointRounding.AwayFromZero));
    }

    /// <summary>Measures complete PCM frames, rejecting partial or empty engine outputs.</summary>
    public static long Measure(string path, int channels)
    {
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
        var length = new FileInfo(path).Length;
        var blockAlign = checked(channels * sizeof(float));
        if (length == 0 || length % blockAlign != 0)
            throw new InvalidOperationException("PCM output is empty or contains an incomplete sample frame.");
        return length / blockAlign;
    }

    /// <summary>Pads silence or trims only the tail; this does not repair an internal timing error.</summary>
    public static async Task<PcmAudio> NormalizeAsync(string path, int sampleRate, int channels,
        long targetSamples, CancellationToken ct)
    {
        if (sampleRate <= 0 || targetSamples <= 0) throw new ArgumentOutOfRangeException(nameof(targetSamples));
        var measured = Measure(path, channels);
        var blockAlign = checked(channels * sizeof(float));
        var targetBytes = checked(targetSamples * blockAlign);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous);
        ct.ThrowIfCancellationRequested();
        if (measured > targetSamples) stream.SetLength(targetBytes);
        else
        {
            stream.Position = stream.Length;
            var silence = new byte[81920];
            while (stream.Position < targetBytes)
            {
                var count = (int)Math.Min(silence.Length, targetBytes - stream.Position);
                await stream.WriteAsync(silence.AsMemory(0, count), ct);
            }
        }
        await stream.FlushAsync(ct);
        if (stream.Length != targetBytes) throw new IOException("PCM length normalization failed.");
        return new PcmAudio(path, sampleRate, channels, targetSamples);
    }
}

/// <summary>Maps engine timestamps once, accounting for a segment offset and a measured engine delay.</summary>
public sealed record EngineTimeline
{
    /// <summary>Creates a mapping; whole-file aligned PCM uses zero offset and zero delay.</summary>
    public EngineTimeline(double segmentStartSeconds = 0, double engineDelaySeconds = 0)
    {
        if (!double.IsFinite(segmentStartSeconds) || !double.IsFinite(engineDelaySeconds) || engineDelaySeconds < 0)
            throw new ArgumentException("Timestamp mapping requires finite offsets and a nonnegative measured delay.");
        SegmentStartSeconds = segmentStartSeconds;
        EngineDelaySeconds = engineDelaySeconds;
    }

    /// <summary>Gets the segment start relative to the source presentation origin.</summary>
    public double SegmentStartSeconds { get; }

    /// <summary>Gets the measured delay introduced by the engine, in seconds.</summary>
    public double EngineDelaySeconds { get; }

    /// <summary>Returns presentation-relative time, retaining negative values for explicit clipping by the caller.</summary>
    public double ToPresentationSeconds(double engineSeconds)
    {
        if (!double.IsFinite(engineSeconds)) throw new ArgumentException("Engine timestamp must be finite.");
        return engineSeconds + SegmentStartSeconds - EngineDelaySeconds;
    }
}
