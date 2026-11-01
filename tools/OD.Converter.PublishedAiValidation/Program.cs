using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using OD.Converter.Services;
using OD.Converter.Services.Modules;

if (!OperatingSystem.IsWindows() || args.Length is not (5 or 6) || args.Length == 6 && args[5] != "--full-transcription")
{
    Console.Error.WriteLine("Usage: PublishedAiValidation <published-exe> <separation-module-directory> <transcription-module-directory> <source-video> <new-output-directory> [--full-transcription]");
    return 1;
}

var executable = Path.GetFullPath(args[0]);
var source = Path.GetFullPath(args[3]);
var root = Path.GetFullPath(args[4]);
var fullTranscription = args.Length == 6;
if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
    throw new ArgumentException("Validation requires a new, empty output directory.");
if (!File.Exists(executable) || !File.Exists(source)) throw new FileNotFoundException("Validation inputs are missing.");
var paths = new ModulePaths();
foreach (var id in Enum.GetValues<ModuleId>())
{
    if (Directory.Exists(paths.ModuleDirectory(id)))
        throw new IOException("Validation refuses existing personal modules. Use a dedicated standard account.");
    if (File.Exists(paths.LockFile(id)))
        using (File.Open(paths.LockFile(id), FileMode.Open, FileAccess.Read, FileShare.None)) { }
}
Directory.CreateDirectory(root);
var standalone = Path.Combine(root, "standalone été & [AI]");
Directory.CreateDirectory(standalone);
var published = Path.Combine(standalone, "odconv.exe");
File.Copy(executable, published);
var ffmpeg = Path.GetFullPath("src/OD.Converter/tools/ffmpeg.exe");
var ffprobe = Path.GetFullPath("src/OD.Converter/tools/ffprobe.exe");
var staged = new List<ModuleId>();
var commands = new List<object>();
var measurements = new List<object>();
var cleanup = new List<object>();
var exports = new List<(string Path, string Sha256)>();
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
var executableHash = await Hash(published);
var sourceHash = await Hash(source);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, value) => { value.Cancel = true; cancellation.Cancel(); };
string? error = null;
try
{
    var metadata = await Run(ffprobe, ["-v", "error", "-show_format", "-show_streams", "-of", "json", source]);
    await File.WriteAllTextAsync(Path.Combine(root, "source-probe.json"), metadata);
    using var probe = JsonDocument.Parse(metadata);
    var duration = double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
    if (!double.IsFinite(duration) || duration < 90) throw new InvalidDataException("Provide a video at least 90 seconds long.");
    var audio = probe.RootElement.GetProperty("streams").EnumerateArray().First(stream => stream.GetProperty("codec_type").GetString() == "audio");
    var streamIndex = audio.GetProperty("index").GetInt32().ToString(CultureInfo.InvariantCulture);
    var clips = new List<(string Path, string Name)>();
    if (fullTranscription)
    {
        clips.Add((source, "full"));
        measurements.Add(new { Check = "full-source", Name = "full", SourceOffsetSeconds = 0, DurationSeconds = duration, Sha256 = sourceHash });
    }
    foreach (var item in fullTranscription ? [] : new[] { (Name: "beginning", Offset: 0.0), (Name: "middle", Offset: (duration - 30) / 2), (Name: "ending", Offset: duration - 30) })
    {
        var clip = Path.Combine(root, item.Name + ".mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-ss", item.Offset.ToString("R", CultureInfo.InvariantCulture), "-i", source,
            "-t", "30", "-map", "0:v:0", "-map", "0:" + streamIndex, "-vf", "scale=320:-2", "-c:v", "mpeg4", "-q:v", "5", "-c:a", "alac", "-ar", "48000", "-ac", "2", clip]);
        measurements.Add(new { Check = "source-excerpt", item.Name, SourceOffsetSeconds = item.Offset, DurationSeconds = 30, Sha256 = await Hash(clip) });
        clips.Add((clip, item.Name));
    }

    var modules = fullTranscription
        ? new[] { (Id: ModuleId.Transcription, Source: args[2]) }
        : [(Id: ModuleId.Separation, Source: args[1]), (Id: ModuleId.Transcription, Source: args[2])];
    foreach (var item in modules)
    {
        var module = Path.GetFullPath(item.Source);
        Console.WriteLine($"Staging existing {ModulePaths.Name(item.Id)} generation for published-EXE verification...");
        ModulePaths.ValidateTree(module, cancellation.Token);
        var pointer = await File.ReadAllTextAsync(Path.Combine(module, "active.json"));
        using var active = JsonDocument.Parse(pointer);
        var generation = active.RootElement.GetProperty("Generation").GetString()!;
        if (!Guid.TryParseExact(generation, "N", out _)) throw new InvalidDataException("Unsafe generation pointer.");
        var payload = ModulePaths.Child(module, "versions/" + generation);
        var destination = paths.ModuleDirectory(item.Id);
        if (Directory.Exists(destination)) throw new IOException("A personal module appeared during preflight.");
        staged.Add(item.Id);
        Directory.CreateDirectory(destination);
        var target = ModulePaths.Child(destination, "versions/" + generation);
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories))
        {
            cancellation.Token.ThrowIfCancellationRequested();
            var copied = ModulePaths.Child(target, Path.GetRelativePath(payload, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copied)!);
            File.Copy(file, copied);
        }
        await File.WriteAllTextAsync(Path.Combine(destination, "active.json"), pointer);
        await Run(published, ["modules", "verify", ModulePaths.Name(item.Id)]);
        measurements.Add(new { Check = "copied-generation-verified-by-published-exe", Module = ModulePaths.Name(item.Id), Generation = generation, Source = module,
            ManifestSha256 = await Hash(Path.Combine(payload, "module.json")) });
    }

    foreach (var clip in clips)
    {
        foreach (var action in fullTranscription ? new[] { "transcribe" } : ["transcribe", "separate-transcribe"])
        {
            var prefix = clip.Name + "-" + action;
            var arguments = new List<string> { "process", clip.Path, "--action", action, "--audio-track", fullTranscription ? streamIndex : "1", "--language", "fr",
                "--output-dir", root, "--prefix", prefix, "--bit-depth", "32" };
            if (action == "separate-transcribe") arguments.AddRange(["--exports", "original,vocals,background,transcript,subtitles"]);
            await Run(published, arguments.ToArray());
            var text = await File.ReadAllTextAsync(Path.Combine(root, prefix + "-transcript.txt"));
            var subtitles = await File.ReadAllTextAsync(Path.Combine(root, prefix + "-subtitles.srt"));
            var cues = TranscriptFormat.Parse(subtitles, fullTranscription ? duration : 30);
            if (cues.Count == 0) throw new InvalidDataException($"No speech recognized in {prefix}; human review required.");
            if (!text.Replace("\r\n", "\n").Trim().Equals(string.Join("\n", cues.Select(cue => cue.Text)), StringComparison.Ordinal))
                throw new InvalidDataException("TXT does not match SRT segments.");
            measurements.Add(new { Check = "transcript-structure", Clip = clip.Name, Action = action, Cues = cues,
                TxtSha256 = await Hash(Path.Combine(root, prefix + "-transcript.txt")), SrtSha256 = await Hash(Path.Combine(root, prefix + "-subtitles.srt")) });
            foreach (var suffix in action == "transcribe"
                ? new[] { "-transcript.txt", "-subtitles.srt" }
                : new[] { "-original.wav", "-vocals.wav", "-background.wav", "-transcript.txt", "-subtitles.srt" })
            {
                var file = Path.Combine(root, prefix + suffix);
                exports.Add((file, await Hash(file)));
            }
            if (action != "separate-transcribe") continue;
            var original = await Decode(prefix + "-original.wav");
            var vocals = await Decode(prefix + "-vocals.wav");
            var background = await Decode(prefix + "-background.wav");
            var sum = vocals.Zip(background, (a, b) => a + b).ToArray();
            var times = new[] { ActiveWindow(original, 0.25, 5), ActiveWindow(original, 12, 18), ActiveWindow(original, 25, 29.75) };
            var lags = times.Select(time => BestLag(original, sum, time)).ToArray();
            measurements.Add(new { Check = "separated-pcm", Clip = clip.Name, Frames = original.Length / 2,
                Rate = 48000, Channels = 2, ReconstructedWindowTimesSeconds = times, ReconstructedLagsSamples = lags,
                ReconstructionErrorRms = Math.Sqrt(sum.Zip(original, (a, b) => (double)(a - b) * (a - b)).Average()) });
            if (lags.Any(lag => Math.Abs(lag) > 1)) throw new InvalidDataException("Reconstructed audio alignment exceeds one sample.");
            SaveReport();
        }
    }
    if (await Hash(source) != sourceHash || await Hash(published) != executableHash) throw new IOException("Validation changed an input.");
}
catch (Exception ex)
{
    error = ex.ToString();
    Console.Error.WriteLine(error);
}
finally
{
    foreach (var id in staged)
    {
        try
        {
            await Run(published, ["modules", "uninstall", ModulePaths.Name(id)], cleanupOperation: true);
            if (Directory.Exists(paths.ModuleDirectory(id))) throw new IOException("Module directory remains after uninstall.");
            await Run(published, ["modules", "verify", ModulePaths.Name(id)], 2, cleanupOperation: true);
            foreach (var other in staged.Where(other => other != id && Directory.Exists(paths.ModuleDirectory(other))))
                await Run(published, ["modules", "verify", ModulePaths.Name(other)], cleanupOperation: true);
            cleanup.Add(new { Module = ModulePaths.Name(id), Passed = true });
        }
        catch (Exception ex)
        {
            error ??= ex.ToString();
            cleanup.Add(new { Module = ModulePaths.Name(id), Passed = false, Error = ex.ToString() });
        }
    }
    try
    {
        foreach (var output in exports)
            if (await Hash(output.Path) != output.Sha256) throw new IOException("Uninstall changed a finalized export.");
        if (await Hash(source) != sourceHash || await Hash(published) != executableHash) throw new IOException("Cleanup changed an input.");
    }
    catch (Exception ex) { error ??= ex.ToString(); }
    SaveReport();
}
return error is null ? 0 : 3;

