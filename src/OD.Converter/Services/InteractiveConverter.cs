using OD.Converter.Models;
using Spectre.Console;

namespace OD.Converter.Services;

public static class InteractiveConverter
{
    public static async Task<int> RunAsync(string? initialInput, string? ffmpegOverride, CancellationToken ct)
    {
        while (true)
        {
            Console.Clear();
            AnsiConsole.Write(new FigletText("odconv").Color(Color.Cyan1));
            AnsiConsole.MarkupLine("[grey]Conversion audio interactive — laissez vide + Entrée pour quitter[/]");

            string inputPath = initialInput ?? "";
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                inputPath = AnsiConsole.Prompt(
                    new TextPrompt<string>("Fichier source [grey](glisser-déposer ou chemin)[/] :")
                        .AllowEmpty()
                        .Validate(p =>
                        {
                            if (string.IsNullOrWhiteSpace(p)) return ValidationResult.Success();
                            var t = p.Trim().Trim('"');
                            return File.Exists(t) ? ValidationResult.Success() : ValidationResult.Error("Fichier introuvable");
                        }));
                inputPath = inputPath.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(inputPath))
                    return 0;
            }
            else
            {
                inputPath = inputPath.Trim().Trim('"');
            }

            if (!File.Exists(inputPath))
            {
                ErrorHandler.FailInteractive($"fichier introuvable : \"{inputPath}\"");
                initialInput = null;
                continue;
            }

            // probe
            ProbeInfo? probe = null;
            try
            {
                var ffprobe = FfmpegLocator.ResolveFfprobe(ffmpegOverride);
                probe = await FfprobeService.TryProbeAsync(inputPath, ffprobe, ct);
            }
            catch { }

            if (probe is not null)
                AnsiConsole.MarkupLine($"[grey]Source : {Markup.Escape(FfprobeService.FormatProbeHuman(probe))} — {Markup.Escape(Path.GetFileName(inputPath))}[/]");
            else
                AnsiConsole.MarkupLine($"[grey]Source : {Markup.Escape(Path.GetFileName(inputPath))}[/]");

