using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 3 || !OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Usage: ReleaseValidation <published-exe> <fixture-ffmpeg> <new-output-directory>");
    return 1;
}

var executable = Path.GetFullPath(args[0]);
var ffmpeg = Path.GetFullPath(args[1]);
var root = Path.GetFullPath(args[2]);
if (!File.Exists(executable) || !File.Exists(ffmpeg)) throw new FileNotFoundException("Validation inputs are missing.");
if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
    throw new ArgumentException("Validation requires a new, empty output directory.");
Directory.CreateDirectory(root);
var standalone = Path.Combine(root, "standalone été & [test]");
Directory.CreateDirectory(standalone);
var published = Path.Combine(standalone, "odconv.exe");
File.Copy(executable, published);
var extraction = Path.Combine(root, "bundle");
var results = new List<object>();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, value) => { value.Cancel = true; cancellation.Cancel(); };
var ct = cancellation.Token;
var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(published)));
try
{
    foreach (var command in new[] { new[] { "--help" }, ["--version"], ["formats"], ["process", "--help"], ["modules", "--help"] })
        await Run(published, command);

    var marker = Path.Combine(root, "markers.f32");
    using (var file = File.Create(marker))
    using (var writer = new BinaryWriter(file))
    {
        file.SetLength(3 * 48000 * sizeof(float));
        foreach (var time in new[] { 0.25, 1.5, 2.75 })
        {
            file.Position = (long)(time * 48000) * sizeof(float);
            writer.Write(0.75f);
        }
    }
    var wave = Path.Combine(root, "markers.wav");
    await Generate("-f", "f32le", "-ar", "48000", "-ac", "1", "-i", marker, "-c:a", "pcm_f32le", wave);
    foreach (var item in new[] { ("mp4", "mpeg4", "alac", 1), ("mkv", "ffv1", "pcm_f32le", 48),
        ("mov", "mpeg4", "pcm_s16le", 1), ("avi", "mpeg4", "pcm_s16le", 1), ("webm", "libvpx", "libopus", 480) })
    {
        var source = Path.Combine(root, "source." + item.Item1);
        await Generate("-f", "lavfi", "-i", "color=s=32x32:r=25:d=5", "-itsoffset", item.Item1 == "avi" ? "0" : "0.5", "-i", wave,
            "-map", "0:v", "-map", "1:a", "-c:v", item.Item2, "-c:a", item.Item3, source);
        await Extract(source, item.Item1, 48000, 5, item.Item1 == "avi" ? [0.25, 1.5, 2.75] : [0.75, 2, 3.25], item.Item4);
    }

    const int duration = 7200;
    var times = new[] { 0.25, 1800.0, 3600.0, 5400.0, 7199.75 };
    var longRaw = Path.Combine(root, "two-hours.f32");
    using (var file = File.Create(longRaw))
    using (var writer = new BinaryWriter(file))
    {
        file.SetLength((long)duration * 8000 * sizeof(float));
        foreach (var time in times)
        {
            file.Position = (long)(time * 8000) * sizeof(float);
            writer.Write(0.75f);
        }
    }
    var longWave = Path.Combine(root, "two-hours.wav");
    await Generate("-f", "f32le", "-ar", "8000", "-ac", "1", "-i", longRaw, "-c:a", "pcm_f32le", longWave);
    await Extract(longWave, "two-hours", 22050, duration, times, 1);

    var converted = Path.Combine(root, "classic.mp3");
    var dryRun = await Run(published, ["convert", wave, "--to", "mp3", "-o", converted, "--dry-run"]);
    var bundledTool = Directory.GetFiles(extraction, "ffmpeg.exe", SearchOption.AllDirectories).Single();
    if (!dryRun.Contains(bundledTool, StringComparison.OrdinalIgnoreCase))
        throw new IOException("The published executable did not select its embedded FFmpeg.");
    results.Add(new { Check = "embedded-ffmpeg-selected", Path = bundledTool });
    await Run(published, ["convert", wave, "--to", "mp3", "-o", converted]);
    await Run(published, ["probe", converted]);
    var before = SHA256.HashData(File.ReadAllBytes(converted));
    await Run(published, ["convert", wave, "--to", "mp3", "-o", converted], 1);
    if (!before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(converted)))) throw new IOException("Collision changed an existing file.");
    results.Add(new { Check = "classic-conversion-and-collision", Passed = true });
    return 0;
}
catch (Exception ex)
{
    results.Add(new { Check = "failure", Error = ex.ToString() });
    Console.Error.WriteLine(ex.Message);
    return 3;
}
finally
{
    await File.WriteAllTextAsync(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new
    {
        Executable = executable, Sha256 = hash, OS = Environment.OSVersion.ToString(),
        Runtime = Environment.Version.ToString(), IsCleanWindows = false,
        Isolation = "EXE copied alone; Windows-only PATH; private bundle/temp; no FFmpeg override. Host installations remain present.",
        Results = results
    }, new JsonSerializerOptions { WriteIndented = true }));
}

