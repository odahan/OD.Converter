using System.Globalization;
using System.Text.Json;
using OD.Converter.Models;

namespace OD.Converter.Services;

/// <summary>Parses FFprobe metadata without losing unknown or signed presentation timestamps.</summary>
public static class MediaProbeParser
{
    /// <summary>Reads all audio streams and a conservative presentation timeline from FFprobe JSON.</summary>
    public static MediaSource Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var format = Property(root, "format");
        var streams = Property(root, "streams");
        var audio = new List<AudioTrackInfo>();
        var video = new List<JsonElement>();
        if (streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var type = Text(stream, "codec_type");
                if (type == "video" && Number(Property(stream, "disposition"), "attached_pic") != 1)
                    video.Add(stream);
                if (type != "audio") continue;
                var index = Number(stream, "index");
                if (index is null || index < 0 || index > int.MaxValue || index != Math.Truncate(index.Value))
                    throw new ArgumentException("FFprobe returned an audio stream without a valid absolute index.");
                if (audio.Any(track => track.StreamIndex == (int)index.Value))
                    throw new ArgumentException("FFprobe returned duplicate audio stream indexes.");
                var tags = Property(stream, "tags");
                audio.Add(new AudioTrackInfo(
                    (int)index.Value, audio.Count, Text(stream, "codec_name") ?? "unknown",
                    Text(tags, "language"), Text(tags, "title"),
                    Integer(stream, "sample_rate"), Integer(stream, "channels"), Text(stream, "channel_layout"),
                    Integer(stream, "bits_per_sample") is > 0 and var bits ? bits : Integer(stream, "bits_per_raw_sample"),
                    (long)(Number(stream, "bit_rate") is >= 0 and < long.MaxValue and var rate ? rate : 0),
                    Number(stream, "start_time"), Duration(stream)));
            }
        }

        var warnings = new List<string>();
        var origin = Number(format, "start_time");
        var originReliability = origin.HasValue ? TimelineReliability.Container : TimelineReliability.Unknown;
        if (origin is null)
        {
            var starts = audio.Select(track => track.StartSeconds)
                .Concat(video.Select(stream => Number(stream, "start_time")))
                .ToArray();
            if (starts.Length > 0 && starts.All(value => value.HasValue)) origin = starts.Min();
            if (origin.HasValue) originReliability = TimelineReliability.Streams;
        }

        var target = Duration(format);
        var durationReliability = target.HasValue ? TimelineReliability.Container : TimelineReliability.Unknown;
        var referenceEnds = video.Count > 0
            ? video.Select(stream => End(Number(stream, "start_time"), Duration(stream))).ToArray()
            : audio.Select(track => End(track.StartSeconds, track.DurationSeconds)).ToArray();
        var referenceEnd = referenceEnds.Where(value => value.HasValue).Max();
        var referenceDuration = referenceEnd - origin;
        if (target is null && origin.HasValue && referenceEnds.Length > 0 && referenceEnds.All(value => value.HasValue)
            && referenceDuration > 0)
        {
            target = referenceDuration;
            durationReliability = TimelineReliability.Streams;
        }
        if (target.HasValue && referenceDuration.HasValue
            && (referenceDuration.Value - target.Value > 0.1
                || referenceEnds.All(value => value.HasValue) && Math.Abs(target.Value - referenceDuration.Value) > 0.1))
        {
            durationReliability = TimelineReliability.Inconsistent;
            warnings.Add("Container duration and presentation stream endpoints disagree; measure the timeline before validating alignment.");
        }
        if (origin is null) warnings.Add("Presentation origin is unknown; zero has not been assumed.");
        if (target is null) warnings.Add("Presentation duration is unknown; measure decoded media before validating alignment.");
        if (audio.Any(track => track.StartSeconds is null)) warnings.Add("At least one audio stream has an unknown start timestamp.");

        return new MediaSource(Text(format, "format_name") ?? "", video.Count > 0, audio.AsReadOnly(),
            new PresentationTimeline(origin, originReliability, target, durationReliability, warnings.AsReadOnly()));
    }

    private static double? End(double? start, double? duration) => start + duration;

    private static double? Duration(JsonElement element) => Number(element, "duration") is > 0 and var value ? value : null;

    private static int Integer(JsonElement element, string name) =>
        Number(element, name) is >= 0 and <= int.MaxValue and var value ? (int)value : 0;

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static string? Text(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static double? Number(JsonElement element, string name)
    {
        var value = Property(element, name);
        var text = value.ValueKind is JsonValueKind.String or JsonValueKind.Number ? value.ToString() : null;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? number : null;
    }
}
