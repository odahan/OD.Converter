using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OD.Converter.Services;

namespace OD.Converter.Gui;

internal sealed partial class MainViewModel
{
    public EncodingSettings ConversionEncoding { get; } = new("mp3");
    public IReadOnlyList<string> SourceModes { get; } = ["Single file", "Multiple files", "Folder"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFolder), nameof(IsBatch), nameof(IsSingle), nameof(IsMultiple), nameof(DestinationLabel))]
    private string conversionMode = "Single file";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string conversionSource = "";

    [ObservableProperty] private string conversionOutput = "";
    [ObservableProperty] private string conversionPattern = "*";
    [ObservableProperty] private bool recursive;
    [ObservableProperty] private bool allowOverwrite;
    [ObservableProperty] private bool suggestMono = true;
    [ObservableProperty] private string conversionTools = "";
    [ObservableProperty] private string conversionReview = "";

    public bool IsFolder => ConversionMode == "Folder";
    public bool IsSingle => ConversionMode == "Single file";
    public bool IsBatch => !IsSingle;
    public bool IsMultiple => ConversionMode == "Multiple files";
    public string DestinationLabel => IsSingle ? "_Output file (optional)" : "_Output folder (optional)";

    partial void OnConversionModeChanged(string value)
    {
        ConversionSource = "";
        ConversionOutput = "";
        ClearConversionReview();
    }

    partial void OnConversionSourceChanged(string value) => ClearConversionReview();
    partial void OnConversionOutputChanged(string value) => ClearConversionReview();
    partial void OnConversionPatternChanged(string value) => ClearConversionReview();
    partial void OnRecursiveChanged(bool value) => ClearConversionReview();
    partial void OnAllowOverwriteChanged(bool value) => ClearConversionReview();
    partial void OnSuggestMonoChanged(bool value) => ClearConversionReview();
    partial void OnConversionToolsChanged(string value) => ClearConversionReview();

    private void ClearConversionReview() => ConversionReview = "";

    [RelayCommand]
    private void BrowseConversionSource() => TryDesktop(() =>
    {
        if (IsFolder)
        {
            if (desktop.PickFolder("Select a source folder") is { } folder) ConversionSource = folder;
        }
        else if (desktop.PickFiles(IsBatch) is { Length: > 0 } paths)
            ConversionSource = string.Join(Environment.NewLine, paths);
    });

    [RelayCommand]
    private void BrowseConversionOutput() => TryDesktop(() =>
    {
        var selected = IsBatch ? desktop.PickFolder("Choose an output folder") :
            desktop.PickOutput(string.IsNullOrWhiteSpace(ConversionOutput)
                ? Path.ChangeExtension(Path.GetFileName(CleanPath(ConversionSource)), ConversionEncoding.Format.Extension)
                : ConversionOutput, ConversionEncoding.Format.Extension);
        if (selected is not null) ConversionOutput = selected;
    });

    private async Task ConvertAsync(bool preview, CancellationToken ct)
    {
        var inputs = IsSingle || IsFolder ? [CleanPath(ConversionSource)] :
            ConversionSource.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (IsFolder && !Directory.Exists(inputs[0])) throw new ArgumentException("Select an existing source folder.");
        if (!IsFolder && inputs.Any(Directory.Exists)) throw new ArgumentException("Select files or switch the input mode to Folder.");

        SetProgress("Resolving inputs and destinations");
        var tools = OptionalPath(ConversionTools);
        var mono = false;
        if (IsSingle && SuggestMono && !ConversionEncoding.Mono && File.Exists(inputs[0]))
        {
            try
            {
                var info = await Task.Run(() => processing.AnalyzeAsync(inputs[0], tools, ct), ct);
                mono = info.Channels == 1;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { AppendDiagnostic($"Automatic mono suggestion unavailable: {ex.Message}"); }
        }

        var encoding = ConversionEncoding.GetOptions(tools, AllowOverwrite, mono, preview);
        var output = OptionalPath(ConversionOutput);
        if (IsSingle && output is null)
            output = encoding.Mono ? FormatRegistry.DefaultOutputPathMono(inputs[0], encoding.TargetFormat)
                : FormatRegistry.DefaultOutputPath(inputs[0], encoding.TargetFormat);

        var request = new ConversionRequest(inputs, encoding, IsSingle ? output : null, IsBatch ? output : null,
            IsFolder && !string.IsNullOrWhiteSpace(ConversionPattern) ? ConversionPattern.Trim() : null,
            IsFolder && Recursive);
        var plan = await Task.Run(() => conversion.Plan(request, ct), ct);
        var validOutputs = plan.Entries.Where(entry => entry.Error is null).Select(entry => entry.Options.OutputPath);
        var proposed = new OverwriteApproval(validOutputs);

        ConversionReview = string.Join(Environment.NewLine, plan.Entries.Select(entry =>
        {
            var options = entry.Options;
            var destination = Path.GetFullPath(options.OutputPath);
            var exists = File.Exists(destination) ? " (already exists)" : "";
            return $"{Path.GetFullPath(options.InputPath)} → {destination}{exists}" +
                (entry.Error is not null ? $"\n  {entry.Error}" :
                    preview ? $"\n  {FfmpegConverter.BuildCommandLine(plan.Executable, entry.Arguments!)}" : "");
        }));
        if (mono) ConversionReview = $"Mono suggested from the source audio.{Environment.NewLine}{ConversionReview}";
        if (preview)
        {
            if (plan.Entries.Any(entry => entry.Error is not null)) State = "Failed";
            return;
        }

        var hasConflicts = proposed.Paths.Any();
        if (plan.Entries.Any(entry => entry.Error is null))
        {
            var summary = hasConflicts
                ? $"{ConversionReview}\n\nExisting files:\n{string.Join("\n", proposed.Paths)}\n\n" +
                  (AllowOverwrite ? "Replace these files after successful conversion?" : "Existing files will be skipped.")
                : ConversionReview;
            if (!desktop.Confirm("Review conversion", summary, hasConflicts && AllowOverwrite ? "_Replace and convert" : "_Convert"))
            {
                State = "Cancelled";
                return;
            }
        }

        var approval = AllowOverwrite ? proposed : new OverwriteApproval([]);
        var sourcePaths = plan.Entries.Select(entry => Path.GetFullPath(entry.Options.InputPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var attempted = 0;
        try
        {
            foreach (var entry in plan.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var name = entry.Options.InputPath;
                var path = Path.GetFullPath(entry.Options.OutputPath);
                SetProgress($"Converting {attempted + 1} of {plan.Entries.Count} · {Path.GetFileName(name)}",
                    attempted, plan.Entries.Count);
                var result = sourcePaths.Contains(path) && entry.Error is null
                    ? new ConversionItemResult(entry, 1, "The output would replace a source file in this batch.")
                    : await Task.Run(() => conversion.ExecuteAsync(plan.Executable, entry, ct, approval: approval), ct);
                var success = result.ExitCode == 0;
                if (!string.IsNullOrWhiteSpace(result.TechnicalDetail)) AppendDiagnostic(result.TechnicalDetail);
                AddResult(new ResultRow(name, path, success ? "Completed" : "Failed",
                    result.Error ?? $"{result.OutputBytes:N0} bytes", success));
                attempted++;
            }
        }
        catch (OperationCanceledException)
        {
            foreach (var entry in plan.Entries.Skip(attempted))
                AddResult(new ResultRow(entry.Options.InputPath, Path.GetFullPath(entry.Options.OutputPath),
                    "Cancelled", "Conversion did not finalize this file."));
            throw;
        }
        if (Results.Any(row => !row.Finalized)) State = "Failed";
    }
}
