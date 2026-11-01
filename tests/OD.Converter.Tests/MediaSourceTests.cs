using System.Globalization;
using OD.Converter.Models;
using OD.Converter.Services;
using Xunit;

namespace OD.Converter.Tests;

public sealed class MediaSourceTests
{
    [Fact]
    public void Parse_ListsAbsoluteIndexesAndAudioOrdinals_WithSignedOffsetsAndTags()
    {
        var source = MediaProbeParser.Parse("""
            {"format":{"format_name":"matroska","start_time":"-2","duration":"10"},"streams":[
              {"index":0,"codec_type":"video","start_time":"-2","duration":"10"},
              {"index":2,"codec_type":"audio","codec_name":"aac","start_time":"-1.5","duration":"8",
               "sample_rate":"48000","channels":2,"channel_layout":"stereo","bits_per_raw_sample":"24",
               "tags":{"language":"fr","title":"Interview [main]"}},
              {"index":4,"codec_type":"subtitle"},
              {"index":5,"codec_type":"audio","codec_name":"opus","start_time":"-2.5","duration":"10"}]}
            """);
        Assert.True(source.HasVideo);
        Assert.Equal(new[] { 2, 5 }, source.AudioTracks.Select(track => track.StreamIndex));
        Assert.Equal(new[] { 0, 1 }, source.AudioTracks.Select(track => track.AudioIndex));
        var first = source.SelectAudioTrack();
        Assert.Equal("fr", first.Language);
        Assert.Equal("Interview [main]", first.Title);
        Assert.Equal(48000, first.SampleRate);
        Assert.Equal(24, first.BitsPerSample);
        Assert.Equal(0.5, first.GetOffsetSeconds(source.Timeline));
        Assert.Equal(-0.5, source.SelectAudioTrack(5).GetOffsetSeconds(source.Timeline));
        Assert.Throws<ArgumentException>(() => source.SelectAudioTrack(1));
        Assert.Equal(TimelineReliability.Container, source.Timeline.DurationReliability);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"format\":{\"duration\":\"N/A\",\"start_time\":\"NaN\"},\"streams\":[]}")]
    [InlineData("{\"format\":{\"duration\":\"Infinity\",\"start_time\":\"N/A\"}}")]
    public void Parse_DoesNotInventMissingTimestamps(string json)
    {
        var source = MediaProbeParser.Parse(json);
        Assert.Null(source.Timeline.OriginSeconds);
        Assert.Null(source.Timeline.TargetDurationSeconds);
        Assert.Equal(TimelineReliability.Unknown, source.Timeline.DurationReliability);
        Assert.NotEmpty(source.Timeline.Warnings);
    }

    [Fact]
    public void Parse_VideoWithoutAudio_RejectsTrackSelection()
    {
        var source = MediaProbeParser.Parse("""{"streams":[{"index":0,"codec_type":"video"}]}""");
        Assert.True(source.HasVideo);
        Assert.Contains("no audio track", Assert.Throws<ArgumentException>(() => source.SelectAudioTrack()).Message);
    }

    [Fact]
    public void Parse_AudioCoverArt_DoesNotDefineVideoTimeline()
    {
        var source = MediaProbeParser.Parse("""
            {"streams":[{"index":0,"codec_type":"audio","start_time":"2.5","duration":"4"},
            {"index":1,"codec_type":"video","disposition":{"attached_pic":1}}]}
            """);
        Assert.False(source.HasVideo);
        Assert.Equal(2.5, source.Timeline.OriginSeconds);
        Assert.Equal(4, source.Timeline.TargetDurationSeconds);
        Assert.Equal(TimelineReliability.Streams, source.Timeline.DurationReliability);
        Assert.Equal(0, source.SelectAudioTrack().GetOffsetSeconds(source.Timeline));
    }

    [Fact]
    public void Parse_UsesVideoTimeline_WhenAudioEndsEarly()
    {
        var source = MediaProbeParser.Parse("""
            {"streams":[{"index":0,"codec_type":"video","start_time":"0","duration":"10"},
            {"index":1,"codec_type":"audio","start_time":"2","duration":"3"}]}
            """);
        Assert.Equal(10, source.Timeline.TargetDurationSeconds);
        Assert.Equal(2, source.SelectAudioTrack().GetOffsetSeconds(source.Timeline));
    }

    [Fact]
    public void Parse_IncompleteStreamDurations_DoesNotClaimKnownTarget()
    {
        var source = MediaProbeParser.Parse("""
            {"streams":[{"index":0,"codec_type":"audio","start_time":"0","duration":"4"},
            {"index":1,"codec_type":"audio","start_time":"0"}]}
            """);
        Assert.Null(source.Timeline.TargetDurationSeconds);
    }

    [Fact]
    public void Parse_MissingVideoStart_DoesNotUseDelayedAudioAsOrigin()
    {
        var source = MediaProbeParser.Parse("""
            {"streams":[{"index":0,"codec_type":"video","duration":"10"},
            {"index":1,"codec_type":"audio","start_time":"2","duration":"3"}]}
            """);
        Assert.Null(source.Timeline.OriginSeconds);
        Assert.Null(source.SelectAudioTrack().GetOffsetSeconds(source.Timeline));
        Assert.Null(source.Timeline.TargetDurationSeconds);
    }

    [Fact]
    public void Parse_PartialEndpoints_DoNotContradictLongerContainerDuration()
    {
        var source = MediaProbeParser.Parse("""
            {"format":{"start_time":"0","duration":"10"},
            "streams":[{"index":0,"codec_type":"audio","start_time":"0","duration":"4"},
            {"index":1,"codec_type":"audio","start_time":"0"}]}
            """);
        Assert.Equal(10, source.Timeline.TargetDurationSeconds);
        Assert.Equal(TimelineReliability.Container, source.Timeline.DurationReliability);
    }

    [Fact]
    public void Parse_ConflictingDuration_IsFlagged()
    {
        var source = MediaProbeParser.Parse("""
            {"format":{"start_time":"5","duration":"4"},
            "streams":[{"index":0,"codec_type":"video","start_time":"5","duration":"10"}]}
            """);
        Assert.Equal(4, source.Timeline.TargetDurationSeconds);
        Assert.Equal(TimelineReliability.Inconsistent, source.Timeline.DurationReliability);
        Assert.NotEmpty(source.Timeline.Warnings);
    }

    [Fact]
    public void Parse_Decimals_AreIndependentOfCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var source = MediaProbeParser.Parse("""{"format":{"start_time":"-1.25","duration":"3.5"}}""");
            Assert.Equal(-1.25, source.Timeline.OriginSeconds);
            Assert.Equal(3.5, source.Timeline.TargetDurationSeconds);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Parse_MissingAudioIndex_RejectsAmbiguousSelection()
    {
        Assert.Throws<ArgumentException>(() => MediaProbeParser.Parse("""{"streams":[{"codec_type":"audio"}]}"""));
    }
}
