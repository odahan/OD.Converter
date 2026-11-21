using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OD.Converter.Models;
using OD.Converter.Services;

namespace OD.Converter.Gui;

/// <summary>Exposes the encoder settings relevant to the selected format.</summary>
public sealed partial class EncodingSettings : ObservableObject
{
    public EncodingSettings(string defaultFormat) => format = FormatRegistry.Get(defaultFormat)!;

    public IReadOnlyList<FormatInfo> Formats { get; } = FormatRegistry.All.ToArray();
    public IReadOnlyList<string> SampleRates { get; } = ["Keep source", .. ConversionOptionsValidator.SampleRates.Select(FormatNumber)];
    public IReadOnlyList<string> ChannelCounts { get; } = ["Keep source", .. ConversionOptionsValidator.ChannelCounts.Select(FormatNumber)];
    public IReadOnlyList<string> Depths { get; } = ["Default", .. ConversionOptionsValidator.BitDepths.Select(FormatNumber)];
    public IReadOnlyList<string> CompressionLevels { get; } = ["Default", .. Enumerable.Range(0, 9).Select(FormatNumber)];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBitrate), nameof(HasQuality), nameof(HasDepth), nameof(HasCompression), nameof(Qualities), nameof(BitrateHint))]
    private FormatInfo format = null!;

    [ObservableProperty] private string bitrate = "";
    [ObservableProperty] private string quality = "Default";
    [ObservableProperty] private string sampleRate = "Keep source";
    [ObservableProperty] private string channels = "Keep source";
    [ObservableProperty] private string bitDepth = "Default";
    [ObservableProperty] private string compression = "Default";
    [ObservableProperty] private bool mono;

    public bool HasBitrate => !Format.IsLossless;
    public bool HasQuality => Format.MaximumQuality is not null;
    public bool HasDepth => Format.SupportsBitDepth;
    public bool HasCompression => Format.SupportsCompression;
    public IReadOnlyList<string> Qualities => ["Default", .. Enumerable.Range(0, (Format.MaximumQuality ?? 0) + 1).Select(FormatNumber)];
    public string BitrateHint => Format.Key == "ogg" ? "Blank uses Vorbis quality 6."
        : $"Blank uses the format default ({Format.DefaultBitrate}).";

    partial void OnFormatChanged(FormatInfo value) => Quality = "Default";

    public ConversionOptions GetOptions(string? ffmpegPath, bool overwrite = false, bool suggestedMono = false, bool dryRun = false) => new()
    {
        TargetFormat = Format.Key,
        Bitrate = HasBitrate && !string.IsNullOrWhiteSpace(Bitrate) ? Bitrate.Trim() : null,
        Quality = HasQuality ? ToNumber(Quality) : null,
        SampleRate = ToNumber(SampleRate),
        Channels = ToNumber(Channels),
        Mono = Mono || suggestedMono,
        BitDepth = HasDepth ? ToNumber(BitDepth) : null,
        CompressionLevel = HasCompression ? ToNumber(Compression) : null,
        Overwrite = overwrite ? "always" : "never",
        FfmpegPath = ffmpegPath,
        DryRun = dryRun
    };

    private static string FormatNumber(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static int? ToNumber(string? selection) => int.TryParse(selection, CultureInfo.InvariantCulture, out var value) ? value : null;
}
