using System.Security.Cryptography;
using System.Text.Json;
using OD.Converter.Commands;
using OD.Converter.Models;
using OD.Converter.Services;
using OD.Converter.Services.Modules;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: TranscriptionValidation <module-root> <verified-artifact-directory> <lot4-fixture-directory|english-audio-file> <new-output-directory>");
    return 1;
}

var root = Path.GetFullPath(args[3]);
if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
    throw new ArgumentException("Validation requires a new, empty output directory.");
Directory.CreateDirectory(root);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, value) => { value.Cancel = true; cancellation.Cancel(); };
var ct = cancellation.Token;
var manager = new ModuleManager(new ModulePaths(args[0]), new OfflineArtifacts(args[1]), BuiltInModuleRecipes.Create());
await manager.InstallAsync(ModuleId.Transcription, false, null, ct);
var converter = new FfmpegConverter(new ConversionOptionsValidator(new FileSystem()));
var ffmpeg = Path.GetFullPath("src/OD.Converter/tools/ffmpeg.exe");
var ffprobe = Path.GetFullPath("src/OD.Converter/tools/ffprobe.exe");
var silence = Path.Combine(root, "silence é.wav");
var generated = await converter.RunAsync(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i",
    "anullsrc=r=48000:cl=stereo:d=4", "-c:a", "pcm_s16le", silence], ct);
if (generated.ExitCode != 0) throw new IOException(generated.StandardError);
var cases = File.Exists(args[2])
    ? new[] { (Input: Path.GetFullPath(args[2]), Action: "transcribe", Language: "en", Track: (int?)null) }
    : new[]
{
    (Input: silence, Action: "transcribe", Language: "auto", Track: (int?)null),
    (Input: Path.Combine(args[2], "narration musicale é.wav"), Action: "transcribe", Language: "fr", Track: (int?)null),
    (Input: Path.Combine(args[2], "alternating interview.wav"), Action: "transcribe", Language: "auto", Track: (int?)null),
    (Input: Path.Combine(args[2], "delayed second track é.mkv"), Action: "transcribe", Language: "fr", Track: (int?)2),
    (Input: Path.Combine(args[2], "synthetic ambience.wav"), Action: "transcribe", Language: "auto", Track: (int?)null),
    (Input: Path.Combine(args[2], "narration musicale é.wav"), Action: "separate-transcribe", Language: "fr", Track: (int?)null)
};
var report = new List<object>();
var failed = false;
foreach (var item in cases)
{
    var arguments = new List<string> { Path.GetFullPath(item.Input), "--action", item.Action, "--language", item.Language,
        "--output-dir", Path.Combine(root, "exports"), "--prefix", Path.GetFileNameWithoutExtension(item.Input) + "-" + item.Action };
    if (item.Track.HasValue) arguments.AddRange(["--audio-track", item.Track.Value.ToString()]);
    var request = ProcessCommand.ParseArguments(arguments.ToArray());
    var source = (await new FfprobeService().ProbeAsync(request.InputPath, ffprobe, ct)).Source!;
    var plan = new ProcessingPlan(request, source, source.SelectAudioTrack(request.AudioStreamIndex));
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var result = await new AudioTranscriptionService(manager, new ModuleProcessRunner(Console.Write), converter, converter)
        .TranscribeAsync(plan, ffmpeg, ct);
    var srt = result.Outputs.SingleOrDefault(output => output.Kind == ExportKind.Subtitles);
    var cues = srt is null ? null : TranscriptFormat.Parse(await File.ReadAllTextAsync(srt.Path, ct), source.Timeline.TargetDurationSeconds!.Value);
    var expectedNoSpeech = item.Input == silence || Path.GetFileName(item.Input) == "synthetic ambience.wav";
    var speechExpectationMet = !expectedNoSpeech || cues is { Count: 0 };
    report.Add(new { request.InputPath, request.Action, request.Language, Track = plan.Track.StreamIndex,
        source.Timeline.TargetDurationSeconds, result.ExitCode, result.ErrorMessage, result.Warnings,
        Seconds = watch.Elapsed.TotalSeconds, ExpectedNoSpeech = expectedNoSpeech,
        SpeechExpectationMet = speechExpectationMet, Cues = cues, result.Outputs });
    failed |= result.ExitCode != 0 || !speechExpectationMet;
    await File.WriteAllTextAsync(Path.Combine(root, "results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), ct);
}
return failed ? 3 : 0;

/// <summary>Reuses only artifacts matching the pinned size and complete SHA-256, without network access.</summary>
sealed class OfflineArtifacts(string directory) : IModuleDownloader
{
    public async Task DownloadAsync(ModuleDownload artifact, string destination, IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (new FileInfo(file).Length != artifact.SizeBytes) continue;
            await using var stream = File.OpenRead(file);
            if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, destination);
            return;
        }
        throw new FileNotFoundException($"Pinned validation artifact unavailable locally: {artifact.Destination}");
    }
}
