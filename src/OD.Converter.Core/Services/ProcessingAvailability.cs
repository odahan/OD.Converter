using OD.Converter.Models;
using OD.Converter.Services.Modules;

namespace OD.Converter.Services;

/// <summary>Shares module prerequisites between interactive choices and command execution.</summary>
public static class ProcessingAvailability
{
    /// <summary>Returns only the modules needed by the requested action.</summary>
    public static IReadOnlyList<ModuleId> RequiredModules(ProcessingAction action) => action switch
    {
        ProcessingAction.Extract => [],
        ProcessingAction.Separate => [ModuleId.Separation],
        ProcessingAction.Transcribe => [ModuleId.Transcription],
        ProcessingAction.SeparateTranscribe => [ModuleId.Separation, ModuleId.Transcription],
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    /// <summary>Explains every unavailable prerequisite, including an actionable lifecycle command.</summary>
    public static string? UnavailableReason(ProcessingAction action, IReadOnlyDictionary<ModuleId, ModuleStatus> statuses)
    {
        var reasons = RequiredModules(action).Where(id => !statuses.TryGetValue(id, out var status) || status.State != ModuleState.Ready)
            .Select(id =>
            {
                var state = statuses.TryGetValue(id, out var status) ? status.State : ModuleState.NotInstalled;
                var operation = state == ModuleState.NeedsRepair ? "repair" : state == ModuleState.Installing ? "status" : "install";
                return $"Requires {ModulePaths.Name(id)} ({state}). Run odconv modules {operation} {ModulePaths.Name(id)}.";
            }).ToArray();
        return reasons.Length == 0 ? null : string.Join(" ", reasons);
    }

    /// <summary>Checks required modules offline without downloading or inspecting unrelated engines.</summary>
    public static async Task<string?> CheckAsync(ProcessingAction action, ModuleManager manager, CancellationToken ct)
    {
        var statuses = new Dictionary<ModuleId, ModuleStatus>();
        foreach (var id in RequiredModules(action)) statuses[id] = await manager.StatusAsync(id, ct);
        return UnavailableReason(action, statuses);
    }
}