void SaveReport() => File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new
{
    Executable = executable, ExecutableSha256 = executableHash, Source = source, SourceSha256 = sourceHash,
    Mode = fullTranscription ? "FullTranscription" : "Excerpts",
    OS = Environment.OSVersion.ToString(), Runtime = Environment.Version.ToString(),
    AdministratorToken = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator),
    IsCleanWindows = false, HumanReviewCompleted = false, Provisioning = "Copied existing pinned active generations; published CLI verifies them. No installation/download exercised.",
    Passed = error is null && cleanup.Count == staged.Count && cleanup.Count == (fullTranscription ? 1 : 2), Error = error, Commands = commands, Measurements = measurements,
    Exports = exports.Select(output => new { output.Path, output.Sha256 }), Cleanup = cleanup
}, jsonOptions));

async Task<string> Run(string program, string[] arguments, int expected = 0, bool cleanupOperation = false)
{
    var token = cleanupOperation ? CancellationToken.None : cancellation.Token;
    token.ThrowIfCancellationRequested();
    var info = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = standalone };
    foreach (var argument in arguments) info.ArgumentList.Add(argument);
    info.Environment["PATH"] = Environment.GetFolderPath(Environment.SpecialFolder.System);
    foreach (var key in new[] { "OD_CONVERTER_FFMPEG", "PYTHONHOME", "PYTHONPATH", "DOTNET_ROOT", "DOTNET_ROOT_X64" }) info.Environment.Remove(key);
    info.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.Combine(root, "bundle");
    var temp = Path.Combine(root, "temp");
    Directory.CreateDirectory(temp);
    info.Environment["TEMP"] = info.Environment["TMP"] = temp;
    using var process = Process.Start(info) ?? throw new IOException("Cannot start validation process.");
    var watch = Stopwatch.StartNew();
    var output = process.StandardOutput.ReadToEndAsync();
    var errors = process.StandardError.ReadToEndAsync();
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
    timeout.CancelAfter(TimeSpan.FromMinutes(20));
    try { await process.WaitForExitAsync(timeout.Token); }
    catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
    var stdout = await output;
    await File.AppendAllTextAsync(Path.Combine(root, "commands.log"), $"{program} {JsonSerializer.Serialize(arguments)}\nExit {process.ExitCode}\n{stdout}\n{await errors}\n");
    commands.Add(new { Program = program, Arguments = arguments, process.ExitCode, Expected = expected, Seconds = watch.Elapsed.TotalSeconds });
    SaveReport();
    Console.WriteLine($"Exit {process.ExitCode} ({watch.Elapsed.TotalSeconds:F1}s): {string.Join(" ", arguments)}");
    if (process.ExitCode != expected) throw new IOException($"Expected exit {expected}, received {process.ExitCode}. See commands.log.");
    return stdout;
}

