using OD.Converter.Services;
using OD.Converter.Services.Modules;
using Spectre.Console;

namespace OD.Converter.Commands;

/// <summary>Exposes the shared, isolated optional module lifecycle without implicit installation.</summary>
public sealed class ModulesCommand(ModuleManager manager)
{
    /// <summary>Executes an explicit module command and preserves the common CLI exit codes.</summary>
    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Any(arg => arg is "--help" or "-h" or "-?")) { PrintHelp(); return 0; }
        var valid = args is ["list"] or ["status" or "install" or "verify" or "repair" or "uninstall", "separation" or "transcription"];
        if (!valid) return ErrorHandler.Fail("Usage: odconv modules list | <status|install|verify|repair|uninstall> <separation|transcription>");
        try
        {
            ct.ThrowIfCancellationRequested();
            if (args is ["list"])
            {
                ConsoleTheme.Section(AnsiConsole.Console, "Optional modules", "Offline availability checks / private CPU engines");
                var statuses = new List<ModuleStatus>();
                foreach (var module in Enum.GetValues<ModuleId>()) statuses.Add(await manager.StatusAsync(module, ct));
                ConsoleTheme.ModuleStatuses(AnsiConsole.Console, statuses);
                return 0;
            }

            var id = args[1] == "separation" ? ModuleId.Separation : ModuleId.Transcription;
            if (args[0] is "status" or "verify")
            {
                var status = await manager.StatusAsync(id, ct);
                PrintStatus(status);
                return args[0] == "status" || status.State == ModuleState.Ready ? 0 : 2;
            }

            Console.WriteLine(manager.Describe(id));
            if (args[0] == "uninstall") await manager.UninstallAsync(id, ct);
            else
            {
                var progress = new ConsoleProgress();
                await manager.InstallAsync(id, args[0] == "repair", progress, ct);
            }
            PrintStatus(await manager.StatusAsync(id, ct));
            return 0;
        }
        catch (OperationCanceledException) { return ErrorHandler.Fail("Module operation cancelled; no incomplete generation was activated.", 130); }
        catch (ModuleRecipeUnavailableException ex) { return ErrorHandler.Fail(ex.Message, 2); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return ErrorHandler.Fail($"Module operation failed: {ex.Message}", 3);
        }
    }

    /// <summary>Prints the fixed module management syntax.</summary>
    public static void PrintHelp()
    {
        ConsoleTheme.Section(AnsiConsole.Console, "Optional modules / command reference");
        Console.WriteLine("Usage: odconv modules list");
        Console.WriteLine("       odconv modules <status|install|verify|repair|uninstall> <separation|transcription>");
        Console.WriteLine("States: Not installed, Installing, Ready, Needs repair. Checks are offline; no implicit downloads.");
        Console.WriteLine("Install/repair explicitly download the pinned CPU engine, dependencies and model. No administrator rights required.");
        Console.WriteLine("Storage: %LOCALAPPDATA%/OD.Converter/modules/<module>. CPU baseline; no global PATH changes.");
    }

    private static void PrintStatus(ModuleStatus status)
    {
        ConsoleTheme.ModuleStatuses(AnsiConsole.Console, [status]);
        AnsiConsole.MarkupLine($"[grey]Location: {Markup.Escape(status.Location)}[/]");
    }

    private sealed class ConsoleProgress : IProgress<ModuleProgress>
    {
        private string? lastStage;
        private long lastBytes;

        public void Report(ModuleProgress value)
        {
            if (lastStage == value.Stage && value.BytesReceived - lastBytes < 4 * 1024 * 1024 &&
                value.BytesReceived != value.TotalBytes) return;
            lastStage = value.Stage;
            lastBytes = value.BytesReceived;
            AnsiConsole.MarkupLine($"[cyan1]{Markup.Escape(value.TotalBytes is long total
                ? $"{value.Stage}: {value.BytesReceived:N0} / {total:N0} bytes"
                : value.Stage)}[/]");
        }
    }
}
