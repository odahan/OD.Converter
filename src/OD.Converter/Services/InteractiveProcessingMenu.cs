using System.Globalization;
using System.Diagnostics;
using OD.Converter.Commands;
using OD.Converter.Models;
using OD.Converter.Services.Modules;
using Spectre.Console;

namespace OD.Converter.Services;

/// <summary>Presents common audio/video workflows and explicit optional module operations.</summary>
public sealed class InteractiveProcessingMenu(
    IFfmpegLocator locator, IFileSystem files, IFfprobeService probe, InteractiveConverter legacyConverter,
    ModuleManager modules, IAnsiConsole console, UserGuideService? userGuide = null, Action<string>? openExternalLink = null)
{
    /// <summary>Runs the main menu; processing and module commands retain their shared CLI implementations.</summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        promptCancellation = ct;
        try
        {
            ConsoleTheme.Header(console);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                ConsoleTheme.Section(console, "Main menu", "Use Up/Down to choose, Enter to open. Ctrl+C cancels the current session.");
                var choice = await SelectAsync("[bold]What would you like to do?[/]", ["Process audio / video", "Convert audio (classic)", "Optional modules", "User guide / Guide utilisateur", "Buy me a coffee", "Exit"]);
                if (choice == "Exit") return 0;
                if (choice == "User guide / Guide utilisateur") OpenGuide();
                else if (choice == "Buy me a coffee") await SupportProjectAsync();
                else if (choice == "Optional modules") await ManageModulesAsync(ct);
                else if (choice == "Convert audio (classic)")
                {
                    if (await legacyConverter.RunAsync(null, null, ct) == 130) return 130;
                }
                else await ProcessAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            console.MarkupLine("[yellow]Cancelled. Completed exports reported above are retained.[/]");
            return 130;
        }
    }

    private async Task SupportProjectAsync()
    {
        ConsoleTheme.Section(console, "Buy me a coffee", "Support OD.Converter / Olivier Dahan · KNHSynths");
        console.Write(new Panel(new Markup(
            "[bold yellow]Enjoying OD.Converter?[/]\n\n" +
            "If OD.Converter is useful to you, you can support its development on Buy Me a Coffee.\n" +
            "OD.Converter is DonationWare. Contributions of any amount are voluntary and appreciated.\n\n" +
            $"[underline cyan1]{ApplicationInfo.SupportUrl}[/]"))
            .Header("[bold yellow]Thank you for your support[/]").Border(BoxBorder.Rounded).BorderColor(Color.Yellow));

        if (await SelectAsync("Support the project:", ["Open Buy Me a Coffee", "Back"]) == "Back") return;
        promptCancellation.ThrowIfCancellationRequested();
        try
        {
            if (openExternalLink is not null) openExternalLink(ApplicationInfo.SupportUrl);
            else
            {
                using var browser = Process.Start(new ProcessStartInfo(ApplicationInfo.SupportUrl) { UseShellExecute = true });
            }
            ConsoleTheme.Success(console, "Buy Me a Coffee opened in your default browser.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            console.MarkupLine($"[red]Unable to open Buy Me a Coffee:[/] {Markup.Escape(ex.Message)}");
            console.MarkupLine($"[grey]Open this address manually: {ApplicationInfo.SupportUrl}[/]");
        }
    }

    private void OpenGuide()
    {
        ConsoleTheme.Section(console, "User guide / Guide utilisateur", "Offline English and French help opens in your default browser.");
        try
        {
            var path = (userGuide ?? new UserGuideService()).Open();
            ConsoleTheme.Success(console, $"User guide opened: {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            console.MarkupLine($"[red]Unable to open the user guide:[/] {Markup.Escape(ex.Message)}");
            console.MarkupLine("[grey]Keep the EXE in a writable folder. If the HTML file was extracted, open it manually in your browser.[/]");
        }
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        try
        {
            ConsoleTheme.Section(console, "1 / Source & audio track", "Choose an audio file or a video soundtrack. The original source is preserved.");
            var input = await PromptAsync(new TextPrompt<string>("Source file [grey](audio or video; blank to go back)[/]:")
                .AllowEmpty().Validate(value => string.IsNullOrWhiteSpace(value) || files.FileExists(CleanPath(value))
                    ? ValidationResult.Success() : ValidationResult.Error("File not found.")));
            if (string.IsNullOrWhiteSpace(input)) return;
            input = CleanPath(input);
            var info = await probe.ProbeAsync(input, locator.ResolveFfprobe(null), ct);
            var source = info.Source ?? MediaProbeParser.Parse(info.RawJson);
            var track = source.SelectAudioTrack();
            var tracks = ConsoleTheme.CreateTable("Stream", "Codec / language / title", "Audio", "Duration (s)");
            foreach (var item in source.AudioTracks)
                tracks.AddRow(item.StreamIndex.ToString(CultureInfo.InvariantCulture),
                    Markup.Escape($"{item.CodecName} / {item.Language ?? "unknown"} / {item.Title ?? "untitled"}"),
                    $"{item.SampleRate} Hz / {item.Channels} channel(s)",
                    item.DurationSeconds?.ToString("0.###", CultureInfo.InvariantCulture) ?? "unknown");
            console.Write(tracks);
            if (source.AudioTracks.Count > 1)
                track = await PromptAsync(new SelectionPrompt<AudioTrackInfo>().Title("Audio track:")
                    .UseConverter(item => $"Stream {item.StreamIndex}: {Markup.Escape(item.Language ?? "unknown")} — {Markup.Escape(item.Title ?? item.CodecName)}")
                    .HighlightStyle(ConsoleTheme.SelectionStyle).AddChoices(source.AudioTracks));

            ConsoleTheme.Section(console, "2 / Treatment & exports");
            console.MarkupLine("[grey]Checking optional modules offline...[/]");
            var statuses = await GetStatusesAsync(ct);
            var available = new List<ProcessingAction>();
            foreach (var action in Enum.GetValues<ProcessingAction>())
            {
                var reason = ProcessingAvailability.UnavailableReason(action, statuses);
                if (reason is null) available.Add(action);
                else console.MarkupLine($"[grey]{Label(action)} — {Markup.Escape(reason)}[/]");
            }
            var labels = available.Select(Label).Append("Back").ToArray();
            var selected = await SelectAsync("Treatment:", labels);
            if (selected == "Back") return;
            var workflow = available.Single(action => Label(action) == selected);
            var exportPrompt = new MultiSelectionPrompt<ExportKind>().Title("Files to create [grey](Space to toggle, Enter to accept)[/]:")
                .HighlightStyle(ConsoleTheme.SelectionStyle).Required().UseConverter(kind => kind switch
                {
                    ExportKind.Original => "Original soundtrack",
                    ExportKind.Vocals => "Isolated voice / vocals",
                    ExportKind.Background => "Background / instrumental",
                    ExportKind.Transcript => "Transcript (TXT)",
                    _ => "Subtitles (SRT)"
                })
                .AddChoices(ProcessingConventions.AllowedExports(workflow));
            foreach (var kind in ProcessingConventions.DefaultExports(workflow)) exportPrompt.Select(kind);
            var exports = await PromptAsync(exportPrompt);
            var arguments = new List<string> { input, "--action", ActionName(workflow), "--audio-track", track.StreamIndex.ToString(CultureInfo.InvariantCulture),
                "--exports", string.Join(',', exports.Select(kind => kind.ToString().ToLowerInvariant())) };
            if (workflow is ProcessingAction.Transcribe or ProcessingAction.SeparateTranscribe)
            {
                console.MarkupLine("[grey]Choose the source language when known. Auto may omit a language in multilingual recordings; review TXT/SRT after processing.[/]");
                arguments.AddRange(["--language", await PromptAsync(new TextPrompt<string>("Source language [grey](auto or two-letter code, e.g. en, fr)[/]:")
                    .DefaultValue("auto").Validate(value => value == "auto" || (value.Length == 2 && value.All(character => character is >= 'a' and <= 'z'))
                        ? ValidationResult.Success() : ValidationResult.Error("Use auto or a lowercase two-letter code.")))]);
            }
            if (exports.Any(kind => kind is ExportKind.Original or ExportKind.Vocals or ExportKind.Background))
                await AddAudioOptionsAsync(arguments, track);
            ConsoleTheme.Section(console, "3 / Destination");
            arguments.AddRange(["--output-dir", CleanPath(await PromptAsync(new TextPrompt<string>("Output directory:").WithConverter(Markup.Escape)
                .DefaultValue(Path.GetDirectoryName(Path.GetFullPath(input))!))),
                "--prefix", await PromptAsync(new TextPrompt<string>("Filename prefix:").WithConverter(Markup.Escape).DefaultValue(Path.GetFileNameWithoutExtension(input))
                    .Validate(value =>
                    {
                        try { ProcessingConventions.ValidatePrefix(value); return ValidationResult.Success(); }
                        catch (ArgumentException ex) { return ValidationResult.Error(ex.Message); }
                    }))]);
            var request = ProcessCommand.ParseArguments(arguments.ToArray());
            var collisions = request.Exports.Select(kind => ProcessingConventions.OutputPath(request, kind)).Where(File.Exists).ToArray();
            if (collisions.Length > 0)
            {
                foreach (var path in collisions) console.MarkupLine($"[yellow]Already exists: {Markup.Escape(path)}[/]");
                if (!await ConfirmAsync("Overwrite these files after successful export?", false)) return;
                arguments.AddRange(["--overwrite", "always"]);
                request = ProcessCommand.ParseArguments(arguments.ToArray());
            }
            foreach (var kind in request.Exports) AudioExportService.ValidateDestination(request, kind);
            ConsoleTheme.Section(console, "4 / Review & start");
            var summary = ConsoleTheme.CreateTable("Option", "Value");
            summary.AddRow("Source", Markup.Escape(input));
            summary.AddRow("Audio stream", track.StreamIndex.ToString(CultureInfo.InvariantCulture));
            summary.AddRow("Treatment", Label(workflow));
            summary.AddRow("Source language", request.Language);
            foreach (var kind in request.Exports) summary.AddRow(kind.ToString(), Markup.Escape(ProcessingConventions.OutputPath(request, kind)));
            foreach (var pair in arguments.Skip(1).Chunk(2))
                if (pair.Length == 2 && pair[0] is "--to" or "--bitrate" or "--quality" or "--sample-rate" or "--channels" or "--bit-depth" or "--compression")
                    summary.AddRow(pair[0][2..], Markup.Escape(pair[1]));
            console.Write(summary);
            if (!await ConfirmAsync("Start processing?", true)) return;
            ct.ThrowIfCancellationRequested();
            console.MarkupLine("[grey]Processing locally. Press Ctrl+C to cancel. Steps and finalized files follow.[/]");
            var code = await new ProcessCommand(locator, files, probe).RunAsync(arguments.ToArray(), ct);
            console.MarkupLine(code == 0 ? "[green]Processing completed.[/]" : $"[yellow]Processing ended with exit code {code}. Review created and missing files above.[/]");
            if (code == 130) throw new OperationCanceledException(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            console.MarkupLine($"[red]Unable to process source:[/] {Markup.Escape(ex.Message)}");
        }
    }

    private async Task AddAudioOptionsAsync(List<string> arguments, AudioTrackInfo track)
    {
        var format = await SelectAsync("Audio export format [grey](WAV recommended for editing)[/]:", new[] { "wav" }.Concat(FormatRegistry.All.Select(item => item.Key).Where(key => key != "wav")));
        arguments.AddRange(["--to", format]);
        if (format is "wav" or "aiff") arguments.AddRange(["--bit-depth", await SelectAsync("PCM bit depth:", ["16", "24", "32"])]);
        else if (format == "flac") arguments.AddRange(["--compression", await SelectAsync("FLAC compression:", ["5", "8", "0", "3"])]);
        else if (format == "ogg") arguments.AddRange(["--quality", await SelectAsync("Ogg quality:", ["6", "8", "4", "10", "2"])]);
        else if (format == "mp3")
        {
            var mode = await SelectAsync("MP3 encoding:", ["320k", "256k", "192k", "128k", "VBR q0", "VBR q2"]);
            arguments.AddRange(mode.StartsWith("VBR", StringComparison.Ordinal)
                ? ["--quality", mode[^1..]] : ["--bitrate", mode]);
        }
        else if (FormatRegistry.Get(format)?.IsLossless == false)
            arguments.AddRange(["--bitrate", await SelectAsync("Audio bitrate:", format == "opus" ? ["128k", "96k", "64k", "192k", "256k"] : ["192k", "128k", "256k", "320k"])]);
        var channels = await SelectAsync($"Channels [grey](source: {track.Channels})[/]:", ["Keep source", "Mono", "Stereo", "6 channels"]);
        if (channels != "Keep source") arguments.AddRange(["--channels", channels == "Mono" ? "1" : channels == "Stereo" ? "2" : "6"]);
        var rate = await SelectAsync($"Sample rate [grey](source: {track.SampleRate} Hz)[/]:", ["Keep source", "44100", "48000", "22050", "96000"]);
        if (rate != "Keep source") arguments.AddRange(["--sample-rate", rate]);
    }

    private async Task ManageModulesAsync(CancellationToken ct)
    {
        while (true)
        {
            ConsoleTheme.Section(console, "Optional modules", "Demucs separates vocals; Whisper generates text and subtitles. All checks are offline.");
            console.MarkupLine("[grey]Checking optional modules offline...[/]");
            var statuses = await GetStatusesAsync(ct);
            ConsoleTheme.ModuleStatuses(console, statuses.Values);
            var name = await SelectAsync("Optional modules:", ["separation", "transcription", "Back"]);
            if (name == "Back") return;
            var operation = await SelectAsync($"{name}:", ["Verify", "Install", "Repair", "Uninstall", "Back"]);
            if (operation == "Back") continue;
            var id = name == "separation" ? ModuleId.Separation : ModuleId.Transcription;
            console.Write(new Panel(new Text(modules.Describe(id)))
                .Header("[bold cyan1]Module details[/]").Border(BoxBorder.Rounded).BorderColor(Color.Grey));
            if (operation is "Install" or "Repair" or "Uninstall")
                if (!await ConfirmAsync($"{operation} {name}?", false)) continue;
            var code = await new ModulesCommand(modules).RunAsync([operation.ToLowerInvariant(), name], ct);
            if (code == 130) throw new OperationCanceledException(ct);
        }
    }

    private async Task<Dictionary<ModuleId, ModuleStatus>> GetStatusesAsync(CancellationToken ct)
    {
        var statuses = new Dictionary<ModuleId, ModuleStatus>();
        foreach (var id in Enum.GetValues<ModuleId>()) statuses[id] = await modules.StatusAsync(id, ct);
        return statuses;
    }

    private CancellationToken promptCancellation;

    private Task<T> PromptAsync<T>(IPrompt<T> prompt) => prompt.ShowAsync(console, promptCancellation);

    private Task<bool> ConfirmAsync(string title, bool defaultValue) =>
        PromptAsync(new ConfirmationPrompt(title) { DefaultValue = defaultValue });

    private Task<string> SelectAsync(string title, IEnumerable<string> choices) =>
        PromptAsync(new SelectionPrompt<string>().Title(title).HighlightStyle(ConsoleTheme.SelectionStyle)
            .PageSize(12).UseConverter(choice => choice switch
            {
                "Process audio / video" => "[bold]Process audio / video[/]  [grey]Extract, separate, transcribe[/]",
                "Convert audio (classic)" => "[bold]Convert audio (classic)[/]  [grey]Guided format conversion[/]",
                "Optional modules" => "[bold]Optional modules[/]  [grey]Manage Demucs & Whisper[/]",
                "User guide / Guide utilisateur" => "[bold]User guide / Guide utilisateur[/]  [grey]EN / FR, offline[/]",
                "Buy me a coffee" => "[bold yellow]Buy me a coffee[/]  [grey]Support the project[/]",
                "Back" or "Exit" => $"[grey]{choice}[/]",
                _ => Markup.Escape(choice)
            }).AddChoices(choices));

    private static string CleanPath(string value) => value.Trim().Trim('"');

    private static string ActionName(ProcessingAction action) => action switch
    {
        ProcessingAction.Extract => "extract",
        ProcessingAction.Separate => "separate",
        ProcessingAction.Transcribe => "transcribe",
        ProcessingAction.SeparateTranscribe => "separate-transcribe",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static string Label(ProcessingAction action) => action switch
    {
        ProcessingAction.Extract => "Convert / extract audio",
        ProcessingAction.Separate => "Separate voice and background",
        ProcessingAction.Transcribe => "Generate transcript and subtitles",
        ProcessingAction.SeparateTranscribe => "Separate and transcribe",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}
