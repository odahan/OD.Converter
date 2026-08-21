using OD.Converter.Models;
using OD.Converter.Services;
using Spectre.Console;

namespace OD.Converter.Commands;

public static class ConvertCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        // args after "convert": [input] [options]
        string? input = null;
        string? to = null;
        string? output = null;
        string? bitrate = null;
        int? quality = null;
        int? sr = null;
        int? ch = null;
        bool mono = false;
        int? bitDepth = null;
        int? comp = null;
        string overwrite = "never";
        string? ffmpegOverride = null;
        bool dryRun = false;

        var positional = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--to": case "-t": to = Next(args, ref i, "--to"); break;
                case "--output": case "-o": output = Next(args, ref i, "--output"); break;
                case "--bitrate": case "-b": bitrate = Next(args, ref i, "--bitrate"); break;
                case "--quality": case "-q": quality = int.Parse(Next(args, ref i, "--quality")); break;
                case "--sample-rate": sr = int.Parse(Next(args, ref i, "--sample-rate")); break;
                case "--channels": ch = int.Parse(Next(args, ref i, "--channels")); break;
                case "--mono": mono = true; break;
                case "--bit-depth": bitDepth = int.Parse(Next(args, ref i, "--bit-depth")); break;
                case "--compression": comp = int.Parse(Next(args, ref i, "--compression")); break;
                case "--overwrite": overwrite = Next(args, ref i, "--overwrite"); break;
                case "--ffmpeg": ffmpegOverride = Next(args, ref i, "--ffmpeg"); break;
                case "--dry-run": dryRun = true; break;
                case "--help": case "-h": case "-?": PrintHelp(); return 0;
                default:
                    if (a.StartsWith("-"))
                    {
                        ErrorHandler.Fail($"option inconnue : \"{a}\". Utilisez --help");
                        return 1;
                    }
                    positional.Add(a);
                    break;
            }
        }

        if (positional.Count > 0) input = positional[0];
        if (positional.Count > 1)
        {
            ErrorHandler.Fail($"trop d'arguments positionnels : \"{string.Join(" ", positional.Skip(1))}\"");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            ErrorHandler.Fail("fichier source manquant. Usage : odconv convert <fichier> --to <format> [-o sortie]");
            return 1;
        }
        if (string.IsNullOrWhiteSpace(to))
        {
            ErrorHandler.Fail("format cible manquant. Utilisez --to <format> (ex: --to mp3). Formats : " + FormatRegistry.SupportedList);
            return 1;
        }

        input = input.Trim().Trim('"');
        to = to.Trim().TrimStart('.').ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(output)) output = output.Trim().Trim('"');

        if (!File.Exists(input))
        {
            ErrorHandler.Fail($"fichier introuvable : \"{input}\"");
            return 1;
        }
        var fmt = FormatRegistry.Get(to);
        if (fmt is null)
        {
            ErrorHandler.Fail($"format non supporté : \"{to}\". Formats : {FormatRegistry.SupportedList}");
            return 1;
        }
        if (overwrite != "never" && overwrite != "always")
        {
            ErrorHandler.Fail($"valeur --overwrite invalide : \"{overwrite}\" (attendu: never|always)");
            return 1;
        }
        if (ch.HasValue && ch != 1 && ch != 2 && ch != 6)
        {
            ErrorHandler.Fail($"--channels invalide : {ch} (attendu 1, 2 ou 6)");
            return 1;
        }
        if (mono && ch.HasValue && ch != 1)
        {
            AnsiConsole.MarkupLine("[yellow]Avertissement :[/] --mono prioritaire sur --channels, sortie mono.");
        }

        if (string.IsNullOrWhiteSpace(output))
            output = mono ? FormatRegistry.DefaultOutputPathMono(input, to) : FormatRegistry.DefaultOutputPath(input, to);
        else if (string.IsNullOrWhiteSpace(Path.GetExtension(output)))
            output += fmt.Extension;

        if (File.Exists(output) && overwrite == "never" && !dryRun)
        {
            ErrorHandler.Fail($"le fichier de sortie existe déjà : \"{output}\". Utilisez --overwrite always ou choisissez un autre nom.");
            return 1;
        }

        var outDir = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            try { Directory.CreateDirectory(outDir); }
            catch (Exception ex)
            {
                ErrorHandler.Fail($"impossible de créer le dossier de sortie \"{outDir}\" : {ex.Message}");
                return 1;
            }
        }

        string ffmpeg;
        try { ffmpeg = FfmpegLocator.Resolve(ffmpegOverride); }
        catch (Exception ex) { ErrorHandler.Fail(ex.Message); return 2; }

        var opts = new ConversionOptions
        {
            InputPath = input,
            OutputPath = output,
            TargetFormat = to,
            Bitrate = bitrate,
            Quality = quality,
            SampleRate = sr,
            Channels = ch,
            Mono = mono,
            BitDepth = bitDepth,
            CompressionLevel = comp,
            Overwrite = overwrite,
            FfmpegPath = ffmpegOverride,
            DryRun = dryRun,
        };

        List<string> ffArgs;
        try { ffArgs = FfmpegConverter.BuildArguments(opts); }
        catch (Exception ex) { ErrorHandler.Fail(ex.Message); return 1; }

        var cmdLine = FfmpegConverter.BuildCommandLine(ffmpeg, ffArgs);
        if (dryRun)
        {
            AnsiConsole.MarkupLine("[grey]Dry-run :[/]");
            Console.WriteLine(cmdLine);
            return 0;
        }

        AnsiConsole.MarkupLine($"[grey]Conversion :[/] {Markup.Escape(Path.GetFileName(input))} → {Markup.Escape(Path.GetFileName(output))} [[{fmt.Key}]]");

        try
        {
            var (code, stderr) = await FfmpegConverter.RunWithOutputAsync(ffmpeg, ffArgs, ct);
            if (code == 0)
            {
                if (!File.Exists(output))
                {
                    ErrorHandler.Fail($"conversion terminée mais fichier de sortie introuvable : \"{output}\"");
                    return 3;
                }
                var fi = new FileInfo(output);
                AnsiConsole.MarkupLine($"[green]OK[/] {Markup.Escape(output)} ({fi.Length / 1024} Ko)");
                return 0;
            }
            else
            {
                var last = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";
                if (last.Length > 300) last = last[..300];
                var detail = string.IsNullOrWhiteSpace(last) ? $"ffmpeg code {code}" : last;
                ErrorHandler.Fail($"conversion échouée ({detail}). Vérifiez le fichier source ou essayez un autre format.");
                try { if (File.Exists(output) && new FileInfo(output).Length == 0) File.Delete(output); } catch { }
                return 3;
            }
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Conversion annulée.[/]");
            return 130;
        }
        catch (Exception ex)
        {
            ErrorHandler.Fail($"erreur inattendue : {ex.Message}");
            return 3;
        }
    }

    private static string Next(string[] args, ref int i, string name)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"valeur manquante pour {name}");
        return args[++i];
    }

    public static void PrintHelp()
    {
        AnsiConsole.WriteLine("Usage: odconv convert <fichier> --to <format> [options]");
        AnsiConsole.WriteLine("");
        AnsiConsole.WriteLine("Formats : " + FormatRegistry.SupportedList);
        AnsiConsole.WriteLine("");
        AnsiConsole.WriteLine("Options :");
        AnsiConsole.WriteLine("  -t, --to <fmt>        Format cible (requis)");
        AnsiConsole.WriteLine("  -o, --output <f>      Fichier sortie (défaut: même nom + ext)");
        AnsiConsole.WriteLine("  -b, --bitrate <k>     Bitrate ex: 320k, 192k (lossy)");
        AnsiConsole.WriteLine("  -q, --quality <n>     Qualité VBR (mp3 0-9, ogg 0-10)");
        AnsiConsole.WriteLine("      --sample-rate <n> 44100, 48000...");
        AnsiConsole.WriteLine("      --channels <n>    1, 2, 6");
        AnsiConsole.WriteLine("      --mono            Forcer mono (1 canal)");
        AnsiConsole.WriteLine("      --bit-depth <n>   16,24,32 (wav/aiff)");
        AnsiConsole.WriteLine("      --compression <n> 0-8 (flac)");
        AnsiConsole.WriteLine("      --overwrite <v>   never|always (défaut never)");
        AnsiConsole.WriteLine("      --ffmpeg <path>   Chemin ffmpeg.exe");
        AnsiConsole.WriteLine("      --dry-run         Afficher commande sans exécuter");
    }
}
