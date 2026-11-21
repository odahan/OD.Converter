using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OD.Converter.Services;
using OD.Converter.Services.Modules;

namespace OD.Converter.Gui;

/// <summary>Coordinates the four desktop workflows and owns the lifetime of the active operation.</summary>
internal sealed partial class MainViewModel : ObservableObject
{
    private readonly Desktop desktop;
    private readonly ConversionService conversion;
    private readonly ProcessingService processing;
    private readonly ModuleManager modules;
    private readonly UserGuideService guide;
    private readonly StringBuilder diagnostics = new();

    public MainViewModel(Desktop desktop, ConversionService conversion, ProcessingService processing, ModuleManager modules,
        UserGuideService guide, string? startupSource)
    {
        this.desktop = desktop;
        this.conversion = conversion;
        this.processing = processing;
        this.modules = modules;
        this.guide = guide;
        ConversionSource = startupSource ?? "";
        ConversionEncoding.PropertyChanged += (_, _) => ClearConversionReview();
        ProcessingEncoding.PropertyChanged += (_, _) => ReviewProcessing();
        RebuildExports();
        UpdateModuleChoices();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool isBusy;

    [ObservableProperty] private int selectedPage;
    [ObservableProperty] private string state = "Ready";
    [ObservableProperty] private string stage = "";
    [ObservableProperty] private bool isIndeterminate = true;
    [ObservableProperty] private double completedUnits;
    [ObservableProperty] private double totalUnits = 1;
    [ObservableProperty] private bool showResults;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenResultCommand), nameof(ShowResultFolderCommand), nameof(CopyResultPathCommand))]
    private ResultRow? selectedResult;

    public bool CanEdit => !IsBusy;
    public ObservableCollection<ResultRow> Results { get; } = [];
    public ObservableCollection<string> Notices { get; } = [];

    public async Task CancelAndWaitAsync()
    {
        RunCancelCommand.Execute(null);
        await (RunCommand.ExecutionTask ?? Task.CompletedTask);
    }

    [RelayCommand(CanExecute = nameof(CanRun), IncludeCancelCommand = true)]
    private async Task RunAsync(string operation, CancellationToken ct)
    {
        if (IsBusy) return;
        IsBusy = true;
        Results.Clear();
        SelectedResult = null;
        Notices.Clear();
        diagnostics.Clear();
        ShowResults = false;
        State = "Running";
        SetProgress("Preparing…");
        try
        {
            switch (operation)
            {
                case "Preview": await ConvertAsync(preview: true, ct); break;
                case "Convert": await ConvertAsync(preview: false, ct); break;
                case "Analyze": await AnalyzeAsync(inspect: false, ct); break;
                case "Inspect": await AnalyzeAsync(inspect: true, ct); break;
                case "Process": await ProcessAsync(ct); break;
                case "Refresh":
                case "Verify":
                case "Install":
                case "Repair":
                case "Uninstall":
                    await RunModuleOperationAsync(operation, ct);
                    break;
                default: throw new ArgumentException("Unknown operation.");
            }
            if (State == "Running") State = "Completed";
        }
        catch (OperationCanceledException) { State = "Cancelled"; }
        catch (Exception ex)
        {
            State = "Failed";
            Notice(ex.Message);
            AppendDiagnostic(ex.ToString());
        }
        finally
        {
            Stage = "";
            IsBusy = false;
        }
    }

    private bool CanRun(string? operation) => !IsBusy && operation switch
    {
        "Analyze" => !string.IsNullOrWhiteSpace(ProcessingSource),
        "Inspect" => !string.IsNullOrWhiteSpace(InspectSource),
        "Preview" or "Convert" => !string.IsNullOrWhiteSpace(ConversionSource),
        "Process" => CanProcess,
        "Verify" => CanVerifyModule,
        "Install" => CanInstallModule,
        "Repair" => CanRepairModule,
        "Uninstall" => CanUninstallModule,
        "Refresh" => true,
        _ => false
    };

    internal void Report(MediaProgress progress)
    {
        if (!IsBusy) return;
        switch (progress.Kind)
        {
            case MediaProgressKind.PreparingAudio: SetProgress("Preparing audio on the presentation timeline"); break;
            case MediaProgressKind.Separating: SetProgress("Separating vocals and background"); break;
            case MediaProgressKind.Transcribing: SetProgress("Transcribing"); break;
            case MediaProgressKind.Encoding: SetProgress("Encoding audio export"); break;
            case MediaProgressKind.Warning:
            case MediaProgressKind.CleanupWarning:
                Notice(progress.Message);
                break;
        }
        AppendDiagnostic(progress.Message, progress.Kind != MediaProgressKind.NativeOutput);
    }

    private void SetProgress(string stage, double? completed = null, double? total = null)
    {
        Stage = stage;
        TotalUnits = Math.Max(1, total ?? 1);
        CompletedUnits = completed ?? 0;
        IsIndeterminate = total is null;
    }

    private void Notice(string text)
    {
        if (!string.IsNullOrWhiteSpace(text)) Notices.Add(text.Trim());
        ShowResults = true;
    }

    private void AddResult(ResultRow row)
    {
        Results.Add(row);
        SelectedResult ??= row;
        ShowResults = true;
    }

    private void AppendDiagnostic(string text, bool addNewLine = true)
    {
        diagnostics.Append(text);
        if (addNewLine) diagnostics.AppendLine();
        if (diagnostics.Length > 100_000) diagnostics.Remove(0, diagnostics.Length - 100_000);
    }

    private bool CanCopyResultPath() => SelectedResult is not null;

    private bool CanUseResult() => SelectedResult?.Finalized == true;

    [RelayCommand(CanExecute = nameof(CanUseResult))]
    private void OpenResult() => TryDesktop(() => Desktop.Open(SelectedResult!.Path));

    [RelayCommand(CanExecute = nameof(CanUseResult))]
    private void ShowResultFolder() => TryDesktop(() => Desktop.ShowFile(SelectedResult!.Path));

    [RelayCommand(CanExecute = nameof(CanCopyResultPath))]
    private void CopyResultPath() => TryDesktop(() => Desktop.Copy(SelectedResult!.Path));

    [RelayCommand]
    private void ShowDiagnostics() => desktop.Show("Operation details",
        diagnostics.Length == 0 ? "No technical details were recorded." : diagnostics.ToString());

    [RelayCommand]
    private void NavigateToModules() => SelectedPage = 3;

    [RelayCommand]
    private void ShowHelp(string section) => TryDesktop(() =>
    {
        switch (section)
        {
            case "Supported formats":
                desktop.Show(section, string.Join("\n\n", FormatRegistry.All.Select(format =>
                    $"{format.Key.ToUpperInvariant()} · {format.Extension} · {format.Description}\n" +
                    $"{format.Container} / {format.Codec} · {(format.IsLossless ? "Lossless" : "Lossy")}\n{format.FfmpegArgs}")) +
                    $"\n\nAliases for M4A: {string.Join(", ", FormatRegistry.Aliases.Where(pair => pair.Value == "m4a").Select(pair => pair.Key))}\n\n" +
                    "Bitrate accepts a number followed by k or M. MP3 and Ogg can use VBR quality instead of bitrate. " +
                    "Keep source leaves sample rate and channels unspecified. Force mono takes priority over channel count. " +
                    "WAV and AIFF support PCM depth; FLAC supports compression from 0 to 8.");
                break;
            case "User guide":
                var path = guide.Extract();
                if (desktop.Confirm(section, $"The offline English/French guide has been extracted to:\n\n{path}", "_Open guide"))
                    guide.Open();
                break;
            case "About":
                desktop.Show(section, $"OD.Converter {ApplicationInfo.Version}\nWindows x64 · FFmpeg 8.1.2\n\nAudio conversion and audio/video processing");
                break;
            case "Buy Me a Coffee":
                Desktop.Open(ApplicationInfo.SupportUrl);
                break;
        }
    });

    private void TryDesktop(Action action)
    {
        try { action(); }
        catch (Exception ex) { desktop.Show("Operation failed", ex.Message); }
    }

    private static string CleanPath(string value) => value.Trim().Trim('"');
    private static string? OptionalPath(string value) => string.IsNullOrWhiteSpace(value) ? null : CleanPath(value);
}

/// <summary>Represents one planned or attempted file in the desktop results.</summary>
internal sealed record ResultRow(string Name, string Path, string Status, string Detail, bool Finalized = false);
