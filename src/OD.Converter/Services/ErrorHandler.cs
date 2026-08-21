using Spectre.Console;

namespace OD.Converter.Services;

public static class ErrorHandler
{
    public static int Fail(string message, int exitCode = 1)
    {
        AnsiConsole.MarkupLine($"[red]Erreur :[/] {Markup.Escape(message)}");
        return exitCode;
    }

    public static void FailInteractive(string message)
    {
        AnsiConsole.MarkupLine($"[red]Erreur :[/] {Markup.Escape(message)}");
        AnsiConsole.MarkupLine("[grey]Appuyez sur une touche pour continuer...[/]");
        try { Console.ReadKey(true); } catch { Console.ReadLine(); }
    }
}