async Task<float[]> Decode(string name)
{
    var file = Path.Combine(root, name);
    using var metadata = JsonDocument.Parse(await Run(ffprobe, ["-v", "error", "-select_streams", "a", "-show_streams", "-of", "json", file]));
    var streams = metadata.RootElement.GetProperty("streams");
    if (streams.GetArrayLength() != 1 || streams[0].GetProperty("sample_rate").GetString() != "48000" ||
        streams[0].GetProperty("channels").GetInt32() != 2 || streams[0].GetProperty("codec_name").GetString() != "pcm_s32le")
        throw new InvalidDataException("Incorrect PCM export format.");
    var raw = file + ".f32";
    try
    {
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", file, "-c:a", "pcm_f32le", "-f", "f32le", raw]);
        var bytes = await File.ReadAllBytesAsync(raw);
        if (bytes.Length != 30 * 48000 * 2 * 4) throw new InvalidDataException("Incorrect PCM frame count.");
        var samples = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        if (samples.Any(value => !float.IsFinite(value))) throw new InvalidDataException("Non-finite PCM samples.");
        return samples;
    }
    finally { if (File.Exists(raw)) File.Delete(raw); }
}

static async Task<string> Hash(string file)
{
    await using var stream = File.OpenRead(file);
    return Convert.ToHexString(await SHA256.HashDataAsync(stream));
}

