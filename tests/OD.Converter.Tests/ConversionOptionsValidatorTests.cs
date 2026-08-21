using OD.Converter.Models;
using OD.Converter.Services;
using Xunit;

namespace OD.Converter.Tests;

public sealed class ConversionOptionsValidatorTests
{
    [Fact]
    public void Validate_AcceptsSupportedMp3Options()
    {
        var options = CreateOptions("mp3", bitrate: "192k", sampleRate: 48000, channels: 2);

        ConversionOptionsValidator.Validate(options);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void Validate_RejectsOutOfRangeMp3Quality(int quality)
    {
        var options = CreateOptions("mp3", quality: quality);

        var exception = Assert.Throws<ArgumentException>(() => ConversionOptionsValidator.Validate(options));

        Assert.Contains("--quality", exception.Message);
    }

    [Fact]
    public void Validate_RejectsQualityForUnsupportedFormat()
    {
        var options = CreateOptions("flac", quality: 5);

        var exception = Assert.Throws<ArgumentException>(() => ConversionOptionsValidator.Validate(options));

        Assert.Contains("not supported", exception.Message);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(20)]
    public void Validate_RejectsUnsupportedBitDepth(int bitDepth)
    {
        var options = CreateOptions("wav", bitDepth: bitDepth);

        Assert.Throws<ArgumentException>(() => ConversionOptionsValidator.Validate(options));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void Validate_RejectsInvalidFlacCompression(int compressionLevel)
    {
        var options = CreateOptions("flac", compressionLevel: compressionLevel);

        Assert.Throws<ArgumentException>(() => ConversionOptionsValidator.Validate(options));
    }

    [Fact]
    public void EnsureDifferentPaths_RejectsPathsToTheSameFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "odconv", "track.wav");

        var exception = Assert.Throws<ArgumentException>(() =>
            ConversionOptionsValidator.EnsureDifferentPaths(path, Path.GetFullPath(path)));

        Assert.Contains("must be different", exception.Message);
    }

    [Fact]
    public void BuildArguments_UsesRequestedWavBitDepth()
    {
        var options = CreateOptions("wav", bitDepth: 24);

        var arguments = FfmpegConverter.BuildArguments(options);

        Assert.Contains("pcm_s24le", arguments);
    }

    [Theory]
    [InlineData("mp4", "m4a")]
    [InlineData("m4b", "m4a")]
    public void Get_ResolvesFormatAliases(string alias, string expectedKey)
    {
        var format = FormatRegistry.Get(alias);

        Assert.NotNull(format);
        Assert.Equal(expectedKey, format.Key);
    }

    private static ConversionOptions CreateOptions(
        string format,
        string? bitrate = null,
        int? quality = null,
        int? sampleRate = null,
        int? channels = null,
        int? bitDepth = null,
        int? compressionLevel = null) => new()
    {
        InputPath = "input.wav",
        OutputPath = "output." + format,
        TargetFormat = format,
        Bitrate = bitrate,
        Quality = quality,
        SampleRate = sampleRate,
        Channels = channels,
        BitDepth = bitDepth,
        CompressionLevel = compressionLevel,
    };
}
