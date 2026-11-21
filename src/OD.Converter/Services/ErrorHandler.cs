using System.Globalization;
using Spectre.Console;

namespace OD.Converter.Services;

/// <summary>Displays consistent command-line errors.</summary>
public static class ErrorHandler
{
    /// <summary>Returns the last meaningful FFmpeg error line, limited to 300 text elements.</summary>
    internal static string FfmpegDetail(string standardError, int exitCode)
    {
        var error = standardError.AsSpan().TrimEnd();
        if (error.IsEmpty)
            return $"FFmpeg exited with code {exitCode}";

        var detail = error[(error.LastIndexOf('\n') + 1)..].TrimStart().ToString();
        const int maxTextElements = 300;
        if (detail.Length <= maxTextElements)
            return detail;

        var text = new StringInfo(detail);
        return text.LengthInTextElements > maxTextElements
            ? text.SubstringByTextElements(0, maxTextElements)
            : detail;
    }

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
