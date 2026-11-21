namespace OD.Converter.Models;

/// <summary>Indicates how a timeline value was obtained; reported values are not measured alignment.</summary>
public enum TimelineReliability
{
    /// <summary>No usable value was reported.</summary>
    Unknown,
    /// <summary>The container reported the value.</summary>
    Container,
    /// <summary>The value was derived from stream metadata.</summary>
    Streams,
    /// <summary>Container and stream metadata disagree.</summary>
    Inconsistent
}

/// <summary>Describes presentation time without substituting zero for missing timestamps.</summary>
public sealed record PresentationTimeline(
    double? OriginSeconds,
    TimelineReliability OriginReliability,
    double? TargetDurationSeconds,
    TimelineReliability DurationReliability,
    IReadOnlyList<string> Warnings);

/// <summary>Describes one audio stream. StreamIndex is absolute; AudioIndex is its audio-only ordinal.</summary>
public sealed record AudioTrackInfo(
    int StreamIndex,
    int AudioIndex,
    string CodecName,
    string? Language,
    string? Title,
    int SampleRate,
    int Channels,
    string? ChannelLayout,
    int BitsPerSample,
    long BitRate,
    double? StartSeconds,
    double? DurationSeconds)
{
    /// <summary>Returns the signed stream offset relative to presentation, or null when unknown.</summary>
    public double? GetOffsetSeconds(PresentationTimeline timeline) => StartSeconds - timeline.OriginSeconds;
}

/// <summary>Common source metadata for audio and video processing.</summary>
public sealed record MediaSource(
    string FormatName,
    bool HasVideo,
    IReadOnlyList<AudioTrackInfo> AudioTracks,
    PresentationTimeline Timeline)
{
    /// <summary>Selects an absolute FFprobe stream index, defaulting to the first audio stream.</summary>
    public AudioTrackInfo SelectAudioTrack(int? streamIndex = null)
    {
        if (AudioTracks.Count == 0)
            throw new ArgumentException("The source contains no audio track. Choose an audio file or a video with audio.");
        return streamIndex is null ? AudioTracks[0] :
            AudioTracks.FirstOrDefault(track => track.StreamIndex == streamIndex)
            ?? throw new ArgumentException($"Audio stream {streamIndex} was not found. --audio-track expects an absolute audio stream index from probe.");
    }
}
