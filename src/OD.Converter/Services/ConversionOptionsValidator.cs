using System.Text.RegularExpressions;
using OD.Converter.Models;

namespace OD.Converter.Services;

/// <summary>
/// Validates conversion options before an FFmpeg process is started.
/// </summary>
public partial class ConversionOptionsValidator : IConversionOptionsValidator
{
    private readonly IFileSystem _fileSystem;
    private static readonly HashSet<int> SupportedSampleRates = [22050, 44100, 48000, 96000];
    private static readonly HashSet<int> SupportedChannels = [1, 2, 6];
    private static readonly HashSet<int> SupportedBitDepths = [16, 24, 32];

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
        if (options.SampleRate.HasValue && !SupportedSampleRates.Contains(options.SampleRate.Value))
            throw new ArgumentException("Invalid --sample-rate value. Expected: 22050, 44100, 48000, or 96000.");

        // Channel count must be 1 (mono), 2 (stereo), or 6 (5.1 surround)
        if (options.Channels.HasValue && !SupportedChannels.Contains(options.Channels.Value))
            throw new ArgumentException("Invalid --channels value. Expected: 1, 2, or 6.");

        // Quality is only supported for MP3 and Ogg Vorbis
        if (options.Quality.HasValue)
        {
            var maximumQuality = format.Key switch { "mp3" => 9, "ogg" => 10, _ => -1 };
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
            if (format.Key is not ("wav" or "aiff"))
                throw new ArgumentException($"--bit-depth is not supported for {format.Key} output.");
            if (!SupportedBitDepths.Contains(options.BitDepth.Value))
                throw new ArgumentException("Invalid --bit-depth value. Expected: 16, 24, or 32.");
        }

        // Compression level is only valid for FLAC
        if (options.CompressionLevel.HasValue)
        {
            if (format.Key != "flac")
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