async Task Extract(string source, string prefix, int rate, int seconds, double[] times, int tolerance)
{
    await Run(published, ["process", source, "--action", "extract", "--audio-track", source.EndsWith(".wav") ? "0" : "1",
        "--output-dir", root, "--prefix", prefix, "--sample-rate", rate.ToString(), "--bit-depth", "32"]);
    var output = Path.Combine(root, prefix + "-original.wav");
    var decoded = Path.Combine(root, prefix + "-check.f32");
    await Generate("-i", output, "-f", "f32le", "-c:a", "pcm_f32le", decoded);
    using var reader = new BinaryReader(File.OpenRead(decoded));
    var frames = reader.BaseStream.Length / sizeof(float);
    if (reader.BaseStream.Length != (long)rate * seconds * sizeof(float)) throw new IOException($"Incorrect PCM length: {prefix}: {frames}.");
    var errors = new List<long>();
    foreach (var time in times)
    {
        var target = (long)Math.Round(time * rate);
        var start = Math.Max(0, target - rate / 20);
        var end = Math.Min(frames, target + rate / 20);
        reader.BaseStream.Position = start * sizeof(float);
        float amplitude = 0;
        long peak = start;
        for (var frame = start; frame < end; frame++)
        {
            var value = Math.Abs(reader.ReadSingle());
            if (value > amplitude) { amplitude = value; peak = frame; }
        }
        if (amplitude < 0.01 || Math.Abs(peak - target) > tolerance)
            throw new IOException($"Marker mismatch: {prefix} at {time}s: {peak - target} samples, amplitude {amplitude}.");
        errors.Add(peak - target);
    }
    results.Add(new { Check = prefix, Frames = frames, Rate = rate, MarkerErrors = errors, ToleranceSamples = tolerance });
    Console.WriteLine($"PASS {prefix}: {frames} frames; marker errors {string.Join(", ", errors)}");
}

Task Generate(params string[] arguments) => Run(ffmpeg, new[] { "-nostdin", "-v", "error", "-y" }.Concat(arguments).ToArray());

async Task<string> Run(string program, string[] arguments, int expected = 0)
{
    ct.ThrowIfCancellationRequested();
    var info = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true,
        RedirectStandardError = true, WorkingDirectory = standalone, CreateNoWindow = true };
    foreach (var argument in arguments) info.ArgumentList.Add(argument);
    info.Environment["PATH"] = Environment.GetFolderPath(Environment.SpecialFolder.System);
    foreach (var key in new[] { "OD_CONVERTER_FFMPEG", "PYTHONHOME", "PYTHONPATH", "DOTNET_ROOT", "DOTNET_ROOT_X64" }) info.Environment.Remove(key);
    info.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = extraction;
    var temp = Path.Combine(root, "temp");
    Directory.CreateDirectory(temp);
    info.Environment["TEMP"] = info.Environment["TMP"] = temp;
    using var process = Process.Start(info) ?? throw new IOException("Cannot start validation process.");
    var stdout = process.StandardOutput.ReadToEndAsync(ct);
    var stderr = process.StandardError.ReadToEndAsync(ct);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromMinutes(15));
    try { await process.WaitForExitAsync(timeout.Token); }
    catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
    var log = string.Join(" ", arguments) + Environment.NewLine + await stdout + await stderr;
    await File.AppendAllTextAsync(Path.Combine(root, "commands.log"), $"Exit {process.ExitCode}: {log}{Environment.NewLine}", ct);
    if (process.ExitCode != expected) throw new IOException($"Expected exit {expected}, received {process.ExitCode}: {log}");
    return log;
}
