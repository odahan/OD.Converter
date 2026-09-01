using OD.Converter.Commands;
using OD.Converter.Services;
using Spectre.Console;

// Set up core services
var rawArgs = args;
var fileSystem = new FileSystem();
var validator = new ConversionOptionsValidator(fileSystem);
var ffmpegLocator = new FfmpegLocator();
var ffmpegConverter = new FfmpegConverter(validator);
var ffprobeService = new FfprobeService();

// If no arguments or a file path is provided, enter interactive mode
if (rawArgs.Length == 0 || ShouldRunInteractive(rawArgs))
{
    string? initialInput = rawArgs.Length == 1 ? rawArgs[0].Trim().Trim('"') : null;

    // Validate the input file if given
    if (initialInput != null && !File.Exists(initialInput) && !initialInput.StartsWith('-'))
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] Source file not found: \"{Markup.Escape(initialInput)}\"");
        return 1;
    }

    // Check for FFmpeg override passed to interactive mode
    string? ffmpegOverride = null;
    for (int i = 0; i < rawArgs.Length - 1; i++)
        if (rawArgs[i] == "--ffmpeg") ffmpegOverride = rawArgs[i + 1];

    var interactiveConverter = new InteractiveConverter(ffmpegLocator, ffmpegConverter, ffprobeService, fileSystem, ffmpegConverter, validator);
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    return await interactiveConverter.RunAsync(initialInput, ffmpegOverride, cts.Token);
}

// Parse the command name and remaining arguments
var cmd = rawArgs[0].ToLowerInvariant();
string[] rest = rawArgs.Skip(1).ToArray();

using var cts2 = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts2.Cancel(); };

int exit;
switch (cmd)
{
    case "convert":
        exit = await new ConvertCommand(ffmpegConverter, ffmpegLocator, fileSystem, ffmpegConverter, validator).RunAsync(rest, cts2.Token);
        break;
    case "probe":
    case "info":
        exit = await new ProbeCommand(ffmpegLocator, fileSystem, ffprobeService).RunAsync(rest, cts2.Token);
        break;
    case "formats":
        exit = await new FormatsCommand().RunAsync(rest, cts2.Token);
        break;
    case "--help": case "-h": case "-?": case "help":
        PrintRootHelp(); exit = 0; break;
    case "--version": case "-v": case "version":
        Console.WriteLine($"odconv {ApplicationInfo.Version} (.NET 10 win-x64, FFmpeg 8.1.2)");
        exit = 0; break;
    default:
        AnsiConsole.MarkupLine($"[red]Error:[/] Unknown command: \"{Markup.Escape(cmd)}\"");
        PrintRootHelp();
        exit = 1;
        break;
}
return exit;

// Determines whether the given arguments indicate an interactive session
static bool ShouldRunInteractive(string[] a)
{
    if (a.Length == 0) return true;
    if (a.Length == 1)
    {
        var p = a[0].Trim().Trim('"');
        if (File.Exists(p)) return true;
        // If it looks like a file path (contains dot, backslash, or slash) and is not a command, treat as interactive
        if (!p.StartsWith('-') && !IsSubcommand(p) && (p.Contains('.') || p.Contains('\\') || p.Contains('/')))
            return true;
    }
    return false;
}

// Checks if a string is a known subcommand
static bool IsSubcommand(string s) =>
    s is "convert" or "probe" or "info" or "formats" or "help" or "--help" or "-h" or "--version" or "-?";

// Prints the root help text
static void PrintRootHelp()
{
    AnsiConsole.WriteLine("odconv — audio converter");
    AnsiConsole.WriteLine("");
    AnsiConsole.WriteLine("Usage :");
    AnsiConsole.WriteLine("  odconv <file>                    Open the interactive menu");
    AnsiConsole.WriteLine("  odconv convert <file>... --to <fmt> [options]    Convert files from the command line");
    AnsiConsole.WriteLine("  odconv probe <f> [--json]        Show file metadata");
    AnsiConsole.WriteLine("  odconv formats                   List supported formats");
    AnsiConsole.WriteLine("");
    AnsiConsole.WriteLine("Options for convert:");
    AnsiConsole.WriteLine("  --output-dir <dir>   Output directory (preserves structure)");
    AnsiConsole.WriteLine("  --recursive          Process subdirectories recursively");
    AnsiConsole.WriteLine("  --pattern <glob>     File pattern, e.g. \"*.wav\"");
    AnsiConsole.WriteLine("  --help               Show this help");
    AnsiConsole.WriteLine("");
    AnsiConsole.WriteLine("Exemples :");
    AnsiConsole.WriteLine("  odconv song.wav                  # menu interactif");
    AnsiConsole.WriteLine("  odconv convert in.wav --to mp3 --bitrate 320k -o out.mp3");
    AnsiConsole.WriteLine("  odconv convert in.flac --to ogg --quality 6 --mono");
    AnsiConsole.WriteLine("  odconv convert in.wav --to flac --compression 5");
    AnsiConsole.WriteLine("  odconv convert *.wav --to mp3 --output-dir ./mp3  # batch");
    AnsiConsole.WriteLine("  odconv probe track.mp3 --json");
    AnsiConsole.WriteLine("");
    AnsiConsole.WriteLine("Formats: wav, flac, mp3, ogg, opus, aac, m4a, aiff, wma, wv, alac");
}
