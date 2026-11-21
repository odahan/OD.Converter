using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using OD.Converter.Models;
using OD.Converter.Services;
using OD.Converter.Services.Modules;

namespace OD.Converter.Gui;

internal sealed partial class MainViewModel
{
    private readonly Dictionary<ModuleId, ModuleStatus> moduleStatuses = [];

    public IReadOnlyList<ModuleId> ModuleOptions { get; } = Enum.GetValues<ModuleId>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModuleStateText), nameof(ModuleDescription), nameof(ModuleMessage))]
    private ModuleId selectedModule;

    public string ModuleDescription => moduleStatuses.TryGetValue(SelectedModule, out var status)
        ? status.Location : "Refresh to check the installation.";
    public string ModuleStateText => moduleStatuses.TryGetValue(SelectedModule, out var status)
        ? ModuleStateLabel(status.State) : "Status not checked";
    public string ModuleMessage => moduleStatuses.TryGetValue(SelectedModule, out var status) ? status.Message : "";

    private ModuleState? SelectedModuleState => moduleStatuses.GetValueOrDefault(SelectedModule)?.State;
    private bool updatingWorkflows;

    partial void OnSelectedPageChanged(int value)
    {
        if (value == 3 && moduleStatuses.Count == 0 && !IsBusy)
            RunCommand.Execute("Refresh");
    }

    private bool CanVerifyModule => SelectedModuleState is ModuleState.Ready or ModuleState.NeedsRepair;
    private bool CanInstallModule => SelectedModuleState == ModuleState.NotInstalled;
    private bool CanRepairModule => SelectedModuleState == ModuleState.NeedsRepair;
    private bool CanUninstallModule => SelectedModuleState is ModuleState.Ready or ModuleState.NeedsRepair;

    partial void OnSelectedModuleChanged(ModuleId value) => RunCommand.NotifyCanExecuteChanged();

    private async Task RunModuleOperationAsync(string operation, CancellationToken ct)
    {
        var id = SelectedModule;
        if (operation is "Install" or "Repair")
        {
            var installation = await Task.Run(() => modules.InstallationInfo(id), ct);
            var description = $"{id}\n\nVersion: {installation.Version}\nLocation: {installation.Location}\n" +
                $"Profile: Windows x64 CPU\nDownload: {installation.DownloadBytes:N0} bytes";
            if (!desktop.Confirm($"{operation} {id}", description, $"_{operation}"))
            {
                State = "Cancelled";
                return;
            }
        }
        else if (operation == "Uninstall" &&
                 !desktop.Confirm($"Uninstall {id}", $"Remove {id} from {ModuleDescription}?", "_Uninstall"))
        {
            State = "Cancelled";
            return;
        }

        SetProgress(operation == "Uninstall" ? $"Uninstalling {id}" : $"Checking {id} offline");
        try
        {
            if (operation is "Install" or "Repair")
            {
                SetProgress($"{operation} · {id}");
                var location = moduleStatuses[id].Location;
                moduleStatuses[id] = new ModuleStatus(id, ModuleState.Installing, location, $"{operation} in progress.");
                UpdateModuleChoices();
                var lastUpdate = Stopwatch.StartNew();
                var report = new Progress<ModuleProgress>(value =>
                {
                    if (!IsBusy) return;
                    var detail = value.TotalBytes is { } size ? $" · {value.BytesReceived:N0} / {size:N0} bytes" : "";
                    SetProgress(value.Stage + detail, value.BytesReceived, value.TotalBytes);
                });
                await Task.Run(() => modules.InstallAsync(id, operation == "Repair", new ModuleTransferProgress(report, lastUpdate), ct), ct);
            }
            else if (operation == "Uninstall")
                await Task.Run(() => modules.UninstallAsync(id, ct), ct);
        }
        finally
        {
            if (operation is "Install" or "Repair" or "Uninstall")
            {
                if (ct.IsCancellationRequested)
                {
                    moduleStatuses.Remove(id);
                    UpdateModuleChoices();
                }
                else await RefreshModuleAsync(id, ct);
            }
        }

        if (operation == "Refresh")
        {
            foreach (var module in ModuleOptions) await RefreshModuleAsync(module, ct);
        }
        else if (operation == "Verify")
        {
            await RefreshModuleAsync(id, ct);
            if (moduleStatuses[id].State != ModuleState.Ready)
            {
                Notice(moduleStatuses[id].Message);
                State = "Failed";
            }
        }
    }

    private async Task RefreshModuleAsync(ModuleId id, CancellationToken ct)
    {
        SetProgress($"Checking {id} offline");
        moduleStatuses[id] = await Task.Run(() => modules.StatusAsync(id, ct), ct);
        UpdateModuleChoices();
    }

    private void UpdateModuleChoices()
    {
        var selected = SelectedWorkflow?.Action ?? ProcessingAction.Extract;
        var choices = Enum.GetValues<ProcessingAction>().Select(action =>
        {
            var prerequisites = ProcessingAvailability.RequiredModules(action);
            var unavailable = prerequisites.Where(id => moduleStatuses.GetValueOrDefault(id)?.State != ModuleState.Ready).ToArray();
            var requirement = prerequisites.Count == 0 ? "Works offline with FFmpeg." :
                "Required modules · " + string.Join(" · ", prerequisites.Select(id =>
                    $"{id}: {(moduleStatuses.TryGetValue(id, out var status) ? ModuleStateLabel(status.State) : "status not checked")}"));
            var actionName = action switch
            {
                ProcessingAction.Extract => "Extract soundtrack",
                ProcessingAction.Separate => "Separate vocals and background",
                ProcessingAction.Transcribe => "Transcribe",
                _ => "Separate and transcribe"
            };
            return new WorkflowChoice(action, unavailable.Length == 0 ? actionName : $"{actionName} (unavailable)",
                unavailable.Length == 0, requirement);
        }).ToArray();
        updatingWorkflows = true;
        Workflows = choices;
        SelectedWorkflow = choices.Single(choice => choice.Action == selected);
        updatingWorkflows = false;
        ProcessingAvailabilityText = SelectedWorkflow.Requirement;
        ReviewProcessing();

        OnPropertyChanged(nameof(ModuleStateText));
        OnPropertyChanged(nameof(ModuleDescription));
        OnPropertyChanged(nameof(ModuleMessage));
        RunCommand.NotifyCanExecuteChanged();
    }

    private static string ModuleStateLabel(ModuleState state) => state switch
    {
        ModuleState.NotInstalled => "Not installed",
        ModuleState.NeedsRepair => "Needs repair",
        _ => state.ToString()
    };

    private sealed class ModuleTransferProgress(IProgress<ModuleProgress> target, Stopwatch clock) : IProgress<ModuleProgress>
    {
        public void Report(ModuleProgress value)
        {
            if (value.TotalBytes is not null && value.BytesReceived != value.TotalBytes && clock.ElapsedMilliseconds < 75) return;
            clock.Restart();
            target.Report(value);
        }
    }
}
