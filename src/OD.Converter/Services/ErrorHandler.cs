using Spectre.Console;

namespace OD.Converter.Services;

/// <summary>Displays consistent command-line errors.</summary>
public static class ErrorHandler
{
    /// <summary>Displays an error and returns the requested exit code.</summary>
    public static int Fail(string message, int exitCode = 1)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
        return exitCode;
    }

    /// <summary>Displays an error and waits for user input in interactive mode.</summary>
    public static void FailInteractive(string message)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
        AnsiConsole.MarkupLine("[grey]Press any key to continue...[/]");
        try { Console.ReadKey(true); } catch { Console.ReadLine(); }
    }
}