static int BestLag(float[] reference, float[] actual, double time)
{
    var start = (int)Math.Round(time * 48000) - 64;
    var bestScore = double.NegativeInfinity;
    var bestLag = 0;
    for (var lag = -32; lag <= 32; lag++)
    {
        double cross = 0, energy = 0;
        for (var frame = start; frame < start + 640; frame++)
        {
            cross += reference[frame * 2] * actual[(frame + lag) * 2];
            energy += (double)actual[(frame + lag) * 2] * actual[(frame + lag) * 2];
        }
        var score = cross / Math.Sqrt(energy + 1e-20);
        if (score > bestScore) { bestScore = score; bestLag = lag; }
    }
    if (double.IsNegativeInfinity(bestScore) || EnergyAtReference(reference, start) < 1e-12)
        throw new InvalidDataException("Silent correlation window; alignment cannot be inferred.");
    return bestLag;

    static double EnergyAtReference(float[] samples, int start) => Enumerable.Range(start, 640).Sum(frame => (double)samples[frame * 2] * samples[frame * 2]);
}

static double ActiveWindow(float[] samples, double from, double to)
{
    var windows = new List<(double Time, double Energy)>();
    for (var time = from; time <= to; time += 0.25)
    {
        var start = (int)Math.Round(time * 48000) - 64;
        var energy = Enumerable.Range(start, 640).Sum(frame => (double)samples[frame * 2] * samples[frame * 2]);
        windows.Add((time, energy));
    }
    return windows.MaxBy(window => window.Energy).Time;
}
