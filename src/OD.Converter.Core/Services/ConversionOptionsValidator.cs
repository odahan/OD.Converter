using System.Text.RegularExpressions;
using OD.Converter.Models;

namespace OD.Converter.Services;

/// <summary>
/// Validates conversion options before an FFmpeg process is started.
/// </summary>
public partial class ConversionOptionsValidator : IConversionOptionsValidator
{
    private readonly IFileSystem _fileSystem;
    /// <summary>Gets the sample rates accepted by the encoder settings.</summary>
    public static IReadOnlyList<int> SampleRates { get; } = Array.AsReadOnly<int>([22050, 44100, 48000, 96000]);
    /// <summary>Gets the channel counts accepted by the encoder settings.</summary>
    public static IReadOnlyList<int> ChannelCounts { get; } = Array.AsReadOnly<int>([1, 2, 6]);
    /// <summary>Gets the supported PCM bit depths.</summary>
    public static IReadOnlyList<int> BitDepths { get; } = Array.AsReadOnly<int>([16, 24, 32]);

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversionOptionsValidator"/> class.
    /// </summary>
    /// <param name="fileSystem">The file system abstraction for path operations.</param>
    public ConversionOptionsValidator(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public void Validate(ConversionOptions options)
    {
        var format = FormatRegistry.Get(options.TargetFormat)
            ?? throw new ArgumentException($"Unsupported format: {options.TargetFormat}.");

        // Overwrite must be explicitly 'never' or 'always'
        if (!string.Equals(options.Overwrite, "never", StringComparison.Ordinal) &&
            !string.Equals(options.Overwrite, "always", StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid --overwrite value. Expected: never or always.");
        }

        // Sample rate must be one of the common values supported by most codecs
        if (options.SampleRate.HasValue && !SampleRates.Contains(options.SampleRate.Value))
            throw new ArgumentException("Invalid --sample-rate value. Expected: 22050, 44100, 48000, or 96000.");

        // Channel count must be 1 (mono), 2 (stereo), or 6 (5.1 surround)
        if (options.Channels.HasValue && !ChannelCounts.Contains(options.Channels.Value))
            throw new ArgumentException("Invalid --channels value. Expected: 1, 2, or 6.");

        // Quality is only supported for MP3 and Ogg Vorbis
        if (options.Quality.HasValue)
        {
            var maximumQuality = format.MaximumQuality ?? -1;
            if (maximumQuality < 0)
                throw new ArgumentException($"--quality is not supported for {format.Key} output.");
            if (options.Quality.Value is < 0 || options.Quality.Value > maximumQuality)
                throw new ArgumentException($"Invalid --quality value for {format.Key}. Expected: 0 to {maximumQuality}.");
        }

        // Bitrate validation: must be a number followed by k or M (case-insensitive)
        if (!string.IsNullOrWhiteSpace(options.Bitrate))
        {
            if (format.IsLossless)
                throw new ArgumentException($"--bitrate is not supported for {format.Key} output.");
            if (!BitratePattern().IsMatch(options.Bitrate))
                throw new ArgumentException("Invalid --bitrate value. Use a value such as 192k or 320k.");
        }

        // Cannot specify both quality and bitrate
        if (options.Quality.HasValue && !string.IsNullOrWhiteSpace(options.Bitrate))
            throw new ArgumentException("Use either --quality or --bitrate, not both.");

        // Bit depth is only valid for PCM formats (WAV, AIFF)
        if (options.BitDepth.HasValue)
        {
            if (!format.SupportsBitDepth)
                throw new ArgumentException($"--bit-depth is not supported for {format.Key} output.");
            if (!BitDepths.Contains(options.BitDepth.Value))
                throw new ArgumentException("Invalid --bit-depth value. Expected: 16, 24, or 32.");
        }

        // Compression level is only valid for FLAC
        if (options.CompressionLevel.HasValue)
        {
            if (!format.SupportsCompression)
                throw new ArgumentException($"--compression is not supported for {format.Key} output.");
            if (options.CompressionLevel.Value is < 0 or > 8)
                throw new ArgumentException("Invalid --compression value. Expected: 0 to 8.");
        }
    }

    /// <inheritdoc />
    public void EnsureDifferentPaths(string inputPath, string outputPath)
    {
        var fullInput = _fileSystem.GetFullPath(inputPath);
        var fullOutput = _fileSystem.GetFullPath(outputPath);
        if (string.Equals(fullInput, fullOutput, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The output file must be different from the source file.");
    }

    [GeneratedRegex("^[0-9]+[kKmM]$")]
    private static partial Regex BitratePattern();
}
