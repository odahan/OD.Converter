using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OD.Converter.Models;
using OD.Converter.Services;
using OD.Converter.Services.Modules;

namespace OD.Converter.Gui;

internal sealed partial class MainViewModel
{
    private ProbeInfo? analyzed;
    public EncodingSettings ProcessingEncoding { get; } = new("wav");
    public ObservableCollection<ExportChoice> Exports { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string processingSource = "";

    [ObservableProperty] private string processingTools = "";
    [ObservableProperty] private string processingDirectory = "";
    [ObservableProperty] private string processingPrefix = "";
    [ObservableProperty] private string processingLanguage = "Auto";
    [ObservableProperty] private IReadOnlyList<TrackChoice> tracks = [];
    [ObservableProperty] private TrackChoice? selectedTrack;
    [ObservableProperty] private IReadOnlyList<WorkflowChoice> workflows = [];
    [ObservableProperty] private WorkflowChoice? selectedWorkflow;
    [ObservableProperty] private string processingSummary = "Analyze a source to select its audio track.";
    [ObservableProperty] private string processingAvailabilityText = "";
    [ObservableProperty] private bool hasAudioExports;
    [ObservableProperty] private bool showLanguage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string inspectSource = "";

    [ObservableProperty] private string inspectTools = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowJsonCommand), nameof(CopyJsonCommand))]
    private string rawJson = "";

    [ObservableProperty] private string inspection = "";

    private bool CanProcess => analyzed is not null && SelectedTrack is not null &&
        SelectedWorkflow?.Available == true && Exports.Any(export => export.Selected);

    partial void OnProcessingSourceChanged(string value) => ClearAnalysis();
    partial void OnProcessingToolsChanged(string value) => ClearAnalysis();
    partial void OnProcessingDirectoryChanged(string value) => ReviewProcessing();
    partial void OnProcessingPrefixChanged(string value) => ReviewProcessing();
    partial void OnProcessingLanguageChanged(string value) => ReviewProcessing();
    partial void OnSelectedTrackChanged(TrackChoice? value) => ReviewProcessing();
    partial void OnSelectedWorkflowChanged(WorkflowChoice? oldValue, WorkflowChoice? newValue)
    {
        if (updatingWorkflows) return;
        if (oldValue?.Action != newValue?.Action) RebuildExports();
        ProcessingAvailabilityText = newValue?.Requirement ?? "";
        ReviewProcessing();
    }

    partial void OnInspectSourceChanged(string value) => ClearInspection();
    partial void OnInspectToolsChanged(string value) => ClearInspection();

    private void ClearAnalysis()
    {
        analyzed = null;
        Tracks = [];
        SelectedTrack = null;
        ProcessingDirectory = "";
        ProcessingPrefix = "";
        ReviewProcessing();
    }

    private void ClearInspection()
    {
        Inspection = "";
        RawJson = "";
    }

    [RelayCommand]
    private void BrowseProcessingSource() => TryDesktop(() =>
    {
        if (desktop.PickFiles(false) is [var path]) ProcessingSource = path;
    });

    [RelayCommand]
    private void BrowseInspectSource() => TryDesktop(() =>
    {
        if (desktop.PickFiles(false) is [var path]) InspectSource = path;
    });

    [RelayCommand]
    private void BrowseProcessingDestination() => TryDesktop(() =>
    {
        if (desktop.PickFolder("Choose an export folder") is { } folder) ProcessingDirectory = folder;
    });

    private async Task AnalyzeAsync(bool inspect, CancellationToken ct)
    {
        var input = CleanPath(inspect ? InspectSource : ProcessingSource);
        var tools = OptionalPath(inspect ? InspectTools : ProcessingTools);
        if (inspect) ClearInspection();
        else ClearAnalysis();
        SetProgress("Analyzing source");
        var info = await Task.Run(() => processing.AnalyzeAsync(input, tools, ct), ct);
        var source = info.Source ?? MediaProbeParser.Parse(info.RawJson);
        if (inspect)
        {
            Inspection = DescribeInspection(input, info, source);
            RawJson = info.RawJson;
            return;
        }

        analyzed = info;
        Tracks = source.AudioTracks.Select(track => new TrackChoice(track, DescribeTrack(track))).ToArray();
        SelectedTrack = Tracks.FirstOrDefault();
        ProcessingDirectory = Path.GetDirectoryName(Path.GetFullPath(input))!;
        ProcessingPrefix = Path.GetFileNameWithoutExtension(input);
        foreach (var warning in source.Timeline.Warnings) Notice(warning);
        ReviewProcessing();
    }

    private bool CanUseJson() => RawJson.Length != 0;

    [RelayCommand(CanExecute = nameof(CanUseJson))]
    private void ShowJson() => desktop.Show("Raw FFprobe JSON", RawJson);

    [RelayCommand(CanExecute = nameof(CanUseJson))]
    private void CopyJson() => TryDesktop(() => Desktop.Copy(RawJson));

    private void RebuildExports()
    {
        Exports.Clear();
        var action = SelectedWorkflow?.Action ?? ProcessingAction.Extract;
        var defaults = ProcessingConventions.DefaultExports(action);
        foreach (var kind in ProcessingConventions.AllowedExports(action))
        {
            var choice = new ExportChoice(kind, defaults.Contains(kind));
            choice.PropertyChanged += (_, _) => ReviewProcessing();
            Exports.Add(choice);
        }
        ShowLanguage = ProcessingAvailability.RequiredModules(action).Contains(ModuleId.Transcription);
    }

    private ProcessingRequest CurrentProcessingRequest()
    {
        var encoding = HasAudioExports ? ProcessingEncoding.GetOptions(OptionalPath(ProcessingTools)) :
            new ConversionOptions { TargetFormat = "wav", FfmpegPath = OptionalPath(ProcessingTools) };
        var language = ProcessingLanguage.Trim();
        if (!ShowLanguage || language.Equals("auto", StringComparison.OrdinalIgnoreCase)) language = "auto";
        var source = CleanPath(ProcessingSource);
        return new ProcessingRequest(source, SelectedWorkflow?.Action ?? ProcessingAction.Extract,
            SelectedTrack?.Track.StreamIndex, OptionalPath(ProcessingDirectory) ?? Path.GetDirectoryName(Path.GetFullPath(source))!,
            ProcessingPrefix, Exports.Where(item => item.Selected).Select(item => item.Kind).ToArray(), language, encoding);
    }

    private void ReviewProcessing()
    {
        HasAudioExports = Exports.Any(item => item.Selected && ProcessingConventions.IsAudioExport(item.Kind));
        RunCommand.NotifyCanExecuteChanged();
        if (analyzed is null)
        {
            ProcessingSummary = "Analyze a source to select its audio track.";
            return;
        }
        if (SelectedTrack is null)
        {
            ProcessingSummary = "This source contains no audio track. You can still inspect it.";
            return;
        }
        if (!Exports.Any(item => item.Selected))
        {
            ProcessingSummary = "Select at least one export.";
            return;
        }
        try
        {
            var request = CurrentProcessingRequest();
            var plan = ProcessingService.Plan(request, analyzed);
            ProcessingSummary = $"{DescribeTrack(plan.Track)}\n\n" + string.Join("\n", request.Exports.Select(kind =>
            {
                var path = Path.GetFullPath(ProcessingConventions.OutputPath(request, kind));
                return $"{ExportChoice.Name(kind)} → {path}{(File.Exists(path) ? " (already exists)" : "")}";
            }));
        }
        catch (Exception ex) { ProcessingSummary = ex.Message; }
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        var request = CurrentProcessingRequest();
        var plan = ProcessingService.Plan(request, analyzed!);
        var paths = request.Exports.ToDictionary(kind => kind,
            kind => Path.GetFullPath(ProcessingConventions.OutputPath(request, kind)));
        var approval = new OverwriteApproval(paths.Values);
        var conflicts = approval.Paths.ToArray();
        var replacementDetails = conflicts.Length == 0 ? "" : $"\n\nReplace these existing files?\n{string.Join("\n", conflicts)}";
        var summary = $"{DescribeTrack(plan.Track)}\n\n" +
            string.Join("\n", paths.Select(pair => $"{ExportChoice.Name(pair.Key)} → {pair.Value}")) + replacementDetails;
        if (!desktop.Confirm("Review processing", summary, conflicts.Length == 0 ? "_Process" : "_Replace and process"))
        {
            State = "Cancelled";
            return;
        }

        SetProgress("Analyzing source");
        ProcessingResult result;
        try
        {
            var info = await Task.Run(() => processing.AnalyzeAsync(request.InputPath, request.AudioOptions.FfmpegPath, ct), ct);
            plan = ProcessingService.Plan(request with { Approval = approval }, info);
            foreach (var warning in plan.Source.Timeline.Warnings) Notice(warning);
            SetProgress("Checking workflow and destinations");
            result = await Task.Run(() => processing.ExecuteAsync(plan, ct), ct);
        }
        catch (OperationCanceledException) { result = new ProcessingResult(130, [], [], null); }
        catch (Exception ex) { result = new ProcessingResult(3, [], [], ex.Message); }

        var finalized = result.Outputs.ToDictionary(output => output.Kind);
        foreach (var (kind, path) in paths)
        {
            var created = finalized.TryGetValue(kind, out var output);
            AddResult(new ResultRow(ExportChoice.Name(kind), output?.Path ?? path,
                created ? "Completed" : "Not created", created ? "" : "The requested export was not finalized.", created));
        }
        foreach (var warning in result.Warnings) Notice(warning);
        if (ProcessingService.EncoderDelayWarning(request, result.Outputs) is { } delay) Notice(delay);
        if (result.ErrorMessage is not null)
        {
            Notice(result.ErrorMessage);
            AppendDiagnostic(result.ErrorMessage);
        }
        State = result.ExitCode switch { 0 => "Completed", 130 => "Cancelled", _ => "Failed" };
    }

    private static string DescribeTrack(AudioTrackInfo track) =>
        $"Stream {track.StreamIndex} · {track.CodecName} · {track.Language ?? "unknown language"}" +
        $"{(track.Title is null ? "" : $" · {track.Title}")} · " +
        $"{(track.Channels > 0 ? $"{track.Channels} channel(s)" : "unknown channels")} · {Seconds(track.DurationSeconds)}";

    private static string DescribeInspection(string input, ProbeInfo info, MediaSource source)
    {
        using var json = JsonDocument.Parse(info.RawJson);
        var audioByIndex = source.AudioTracks.ToDictionary(track => track.StreamIndex);
        var timeline = source.Timeline;
        var lines = new List<string>
        {
            Path.GetFullPath(input), $"Container: {source.FormatName}",
            $"Presentation origin: {Seconds(timeline.OriginSeconds)} ({timeline.OriginReliability})",
            $"Target duration: {Seconds(timeline.TargetDurationSeconds)} ({timeline.DurationReliability})",
            "Timeline values are metadata estimates. PCM preparation measures alignment separately."
        };
        if (info.BitRate > 0) lines.Add($"Container bitrate: {info.BitRate:N0} bit/s");
        if (source.AudioTracks.Count == 0) lines.Add("No audio tracks. Audio processing is unavailable.");
        foreach (var warning in timeline.Warnings) lines.Add($"Warning: {warning}");

        if (json.RootElement.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                if (!stream.TryGetProperty("index", out var index) || !index.TryGetInt32(out var number)) continue;
                lines.Add("");
                if (audioByIndex.TryGetValue(number, out var track))
                {
                    lines.Add(DescribeTrack(track));
                    lines.Add($"  Start: {Seconds(track.StartSeconds)} · Offset: {Seconds(track.GetOffsetSeconds(timeline))}");
                    lines.Add($"  Sample rate: {(track.SampleRate > 0 ? $"{track.SampleRate:N0} Hz" : "unknown")} · Layout: {track.ChannelLayout ?? "unknown"}" +
                        (track.BitsPerSample > 0 ? $" · {track.BitsPerSample} bit" : "") +
                        (track.BitRate > 0 ? $" · {track.BitRate:N0} bit/s" : ""));
                }
                else
                {
                    lines.Add($"Stream {number} · {Read(stream, "codec_type")} · {Read(stream, "codec_name")}");
                    var start = ReadNumber(stream, "start_time");
                    lines.Add($"  Start: {Seconds(start)} · Duration: {Seconds(ReadNumber(stream, "duration"))} · Offset: {Seconds(start - timeline.OriginSeconds)}");
                }
            }
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string Read(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) ? value.ToString() : "unknown";

    private static double? ReadNumber(JsonElement element, string key) =>
        double.TryParse(Read(element, key), CultureInfo.InvariantCulture, out var number) ? number : null;

    private static string Seconds(double? value) => value is { } seconds
        ? $"{seconds.ToString("0.######", CultureInfo.InvariantCulture)} s" : "unknown";
}

internal sealed record TrackChoice(AudioTrackInfo Track, string Label);
internal sealed record WorkflowChoice(ProcessingAction Action, string Label, bool Available, string Requirement);

internal sealed partial class ExportChoice(ExportKind kind, bool selected) : ObservableObject
{
    [ObservableProperty] private bool selected = selected;

    public ExportKind Kind { get; } = kind;
    public string Label => Name(Kind);

    public static string Name(ExportKind kind) => kind switch
    {
        ExportKind.Transcript => "transcript (TXT)",
        ExportKind.Subtitles => "subtitles (SRT)",
        _ => kind.ToString().ToLowerInvariant()
    };
}
