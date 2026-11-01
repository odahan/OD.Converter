using OD.Converter.Services.Modules;
using Spectre.Console;

namespace OD.Converter.Services;

/// <summary>Provides shared visual elements for interactive screens and human-readable commands.</summary>
public static class ConsoleTheme
{
    /// <summary>Gets the focused selection style, with contrast against the terminal background.</summary>
    public static Style SelectionStyle => Style.Parse("black on cyan1");

    /// <summary>Displays the application identity, adapting to narrow terminals.</summary>
    public static void Header(IAnsiConsole console)
    {
        console.WriteLine();
        if (console.Profile.Width >= 60)
            console.Write(new FigletText("odconv").LeftJustified().Color(Color.Cyan1));
        console.Write(new Rule($"[bold cyan1]OD.Converter[/] [grey]v{ApplicationInfo.Version}[/]")
            .LeftJustified().RuleStyle("grey"));
        console.MarkupLine("[grey]Audio & video toolkit  /  Convert · Extract · Separate · Transcribe[/]");
        console.WriteLine();
    }

    /// <summary>Separates a screen or workflow stage from previous output.</summary>
    public static void Section(IAnsiConsole console, string title, string? hint = null)
    {
        console.WriteLine();
        console.Write(new Rule($"[bold cyan1]{Markup.Escape(title)}[/]").LeftJustified().RuleStyle("grey"));
        if (hint is not null) console.MarkupLine($"[grey]{Markup.Escape(hint)}[/]");
        console.WriteLine();
    }

    /// <summary>Creates a consistently styled table with escaped column headings.</summary>
    public static Table CreateTable(params string[] columns)
    {
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        foreach (var column in columns) table.AddColumn(new TableColumn($"[bold cyan1]{Markup.Escape(column)}[/]"));
        return table;
    }

    /// <summary>Displays module availability with readable state names and actionable details.</summary>
    public static void ModuleStatuses(IAnsiConsole console, IEnumerable<ModuleStatus> statuses)
    {
        var table = CreateTable("Module", "State", "Details");
        foreach (var status in statuses)
        {
            var (label, color) = status.State switch
            {
                ModuleState.Ready => ("Ready", "green"),
                ModuleState.NotInstalled => ("Not installed", "grey"),
                ModuleState.Installing => ("Installing", "cyan1"),
                _ => ("Needs repair", "yellow")
            };
            table.AddRow($"[bold]{ModulePaths.Name(status.Id)}[/]", $"[{color}]{label}[/]", Markup.Escape(status.Message));
        }
        console.Write(table);
    }

    /// <summary>Displays a positive result without interpreting user text as markup.</summary>
    public static void Success(IAnsiConsole console, string message) => console.MarkupLine($"[bold green]OK[/]  {Markup.Escape(message)}");

    /// <summary>Displays a warning without interpreting user text as markup.</summary>
    public static void Warning(IAnsiConsole console, string message) => console.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(message)}");
}