            var format = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Format cible :")
                    .AddChoices(FormatRegistry.All.Select(f => f.Key).ToArray()));

            var fmtInfo = FormatRegistry.Get(format)!;

            string? bitrate = null;
            int? quality = null;
            int? compression = null;
            int? bitDepth = null;

            if (!fmtInfo.IsLossless)
            {
                if (format == "mp3")
                {
                    var brChoice = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("Bitrate MP3 :")
                            .AddChoices(new[] { "320k (recommandé)", "256k", "192k", "128k", "VBR q0 (qualité max)", "VBR q2" }));
                    if (brChoice.Contains("VBR q0")) quality = 0;
                    else if (brChoice.Contains("VBR q2")) quality = 2;
                    else bitrate = brChoice.Split(' ')[0];
                }
                else if (format == "ogg")
                {
                    var qChoice = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("Qualité Ogg Vorbis (0-10) :")
                            .AddChoices(new[] { "6 (~192k, recommandé)", "8 (~256k)", "4 (~128k)", "10 (max)", "2 (faible)" }));
                    var qStr = qChoice.Split(' ')[0];
                    quality = int.Parse(qStr);
                }
                else if (format == "opus")
                {
                    bitrate = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("Bitrate Opus :")
                            .AddChoices(new[] { "128k (recommandé)", "96k", "64k", "192k", "256k" })
                    ).Split(' ')[0];
                }
                else // aac/m4a/wma
                {
                    bitrate = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title($"Bitrate {format} :")
                            .AddChoices(new[] { "192k (recommandé)", "128k", "256k", "320k" })
                    ).Split(' ')[0];
                }
            }
            else
            {
                if (format == "flac")
                {
                    compression = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("Compression FLAC (0-8) :")
                            .AddChoices(new[] { "5 (recommandé)", "8 (max)", "0 (rapide)", "3" })
                    ).Split(' ')[0] is var s && int.TryParse(s, out var lv) ? lv : 5;
                }
                if (format == "wav" || format == "aiff")
                {
                    var bd = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("Profondeur :")
                            .AddChoices(new[] { "16 bit (recommandé)", "24 bit", "32 bit" }));
                    bitDepth = bd.StartsWith("24") ? 24 : bd.StartsWith("32") ? 32 : 16;
                }
            }

            var mono = AnsiConsole.Confirm("Mono (downmix 1 canal) ?", defaultValue: probe?.Channels == 1);

            int? sampleRate = null;
            var keepSr = AnsiConsole.Confirm("Conserver la fréquence d'échantillonnage source ?", true);
            if (!keepSr)
            {
                var srChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Fréquence :")
                        .AddChoices(new[] { "44100", "48000", "22050", "96000" }));
                sampleRate = int.Parse(srChoice);
            }

            var defaultOut = mono
                ? FormatRegistry.DefaultOutputPathMono(inputPath, format)
                : FormatRegistry.DefaultOutputPath(inputPath, format);

            var outputPath = AnsiConsole.Prompt(
                new TextPrompt<string>($"Fichier de sortie [[défaut {Path.GetFileName(defaultOut)}]] :")
                    .AllowEmpty()
                    .DefaultValue(defaultOut));

            outputPath = string.IsNullOrWhiteSpace(outputPath) ? defaultOut : outputPath.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(Path.GetExtension(outputPath)))
                outputPath += fmtInfo.Extension;

            if (File.Exists(outputPath))
            {
                var ow = AnsiConsole.Confirm($"Le fichier existe déjà : \"{Path.GetFileName(outputPath)}\". Écraser ?", false);
                if (!ow)
                {
                    initialInput = inputPath;
                    continue;
                }
            }

            // Récap
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Option"); table.AddColumn("Valeur");
            table.AddRow("Source", Markup.Escape(inputPath));
            table.AddRow("Cible", $"{format} → {Markup.Escape(outputPath)}");
            if (bitrate != null) table.AddRow("Bitrate", bitrate);
            if (quality != null) table.AddRow("Qualité", quality.Value.ToString());
            if (bitDepth != null) table.AddRow("Bit depth", $"{bitDepth} bit");
            if (compression != null) table.AddRow("Compression", compression.Value.ToString());
            table.AddRow("Mono", mono ? "oui" : "non");
            if (sampleRate != null) table.AddRow("Sample rate", sampleRate.Value.ToString());
            AnsiConsole.Write(table);

            if (!AnsiConsole.Confirm("Lancer la conversion ?", true))
            {
                initialInput = inputPath;
                continue;
            }

            string ffmpeg;
            try { ffmpeg = FfmpegLocator.Resolve(ffmpegOverride); }
            catch (Exception ex) { ErrorHandler.FailInteractive(ex.Message); return 2; }

            var opts = new ConversionOptions
            {
                InputPath = inputPath,
                OutputPath = outputPath,
                TargetFormat = format,
                Bitrate = bitrate,
                Quality = quality,
                SampleRate = sampleRate,
                Mono = mono,
                BitDepth = bitDepth,
                CompressionLevel = compression,
                Overwrite = "always",
                FfmpegPath = ffmpegOverride,
            };

            List<string> args;
            try { args = FfmpegConverter.BuildArguments(opts); }
            catch (Exception ex) { ErrorHandler.FailInteractive(ex.Message); continue; }

            AnsiConsole.MarkupLine("[grey]Conversion en cours...[/]");
            var (code, stderr) = await FfmpegConverter.RunWithOutputAsync(ffmpeg, args, ct);
            if (code == 0)
            {
                var fi = new FileInfo(outputPath);
                AnsiConsole.MarkupLine($"[green]OK[/] {Markup.Escape(outputPath)} ({fi.Length / 1024} Ko)");
            }
            else
            {
                var msg = string.IsNullOrWhiteSpace(stderr) ? $"ffmpeg code {code}" : stderr.Split('\n').Last(s => !string.IsNullOrWhiteSpace(s)).Trim();
                if (msg.Length > 300) msg = msg[..300];
                ErrorHandler.FailInteractive($"conversion échouée : {msg}");
                initialInput = inputPath;
                continue;
            }

            if (!AnsiConsole.Confirm("Convertir un autre fichier ?", false))
                return 0;

            initialInput = null;
        }
    }
}
