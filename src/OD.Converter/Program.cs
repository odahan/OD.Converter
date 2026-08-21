using OD.Converter.Commands;
using OD.Converter.Services;
using Spectre.Console;

var rawArgs = args;

if (rawArgs.Length == 0 || ShouldRunInteractive(rawArgs))
{
    string? initialInput = rawArgs.Length == 1 ? rawArgs[0].Trim().Trim('"') : null;

    if (initialInput != null && !File.Exists(initialInput) && !initialInput.StartsWith('-'))
    {
        AnsiConsole.MarkupLine($"[red]Erreur :[/] fichier introuvable : \"{Markup.Escape(initialInput)}\"");
        return 1;
    }

    string? ffmpegOverride = null;
    for (int i = 0; i < rawArgs.Length - 1; i++)
        if (rawArgs[i] == "--ffmpeg") ffmpegOverride = rawArgs[i + 1];

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    return await InteractiveConverter.RunAsync(initialInput, ffmpegOverride, cts.Token);
}

var cmd = rawArgs[0].ToLowerInvariant();
string[] rest = rawArgs.Skip(1).ToArray();

using var cts2 = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts2.Cancel(); };

int exit;
switch (cmd)
{
    case "convert":
        exit = await ConvertCommand.RunAsync(rest, cts2.Token);
        break;
    case "probe":
    case "info":
        exit = await ProbeCommand.RunAsync(rest, cts2.Token);
        break;
    case "formats":
        exit = await FormatsCommand.RunAsync(rest, cts2.Token);
        break;
    case "--help": case "-h": case "-?": case "help":
        PrintRootHelp(); exit = 0; break;
    case "--version": case "-v": case "version":
        Console.WriteLine("odconv 1.0.0 (.NET 10 win-x64, ffmpeg 8.1.2)");
        exit = 0; break;
    default:
        AnsiConsole.MarkupLine($"[red]Erreur :[/] commande inconnue : \"{Markup.Escape(cmd)}\"");
        PrintRootHelp();
        exit = 1;
        break;
}
return exit;

static bool ShouldRunInteractive(string[] a)
{
    if (a.Length == 0) return true;
    if (a.Length == 1)
    {
        var p = a[0].Trim().Trim('"');
        if (File.Exists(p)) return true;
        if (!p.StartsWith('-') && !IsSubcommand(p) && (p.Contains('.') || p.Contains('\\') || p.Contains('/')))
            return true; // will show error if not found but still interactive path
    }
    return false;
}

static bool IsSubcommand(string s) =>
    s is "convert" or "probe" or "info" or "formats" or "help" or "--help" or "-h" or "--version" or "-?";

static void PrintRootHelp()
{
    AnsiConsole.WriteLine("odconv — convertisseur audio");
    AnsiConsole.WriteLine("");
    AnsiConsole.WriteLine("Usage :");
    AnsiConsole.WriteLine("  odconv <fichier>                 Ouvrir le menu interactif");
    AnsiConsole.WriteLine("  odconv convert <f> --to <fmt>    Conversion en ligne de commande");
    AnsiConsole.WriteLine("  odconv probe <f> [--json]        Infos fichier");
    AnsiConsole.WriteLine("  odconv formats                   Lister formats supportés");
    AnsiConsole.WriteLine("");
    AnsiConsole.WriteLine("Exemples :");
    AnsiConsole.WriteLine("  odconv song.wav                  # menu interactif");
    AnsiConsole.WriteLine("  odconv convert in.wav --to mp3 --bitrate 320k -o out.mp3");
    AnsiConsole.WriteLine("  odconv convert in.flac --to ogg --quality 6 --mono");
    AnsiConsole.WriteLine("  odconv convert in.wav --to flac --compression 5");
    AnsiConsole.WriteLine("  odconv probe track.mp3 --json");
    AnsiConsole.WriteLine("");
    AnsiConsole.WriteLine("Formats : wav, flac, mp3, ogg, opus, aac, m4a, aiff, wma, wv, alac");
}
